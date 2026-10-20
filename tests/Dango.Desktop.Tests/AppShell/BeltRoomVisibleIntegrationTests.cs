using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass, DoNotParallelize]
public sealed class BeltRoomVisibleIntegrationTests
{
    [STATestMethod, TestCategory("ForegroundIntegration")]
    public void ActualProductRoomAndDesktopPetHandoffsKeepOneBodyAndRealInputAndFiles()
    {
        if (Environment.GetEnvironmentVariable("DANGO_F7_SCREEN_AUTHORIZED") != "1")
            Assert.Inconclusive("Explicit screen authorization is required for this foreground check.");
        var module = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE")
            ?? throw new InvalidOperationException("An explicit independent room module is required.");
        var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "belt-room-visible", Guid.NewGuid().ToString("N"));
        var catalog = new LocalRoomCatalogStore(root);
        var initial = catalog.Refresh();
        Assert.IsTrue(initial.Success, initial.Warning);
        var id = initial.Catalog!.Rooms.Single().RoomId;
        var layout = FurnitureLayout.ForItems(1, Guid.NewGuid(),
            [new(Guid.NewGuid(), FurnitureItemKind.StaticPlatform, new(1440, 120)),
             new(Guid.NewGuid(), FurnitureItemKind.RoundBall, new(1800, 240))]);
        File.WriteAllBytes(catalog.LayoutPath(id), FurnitureLayoutSerializer.Serialize(layout, FurnitureLayoutContract.OrdinaryRoom, id));
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"),
            isolatedBeltBodyReview: true, beltRoomModulePath: module);
        host.ShowControlCenter();
        var homeWindow = Field<ControlCenterWindow>(host, "controlCenterWindow");
        var home = homeWindow.ProductHome;
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Wait(() => home.DisplayedBeltSnapshot is { Error: null, Frame: not null } &&
            home.CatalogSnapshot?.Find(id) is not null, host, runtime);
        var sourceTick = home.BoundBeltBody!.Metrics.NativeStepCalls;
        host.EnterCatalogRoom(id, false);
        Assert.IsTrue(homeWindow.IsVisible, "Keep the source until the target actually renders.");
        Wait(() => !host.LocalHandoffPending && host.LocalPresentation == LocalRoomPresentation.Window, host, runtime);
        Assert.IsFalse(homeWindow.IsVisible);
        Assert.IsFalse(home.BoundBeltBody.Metrics.Running);
        Assert.IsTrue(host.LocalWindow!.Scene.HasHealthyFrame);
        Assert.AreEqual(2, runtime.Furniture!.RenderItems.Count);
        var firstRoom = runtime.LastBeltRoomFrame!;
        var window = host.LocalWindow;
        window.Activate(); window.FocusScene(); Pump(150);
        Assert.AreEqual(new WindowInteropHelper(window).Handle, GetForegroundWindow());
        var before = runtime.CurrentSelectedCenter!.Value.X;
        try { keybd_event(0x44, 0, 0, 0); Pump(350); }
        finally { keybd_event(0x44, 0, 2, 0); }
        Pump(80);
        Assert.IsTrue(runtime.CurrentSelectedCenter!.Value.X > before + 1, "A real focused D key must drive the new body.");
        var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver");
        Assert.IsTrue(driver.Key(Key.Space, true, false),
            $"Ready={driver.IsReady}; owner={driver.OwnsRuntime}; blocked={window.Scene.InputBlocked}; " +
            $"pet={window.Scene.DesktopPetPresentationEnabled}; healthy={window.Scene.HasHealthyFrame}; " +
            $"runtime={runtime.FrameFaultState}; host={host.LocalRoomFailure}; nativeFault={runtime.LastBeltRoomFrame?.Fault}; " +
            $"scheduler={driver.FrameDiagnostics}");
        Pump(50); driver.Key(Key.Space, false, false);
        Pump(700);
        foreach (var preset in new[] { RoomViewPreset.Whole, RoomViewPreset.Near, RoomViewPreset.Standard })
            window.Scene.SelectView(preset);
        Pump(150);
        typeof(DangoApplicationHost).GetMethod("SaveRoomCheckpoint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, [true]);
        Wait(() => File.Exists(Path.Combine(root, "device", "rooms", id.ToString("D"), "resume.json")), host, runtime);
        Assert.AreEqual(layout, catalog.ReadLayout(id), "Motion must not rewrite saved furniture anchors.");
        var epoch = runtime.LastBeltRoomFrame!.OwnerEpoch;
        host.HideLocalRoom(); Pump(80);
        Assert.IsFalse(runtime.IsRunning);
        host.RestoreLocalRoom();
        Wait(() => !host.LocalHandoffPending && driver.IsReady, host, runtime);
        Assert.AreEqual(epoch, runtime.LastBeltRoomFrame!.OwnerEpoch, "Hide/restore retains this room owner.");
        host.ReturnToPorch();
        Wait(() => !host.LocalHandoffPending && host.LocalPresentation == LocalRoomPresentation.Porch &&
            home.BoundBeltBody.Metrics.Running && home.DisplayedBeltSnapshot is { Error: null }, host, runtime);
        Assert.IsTrue(homeWindow.IsVisible);
        Assert.IsNull(runtime.FrameDriver);
        Assert.IsTrue(home.BoundBeltBody.Metrics.NativeStepCalls > sourceTick);
        host.EnterCatalogRoom(id, false);
        Wait(() => !host.LocalHandoffPending && host.LocalPresentation == LocalRoomPresentation.Window, host, runtime);
        Assert.AreNotEqual(firstRoom.OwnerEpoch, runtime.LastBeltRoomFrame!.OwnerEpoch, "A fresh entry must not inherit another room's q/v.");
        host.ReturnToPorch();
        Wait(() => !host.LocalHandoffPending && host.LocalPresentation == LocalRoomPresentation.Porch, host, runtime);
        host.EnterDesktopPetRoom();
        Wait(() => !host.LocalHandoffPending && host.LocalPresentation == LocalRoomPresentation.DesktopPet, host, runtime);
        var pet = host.DesktopPetHost!;
        Assert.IsTrue(pet.IsReady, pet.LastSurfaceFailure);
        Assert.AreSame(runtime.LastFrame, pet.Scene.LastAppliedFrame);
        Assert.AreSame(pet.Scene.LastAppliedFrame, pet.PetWindow!.LastAppliedFrame);
        Assert.IsFalse(driver.Key(Key.D, true, false), "The desktop pet does not consume other applications' keys.");
        Assert.IsFalse(home.BoundBeltBody.Metrics.Running);
        host.ReturnToPorch();
        Wait(() => !host.LocalHandoffPending && host.LocalPresentation == LocalRoomPresentation.Porch &&
            home.BoundBeltBody.Metrics.Running, host, runtime);
        Assert.IsFalse(pet.IsVisible);
        Console.WriteLine($"Visible F7 room/desktop handoff and real key/file checks passed; layout items={layout.Items.Count}.");
    }

    private static T Field<T>(object value, string name) => (T)value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Wait(Func<bool> condition, DangoApplicationHost host, StackedDangoSceneRuntime runtime)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(12))
        {
            if (host.LocalRoomFailure is { } failure) Assert.Fail(failure);
            if (runtime.LastError is { } error) Assert.Fail(error);
            Pump(10);
        }
        Assert.IsTrue(condition(), host.LocalRoomFailure ?? runtime.LastError ?? "Actual host did not become ready.");
    }
    private static void Pump(int milliseconds)
    {
        var until = Stopwatch.StartNew();
        while (until.ElapsedMilliseconds < milliseconds)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
}
