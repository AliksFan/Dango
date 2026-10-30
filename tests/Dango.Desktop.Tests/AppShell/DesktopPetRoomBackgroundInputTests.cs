using System.Runtime.InteropServices;
using System.Windows;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.AppShell;
using Dango.Desktop.Rendering.Visuals;
using Forms = System.Windows.Forms;

namespace Dango.Desktop.Tests.AppShell;

// Uses the real host/COM/driver/frames in an explicitly offscreen display
// geometry. No activation, OS capture, menu display or global input is used.
[TestClass]
[DoNotParallelize]
public sealed class DesktopPetRoomBackgroundInputTests
{
    [STATestMethod]
    public void CrossThreadMouseHitTestPreservesInputWithoutCallingShellInsideTheSynchronousMessage()
    {
        var area = new System.Drawing.Rectangle(-20000, -20000, 1024, 640);
        Assert.IsFalse(Forms.Screen.AllScreens.Any(screen => screen.Bounds.IntersectsWith(area)));
        var geometry = new DesktopPetDisplayGeometry(area, area);
        using var foreground = new OwnForegroundObservation();
        var desktop = new RecordingDesktop();
        using var runtime = ProductPhysicsComposition.Default.CreateRoom();
        runtime.ConfigureBeltRoomAppearance(DangoAppearanceConfiguration.Default, beginRoom: true);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        using var driver = new LocalRoomSceneDriver(new(), runtime);
        using var host = new DesktopPetRoomHost(createVirtualDesktop: () => desktop, readDisplayGeometry: () => geometry);
        host.Bind(driver);
        host.SetDisplayPercent(72);
        host.HealthyFrameReady += host.ConfirmReady;
        try
        {
            host.Show(1, Forms.Screen.PrimaryScreen!);
            driver.Start(host.Scene, 1, enterWorld: true);
            DesktopPetRoomFixture.WaitUntil(() => host.IsReady && host.PetWindow!.InteractionEnabled, runtime);
            var center = runtime.CurrentSelectedCenter!.Value;
            var world = new LogicalPoint(center.X, center.Y + runtime.NeutralBodyGroundOffset) + host.Scene.PresentationOffset;
            var projected = host.Scene.RoomToScene(world);
            var screen = host.Scene.PointToScreen(new(projected.X, projected.Y));
            var parameter = (nint)((uint)(ushort)(short)Math.Round(screen.X) | ((uint)(ushort)(short)Math.Round(screen.Y) << 16));
            var handle = host.PetWindow!.Handle;
            var message = Task.Run(() =>
            {
                // Same native notification sequence as another application
                // taking focus, without actually activating or controlling it.
                foreach (var notification in new uint[] { 0x001C, 0x0086, 0x0006 })
                    if (SendMessageTimeout(handle, notification, 0, 0, 2, 3000, out _) == 0)
                        return (Sent: false, Result: (nint)0);
                if (SendMessageTimeout(handle, 0x0021, 0, 0, 2, 3000, out var activation) == 0 || activation != 3)
                    return (Sent: false, Result: activation);
                var sent = SendMessageTimeout(handle, 0x0084, 0, parameter, 2, 3000, out var result);
                return (Sent: sent != 0, Result: result);
            });
            DesktopPetRoomFixture.WaitUntil(() => message.IsCompleted, runtime, timeoutMilliseconds: 5000);
            var response = message.GetAwaiter().GetResult();
            Assert.IsTrue(response.Sent, "The background message must reach the own real HWND within its bound.");
            Assert.AreEqual((nint)1, response.Result,
                $"Cross-thread hit-test must qualify. Receiver={desktop.LastExceptionType}; HResult=0x{desktop.LastExceptionCode:X8}; {host.LastSurfaceFailure}");
            Assert.AreEqual(0, desktop.CallsInsideSynchronousMessage,
                "No Shell qualification call may occur inside any cross-thread synchronous window message.");
            Assert.IsTrue(host.IsReady && host.PetWindow.InteractionEnabled);
            Assert.AreEqual(string.Empty, host.LastFailureReason);
        }
        finally { driver.Stop(); host.Hide(); foreground.AssertNoActivation(); }
    }

