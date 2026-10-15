using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows.Threading;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Furniture;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class OrdinaryRoomFurnitureRejectedEntryTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void CorruptRoomLayoutNeverAcknowledgesAHealthyEmptyRoomAndPreservesOriginalBytes()
    {
        var module = Environment.GetEnvironmentVariable("DANGO_ORDINARY_FURNITURE_TEST_MODULE") ??
            throw new InvalidOperationException("The real candidate furniture module is required.");
        RejectEntry(module, Encoding.UTF8.GetBytes("{\"schemaVersion\":999}"), expectedProtected: true);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void NonemptyLayoutWithMissingFurnitureAbiCannotEnterAnEmptyRoomOrRewriteItsLayout()
    {
        var module = Environment.GetEnvironmentVariable("DANGO_ORDINARY_FURNITURE_BASELINE_PATH") ??
            throw new InvalidOperationException("The separate room-bounds module without collection ABI is required.");
        var layout = new FurnitureLayout(3, Guid.NewGuid(), new(Guid.NewGuid(), FurnitureLayoutContract.OrdinaryRoom.DefaultAnchor));
        var bytes = FurnitureLayoutSerializer.Serialize(layout, FurnitureLayoutContract.OrdinaryRoom);
        RejectEntry(module, bytes, expectedProtected: false);
    }

    private static void RejectEntry(string module, byte[] original, bool expectedProtected)
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "furniture-room", "rejected-entry", Guid.NewGuid().ToString("N"));
        var path = FurnitureLayoutStore.GetLayoutPath(root, FurnitureLayoutContract.OrdinaryRoom);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, original);
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"),
            nativeLibraryPath: module, isolatedLocalRoomReview: true);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter(); Pump(250);
        var home = Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome;
        home.ResumePorch(); Pump(300);
        var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver");
        var readyAcknowledgements = 0;
        driver.HealthyFrameReady += _ => readyAcknowledgements++;
        if (expectedProtected)
        {
            Assert.IsNull(home.CatalogSnapshot, "A corrupt legacy layout must block catalog publication before room admission.");
            Assert.IsFalse(home.PorchScene.RequestNearbyFurniture());
            Assert.IsFalse(File.Exists(Path.Combine(root, LocalRoomCatalogStore.CatalogPath)));
        }
        else Assert.IsTrue(home.PorchScene.RequestNearbyFurniture(), "A valid catalog must reach the real doorway before the missing-ABI rejection.");
        Until(() => !host.LocalHandoffPending, 4500);
        Pump(100);
        if (!expectedProtected) Assert.IsNotNull(host.LocalRoomFailure, "The actual host must expose its failed entry result.");
        Assert.AreNotEqual(LocalRoomPresentation.Window, host.LocalPresentation, "A failed saved layout cannot be acknowledged as an empty playable room.");
        Assert.AreEqual(0, readyAcknowledgements, "The target room cannot issue even a transient healthy first-frame acknowledgement.");
        Assert.IsFalse(driver.IsReady);
        Assert.IsFalse(host.LocalWindow!.Scene.HasHealthyFrame);
        Assert.IsFalse(host.LocalWindow.IsFurnitureEditing);
        Assert.IsFalse(host.LocalWindow.BeginFurnitureEditing());
        if (expectedProtected) Assert.IsNull(runtime.Furniture, "A corrupt unpublished room cannot create a synthetic empty furniture session.");
        else
        {
            Assert.IsNotNull(runtime.Furniture);
            Assert.IsFalse(runtime.Furniture.CanEdit);
            Assert.IsFalse(runtime.Furniture.IsAttached);
            Assert.IsNotNull(runtime.Furniture.Layout.Platform, "The nonempty saved layout remains known; it is not replaced by a synthetic empty layout.");
        }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.IsFalse(File.Exists(FurnitureLayoutStore.GetLayoutPath(root)), "The rejected room cannot create or overwrite the independent trial namespace.");
        Assert.AreEqual(1, runtime.EntityIds.Count, "A failed entry does not fabricate another room Dango.");
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static void Until(Func<bool> condition, int timeout)
    {
        var clock = Stopwatch.StartNew();
        do { Pump(30); } while (!condition() && clock.ElapsedMilliseconds < timeout);
        Assert.IsTrue(condition(), "The real product-room host did not finish its failed-entry handshake before the bounded deadline.");
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
