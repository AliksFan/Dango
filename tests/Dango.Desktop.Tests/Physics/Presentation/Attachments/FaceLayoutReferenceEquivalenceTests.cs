using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Tests.Physics.Presentation.Attachments.Reference;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class FaceLayoutReferenceEquivalenceTests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    public void EyeShapeOpennessAndGazeMatrixMatchesFrozen8f0ca91()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        foreach (var eyeShape in Enum.GetValues<EyeShape>())
            foreach (var (openness, strength) in new[] { (0d, 0d), (0.4d, 0.25d), (1d, 1d) })
            {
                var left = new EyeMotionSample(new(-1, 1), openness, eyeShape, strength);
                var right = new EyeMotionSample(new(1, -1), openness, eyeShape, strength);
                foreach (var angle in new[] { -Math.PI / 18, 0d, Math.PI / 18 })
                    foreach (var range in new[] { 5d, 7d }) AssertLayout(shape, left, right, angle, range);
            }
    }

    [TestMethod]
    public void NarrowAsymmetricBoundaryAndFallbackLayoutsMatchFrozen8f0ca91()
    {
        var eyes = new EyeMotionSample(new(1, 1), 0.4, EyeShape.Soft, 1);
        var opposite = new EyeMotionSample(new(-1, -1), 1, EyeShape.Round, 0);
        var fixtures = new[]
        {
            AttachmentShapeGeometryTests.Oval(width: 0.28),
            AttachmentShapeGeometryTests.Oval(height: 0.015),
            AttachmentShapeGeometryTests.Oval(width: 0.18, height: 0.08, shear: 0.12),
            AttachmentShapeGeometryTests.Oval(width: 0.7, height: 0.8, shear: -0.16)
        };
        var safety = new List<string>();
        foreach (var points in fixtures)
        {
            var shape = AttachmentShapeGeometry.Create(points);
            AssertLayout(shape, eyes, opposite, 0.17, 7);
            safety.Add(FaceLayoutSolver.Solve(shape, eyes, opposite, 0.17, 7).Safety);
        }
        CollectionAssert.AreEqual(new[] { "Normal", "EyeHidden", "EyeHidden", "Normal" }, safety);
        var flat = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(height: 0.015));
        var fallback = FaceLayoutSolver.Solve(flat, eyes, opposite, 0.17, 7);
        Assert.AreNotEqual("Normal", fallback.Safety, "This fixture must reject the original normal layout.");
    }

    [TestMethod]
    public void ShearedShapesAtRepresentativeAnglesMatchFrozen8f0ca91()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(shear: 0.16));
        var left = new EyeMotionSample(new(-1, 1), 1, EyeShape.Round, 0);
        var right = new EyeMotionSample(new(1, -1), 0.25, EyeShape.Soft, 0.8);
        foreach (var angle in new[] { -0.17, 0d, 0.17 }) AssertLayout(shape, left, right, angle, 7);
    }

    [TestMethod]
    public void InvalidInputsThrowTheSameArgumentExceptionAsFrozen8f0ca91()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        var valid = new EyeMotionSample(new(0, 0), 1, EyeShape.Round, 0);
        var invalidSamples = new[]
        {
            valid with { Gaze = new(double.NaN, 0) }, valid with { Openness = -0.001 },
            valid with { Strength = 1.001 }, valid with { Shape = (EyeShape)999 }
        };
        foreach (var invalid in invalidSamples) AssertThrowsSame(() => FaceLayoutSolver.Solve(shape, invalid, valid, 0), () => FrozenFaceLayoutSolver.Solve(shape, invalid, valid, 0));
        foreach (var range in new[] { double.NaN, 0d, 7.000001d }) AssertThrowsSame(() => FaceLayoutSolver.Solve(shape, valid, valid, 0, range), () => FrozenFaceLayoutSolver.Solve(shape, valid, valid, 0, range));
        foreach (var angle in new[] { double.NaN, Math.PI / 18 + 1e-8 }) AssertThrowsSame(() => FaceLayoutSolver.Solve(shape, valid, valid, angle), () => FrozenFaceLayoutSolver.Solve(shape, valid, valid, angle));
    }

    [TestMethod]
    public void CandidateOutlineReuseAllocatesLessThanFrozenBaseline()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        var left = new EyeMotionSample(new(-1, 1), 0.4, EyeShape.Soft, 1);
        var right = new EyeMotionSample(new(1, -1), 1, EyeShape.Round, 0);
        for (var i = 0; i < 32; i++) { _ = FaceLayoutSolver.Solve(shape, left, right, 0.17, 7); _ = FrozenFaceLayoutSolver.Solve(shape, left, right, 0.17, 7); }
        var current = Measure(() => FaceLayoutSolver.Solve(shape, left, right, 0.17, 7));
        var frozen = Measure(() => FrozenFaceLayoutSolver.Solve(shape, left, right, 0.17, 7));
        TestContext.WriteLine($"current={current} frozen={frozen} bytes/call");
        Assert.IsTrue(current < frozen, $"Expected candidate-local outline reuse to reduce allocations; current={current}, frozen={frozen}.");
    }

    [TestMethod]
    public void SizeReductionFallbackMatchesFrozen8f0ca91()
    {
        // Preserve the frozen fixture's actual attachment-space height when
        // the contour mapping changes from 58/160 to 72/160 on the Y axis.
        var contour = AttachmentShapeGeometryTests.Oval(width: 0.28, height: 0.3, shear: 0.16)
            .Select(point => new Dango.Desktop.Physics.Presentation.PresentationPoint(point.X, point.Y * 58 / 72)).ToArray();
        var shape = AttachmentShapeGeometry.Create(contour);
        var left = new EyeMotionSample(new(1, 1), 1, EyeShape.Surprised, 1);
        var right = new EyeMotionSample(new(-1, -1), 1, EyeShape.Angry, 1);
        AssertLayout(shape, left, right, 0.17, 7);
        var layout = FaceLayoutSolver.Solve(shape, left, right, 0.17, 7);
        Assert.AreEqual("SizeReduced", layout.Safety);
        Assert.AreEqual(0, layout.GazeScale); Assert.AreEqual(0.8, layout.SpacingScale);
        Assert.AreEqual(0.7238037109375, layout.SizeScale);
    }

    private static void AssertLayout(AttachmentShapeGeometry shape, EyeMotionSample left, EyeMotionSample right, double angle, double range)
    {
        var expected = FrozenFaceLayoutSolver.Solve(shape, left, right, angle, range);
        var actual = FaceLayoutSolver.Solve(shape, left, right, angle, range);
        Assert.AreEqual(expected, actual);
    }

    private static void AssertThrowsSame(Action actual, Action expected)
    {
        var expectedException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(expected);
        var actualException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(actual);
        Assert.AreEqual(expectedException.ParamName, actualException.ParamName);
    }

    private static long Measure(Func<FaceLayout> solve)
    {
        const int iterations = 256;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++) _ = solve();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }
}
