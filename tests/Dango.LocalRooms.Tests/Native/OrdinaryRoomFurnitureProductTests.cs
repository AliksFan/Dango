using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Dango.Core.Furniture;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Furniture;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.LocalRooms.Tests.Native;

[TestClass]
public sealed class OrdinaryRoomFurnitureProductTests
{
    [STATestMethod]
    public void PorchFurnitureRequestRequiresNearbyLocalOrdinaryDoor()
    {
        var porch = new PorchSceneView();
        var requests = 0;
        porch.LocalFurnitureRequested += () => requests++;
        Assert.IsFalse(porch.RequestNearbyFurniture());
        porch.SetLocalRoomEnabled(true);
        Assert.IsTrue(porch.RequestNearbyFurniture());
        Assert.AreEqual(1, requests);
        porch.ResetProjection(1100);
        Assert.IsFalse(porch.RequestNearbyFurniture());
        porch.SetDesktopPetRoomEnabled(true);
        porch.ResetProjection(780);
        Assert.IsFalse(porch.RequestNearbyFurniture());
        porch.SetReviewMode(true);
        porch.ResetProjection(560);
        Assert.IsFalse(porch.RequestNearbyFurniture());
        Assert.AreEqual(1, requests);
    }

    [TestMethod]
    public void EditingCameraRestoresExactManualViewAndRespectsLaterViewportBounds()
    {
        var camera = new RoomViewController();
        camera.EnableWindowCamera();
        camera.Resize(1000, 700, 360, 260);
        camera.Update(1000, 0, true);
        camera.SelectCameraMode(RoomCameraMode.Manual);
        camera.ApplyZoomSteps(-2);
        camera.PanByScreenDelta(-90, -25);
        var before = camera.CaptureState();
        var scale = camera.Scale;
        camera.SelectPreset(RoomViewPreset.Whole);
        camera.RestoreState(before);
        Assert.AreEqual(before.Mode, camera.CameraMode);
        Assert.AreEqual(before.Preset, camera.Preset);
        Assert.AreEqual(scale, camera.Scale, 1e-9);
        Assert.AreEqual(before.X, camera.CameraX, 1e-9);
        Assert.AreEqual(before.Y, camera.CameraY, 1e-9);
        camera.Resize(1200, 700, 360, 260);
        camera.RestoreState(before);
        Assert.IsTrue(camera.CameraX >= 0 && double.IsFinite(camera.Projection.OffsetY));
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ActualProductRoomPreservesWorldAndAppearanceAcrossEditSaveHideAndPorch()
    {
        var root = NewRoot();
        var accessories = new AccessorySettingsSession(root);
        var outfit = new AccessoryConfiguration([
            new("fictional-furniture-front", "Fictional front", "builtin-top-hat", 64, 64)
            { Layer = AccessoryLayer.Front, Softness = 80, GravityInfluence = 60, ConnectionStrength = 35 },
            new("fictional-furniture-rear", "Fictional rear", "builtin-sprout", 64, 64)
            { Layer = AccessoryLayer.Behind, BodyAnchorY = .3, Softness = 65, GravityInfluence = 40 },
        ]);
        accessories.Stage(outfit);
        Assert.IsTrue(accessories.TrySave(), accessories.StatusMessage);
        using var host = CreateHost(root);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom, "Product rooms must use the continuous body module.");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host); Pump(250);
        var home = Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome;
        var catalog = new LocalRoomCatalogStore(root);
        var roomId = catalog.ReadCurrent().Rooms.Single(room => room.Life == LocalRoomLife.Active).RoomId;
        ProductHostTestSupport.ResumePorchAtCatalogRoomDoor(home, roomId);
        Assert.IsTrue(home.PorchScene.RequestNearbyFurniture());
        Until(() => !host.LocalHandoffPending, 4500);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        var window = host.LocalWindow!;
        Assert.IsTrue(window.IsFurnitureEditing, runtime.Furniture?.Status);
        var furniture = runtime.Furniture!;
        Assert.IsTrue(furniture.CanEdit, furniture.Status);
        Assert.HasCount(2, runtime.GetAccessoryFrames(runtime.LastFrame!, 0), "Real equipped images must render in the same product room.");
        Assert.IsNull(runtime.AccessoryError);
        Assert.IsNull(runtime.AttachmentError);
        var generation = furniture.WorldGeneration;
        var roomEpoch = runtime.LastBeltRoomFrame!.OwnerEpoch;
        var entity = runtime.EntityIds.Single();
        var appearance = runtime.DisplayHatAppearance;
        var selectedCenter = runtime.CurrentSelectedCenter;
        var physicsStep = furniture.PhysicsStep;
        Assert.IsTrue(window.EndFurnitureEditing());
        Assert.IsTrue(window.BeginFurnitureEditing());
        Assert.AreEqual(generation, furniture.WorldGeneration);
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.AreEqual(selectedCenter, runtime.CurrentSelectedCenter, "Edit toggles must not rehost.");
        Assert.AreEqual(physicsStep, furniture.PhysicsStep, "Edit controls must not secretly Step.");

