using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.LocalRoomProbe;

internal static partial class Program
{
    private static string? MouseVCancelRoot;
    private static void VerifyDesktopMouse(string root, string module)
    {
        root = ValidateRoot(root, false);
        MouseVCancelRoot = root;
        Require(Environment.GetEnvironmentVariable("DANGO_LOCAL_SCREEN_AUTHORIZED") == "1", "Screen authorization required.");
        Require(Path.GetFileName(module) == "dango_belt_room.dll" && File.Exists(module), "Default Belt room module required.");
        var resultPath = Path.Combine(root, "desktop-mouse-result.json");
        Require(!File.Exists(resultPath), "Use a fresh mouse verification result root.");
        var priorForeground = MouseVForeground();
        Require(MouseVCursor(out var priorCursor), "Cannot save cursor position.");
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        DangoApplicationHost? host = null;
        Window? owned = null;
        StackedDangoSceneRuntime? runtime = null;
        DesktopPetDangoWindow? surface = null;
        bool held = false, passed = false, released = false, hostDisposed = false, ownedClosed = false;
        bool cursorRestored = false, foregroundRestored = false, appShutdown = false;
        int underlyingClicks = 0;
        var checks = new List<string>();
        string? failure = null;
        var cleanupErrors = new List<string>();
        try
        {
            MouseVIdle(false);
            host = new(settingsFile: Dango.Desktop.App.ResolveLocalInstallAcceptanceSettingsFile(
                ["--local-install-acceptance-settings", root]), roomModulePath: Path.GetFullPath(module));
            host.Start();
            runtime = MouseVField<StackedDangoSceneRuntime>(host, "stackedRuntime");
            Require(runtime.UsesBeltRoom, "Production host did not select Belt runtime.");
            var home = MouseVField<ControlCenterWindow>(host, "controlCenterWindow").ProductHome;
            MouseVWait(() => home.CatalogSnapshot is not null, "catalog startup");
            home.ReduceMotion = true;
            home.ResumePorch();
            MouseVWait(() => home.DisplayedBeltSnapshot is { Error: null, Frame: not null } && home.BoundBeltBody!.Metrics.Running, "porch readiness");
            host.EnterDesktopPetRoom();
            MouseVWait(() => !host.LocalHandoffPending && host.LocalPresentation == LocalRoomPresentation.DesktopPet && host.DesktopPetHost?.IsReady == true, "desktop-pet entry");
            var pet = host.DesktopPetHost!;
            surface = pet.PetWindow!;
            var driver = MouseVField<LocalRoomSceneDriver>(host, "localRoomDriver");
            Require(pet.Scene.HasHealthyFrame && surface.HasHealthyFrame &&
                ReferenceEquals(runtime.LastFrame, pet.Scene.LastAppliedFrame) && ReferenceEquals(pet.Scene.LastAppliedFrame, surface.LastAppliedFrame), "Dual surfaces must share a healthy actual frame.");
            Require(driver.OwnsRuntime && runtime.FrameDriver is not null && !home.BoundBeltBody!.Metrics.Running, "Room must be the unique live owner.");
            var identity = runtime.LastBeltRoomFrame!;
            var owner = runtime.FrameDriver;
            checks.Add("actual-default-Belt-host-dual-surfaces-and-unique-owner");

            var editor = new TextBox { Text = "fictional owned input fixture", AcceptsReturn = true };
            editor.PreviewMouseLeftButtonDown += (_, _) => underlyingClicks++;
            var area = SystemParameters.WorkArea;
            owned = new Window { Title = string.Empty, Content = editor, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = area.Left, Top = area.Top,
                Width = area.Width, Height = area.Height };
            owned.Show();
            var ownHandle = new WindowInteropHelper(owned).Handle;
            Require(owned.Activate() && MouseVForeground() == ownHandle, "Owned anonymous foreground unavailable; input refused.");
            editor.Focus(); Keyboard.Focus(editor); MouseVPump(80);
            void Foreground()
            {
                ContinueScreenCheck(root);
                Require(MouseVForeground() == ownHandle && owned.IsVisible, "Owned foreground lost; input refused.");
                Require(Keyboard.FocusedElement == editor, "Owned editor focus changed.");
                Require(pet.IsReady && surface.HasHealthyFrame && driver.OwnsRuntime && runtime.LastError is null, "Pet surface or owner became unhealthy.");
                MouseVIdle(held);
            }
            Point Screen(LogicalPoint point)
            {
                var scene = pet.Scene.RoomToScene(point);
                return pet.Scene.PointToScreen(new Point(scene.X, scene.Y));
            }
            LogicalRect Bounds() => DangoHostFrameBinding.Bounds(runtime.LastFrame!, 0)
                .Translate(new LogicalVector(0, runtime.NeutralBodyGroundOffset));
            nint At(Point p) => MouseVAncestor(MouseVWindowAt(new((int)Math.Round(p.X), (int)Math.Round(p.Y))), 2);
            void Move(Point p, nint expected = 0)
            {
                Foreground();
                var target = At(p);
                Require(target == ownHandle || target == surface.Handle, "Destination belongs to an unrelated window; input refused.");
                if (expected != 0) Require(target == expected, "Native hit target differs from expected owned surface.");
                Require(MouseVSetCursor((int)Math.Round(p.X), (int)Math.Round(p.Y)), "Cursor move failed.");
                MouseVPump(12); Foreground();
                Require(At(p) == ownHandle || At(p) == surface.Handle, "Destination changed to an unrelated window.");
            }
            void Button(bool down)
            {
                Foreground();
                Require(MouseVCursor(out var cursor), "Cannot read owned injection position.");
                var target = At(new Point(cursor.X, cursor.Y));
                Require(target == ownHandle || target == surface.Handle, "Click target belongs to an unrelated window.");
                Require(!held || !driver.IsPointerActive || MouseVCapture() == surface.Handle, "Owned drag capture lost; input refused.");
                // Set ownership before injection so the finally path releases even if SendInput fails partially.
                if (down) held = true;
                MouseVButton(down);
                if (!down) held = false;
                MouseVPump(20); Foreground();
            }
            MouseVWait(() => runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded, "settled body");
            var body = Screen(Bounds().Center);
            Move(body, surface.Handle);
            var clicksBefore = underlyingClicks;
            Button(true);
            Require(surface.IsPointerCaptured && driver.IsPointerActive, "Real OS down did not capture pet pointer.");
            Button(false);
            MouseVWait(() => runtime.HasLightFeedback, "real body tap feedback", 1200);
            Require(underlyingClicks == clicksBefore && !surface.IsPointerCaptured && !runtime.IsDragging && !driver.IsPointerActive,
                "Tap reached underlying editor or retained capture.");
            checks.Add("SendInput-body-hit-light-feedback-no-focus-steal");

            Point? corner = null;
            var bounds = Bounds();
            foreach (var x in new[] { .025, .975 })
                foreach (var y in new[] { .025, .975 })
                {
                    var point = new LogicalPoint(bounds.X + bounds.Width * x, bounds.Y + bounds.Height * y);
                    var scene = pet.Scene.RoomToScene(point);
                    if (!surface.HitTestScenePoint(new Point(scene.X, scene.Y)) && At(Screen(point)) == ownHandle)
                        corner = Screen(point);
                }
            Require(corner is not null, "No transparent contour corner was reachable.");
            foreach (var blank in new[] { corner!.Value, new Point(0, 0) })
            {
                // The second point is inside our WPF client rather than on a window border.
                var target = blank == new Point(0, 0) ? editor.PointToScreen(new Point(50, 50)) : blank;
                clicksBefore = underlyingClicks;
                Move(target, ownHandle); Button(true); Button(false);
                Require(underlyingClicks == clicksBefore + 1, "Transparent space did not deliver an actual OS click to the owned editor.");
            }
            checks.Add("transparent-contour-corner-and-distant-blank-real-underlying-clicks");

            var initial = Bounds().Center;
            Move(Screen(initial), surface.Handle); Button(true);
            var lifted = new LogicalPoint(initial.X + 110, initial.Y - 180);
            for (var i = 1; i <= 8; i++)
                Move(Screen(new LogicalPoint(initial.X + (lifted.X - initial.X) * i / 8,
                    initial.Y + (lifted.Y - initial.Y) * i / 8)));
            MouseVWait(() => runtime.IsDragging && Bounds().Center.Y < initial.Y - 55, "real OS body drag displacement");
            Require(surface.IsPointerCaptured && driver.IsPointerActive, "Drag capture disappeared.");
            Require(At(Screen(initial)) == ownHandle, "Native body region remained at its previous location.");
            Button(false);
            MouseVWait(() => !surface.IsPointerCaptured && !runtime.IsDragging && !driver.IsPointerActive, "drag release");
            Require(MouseVCapture() == 0, "Native capture remained after release.");
            MouseVWait(() => runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded, "post-drag grounding");
            Foreground();
            var finalIdentity = runtime.LastBeltRoomFrame!;
            Require(identity.OwnerEpoch == finalIdentity.OwnerEpoch && identity.BodyId == finalIdentity.BodyId && ReferenceEquals(owner, runtime.FrameDriver), "Pointer interaction replaced body identity or scheduler owner.");
            checks.Add("SendInput-drag-displacement-region-update-release-no-capture-and-grounding");
            passed = true;
        }
        catch (Exception error) { failure = error is InvalidDataException ? error.Message : error.GetType().Name; throw; }
        finally
        {
            void Clean(string name, Action action) { try { action(); } catch { cleanupErrors.Add(name); } }
            Clean("owned-button-release", () => { if (held) { MouseVButton(false); held = false; } released = true; });
            Clean("host-dispose", () => { host?.Dispose(); hostDisposed = runtime?.FrameDriver is null && runtime?.IsRunning != true; });
            Clean("owned-window-close", () => { owned?.Close(); ownedClosed = owned?.IsVisible != true; });
            Clean("cursor-restore", () => { cursorRestored = MouseVSetCursor(priorCursor.X, priorCursor.Y); });
            Clean("foreground-restore", () => { foregroundRestored = priorForeground == 0 || !MouseVIsWindow(priorForeground) ||
                MouseVForeground() == priorForeground || MouseVSetForeground(priorForeground); });
            Clean("application-shutdown", () => { app.Shutdown(); appShutdown = true; });
            MouseVCancelRoot = null;
            var cleanupPassed = released && hostDisposed && ownedClosed && cursorRestored && foregroundRestored && appShutdown && cleanupErrors.Count == 0;
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { passed = passed && cleanupPassed, failure, checks,
                realOsMouse = true, fictionalUnderlyingApplication = true,
                unverified = new[] { "Shift petting", "maximized application variant", "subjective visual quality", "unrelated real applications" },
                cleanup = new { released, hostDisposed, ownedClosed, cursorRestored, foregroundRestored, appShutdown, cleanupErrors } }, new JsonSerializerOptions { WriteIndented = true }));
            if (failure is null) Require(cleanupPassed, "Mouse verification cleanup incomplete; see owned result.");
        }
    }

    private static T MouseVField<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void MouseVWait(Func<bool> condition, string operation, int milliseconds = 12000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < milliseconds) MouseVPump(5);
        Require(condition(), "Timed out: " + operation);
    }
    private static void MouseVPump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            if (MouseVCancelRoot is { } root) ContinueScreenCheck(root);
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(1);
        }
    }
    private static void MouseVIdle(bool ownedLeft)
    {
        for (var key = 1; key <= 254; key++)
            if (!(key == 1 && ownedLeft)) Require((MouseVKeyState(key) & 0x8000) == 0, "Unowned pressed key or mouse button; injection refused.");
    }
    private static void MouseVButton(bool down)
    {
        var inputs = new[] { new MouseVInput { Type = 0, Union = new MouseVUnion { Mouse = new MouseVMouse { Flags = down ? 0x0002u : 0x0004u } } } };
        Require(MouseVSendInput(1, inputs, Marshal.SizeOf<MouseVInput>()) == 1, "SendInput failed.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseVPoint(int x, int y) { public int X = x; public int Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseVInput { public uint Type; public MouseVUnion Union; }
    [StructLayout(LayoutKind.Explicit)] private struct MouseVUnion { [FieldOffset(0)] public MouseVMouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseVMouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint MouseVForeground();
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MouseVSetForeground(nint window);
    [DllImport("user32.dll", EntryPoint = "IsWindow")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MouseVIsWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MouseVCursor(out MouseVPoint point);
    [DllImport("user32.dll", EntryPoint = "SetCursorPos")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MouseVSetCursor(int x, int y);
    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")] private static extern short MouseVKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")] private static extern nint MouseVWindowAt(MouseVPoint point);
    [DllImport("user32.dll", EntryPoint = "GetAncestor")] private static extern nint MouseVAncestor(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetCapture")] private static extern nint MouseVCapture();
    [DllImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)] private static extern uint MouseVSendInput(uint count, MouseVInput[] inputs, int size);
}
