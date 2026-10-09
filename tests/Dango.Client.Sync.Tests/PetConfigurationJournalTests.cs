using System.Text;
using System.Text.Json;
using Dango.Client.Sync.Pets;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Client.Sync.Tests;

[TestClass]
public sealed class PetConfigurationJournalTests
{
    [TestMethod]
    [DataRow(true, PetConfigurationChoice.Local)]
    [DataRow(true, PetConfigurationChoice.Cloud)]
    [DataRow(false, PetConfigurationChoice.Local)]
    [DataRow(false, PetConfigurationChoice.Cloud)]
    public async Task OriginalsAndChoiceSurviveRestartWithoutReceiptIncludingMissingAndLegacyBytes(bool exists, PetConfigurationChoice choice)
    {
        var directory = NewDirectory();
        var raw = exists ? Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"fictionalOriginal\":true}\r\n") : [];
        var originals = Originals(exists, raw, choice);
        var journal = new PetConfigurationFileJournal(directory);
        var id = await journal.SaveOriginalsAsync(originals, CancellationToken.None);
        var originalPath = Path.Combine(directory, id.ToString("N") + ".originals.json");
        var persistedBefore = await File.ReadAllBytesAsync(originalPath);
        var restarted = new PetConfigurationFileJournal(directory);
        var loaded = await restarted.LoadOriginalsAsync(id, CancellationToken.None);
        Assert.AreEqual(exists, loaded.Local.Exists);
        CollectionAssert.AreEqual(raw, loaded.Local.OriginalBytes.ToArray());
        Assert.AreEqual(choice, loaded.Choice);
        Assert.AreEqual(originals.Origin, loaded.Origin);
        Assert.AreEqual(originals.Cloud.UserId, loaded.Cloud.UserId);
        Assert.AreEqual(originals.Cloud.PetId, loaded.Cloud.PetId);
        Assert.AreEqual(originals.Cloud.ConfigurationVersion, loaded.Cloud.ConfigurationVersion);
        Assert.IsTrue(AppearanceContract.Equivalent(originals.Local.NormalizedConfiguration, loaded.Local.NormalizedConfiguration));
        Assert.IsTrue(AppearanceContract.Equivalent(originals.Cloud.Configuration, loaded.Cloud.Configuration));
        Assert.IsNull(await restarted.LoadReceiptAsync(id));
        CollectionAssert.AreEqual(persistedBefore, await File.ReadAllBytesAsync(originalPath));
        var text = Encoding.UTF8.GetString(persistedBefore);
        foreach (var field in new[] { "cookie", "password", "csrf", "proof", "connectionString" })
            Assert.IsFalse(text.Contains('"' + field + '"', StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SeparateReceiptIsBoundToChoiceAndCannotOverwriteOriginalsOrAnotherReceipt()
    {
        var directory = NewDirectory();
        var journal = new PetConfigurationFileJournal(directory);
        var originals = Originals(true, Encoding.UTF8.GetBytes("fictional original"), PetConfigurationChoice.Local);
        var id = await journal.SaveOriginalsAsync(originals, CancellationToken.None);
        var originalPath = Path.Combine(directory, id.ToString("N") + ".originals.json");
        var before = await File.ReadAllBytesAsync(originalPath);
        var wrongChoice = new PetConfigurationReceipt(PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, DateTimeOffset.UtcNow, 1, null);
        await FaultAsync("archive_invalid", () => journal.WriteReceiptAsync(id, wrongChoice, CancellationToken.None));
        var receipt = new PetConfigurationReceipt(PetConfigurationChoice.Local, PetChoiceOutcome.TargetSatisfied, DateTimeOffset.UtcNow, 2, null);
        await journal.WriteReceiptAsync(id, receipt, CancellationToken.None);
        var restarted = new PetConfigurationFileJournal(directory);
        Assert.AreEqual(receipt, await restarted.LoadReceiptAsync(id));
        await FaultAsync("archive_write_failed", () => restarted.WriteReceiptAsync(id, receipt with { Outcome = PetChoiceOutcome.Conflict }, CancellationToken.None));
        Assert.AreEqual(receipt, await restarted.LoadReceiptAsync(id));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(originalPath));
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.temporary").Length);
    }

    [TestMethod]
    public async Task MalformedUnknownDuplicateOversizedAndInvalidConfigurationArchivesStayUntouched()
    {
        var directory = NewDirectory();
        var journal = new PetConfigurationFileJournal(directory);
        var seedId = await journal.SaveOriginalsAsync(Originals(true, [], PetConfigurationChoice.Local), CancellationToken.None);
        var valid = await File.ReadAllTextAsync(Path.Combine(directory, seedId.ToString("N") + ".originals.json"));
        foreach (var json in new[]
        {
            "{", valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":99", StringComparison.Ordinal),
            valid[..^1] + ",\"schemaVersion\":1}", valid[..^1] + ",\"unknown\":true}",
            valid.Replace("\"choice\":0", "\"choice\":99", StringComparison.Ordinal),
            valid.Replace("\"appearanceSchemaVersion\":3", "\"appearanceSchemaVersion\":99", StringComparison.Ordinal),
            new string(' ', 131073) + valid
        })
        {
            var id = Guid.NewGuid();
            var path = Path.Combine(directory, id.ToString("N") + ".originals.json");
            await File.WriteAllTextAsync(path, json);
            await FaultAsync("archive_invalid", () => journal.LoadOriginalsAsync(id, CancellationToken.None));
            Assert.IsTrue(string.Equals(json, await File.ReadAllTextAsync(path), StringComparison.Ordinal));
            Assert.IsFalse(File.Exists(Path.Combine(directory, id.ToString("N") + ".receipt.json")));
        }
    }

