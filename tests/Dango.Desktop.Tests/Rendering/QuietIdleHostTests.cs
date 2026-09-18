using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.StandardWindow;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class QuietIdleHostTests
{
    [STATestMethod]
    public void WallpaperForegroundEdgeCancelsOnceAndBackgroundCanWaitAgain()
    {
        using var runtime = LightFeedbackRuntimeTests.Create();
        var scene = new DesktopSceneSession();
        var host = new WorkerWWindow(scene, runtime);
        try
        {
            runtime.QuietIdle.Next = (min, _) => min;
            runtime.AttachQuietIdleHost(host, true);
            host.ObserveQuietIdleForeground(true);
            QuietIdleRuntimeTests.WaitFor(runtime, QuietIdleAction.Blink);
            host.ObserveQuietIdleForeground(false);
            Assert.IsFalse(runtime.HasQuietIdleAction);
            var target = runtime.GetLightInteractionTarget(runtime.EntityIds[0]);
            var seen = false;
            for (var i = 0; i < 260; i++)
            {
                host.ObserveQuietIdleForeground(false);
                LightFeedbackRuntimeTests.Steps(runtime, 1);
                seen |= runtime.HasQuietIdleAction;
            }
            Assert.AreEqual(target, runtime.GetLightInteractionTarget(runtime.EntityIds[0]));
            Assert.IsTrue(seen, "A persistent background state must not cancel every frame.");
            host.SetInteractionEnabled(false);
            Assert.IsFalse(runtime.HasQuietIdleAction);
        }
        finally { host.Close(); scene.Exit(1000); }
    }

    [STATestMethod]
    public void WindowDisableCancelsAndCoordinatorShowsCancellationError()
    {
        using var runtime = LightFeedbackRuntimeTests.Create();
        var scene = new DesktopSceneSession();
        var window = new DangoSceneWindow(scene, runtime);
        var wallpaper = new WorkerWWindow(scene, runtime);
        try
        {
            runtime.QuietIdle.Next = (min, _) => min;
            runtime.AttachQuietIdleHost(window, true);
            QuietIdleRuntimeTests.WaitFor(runtime, QuietIdleAction.LookLeft);
            window.SetInteractionEnabled(false);
            Assert.IsFalse(runtime.HasQuietIdleAction);
            runtime.UpdateQuietIdleHost(window, true);
            QuietIdleRuntimeTests.WaitFor(runtime, QuietIdleAction.LookLeft);
            for (var i = 0; i < 64; i++) Assert.IsTrue(runtime.SubmitAttachment(new Dango.Core.Animation.SetHatSlide(0)).Accepted);
            window.SetInteractionEnabled(false);
            var coordinator = new DesktopHostCoordinator(wallpaper, window, runtime);
            Assert.AreEqual(runtime.QuietIdleError, coordinator.GetSnapshot().LastError);
            Assert.IsNotNull(coordinator.GetSnapshot().LastError);
        }
        finally { window.PrepareForExit(); window.Close(); wallpaper.Close(); scene.Exit(1000); }
    }
}