    [STATestMethod]
    public void MouseMessagesUseConfirmedStateAndTimerDesktopChangesRecoverWithFreshPresentation()
    {
        var area = new System.Drawing.Rectangle(-20000, -20000, 1024, 640);
        Assert.IsFalse(Forms.Screen.AllScreens.Any(screen => screen.Bounds.IntersectsWith(area)));
        var geometry = new DesktopPetDisplayGeometry(area, area);
        using var foreground = new OwnForegroundObservation();
        var desktop = new OneUncertainSample();
        using var runtime = ProductPhysicsComposition.Default.CreateRoom();
        runtime.ConfigureBeltRoomAppearance(DangoAppearanceConfiguration.Default, beginRoom: true);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        using var driver = new LocalRoomSceneDriver(new(), runtime);
        using var host = new DesktopPetRoomHost(createVirtualDesktop: () => desktop, readDisplayGeometry: () => geometry);
        host.Bind(driver);
        host.HealthyFrameReady += host.ConfirmReady;
        try
        {
            host.Show(1, Forms.Screen.PrimaryScreen!);
            driver.Start(host.Scene, 1, enterWorld: true);
            DesktopPetRoomFixture.WaitUntil(() => host.IsReady && host.PetWindow!.InteractionEnabled, runtime);
            var center = runtime.CurrentSelectedCenter!.Value;
            var world = new LogicalPoint(center.X, center.Y + runtime.NeutralBodyGroundOffset) + host.Scene.PresentationOffset;
            var projected = host.Scene.RoomToScene(world);
            var screen = host.Scene.PointToScreen(new(projected.X, projected.Y));
            var packed = (nint)((uint)(ushort)(short)Math.Round(screen.X) | ((uint)(ushort)(short)Math.Round(screen.Y) << 16));
            var queries = desktop.QueryCount;
            desktop.RejectNext = true;
            Assert.AreEqual((nint)1, SendMessage(host.PetWindow!.Handle, 0x0084, 0, packed),
                "Native input uses the already confirmed cache, without querying Shell inside the message.");
            Assert.AreEqual(queries, desktop.QueryCount);
            host.CheckSurfaceHealth();
            Assert.IsTrue(host.IsDesktopSuspended);
            Assert.IsFalse(host.IsReady || host.PetWindow.InteractionEnabled);
            Assert.AreEqual((nint)(-1), SendMessage(host.PetWindow.Handle, 0x0084, 0, packed));
            host.CheckSurfaceHealth();
            DesktopPetRoomFixture.WaitUntil(() => host.IsReady && host.PetWindow!.InteractionEnabled, runtime,
                diagnostic: () => $"Ready={host.IsReady}; Input={host.PetWindow?.InteractionEnabled}; Suspended={host.IsDesktopSuspended}; {host.LastSurfaceFailure}");
            Assert.AreEqual(string.Empty, host.LastFailureReason);
        }
        finally
        {
            driver.Stop();
            host.Hide();
            foreground.AssertNoActivation();
        }
    }

