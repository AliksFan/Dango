using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.StandardWindow;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Tests.Physics.Presentation.Attachments;
using Dango.Physics;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class AttachmentSceneIntegrationTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    private static readonly LogicalRect Bounds = new(400, 500, 160, 120);
    private static PhysicsPresentationInput Primary(LogicalRect? bounds = null) => new(
        "fixture-dango", bounds ?? Bounds, DangoActionState.Idle, 1000, 17,
        "fixture-ground", new(0, -1), 0, 1, false, true, []);

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void NewOldAndDirectPhysicsAgreeForNineHundredSteps(int count)
    {
        using var candidate = StackedDangoSceneRuntime.CreateEyeHatReview();
        using var old = new StackedDangoSceneRuntime();
        using var direct = DangoPhysicsWorld.Create(Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName));
        var ids = new List<DangoEntityId> { direct.Add(0, 0) };
        candidate.Start(); old.Start();
        for (var i = 1; i < count; i++) { candidate.AddDango(); old.AddDango(); ids.Add(direct.Add(i == 1 ? -2.8 : 2.8, 0)); }
        for (var i = 0; i < 900; i++)
        {
            var input = new DirectionalControlIntent(i is >= 100 and < 130, i is >= 160 and < 190, i == 240, i is >= 300 and < 330);
            candidate.SetDirectionalControl(input); old.SetDirectionalControl(input);
            var a = candidate.Tick(Primary(), Dt); var b = old.Tick(Primary(), Dt);
            var c = direct.Step(Dt, ids.Select((id, j) => j == 0 ? new DangoPhysicsInput(id,
                input.Left ? -1 : input.Right ? 1 : 0, input.Squash ? 1 : 0, input.Jump) : new(id)).ToArray());
            Assert.IsTrue(c.Succeeded, $"count={count}, step={i}");
            Assert.AreEqual(JsonSerializer.Serialize(c), JsonSerializer.Serialize(candidate.LastPhysicsFrame));
            Assert.AreEqual(JsonSerializer.Serialize(c), JsonSerializer.Serialize(old.LastPhysicsFrame));
            Assert.IsNull(candidate.AttachmentError);
            for (var j = 0; j < count; j++)
            {
                Assert.IsNull(b.Bodies[j].Attachments, "The explicit legacy comparison must keep the old route.");
                var attachment = a.Bodies[j].Attachments!;
                Assert.IsNotNull(attachment);
                Assert.AreEqual((long)i, attachment.StepIndex);
                Assert.IsTrue(attachment.Face.Left.Visible && attachment.Face.Right.Visible);
                Assert.IsNotNull(attachment.Hat!.Pose);
                CollectionAssert.AreEqual(b.Bodies[j].Contour.ToArray(), a.Bodies[j].Contour.ToArray());
            }
        }
    }

    [TestMethod]
    public void BackToBackAdditionsReserveSeparateSpawnPositionsBeforeNextTick()
    {
        using var runtime = new StackedDangoSceneRuntime(); runtime.Start();
        var viewport = new LogicalRect(0, 0, 960, 720);
        runtime.SetViewport(viewport);
        for (var i = 0; i < 180; i++) runtime.Tick(Primary(), Dt);
        Assert.IsTrue(runtime.AddDangoInViewport(viewport));
        Assert.IsTrue(runtime.AddDangoInViewport(viewport));
        for (var i = 0; i < 180; i++)
        {
            runtime.Tick(Primary(), Dt);
            Assert.IsNull(runtime.LastError, $"Back-to-back spawn step {i}: {runtime.LastError}");
        }
    }

    [TestMethod]
    public void PauseMigrationRemovalAndResetPreserveOrInvalidateTheCorrectState()
    {
        using var runtime = StackedDangoSceneRuntime.CreateEyeHatReview(); runtime.Start();
        runtime.AddDango(); runtime.AddDango();
        for (var i = 0; i < 100; i++) runtime.Tick(Primary(), Dt);
        var selected = runtime.SelectedEntity!.Value;
        Assert.IsTrue(runtime.SubmitAttachment(new SetEyes(EyeSelection.Both, 1, EyeShape.Happy, 1)).Accepted);
        var before = runtime.Tick(Primary(), Dt);
        Assert.AreEqual(EyeShape.Happy, before.Bodies[0].Attachments!.Face.Left.Shape);
        Assert.AreEqual(EyeShape.Round, before.Bodies[1].Attachments!.Face.Left.Shape);
        runtime.Stop();
        Assert.IsFalse(runtime.SubmitAttachment(Blink.Default(EyeSelection.Both)).Accepted);
        Assert.AreSame(before, runtime.LastFrame);
        runtime.Start();
        var moved = runtime.Tick(Primary(new(100, 200, 160, 120)), Dt);
        Assert.AreEqual(before.Bodies[0].Attachments!.StepIndex + 1, moved.Bodies[0].Attachments!.StepIndex);
        Assert.AreEqual(before.Bodies[0].Attachments!.Generation, moved.Bodies[0].Attachments!.Generation);
        Assert.AreEqual(EyeShape.Happy, moved.Bodies[0].Attachments!.Face.Left.Shape);
        var generation = moved.Bodies[0].Attachments!.Generation;
        runtime.RegenerateDango();
        var reset = runtime.Tick(Primary(new(100, 200, 160, 120)), Dt);
        Assert.AreEqual(selected, reset.EntityIds[0]);
        Assert.IsTrue(reset.Bodies[0].Attachments!.Generation > generation);
        Assert.AreEqual(EyeShape.Round, reset.Bodies[0].Attachments!.Face.Left.Shape);
        runtime.RemoveDango(); runtime.AddDango();
        var added = runtime.Tick(Primary(new(100, 200, 160, 120)), Dt);
        Assert.IsFalse(added.EntityIds.Contains(selected));
        Assert.IsTrue(added.Bodies.All(b => b.Attachments!.Face.Left.Shape == EyeShape.Round));
    }

    [TestMethod]
    public void FailedPhysicsFreezesAttachmentsAndFirstFailureDoesNotInventThem()
    {
        using var scene = new DesktopPhysicsScene(Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName), Bounds);
        var frame = scene.Step(Dt);
        var failed = frame with { Physics = frame.Physics with { ErrorCode = -999 } };
        using var controller = new AttachmentSceneController(EyeMotionTests.Body());
        Assert.IsNull(controller.Advance(failed, Dt.TotalSeconds, scene.SelectedEntity, DangoFacingDirection.Right)[0].Attachments);
        var first = controller.Advance(frame, Dt.TotalSeconds, scene.SelectedEntity, DangoFacingDirection.Right)[0].Attachments;
        Assert.AreSame(first, controller.Advance(failed, Dt.TotalSeconds, scene.SelectedEntity, DangoFacingDirection.Left)[0].Attachments);
    }

    [STATestMethod]
    public void BothCompiledHostsAndOverlayReadSameFramesWithoutAdvancingThem()
    {
        using var runtime = StackedDangoSceneRuntime.CreateEyeHatReview();
        var session = new DesktopSceneSession();
        var window = new DangoSceneWindow(session, runtime);
        var wallpaper = new WorkerWWindow(session, runtime);
        var overlay = new DesktopDangoSceneOverlayWindow(p => new(p.X, p.Y), p => new(p.X, p.Y), (_, _) => false, _ => { }, () => { });
        try
        {
            runtime.Start(); runtime.AddDango(); runtime.AddDango();
            var frame = runtime.Tick(Primary(), Dt);
            Assert.IsTrue(runtime.TryApplyHatPreset("bowler"));
            foreach (var host in new object[] { window, wallpaper })
            {
                var apply = host.GetType().GetMethod("ApplyPresentationFrame", BindingFlags.NonPublic | BindingFlags.Instance)!;
                for (var repeat = 0; repeat < 3; repeat++) apply.Invoke(host, [frame]);
                var visual = (DeformableDangoVisual)((System.Windows.FrameworkElement)host).FindName("DangoVisual");
                var hat = (DangoHatVisual)((System.Windows.FrameworkElement)host).FindName("DangoHat");
                Assert.AreEqual(1.35, hat.HatSize); Assert.AreEqual(HatAppearance.Bowler, hat.Appearance);
                Assert.AreEqual(frame.EntityIds[0], visual.Tag);
            }
            overlay.ApplyFrame(frame, runtime.DisplayHatAppearance, runtime.DisplayHatSize);
            var hats = ((Canvas)overlay.Content).Children.OfType<DangoHatVisual>().ToArray();
            Assert.IsTrue(hats.All(h => h.HatSize == 1.35 && h.Appearance == HatAppearance.Bowler));
            Assert.AreSame(frame, runtime.LastFrame);
            Assert.AreEqual(0L, frame.Bodies[0].Attachments!.StepIndex);
        }
        finally { window.PrepareForExit(); window.Close(); wallpaper.Close(); overlay.Close(); session.Exit(2000); }
    }
}
