using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ControlCenterWindowSettingsStoreTests
{
    [TestMethod]
    public void NonSensitiveWindowSizeRoundTrips()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new ControlCenterWindowSettingsStore();

            store.Save(path, new ControlCenterWindowSettings(940, 620));
            var restored = store.Load(path);

            Assert.AreEqual(940, restored.Width);
            Assert.AreEqual(620, restored.Height);
            var json = File.ReadAllText(path);
            Assert.IsFalse(json.Contains("left", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(json.Contains("user", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void UnsafeOrUnusableWindowSizesAreRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => ControlCenterWindowSettingsValidator.Validate(new ControlCenterWindowSettings(400, 300)));
        Assert.ThrowsExactly<InvalidDataException>(
            () => ControlCenterWindowSettingsValidator.Validate(new ControlCenterWindowSettings(10000, 10000)));
    }

    [TestMethod]
    public void LayeringExperimentRoundTripsWithWindowSettings()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new ControlCenterWindowSettingsStore();
            var layering = new DesktopLayeringSettings(
                DesktopObjectLayer.AboveDesktopIcons,
                new Dictionary<string, DesktopObjectLayer>
                {
                    [DesktopLayeringSettings.PrototypeCushionId] = DesktopObjectLayer.AboveDesktopIcons,
                });

            store.Save(path, new ControlCenterWindowSettings(940, 620, 2, layering));
            var restored = store.Load(path);

            Assert.AreEqual(2, restored.SchemaVersion);
            Assert.AreEqual(DesktopObjectLayer.AboveDesktopIcons, restored.DesktopLayeringExperiment.DangoLayer);
            Assert.AreEqual(
                DesktopObjectLayer.AboveDesktopIcons,
                restored.DesktopLayeringExperiment.FurnitureLayers[DesktopLayeringSettings.PrototypeCushionId]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LegacyWindowOnlySettingsMigrateToLayeringDefaults()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, """
                {
                  "width": 940,
                  "height": 620
                }
                """);

            var restored = new ControlCenterWindowSettingsStore().Load(path);

            Assert.AreEqual(2, restored.SchemaVersion);
            Assert.AreEqual(
                DesktopObjectLayer.BelowDesktopIcons,
                restored.DesktopLayeringExperiment.DangoLayer);
            Assert.IsFalse(restored.KeyboardControl.IsEnabled);
            Assert.AreEqual(KeyboardControlScheme.ArrowKeys, restored.KeyboardControl.Scheme);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("WorkerW")]
    [DataRow("Overlay")]
    public void LegacyHostModesMigrateToWallpaper(string legacyMode)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, $$"""
                {
                  "schemaVersion": 1,
                  "width": 940,
                  "height": 620,
                  "displayMode": "{{legacyMode}}"
                }
                """);

            var result = new ControlCenterWindowSettingsStore().LoadOrDefault(path);

            Assert.AreEqual(DesktopDisplayMode.Wallpaper, result.Settings.DisplayMode);
            Assert.IsTrue(result.WasMigrated);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ValidWindowModeAndScenePlacementRoundTripInSchemaTwo()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var sceneWindow = new SceneWindowSettings(800, 600, "DISPLAY1", 0.25, 0.75);
            var store = new ControlCenterWindowSettingsStore();

            store.Save(path, new ControlCenterWindowSettings(
                940,
                620,
                displayMode: DesktopDisplayMode.Window,
                sceneWindow: sceneWindow));
            var restored = store.Load(path);

            Assert.AreEqual(2, restored.SchemaVersion);
            Assert.AreEqual(DesktopDisplayMode.Window, restored.DisplayMode);
            Assert.AreEqual(sceneWindow, restored.SceneWindow);
            var json = File.ReadAllText(path);
            StringAssert.Contains(json, "\"displayMode\": \"Window\"");
            Assert.IsFalse(json.Contains("Overlay", StringComparison.Ordinal));
            Assert.IsFalse(json.Contains("WorkerW", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void UnknownSchemaOrModeReturnsSafeDefaultWithWarning()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, """
                {
                  "schemaVersion": 99,
                  "width": 940,
                  "height": 620,
                  "displayMode": "Unexpected"
                }
                """);

            var result = new ControlCenterWindowSettingsStore().LoadOrDefault(path);

            Assert.AreEqual(DesktopDisplayMode.Wallpaper, result.Settings.DisplayMode);
            Assert.IsFalse(result.WasMigrated);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Warning));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void KeyboardControlRoundTripsWithoutInputHistory()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new ControlCenterWindowSettingsStore();
            store.Save(path, new ControlCenterWindowSettings(
                940,
                620,
                keyboardControl: new KeyboardControlSettings(true, KeyboardControlScheme.Wasd)));

            var restored = store.Load(path);
            Assert.IsTrue(restored.KeyboardControl.IsEnabled);
            Assert.AreEqual(KeyboardControlScheme.Wasd, restored.KeyboardControl.Scheme);
            var json = File.ReadAllText(path);
            Assert.IsFalse(json.Contains("lastKey", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(json.Contains("history", StringComparison.OrdinalIgnoreCase));
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
}
