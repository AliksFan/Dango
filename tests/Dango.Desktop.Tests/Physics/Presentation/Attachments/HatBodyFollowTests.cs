using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class HatBodyFollowTests
{
    [TestMethod]
    public void AbruptStopCoastsForwardThenReboundsOnBothAxes()
    {
        foreach (var vertical in new[] { false, true })
        foreach (var sign in new[] { -1, 1 })
        {
            var follow = new HatBodyFollow();
            // Establish steady travel so the stop is isolated from startup.
            for (var i = 0; i <= 120; i++) Tick(follow, vertical ? 0 : sign * i / 60d, vertical ? sign * i / 60d : 0);
            var values = new List<double>();
            for (var i = 0; i < 180; i++)
            {
                Tick(follow, vertical ? 0 : sign * 2, vertical ? sign * 2 : 0);
                values.Add(sign * (vertical ? follow.Current.Lift : follow.Current.Slide));
            }
            Assert.IsTrue(values[0] > 0, "Stopping must immediately preserve forward inertia.");
            var peak = values.Max();
            Assert.IsTrue(peak > (vertical ? 0.012 : 0.025), $"Visible coast missing: vertical={vertical}, peak={peak}");
            Assert.IsTrue(values.IndexOf(peak) >= 1, "Coast must continue after the first stopped frame.");
            Assert.IsTrue(values.Skip(values.IndexOf(peak)).Min() < -0.0005, "Spring must visibly cross neutral on return.");
            Assert.AreEqual(0, values[^1], 0.00001);
        }
    }

    [TestMethod]
    public void RenderedTrackPreservesCoastAndReboundAfterStop()
    {
        foreach (var sign in new[] { -1, 1 })
        {
            var frame = EyeMotionTests.Body();
            var motion = new HatMotionController(frame);
            motion.Prepare(AttachmentShapeGeometry.Create(frame.Contour), frame.Hat);
            void Step(double x)
            {
                motion.Observe(new(x, 0), 1, 1d / 60);
                for (var j = 0; j < 4; j++) motion.Advance(1d / 240, 0, 0);
            }
            for (var i = 0; i <= 120; i++) Step(sign * i / 60d);
            var values = new List<double>();
            for (var i = 0; i < 180; i++) { Step(sign * 2); values.Add(sign * motion.Current.S); }
            Assert.IsTrue(values.Max() > 0.02, $"Rendered coast was filtered away: {values.Max()}");
            Assert.IsTrue(values.Skip(values.IndexOf(values.Max())).Min() < -0.0005);
            Assert.AreEqual(0, values[^1], 0.00001);
        }
    }

    [TestMethod]
    public void ConstantVelocityAndResumeDoNotInventMotionAndLargeInputsStayBounded()
    {
        var follow = new HatBodyFollow();
        for (var i = 0; i < 180; i++) Tick(follow, i * 0.02, i * 0.01);
        Assert.AreEqual(0, follow.Current.Lift, 1e-10); Assert.AreEqual(0, follow.Current.Bend, 1e-10);
        follow.ResetSignals(); Tick(follow, 100, 100); Tick(follow, 100, 100);
        Assert.AreEqual(0, follow.Current.Lift, 1e-10);
        for (var i = 0; i < 600; i++)
        {
            follow.Observe(new(i % 2 == 0 ? 1 : -1, i % 3), i % 2 == 0 ? 0.5 : 1.5, 1d / 60);
            for (var j = 0; j < 4; j++) follow.Advance(1d / 240, 0.0001);
            Assert.IsTrue(follow.Current.Lift <= 0.0001 && follow.Current.Lift >= -HatBodyFollow.LiftLimit);
            Assert.IsTrue(Math.Abs(follow.Current.Bend) <= HatBodyFollow.DeformationLimit && Math.Abs(follow.Current.Stretch) <= HatBodyFollow.DeformationLimit);
            Assert.IsTrue(Math.Abs(follow.Current.Slide) <= HatBodyFollow.SlideLimit);
        }
    }

    [TestMethod]
    public void SmoothMotionAgreesAcrossFrameRates()
    {
        HatFollowFrame[] Run(int hz)
        {
            var follow = new HatBodyFollow(); var result = new List<HatFollowFrame>();
            for (var i = 0; i < hz * 2; i++)
            {
                var t = (i + 1d) / hz;
                follow.Observe(new(Math.Sin(t * 3) * 0.2, Math.Cos(t * 3) * 0.2), 1 + Math.Sin(t * 2) * 0.05, 1d / hz);
                for (var j = 0; j < 240 / hz; j++) follow.Advance(1d / 240, HatBodyFollow.LiftLimit);
                if ((i + 1) % (hz / 10) == 0) result.Add(follow.Current);
            }
            return result.ToArray();
        }
        var reference = Run(120);
        foreach (var hz in new[] { 30, 60 })
            foreach (var (a, b) in reference.Zip(Run(hz)))
            {
                Assert.AreEqual(a.Lift, b.Lift, 0.002);
                Assert.AreEqual(a.Bend, b.Bend, 0.005); Assert.AreEqual(a.Stretch, b.Stretch, 0.005);
            }
    }
    private static void Tick(HatBodyFollow follow, double x, double y, double aspect = 1)
    {
        follow.Observe(new(x, y), aspect, 1d / 60);
        for (var i = 0; i < 4; i++) follow.Advance(1d / 240, HatBodyFollow.LiftLimit);
    }

    [TestMethod]
    public void AccelerationMovesRootAndTipThenSettlesWithoutIdleOscillation()
    {
        var follow = new HatBodyFollow(); double y = 0;
        for (var i = 0; i < 10; i++) Tick(follow, 0, y);
        for (var i = 0; i < 8; i++) { y += 0.03; Tick(follow, 0, y); }
        Assert.IsTrue(follow.Current.Lift < -0.0001, "Hat should initially lag an upward moving body.");
        Assert.IsTrue(follow.Current.Stretch < -0.0001);
        for (var i = 0; i < 300; i++) Tick(follow, 0, y);
        Assert.AreEqual(0, follow.Current.Lift, 1e-8); Assert.AreEqual(0, follow.Current.Stretch, 1e-8);
    }

    [TestMethod]
    public void HorizontalResponseMirrorsAndSquashCompressesTip()
    {
        var left = new HatBodyFollow(); var right = new HatBodyFollow();
        for (var i = 0; i < 10; i++) { Tick(left, 0, 0); Tick(right, 0, 0); }
        for (var i = 1; i <= 8; i++) { Tick(left, -i * 0.03, 0); Tick(right, i * 0.03, 0); }
        Assert.IsTrue(right.Current.Bend < -0.0001);
        Assert.AreEqual(-left.Current.Bend, right.Current.Bend, 1e-10);
        var squash = new HatBodyFollow(); Tick(squash, 0, 0); Tick(squash, 0, 0);
        for (var i = 1; i < 8; i++) Tick(squash, 0, 0, 1 - i * 0.02);
        Assert.IsTrue(squash.Current.Stretch < -0.0001);
    }
}
