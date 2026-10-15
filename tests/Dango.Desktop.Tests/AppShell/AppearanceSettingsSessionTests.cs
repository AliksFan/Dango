using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Presentation.Attachments;
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
    [DataRow("{\"appearanceSchemaVersion\":1,\"bodyColorHex\":\"#000000\"}", "#2B201E")]
    [DataRow("{\"appearanceSchemaVersion\":2,\"bodyColorHex\":\"#000000\",\"eyeColorHex\":\"#000000\"}", "#000000")]
    public void LegacySchemaOnlyMigratesOnExplicitSave(string original, string expectedEyes)
    {
        var path = NewPath();
        Write(path, original);
        var session = new AppearanceSettingsSession(path);

        Assert.AreEqual("#000000", session.SavedBodyColor.ToHex());
        Assert.AreEqual(expectedEyes, session.SavedEyeColor.ToHex());
        Assert.AreEqual(EyeLayoutParameters.Default, session.SavedAppearance.EyeLayout);
        Assert.IsFalse(session.IsDirty);
        Assert.IsTrue(session.HasPendingSave);
        session.CancelPreview();
        Assert.AreEqual(original, File.ReadAllText(path));
        Assert.IsFalse(session.IsDirty);
        Assert.IsTrue(session.HasPendingSave);

        Assert.IsTrue(session.TrySave());
        Assert.IsFalse(session.HasPendingSave);
        Assert.AreEqual("#000000", new AppearanceSettingsStore().Load(path).BodyColor.ToHex());
        Assert.AreEqual(expectedEyes, new AppearanceSettingsStore().Load(path).EyeColor.ToHex());
        StringAssert.Contains(File.ReadAllText(path), "\"appearanceSchemaVersion\": 4");
        var reopened = new AppearanceSettingsSession(path);
        Assert.AreEqual(session.SavedAppearance, reopened.SavedAppearance);
        Assert.IsFalse(reopened.HasPendingSave);
    }

    [TestMethod]
    public void WholeAppearanceDraftCancelDefaultsAndReopenRestoreEveryField()
    {
        var path = NewPath();
        var session = new AppearanceSettingsSession(path);
        var appearance = NondefaultAppearance();

        session.StagePreview(appearance);
        Assert.AreEqual(appearance, session.DraftAppearance);
        Assert.AreEqual(DangoAppearanceConfiguration.Default, session.SavedAppearance);
        Assert.IsTrue(session.IsDirty);
        Assert.IsFalse(File.Exists(path));
        session.CancelPreview();
        Assert.AreEqual(DangoAppearanceConfiguration.Default, session.DraftAppearance);
        Assert.IsFalse(session.HasPendingSave);
        session.StagePreview(appearance);
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(appearance, session.SavedAppearance);
        Assert.AreEqual(appearance, new AppearanceSettingsSession(path).SavedAppearance);
        Assert.IsFalse(session.IsDirty);

        session.RestoreDefault();
        Assert.AreEqual(DangoAppearanceConfiguration.Default, session.DraftAppearance);
        Assert.AreEqual(appearance, session.SavedAppearance);
        Assert.IsTrue(session.IsDirty);
        session.CancelPreview();
        Assert.AreEqual(appearance, session.DraftAppearance);
        Assert.IsFalse(session.IsDirty);
    }

    [TestMethod]
    public void StagingEyesDoesNotDiscardColorDraftAndColorDefaultsKeepEyeDraft()
    {
        var session = new AppearanceSettingsSession(NewPath());
        var black = OpaqueSrgbColor.Parse("#000000");
        var eyes = new EyeLayoutParameters(140, 100, 66, 34);
        session.StageBodyPreview(black);
        session.StageEyePreview(black);
        session.StageEyes(eyes);

        Assert.AreEqual(new DangoAppearanceConfiguration(black, black, eyes), session.DraftAppearance);
        Assert.IsTrue(session.IsDirty);
        session.RestoreDefaultBody();
        Assert.AreEqual(OpaqueSrgbColor.Default, session.PreviewBodyColor);
        Assert.AreEqual(black, session.PreviewEyeColor);
        Assert.AreEqual(eyes, session.DraftAppearance.EyeLayout);
        session.RestoreDefaultEye();
        Assert.AreEqual(AppearanceSettings.DefaultEyeColor, session.PreviewEyeColor);
        Assert.AreEqual(eyes, session.DraftAppearance.EyeLayout);
    }

    [TestMethod]
    public void ColorAndEyeEditsKeepTheDraftBody()
    {
        var session = new AppearanceSettingsSession(NewPath());
        var body = new Dango.Desktop.Physics.Tuning.DangoBodyControls(120, 0, 10);
        session.StageBody(body);
        session.StageBodyPreview(OpaqueSrgbColor.Parse("#000000"));
        session.StageEyePreview(OpaqueSrgbColor.Parse("#010101"));
        session.StageEyes(new EyeLayoutParameters(140, 100, 66, 34));
        session.RestoreDefaultBody();
        session.RestoreDefaultEye();
        Assert.AreEqual(body, session.DraftAppearance.Body);
    }

    [TestMethod]
    public void UnpreviewedBodyCannotBeSavedAndAPreviewBindsTheExactControls()
    {
        var path = NewPath();
        var session = new AppearanceSettingsSession(path);
        var body = new Dango.Desktop.Physics.Tuning.DangoBodyControls(110, 30, 70);
        session.StageBody(body);
        Assert.IsTrue(session.RequiresBodyPreview);
        Assert.IsFalse(session.TrySave());
        Assert.IsFalse(File.Exists(path));

        session.MarkBodyPreviewed(new(110, 30, 71), Dango.Desktop.Physics.Tuning.DangoBodyMaterial.ModelVersion);
        Assert.IsTrue(session.RequiresBodyPreview);
        session.MarkBodyPreviewed(body, "older-model");
        Assert.IsTrue(session.RequiresBodyPreview);
        session.MarkBodyPreviewed(body, Dango.Desktop.Physics.Tuning.DangoBodyMaterial.ModelVersion);
        Assert.IsFalse(session.RequiresBodyPreview);
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(body, new AppearanceSettingsStore().Load(path).Configuration.Body);

        // Colour-only edits never need a body preview.
        session.StageBodyPreview(OpaqueSrgbColor.Parse("#000000"));
        Assert.IsFalse(session.RequiresBodyPreview);
    }

    [TestMethod]
    public void BodyEditsInvalidateOldPreviewEvenWhenReturningToSavedOrEarlierPreview()
    {
        var session = new AppearanceSettingsSession(NewPath());
        var body = new Dango.Desktop.Physics.Tuning.DangoBodyControls(110, 30, 70);
        session.StageBody(body);
        session.MarkBodyPreviewed(body, Dango.Desktop.Physics.Tuning.DangoBodyMaterial.ModelVersion);
        session.StageBody(new(111, 30, 70));
        session.StageBody(body);
        Assert.IsTrue(session.RequiresBodyPreview, "An earlier success must not revive after any body edit.");
        Assert.IsNull(session.PreviewedBody);
        session.StageBody(session.SavedAppearance.Body);
        Assert.IsTrue(session.RequiresBodyPreview, "Saved controls also need a rebuild after another body was active.");
        session.CancelPreview();
        Assert.IsFalse(session.RequiresBodyPreview);
    }

    [TestMethod]
    public void ProtectedBodyCannotBackupOrWriteBeforePreviewAndDefaultsRemainDraftOnly()
    {
        var path = NewPath();
        Write(path, "broken");
        var backupCount = 0;
        var session = new AppearanceSettingsSession(path, backup: (_, _) => backupCount++);
        session.StageBody(new(120, 0, 0));
        Assert.IsFalse(session.TryRecoverAndSave());
        Assert.AreEqual(0, backupCount);
        Assert.IsTrue(session.IsWriteProtected);
        Assert.AreEqual("broken", File.ReadAllText(path));
        session.RestoreDefault();
        Assert.IsTrue(session.RequiresBodyPreview);
        Assert.AreEqual(DangoAppearanceConfiguration.Default, session.DraftAppearance);
        Assert.AreEqual("broken", File.ReadAllText(path));
    }

    [TestMethod]
    public void FailedWholeAppearanceSaveKeepsSavedAndCompleteDraftUntilRetry()
    {
        var path = NewPath();
        var store = new AppearanceSettingsStore();
        store.Save(path, AppearanceSettings.CreateDefault());
        var original = File.ReadAllBytes(path);
        var writer = new SwitchableWriter();
        var session = new AppearanceSettingsSession(path, new AppearanceSettingsStore(writer));
        var appearance = NondefaultAppearance();
        session.StagePreview(appearance);

        Assert.IsFalse(session.TrySave());
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.AreEqual(DangoAppearanceConfiguration.Default, session.SavedAppearance);
        Assert.AreEqual(appearance, session.DraftAppearance);
        Assert.IsTrue(session.HasPendingSave);
        writer.Fail = false;
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(appearance, session.SavedAppearance);
        Assert.AreEqual(appearance, new AppearanceSettingsSession(path).SavedAppearance);
        Assert.IsFalse(session.HasPendingSave);
    }

    [TestMethod]
    public void RealLockedTargetReplacementFailsThenUnlockRetryReloadsWholeAppearance()
    {
        var path = NewPath();
        new AppearanceSettingsStore().Save(path, AppearanceSettings.CreateDefault());
        var original = File.ReadAllBytes(path);
        var session = new AppearanceSettingsSession(path);
        var appearance = NondefaultAppearance();
        session.StagePreview(appearance);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.IsFalse(session.TrySave(), "The real Windows target lock must reject the atomic replacement.");
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.AreEqual(DangoAppearanceConfiguration.Default, session.SavedAppearance);
            Assert.AreEqual(appearance, session.DraftAppearance);
            Assert.IsTrue(session.IsDirty);
            Assert.IsTrue(session.HasPendingSave);
            Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
        }

        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(appearance, session.SavedAppearance);
        Assert.IsFalse(session.HasPendingSave);
        var reopened = new AppearanceSettingsSession(path);
        Assert.AreEqual(appearance, reopened.SavedAppearance);
        Assert.AreEqual(appearance, reopened.DraftAppearance);
        Assert.IsFalse(reopened.IsWriteProtected);
        Assert.IsFalse(reopened.HasPendingSave);
    }

    [TestMethod]
    public void ProtectedDefaultBackupThenRealReplacementFailureRetainsPendingAndDoesNotRepeatBackup()
    {
        var path = NewPath();
        const string original = "future-or-corrupt";
        Write(path, original);
        var session = new AppearanceSettingsSession(path);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.IsFalse(session.TryRecoverAndSave());
            Assert.IsFalse(session.IsWriteProtected);
            Assert.IsFalse(session.IsDirty);
            Assert.IsTrue(session.HasPendingSave);
            Assert.AreEqual(original, File.ReadAllText(path));
            var backups = Directory.GetFiles(Path.GetDirectoryName(path)!, "appearance.json.backup-*");
            Assert.AreEqual(1, backups.Length);
            Assert.AreEqual(original, File.ReadAllText(backups[0]));
            Assert.IsFalse(session.TryRecoverAndSave());
            Assert.AreEqual(1, Directory.GetFiles(Path.GetDirectoryName(path)!, "appearance.json.backup-*").Length);
            Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
            session.CancelPreview();
            Assert.IsTrue(session.HasPendingSave);
        }

        Assert.IsTrue(session.TrySave());
        Assert.IsFalse(session.HasPendingSave);
        Assert.AreEqual(DangoAppearanceConfiguration.Default, new AppearanceSettingsSession(path).SavedAppearance);
        Assert.AreEqual(1, Directory.GetFiles(Path.GetDirectoryName(path)!, "appearance.json.backup-*").Length);
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
        const string original = "{\"appearanceSchemaVersion\":4,\"bodyColorHex\":\"#C9E2FF\",\"eyeColorHex\":\"#2B201E\",\"eyeSizePercent\":100,\"eyeOvalityPercent\":0,\"eyeHeightControlPercent\":51,\"eyeSpacingControlPercent\":24.5}";
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

    private static DangoAppearanceConfiguration NondefaultAppearance() => new(
        OpaqueSrgbColor.Parse("#000000"), OpaqueSrgbColor.Parse("#000000"),
        new EyeLayoutParameters(139.75, -67.125, 40.25, 33.875));

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
