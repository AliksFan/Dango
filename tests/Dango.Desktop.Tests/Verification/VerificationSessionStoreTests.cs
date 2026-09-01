using System.Text.Json;
using Dango.Desktop.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Verification;

[TestClass]
public sealed class VerificationSessionStoreTests
{
    [TestMethod]
    public void SessionRoundTripsWithoutAddingPrivateDesktopFields()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var definition = VerificationDefinition.CreateP1002Overlay();
            var store = new VerificationSessionStore(definition);
            var path = Path.Combine(directory, "session.json");
            var session = VerificationSession.Create(
                definition,
                new VerificationEnvironmentSnapshot("1.0.0", "test", "Overlay", "26100", "1 display / 100%"),
                DateTimeOffset.UtcNow);
            session.Results[0].Outcome = ManualVerificationOutcome.Passed;
            session.Results[0].Notes = "使用虚构环境完成";

            store.Save(path, session);
            var restored = store.Load(path);
            var json = File.ReadAllText(path);

            Assert.AreEqual(ManualVerificationOutcome.Passed, restored.Results[0].Outcome);
            Assert.IsFalse(json.Contains("windowTitle", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(json.Contains("userName", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(json.Contains("desktopFiles", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void UnknownJsonFieldsAreRejectedInsteadOfSilentlyPersisted()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var definition = VerificationDefinition.CreateP1002Overlay();
            var store = new VerificationSessionStore(definition);
            var path = Path.Combine(directory, "session.json");
            var session = VerificationSession.Create(
                definition,
                new VerificationEnvironmentSnapshot("1.0.0", "test", "Overlay", "26100", "1 display / 100%"),
                DateTimeOffset.UtcNow);
            store.Save(path, session);
            var json = File.ReadAllText(path).Replace(
                "\"schemaVersion\": 1,",
                "\"schemaVersion\": 1,\n  \"windowTitle\": \"must-not-load\",",
                StringComparison.Ordinal);
            File.WriteAllText(path, json);

            Assert.ThrowsExactly<JsonException>(() => store.Load(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void FailedAtomicWriteDoesNotReplaceExistingDraft()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var definition = VerificationDefinition.CreateP1002Overlay();
            var path = Path.Combine(directory, "session.json");
            File.WriteAllText(path, "original");
            var store = new VerificationSessionStore(definition, new FailingAtomicTextWriter());
            var session = VerificationSession.Create(
                definition,
                new VerificationEnvironmentSnapshot("1.0.0", "test", "Overlay", "26100", "1 display / 100%"),
                DateTimeOffset.UtcNow);

            Assert.ThrowsExactly<IOException>(() => store.Save(path, session));
            Assert.AreEqual("original", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Dango.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FailingAtomicTextWriter : IAtomicTextWriter
    {
        public void Write(string path, string content) => throw new IOException("simulated write failure");
    }
}
