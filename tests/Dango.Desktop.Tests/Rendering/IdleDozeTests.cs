using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class IdleDozeTests
{
    public TestContext TestContext { get; set; } = null!;

    private static void Steps(IdleDozeController c, int count, bool busy = false, bool eligible = true)
    {
        for (var i = 0; i < count; i++) c.Advance(new(true, 0, 0), .1, eligible, busy, _ => true);
    }

    [TestMethod]
    [DataRow(false, 3000)]
    [DataRow(true, 6000)]
    public void IdleEndpointsAndBusyMovementDoNotResetDeadline(bool maximum, int steps)
    {
        var c = new IdleDozeController { Next = (min, max) => maximum ? max - 1 : min };
        Steps(c, steps - 1, true); Assert.IsFalse(c.Due);
        Steps(c, 1, true); Assert.IsTrue(c.Due); Assert.IsFalse(c.Active);
        Steps(c, 1); Assert.IsTrue(c.Active); Assert.IsFalse(c.Sleeping);
        Steps(c, 7); Assert.IsFalse(c.Sleeping);
        Steps(c, 1); Assert.IsTrue(c.Sleeping);
    }

    [TestMethod]
    public void DurationBucketsHaveExactFiveTwentyFiveSeventyWeights()
    {
        var buckets = new int[3];
        for (var bucket = 0; bucket < 100; bucket++)
        {
            var c = new IdleDozeController { Next = (min, max) => min == 0 ? bucket : min };
            Steps(c, 3008); Assert.IsTrue(c.Sleeping);
            var remaining = 0;
            while (c.Active && remaining < 6001) { Steps(c, 1); remaining++; }
            var expected = bucket < 5 ? 300 : bucket < 30 ? 1200 : 3000;
            Assert.AreEqual(expected, remaining);
            buckets[bucket < 5 ? 0 : bucket < 30 ? 1 : 2]++;
            Assert.IsFalse(c.Due);
            Steps(c, 2999); Assert.IsFalse(c.Active);
        }
        CollectionAssert.AreEqual(new[] { 5, 25, 70 }, buckets);
    }

    [TestMethod]
    [DataRow(0, 1200)]
    [DataRow(5, 3000)]
    [DataRow(30, 6000)]
    public void UpperSleepEndpointsAndIneligibleReset(int bucket, int duration)
    {
        var c = new IdleDozeController { Next = (min, max) => min == 0 ? bucket : max - 1 };
        Steps(c, 6008); Assert.IsTrue(c.Sleeping);
        Steps(c, duration - 1); Assert.IsTrue(c.Sleeping);
        Steps(c, 1); Assert.IsFalse(c.Active);
        Steps(c, 6008); Assert.IsTrue(c.Sleeping);
        Steps(c, 1, eligible: false); Assert.IsFalse(c.Active);
        Steps(c, 5999); Assert.IsFalse(c.Active);
    }

    [TestMethod]
    public void ClosingRampAndReleaseFailureLatchUntilReset()
    {
        var c = new IdleDozeController { Next = (min, _) => min };
        Steps(c, 3000);
        var openness = new List<double>();
        for (var i = 0; i < 8; i++) c.Advance(new(true, 0, 0), .1, true, false,
            intent => { if (intent is SetEyes eyes) openness.Add(eyes.Openness); return true; });
        Assert.IsTrue(openness[0] > openness[2]); Assert.AreEqual(0, openness[^1]);
        c.Cancel(_ => false); Assert.IsNotNull(c.Error); Assert.IsFalse(c.Active);
        Steps(c, 7000); Assert.IsFalse(c.Active);
        c.Reset(); Assert.IsNull(c.Error);
    }

    private static void Prime(StackedDangoSceneRuntime runtime)
    {
        var p = runtime.LastPhysicsFrame!.Bodies[0];
        runtime.Doze.Next = (min, _) => min;
        for (var i = 0; i < 3000; i++) runtime.Doze.Advance(new(true, p.CoreX, p.CoreY), .1, true, true, _ => true);
        Assert.IsTrue(runtime.Doze.Due);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RealEyesCloseAndFirstInteractionWakesWithoutChangingPhysics(bool petting)
    {
        using var runtime = LightFeedbackRuntimeTests.Create();
        using var reference = LightFeedbackRuntimeTests.Create();
        runtime.SetViewport(new LogicalRect(0, 0, 1200, 900));
        reference.SetViewport(new LogicalRect(0, 0, 1200, 900));
        LightFeedbackRuntimeTests.Steps(runtime, 200); LightFeedbackRuntimeTests.Steps(reference, 200);
        var owner = new object(); runtime.AttachQuietIdleHost(owner, true); runtime.UpdateWanderScreenWidth(owner, 1200);
        Prime(runtime);
        for (var i = 0; i < 75; i++)
        {
            LightFeedbackRuntimeTests.Steps(runtime, 1); LightFeedbackRuntimeTests.Steps(reference, 1);
            Assert.AreEqual(JsonSerializer.Serialize(reference.LastPhysicsFrame), JsonSerializer.Serialize(runtime.LastPhysicsFrame));
            Assert.AreEqual(JsonSerializer.Serialize(reference.LastFrame!.Bodies[0].Attachments!.Hat), JsonSerializer.Serialize(runtime.LastFrame!.Bodies[0].Attachments!.Hat));
            Assert.IsFalse(runtime.Wander.Active);
        }
        var face = runtime.LastFrame!.Bodies[0].Attachments!.Face;
        Assert.AreEqual(EyeShape.Sleeping, face.Left.Shape); Assert.AreEqual(EyeShape.Sleeping, face.Right.Shape);
        Assert.IsTrue(runtime.Doze.Sleeping); Assert.IsTrue(face.Left.Openness < .03 && face.Right.Openness < .03);
        Assert.IsNotNull(runtime.LastFrame.Bodies[0].DozeSeconds);
        Assert.IsTrue(runtime.TryReact(runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value, petting));
        Assert.IsFalse(runtime.Doze.Active);
        LightFeedbackRuntimeTests.Steps(runtime, 50);
        Assert.IsNull(runtime.LastFrame.Bodies[0].DozeSeconds);
        Assert.IsTrue(runtime.LastFrame.Bodies[0].Attachments!.Face.Left.Openness > .95);
        Assert.IsFalse(runtime.Wander.Active);
    }

    [TestMethod]
    [DataRow("press")]
    [DataRow("hide")]
    [DataRow("screen")]
    [DataRow("add")]
    [DataRow("direction")]
    [DataRow("reset")]
    public void LifecycleAndPressReleaseSleepWithoutReplay(string action)
    {
        using var runtime = LightFeedbackRuntimeTests.Create();
        runtime.SetViewport(new LogicalRect(0, 0, 1200, 900));
        LightFeedbackRuntimeTests.Steps(runtime, 200);
        var owner = new object(); runtime.AttachQuietIdleHost(owner, true); runtime.UpdateWanderScreenWidth(owner, 1200);
        Prime(runtime); LightFeedbackRuntimeTests.Steps(runtime, 75); Assert.IsTrue(runtime.Doze.Sleeping);
        switch (action)
        {
            case "press": Assert.IsGreaterThan(0L, runtime.BeginLightPress(runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value)); break;
            case "hide": runtime.UpdateQuietIdleHost(owner, false); break;
            case "screen": runtime.UpdateWanderScreenWidth(owner, 1920); break;
            case "add": Assert.IsTrue(runtime.AddDangoInViewport(new(0, 0, 1200, 900))); break;
            case "direction": runtime.SetDirectionalControl(new(true, false, false, false)); break;
            case "reset": runtime.RegenerateDango(); break;
        }
        Assert.IsFalse(runtime.Doze.Active);
        Assert.IsTrue(runtime.LastFrame is null || runtime.LastFrame.Bodies.All(body => body.DozeSeconds is null));
        LightFeedbackRuntimeTests.Steps(runtime, 15);
        Assert.IsFalse(runtime.Doze.Active);
        Assert.IsTrue(runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Openness > .95);
    }

    [STATestMethod]
    public void ZzzRequiresClosedEyesMovesAndDisappearsOnWake()
    {
        using var runtime = LightFeedbackRuntimeTests.Create();
        runtime.SetViewport(new LogicalRect(0, 0, 1200, 900));
        LightFeedbackRuntimeTests.Steps(runtime, 200);
        var owner = new object(); runtime.AttachQuietIdleHost(owner, true); runtime.UpdateWanderScreenWidth(owner, 1200);
        var visual = new DeformableDangoVisual();
        var layer = (Canvas)visual.FindName("DozeLayer");
        visual.ApplyFrame(runtime.LastFrame!.Bodies[0] with { DozeSeconds = 1 });
        Assert.AreEqual(Visibility.Collapsed, layer.Visibility);
        Prime(runtime); LightFeedbackRuntimeTests.Steps(runtime, 75);
        var closed = runtime.LastFrame!.Bodies[0];
        visual.HatSize = runtime.DisplayHatSize;
        visual.ApplyFrame(closed with { DozeSeconds = 2.6 });
        visual.Measure(new System.Windows.Size(160, 120)); visual.Arrange(new Rect(0, 0, 160, 120)); visual.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(480, 360, 288, 288, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        var imagePath = Path.Combine(TestContext.TestResultsDirectory!, "doze-preview.png");
        Directory.CreateDirectory(TestContext.TestResultsDirectory!);
        using (var file = File.Create(imagePath)) encoder.Save(file);
        TestContext.AddResultFile(imagePath);
        visual.ApplyFrame(closed with { DozeSeconds = 1 });
        Assert.AreEqual(Visibility.Visible, layer.Visibility); Assert.IsFalse(layer.IsHitTestVisible);
        var z = (TextBlock)layer.Children[0]; var y = Canvas.GetTop(z); var opacity = z.Opacity; var size = z.FontSize;
        visual.ApplyFrame(closed with { DozeSeconds = 2.4 });
        Assert.IsTrue(y < 32 && Canvas.GetTop(z) < y);
        Assert.IsTrue(z.FontSize > size); Assert.IsTrue(z.Opacity < opacity && z.Opacity > 0);
        var younger = (TextBlock)layer.Children[2];
        Assert.IsTrue(Canvas.GetTop(z) < Canvas.GetTop(younger));
        Assert.IsTrue(z.FontSize > younger.FontSize && z.Opacity < younger.Opacity);
        visual.ApplyFrame(closed with { DozeSeconds = null });
        Assert.AreEqual(Visibility.Collapsed, layer.Visibility);
    }
}
