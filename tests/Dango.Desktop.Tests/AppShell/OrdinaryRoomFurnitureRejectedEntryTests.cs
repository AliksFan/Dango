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
        var module = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE") ??
            Path.Combine(AppContext.BaseDirectory, "dango_belt_room.dll");
        RejectEntry(module, Encoding.UTF8.GetBytes("{\"schemaVersion\":999}"), expectedProtected: true);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void NonemptyLayoutWithLegacyModuleCannotEnterAnEmptyRoomOrRewriteItsLayout()
    {
        var module = Path.Combine(AppContext.BaseDirectory, Dango.Physics.DangoPhysicsWorld.NativeLibraryFileName);
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
            roomModulePath: module);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom);
        Assert.AreEqual(0, runtime.EntityIds.Count);
        host.ShowControlCenter(); Pump(250);
        var home = Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome;
        ProductHostTestSupport.WaitFor(() => home.DisplayedBeltSnapshot?.Frame is not null &&
            !Field<bool>(home, "roomCatalogRefreshing"), "Body presentation and the real catalog attempt must finish.");
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
        else
        {
            Assert.IsNotNull(home.CatalogSnapshot);
            Assert.IsTrue(home.PorchScene.RequestNearbyFurniture(), "A valid catalog must reach the real doorway before module rejection.");
            Until(() => host.LocalRoomFailure is not null, 4500);
        }
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
            Assert.IsNull(runtime.Furniture, "A module rejected before initialization must not create a synthetic furniture session.");
            Assert.IsNotNull(runtime.LastError);
        }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.IsFalse(File.Exists(FurnitureLayoutStore.GetLayoutPath(root)), "The rejected room cannot create or overwrite the independent trial namespace.");
        Assert.AreEqual(0, runtime.EntityIds.Count, "A rejected layout or module must never create a playable room body.");
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
