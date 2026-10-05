using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class AppearanceSettingsSessionTests
{
    [TestMethod]
    public void PreviewCancelDefaultAndSaveKeepTheThreeStatesSeparate()
    {
        var path = NewPath();
        var session = new AppearanceSettingsSession(path);
        var blue = OpaqueSrgbColor.Parse("#C9E2FF");

        Assert.AreEqual("#FFE8C2", session.SavedColor.ToHex());
        session.StagePreview(blue);
        Assert.AreEqual("#FFE8C2", session.SavedColor.ToHex());
        Assert.AreEqual("#C9E2FF", session.PreviewColor.ToHex());
        Assert.IsTrue(session.IsDirty);
        Assert.IsFalse(File.Exists(path));

        session.CancelPreview();
        Assert.AreEqual("#FFE8C2", session.PreviewColor.ToHex());
        Assert.IsFalse(session.IsDirty);

        session.StagePreview(blue);
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual("#C9E2FF", session.SavedColor.ToHex());
        Assert.IsFalse(session.IsDirty);

        session.RestoreDefault();
        Assert.AreEqual("#FFE8C2", session.PreviewColor.ToHex());
        Assert.AreEqual("#C9E2FF", session.SavedColor.ToHex());
        Assert.IsTrue(session.IsDirty);
    }

    [TestMethod]
    public void BodyAndEyePreviewsAreIndependentAndCancelRestoresBoth()
    {
        var session = new AppearanceSettingsSession(NewPath());
        var black = OpaqueSrgbColor.Parse("#000000");

        session.StageBodyPreview(black);
        Assert.AreEqual(black, session.PreviewBodyColor);
        Assert.AreEqual("#2B201E", session.PreviewEyeColor.ToHex());
        session.StageEyePreview(black);
        Assert.AreEqual(black, session.PreviewEyeColor);
        Assert.IsTrue(session.IsDirty);

        session.CancelPreview();
        Assert.AreEqual("#FFE8C2", session.PreviewBodyColor.ToHex());
        Assert.AreEqual("#2B201E", session.PreviewEyeColor.ToHex());
        Assert.IsFalse(session.IsDirty);

        session.StageBodyPreview(black);
        session.StageEyePreview(black);
        Assert.IsTrue(session.TrySave());
        session.RestoreDefaultEye();
        Assert.AreEqual(black, session.PreviewBodyColor);
        Assert.AreEqual("#2B201E", session.PreviewEyeColor.ToHex());
        session.CancelPreview();
        session.RestoreDefaultBody();
        Assert.AreEqual("#FFE8C2", session.PreviewBodyColor.ToHex());
        Assert.AreEqual(black, session.PreviewEyeColor);
    }

    [TestMethod]
    public void LegacySchemaOnlyMigratesOnExplicitSave()
    {
        var path = NewPath();
        const string original = "{\"appearanceSchemaVersion\":1,\"bodyColorHex\":\"#000000\"}";
        Write(path, original);
        var session = new AppearanceSettingsSession(path);

        Assert.AreEqual("#000000", session.SavedBodyColor.ToHex());
        Assert.AreEqual("#2B201E", session.SavedEyeColor.ToHex());
        Assert.IsFalse(session.IsDirty);
        Assert.IsTrue(session.HasPendingSave);
        session.CancelPreview();
        Assert.AreEqual(original, File.ReadAllText(path));

        Assert.IsTrue(session.TrySave());
        Assert.IsFalse(session.HasPendingSave);
        Assert.AreEqual("#000000", new AppearanceSettingsStore().Load(path).BodyColor.ToHex());
        Assert.AreEqual("#2B201E", new AppearanceSettingsStore().Load(path).EyeColor.ToHex());
        StringAssert.Contains(File.ReadAllText(path), "\"appearanceSchemaVersion\": 2");
    }

    [TestMethod]
    public void FailedSaveKeepsPreviewDirtyAndOldBytesForRetry()
    {
        var path = NewPath();
        const string original = "{\"appearanceSchemaVersion\":1,\"bodyColorHex\":\"#FFE8C2\"}";
        Write(path, original);
        var writer = new SwitchableWriter();
        var session = new AppearanceSettingsSession(path, new AppearanceSettingsStore(writer));
        session.StagePreview(OpaqueSrgbColor.Parse("#C9E2FF"));
        session.StageEyePreview(OpaqueSrgbColor.Parse("#C9E2FF"));

        Assert.IsFalse(session.TrySave());
        Assert.AreEqual(original, File.ReadAllText(path));
        Assert.AreEqual("#FFE8C2", session.SavedColor.ToHex());
        Assert.AreEqual("#C9E2FF", session.PreviewColor.ToHex());
        Assert.AreEqual("#2B201E", session.SavedEyeColor.ToHex());
        Assert.AreEqual("#C9E2FF", session.PreviewEyeColor.ToHex());
        Assert.IsTrue(session.IsDirty);

        writer.Fail = false;
        Assert.IsTrue(session.TrySave());
        Assert.IsFalse(session.IsDirty);
        Assert.AreEqual("#C9E2FF", new AppearanceSettingsStore().Load(path).BodyColor.ToHex());
        Assert.AreEqual("#C9E2FF", new AppearanceSettingsStore().Load(path).EyeColor.ToHex());
    }

    [TestMethod]
    public void ProtectedSidecarRejectsNormalSaveThenBackupSuccessUnlocksRetryAfterWriteFailure()
    {
        var path = NewPath();
        const string original = "{\"appearanceSchemaVersion\":3,\"bodyColorHex\":\"#C9E2FF\",\"eyeColorHex\":\"#2B201E\"}";
        Write(path, original);
        var writer = new SwitchableWriter();
        var backups = new List<string>();
        var session = new AppearanceSettingsSession(path, new AppearanceSettingsStore(writer), (source, destination) =>
        {
            File.Copy(source, destination, overwrite: false);
            backups.Add(destination);
        });
        session.StagePreview(OpaqueSrgbColor.Parse("#DDCCF8"));

        Assert.IsTrue(session.IsWriteProtected);
        Assert.IsFalse(session.TrySave());
        Assert.AreEqual(original, File.ReadAllText(path));
        Assert.IsFalse(session.TryRecoverAndSave());
        Assert.IsFalse(session.IsWriteProtected);
        Assert.IsTrue(session.IsDirty);
        Assert.AreEqual(original, File.ReadAllText(path));
        Assert.AreEqual(1, backups.Count);
        Assert.AreEqual(original, File.ReadAllText(backups[0]));

        writer.Fail = false;
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual("#DDCCF8", new AppearanceSettingsStore().Load(path).BodyColor.ToHex());
    }

    [TestMethod]
    public void FailedBackupKeepsProtectionAndOriginal()
    {
        var path = NewPath();
        const string original = "broken";
        Write(path, original);
        var session = new AppearanceSettingsSession(path, backup: (_, _) => throw new IOException("simulated backup failure"));

        Assert.IsFalse(session.TryRecoverAndSave());
        Assert.IsTrue(session.IsWriteProtected);
        Assert.AreEqual(original, File.ReadAllText(path));
    }

    [TestMethod]
    public void RecoveringProtectedDefaultWritesAValidSidecarEvenWithoutAColorDifference()
    {
        var path = NewPath();
        const string original = "broken";
        Write(path, original);
        string? backupPath = null;
        var session = new AppearanceSettingsSession(path, backup: (source, destination) =>
        {
            File.Copy(source, destination, overwrite: false);
            backupPath = destination;
        });

        Assert.IsFalse(session.IsDirty);
        Assert.IsTrue(session.TryRecoverAndSave());

        Assert.IsFalse(session.IsWriteProtected);
        Assert.IsFalse(session.HasPendingSave);
        Assert.AreEqual(original, File.ReadAllText(backupPath!));
        Assert.AreEqual(OpaqueSrgbColor.Default, new AppearanceSettingsStore().Load(path).BodyColor);
    }

    [TestMethod]
    public void FailedProtectedDefaultWriteCanBeRetriedWithTheNormalSaveAction()
    {
        var path = NewPath();
        Write(path, "broken");
        var writer = new SwitchableWriter();
        var session = new AppearanceSettingsSession(path, new AppearanceSettingsStore(writer));

        Assert.IsFalse(session.TryRecoverAndSave());
        Assert.IsFalse(session.IsWriteProtected);
        Assert.IsFalse(session.IsDirty);
        Assert.IsTrue(session.HasPendingSave);

        writer.Fail = false;
        Assert.IsTrue(session.TrySave());
        Assert.IsFalse(session.HasPendingSave);
        Assert.AreEqual(OpaqueSrgbColor.Default, new AppearanceSettingsStore().Load(path).BodyColor);
    }

    private static string NewPath() => Path.Combine(TestRepository.Root, "artifacts", "test-runs", "appearance",
        Guid.NewGuid().ToString("N"), "appearance.json");

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private sealed class SwitchableWriter : IAtomicTextWriter
    {
        public bool Fail { get; set; } = true;
        public void Write(string path, string content)
        {
            if (Fail) throw new IOException("simulated write failure");
            new AtomicTextFileWriter().Write(path, content);
        }
    }
}
