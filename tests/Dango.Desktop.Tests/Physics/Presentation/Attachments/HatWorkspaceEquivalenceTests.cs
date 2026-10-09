using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Tests.Physics.Presentation.Attachments.Reference;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class HatWorkspaceEquivalenceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RepeatedRemapSlideFollowAndUnavailableRecoveryMatchFrozenMotionBitForBit(bool seeded)
    {
        var baseline = seeded ? EyeMotionTests.Body() : null;
        var actual = new HatMotionController(baseline);
        var expected = new FrozenMotion(baseline);
        AssertFrame(expected.Current, actual.Current);
        var reference = new PresentationAttachment(new(0.04, 0.75), 0.2);
        for (var frame = 0; frame < 64; frame++)
        {
            var contour = frame switch
            {
                24 or 25 => AttachmentShapeGeometryTests.Oval(width: 0.015),
                < 12 => AttachmentShapeGeometryTests.Oval(),
                < 24 => AttachmentShapeGeometryTests.Oval(height: 0.6 + frame % 4 * 0.03, shear: -0.08),
                < 40 => AttachmentShapeGeometryTests.Oval(shear: frame % 2 == 0 ? 0.16 : -0.16),
                < 52 => HatTrackGeometryTests.FlatTop(),
                _ => AttachmentShapeGeometryTests.Oval()
            };
            var shape = AttachmentShapeGeometry.Create(contour);
            expected.Prepare(shape, reference);
            actual.Prepare(shape, reference);
            AssertFrame(expected.Current, actual.Current);
            if (frame is 5 or 18 or 24 or 38 or 52)
            {
                var pulse = frame % 2 == 0 ? -0.3 : 0.2;
                expected.Pulse(pulse); actual.Pulse(pulse);
                AssertFrame(expected.Current, actual.Current);
            }
            if (frame == 30) { expected.ResetSignals(); actual.ResetSignals(); }
            var x = frame < 18 ? frame * 0.025 : frame < 36 ? 0.425 : 0.425 - (frame - 36) * 0.02;
            var y = frame < 20 ? Math.Sin(frame * 0.13) * 0.08 : 0;
            var aspect = 1 + Math.Sin(frame * 0.17) * 0.1;
            var seconds = (frame % 3) switch { 0 => 1d / 30, 1 => 1d / 60, _ => 1d / 120 };
            expected.Observe(new(x, y), aspect, seconds);
            actual.Observe(new(x, y), aspect, seconds);
            var tilt = Math.Sin(frame * 0.2) * Math.PI / 18;
            var offset = frame < 16 ? 0.8 : frame < 32 ? -0.9 : 0;
            for (var substep = 0; substep < 240 * seconds; substep++)
            {
                expected.Advance(1d / 240, tilt, offset);
                actual.Advance(1d / 240, tilt, offset);
            }
            AssertFrame(expected.Current, actual.Current);
            AssertFrame(expected.Current, actual.Current); // Reading Current must not consume cached values.
        }
        Assert.IsNotNull(actual.Current.Pose, "The unavailable crown must recover a real pose.");
    }

    [TestMethod]
    public void NeutralCacheAndRemapFitLiftAndLimitsMatchFrozenPlacementBitForBit()
    {
        var source = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(shear: 0.16));
        foreach (var reference in new[]
        {
            new PresentationAttachment(new(0, 0.8), 0),
            new PresentationAttachment(new(0.04, 0.75), 0.2),
            new PresentationAttachment(new(-0.03, 0.6), -0.25)
        })
        {
            var actual = HatPlacementSolver.Create(source, reference)!;
            var expected = FrozenHatPlacementSolver.Create(source, reference)!;
            ComparePlacement(expected, actual);
            foreach (var contour in new[]
            {
                AttachmentShapeGeometryTests.Oval(height: 0.6, shear: -0.08),
                HatTrackGeometryTests.FlatTop(),
                AttachmentShapeGeometryTests.Oval(width: 0.015),
                AttachmentShapeGeometryTests.Oval(shear: 0.16)
            })
            {
                var shape = AttachmentShapeGeometry.Create(contour);
                var next = actual.Remap(shape); var oldNext = expected.Remap(shape);
                Assert.AreEqual(oldNext is null, next is null);
                if (next is null || oldNext is null) continue;
                ComparePlacement(oldNext, next);
                actual = next; expected = oldNext;
            }
        }
    }

    [TestMethod]
    public void SharedSearchWorkspaceMatchesIndependentFitsWithoutCrossTalk()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(shear: -0.16));
        var placement = HatPlacementSolver.Create(shape, new(new(0.04, 0.75), 0.2))!;
        var saved = placement.Fit(0)!;
        var savedCenter = saved.Center; var savedAngle = saved.Angle;
        using var workspace = placement.RentWorkspace();
        var values = Enumerable.Range(0, 161).Select(i => placement.Minimum + (placement.Maximum - placement.Minimum) * i / 160d)
            .Concat([0, BitConverter.Int64BitsToDouble(long.MinValue), double.NaN, double.PositiveInfinity,
                Math.BitDecrement(placement.Minimum), Math.BitIncrement(placement.Maximum)]).ToArray();
        foreach (var s in values.Concat(values.Reverse()))
            AssertPose(placement.Fit(s), placement.Fit(s, workspace));
        var concurrent = Task.WhenAll(values.Select(s => Task.Run(() => placement.Fit(s)))).GetAwaiter().GetResult();
        for (var i = 0; i < values.Length; i++) AssertPose(placement.Fit(values[i]), concurrent[i]);
        AssertPoint(savedCenter, saved.Center); AssertBits(savedAngle, saved.Angle);
    }

    private static void ComparePlacement(FrozenHatPlacementSolver expected, HatPlacementSolver actual)
    {
        AssertBits(expected.Minimum, actual.Minimum); AssertBits(expected.Maximum, actual.Maximum);
        var values = new[] { 0d, BitConverter.Int64BitsToDouble(long.MinValue), expected.Minimum, expected.Maximum,
            Math.BitDecrement(expected.Minimum), Math.BitIncrement(expected.Maximum), double.NaN,
            double.PositiveInfinity, double.NegativeInfinity }
            .Concat(Enumerable.Range(0, 17).Select(i => expected.Minimum + (expected.Maximum - expected.Minimum) * i / 16d));
        foreach (var s in values)
        {
            var frozen = expected.Fit(s);
            AssertPose(frozen is null ? null : new(frozen.Center, frozen.Angle, frozen.S), actual.Fit(s));
            AssertBits(expected.AvailableLift(s), actual.AvailableLift(s));
        }
    }

    private static void AssertFrame(HatMotionFrame expected, HatMotionFrame actual)
    {
        AssertPose(expected.Pose, actual.Pose);
        AssertBits(expected.S, actual.S); AssertBits(expected.Velocity, actual.Velocity);
        AssertBits(expected.Minimum, actual.Minimum); AssertBits(expected.Maximum, actual.Maximum);
        Assert.AreEqual(expected.Safety, actual.Safety);
        AssertBits(expected.Follow.Lift, actual.Follow.Lift); AssertBits(expected.Follow.Bend, actual.Follow.Bend);
        AssertBits(expected.Follow.Stretch, actual.Follow.Stretch); AssertBits(expected.Follow.Slide, actual.Follow.Slide);
    }

    private static void AssertPose(HatVisualPose? expected, HatVisualPose? actual)
    {
        Assert.AreEqual(expected is null, actual is null);
        if (expected is null || actual is null) return;
        AssertPoint(expected.Center, actual.Center); AssertBits(expected.Angle, actual.Angle); AssertBits(expected.S, actual.S);
    }

    private static void AssertPoint(AttachmentPoint expected, AttachmentPoint actual)
    {
        AssertBits(expected.X, actual.X); AssertBits(expected.Y, actual.Y);
    }

    private static void AssertBits(double expected, double actual) =>
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual), $"{expected:R} != {actual:R}");

    // Frozen from HatMotionController at 5bc3215. The existing 75de095 placement
    // oracle keeps the reference independent of the new neutral/scratch caches.
    private sealed class FrozenMotion(PhysicsPresentationFrame? baseline = null)
    {
        private FrozenHatPlacementSolver? calibration = baseline is null ? null : FrozenHatPlacementSolver.Create(AttachmentShapeGeometry.Create(baseline.Contour), baseline.Hat);
        private FrozenHatPlacementSolver? placement;
        private SpringState state;
        private string safety = "Normal";
        private readonly HatBodyFollow follow = new();
        public HatMotionFrame Current
        {
            get
            {
                var pose = placement?.Fit(state.Position);
                if (pose is not null) pose = pose with { Center = pose.Center + new AttachmentPoint(0, follow.Current.Lift) };
                return new(pose is null ? null : new(pose.Center, pose.Angle, pose.S), state.Position, state.Velocity, placement?.Minimum ?? 0, placement?.Maximum ?? 0, safety) { Follow = follow.Current };
            }
        }
        public void ResetSignals() => follow.ResetSignals();
        public void Observe(HatBodySample? body, double aspect, double seconds) => follow.Observe(placement is null ? null : body, aspect, seconds);

        public void Prepare(AttachmentShapeGeometry shape, PresentationAttachment reference)
        {
            calibration ??= FrozenHatPlacementSolver.Create(shape, reference);
            var next = calibration?.Remap(shape);
            if (next is null) { placement = null; state = new(0, 0); follow.ResetSignals(); safety = "Unavailable"; return; }
            safety = "Normal";
            if (placement is not null && placement.Fit(state.Position) is { } old)
            {
                var desired = old.Center + next.Fit(0)!.Center - placement.Fit(0)!.Center;
                double lo = next.Minimum, hi = next.Maximum;
                for (var i = 0; i < 20; i++)
                {
                    var a = lo + (hi - lo) / 3; var b = hi - (hi - lo) / 3;
                    if ((next.Fit(a)!.Center - desired).Length <= (next.Fit(b)!.Center - desired).Length) hi = b;
                    else lo = a;
                }
                var mapped = (lo + hi) / 2;
                var unchanged = next.Fit(Math.Clamp(state.Position, next.Minimum, next.Maximum));
                if (unchanged is not null && (unchanged.Center - desired).Length < 1e-9) mapped = Math.Clamp(state.Position, next.Minimum, next.Maximum);
                state = state with { Position = mapped };
            }
            if (state.Position < next.Minimum || state.Position > next.Maximum)
            { state = state with { Position = Math.Clamp(state.Position, next.Minimum, next.Maximum) }; safety = "Projected"; }
            placement = next;
            Limit();
        }

        public void Pulse(double delta)
        {
            state = DampedSpring.Impulse(state, delta, 0.3);
            Limit();
        }

        public void Advance(double seconds, double tilt, double offset)
        {
            if (placement is null) return;
            follow.Advance(seconds, placement.AvailableLift(state.Position));
            var explicitTarget = offset >= 0 ? offset * placement.Maximum : -offset * placement.Minimum;
            var target = Math.Clamp(explicitTarget + tilt / (Math.PI / 18) * 0.04 + follow.Current.Slide, placement.Minimum, placement.Maximum);
            state = DampedSpring.AdvanceBounded(state, target, 3.5, 0.8, seconds, placement.Minimum, placement.Maximum);
            Limit();
        }

        private void Limit()
        {
            if (placement is null) { state = new(0, 0); return; }
            var v = Math.Clamp(state.Velocity, -0.3, 0.3);
            if ((state.Position <= placement.Minimum + 1e-6 && v < 0) || (state.Position >= placement.Maximum - 1e-6 && v > 0)) v = 0;
            state = state with { Velocity = v };
        }
    }
}
