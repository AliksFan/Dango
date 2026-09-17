using System.Text.Json;
using Dango.Physics.Configuration;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class DangoPhysicsStabilityTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    public TestContext TestContext { get; set; } = null!;

    private static DangoPhysicsWorld Create()
    {
        return DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
    }

    private sealed class Measurement
    {
        public int Frames { get; set; }
        public int ContactFrames { get; set; }
        public int LastContacts { get; set; }
        public float MaximumSpeed { get; set; }
        public float MinimumArea { get; set; } = float.MaxValue;
        public int FirstSlip { get; set; } = -1;
        public double Width { get; set; }
        public double Offset { get; set; }
        public float FinalUpperY { get; set; }
        public int Cycles { get; set; }
    }

    private static void Check(DangoPhysicsFrame frame, Measurement m, int count)
    {
        Assert.AreEqual(0, frame.ErrorCode, $"Native failure at frame {m.Frames}");
        Assert.AreEqual(count, frame.Bodies.Count);
        if (frame.ContactStepCount > m.LastContacts) m.ContactFrames++;
        m.LastContacts = frame.ContactStepCount;
        foreach (var b in frame.Bodies)
        {
            Assert.AreEqual(16, b.PointCount);
            Assert.IsTrue(b.Contour.Count == 32 && b.Contour.All(float.IsFinite));
            Assert.IsTrue(float.IsFinite(b.CoreX) && float.IsFinite(b.CoreY) && float.IsFinite(b.SignedArea) && float.IsFinite(b.MaximumSpeed));
            Assert.IsTrue(b.SignedArea >= DangoPhysicsDefaults.Current.MinimumArea);
            Assert.IsTrue(b.MaximumSpeed <= DangoPhysicsDefaults.Current.MaximumSpeed + 1e-5);
            m.MaximumSpeed = Math.Max(m.MaximumSpeed, b.MaximumSpeed);
            m.MinimumArea = Math.Min(m.MinimumArea, b.SignedArea);
        }
        m.Frames++;
    }

    // Samples the existing quadratic display contract; used only to measure the offset fixture.
    private static double Width(DangoBodyState body)
    {
        var scale = Math.Min(1, .98 / body.Contour.Max(v => Math.Abs(v)));
        var xs = Enumerable.Range(0, 128).Select(s =>
        {
            int n = s / 8; double t = (s % 8) / 8d;
            return (.5 * (1 - t) * (1 - t) * body.Contour[((n + 15) % 16) * 2] +
                (.5 + t - t * t) * body.Contour[n * 2] + .5 * t * t * body.Contour[((n + 1) % 16) * 2]) * scale * .9;
        }).ToArray();
        return xs.Max() - xs.Min();
    }

    [TestMethod]
    public void SustainedCollisionFiveMinutes()
    {
        using var world = Create(); var a = world.Add(0, 0); var b = world.Add(2.8, 0);
        var m = new Measurement();
        try
        {
            for (int i = 0; i < 18000; i++)
                Check(world.Step(Dt, [new(a, Horizontal: i >= 600 ? 1 : 0), new(b)]), m, 2);
            Assert.IsTrue(m.ContactFrames >= 15000, "Must actually remain in repeated contact, not merely run separated bodies.");
        }
        finally { TestContext.WriteLine(JsonSerializer.Serialize(m)); }
    }

    [TestMethod]
    [DataRow(-.05)]
    [DataRow(0d)]
    [DataRow(.05)]
    public void EccentricStackFiveMinutes(double ratio)
    {
        double width;
        using (var reference = Create())
        {
            var id = reference.Add(0, 0); DangoPhysicsFrame frame = reference.Step(Dt, [new(id)]);
            for (int i = 1; i < 600; i++) frame = reference.Step(Dt, [new(id)]);
            Assert.IsTrue(frame.Succeeded); width = Width(frame.Bodies[0]);
        }
        using var world = Create(); var bottom = world.Add(0, 0); var top = world.Add(width * ratio, 1.65);
        var m = new Measurement { Width = width, Offset = width * ratio };
        try
        {
            for (int i = 0; i < 18000; i++)
            {
                var frame = world.Step(Dt, [new(bottom), new(top)]); Check(frame, m, 2);
                var upper = frame.Bodies.Single(x => x.EntityId == top);
                var lower = frame.Bodies.Single(x => x.EntityId == bottom);
                if (i >= 600 && (upper.IsGrounded || upper.CoreY <= lower.CoreY + .5) && m.FirstSlip < 0) m.FirstSlip = i;
                m.FinalUpperY = upper.CoreY;
            }
            Assert.IsTrue(m.ContactFrames > 0, "Stack must make real contact.");
            // User accepted natural slipping after the original red report; retain the observation,
            // while numerical safety and real contact remain mandatory.
            TestContext.WriteLine($"Accepted natural-slip observation: first slip in observation window={m.FirstSlip}.");
        }
        finally { TestContext.WriteLine(JsonSerializer.Serialize(m)); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void WithdrawBottomSupport(bool remove)
    {
        float untouchedUpperY;
        using (var reference = Create())
        {
            var lower = reference.Add(0, 0); var upper = reference.Add(0, 1.65);
            DangoPhysicsFrame f = reference.Step(Dt, [new(lower), new(upper)]);
            for (int i = 1; i < 180; i++) f = reference.Step(Dt, [new(lower), new(upper)]);
            Assert.IsTrue(f.Succeeded && !f.Bodies[1].IsGrounded);
            untouchedUpperY = f.Bodies[1].CoreY;
        }
        using var world = Create(); var bottom = world.Add(0, 0); var top = world.Add(0, 1.65);
        var m = new Measurement();
        try
        {
            DangoPhysicsFrame frame = world.Step(Dt, [new(bottom), new(top)]); Check(frame, m, 2);
            for (int i = 1; i < 120; i++) { frame = world.Step(Dt, [new(bottom), new(top)]); Check(frame, m, 2); }
            var before = frame.Bodies.Single(x => x.EntityId == top);
            Assert.IsTrue(m.ContactFrames > 0 && before.CoreY > frame.Bodies[0].CoreY + .5 && !before.IsGrounded,
                "Withdrawal must start with a genuinely supported upper body.");
            if (remove) world.Remove(bottom);
            bool fell = false;
            for (int i = 0; i < 1800; i++)
            {
                frame = world.Step(Dt, remove ? [new(top)] :
                    [new(bottom, DragActive: i < 180, DragTargetX: -3, DragTargetY: .8), new(top)]);
                Check(frame, m, remove ? 1 : 2);
                var upper = frame.Bodies.Single(x => x.EntityId == top);
                fell |= upper.CoreY < before.CoreY - .5;
                if (i == 59)
                {
                    TestContext.WriteLine($"Withdrawal one-second upperY={upper.CoreY:R}; untouched upperY={untouchedUpperY:R}");
                    Assert.IsTrue(upper.CoreY < untouchedUpperY - .25, "Withdrawal must cause earlier falling than the untouched stack.");
                }
            }
            var end = frame.Bodies.Single(x => x.EntityId == top); m.FinalUpperY = end.CoreY;
            Assert.IsTrue(fell && end.IsGrounded && end.MaximumSpeed < .03, "Upper body must fall and settle after support withdrawal.");
            if (remove) Assert.AreEqual(1, world.Count);
        }
        finally { TestContext.WriteLine(JsonSerializer.Serialize(m)); }
    }

    [TestMethod]
    public void OneHundredDragReleaseCycles()
    {
        using var world = Create(); var a = world.Add(0, 0); var b = world.Add(2.8, 0); var m = new Measurement();
        try
        {
            for (int cycle = 0; cycle < 100; cycle++)
            {
                for (int i = 0; i < 180; i++)
                {
                    bool dragging = i < 60;
                    var f = world.Step(Dt, [new(a, DragActive: dragging, DragTargetX: cycle % 2 == 0 ? 2.6 : 0, DragTargetY: 2), new(b)]);
                    Check(f, m, 2);
                    Assert.AreEqual(dragging, f.Bodies[0].SelectedDragNode >= 0, "Drag attachment must engage and release.");
                }
                m.Cycles++;
            }
            Assert.IsTrue(m.ContactFrames > 0, "Drag sequence must exercise inter-body collision.");
        }
        finally { TestContext.WriteLine(JsonSerializer.Serialize(m)); }
    }

    [TestMethod]
    public void OneHundredResetsPreserveExactInitialTrajectory()
    {
        using var world = Create(); var a = world.Add(0, 0); var b = world.Add(0, 1.65); var m = new Measurement();
        var reference = new List<string>();
        try
        {
            for (int cycle = 0; cycle <= 100; cycle++)
            {
                if (cycle > 0) { world.Reset(); m.LastContacts = 0; }
                for (int i = 0; i < 120; i++)
                {
                    var frame = world.Step(Dt, [new(a), new(b)]); Check(frame, m, 2);
                    var state = JsonSerializer.Serialize(frame);
                    if (cycle == 0) reference.Add(state);
                    else Assert.AreEqual(reference[i], state, $"Reset {cycle}, frame {i}: trajectory changed.");
                }
                if (cycle > 0) m.Cycles++;
            }
        }
        finally { TestContext.WriteLine(JsonSerializer.Serialize(m)); }
    }

    [TestMethod]
    public void ExplicitTwoBodyCapacityStillRejectsThird()
    {
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, capacity: 2); var a = world.Add(0, 0); var b = world.Add(0, 1.65);
        var error = Assert.ThrowsExactly<DangoPhysicsException>(() => world.Add(0, 3.3));
        Assert.AreEqual(DangoPhysicsFailureKind.CapacityExceeded, error.Kind);
        Assert.AreEqual(-411, error.NativeErrorCode);
        Assert.AreEqual(2, world.Count);
        Assert.IsTrue(world.Step(Dt, [new(a), new(b)]).Succeeded);
        TestContext.WriteLine("Explicit capacity=2 rejects third Add=-411. Default capacity=3 is validated separately.");
    }

    [TestMethod]
    [DataRow(-.05)]
    [DataRow(0d)]
    [DataRow(.05)]
    public void StackSlipDiagnostic(double ratio)
    {
        double width;
        using (var reference = Create())
        {
            var id = reference.Add(0, 0); DangoPhysicsFrame f = reference.Step(Dt, [new(id)]);
            for (int i = 1; i < 600; i++) f = reference.Step(Dt, [new(id)]);
            Assert.IsTrue(f.Succeeded); width = Width(f.Bodies[0]);
        }
        using var world = Create(); var a = world.Add(0, 0); var b = world.Add(width * ratio, 1.65);
        var m = new Measurement(); var trace = new List<object>(); int firstSupport = -1, firstGround = -1;
        try
        {
            for (int i = 0; i < 1200; i++)
            {
                var f = world.Step(Dt, [new(a), new(b)]); Check(f, m, 2);
                var lower = f.Bodies[0]; var upper = f.Bodies[1];
                if (firstSupport < 0 && f.ContactStepCount > 0 && !upper.IsGrounded && upper.CoreY > lower.CoreY + .5) firstSupport = i;
                if (firstSupport >= 0 && firstGround < 0 && upper.IsGrounded) firstGround = i;
                if (i % 30 == 0 || i == firstSupport || i == firstGround)
                    trace.Add(new
                    {
                        Frame = i,
                        LowerX = lower.CoreX,
                        LowerY = lower.CoreY,
                        UpperX = upper.CoreX,
                        UpperY = upper.CoreY,
                        upper.IsGrounded,
                        f.ContactStepCount
                    });
            }
        }
        finally { TestContext.WriteLine(JsonSerializer.Serialize(new { DiagnosticOnly = true, ratio, width, firstSupport, firstGround, trace })); }
    }
}