        furniture.PreviewPlatform(1440, 120);
        Assert.IsTrue(furniture.CanConfirm, furniture.Status);
        var revision = furniture.SceneRevision;
        var request = furniture.ConfirmAsync();
        Until(() => request.IsCompleted, 5000);
        Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, furniture.State, furniture.Status);
        Assert.AreEqual(1L, furniture.Layout.LayoutVersion);
        Assert.HasCount(1, furniture.NativeFrame!.Items);
        Assert.AreEqual(Dango.Physics.DangoFurnitureKind.StaticPlatform, furniture.NativeFrame.Items.Single().Definition.Kind);
        Assert.AreEqual(generation, furniture.WorldGeneration);
        Assert.IsTrue(furniture.SceneRevision > revision);
        var path = catalog.LayoutPath(roomId);
        var bytes = File.ReadAllBytes(path);
        Assert.AreEqual(new FurnitureAnchor(1440, 120), catalog.CreateLayoutStore(roomId).Load(path).Layout!.Platform!.Anchor);
        Assert.IsFalse(File.Exists(FurnitureLayoutStore.GetLayoutPath(root)), "Trial layout must remain separate.");

        var anchor = furniture.Layout.Platform!.Anchor;
        var view = window.Scene;
        foreach (var preset in new[] { RoomViewPreset.Whole, RoomViewPreset.Standard, RoomViewPreset.Near })
        {
            view.SelectView(preset);
            var roundTrip = view.ScreenToFurnitureAnchor(view.AnchorToScreen(anchor));
            Assert.AreEqual(anchor.X, roundTrip.X, 1e-8);
            Assert.AreEqual(anchor.Y, roundTrip.Y, 1e-8);
        }
        for (var i = 0; i < 50; i++)
        {
            Assert.IsTrue(window.EndFurnitureEditing());
            Assert.IsTrue(window.BeginFurnitureEditing());
            Assert.AreEqual(generation, furniture.WorldGeneration);
            Assert.AreEqual(entity, runtime.EntityIds.Single());
        }
        Assert.IsTrue(window.EndFurnitureEditing());
        host.HideLocalRoom();
        Assert.IsNull(runtime.FrameDriver);
        Assert.IsFalse(runtime.IsRunning);
        host.RestoreLocalRoom(); Until(() => !host.LocalHandoffPending, 4500);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        Assert.HasCount(1, furniture.NativeFrame!.Items);
        host.ReturnToPorch(); Until(() => !host.LocalHandoffPending, 4500);
        Assert.AreEqual(LocalRoomPresentation.Porch, host.LocalPresentation, host.LocalRoomFailure);
        Assert.HasCount(0, furniture.NativeFrame!.Items, "Furniture cannot pollute porch.");
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path), "Runtime detach must never save an empty layout.");
        host.EnterLocalRoom(); Until(() => host.LocalPresentation == LocalRoomPresentation.Window && !host.LocalHandoffPending, 4500);
        furniture = runtime.Furniture!;
        Assert.HasCount(1, furniture.NativeFrame!.Items, furniture.Status);
        Assert.AreEqual(anchor, furniture.Layout.Platform!.Anchor);
        Assert.AreNotEqual(roomEpoch, runtime.LastBeltRoomFrame!.OwnerEpoch,
            "A fresh admission must rebuild the physical owner from the saved layout.");
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.AreEqual(appearance, runtime.DisplayHatAppearance);
        Assert.HasCount(2, runtime.GetAccessoryFrames(runtime.LastFrame!, 0));
        Assert.IsNull(runtime.AccessoryError);
        Assert.AreEqual(outfit, new AccessorySettingsSession(root).SavedConfiguration);
        furniture.SelectFurniture(furniture.Layout.Items.Single().InstanceId);
        var deletion = furniture.DeleteAsync(); Until(() => deletion.IsCompleted, 5000);
        Assert.HasCount(0, furniture.NativeFrame!.Items);
        Assert.AreEqual(2L, furniture.Layout.LayoutVersion);
        Assert.IsTrue(furniture.CanStep);
        host.ReturnToPorch(); Until(() => !host.LocalHandoffPending, 4500);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ExistingRoomLayoutLoadsOnFreshProductHostWithoutTrialLayoutOrCopiedPet()
    {
        var root = NewRoot();
        var layout = new FurnitureLayout(3, Guid.NewGuid(), new(Guid.NewGuid(), new(1440, 120)));
        var path = FurnitureLayoutStore.GetLayoutPath(root, FurnitureLayoutContract.OrdinaryRoom);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, FurnitureLayoutSerializer.Serialize(layout, FurnitureLayoutContract.OrdinaryRoom));
        using var host = CreateHost(root);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom, "Product rooms must use the continuous body module.");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host); Pump(250);
        host.EnterLocalRoom(); Until(() => !host.LocalHandoffPending, 4500);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        Assert.AreEqual(layout, runtime.Furniture!.Layout);
        Assert.HasCount(1, runtime.Furniture.NativeFrame!.Items);
        Assert.AreEqual(Dango.Physics.DangoFurnitureKind.StaticPlatform, runtime.Furniture.NativeFrame.Items.Single().Definition.Kind);
        Assert.AreEqual(1, runtime.EntityIds.Count);
        Assert.AreEqual(3L, FurnitureLayoutSerializer.Deserialize(File.ReadAllBytes(path), FurnitureLayoutContract.OrdinaryRoom).LayoutVersion);
        host.ReturnToPorch(); Until(() => !host.LocalHandoffPending, 4500);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ARealLeaseHeldBeforeEditorAdmissionBlocksEntryWithoutChangingTheWorld()
    {
        var root = NewRoot();
        using var host = CreateHost(root);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom, "Product rooms must use the continuous body module.");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host); Pump(250);
        host.EnterLocalRoom(); Until(() => !host.LocalHandoffPending, 4500);
        var furniture = runtime.Furniture!;
        var generation = furniture.WorldGeneration;
        var frame = runtime.LastPhysicsFrame;
        var catalog = new LocalRoomCatalogStore(root);
        var roomId = catalog.ReadCurrent().Rooms.Single().RoomId;
        // Hold the content lease directly: the editor must also acquire this exact room's lease.
        var store = new FurnitureLayoutStore(contract: FurnitureLayoutContract.OrdinaryRoom, roomId: roomId);
        var path = catalog.LayoutPath(roomId);
        var original = File.ReadAllBytes(path);
        using (store.AcquireLease(path))
        {
            Assert.IsFalse(host.LocalWindow!.BeginFurnitureEditing(), "Another process lease must block admission before a preview is created.");
            Assert.IsFalse(host.LocalWindow.IsFurnitureEditing);
            Assert.IsFalse(furniture.HasPreview);
            Assert.AreSame(frame, runtime.LastPhysicsFrame);
            Assert.AreEqual(generation, furniture.WorldGeneration);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        }
        Assert.IsTrue(host.LocalWindow!.BeginFurnitureEditing(), furniture.Status);
        host.ReturnToPorch(); Until(() => !host.LocalHandoffPending, 4500);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ActualProductDisposalReleasesHomeBindingsBeforeQueuedUnloadedCallbacks()
    {
        using var host = CreateHost(NewRoot());
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom, "Product rooms must use the continuous body module.");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host); Pump(250);
        var home = Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome;
        host.EnterLocalRoom(); Until(() => !host.LocalHandoffPending, 4500);
        host.Dispose();
        Assert.IsNull(home.BoundRuntime, "Queued Home.Unloaded cannot retain an already-disposed native owner.");
        Assert.IsNull(home.BoundBeltBody, "Disposal must also release the actual home worker.");
        Assert.IsNull(home.BoundSceneSession);
        Assert.IsNull(runtime.FrameDriver);
        Assert.IsFalse(runtime.IsRunning);
        Pump(100); // Actual WPF Unloaded and pending render callbacks must complete without exceptions.
    }

    private static string NewRoot()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "furniture-room", "product-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
    private static DangoApplicationHost CreateHost(string root) => new(settingsFile: Path.Combine(root, "settings.json"),
        roomModulePath: Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE") ??
            Path.Combine(AppContext.BaseDirectory, "dango_belt_room.dll"));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static void Until(Func<bool> condition, int timeout)
    {
        var watch = Stopwatch.StartNew();
        do { Pump(30); } while (!condition() && watch.ElapsedMilliseconds < timeout);
        Assert.IsTrue(condition(), "Product-room condition timed out.");
    }
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
