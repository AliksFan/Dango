using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ApplicationSettingsSessionTests
{
    private static string NewPath() => Path.Combine(TestRepository.Root, "artifacts", "test-runs",
        "settings", Guid.NewGuid().ToString("N"), "settings.json");

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void MigrationPreservesOldPreferencesAndBacksUpOriginalBytes(int version)
    {
        var path = NewPath();
        var original = $$$"""{"schemaVersion":{{{version}}},"width":1000,"height":700,"displayMode":"Window","showDebugSkeleton":true,"keyboardControl":{"isEnabled":true,"scheme":"Wasd"}}""";
        Write(path, original);
        var session = new ApplicationSettingsSession(path);
        Assert.AreEqual(ControlCenterWindowSettings.CurrentSchemaVersion, session.Current.SchemaVersion);
        Assert.AreEqual(1000, session.Current.Width);
        Assert.AreEqual(700, session.Current.Height);
        Assert.AreEqual(DesktopDisplayMode.Window, session.Current.DisplayMode);
        Assert.IsTrue(session.Current.ShowDebugSkeleton);
        Assert.IsTrue(session.Current.KeyboardControl.IsEnabled);
        Assert.AreEqual(KeyboardControlScheme.Wasd, session.Current.KeyboardControl.Scheme);
        Assert.IsTrue(session.Current.InteractionEnabled);
        Assert.AreEqual("sprout", session.Current.HatPresetId);
        Assert.IsFalse(session.Current.IntroSeen);
        Assert.IsTrue(session.TrySave(session.Current with { HatPresetId = "bowler", IntroSeen = true }));
        Assert.AreEqual(original, File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.backup-*").Single()));
        var restored = new ApplicationSettingsSession(path).Current;
        Assert.AreEqual("bowler", restored.HatPresetId);
        Assert.IsTrue(restored.IntroSeen);
        Assert.IsTrue(restored.ShowDebugSkeleton);
    }

    [TestMethod]
    public void ArrowKeyMigrationBacksUpOriginalWithoutDisablingKeyboardControl()
    {
        var path = NewPath();
        const string original = """
            {"schemaVersion":4,"width":1000,"height":700,"keyboardControl":{"isEnabled":true,"scheme":"ArrowKeys"},"hatPresetId":"bowler"}
            """;
        Write(path, original);

        var session = new ApplicationSettingsSession(path);
        Assert.IsTrue(session.Current.KeyboardControl.IsEnabled);
        Assert.AreEqual(KeyboardControlScheme.Wasd, session.Current.KeyboardControl.Scheme);
        Assert.AreEqual("bowler", session.Current.HatPresetId);
        Assert.IsTrue(session.TrySave());

        Assert.AreEqual(original, File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.backup-*").Single()));
        var restored = new ApplicationSettingsSession(path).Current;
        Assert.IsTrue(restored.KeyboardControl.IsEnabled);
        Assert.AreEqual(KeyboardControlScheme.Wasd, restored.KeyboardControl.Scheme);
        Assert.AreEqual("bowler", restored.HatPresetId);
    }

    [TestMethod]
    [DataRow("{\"schemaVersion\":999,\"futureValue\":\"preserve-me\"}")]
    [DataRow("not-json")]
    public void UnreadableSettingsCannotBeOverwrittenUntilExplicitRecovery(string original)
    {
        var path = NewPath();
        Write(path, original);
        var session = new ApplicationSettingsSession(path);
        Assert.IsTrue(session.IsWriteProtected);
        Assert.IsFalse(session.TrySave(session.Current with { HatPresetId = "top-hat" }));
        Assert.AreEqual(original, File.ReadAllText(path));
        Assert.IsTrue(session.TryRecoverAndSave());
        Assert.AreEqual(original, File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.backup-*").Single()));
        Assert.AreEqual("top-hat", new ApplicationSettingsSession(path).Current.HatPresetId);
    }

    [TestMethod]
    public void FailedBackupProtectsOriginalAndFailedWriteCanBeRetriedWithoutReapplyingCommands()
    {
        var path = NewPath();
        Write(path, "broken");
        var protectedSession = new ApplicationSettingsSession(path, backup: (_, _) => throw new IOException());
        Assert.IsFalse(protectedSession.TryRecoverAndSave());
        Assert.IsTrue(protectedSession.IsWriteProtected);
        Assert.AreEqual("broken", File.ReadAllText(path));

        var goodPath = NewPath();
        var writer = new FailingWriter();
        new ControlCenterWindowSettingsStore().Save(goodPath, ControlCenterWindowSettings.CreateDefault());
        var before = File.ReadAllText(goodPath);
        var session = new ApplicationSettingsSession(goodPath, new ControlCenterWindowSettingsStore(writer));
        Assert.IsFalse(session.TrySave(session.Current with { InteractionEnabled = false, HatPresetId = "bowler" }));
        Assert.AreEqual(before, File.ReadAllText(goodPath));
        Assert.IsFalse(session.Current.InteractionEnabled);
        StringAssert.Contains(session.StatusMessage, "未保存");
        writer.Fail = false;
        Assert.IsTrue(session.TrySave());
        Assert.IsFalse(new ApplicationSettingsSession(goodPath).Current.InteractionEnabled);
        Assert.AreEqual("bowler", new ApplicationSettingsSession(goodPath).Current.HatPresetId);
    }

    [TestMethod]
    public void UnknownHatFallsBackWithoutDiscardingValidPreferences()
    {
        var path = NewPath();
        Write(path, """{"schemaVersion":4,"width":1100,"height":750,"hatPresetId":"future-hat"}""");
        var session = new ApplicationSettingsSession(path);
        Assert.AreEqual(1100, session.Current.Width);
        Assert.AreEqual("sprout", session.Current.HatPresetId);
        Assert.IsFalse(session.IsWriteProtected);
        Assert.IsNotNull(session.StartupWarning);
    }

    private sealed class FailingWriter : IAtomicTextWriter
    {
        public bool Fail { get; set; } = true;
        public void Write(string path, string content)
        {
            if (Fail) throw new IOException();
            new AtomicTextFileWriter().Write(path, content);
        }
    }

    [TestMethod]
    public void InvalidCaptureReportsUnsavedWithoutCrashingOrReplacingPreferences()
    {
        var path = NewPath();
        var session = new ApplicationSettingsSession(path);
        Assert.IsTrue(session.TrySave());
        var before = File.ReadAllText(path);
        Assert.IsFalse(session.TrySave(session.Current with { Width = 10000 }));
        StringAssert.Contains(session.StatusMessage, "未保存");
        Assert.AreEqual(before, File.ReadAllText(path));
        Assert.AreEqual(940, session.Current.Width);
    }
}