    [STATestMethod]
    [DataRow(72)]
    [DataRow(100)]
    [DataRow(200)]
    public void PreparedRealHostAcceptsItsPaintedBodyAndCanRestartThePhysicalRoom(int percent)
    {
        var area = new System.Drawing.Rectangle(-20000, -20000, 1024, 640);
        Assert.IsFalse(Forms.Screen.AllScreens.Any(screen => screen.Bounds.IntersectsWith(area)));
        var geometry = new DesktopPetDisplayGeometry(area, new(-20000, -20000, 1024, 600));
        using var foreground = new OwnForegroundObservation();
        using var runtime = ProductPhysicsComposition.Default.CreateRoom();
        runtime.ConfigureBeltRoomAppearance(DangoAppearanceConfiguration.Default, beginRoom: true);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        using var driver = new LocalRoomSceneDriver(new(), runtime);
        using var host = new DesktopPetRoomHost(readDisplayGeometry: () => geometry);
        host.Bind(driver);
        host.SetDisplayPercent(percent);
        host.HealthyFrameReady += host.ConfirmReady;
        try
        {
            Enter(1);
            AssertHitAndPointer();
            // Exact production recovery order, including regeneration while
            // the old host still exists and replacement of the visible view.
            driver.Stop();
            host.StopInput();
            Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
            runtime.RegenerateDango();
            Enter(2);
            AssertHitAndPointer();
        }
        finally
        {
            driver.Stop();
            host.Hide();
            foreground.AssertNoActivation();
        }

        void Enter(long attempt)
        {
            host.Show(attempt, Forms.Screen.PrimaryScreen!);
            driver.Start(host.Scene, attempt, enterWorld: true);
            DesktopPetRoomFixture.WaitUntil(() => host.IsReady, runtime,
                diagnostic: () => host.LastSurfaceFailure + "; " + host.PetWindow?.LastFrameRejection);
            Assert.IsTrue(host.PetWindow!.InteractionEnabled);
            foreground.AssertNoActivation();
        }

        void AssertHitAndPointer()
        {
            var center = runtime.CurrentSelectedCenter!.Value;
            var displayed = new LogicalPoint(center.X, center.Y + runtime.NeutralBodyGroundOffset) + host.Scene.PresentationOffset;
            var point = host.Scene.RoomToScene(displayed);
            var scenePoint = new Point(point.X, point.Y);
            Assert.IsTrue(host.PetWindow!.HitTestScenePoint(scenePoint), "The real displayed body must qualify for both mouse buttons.");
            var screenPoint = host.Scene.PointToScreen(scenePoint);
            var packed = (nint)((uint)(ushort)(short)Math.Round(screenPoint.X) | ((uint)(ushort)(short)Math.Round(screenPoint.Y) << 16));
            Assert.AreEqual((nint)1, SendMessage(host.PetWindow.Handle, 0x0084, 0, packed), "The native hit-test must accept the same actual body point.");
            Assert.IsTrue(driver.PointerDown(scenePoint));
            var lifted = host.Scene.RoomToScene(new LogicalPoint(center.X + 80, center.Y - 100 + runtime.NeutralBodyGroundOffset) + host.Scene.PresentationOffset);
            Assert.IsTrue(driver.PointerMove(new(lifted.X, lifted.Y)));
            Assert.IsTrue(runtime.IsDragging);
            Assert.IsTrue(driver.PointerUp(new(lifted.X, lifted.Y)));
            Assert.IsFalse(runtime.IsDragging);
            foreach (var key in new[] { System.Windows.Input.Key.A, System.Windows.Input.Key.D,
                System.Windows.Input.Key.W, System.Windows.Input.Key.S, System.Windows.Input.Key.Space })
                Assert.IsFalse(driver.Key(key, down: true, repeat: false), "Desktop-pet mode handles mouse gestures without claiming gameplay keys.");
        }
    }

    [STATestMethod]
    public void FailedDesktopPreparationPreservesBoundedStageAndReleasesTheUnshownPet()
    {
        using var foreground = new OwnForegroundObservation();
        using var host = new DesktopPetRoomHost(createVirtualDesktop: () => new FailedDesktop());
        Assert.ThrowsExactly<InvalidOperationException>(() => host.Show(1, Forms.Screen.PrimaryScreen!));
        Assert.IsNull(host.PetWindow);
        Assert.IsFalse(host.HasRetainedPresentation);
        var report = host.LastFailureSnapshot;
        Assert.IsNotNull(report);
        Assert.AreEqual("pet preparation failed at attach virtual desktop: InvalidOperationException", report.Reason);
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(report).Contains("fictional private message", StringComparison.Ordinal));
        foreground.AssertNoActivation();
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    private sealed class RecordingDesktop : IDesktopPetVirtualDesktop
    {
        private readonly DesktopPetVirtualDesktop inner = new();
        internal string LastExceptionType = string.Empty;
        internal int LastExceptionCode;
        internal int CallsInsideSynchronousMessage;
        public void Attach(nint window) => inner.Attach(window);
        public bool IsCurrent
        {
            get
            {
                if ((InSendMessageEx(0) & 1) != 0) CallsInsideSynchronousMessage++;
                try { return inner.IsCurrent; }
                catch (Exception error)
                {
                    LastExceptionType = error.GetType().Name;
                    LastExceptionCode = error.HResult;
                    throw;
                }
            }
        }
        public void Dispose() => inner.Dispose();
    }

    private sealed class OneUncertainSample : IDesktopPetVirtualDesktop
    {
        internal bool RejectNext;
        internal int QueryCount;
        public void Attach(nint window) { }
        public bool IsCurrent
        {
            get
            {
                QueryCount++;
                if (!RejectNext) return true;
                RejectNext = false;
                return false;
            }
        }
        public void Dispose() { }
    }

    private sealed class FailedDesktop : IDesktopPetVirtualDesktop
    {
        public void Attach(nint window) => throw new InvalidOperationException("fictional private message");
        public bool IsCurrent => false;
        public void Dispose() { }
    }

    [DllImport("user32.dll")] private static extern uint InSendMessageEx(nint reserved);
}
