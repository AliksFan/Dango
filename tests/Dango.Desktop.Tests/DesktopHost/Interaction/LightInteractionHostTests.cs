using System.Reflection;
using System.Windows.Input;
using System.Windows.Interop;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Desktop.DesktopHost.StandardWindow;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Tests.Rendering;
using Message = System.Windows.Forms.Message;

namespace Dango.Desktop.Tests.DesktopHost.Interaction;

[TestClass]
public sealed class LightInteractionHostTests
{
    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompiledWpfHostsCancelOnlyTheirCurrentCaptureAndDisableClearsFeedback(bool wallpaper)
    {
        using var runtime = LightFeedbackRuntimeTests.Create();
        var scene = new DesktopSceneSession();
        object host = wallpaper ? new WorkerWWindow(scene, runtime) : new DangoSceneWindow(scene, runtime);
        try
        {
            var session = Get<LightInteractionSession>(host, "lightInteraction");
            var visual = new DeformableDangoVisual();
            var other = new DeformableDangoVisual();
            var point = DangoHostFrameBinding.Bounds(runtime.LastFrame!, 0).Center;
            var releaseCount = 0;
            Assert.IsTrue(session.TryBegin(runtime.EntityIds[0], point, 0, false, () => true,
                () => releaseCount++, () => true));
            Set(host, "capturedDango", visual);
            var args = new MouseEventArgs(Mouse.PrimaryDevice, 0);
            Invoke(host, "OnDangoLostMouseCapture", other, args);
            Assert.IsTrue(session.IsActive, "A late event from another body must not cancel this press.");
            Invoke(host, "OnDangoLostMouseCapture", visual, args);
            Assert.IsFalse(session.IsActive);
            Assert.AreEqual(1, releaseCount);
            session.End(point, 1, false);
            Assert.IsFalse(runtime.HasLightFeedback);

            Assert.IsTrue(session.TryBegin(runtime.EntityIds[0], point, 10, false, () => true, () =>
            {
                Invoke(host, "OnDangoLostMouseCapture", visual, args);
            }, () => true));
            session.End(point, 20, false);
            Assert.IsTrue(runtime.HasLightFeedback, "Normal release must not undo the accepted blink.");
            ((IDesktopHostControl)host).SetInteractionEnabled(false);
            Assert.IsFalse(runtime.HasLightFeedback);
            LightFeedbackRuntimeTests.Steps(runtime, 12);
            Assert.AreEqual(1, runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Openness, 1e-8);
        }
        finally
        {
            if (host is DangoSceneWindow window) { window.PrepareForExit(); window.Close(); }
            else ((WorkerWWindow)host).Close();
            scene.Exit(1000);
        }
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeMessagesDistinguishNormalUpFromCaptureLoss(bool overlayPath)
    {
        using var runtime = LightFeedbackRuntimeTests.Create();
        var session = new LightInteractionSession(runtime);
        var point = DangoHostFrameBinding.Bounds(runtime.LastFrame!, 0).Center;
        var endCount = 0;
        var cancelCount = 0;
        void End() { endCount++; session.End(point, 10, false); }
        void Cancel() { cancelCount++; session.Cancel(); }
        // A hidden, test-owned native parent; never attach to or alter Explorer.
        using var parent = new HwndSource(new HwndSourceParameters("light-input-fixture")
        { Width = 1, Height = 1, WindowStyle = 0 });
        var target = new WorkerWDesktopTarget(parent.Handle, parent.Handle, 0, 0, false);
        object surface = overlayPath
            ? new DesktopDangoSceneOverlayWindow(_ => point, _ => new(0, 0), (_, _) => false, _ => { }, Cancel, _ => End())
            : new WorkerWInputProxy(target, (_, _) => false, (_, _) => { }, () => Assert.Fail("Legacy completion used"), Cancel, (_, _) => End());
        try
        {
            Action release = surface is WorkerWInputProxy proxy ? proxy.ReleasePointerCapture : ((DesktopDangoSceneOverlayWindow)surface).ReleasePointerCapture;
            Assert.IsTrue(session.TryBegin(runtime.EntityIds[0], point, 0, false, () => true, release, () => true));
            Set(surface, "dragging", true);
            Send(surface, 0x0202);
            Assert.AreEqual(1, endCount);
            Assert.AreEqual(0, cancelCount);
            Assert.IsFalse(Get<bool>(surface, "dragging"));
            Assert.IsTrue(runtime.HasLightFeedback);
            Send(surface, 0x0215);
            Assert.IsTrue(runtime.HasLightFeedback, "Late capture loss after normal Up must be ignored.");
            runtime.CancelInput();

            Assert.IsTrue(session.TryBegin(runtime.EntityIds[0], point, 20, false, () => true, release, () => true));
            Set(surface, "dragging", true);
            // A real nonzero handle distinguishes a lost capture from GetCapture()==0.
            if (surface is DesktopDangoSceneOverlayWindow) Set(surface, "handle", parent.Handle);
            Send(surface, 0x0215);
            Assert.AreEqual(1, cancelCount);
            Assert.IsFalse(session.IsActive);
            Assert.IsFalse(runtime.HasLightFeedback);
            Send(surface, 0x0202);
            Assert.AreEqual(1, endCount);
        }
        finally
        {
            if (surface is WorkerWInputProxy proxy) proxy.Dispose();
            else ((DesktopDangoSceneOverlayWindow)surface).Close();
        }
    }

    [TestMethod]
    [DataRow(1d)]
    [DataRow(1.5d)]
    public void NativeDeviceDeltasPreserveDipThresholdsAndFixedOrigin(double scale)
    {
        var origin = new LogicalPoint(400, 300);
        var controller = new LightInteractionController();
        var target = new LightInteractionTarget(new(1), 1);
        var bounds = new LogicalRect(360, 260, 120, 120);
        Assert.IsTrue(controller.TryBegin(target, origin, 0, false, out var id));
        var before = WorkerWSurfaceCoordinateMapper.OffsetFromDevice(origin, 2.9 * scale, 0, new(scale, scale));
        Assert.AreEqual(LightInteractionResult.None, controller.Move(id, before, 1, false, bounds));
        var drag = WorkerWSurfaceCoordinateMapper.OffsetFromDevice(origin, 3 * scale, 0, new(scale, scale));
        Assert.AreEqual(LightInteractionResult.BeginDrag, controller.Move(id, drag, 2, false, bounds));
        Assert.IsTrue(controller.TryBegin(target, origin, 10, true, out id));
        var pet = WorkerWSurfaceCoordinateMapper.OffsetFromDevice(origin, 16 * scale, 8 * scale, new(scale, scale));
        Assert.AreEqual(LightInteractionResult.Pet, controller.End(id, pet, 610, true, bounds));
    }

    private static void Send(object surface, int message)
    {
        if (surface is WorkerWInputProxy proxy)
            Invoke(proxy, "WndProc", Message.Create(proxy.Handle, message, 0, 0));
        else Invoke(surface, "WindowMessageHook", (nint)0, message, (nint)0, (nint)0, false);
    }

    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
}
