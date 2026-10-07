using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Dango.Core.Coordinates;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class DesktopPetRoomNativeInputTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("NativeWindowInput")]
    [DataRow(false)]
    [DataRow(true)]
    public void FixedViewportUpdatesItsNativeRegionWithoutBlockingOldBodyOrDistantBlankSpace(bool maximized)
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Enter();
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.LastPhysicsFrame!.Bodies.Single() is
        { IsGrounded: true, MaximumSpeed: < .05f }, rig.Runtime);
        using var application = new OwnedForegroundApplication(maximized);
        var initial = rig.Runtime.CurrentSelectedCenter!.Value;
        var oldPoint = rig.ToScene(new(initial.X, initial.Y + rig.Runtime.NeutralBodyGroundOffset));
        var oldScreen = rig.Pet.Scene.PointToScreen(oldPoint);
        nint At(Point screen) => GetAncestor(WindowFromPoint(new((int)Math.Round(screen.X), (int)Math.Round(screen.Y))), 2);
        Assert.AreEqual(rig.Window.Handle, At(oldScreen));
        var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var distant = new Point(work.Left + 60, work.Top + 60);
        Assert.AreEqual(application.Handle, At(distant), "The fixed large HWND must exclude distant transparent space.");
        var target = rig.ToScene(new(initial.X + 120, initial.Y - 180 + rig.Runtime.NeutralBodyGroundOffset));
        SendPointer(rig, application, 0x0201, oldPoint, 1);
        SendPointer(rig, application, 0x0200, target, 1);
        application.WaitUntil(rig, () => rig.Runtime.CurrentSelectedCenter!.Value.Y < initial.Y - 100);
        Assert.IsFalse(rig.Window.HitTestScenePoint(oldPoint), "The old contact must actually be outside the moved body.");
        Assert.AreEqual(application.Handle, At(oldScreen), "The previous body's native region must not remain behind.");
        Assert.AreEqual(application.Handle, At(distant));
        var current = DangoHostFrameBinding.Bounds(rig.Window.LastAppliedFrame!, 0).Center;
        var currentPoint = rig.ToScene(new(current.X, current.Y + rig.Runtime.NeutralBodyGroundOffset));
        Assert.IsTrue(rig.Window.HitTestScenePoint(currentPoint));
        Assert.AreEqual(rig.Window.Handle, At(rig.Pet.Scene.PointToScreen(currentPoint)),
            "The current actual body must still have an interactive native region.");
        SendPointer(rig, application, 0x0202, target, 0);
        Assert.IsFalse(rig.Window.IsPointerCaptured || rig.Driver.IsPointerActive || rig.Runtime.IsDragging);
        application.VerifyForeground();
        Assert.AreEqual(application.Editor, Keyboard.FocusedElement);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("NativeWindowInput")]
    [DataRow(false)]
    [DataRow(true)]
    public void AnonymousForegroundApplicationsKeepNativeContourInteractionAndTransparentSpace(bool maximized)
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Enter();
        using var application = new OwnedForegroundApplication(maximized);
        application.VerifyForeground();
        Assert.IsTrue(rig.Pet.IsVisible, "A normal maximized application must not be interpreted as exclusive fullscreen.");
        Assert.IsTrue(rig.Window.InteractionEnabled, "Application foreground must not require Explorer foreground eligibility.");
        var window = rig.Window;
        Assert.IsFalse(window.ShowActivated);
        Assert.IsFalse(window.ShowInTaskbar);
        Assert.IsFalse(window.Focusable);
        Assert.IsTrue(window.AllowsTransparency);
        var style = (long)GetWindowLongPtr(window.Handle, -20);
        Assert.AreNotEqual(0L, style & 0x08000000, "The interactive pet HWND must have WS_EX_NOACTIVATE.");
        Assert.AreNotEqual(0L, style & 0x00000080, "The pet is a tool window rather than a taskbar application.");
        Assert.AreNotEqual(0L, style & 0x00000008, "The pet must remain above ordinary applications.");
        Assert.AreEqual((nint)3, SendMessage(window.Handle, 0x0021, application.Handle, 0), "Mouse activation returns MA_NOACTIVATE.");
        var bodyBounds = DangoHostFrameBinding.Bounds(rig.Runtime.LastFrame!, 0)
            .Translate(new LogicalVector(0, rig.Runtime.NeutralBodyGroundOffset));
        var center = rig.ToScene(bodyBounds.Center);
        Assert.IsTrue(window.HitTestScenePoint(center));
        var centerScreen = rig.Pet.Scene.PointToScreen(center);
        Assert.AreEqual((nint)1, SendMessage(window.Handle, 0x0084, 0, Pack(centerScreen)));
        Assert.AreEqual(window.Handle, GetAncestor(WindowFromPoint(new((int)Math.Round(centerScreen.X), (int)Math.Round(centerScreen.Y))), 2));
        var blank = FindTransparentCorner(rig, bodyBounds);
        var blankScreen = rig.Pet.Scene.PointToScreen(blank);
        Assert.AreEqual((nint)(-1), SendMessage(window.Handle, 0x0084, 0, Pack(blankScreen)),
            "A point inside the body's bounding box but outside the painted contour must pass through.");
        Assert.AreEqual(application.Handle,
            GetAncestor(WindowFromPoint(new((int)Math.Round(blankScreen.X), (int)Math.Round(blankScreen.Y))), 2),
            "The native window region must leave the underlying owned application reachable.");
        application.VerifyForeground();
        Assert.AreEqual(application.Editor, Keyboard.FocusedElement);
        var entity = rig.Runtime.EntityIds.Single();
        var owner = rig.Runtime.FrameDriver;
        SendPointer(rig, application, 0x0201, center, 1);
        Assert.IsTrue(window.IsPointerCaptured);
        SendPointer(rig, application, 0x0202, center, 0);
        Assert.IsFalse(window.IsPointerCaptured);
        application.WaitUntil(rig, () => rig.Runtime.HasLightFeedback, 1000);
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        Assert.AreSame(owner, rig.Runtime.FrameDriver);
        application.VerifyForeground();
        Assert.AreEqual(application.Editor, Keyboard.FocusedElement);
        foreach (var key in new[] { Key.A, Key.D, Key.S, Key.Space, Key.Escape })
            Assert.IsFalse(rig.Driver.Key(key, down: true, repeat: false), "Desktop-pet mode cannot claim the working application's gameplay keys.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("NativeWindowInput")]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeShiftPettingAndCapturedDragWorkAcrossAnOwnedApplicationWithoutStealingFocus(bool maximized)
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Enter();
        using var application = new OwnedForegroundApplication(maximized);
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        var sceneCenter = rig.ToScene(new(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        // The production light controller needs >=16 world horizontal motion, <=8 vertical motion within 600 ms.
        // A real 24-world stroke satisfies that contract without inventing timestamps or relaxing its thresholds.
        var stroke = rig.ToScene(new(center.X + 24, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        var owner = rig.Runtime.FrameDriver;
        var entity = rig.Runtime.EntityIds.Single();
        var strokeTime = Stopwatch.StartNew();
        SendPointer(rig, application, 0x0201, sceneCenter, 1 | 4);
        Assert.IsTrue(rig.Window.IsPointerCaptured);
        SendPointer(rig, application, 0x0200, stroke, 1 | 4);
        Assert.IsFalse(rig.Runtime.IsDragging, "Shift-horizontal motion is petting, not a physical grab.");
        SendPointer(rig, application, 0x0202, stroke, 4);
        strokeTime.Stop();
        application.WaitUntil(rig, () => rig.Runtime.HasLightFeedback, 1000,
            () => $"StrokeWorldDx=24; StrokeWorldDy=0; StrokeElapsedMs={strokeTime.ElapsedMilliseconds}; {NativeDiagnostic(rig)}");
        application.WaitUntil(rig, () => !rig.Runtime.HasLightFeedback, 1500);
        center = rig.Runtime.CurrentSelectedCenter!.Value;
        sceneCenter = rig.ToScene(new(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        var lifted = rig.ToScene(new(center.X + 110, center.Y - 180 + rig.Runtime.NeutralBodyGroundOffset));
        SendPointer(rig, application, 0x0201, sceneCenter, 1);
        SendPointer(rig, application, 0x0200, lifted, 1);
        Assert.IsTrue(rig.Runtime.IsDragging);
        Assert.IsTrue(rig.Window.IsPointerCaptured);
        application.WaitUntil(rig, () => rig.Runtime.CurrentSelectedCenter!.Value.Y < center.Y - 55);
        application.VerifyForeground();
        SendPointer(rig, application, 0x0202, lifted, 0);
        Assert.IsFalse(rig.Window.IsPointerCaptured);
        Assert.IsFalse(rig.Runtime.IsDragging);
        application.WaitUntil(rig, () => rig.Runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded);
        Assert.AreSame(owner, rig.Runtime.FrameDriver);
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        application.VerifyForeground();
        Assert.AreEqual(application.Editor, Keyboard.FocusedElement);
    }

    private static Point FindTransparentCorner(DesktopPetRoomFixture rig, LogicalRect bounds)
    {
        foreach (var x in new[] { .025, .975 })
            foreach (var y in new[] { .025, .975 })
            {
                var world = new LogicalPoint(bounds.X + bounds.Width * x, bounds.Y + bounds.Height * y);
                var point = rig.ToScene(world);
                if (!rig.Window.HitTestScenePoint(point)) return point;
            }
        Assert.Fail("The real contour must have transparent corners inside its bounding box; rectangle-only hit testing is insufficient.");
        return default;
    }

    private static void SendPointer(DesktopPetRoomFixture rig, OwnedForegroundApplication application, uint message, Point scenePoint, int modifiers)
    {
        application.VerifyForeground();
        var screen = rig.Pet.Scene.PointToScreen(scenePoint);
        var local = new NativePoint((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        Assert.IsTrue(ScreenToClient(rig.Window.Handle, ref local));
        _ = SendMessage(rig.Window.Handle, message, modifiers, Pack(new(local.X, local.Y)));
    }

    private static nint Pack(Point point) => (nint)((uint)(ushort)(short)Math.Round(point.X) |
        ((uint)(ushort)(short)Math.Round(point.Y) << 16));

    private static string NativeDiagnostic(DesktopPetRoomFixture rig) =>
        $"Driver={rig.Driver.DesktopPetPointerDiagnostics}; Host={rig.Pet.LastSurfaceFailure}; " +
        $"LightEnabled={rig.Runtime.LightInteractionsEnabled}; HasLightFeedback={rig.Runtime.HasLightFeedback}; " +
        $"LightError={rig.Runtime.LightInteractionError}; DozeError={rig.Runtime.Doze.Error}; " +
        $"Captured={rig.Pet.PetWindow?.IsPointerCaptured}; SurfaceHealthy={rig.Pet.PetWindow?.HasHealthyFrame}; " +
        $"Scheduler={rig.Scheduler.Snapshot}";

    private sealed class OwnedForegroundApplication : IDisposable
    {
        private readonly Window window;
        private readonly nint previousForeground;
        internal TextBox Editor { get; } = new() { Text = "fictional fixture input", AcceptsReturn = true };
        internal nint Handle { get; }

        internal OwnedForegroundApplication(bool maximized)
        {
            previousForeground = GetForegroundWindow();
            window = new()
            {
                Title = string.Empty,
                Content = Editor,
                ShowInTaskbar = false,
                ShowActivated = true,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = SystemParameters.WorkArea.Left,
                Top = SystemParameters.WorkArea.Top,
                Width = SystemParameters.WorkArea.Width,
                Height = SystemParameters.WorkArea.Height,
                WindowState = maximized ? WindowState.Maximized : WindowState.Normal,
            };
            try
            {
                window.Show();
                Handle = new WindowInteropHelper(window).Handle;
                if (!window.Activate() || GetForegroundWindow() != Handle)
                    Assert.Inconclusive("The anonymous owned application cannot obtain foreground; refusing any native messages.");
                Editor.Focus();
                Keyboard.Focus(Editor);
                DesktopPetRoomFixture.Pump(70);
                VerifyForeground();
            }
            catch { Dispose(); throw; }
        }

        internal void VerifyForeground()
        {
            if (GetForegroundWindow() != Handle || !window.IsVisible)
                Assert.Inconclusive("Owned foreground was lost; refusing native messages without taking focus back.");
        }

        internal void WaitUntil(DesktopPetRoomFixture rig, Func<bool> predicate, int timeoutMilliseconds = 4500,
            Func<string>? diagnostic = null) =>
            DesktopPetRoomFixture.WaitUntil(() =>
            {
                VerifyForeground();
                return predicate();
            }, rig.Runtime, timeoutMilliseconds, diagnostic ?? (() => NativeDiagnostic(rig)));

        public void Dispose()
        {
            var stillOwned = GetForegroundWindow() == Handle;
            window.Close();
            if (stillOwned && previousForeground != 0 && IsWindow(previousForeground)) _ = SetForegroundWindow(previousForeground);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
