using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Tests.Rendering.Reference;
using Dango.Physics;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class GaitClearanceConservativeBoundsTests
{
    [TestMethod]
    [DataRow(1e16, 1d)]
    [DataRow(-1e16, -1d)]
    public void RoundedProjectionBeyondTheRecordedEndpointMustNotBePruned(double startX, double endX)
    {
        var point = new LogicalPoint(0, 0);
        var start = new LogicalPoint(startX, 0);
        var end = new LogicalPoint(endX, 0);
        var dx = end.X - start.X;
        var length = dx * dx;
        var t = Math.Clamp((point.X - start.X) * dx / length, 0, 1);
        var projected = start.X + t * dx;

        AssertBits(1, t, "The fixture must actually reach the clamped endpoint.");
        AssertBits(0, projected, "Start + rounded Delta is outside the recorded endpoint AABB.");
        Assert.AreNotEqual(end.X, projected);
        AssertBits(0, FrozenSegmentDistanceSquared(point, start, end), "The frozen projection reaches the eye point.");
        AssertBits(1, NaiveEndpointBoxDistanceSquared(point, start, end), "The unsafe endpoint-only bound rejects it.");
        Assert.IsFalse(GaitGeometry.CanSkipClearanceSegment(point, start, dx, 0, length, .5));
        AssertBoundDoesNotExceedFrozenDistance(point, start, end, "rounded endpoint");

        // Establish a finite best of .25 before visiting the dangerous edge.
        // The final closing edge is closer than .25 but still farther than zero.
        LogicalPoint[] body = [new(0, .5), new(Math.Sign(startX), .5), start, end];
        LogicalPoint[][] eyes = [[point]];
        var expected = FrozenMinimumEyeClearanceSquared(body, eyes);
        AssertBits(0, expected, "The endpoint counterexample must change the actual minimum.");
        Assert.IsTrue(NaiveEndpointBoxMinimum(body, eyes) > expected,
            "A wrong endpoint-AABB implementation must fail this fixture rather than accidentally find zero elsewhere.");
        AssertBits(expected, GaitGeometry.MinimumEyeClearanceSquared(body, eyes), "complete endpoint loop");
    }

    [TestMethod]
    public void APositiveFiniteBoundUsesStrictComparisonAtAdjacentDoubles()
    {
        var point = new LogicalPoint(-10, -10);
        var start = new LogicalPoint(0, 0);
        var end = new LogicalPoint(4, 0);
        var dx = end.X - start.X;
        var length = dx * dx;
        var bound = GaitGeometry.ClearanceLowerBoundSquared(point, start, dx, 0, length);
        var distance = FrozenSegmentDistanceSquared(point, start, end);
        Assert.IsTrue(double.IsFinite(bound) && bound > 0 && bound <= distance,
            "A canonical far segment must provide a useful, conservative bound; always-false pruning cannot pass.");
        Assert.IsTrue(GaitGeometry.CanSkipClearanceSegment(point, start, dx, 0, length, Math.BitDecrement(bound)),
            "A finite lower bound strictly greater than bestSquared must permit this canonical prune.");
        Assert.IsFalse(GaitGeometry.CanSkipClearanceSegment(point, start, dx, 0, length, bound),
            "Equality must not prune: changing > to >= must fail this test.");
        Assert.IsFalse(GaitGeometry.CanSkipClearanceSegment(point, start, dx, 0, length, Math.BitIncrement(bound)));
        Assert.IsFalse(GaitGeometry.CanSkipClearanceSegment(point, start, dx, 0, length, distance));
        Assert.IsFalse(GaitGeometry.CanSkipClearanceSegment(point, start, dx, 0, length, Math.BitIncrement(distance)));
    }

    [TestMethod]
    public void BoundsRemainConservativeAtAdjacentCoordinatesAndDegenerateEdges()
    {
        (LogicalPoint Start, LogicalPoint End)[] edges =
        [
            (new(0, 0), new(4, 0)),
            (new(4, 3), new(-1, -2)),
            (new(-1, -2), new(4, 3)),
            (new(2, -3), new(2, -3)),
            (new(1, 1), new(Math.BitIncrement(1), Math.BitDecrement(1))),
            (new(0, 0), new(double.Epsilon, -double.Epsilon)),
            (new(1e16, 0), new(1, 0)),
            (new(-1e16, 0), new(-1, 0)),
            (new(-double.MaxValue, 0), new(double.MaxValue, 0)),
        ];
        foreach (var (start, end) in edges)
        {
            LogicalPoint[] points =
            [
                start, end,
                new(Math.BitDecrement(end.X), end.Y), new(Math.BitIncrement(end.X), end.Y),
                new(end.X, Math.BitDecrement(end.Y)), new(end.X, Math.BitIncrement(end.Y)),
                new(0, 0), new(-10, 10), new(2, 3),
            ];
            foreach (var point in points)
            {
                var context = $"point={point}; start={start}; end={end}";
                AssertBoundDoesNotExceedFrozenDistance(point, start, end, context);
                var dx = end.X - start.X; var dy = end.Y - start.Y;
                var length = dx * dx + dy * dy;
                var expected = FrozenSegmentDistanceSquared(point, start, end);
                if (!double.IsFinite(expected))
                {
                    Assert.IsFalse(GaitGeometry.CanSkipClearanceSegment(point, start, dx, dy, length, .25), context);
                    continue;
                }
                foreach (var best in new[] { 0d, double.Epsilon, Math.BitDecrement(expected), expected, Math.BitIncrement(expected) })
                    if (GaitGeometry.CanSkipClearanceSegment(point, start, dx, dy, length, best))
                        Assert.IsTrue(expected > best, "A prune must never discard an equal or smaller frozen distance; " + context);
            }
        }
    }

    [TestMethod]
    public void NonFiniteProjectionInputsAndNonFiniteCurrentMinimaNeverPrune()
    {
        (LogicalPoint Point, LogicalPoint Start, double Dx, double Dy, double Length)[] inputs =
        [
            (new(double.NaN, 0), new(0, 0), 1, 0, 1),
            (new(0, double.NaN), new(0, 0), 1, 0, 1),
            (new(double.PositiveInfinity, 0), new(0, 0), 1, 0, 1),
            (new(0, double.NegativeInfinity), new(0, 0), 1, 0, 1),
            (new(3, 3), new(double.NaN, 0), 1, 0, 1),
            (new(3, 3), new(0, double.PositiveInfinity), 1, 0, 1),
            (new(3, 3), new(0, 0), double.NaN, 0, 1),
            (new(3, 3), new(0, 0), 1, double.NaN, 1),
            (new(3, 3), new(0, 0), double.PositiveInfinity, 0, double.PositiveInfinity),
            (new(3, 3), new(0, 0), 0, double.NegativeInfinity, double.PositiveInfinity),
            (new(3, 3), new(0, 0), 1, 0, double.NaN),
            (new(3, 3), new(0, 0), 1, 0, double.PositiveInfinity),
            (new(3, 3), new(0, 0), 1, 0, double.NegativeInfinity),
        ];
        foreach (var (point, start, dx, dy, length) in inputs)
            Assert.IsFalse(GaitGeometry.CanSkipClearanceSegment(point, start, dx, dy, length, .25),
                $"Nonfinite projection input must reach the original arithmetic: {point}, {start}, {dx:R}, {dy:R}, {length:R}.");
        foreach (var best in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.IsFalse(GaitGeometry.CanSkipClearanceSegment(new(-10, -10), new(0, 0), 4, 0, 16, best),
                $"A nonfinite current minimum must not be used for pruning: {best:R}.");
    }

    [TestMethod]
    public void CompleteLoopKeepsFrozenBitsForReversedRepeatedCrossingAndEmptyInputs()
    {
        // Raw crossing polygons exercise the algebraic distance loop only.
        // AttachmentShapeGeometry rejects materially self-intersecting shapes;
        // this fixture does not claim that they are accepted product contours.
        LogicalPoint[][] polygons =
        [
            [new(-4, -3), new(4, -3), new(4, 3), new(-4, 3)],
            [new(-4, 3), new(4, 3), new(4, -3), new(-4, -3)],
            [new(-4, -3), new(-4, -3), new(4, -3), new(4, 3), new(-4, 3)],
            [new(-4, -3), new(4, 3), new(-4, 3), new(4, -3)],
            [new(2, -3), new(2, -3), new(2, -3)],
            [new(1, 1), new(Math.BitIncrement(1), Math.BitDecrement(1)), new(Math.BitDecrement(1), Math.BitIncrement(1))],
            [],
        ];
        LogicalPoint[][][] eyeSets =
        [
            [[], []],
            [[new(0, 0)]],
            [[new(0, 0), new(1, 1), new(Math.BitIncrement(1), 1)], [new(-1, 1), new(20, 20)]],
            [[new(-4, -3), new(Math.BitIncrement(-4), -3), new(Math.BitDecrement(-4), -3)]],
        ];
        foreach (var body in polygons)
            foreach (var eyes in eyeSets)
                AssertBits(FrozenMinimumEyeClearanceSquared(body, eyes), GaitGeometry.MinimumEyeClearanceSquared(body, eyes),
                    $"raw polygon points={body.Length}; eye samples={eyes.Sum(eye => eye.Length)}");
    }

    [TestMethod]
    public void CompleteLoopPreservesNaNAfterAnEarlierFiniteMinimum()
    {
        LogicalPoint[] finite = [new(-2, -2), new(2, -2), new(2, 2), new(-2, 2)];
        var payloadNaN = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000042UL));
        LogicalPoint[][][] eyeSets =
        [
            [[new(0, 0)], [new(payloadNaN, 0)]],
            [[new(0, 0), new(double.PositiveInfinity, 0)]],
            [[new(0, 0), new(0, double.NegativeInfinity)]],
        ];
        foreach (var eyes in eyeSets)
        {
            var expected = FrozenMinimumEyeClearanceSquared(finite, eyes);
            Assert.IsTrue(double.IsNaN(expected), "The original loop must really encounter NaN after a finite sample.");
            AssertBits(expected, GaitGeometry.MinimumEyeClearanceSquared(finite, eyes), "nonfinite eye minimum propagation");
        }
        LogicalPoint[][] bodies =
        [
            [new(-2, -2), new(2, -2), new(payloadNaN, 2), new(-2, 2)],
            [new(-2, -2), new(2, -2), new(double.PositiveInfinity, 2), new(-2, 2)],
            [new(-2, -2), new(2, -2), new(2, double.NegativeInfinity), new(-2, 2)],
            [new(-2, -2), new(2, -2), new(double.MaxValue, 2), new(-double.MaxValue, 2)],
        ];
        foreach (var body in bodies)
        {
            LogicalPoint[][] eyes = [[new(0, 0)]];
            var expected = FrozenMinimumEyeClearanceSquared(body, eyes);
            Assert.IsTrue(double.IsNaN(expected), "The original loop must really encounter a nonfinite projection.");
            AssertBits(expected, GaitGeometry.MinimumEyeClearanceSquared(body, eyes), "nonfinite segment minimum propagation");
        }
    }

    [TestMethod]
    public void AcceptedFlattenedShapesAndRealEyeOutlinesKeepEveryFrozenClearanceBit()
    {
        var compared = 0; var sawPositive = false; var sawNegativeDelta = false;
        foreach (var (width, height, shear) in new[] { (.7, .8, 0d), (.7, .8, -.16), (.28, .3, .16), (.7, .18, -.08) })
        {
            var contour = Enumerable.Range(0, 16).Select(index => Math.PI * 2 * index / 16 + .13)
                .Select(angle => new PresentationPoint(width * Math.Cos(angle) + shear * Math.Sin(angle), height * Math.Sin(angle))).ToArray();
            var shape = AttachmentShapeGeometry.Create(contour);
            var body = shape.Boundary.Select(FrozenCanvas).ToArray();
            Assert.IsTrue(body.Length >= 8, "This matrix must use actually accepted flattened product contours.");
            sawNegativeDelta |= body.Where((point, index) =>
                body[(index + 1) % body.Length].X - point.X < 0 || body[(index + 1) % body.Length].Y - point.Y < 0).Any();
            foreach (var expression in Enum.GetValues<EyeShape>())
            {
                var left = new EyePresentationFrame(new(-.1, .005), 12d / 160, 10d / 160, -.17, .7, expression, .8, true);
                var right = left with { Center = new(.1, .005), Angle = .17, IsRight = true };
                var eyes = new[] { left, right }.Select(eye => EyeOutline.Create(eye).Select(FrozenCanvas).ToArray()).ToArray();
                var expected = FrozenMinimumEyeClearanceSquared(body, eyes);
                var actual = GaitGeometry.MinimumEyeClearanceSquared(body, eyes);
                AssertBits(expected, actual, $"accepted shape {width:R}/{height:R}/{shear:R}; eye={expression}");
                sawPositive |= double.IsFinite(expected) && expected > 0;
                compared++;
            }
        }
        Assert.AreEqual(4 * Enum.GetValues<EyeShape>().Length, compared);
        Assert.IsTrue(sawPositive && sawNegativeDelta, "The matrix must measure real nonzero clearances and reversed edge axes.");
    }

    [TestMethod]
    public void ZeroWidthAndHighEyeBoundsKeepTheCompleteFrozenMinimum()
    {
        LogicalPoint[] body = [new(-4, -3), new(4, -3), new(4, 3), new(-4, 3)];
        LogicalPoint[][][] eyeSets =
        [
            [[new(0, -1_000_000), new(0, 1_000_000)]],
            [[new(-1_000_000, 0), new(1_000_000, 0)]],
            [[new(-1_000_000, -1_000_000), new(1_000_000, 1_000_000)],
                [new(-1_000_000, 1_000_000), new(1_000_000, -1_000_000)]],
            [[new(0, Math.BitDecrement(1_000_000)), new(0, 1_000_000)]],
            [[new(0, 1_000_000), new(0, Math.BitIncrement(1_000_000))]],
        ];
        Assert.AreEqual(eyeSets[0][0][0].X, eyeSets[0][0][1].X, "The eye box must really have zero width.");
        Assert.IsTrue(eyeSets[0][0][1].Y - eyeSets[0][0][0].Y > 1_000_000, "The eye box must really have a large height.");
        foreach (var eyes in eyeSets)
        {
            var expected = FrozenMinimumEyeClearanceSquared(body, eyes);
            Assert.IsTrue(double.IsFinite(expected) && expected > 0,
                "The high-box fixture must have a nonzero finite minimum rather than matching through a zero sample.");
            AssertBits(expected, GaitGeometry.MinimumEyeClearanceSquared(body, eyes), "zero-width/high-box loop");
        }
        Assert.IsFalse(GaitGeometry.CanSkipClearanceSegment(new(0, Math.BitIncrement(1_000_000)),
            new(0, 0), 4, 0, 16, .25), "The adjacent point outside the eligible bound domain must fall back.");
    }

    [TestMethod]
    public void CompleteGaitWithNoVisibleEyesSingleVisibleEyeAndClosedEyesKeepsTheFrozenFrame()
    {
        var contour = Enumerable.Range(0, 16).Select(index => Math.PI * 2 * index / 16 + .13)
            .Select(angle => new PresentationPoint(.72 * Math.Cos(angle), .72 * Math.Sin(angle))).ToArray();
        var shape = AttachmentShapeGeometry.Create(contour);
        var motion = new EyeMotionSample(new(.35, -.1), .65, EyeShape.Round, .7);
        var face = FaceLayoutSolver.Solve(shape, motion, motion with { Gaze = new(.2, .1) }, .04, EyeLayoutParameters.Default);
        var root = new PresentationAttachment(new(0, .8), 0);
        var pose = HatPlacementSolver.Create(shape, root)!.Fit(0)!;
        var bounds = new LogicalRect(100, 100, 160, 120);
        var viewport = new LogicalRect(0, 0, 1000, 800);
        (bool Left, bool Right, bool Closed)[] visibility =
        [
            (false, false, false), (true, false, false), (false, true, false), (true, true, true),
        ];
        foreach (var (leftVisible, rightVisible, closed) in visibility)
        {
            var actualFace = face with
            {
                Left = face.Left with { Visible = leftVisible, Shape = closed ? EyeShape.NaturalClosed : EyeShape.Round, Openness = closed ? 0 : .65 },
                Right = face.Right with { Visible = rightVisible, Shape = closed ? EyeShape.NaturalClosed : EyeShape.Round, Openness = closed ? 0 : .65 },
            };
            var frame = new PhysicsPresentationFrame(true, contour, new(new(0, -.05), 0), root,
                PhysicsPresentationFallbackReason.None,
                new(PhysicsPresentationBackend.DeterministicFake, 0, PhysicsPresentationDiagnosticCode.FakeActive))
            {
                Attachments = new(23, 7, shape.EstimateTilt(), actualFace)
                {
                    SourceShape = shape,
                    Hat = new(pose, 0, 0, -.1, .1, "Fictional visible-eye clearance fixture"),
                },
            };
            var accepted = 0;
            foreach (var strain in new[] { -.02, -.008, .01, Math.BitIncrement(.01) })
            {
                var expected = FrozenGaitGeometry5bc3215.Create(frame, new(11), bounds, viewport, strain, false, 1.35);
                var actual = GaitGeometry.Create(frame, new(11), bounds, viewport, strain, false, 1.35);
                var context = $"left={leftVisible}, right={rightVisible}, closed={closed}, strain={strain:R}";
                Assert.AreEqual(expected is null, actual is null, context);
                if (expected is null) continue;
                Assert.IsNotNull(actual, context);
                Assert.AreEqual(expected.EntityId, actual.EntityId, context);
                Assert.AreEqual(expected.Generation, actual.Generation, context);
                Assert.AreEqual(expected.StepIndex, actual.StepIndex, context);
                AssertBits(expected.CenterX, actual.CenterX, context);
                AssertBits(expected.BottomY, actual.BottomY, context);
                AssertBits(expected.ScaleX, actual.ScaleX, context);
                AssertBits(expected.ScaleY, actual.ScaleY, context);
                accepted++;
            }
            Assert.IsTrue(accepted > 0, "Each visibility branch must actually produce a frozen gait frame, not just compare null; " +
                $"left={leftVisible}, right={rightVisible}, closed={closed}.");
        }
    }

    private static void AssertBoundDoesNotExceedFrozenDistance(LogicalPoint point, LogicalPoint start, LogicalPoint end, string context)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y;
        var length = dx * dx + dy * dy;
        var bound = GaitGeometry.ClearanceLowerBoundSquared(point, start, dx, dy, length);
        var expected = FrozenSegmentDistanceSquared(point, start, end);
        if (double.IsFinite(bound))
            Assert.IsTrue(bound >= 0 && (!double.IsFinite(expected) || bound <= expected),
                $"The floating lower bound must be nonnegative and no greater than the frozen squared distance; bound={bound:R}, actual={expected:R}; {context}");
        else
            Assert.IsFalse(GaitGeometry.CanSkipClearanceSegment(point, start, dx, dy, length, .25),
                "A nonfinite bound cannot justify a prune; " + context);
    }

    private static LogicalPoint FrozenCanvas(AttachmentPoint point) => new(80 + point.X * 160, 66 - point.Y * 160);

    private static void AssertBits(double expected, double actual, string context) =>
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual), context);

    // Frozen from the pre-pruning Gait clearance loop. Preserve eye/point/edge
    // visitation order, Math.Min, subtraction, dot/division, Clamp, q = A + t*D,
    // and Math.Pow arithmetic. Do not call any product bound or distance helper.
    private static double FrozenMinimumEyeClearanceSquared(LogicalPoint[] body, LogicalPoint[][] eyes)
    {
        var minimum = double.PositiveInfinity;
        foreach (var eye in eyes)
            foreach (var point in eye)
                for (var index = 0; index < body.Length; index++)
                    minimum = Math.Min(minimum, FrozenSegmentDistanceSquared(point, body[index], body[(index + 1) % body.Length]));
        return minimum;
    }

    private static double FrozenSegmentDistanceSquared(LogicalPoint point, LogicalPoint start, LogicalPoint end)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y;
        var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / length, 0, 1);
        var projected = new LogicalPoint(start.X + t * dx, start.Y + t * dy);
        return Math.Pow(point.X - projected.X, 2) + Math.Pow(point.Y - projected.Y, 2);
    }

    // Deliberately wrong control, used only to prove the endpoint fixture can
    // fail an unsafe implementation. Never use this result as the oracle.
    private static double NaiveEndpointBoxMinimum(LogicalPoint[] body, LogicalPoint[][] eyes)
    {
        var minimum = double.PositiveInfinity;
        foreach (var eye in eyes)
            foreach (var point in eye)
                for (var index = 0; index < body.Length; index++)
                {
                    var start = body[index]; var end = body[(index + 1) % body.Length];
                    if (NaiveEndpointBoxDistanceSquared(point, start, end) > minimum) continue;
                    minimum = Math.Min(minimum, FrozenSegmentDistanceSquared(point, start, end));
                }
        return minimum;
    }

    private static double NaiveEndpointBoxDistanceSquared(LogicalPoint point, LogicalPoint start, LogicalPoint end)
    {
        var x = Math.Max(Math.Max(Math.Min(start.X, end.X) - point.X, point.X - Math.Max(start.X, end.X)), 0);
        var y = Math.Max(Math.Max(Math.Min(start.Y, end.Y) - point.Y, point.Y - Math.Max(start.Y, end.Y)), 0);
        return Math.Pow(x, 2) + Math.Pow(y, 2);
    }
}
