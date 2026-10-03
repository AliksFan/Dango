using System.Text.Json;
using System.Text.Json.Nodes;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class SettingsRollbackCompatibilityTests
{
    private static string NewPath() => Path.Combine(TestRepository.Root, "artifacts", "test-runs",
        "settings-rollback", Guid.NewGuid().ToString("N"), "settings.json");

    private static string PresetPath(string path) => Path.ChangeExtension(path, ".window-preset.json");

    [TestMethod]
    [DataRow(ControlCenterWindowPreset.Landscape1080p)]
    [DataRow(ControlCenterWindowPreset.Landscape1440p)]
    [DataRow(ControlCenterWindowPreset.Portrait)]
    public void EachPresetWritesAnOldReaderCompatibleMainFileAndIndependentPreset(ControlCenterWindowPreset preset)
    {
        var path = NewPath();
        var size = ControlCenterWindowPresets.GetSize(preset);
        var store = new ControlCenterWindowSettingsStore();
        store.Save(path, new(size.Width, size.Height, selectedPreset: preset,
            hatPresetId: "bowler", interactionEnabled: false,
            keyboardControl: new(true, KeyboardControlScheme.Wasd)));

        AssertLegacyV4File(path);
        var restored = store.LoadOrDefault(path);
        Assert.AreEqual(preset, restored.Settings.SelectedPreset);
        Assert.AreEqual("bowler", restored.Settings.HatPresetId);
        Assert.IsFalse(restored.Settings.InteractionEnabled);
        Assert.IsFalse(restored.PreserveOriginal);
        Assert.IsTrue(File.Exists(PresetPath(path)));
    }

    [TestMethod]
    public void OldVersionSavingMainPreferencesDoesNotDiscardNewVersionPreset()
    {
        var path = NewPath();
        var store = new ControlCenterWindowSettingsStore();
        store.Save(path, new(1010, 690, selectedPreset: ControlCenterWindowPreset.Portrait));
        AssertLegacyV4File(path);
        var presetBytes = File.ReadAllBytes(PresetPath(path));

        var oldVersionSettings = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        oldVersionSettings["hatPresetId"] = "bowler";
        oldVersionSettings["width"] = 1100;
        oldVersionSettings["height"] = 750;
        File.WriteAllText(path, oldVersionSettings.ToJsonString());

        var restored = store.Load(path);
        Assert.AreEqual("bowler", restored.HatPresetId);
        Assert.AreEqual(1100, restored.Width);
        Assert.AreEqual(ControlCenterWindowPreset.Portrait, restored.SelectedPreset);
        CollectionAssert.AreEqual(presetBytes, File.ReadAllBytes(PresetPath(path)));
    }

    [TestMethod]
    public void SchemaFiveMigrationBacksUpOriginalAndWritesStrictLegacyFormat()
    {
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string original = """{"schemaVersion":5,"width":480,"height":800,"selectedPreset":"Portrait","hatPresetId":"bowler","interactionEnabled":false}""";
        File.WriteAllText(path, original);

        var session = new ApplicationSettingsSession(path);
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(original, File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(path)!, "settings.json.backup-*").Single()));
        AssertLegacyV4File(path);
        var restored = new ApplicationSettingsSession(path).Current;
        Assert.AreEqual(ControlCenterWindowPreset.Portrait, restored.SelectedPreset);
        Assert.AreEqual("bowler", restored.HatPresetId);
        Assert.IsFalse(restored.InteractionEnabled);
    }

    [TestMethod]
    [DataRow("not-json")]
    [DataRow("{\"schemaVersion\":99,\"selectedPreset\":\"future\"}")]
    public void BrokenPresetProtectsItselfWhileValidMainPreferencesRemainUsable(string original)
    {
        var path = NewPath();
        new ControlCenterWindowSettingsStore().Save(path, new(1010, 690,
            selectedPreset: ControlCenterWindowPreset.Portrait, interactionEnabled: false));
        File.WriteAllText(PresetPath(path), original);

        var session = new ApplicationSettingsSession(path);
        Assert.IsFalse(session.IsWriteProtected, "A bad preset must not protect the valid main settings.");
        Assert.AreEqual(1010, session.Current.Width);
        Assert.IsFalse(session.Current.InteractionEnabled);
        Assert.AreEqual(ControlCenterWindowPreset.Landscape1080p, session.Current.SelectedPreset);
        Assert.IsTrue(session.HasWarning);
        Assert.IsFalse(session.TrySave(session.Current with { HatPresetId = "bowler" }));
        Assert.AreEqual(original, File.ReadAllText(PresetPath(path)));
        Assert.AreEqual("bowler", new ApplicationSettingsSession(path).Current.HatPresetId);

        Assert.IsTrue(session.TryRecoverAndSave());
        Assert.AreEqual(original, File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(path)!, "settings.window-preset.json.backup-*").Single()));
        Assert.AreEqual(ControlCenterWindowPreset.Landscape1080p, new ApplicationSettingsSession(path).Current.SelectedPreset);
    }

    [TestMethod]
    public void PresetWriteFailureKeepsMainUnchangedAndRetryPersistsBothPreferences()
    {
        var path = NewPath();
        new ControlCenterWindowSettingsStore().Save(path, new(1010, 690));
        var mainBefore = File.ReadAllBytes(path);
        var writer = new SelectiveWriter(PresetPath(path));
        var session = new ApplicationSettingsSession(path, new(writer));

        Assert.IsFalse(session.TrySave(session.Current with
        { SelectedPreset = ControlCenterWindowPreset.Portrait, HatPresetId = "bowler" }));
        CollectionAssert.AreEqual(mainBefore, File.ReadAllBytes(path));
        StringAssert.Contains(session.StatusMessage, "未保存");
        writer.Fail = false;
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(ControlCenterWindowPreset.Portrait, new ApplicationSettingsSession(path).Current.SelectedPreset);
        Assert.AreEqual("bowler", new ApplicationSettingsSession(path).Current.HatPresetId);
        AssertLegacyV4File(path);
    }

    [TestMethod]
    public void MainWriteFailureNeverReportsTheIndependentPresetAsFullySaved()
    {
        var path = NewPath();
        new ControlCenterWindowSettingsStore().Save(path, new(1010, 690));
        var mainBefore = File.ReadAllBytes(path);
        var writer = new SelectiveWriter(path);
        var session = new ApplicationSettingsSession(path, new(writer));
        Assert.IsFalse(session.TrySave(session.Current with
        { SelectedPreset = ControlCenterWindowPreset.Portrait, HatPresetId = "bowler" }));
        CollectionAssert.AreEqual(mainBefore, File.ReadAllBytes(path));
        StringAssert.Contains(session.StatusMessage, "未保存");
        writer.Fail = false;
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual("bowler", new ApplicationSettingsSession(path).Current.HatPresetId);
        Assert.AreEqual(ControlCenterWindowPreset.Portrait, new ApplicationSettingsSession(path).Current.SelectedPreset);
    }

    private static void AssertLegacyV4File(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.AreEqual(4, root.GetProperty("schemaVersion").GetInt32(), "The actual old reader rejects schema 5.");
        string[] oldFields = ["width", "height", "schemaVersion", "desktopLayeringExperiment", "keyboardControl",
            "displayMode", "sceneWindow", "showDebugSkeleton", "interactionEnabled", "hatPresetId", "introSeen"];
        CollectionAssert.AreEquivalent(oldFields, root.EnumerateObject().Select(p => p.Name).ToArray(),
            "The old reader rejects any unmapped main-file field.");
        Assert.IsTrue(root.GetProperty("width").GetDouble() >= 820);
        Assert.IsTrue(root.GetProperty("height").GetDouble() >= 560);
    }

    private sealed class SelectiveWriter(string failedPath) : IAtomicTextWriter
    {
        public bool Fail { get; set; } = true;
        public void Write(string path, string content)
        {
            if (Fail && path == failedPath) throw new IOException("fictional preset write failure");
            new AtomicTextFileWriter().Write(path, content);
        }
    }
}
