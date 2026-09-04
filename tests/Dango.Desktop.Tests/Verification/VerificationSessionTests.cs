using Dango.Desktop.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Verification;

[TestClass]
public sealed class VerificationSessionTests
{
    [TestMethod]
    public void P1003DefinitionUsesStableUniqueItemIds()
    {
        var definition = VerificationDefinition.CreateP1003WorkerW();

        Assert.AreEqual("p1-003-workerw", definition.Id);
        Assert.IsNotEmpty(definition.Items);
        Assert.AreEqual(
            definition.Items.Count,
            definition.Items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(definition.Items.All(item => !string.IsNullOrWhiteSpace(item.Instructions)));
        Assert.IsTrue(definition.Items.All(item => !string.IsNullOrWhiteSpace(item.Expected)));
    }

    [TestMethod]
    public void P1006DefinitionCoversOnlyTheTwoFormalDisplayModes()
    {
        var definition = VerificationDefinition.CreateP1006WindowModes();

        Assert.AreEqual("p1-006-window-modes", definition.Id);
        Assert.IsTrue(definition.Items.Any(item => item.Id == "wallpaper-default"));
        Assert.IsTrue(definition.Items.Any(item => item.Id == "window-standard"));
        Assert.IsFalse(definition.Title.Contains("Overlay", StringComparison.Ordinal));
        Assert.IsFalse(definition.Items.Any(item =>
            item.Title.Contains("Overlay", StringComparison.Ordinal) ||
            item.Instructions.Contains("Overlay", StringComparison.Ordinal) ||
            item.Expected.Contains("Overlay", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void NewSessionStartsWithEveryRequiredItemUnverified()
    {
        var definition = VerificationDefinition.CreateP1003WorkerW();
        var session = VerificationSession.Create(
            definition,
            EnvironmentSnapshot(),
            new DateTimeOffset(2026, 9, 1, 9, 30, 0, TimeSpan.FromHours(8)));

        Assert.AreEqual(definition.Items.Count, session.Results.Count);
        Assert.IsTrue(session.Results.All(result => result.Outcome == ManualVerificationOutcome.Unverified));
        VerificationSessionValidator.Validate(definition, session);
    }

    [TestMethod]
    public void UnableToRunRequiresAReason()
    {
        var definition = VerificationDefinition.CreateP1003WorkerW();
        var session = VerificationSession.Create(definition, EnvironmentSnapshot(), DateTimeOffset.UtcNow);
        session.Results[0].Outcome = ManualVerificationOutcome.UnableToRun;

        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => VerificationSessionValidator.Validate(definition, session));

        StringAssert.Contains(exception.Message, "无法执行");
    }

    [TestMethod]
    public void FailureRequiresMinimumReproductionNotes()
    {
        var definition = VerificationDefinition.CreateP1003WorkerW();
        var session = VerificationSession.Create(definition, EnvironmentSnapshot(), DateTimeOffset.UtcNow);
        session.Results[0].Outcome = ManualVerificationOutcome.Failed;

        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => VerificationSessionValidator.Validate(definition, session));

        StringAssert.Contains(exception.Message, "人工失败");
    }

    [TestMethod]
    public void UnknownOrMissingItemCannotBeAccepted()
    {
        var definition = VerificationDefinition.CreateP1003WorkerW();
        var session = VerificationSession.Create(definition, EnvironmentSnapshot(), DateTimeOffset.UtcNow);
        session.Results.RemoveAt(0);
        session.Results.Add(new VerificationItemResult("unknown-item"));

        Assert.ThrowsExactly<InvalidDataException>(
            () => VerificationSessionValidator.Validate(definition, session));
    }

    private static VerificationEnvironmentSnapshot EnvironmentSnapshot() =>
        new("1.0.0", "test-build", "Wallpaper", "26100", "1 display / 150%");
}
