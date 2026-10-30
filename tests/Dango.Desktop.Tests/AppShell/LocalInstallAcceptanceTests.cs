using System.Text.Json;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using System.Reflection;
using System.Windows;
using System.Diagnostics;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class LocalInstallAcceptanceTests
{
    [TestMethod]
    public void ExplicitOwnedAcceptanceDirectoryUsesFictionalSettingsOutsideThePackage()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, ".dango-local-install-acceptance.json"),
            """{"application":"Dango","acceptanceFormat":1,"fictional":true}""");
        Assert.AreEqual(Path.Combine(root, "settings.json"), App.ResolveLocalInstallAcceptanceSettingsFile(
            ["--local-install-acceptance-settings", root]));
        Assert.IsNull(App.ResolveLocalInstallAcceptanceSettingsFile([]));
        Assert.AreNotEqual(App.ResolveSingleInstanceName([]),
            App.ResolveSingleInstanceName(["--local-install-acceptance-settings", root]),
            "Owned acceptance must never activate the daily product instance.");
        Assert.AreEqual(App.ResolveSingleInstanceName(["--data-root", root]),
            App.ResolveSingleInstanceName(["--local-install-acceptance-settings", root]),
            "The same resolved data directory has one instance identity regardless of its entry option.");
    }

    [TestMethod]
    public void AcceptanceRequiresOwnedRootAndCannotMixReviewModesOrDailySettings()
    {
        var root = NewRoot();
        Assert.ThrowsExactly<FileNotFoundException>(() => App.ResolveLocalInstallAcceptanceSettingsFile(
            ["--local-install-acceptance-settings", root]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveLocalInstallAcceptanceSettingsFile(
            ["--local-install-acceptance-settings", root, "--eye-hat-review"]));
        var daily = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango");
        Assert.ThrowsExactly<InvalidDataException>(() => App.ResolveLocalInstallAcceptanceSettingsFile(
            ["--local-install-acceptance-settings", daily]));
        Assert.ThrowsExactly<InvalidDataException>(() => App.ResolveLocalInstallAcceptanceSettingsFile(
            ["--local-install-acceptance-settings", AppContext.BaseDirectory]));
    }

    [TestMethod]
    [DataRow("settings.json")]
    [DataRow("settings.window-preset.json")]
    [DataRow("appearance.json")]
    public void AcceptanceRejectsSettingsReparsePointsBeforeReadingThem(string fileName)
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, ".dango-local-install-acceptance.json"),
            """{"application":"Dango","acceptanceFormat":1,"fictional":true}""");
        var fictionalTarget = NewRoot();
        // A directory junction needs no symbolic-link privilege and exercises the same
        // reparse attribute as a settings-file symlink without touching real settings.
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(Path.Combine(root, fileName));
        start.ArgumentList.Add(fictionalTarget);
        using var process = Process.Start(start)!;
        Assert.IsTrue(process.WaitForExit(10000), "The owned junction fixture did not finish creating.");
        Assert.AreEqual(0, process.ExitCode, "The owned junction fixture could not be created.");
        Assert.IsTrue((File.GetAttributes(Path.Combine(root, fileName)) & FileAttributes.ReparsePoint) != 0);
        Assert.ThrowsExactly<InvalidDataException>(() => App.ResolveLocalInstallAcceptanceSettingsFile(
            ["--local-install-acceptance-settings", root]));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(fictionalTarget).Length);
    }

    [TestMethod]
    public void AcceptanceRejectsAppearanceDirectoryBeforeAnySettingsLoad()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, ".dango-local-install-acceptance.json"),
            """{"application":"Dango","acceptanceFormat":1,"fictional":true}""");
        Directory.CreateDirectory(Path.Combine(root, "appearance.json"));
        Assert.ThrowsExactly<InvalidDataException>(() => App.ResolveLocalInstallAcceptanceSettingsFile(
            ["--local-install-acceptance-settings", root]));
    }

    [STATestMethod]
    public void DamagedPresetExposesTheRecoveryActionInProductSettings()
    {
        var path = Path.Combine(NewRoot(), "settings.json");
        new ControlCenterWindowSettingsStore().Save(path, new(1010, 690));
        File.WriteAllText(ControlCenterWindowSettingsStore.GetPresetPath(path), "fictional broken preset");
        using var host = new DangoApplicationHost(settingsFile: path, roomModulePath: path + ".missing.dll");
        var window = (ControlCenterWindow)typeof(DangoApplicationHost)
            .GetMethod("GetOrCreateControlCenter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        window.ShowOverview();
        window.ProductHome.ShowSettingsPage();
        Assert.AreEqual(Visibility.Visible, ((FrameworkElement)window.ProductHome.FindName("RecoverSettingsButton")).Visibility);
        Assert.AreEqual("fictional broken preset", File.ReadAllText(ControlCenterWindowSettingsStore.GetPresetPath(path)));
    }

    private static string NewRoot()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "local-acceptance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
