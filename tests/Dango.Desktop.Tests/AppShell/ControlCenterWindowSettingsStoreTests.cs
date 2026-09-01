using Dango.Desktop.AppShell.ControlCenter;
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

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Dango.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
