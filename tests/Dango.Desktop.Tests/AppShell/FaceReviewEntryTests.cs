using System.Diagnostics;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class FaceReviewEntryTests
{
    [TestMethod]
    [DataRow("--face-review")]
    [DataRow("--face-review-settings")]
    public void RetiredFaceArgumentsAreRejectedBeforeSettingsSelection(string argument)
    {
        Assert.ThrowsExactly<ArgumentException>(() => App.ValidateStartupArguments([argument]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveSingleInstanceName([argument]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveLocalInstallAcceptanceSettingsFile([argument, NewRoot()]));
    }

    [TestMethod]
    public void OwnedAcceptanceHasAPathScopedMutexAndRequiresExplicitOwnedSettings()
    {
        var root = NewOwnedRoot();
        var other = NewOwnedRoot();
        var instance = App.ResolveSingleInstanceName(["--local-install-acceptance-settings", root]);
        Assert.AreNotEqual(App.ResolveSingleInstanceName([]), instance);
        Assert.AreNotEqual(App.ResolveSingleInstanceName(["--local-install-acceptance-settings", other]), instance);
        Assert.AreEqual(instance, App.ResolveSingleInstanceName(["--local-install-acceptance-settings", root.ToUpperInvariant() + Path.DirectorySeparatorChar]));
        Assert.IsNull(App.ResolveLocalInstallAcceptanceSettingsFile([]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveLocalInstallAcceptanceSettingsFile(["--local-install-acceptance-settings"]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveLocalInstallAcceptanceSettingsFile(
            ["--local-install-acceptance-settings", root, "--smoke-test"]));
    }

    [TestMethod]
    public void OwnedAcceptanceUsesSettingsBesideAppearanceWithoutReadingOrWritingEitherFile()
    {
        var root = NewOwnedRoot();
        var settings = Path.Combine(root, "settings.json");
        var appearance = Path.Combine(root, "appearance.json");
        File.WriteAllText(settings, "fictional settings sentinel");
        File.WriteAllText(appearance, "fictional appearance sentinel");
        Assert.AreEqual(settings, Resolve(root));
        Assert.AreEqual("fictional settings sentinel", File.ReadAllText(settings));
        Assert.AreEqual("fictional appearance sentinel", File.ReadAllText(appearance));
        Assert.AreEqual(3, Directory.GetFileSystemEntries(root).Length);
    }

    [TestMethod]
    public void AcceptanceRejectsUnownedDailyPackageRelativeAndDriveDirectories()
    {
        Assert.ThrowsExactly<FileNotFoundException>(() => Resolve(NewRoot()));
        var daily = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango");
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(daily));
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(daily + "."));
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(daily + " "));
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(Path.Combine(daily, "fictional-review")));
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(Path.GetDirectoryName(daily)!));
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(AppContext.BaseDirectory));
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(Path.Combine(AppContext.BaseDirectory, "fictional-review")));
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve("fictional-relative-review"));
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(Path.GetPathRoot(TestRepository.Root)!));
    }

    [TestMethod]
    [DataRow("settings.json")]
    [DataRow("settings.window-preset.json")]
    [DataRow("appearance.json")]
    [DataRow(".dango-local-install-acceptance.json")]
    public void AcceptanceRejectsDirectoriesAtEverySettingsOrOwnerPath(string fileName)
    {
        var root = NewOwnedRoot();
        var path = Path.Combine(root, fileName);
        if (File.Exists(path)) File.Delete(path);
        Directory.CreateDirectory(path);
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(root));
    }

    [TestMethod]
    [DataRow("settings.json")]
    [DataRow("settings.window-preset.json")]
    [DataRow("appearance.json")]
    [DataRow(".dango-local-install-acceptance.json")]
    public void AcceptanceRejectsJunctionsAtEverySettingsOrOwnerPath(string fileName)
    {
        var root = NewOwnedRoot();
        var path = Path.Combine(root, fileName);
        if (File.Exists(path)) File.Delete(path);
        var target = NewRoot();
        CreateJunction(path, target);
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(root));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(target).Length);
    }

    [TestMethod]
    public void AcceptanceRejectsJunctionAncestorBeforeOpeningTheOwnerMarker()
    {
        var root = NewRoot();
        var target = NewOwnedRoot();
        var junction = Path.Combine(root, "fictional-junction");
        CreateJunction(junction, target);
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(junction));
        Assert.AreEqual(1, Directory.GetFileSystemEntries(target).Length);
    }

    [TestMethod]
    [DataRow("{\"application\":\"Dango\",\"acceptanceFormat\":2,\"fictional\":true}")]
    [DataRow("{\"application\":\"Dango\",\"acceptanceFormat\":1,\"fictional\":false}")]
    [DataRow("{\"application\":\"Other\",\"acceptanceFormat\":1,\"fictional\":true}")]
    [DataRow("{\"application\":\"Dango\",\"acceptanceFormat\":1,\"fictional\":true,\"extra\":true}")]
    public void AcceptanceRejectsIncorrectOwnerMarker(string marker)
    {
        var root = NewOwnedRoot();
        File.WriteAllText(Path.Combine(root, ".dango-local-install-acceptance.json"), marker);
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(root));
    }

    private static string? Resolve(string root) => App.ResolveLocalInstallAcceptanceSettingsFile(["--local-install-acceptance-settings", root]);

    private static string NewOwnedRoot()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, ".dango-local-install-acceptance.json"),
            """{"application":"Dango","acceptanceFormat":1,"fictional":true}""");
        return root;
    }

    private static string NewRoot()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "face-entry", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CreateJunction(string path, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(path);
        start.ArgumentList.Add(target);
        using var process = Process.Start(start)!;
        Assert.IsTrue(process.WaitForExit(10000), "The fictional junction fixture did not finish creating.");
        Assert.AreEqual(0, process.ExitCode, "The fictional junction fixture could not be created.");
        Assert.IsTrue((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0);
    }
}
