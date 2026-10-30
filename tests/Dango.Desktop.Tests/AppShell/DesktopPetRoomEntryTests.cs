using System.Reflection;
using Dango.Desktop.AppShell;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DesktopPetRoomEntryTests
{
    [TestMethod]
    [DataRow("--unified-live-home-review")]
    [DataRow("--porch-scene-review")]
    [DataRow("--local-room-review")]
    [DataRow("--face-review")]
    [DataRow("--accessory-review")]
    [DataRow("--desktop-pet-room-review")]
    public void RetiredProductModesAreRejectedBeforeHostOrInstanceSelection(string argument)
    {
        Assert.ThrowsExactly<ArgumentException>(() => App.ValidateStartupArguments([argument]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveSingleInstanceName([argument]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveProductSettingsFile([argument]));
    }

    [STATestMethod]
    public void RetainedDiagnosticConstructorRejectsConflictingModesBeforeCreatingAHost()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new DangoApplicationHost(isolatedPhysicsReview: true, isolatedEyeHatReview: true));
    }

    [STATestMethod]
    public void ExplicitOwnedSettingsUseTheDefaultProductAndNeverSaveDailySettings()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "product-entry", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".dango-local-install-acceptance.json"),
            """{"application":"Dango","acceptanceFormat":1,"fictional":true}""");
        var path = App.ResolveLocalInstallAcceptanceSettingsFile(["--local-install-acceptance-settings", root]);
        using var host = new DangoApplicationHost(settingsFile: path);
        Assert.AreEqual(Path.Combine(root, "settings.json"), DesktopPetRoomFixture.Field<string>(host, "settingsPath"));
        Assert.AreNotEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango", "settings.json"), path);
        var runtime = DesktopPetRoomFixture.Field<Dango.Desktop.Rendering.Visuals.StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom);
        Assert.IsFalse(runtime.IsRunning);
        Assert.IsNull(runtime.FrameDriver);
        Assert.AreEqual(0, runtime.EntityIds.Count, "Default startup must defer the independent room world until real entry.");
    }
}
