using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Furniture;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.LocalRoomProbe;

internal static partial class Program
{
    // Foreground screen authorization belongs to the caller. This is a narrow
    // observation of the actual ordinary-room shell, scheduler and native body.
    private static void VisibleObserve(string root, string module, double activeSeconds)
    {
        root = ValidateRoot(root, false);
        Require(double.IsFinite(activeSeconds) && activeSeconds > 0, "Positive finite active duration required.");
        Require(Path.GetFileName(module) == "dango_belt_room.dll" && File.Exists(module), "Actual room module required.");
        Require(!File.Exists(Path.Combine(root, "visible-result.json")), "Use a fresh owned observation root.");
        var started = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        var originalForeground = VisibleForeground();
        DangoApplicationHost? host = null;
        StackedDangoSceneRuntime? runtime = null;
        LocalRoomSceneDriver? driver = null;
        Window? roomWindow = null;
        nint hwnd = 0;
        byte held = 0;
        bool spaceHeld = false, ownerReleased = false, windowClosed = false, foregroundRestored = false;
        bool previousEligible = false, jumpObserved = false;
        double active = 0, lastSample = 0, nextProgress = 30, nextJump = 4;
        double? minX = null, maxX = null, ballMinX = null, ballMaxX = null, ballMinY = null, ballMaxY = null;
        ulong initialTick = 0, lastTick = 0;
        long jumpRequests = 0, faultSamples = 0, activeSamples = 0;
        string? failure = null;
        Guid roomId = Guid.Empty;
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        void ReleaseKeys()
        {
            if (held != 0) { VisibleKey(held, 0, 2, 0); held = 0; }
            if (spaceHeld) { VisibleKey(0x20, 0, 2, 0); spaceHeld = false; }
        }
        bool Eligible() => hwnd != 0 && VisibleForeground() == hwnd && roomWindow is
            { IsActive: true, IsVisible: true, WindowState: not WindowState.Minimized } &&
            driver is { IsReady: true, OwnsRuntime: true, RuntimeRunning: true } &&
            host is { LocalHandoffPending: false, LocalPresentation: LocalRoomPresentation.Window } &&
            host.LocalWindow!.Scene.HasHealthyFrame;
        object Evidence(bool passed) => new
        {
            passed, failure, scope = "foreground ordinary room; real shell/scheduler/native ticks and OS A/D/Space only",
            requestedActiveSeconds = activeSeconds, ActiveSeconds = active,
            excludedSeconds = Math.Max(0, clock.Elapsed.TotalSeconds - active), elapsedSeconds = clock.Elapsed.TotalSeconds,
            startedUtc = started, endedUtc = DateTimeOffset.UtcNow, roomId, pid = Environment.ProcessId,
            initialNativeTick = initialTick, finalNativeTick = lastTick, activeSamples, faultSamples,
            minX, maxX, displacementRange = minX.HasValue ? maxX - minX : null,
            ballMinX, ballMaxX, ballMinY, ballMaxY,
            ballMoved = ballMinY.HasValue && (ballMaxY - ballMinY > 1 || ballMaxX - ballMinX > 1),
            jumpRequests, jumpObserved, platformContact = "not independently instrumented or accepted",
            ownerReleased, windowClosed, foregroundRestored,
            limitations = "Does not establish all play actions, platform contact, visual quality, or manual acceptance."
        };
        void Report(string name, bool passed)
        {
            var path = Path.Combine(root, name);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Evidence(passed), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        void PumpSlice()
        {
            ContinueScreenCheck(root);
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
        try
        {
            // Never release or reuse a physical key/button already held by the user.
            foreach (var key in new[] { 0x01, 0x02, 0x04, 0x05, 0x06, 0x10, 0x11, 0x12, 0x20, 0x41, 0x44 })
                Require((VisibleAsyncKey(key) & 0x8000) == 0,
                    $"Input 0x{key:X2} is already held; foreground observation did not acquire input ownership.");
            var catalog = new LocalRoomCatalogStore(root);
            var initial = catalog.Refresh();
            Require(initial.Success && initial.Catalog!.Rooms.Length == 1, initial.Warning ?? "Fresh one-room catalog required.");
            roomId = initial.Catalog!.Rooms.Single().RoomId;
            Require(catalog.ReadLayout(roomId).Items.Count == 0, "Fresh empty furniture layout required.");
            var furniture = FurnitureLayout.ForItems(1, Guid.NewGuid(),
                [new(Guid.NewGuid(), FurnitureItemKind.StaticPlatform, new(1440, 120)),
                 new(Guid.NewGuid(), FurnitureItemKind.RoundBall, new(1800, 240))]);
            using (var lease = catalog.CreateLayoutStore(roomId).AcquireLease(catalog.LayoutPath(roomId)))
            using (var stage = lease.Stage(furniture, 0))
                Require(stage.Commit().State == FurnitureCommitState.Committed, "Observation furniture commit failed.");
            host = new DangoApplicationHost(settingsFile: Dango.Desktop.App.ResolveLocalInstallAcceptanceSettingsFile(
                ["--local-install-acceptance-settings", root]), roomModulePath: Path.GetFullPath(module));
            host.Start();
            host.ShowControlCenter();
            runtime = VisibleField<StackedDangoSceneRuntime>(host, "stackedRuntime");
            driver = VisibleField<LocalRoomSceneDriver>(host, "localRoomDriver");
            var home = VisibleField<ControlCenterWindow>(host, "controlCenterWindow").ProductHome;
            var catalogDeadline = clock.Elapsed.TotalSeconds + 15;
            while (home.CatalogSnapshot?.Find(roomId) is null)
            {
                Require(clock.Elapsed.TotalSeconds < catalogDeadline,
                    $"Catalog shell startup timed out; catalogNull={home.CatalogSnapshot is null}; expectedRoom={roomId}.");
                PumpSlice();
            }
            host.EnterCatalogRoom(roomId, false);
            var roomDeadline = clock.Elapsed.TotalSeconds + 25;
            while (host.LocalHandoffPending || host.LocalPresentation != LocalRoomPresentation.Window || !driver.IsReady)
            {
                Require(host.LocalRoomFailure is null && runtime.LastError is null, host.LocalRoomFailure ?? runtime.LastError ?? "Room failed.");
                Require(clock.Elapsed.TotalSeconds < roomDeadline, "Actual room presentation timed out.");
                PumpSlice();
            }
            roomWindow = host.LocalWindow!;
            hwnd = new WindowInteropHelper(roomWindow).Handle;
            host.LocalWindow!.Activate(); host.LocalWindow.FocusScene();
            var focusDeadline = clock.Elapsed.TotalSeconds + 5;
            while (!Eligible() && clock.Elapsed.TotalSeconds < focusDeadline)
            {
                VisibleSetForeground(hwnd);
                host.LocalWindow.Activate(); host.LocalWindow.FocusScene();
                var retryUntil = clock.Elapsed.TotalSeconds + .1;
                while (clock.Elapsed.TotalSeconds < retryUntil) PumpSlice();
            }
            Require(Eligible(), "Could not acquire the actual visible room foreground safely within five seconds.");
            initialTick = lastTick = runtime.LastBeltRoomFrame!.Tick;
            lastSample = clock.Elapsed.TotalSeconds;
            while (active < activeSeconds)
            {
                ContinueScreenCheck(root);
                Require(clock.Elapsed.TotalSeconds <= activeSeconds + 120, "Wall limit exceeded while awaiting effective foreground time.");
                var eligibleBeforePump = Eligible();
                if (!eligibleBeforePump) ReleaseKeys();
                PumpSlice();
                var now = clock.Elapsed.TotalSeconds;
                var frame = runtime.LastBeltRoomFrame;
                Require(host.LocalRoomFailure is null && runtime.LastError is null, host.LocalRoomFailure ?? runtime.LastError ?? "Runtime failed.");
                if (frame is { Fault: not 0 }) { faultSamples++; throw new InvalidOperationException($"Native fault {frame.Fault}."); }
                var eligible = Eligible();
                if (!eligible) ReleaseKeys();
                if (frame is not null && frame.Tick > lastTick && previousEligible && eligibleBeforePump && eligible)
                {
                    // Bound a sample by native elapsed time as well as wall time. A stalled
                    // scheduler followed by one tick cannot turn its stall into active seconds.
                    active += Math.Min(now - lastSample, (frame.Tick - lastTick) * HostFrameClock.FixedStep.TotalSeconds);
                    activeSamples++;
                    if (runtime.CurrentSelectedCenter is { } center)
                    { minX = Math.Min(minX ?? center.X, center.X); maxX = Math.Max(maxX ?? center.X, center.X); }
                    var ball = runtime.Furniture!.RenderItems.Single(item => item.Kind == FurnitureItemKind.RoundBall).Anchor;
                    ballMinX = Math.Min(ballMinX ?? ball.X, ball.X); ballMaxX = Math.Max(ballMaxX ?? ball.X, ball.X);
                    ballMinY = Math.Min(ballMinY ?? ball.Y, ball.Y); ballMaxY = Math.Max(ballMaxY ?? ball.Y, ball.Y);
                    if (jumpRequests > 0 && (frame.Stats[15] != 0 || frame.BodySnapshot[79] < .15)) jumpObserved = true;
                }
                if (frame is not null && frame.Tick != lastTick || !eligible || !previousEligible) lastSample = now;
                if (frame is not null) lastTick = frame.Tick;
                previousEligible = eligible;
                if (eligible)
                {
                    var desired = (byte)(((int)(active / 8) % 2 == 0) ? 0x44 : 0x41);
                    if (held != desired && Eligible())
                    {
                        ReleaseKeys();
                        if (Eligible()) { VisibleKey(desired, 0, 0, 0); held = desired; }
                    }
                    if (spaceHeld) { VisibleKey(0x20, 0, 2, 0); spaceHeld = false; }
                    if (active >= nextJump && Eligible())
                    { VisibleKey(0x20, 0, 0, 0); spaceHeld = true; jumpRequests++; nextJump = active + 4; }
                }
                if (now >= nextProgress) { Report("visible-progress.json", false); nextProgress = now + 30; }
            }
            Require(active >= activeSeconds && lastTick > initialTick, "Insufficient effective observation.");
            Require(maxX - minX > 1, "OS A/D did not produce observed physical movement.");
            Require(jumpRequests > 0 && jumpObserved, "OS Space did not produce an observed native jump.");
            Require(ballMaxY - ballMinY > 1 || ballMaxX - ballMinX > 1, "No real ball movement observed.");
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            ReleaseKeys();
            try
            {
                host?.Dispose();
                ownerReleased = runtime is null || runtime.FrameDriver is null && !runtime.IsRunning;
                windowClosed = hwnd == 0 || !VisibleIsWindow(hwnd);
                if (!ownerReleased || !windowClosed) failure ??= "Host cleanup did not release owner and close room window.";
            }
            catch (Exception error) { failure = (failure ?? "") + " Cleanup: " + error; }
            finally
            {
                if (originalForeground != 0 && VisibleIsWindow(originalForeground))
                    foregroundRestored = VisibleSetForeground(originalForeground) && VisibleForeground() == originalForeground;
                Report("visible-result.json", failure is null);
            }
        }
        if (failure is not null) throw new InvalidOperationException(failure);
        Console.WriteLine(JsonSerializer.Serialize(Evidence(true)));
        GC.KeepAlive(app);
    }

    private static T VisibleField<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint VisibleForeground();
    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")] private static extern short VisibleAsyncKey(int key);
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool VisibleSetForeground(nint window);
    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool VisibleIsWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "keybd_event")] private static extern void VisibleKey(byte key, byte scan, uint flags, nuint extra);
}
