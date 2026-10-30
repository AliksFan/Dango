using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.Rendering.Visuals;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessorySimulationWindowIntegrationTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void OffscreenNonActivatingEditorConnectsRealWebViewControlsNativeSpaceAndUndoWithoutSaving()
    {
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var pixels = new byte[48 * 32 * 4];
        for (var y = 8; y < 24; y++) for (var x = 12; x < 36; x++)
        { var i = (y * 48 + x) * 4; pixels[i] = 42; pixels[i + 1] = 85; pixels[i + 2] = 165; pixels[i + 3] = 255; }
        var bitmap = BitmapSource.Create(48, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 48 * 4); bitmap.Freeze();
        var source = AccessoryEditorSourceAsset.FromBitmap(bitmap);
        var original = new AccessoryItem("offscreen-mode-item", "虚构模式验收", source.AssetId, 48, 32) { IsEquipped = false, Order = 4 };
        var window = new AccessoryUnifiedEditorWindow(new("offscreen-mode-session", 1, original.Id, 1, original), source, null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false,
        };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var session = (AccessoryUnifiedEditorSession)typeof(AccessoryUnifiedEditorWindow).GetField("session", flags)!.GetValue(window)!;
        var browser = (AccessoryEditorBrowserHost)typeof(AccessoryUnifiedEditorWindow).GetField("browser", flags)!.GetValue(window)!;
        var simulation = (AccessorySimulationView)typeof(AccessoryUnifiedEditorWindow).GetField("simulation", flags)!.GetValue(window)!;
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            // Real HWND/WebView2, outside the virtual desktop, with no activation or OS input.
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            Assert.IsTrue(GetWindowRect(handle, out var bounds)); Assert.IsTrue(bounds.Right < -10000 && bounds.Bottom < -10000);
            Assert.AreNotEqual(handle, GetForegroundWindow());
            Wait(() => session.State is AccessoryUnifiedEditorState.Ready or AccessoryUnifiedEditorState.Failed);
            Assert.AreEqual(AccessoryUnifiedEditorState.Ready, session.State, session.ErrorMessage);
            var core = browser.Runtime.Core!;
            string Script(string script) => Pump(core.ExecuteScriptAsync(script));
            Wait(() => Script("Boolean(window.DangoMiniPaint?.getWearParameters())") == "true");
            var priorImage = Script("DangoMiniPaint.exportPng().pngBase64");
            Script("document.getElementById('dynamic_preview_tab').click()");
            Wait(() => simulation.IsActive && simulation.Snapshot is not null && simulation.Presenter.Inspect().Accessories.Count == 1);
            Assert.IsFalse(browser.SimulationCutout.IsEmpty); Assert.IsNull(simulation.Error);
            Assert.IsFalse(window.TryRestoreSimulationFocus(), "An offscreen inactive editor never takes focus.");
            Assert.IsFalse(simulation.IsKeyboardFocusWithin);
            var focusIntents = 0; var eligible = true;
            Assert.IsTrue(window.TryRestoreSimulationFocus(() => eligible, () => focusIntents++));
            eligible = false;
            Assert.IsFalse(window.TryRestoreSimulationFocus(() => eligible, () => focusIntents++));
            Assert.AreEqual(1, focusIntents, "Deferred eligibility is read again after deactivation.");
            eligible = true;
            // The HWND stays inactive, so the browser correctly reports hasFocus=false.
            // Inject the focus lease over the real bridge; browser detection has separate JS tests.
            Script("window.dispatchEvent(new CustomEvent('dango-host-command',{detail:{command:'simulation-focus',x:1,y:0}}))");
            Wait(() => !window.TryRestoreSimulationFocus(() => eligible, () => { }));
            Assert.IsFalse(window.TryRestoreSimulationFocus(() => eligible, () => focusIntents++));
            Script("window.dispatchEvent(new CustomEvent('dango-host-command',{detail:{command:'simulation-focus',x:0,y:0}}))");
            Wait(() => window.TryRestoreSimulationFocus(() => eligible, () => { }));
            Assert.AreEqual(1, focusIntents, "Protected browser input must supersede queued restoration.");
            Assert.AreEqual("none", JsonSerializer.Deserialize<string>(Script("getComputedStyle(document.querySelector('.sidebar_left')).display")));
            Assert.AreEqual(0, int.Parse(Script("document.querySelectorAll('[aria-label=\"跳跃\"],[aria-label=\"暂停动态预览\"],#dynamic_preview_page img').length")));
            Assert.AreEqual(original.Order, session.Parameters.Order); Assert.IsFalse(session.Parameters.IsEquipped);
            CheckHole();
            // Route local WPF events only; this fixture never generates OS keyboard input.
            var rightDown = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(simulation)!, 0, System.Windows.Input.Key.D)
                { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            simulation.RaiseEvent(rightDown); Assert.IsTrue(rightDown.Handled);
            Wait(() => simulation.Snapshot!.ActualInput.Horizontal == 1);
            var rightUp = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(simulation)!, 0, System.Windows.Input.Key.D)
                { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyUpEvent };
            simulation.RaiseEvent(rightUp); Assert.IsTrue(rightUp.Handled);
            Wait(() => simulation.Snapshot!.ActualInput.Horizontal == 0);
            var tick = simulation.Snapshot!.Sequence; Pump(Task.Delay(150));
            Assert.IsTrue(simulation.Snapshot!.Sequence > tick, "An offscreen surface must actually consume live physical frames.");
            var render = new RenderTargetBitmap((int)simulation.ActualWidth, (int)simulation.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            render.Render(simulation); var captured = new byte[render.PixelWidth * render.PixelHeight * 4]; render.CopyPixels(captured, render.PixelWidth * 4, 0);
            var bodyPixel = simulation.PresenterToViewport(AccessoryBodyMapper.Map(simulation.Presenter.Inspect().Body!, simulation.Presenter.GaitFrame, .5, .75));
            var bodyIndex = ((int)bodyPixel.Y * render.PixelWidth + (int)bodyPixel.X) * 4;
            var bodyColor = DangoAppearanceConfiguration.Default.BodyColor;
            Assert.AreEqual(bodyColor.B, captured[bodyIndex]); Assert.AreEqual(bodyColor.G, captured[bodyIndex + 1]);
            Assert.AreEqual(bodyColor.R, captured[bodyIndex + 2]); Assert.AreEqual((byte)255, captured[bodyIndex + 3]);

            Script("(()=>{const input=document.getElementById('wear_connectionStrength');input.value='37';input.dispatchEvent(new Event('change',{bubbles:true}));})()");
            Wait(() => session.Parameters.ConnectionStrength == 37);
            Script("(()=>{const input=document.getElementById('wear_layer');input.value='Behind';input.dispatchEvent(new Event('change',{bubbles:true}));})()");
            Wait(() => session.Parameters.Layer == AccessoryLayer.Behind && simulation.Presenter.Inspect().Accessories.Single().Layer == AccessoryLayer.Behind);
            Script("(()=>{const input=document.getElementById('wear_maxConnectionTravelPercent');input.value='0';input.dispatchEvent(new Event('change',{bubbles:true}));})()");
            Wait(() => session.Parameters.MaxConnectionTravelPercent == 0 && simulation.Presenter.Inspect().Accessories.Single().MaximumConnectionTravelDip == 0);
            Script("DangoMiniPaint.undo()"); Wait(() => session.Parameters.MaxConnectionTravelPercent == 75 && simulation.Presenter.Inspect().Accessories.Single().MaximumConnectionTravelDip > 0);
            Script("DangoMiniPaint.undo()"); Wait(() => session.Parameters.Layer == AccessoryLayer.Front);
            Script("DangoMiniPaint.undo()"); Wait(() => session.Parameters.ConnectionStrength == 100);

            var body = simulation.Presenter.Inspect().Body!;
            var center = simulation.PresenterToViewport(AccessoryBodyMapper.Map(body, simulation.Presenter.GaitFrame, .5, .5));
            simulation.SetTool("pick"); simulation.MovePointer(center);
            Assert.AreEqual(Visibility.Visible, simulation.AnchorGhost.Visibility);
            simulation.BeginPointer(center); simulation.EndPointer();
            Wait(() => Math.Abs(session.Parameters.BodyAnchorY - .5) < .02);
            Script("DangoMiniPaint.undo()"); Wait(() => session.Parameters.BodyAnchorY == 0);
            simulation.SetTool("pick"); simulation.MovePointer(center);
            var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(simulation)!, 0, System.Windows.Input.Key.Escape)
                { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            simulation.RaiseEvent(escape);
            Assert.IsTrue(escape.Handled); Assert.IsFalse(simulation.IsPicking);
            Assert.AreEqual(Visibility.Collapsed, simulation.AnchorGhost.Visibility); Assert.IsTrue(window.IsVisible);
            Assert.AreEqual(AccessoryUnifiedEditorState.Ready, session.State);
            Assert.AreEqual(priorImage, Script("DangoMiniPaint.exportPng().pngBase64"));

            simulation.PanBy(new Vector(13, -7)); simulation.ZoomAt(new Point(140, 110), 1.2);
            var refreshOwner = simulation.Runtime!; var refreshEpoch = refreshOwner.Epoch;
            var priorPan = simulation.CameraPan; var priorScale = simulation.CameraScale; var priorWear = session.Parameters;
            Script("document.querySelector('#simulation_tools [aria-label=\"刷新团子\"]').click()");
            Wait(() => simulation.IsActive && refreshOwner.Epoch > refreshEpoch && simulation.Snapshot?.Epoch == refreshOwner.Epoch);
            Assert.AreSame(refreshOwner, simulation.Runtime); Assert.AreEqual(priorPan, simulation.CameraPan);
            Assert.AreEqual(priorScale, simulation.CameraScale); Assert.AreEqual(priorWear, session.Parameters);
            Assert.AreEqual(priorImage, Script("DangoMiniPaint.exportPng().pngBase64"));

            Script("document.getElementById('palette_tab').click()"); Wait(() => !simulation.IsActive && browser.SimulationCutout.IsEmpty);
            Assert.IsFalse(window.TryRestoreSimulationFocus(() => true, () => Assert.Fail("Editing mode cannot request gameplay focus.")));
            var owner = simulation.Runtime!;
            Wait(() => !owner.Metrics.Running);
            Pump(owner.PauseAsync());
            var calls = owner.Metrics.NativeStepCalls; Pump(Task.Delay(100));
            Assert.AreEqual(calls, owner.Metrics.NativeStepCalls, "Leaving mode stops its physical owner.");
            Assert.IsNull(session.Result); Assert.AreEqual(original.Order, session.Parameters.Order); Assert.IsFalse(session.Parameters.IsEquipped);

            // Fail only this fixture's renderer while its local drag is held. The physics
            // owner must stop even though the crashed page cannot send a release message.
            Script("document.getElementById('dynamic_preview_tab').click()"); Wait(() => simulation.IsActive);
            center = simulation.PresenterToViewport(AccessoryBodyMapper.Map(simulation.Presenter.Inspect().Body!, simulation.Presenter.GaitFrame, .5, .5));
            simulation.BeginPointer(center); simulation.MovePointer(center + new Vector(12, -12));
            Wait(() => simulation.Snapshot!.ActualInput.DragActive);
            var crash = core.CallDevToolsProtocolMethodAsync("Page.crash", "{}");
            try { Pump(crash.WaitAsync(TimeSpan.FromSeconds(3))); }
            catch (Exception error) when (error is COMException or InvalidOperationException or TimeoutException) { }
            Wait(() => session.State == AccessoryUnifiedEditorState.Failed);
            Assert.IsFalse(simulation.IsActive); Assert.IsTrue(browser.SimulationCutout.IsEmpty);
            Wait(() => !owner.Metrics.Running); Assert.IsFalse(simulation.IsDragging);
            Assert.AreNotEqual(handle, GetForegroundWindow());

            void CheckHole()
            {
                Assert.AreNotEqual(0, GetWindowRgn(browser.Handle, region));
                var r = browser.GetSimulationCutoutBounds(browser.RenderSize, VisualTreeHelper.GetDpi(browser))!.Value;
                Assert.IsFalse(PtInRegion(region, r.Left + r.Width / 2, r.Top + r.Height / 2));
                Assert.IsTrue(PtInRegion(region, 1, 50), "The rest of the editor HWND remains interactive.");
            }
        }
        finally
        {
            window.Close(); DeleteObject(region); SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    private static T Pump<T>(Task<T> task)
    {
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(frame); return task.GetAwaiter().GetResult();
    }
    private static void Pump(Task task) => Pump(Wrap(task));
    private static async Task<bool> Wrap(Task task) { await task; return true; }
    private static void Wait(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(20)) Pump(Task.Delay(30));
        Assert.IsTrue(condition(), "Real offscreen editor did not reach the expected state.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
}
