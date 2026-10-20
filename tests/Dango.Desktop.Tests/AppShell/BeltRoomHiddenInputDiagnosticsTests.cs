using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass, DoNotParallelize]
public sealed class BeltRoomHiddenInputDiagnosticsTests
{
    [STATestMethod, TestCategory("NativeIntegration")]
    [DataRow(12)]
    [DataRow(21)]
    [DataRow(30)]
    [DataRow(60)]
    public void SameLayoutDReleaseThenSpaceRecordsTheActualHiddenDriverState(int warmup)
    {
        var module = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE")
            ?? throw new InvalidOperationException("An explicit independent room module is required.");
        var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "belt-hidden-key", Guid.NewGuid().ToString("N"));
        var catalog = new LocalRoomCatalogStore(root);
        var room = catalog.Refresh().Catalog!.Rooms.Single();
        var layout = FurnitureLayout.ForItems(1, Guid.NewGuid(),
            [new(Guid.NewGuid(), FurnitureItemKind.StaticPlatform, new(1440, 120)),
             new(Guid.NewGuid(), FurnitureItemKind.RoundBall, new(1800, 240))]);
        File.WriteAllBytes(catalog.LayoutPath(room.RoomId), FurnitureLayoutSerializer.Serialize(layout, FurnitureLayoutContract.OrdinaryRoom, room.RoomId));
        using var runtime = StackedDangoSceneRuntime.CreateBeltRoom(module);
        runtime.ConfigureBeltRoomAppearance(DangoAppearanceConfiguration.Default, beginRoom: true);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var source = new ManualFrames(); long ticks = 0;
        var view = new LocalRoomSceneView(); view.EnableWindowCamera();
        using var hidden = new HwndSource(new HwndSourceParameters("Fictional hidden F7 D release Space diagnostic")
        { Width = 960, Height = 600, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = view };
        Assert.IsFalse(IsWindowVisible(hidden.Handle));
        view.Measure(new(960, 600)); view.Arrange(new(0, 0, 960, 600)); view.UpdateLayout();
        Assert.IsTrue(view.IsVisible, "The hidden HwndSource must meet the real driver's visibility condition.");
        using var driver = new LocalRoomSceneDriver(new DesktopSceneSession(), runtime, source, () => ticks, TimeSpan.TicksPerSecond);
        driver.BindRoom(catalog, room, null);
        var scheduler = Field<HostFrameScheduler>(driver, "scheduler");
        var failures = new List<string>();
        driver.Failed += (attempt, failure) => failures.Add($"generation={attempt} tick={runtime.LastBeltRoomFrame?.Tick} {failure}");
        void Log(string stage) => Console.WriteLine($"{stage}: warmup={warmup}; own={scheduler.OwnsRuntime}; ready={driver.IsReady}; readySent={Field<bool>(driver, "readySent")}; blocked={view.InputBlocked}; pet={view.DesktopPetPresentationEnabled}; visible={view.IsVisible}; healthy={view.HasHealthyFrame}; running={runtime.IsRunning}; error={runtime.LastError ?? "none"}; nativeFault={runtime.LastBeltRoomFrame?.Fault}; nativeTick={runtime.LastBeltRoomFrame?.Tick}; scheduler={scheduler.Snapshot}; hostFailures={string.Join(" | ", failures)}");
        void Advance(int count)
        {
            for (var i = 0; i < count; i++) { ticks += HostFrameClock.FixedStep.Ticks; source.Raise(ticks); Pump(1); }
        }
        driver.Start(view, 1, true);
        Advance(warmup);
        var bitmap = new RenderTargetBitmap(960, 600, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var clock = Stopwatch.StartNew(); while (!driver.IsReady && clock.ElapsedMilliseconds < 3000) Pump(5);
        Log("before D");
        Assert.IsTrue(driver.IsReady);
        var before = runtime.CurrentSelectedCenter!.Value.X;
        var dDown = driver.Key(Key.D, true, false); Log("D down admitted=" + dDown);
        Advance(20);
        var dUp = driver.Key(Key.D, false, false); Log("D up admitted=" + dUp);
        Advance(5);
        var space = driver.Key(Key.Space, true, false); Log("Space admitted=" + space);
        driver.Key(Key.Space, false, false);
        Assert.IsTrue(runtime.CurrentSelectedCenter!.Value.X > before + 1, "The real body must have moved.");
        Assert.IsTrue(dDown && dUp && space, "See recorded driver admission state.");
        Assert.IsNull(runtime.LastError, string.Join(" | ", failures));
        Assert.AreEqual(0, failures.Count, string.Join(" | ", failures));
        Assert.IsFalse(IsWindowVisible(hidden.Handle), "No stage may show or activate this verification surface.");
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private sealed class ManualFrames : IHostFrameSource
    {
        public event EventHandler? Rendering;
        internal void Raise(long ticks) => Rendering?.Invoke(this,
            (EventArgs)Activator.CreateInstance(typeof(RenderingEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { TimeSpan.FromTicks(ticks) }, null)!);
    }
    private static void Pump(int milliseconds)
    {
        var until = Stopwatch.StartNew();
        while (until.ElapsedMilliseconds < milliseconds)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(1);
        }
    }
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
}
