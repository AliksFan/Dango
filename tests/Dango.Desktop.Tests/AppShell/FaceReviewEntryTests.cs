using System.Diagnostics;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class FaceReviewEntryTests
{
    [TestMethod]
    public void FaceReviewCannotBeCombinedWithAnyOtherReviewMode()
    {
        App.ValidateIsolatedReviewSelection(["--face-review"]);
        foreach (var other in new[] { "--home-ui-review", "--settings-ui-review", "--physics-host-review",
                     "--eye-hat-review", "--unified-live-home-review", "--porch-scene-review", "--local-room-review" })
            Assert.ThrowsExactly<ArgumentException>(() => App.ValidateIsolatedReviewSelection(["--face-review", other]), other);
    }

    [TestMethod]
    public void FaceReviewHasItsOwnMutexAndRequiresExplicitOwnedSettings()
    {
        var instance = App.ResolveSingleInstanceName(["--face-review"]);
        Assert.AreNotEqual(App.ResolveSingleInstanceName([]), instance);
        foreach (var other in new[] { "--physics-host-review", "--eye-hat-review", "--unified-live-home-review",
                     "--porch-scene-review", "--local-room-review" })
            Assert.AreNotEqual(App.ResolveSingleInstanceName([other]), instance, other);
        Assert.IsNull(App.ResolveFaceReviewSettingsFile([]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveFaceReviewSettingsFile(["--face-review"]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveFaceReviewSettingsFile(["--face-review-settings", NewRoot()]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveFaceReviewSettingsFile(
            ["--face-review", "--face-review-settings", NewRoot(), "--smoke-test"]));
    }

    [TestMethod]
    public void OwnedReviewUsesSettingsBesideAppearanceWithoutReadingOrWritingEitherFile()
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
    public void ReviewRejectsUnownedDailyPackageRelativeAndDriveDirectories()
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
    [DataRow(".dango-face-review.json")]
    public void ReviewRejectsDirectoriesAtEverySettingsOrOwnerPath(string fileName)
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
    [DataRow(".dango-face-review.json")]
    public void ReviewRejectsJunctionsAtEverySettingsOrOwnerPath(string fileName)
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
    public void ReviewRejectsJunctionAncestorBeforeOpeningTheOwnerMarker()
    {
        var root = NewRoot();
        var target = NewOwnedRoot();
        var junction = Path.Combine(root, "fictional-junction");
        CreateJunction(junction, target);
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(junction));
        Assert.AreEqual(1, Directory.GetFileSystemEntries(target).Length);
    }

    [TestMethod]
    [DataRow("{\"application\":\"Dango\",\"faceReviewFormat\":2,\"fictional\":true}")]
    [DataRow("{\"application\":\"Dango\",\"faceReviewFormat\":1,\"fictional\":false}")]
    [DataRow("{\"application\":\"Other\",\"faceReviewFormat\":1,\"fictional\":true}")]
    [DataRow("{\"application\":\"Dango\",\"faceReviewFormat\":1,\"fictional\":true,\"extra\":true}")]
    public void ReviewRejectsIncorrectOwnerMarker(string marker)
    {
        var root = NewOwnedRoot();
        File.WriteAllText(Path.Combine(root, ".dango-face-review.json"), marker);
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(root));
    }

    private static string? Resolve(string root) => App.ResolveFaceReviewSettingsFile(["--face-review", "--face-review-settings", root]);

    private static string NewOwnedRoot()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, ".dango-face-review.json"),
            """{"application":"Dango","faceReviewFormat":1,"fictional":true}""");
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
