using System.IO;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.Persistence;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class LocalRoomSaveIntegrationTests
{
    [STATestMethod]
    public void DisposalHonorsAnAlreadyHandledExitWithoutTryingToSaveAgain()
    {
        var settingsPath = Path.Combine(NewRoot(), "settings.json");
        var path = new DangoUserDataPaths(settingsPath).LocalRoomSavePath;
        var initial = new LocalRoomSaveSession(path);
        Assert.IsTrue(initial.TrySave());
        var original = File.ReadAllBytes(path);
        using var host = new DangoApplicationHost(settingsFile: settingsPath, isolatedLocalRoomReview: true);
        host.ShowControlCenter();
        host.LocalRoomSaves.UpdateRoom(host.LocalRoomSaves.OrdinaryRoom with { ResumeCenterX = 1200 });
        // RequestExit sets this only after the user has handled the save/retry/no-save exit decisions.
        typeof(DangoApplicationHost).GetField("exitSettingsHandled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, true);
        host.Dispose();
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void RealDesktopPetHostSavesItsOwnPositionAndRestoresItOnOfflineColdRestart()
    {
        var settingsPath = Path.Combine(NewRoot(), "settings.json");
        var save = new LocalRoomSaveSession(new DangoUserDataPaths(settingsPath).LocalRoomSavePath);
        save.UpdateRoom(save.OrdinaryRoom with { ResumeCenterX = 1250 });
        save.UpdateRoom(save.DesktopPetRoom with { ResumeCenterX = 400 });
        Assert.IsTrue(save.TrySave());
        double savedX;
        Guid roomId;
        using (var host = CreateHost(settingsPath))
        {
            EnterDesktopPetRoom(host);
            var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
            var center = runtime.CurrentSelectedCenter!.Value;
            Assert.AreEqual(400d, center.X, 5);
            var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver");
            var scene = host.DesktopPetHost!.Scene;
            var start = scene.RoomToScene(new(center.X, center.Y + runtime.NeutralBodyGroundOffset));
            var target = scene.RoomToScene(new(center.X + 145, center.Y + runtime.NeutralBodyGroundOffset));
            Assert.IsTrue(driver.PointerDown(new(start.X, start.Y), nowMilliseconds: 0));
            Assert.IsTrue(driver.PointerMove(new(target.X, target.Y), nowMilliseconds: 20));
            var until = Environment.TickCount64 + 2000;
            while (runtime.CurrentSelectedCenter!.Value.X < center.X + 80 && Environment.TickCount64 < until) Pump(20);
            Assert.IsTrue(runtime.CurrentSelectedCenter!.Value.X > center.X + 80);
            Assert.IsTrue(driver.PointerUp(new(target.X, target.Y), nowMilliseconds: 500));
            Pump(400);
            host.HideLocalRoom();
            savedX = host.LocalRoomSaves.DesktopPetRoom.ResumeCenterX!.Value;
            roomId = host.LocalRoomSaves.DesktopPetRoom.RoomId;
            Assert.IsTrue(savedX > 405, "Actual desktop-pet movement must replace its own checkpoint.");
            Assert.AreEqual(1250d, host.LocalRoomSaves.OrdinaryRoom.ResumeCenterX);
            Assert.IsFalse(host.LocalRoomSaves.HasPendingSave, host.LocalRoomSaves.StatusMessage);
        }
        using var cold = CreateHost(settingsPath);
        EnterDesktopPetRoom(cold);
        Assert.AreEqual(roomId, cold.LocalRoomSaves.DesktopPetRoom.RoomId);
        Assert.AreEqual(savedX, Field<StackedDangoSceneRuntime>(cold, "stackedRuntime").CurrentSelectedCenter!.Value.X, 5);
        Assert.AreEqual(1250d, cold.LocalRoomSaves.OrdinaryRoom.ResumeCenterX);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ActualWindowSavesAtHideAndOfflineColdHostRestoresPositionAndManualCamera()
    {
        var root = NewRoot();
        var settingsPath = Path.Combine(root, "settings.json");
        double savedX;
        LocalRoomCameraSaveData camera;
        Guid roomId;
        using (var host = CreateHost(settingsPath))
        {
            host.EnterLocalRoom(); WaitForRoom(host);
            var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver");
            Assert.IsTrue(driver.Key(Key.D, true, false)); Pump(180);
            Assert.IsTrue(driver.Key(Key.D, false, false));
            host.LocalWindow!.Scene.SelectCameraMode(RoomCameraMode.Manual);
            Assert.IsTrue(host.LocalWindow.Scene.PanCamera(-120, 20));
            camera = host.LocalWindow.Scene.Camera.CaptureSaveData();
            host.HideLocalRoom();
            Assert.IsFalse(host.LocalRoomSaves.HasPendingSave, host.LocalRoomSaves.StatusMessage);
            savedX = host.LocalRoomSaves.OrdinaryRoom.ResumeCenterX!.Value;
            roomId = host.LocalRoomSaves.OrdinaryRoom.RoomId;
            Assert.IsTrue(savedX > LocalRoomSceneView.WorldWidth / 2 + 5);
            Assert.IsNull(Field<StackedDangoSceneRuntime>(host, "stackedRuntime").FrameDriver);
        }
        // A new host reads only this isolated directory. There is no service/login initialization in local startup.
        using var cold = CreateHost(settingsPath);
        cold.EnterLocalRoom(); WaitForRoom(cold);
        Assert.AreEqual(roomId, cold.LocalRoomSaves.OrdinaryRoom.RoomId);
        Assert.AreEqual(savedX, Field<StackedDangoSceneRuntime>(cold, "stackedRuntime").CurrentSelectedCenter!.Value.X, 5);
        Assert.AreEqual(camera, cold.LocalWindow!.Scene.Camera.CaptureSaveData());
        Assert.IsFalse(cold.LocalHandoffPending);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void FailedFirstFrameAndDisposalDoNotReplaceExistingHealthyCheckpoint()
    {
        var settingsPath = Path.Combine(NewRoot(), "settings.json");
        var savePath = new DangoUserDataPaths(settingsPath).LocalRoomSavePath;
        var save = new LocalRoomSaveSession(savePath);
        save.UpdateRoom(save.OrdinaryRoom with { ResumeCenterX = 1250 });
        Assert.IsTrue(save.TrySave());
        var original = File.ReadAllBytes(savePath);
        using (var host = CreateHost(settingsPath))
        {
            host.LocalWindow!.Scene.Visibility = System.Windows.Visibility.Hidden;
            host.EnterLocalRoom(); Pump(3400);
            Assert.IsNotNull(host.LocalRoomFailure);
            Assert.IsFalse(host.LocalHandoffPending);
        }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(savePath));
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ChangedWorldBoundsUseSafeGroundEntryWithoutDeletingSavedOtherRoom()
    {
        var settingsPath = Path.Combine(NewRoot(), "settings.json");
        var save = new LocalRoomSaveSession(new DangoUserDataPaths(settingsPath).LocalRoomSavePath);
        save.UpdateRoom(save.OrdinaryRoom with { ResumeCenterX = 99999 });
        save.UpdateRoom(save.DesktopPetRoom with { ResumeCenterX = 300 });
        Assert.IsTrue(save.TrySave());
        using var host = CreateHost(settingsPath);
        host.EnterLocalRoom(); WaitForRoom(host);
        var center = Field<StackedDangoSceneRuntime>(host, "stackedRuntime").CurrentSelectedCenter!.Value;
        Assert.AreEqual(LocalRoomSceneView.WorldWidth / 2, center.X, 5);
        Assert.AreEqual(300d, host.LocalRoomSaves.DesktopPetRoom.ResumeCenterX);
        Assert.IsTrue(center.Y > 200, "Ground entry must not restore a transient jump height.");
    }

    private static DangoApplicationHost CreateHost(string path)
    {
        var host = new DangoApplicationHost(settingsFile: path, isolatedLocalRoomReview: true);
        Assert.IsTrue(Field<StackedDangoSceneRuntime>(host, "stackedRuntime").TryInitialize());
        host.ShowControlCenter(); Pump(300);
        return host;
    }
    private static void WaitForRoom(DangoApplicationHost host)
    {
        var until = Environment.TickCount64 + 4000;
        while (host.LocalHandoffPending && Environment.TickCount64 < until) Pump(20);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        Assert.IsNull(host.LocalRoomFailure);
    }
    private static void EnterDesktopPetRoom(DangoApplicationHost host)
    {
        host.EnterDesktopPetRoom();
        var until = Environment.TickCount64 + 4000;
        while (host.LocalHandoffPending && Environment.TickCount64 < until) Pump(20);
        Assert.AreEqual(LocalRoomPresentation.DesktopPet, host.LocalPresentation, host.LocalRoomFailure);
        Assert.IsTrue(host.DesktopPetHost!.IsReady);
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static string NewRoot()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "local-room", "b2-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); return root;
    }
    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
}