    [TestMethod]
    public async Task ArchiveDirectoryWriteFailureAndCancellationDoNotPublishPartialOriginals()
    {
        var collision = NewDirectory();
        var preexisting = Encoding.UTF8.GetBytes("fictional protected obstruction");
        await File.WriteAllBytesAsync(collision, preexisting);
        var obstructed = new PetConfigurationFileJournal(collision);
        await FaultAsync("archive_write_failed", () => obstructed.SaveOriginalsAsync(Originals(true, [], PetConfigurationChoice.Cloud), CancellationToken.None));
        CollectionAssert.AreEqual(preexisting, await File.ReadAllBytesAsync(collision));
        var cancelledDirectory = NewDirectory();
        var cancelled = new PetConfigurationFileJournal(cancelledDirectory);
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => cancelled.SaveOriginalsAsync(Originals(true, [], PetConfigurationChoice.Cloud), stopping.Token));
        Assert.IsFalse(Directory.Exists(cancelledDirectory));
    }

    [TestMethod]
    public async Task OriginalBudgetIsInclusiveAndMissingBytesOrChoiceAreRejected()
    {
        var raw = new byte[LocalPetConfigurationSnapshot.OriginalMaximumBytes];
        var originals = Originals(true, raw, PetConfigurationChoice.Local);
        var journal = new PetConfigurationFileJournal(NewDirectory());
        var id = await journal.SaveOriginalsAsync(originals, CancellationToken.None);
        Assert.AreEqual(raw.Length, (await journal.LoadOriginalsAsync(id, CancellationToken.None)).Local.OriginalBytes.Length);
        Assert.ThrowsExactly<PetConfigurationChoiceException>(() => Originals(true, new byte[raw.Length + 1], PetConfigurationChoice.Local));
        Assert.ThrowsExactly<PetConfigurationChoiceException>(() => Originals(false, [1], PetConfigurationChoice.Local));
        await FaultAsync("archive_invalid", () => journal.SaveOriginalsAsync(originals with { Choice = null }, CancellationToken.None));
    }

    [TestMethod]
    public async Task ReceiptCannotPersistUntrustedErrorTextOrAuthenticationMetadata()
    {
        var directory = NewDirectory();
        var journal = new PetConfigurationFileJournal(directory);
        var id = await journal.SaveOriginalsAsync(Originals(true, [], PetConfigurationChoice.Local), CancellationToken.None);
        var receipt = new PetConfigurationReceipt(PetConfigurationChoice.Local, PetChoiceOutcome.Failed,
            DateTimeOffset.UtcNow, null, "untrusted_external_marker");
        await FaultAsync("archive_invalid", () => journal.WriteReceiptAsync(id, receipt, CancellationToken.None));
        Assert.IsNull(await journal.LoadReceiptAsync(id));
    }

    private static PetConfigurationOriginals Originals(bool exists, byte[] raw, PetConfigurationChoice choice) =>
        new(new Uri("https://formal.example.invalid/"), new(exists, raw, PetConfigurationChoiceTests.Configuration()),
            new PetConfigurationSnapshot(Guid.NewGuid(), Guid.NewGuid(), 7, PetConfigurationChoiceTests.Configuration("#000000")), DateTimeOffset.UtcNow, choice);

    private static async Task FaultAsync(string code, Func<Task> action)
    {
        var error = await Assert.ThrowsExactlyAsync<PetConfigurationChoiceException>(action);
        Assert.AreEqual(code, error.Code);
    }

    private static string NewDirectory()
    {
        for (DirectoryInfo? parent = new(AppContext.BaseDirectory); parent is not null; parent = parent.Parent)
            if (File.Exists(Path.Combine(parent.FullName, "AGENTS.md")))
            {
                var root = Path.Combine(parent.FullName, "artifacts", "tests", "pet-configuration-journals");
                Directory.CreateDirectory(root);
                return Path.Combine(root, Guid.NewGuid().ToString("N"));
            }
        throw new InvalidOperationException("The isolated test artifact boundary is unavailable.");
    }
}
