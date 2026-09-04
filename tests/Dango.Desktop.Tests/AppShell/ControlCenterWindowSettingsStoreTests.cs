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

            store.Save(path, new ControlCenterWindowSettings(940, 620, 1, layering));
            var restored = store.Load(path);

            Assert.AreEqual(1, restored.SchemaVersion);
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

            Assert.AreEqual(1, restored.SchemaVersion);
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
