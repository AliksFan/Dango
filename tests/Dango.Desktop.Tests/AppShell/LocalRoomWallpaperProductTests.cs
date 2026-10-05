using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class LocalRoomWallpaperProductTests
{
    public TestContext TestContext { get; set; } = null!;
    private uint diagnosticShellProcess;
    private string? diagnosticFailure;

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ActualWorkerWRoundTripKeepsEntityAndSourceUntilHealthyFrameAndReleasesWallpaperResources()
    {
        var output = Path.Combine(TestRepository.Root, "artifacts", "local-room", "wallpaper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var module = Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
        Assert.IsTrue(File.Exists(module), "The real isolated native module must be present; this integration test cannot be skipped.");
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(output, "settings.json"),
            nativeLibraryPath: module, isolatedLocalRoomReview: true);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        var session = Field<DesktopSceneSession>(host, "sceneSession");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter();
        Pump(350);
        var homeWindow = Field<ControlCenterWindow>(host, "controlCenterWindow");
        var home = homeWindow.ProductHome;
        home.ResumePorch();
        Pump(350);
        Assert.IsTrue(home.OwnsLiveFrameDriver, "The real porch must own the shared runtime before entry.");
        Assert.AreSame(runtime, home.BoundRuntime);
        Assert.AreSame(session, home.BoundSceneSession);
        var entity = runtime.EntityIds.Single();

        host.EnterLocalRoom();
        Assert.IsTrue(homeWindow.IsVisible, "Entering must retain the source before the first healthy room frame.");
        Assert.IsTrue(host.LocalHandoffPending);
        WaitForHandoff(host, "porch to window");
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        var room = host.LocalWindow!;
        var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver");
        var owner = Field<HostFrameScheduler>(driver, "scheduler");
        Assert.IsTrue(room.IsVisible);
        Assert.IsTrue(room.Scene.HasHealthyFrame);
        Assert.IsFalse(homeWindow.IsVisible);
        AssertSingleRoomOwner(runtime, driver, owner, homeWindow);
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        var roomSnapshot = session.Snapshot;
        var healthyAttempts = new List<long>();
        driver.HealthyFrameReady += healthyAttempts.Add;

        var wallpaper = Field<LocalRoomWallpaperHost>(host, "localRoomWallpaper");
        // Hold the actual target below the first-frame gate while its real WorkerW HWNDs are created.
        wallpaper.Scene.Visibility = Visibility.Hidden;
        host.SwitchLocalRoomPresentation(LocalRoomPresentation.Wallpaper);
        Assert.IsTrue(room.IsVisible, "Window source must survive a wallpaper target that has not rendered.");
        Assert.IsTrue(host.LocalHandoffPending, host.LocalRoomFailure);
        var source = NullableField<HwndSource>(wallpaper, "source");
        Assert.IsNotNull(source, "Actual WorkerW/HwndSource creation failed: " + host.LocalRoomFailure);
        var target = NullableField<WorkerWDesktopTarget>(wallpaper, "target");
        Assert.IsNotNull(target, "Actual Shell target is unavailable: " + host.LocalRoomFailure);
        var nativeHost = Field<WorkerWNativeHost>(wallpaper, "nativeHost");
        var wheel = Field<WallpaperWheelInputBridge>(wallpaper, "wheelBridge");
        var sourceHandle = source.Handle;
        var nativeHandle = nativeHost.Handle;
        Assert.IsTrue(NativeMethods.IsValidWindow(sourceHandle));
        Assert.IsTrue(NativeMethods.IsValidWindow(nativeHandle));
        Assert.IsTrue(NativeMethods.IsValidDesktopTarget(target));
        Assert.AreEqual(target.ParentHandle, ParentOf(nativeHandle), "The native host must actually be a Shell child.");
        Assert.AreEqual(nativeHandle, ParentOf(sourceHandle), "The WPF source must actually be a child of the native wallpaper host.");
        Assert.AreSame(wallpaper.Scene, source.RootVisual);
        Assert.AreSame(source, PresentationSource.FromVisual(wallpaper.Scene));
        Assert.AreNotEqual(nint.Zero, Field<nint>(wheel, "hook"), "The actual host path must install its native wheel observer.");
        Assert.IsFalse(wheel.IsHookReleasePending);
        AssertSingleRoomOwner(runtime, driver, owner, homeWindow);
        Pump(100);
        Assert.IsTrue(room.IsVisible, "Even a created HwndSource cannot retire the source before a healthy frame.");
        Assert.IsTrue(host.LocalHandoffPending, host.LocalRoomFailure);
        Assert.IsFalse(wallpaper.Scene.HasHealthyFrame);
        Assert.HasCount(0, healthyAttempts, "A hidden target cannot acknowledge a healthy frame.");

        // Activate only our own existing room window. No external application is selected or minimized.
        room.FocusScene();
        Pump(50);
        Assert.IsTrue(NativeMethods.IsLocalRoomForeground(new WindowInteropHelper(room).Handle),
            "The existing room window must be the real foreground for this exclusion check.");
        var screenBounds = Field<LogicalRect>(wallpaper, "screenBounds");
        var generation = Field<long>(wallpaper, "generation");
        var inputContext = new WallpaperWheelInputContext(target, screenBounds, generation, true, false);
        var point = new WallpaperWheelPoint((int)screenBounds.X + 20, (int)screenBounds.Y + 20);
        var inspected = NativeMethods.InspectRoomWheelInput(inputContext, point);
        Assert.IsTrue(inspected.IsKnown, "The foreground exclusion must be proven by a known query result.");
        Assert.IsFalse(inspected.HasModifier, "A held modifier cannot substitute for the foreground exclusion check.");
        Assert.IsFalse(inspected.IsDesktopForeground, "An owned foreground room window is still an application, not Shell.");
        Assert.IsFalse(WallpaperWheelInputPolicy.ShouldApply(inputContext, inputContext, point, inspected));

        wallpaper.Scene.Visibility = Visibility.Visible;
        wallpaper.Scene.UpdateLayout();
        WaitForHandoff(host, "window to wallpaper");
        Assert.AreEqual(LocalRoomPresentation.Wallpaper, host.LocalPresentation, host.LocalRoomFailure);
        Assert.IsNull(host.LocalRoomFailure);
        Assert.IsTrue(wallpaper.IsVisible);
        Assert.IsTrue(wallpaper.Scene.HasHealthyFrame);
        Assert.IsFalse(room.IsVisible);
        Assert.IsTrue(Field<bool>(wallpaper, "ready"));
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.AreSame(runtime, home.BoundRuntime);
        Assert.AreSame(session, home.BoundSceneSession);
        Assert.AreSame(roomSnapshot, session.Snapshot, "Changing presentation must not reattach or reset the room world.");
        Assert.HasCount(1, healthyAttempts);
        Assert.AreEqual(generation, healthyAttempts[0]);
        AssertSingleRoomOwner(runtime, driver, owner, homeWindow);
        CaptureScene(wallpaper.Scene, Path.Combine(output, "room-wallpaper.png"));

        // The reverse handoff also keeps its real wallpaper source until the new window frame is healthy.
        room.Scene.Visibility = Visibility.Hidden;
        host.SwitchLocalRoomPresentation(LocalRoomPresentation.Window);
        Assert.IsTrue(host.LocalHandoffPending);
        Assert.IsTrue(wallpaper.IsVisible);
        Assert.IsTrue(NativeMethods.IsValidWindow(sourceHandle));
        Assert.IsFalse(room.Scene.HasHealthyFrame, "A cached frame cannot satisfy the reverse handoff.");
        Assert.IsFalse(Field<bool>(wallpaper, "ready"), "Input must stop before the presentation handoff.");
        Assert.AreEqual(nint.Zero, Field<nint>(wheel, "hook"));
        Pump(100);
        Assert.IsTrue(host.LocalHandoffPending, host.LocalRoomFailure);
        Assert.IsTrue(wallpaper.IsVisible, "Wallpaper source must survive the target's withheld first frame.");
        Assert.IsTrue(NativeMethods.IsValidWindow(sourceHandle));
        AssertSingleRoomOwner(runtime, driver, owner, homeWindow);
        Assert.HasCount(1, healthyAttempts, "The hidden reverse target cannot acknowledge a cached frame.");

        room.Scene.Visibility = Visibility.Visible;
        room.Scene.UpdateLayout();
        WaitForHandoff(host, "wallpaper to window");
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        Assert.IsNull(host.LocalRoomFailure);
        Assert.IsTrue(room.IsVisible);
        Assert.IsTrue(room.Scene.HasHealthyFrame);
        Assert.IsFalse(wallpaper.IsVisible);
        Assert.IsFalse(NativeMethods.IsValidWindow(sourceHandle), "The old HwndSource must be released after the healthy target frame.");
        Assert.IsFalse(NativeMethods.IsValidWindow(nativeHandle), "The old native wallpaper host must be released.");
        Assert.IsFalse(wheel.IsHookReleasePending, "Normal disposal must release the native observer.");
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.AreSame(roomSnapshot, session.Snapshot);
        Assert.HasCount(2, healthyAttempts);
        Assert.IsTrue(healthyAttempts[1] > healthyAttempts[0]);
        AssertSingleRoomOwner(runtime, driver, owner, homeWindow);
        CaptureScene(room.Scene, Path.Combine(output, "room-window-restored.png"));
        TestContext.WriteLine("Real WorkerW round trip and same-entity/single-owner/healthy-first-frame checks passed.");
        TestContext.WriteLine("Scene-only visual evidence: " + output);

        host.Dispose();
        Assert.IsNull(runtime.FrameDriver, "Exit must release the shared runtime owner.");
        Assert.IsFalse(runtime.IsRunning);
        Assert.IsFalse(NativeMethods.IsValidWindow(sourceHandle));
        Assert.IsFalse(NativeMethods.IsValidWindow(nativeHandle));
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ActualNativeScanRecognizesBackgroundFullscreenWhileOwnRoomIsForeground()
    {
        var output = Path.Combine(TestRepository.Root, "artifacts", "local-room", "wallpaper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var module = Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
        Assert.IsTrue(File.Exists(module), "The real isolated native module must be present.");
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(output, "settings.json"),
            nativeLibraryPath: module, isolatedLocalRoomReview: true);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter(); Pump(350);
        host.EnterLocalRoom(); WaitForHandoff(host, "classification room window");
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        var wallpaper = Field<LocalRoomWallpaperHost>(host, "localRoomWallpaper");
        wallpaper.Scene.Visibility = Visibility.Hidden;
        host.SwitchLocalRoomPresentation(LocalRoomPresentation.Wallpaper);
        var target = NullableField<WorkerWDesktopTarget>(wallpaper, "target");
        Assert.IsNotNull(target, "The actual host must discover a real Shell target: " + host.LocalRoomFailure);
        CheckBackgroundFullscreenClassification(host.LocalWindow!, target);
    }

    private static void AssertSingleRoomOwner(StackedDangoSceneRuntime runtime, LocalRoomSceneDriver driver,
        HostFrameScheduler owner, ControlCenterWindow home)
    {
        Assert.IsTrue(driver.OwnsRuntime);
        Assert.AreSame(owner, runtime.FrameDriver, "Exactly the local-room scheduler must own the shared runtime.");
        Assert.IsFalse(home.ProductHome.OwnsLiveFrameDriver, "A visible suspended porch cannot drive a second frame owner.");
    }

    private static void WaitForHandoff(DangoApplicationHost host, string operation)
    {
        var watch = Stopwatch.StartNew();
        while (host.LocalHandoffPending && watch.Elapsed < TimeSpan.FromSeconds(2)) Pump(25);
        Assert.IsFalse(host.LocalHandoffPending, operation + " did not produce its real healthy frame: " + host.LocalRoomFailure);
    }

    private static T Field<T>(object instance, string name) =>
        (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)
            ?? throw new AssertFailedException("Required product field is unavailable: " + name));

    private static T? NullableField<T>(object instance, string name) where T : class =>
        (T?)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);

    private static nint ParentOf(nint handle) =>
        (nint)typeof(NativeMethods).GetMethod("GetParent", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [handle])!;

    private void CheckBackgroundFullscreenClassification(LocalRoomWindow room, WorkerWDesktopTarget target)
    {
        var monitor = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(screen => !screen.Primary)
            ?? System.Windows.Forms.Screen.PrimaryScreen!;
        var bounds = monitor.Bounds;
        var fictional = new Window
        {
            Title = "Fictional Dango fullscreen classification fixture",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Width = 320,
            Height = 240,
            Background = Brushes.Beige,
        };
        try
        {
            fictional.Show();
            var handle = new WindowInteropHelper(fictional).Handle;
            var positioned = (bool)typeof(NativeMethods).GetMethod("SetWindowPos", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [handle, nint.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, (uint)0x54])!;
            Assert.IsTrue(positioned, "The fictional background window must actually cover the selected monitor.");
            room.FocusScene();
            Pump(50);
            Assert.IsTrue(NativeMethods.IsLocalRoomForeground(new WindowInteropHelper(room).Handle));
            var actual = OwnWindowFrame(handle);
            Assert.IsTrue(FullscreenGeometryClassifier.IsFullscreen(actual,
                new DesktopPixelRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)));
            var processArguments = new object?[] { target.ShellViewHandle, (uint)0 };
            var thread = (uint)typeof(NativeMethods).GetMethod("GetWindowThreadProcessId", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, processArguments)!;
            Assert.AreNotEqual(0u, thread);
            var shellProcess = (uint)processArguments[1]!;
            Assert.AreNotEqual(0u, shellProcess);
            var scanArguments = new object?[] { shellProcess, false };
            var known = (bool)typeof(NativeMethods).GetMethod("TryHasRoomFullscreenApplication", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, scanArguments)!;
            if (!known) DiagnoseFullscreenQueryFailure(shellProcess);
            Assert.IsTrue(known, "The actual native fullscreen scan could not determine the environment. " + diagnosticFailure);
            Assert.IsTrue((bool)scanArguments[1]!, "The scanner must detect a full-monitor window while a regular room window is foreground.");
            TestContext.WriteLine("Native fullscreen scan detected a fullscreen window while the regular room was foreground and a fictional background fullscreen fixture was present.");
        }
        finally
        {
            fictional.Close();
            room.FocusScene();
        }
    }

    private static DesktopPixelRect OwnWindowFrame(nint handle)
    {
        var rectangleType = typeof(NativeMethods).GetNestedType("NativeRect", BindingFlags.NonPublic)!;
        var arguments = new object?[] { handle, (uint)9, Activator.CreateInstance(rectangleType), 16 };
        var result = (int)typeof(NativeMethods).GetMethod("GetRoomWindowFrame", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        Assert.AreEqual(0, result, "The fictional window's actual DWM frame rectangle must be readable.");
        var rectangle = arguments[2]!;
        int Coordinate(string name) => (int)rectangleType.GetField(name)!.GetValue(rectangle)!;
        return new DesktopPixelRect(Coordinate("Left"), Coordinate("Top"), Coordinate("Right"), Coordinate("Bottom"));
    }

    private void DiagnoseFullscreenQueryFailure(uint shellProcess)
    {
        diagnosticShellProcess = shellProcess;
        diagnosticFailure = null;
        var callbackType = typeof(NativeMethods).GetNestedType("EnumWindowsCallback", BindingFlags.NonPublic)!;
        var callback = Delegate.CreateDelegate(callbackType, this,
            GetType().GetMethod(nameof(InspectClassifierWindow), BindingFlags.Instance | BindingFlags.NonPublic)!);
        _ = typeof(NativeMethods).GetMethod("EnumWindows", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [callback, nint.Zero]);
    }

    private bool InspectClassifierWindow(nint handle, nint parameter)
    {
        // Diagnose only the classifier's required transient metadata. Never query a window title or its contents.
        object? Invoke(string method, params object?[] arguments) =>
            typeof(NativeMethods).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments);
        if (!(bool)Invoke("IsWindowVisible", handle)! || (bool)Invoke("IsRoomWindowMinimized", handle)!) return true;
        var identity = new object?[] { handle, string.Empty, (uint)0 };
        if (!(bool)Invoke("TryGetRoomWindowIdentity", identity)!)
        {
            diagnosticFailure = "Visible-window identity query failed.";
            return false;
        }
        var className = (string)identity[1]!;
        if ((uint)identity[2]! == diagnosticShellProcess && WallpaperWheelInputPolicy.IsShellClass(className)) return true;
        var cloaked = new object?[] { handle, (uint)14, 0, sizeof(int) };
        var cloakedResult = (int)Invoke("GetRoomWindowCloaked", cloaked)!;
        if (cloakedResult != 0)
        {
            diagnosticFailure = $"DwmGetWindowAttribute(DWMWA_CLOAKED) failed with HRESULT={cloakedResult}.";
            return false;
        }
        if ((int)cloaked[2]! != 0) return true;
        var rectangleType = typeof(NativeMethods).GetNestedType("NativeRect", BindingFlags.NonPublic)!;
        var rectangleArguments = new object?[] { handle, (uint)9, Activator.CreateInstance(rectangleType), 16 };
        var rectangleResult = (int)Invoke("GetRoomWindowFrame", rectangleArguments)!;
        var rectangle = rectangleArguments[2]!;
        int Coordinate(string name) => (int)rectangleType.GetField(name)!.GetValue(rectangle)!;
        if (rectangleResult != 0 || Coordinate("Right") <= Coordinate("Left") || Coordinate("Bottom") <= Coordinate("Top"))
        {
            diagnosticFailure = rectangleResult != 0
                ? $"DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) failed with HRESULT={rectangleResult}."
                : Coordinate("Right") < Coordinate("Left") || Coordinate("Bottom") < Coordinate("Top")
                    ? "DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) returned an invalid negative rectangle."
                    : "DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) succeeded but returned a zero-area rectangle.";
            return false;
        }
        return true;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void CaptureScene(LocalRoomSceneView scene, string path)
    {
        Assert.IsTrue(scene.HasHealthyFrame);
        Assert.IsTrue(scene.ActualWidth > 0 && scene.ActualHeight > 0);
        // Render only this room's visual tree. Never capture its desktop parent or another window.
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(scene.ActualWidth), (int)Math.Ceiling(scene.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(scene), null, new Rect(0, 0, scene.ActualWidth, scene.ActualHeight));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Assert.IsTrue(new FileInfo(path).Length > 0);
    }
}
