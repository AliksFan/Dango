using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class FaceLayoutEdgeCacheEquivalenceTests
{
    [TestMethod]
    public void DefaultAndCustomLayoutsKeepEveryOutputBitAcrossShapeAndExpressionFixtures()
    {
        EyeLayoutParameters[] parameters =
        [
            EyeLayoutParameters.Default, new(100, 0, 51, 24.5),
            new(140, 100, 32, 12), new(140, -100, 66, 34),
            new(70, 100, 66, 12), new(70, -100, 32, 34),
            new(137.13, -72.72, 32.17, 12.27), new(99.37, .001, 51.001, 24.5001),
        ];
        var fixtureIndex = 0;
        foreach (var contour in Contours())
        {
            var shape = AttachmentShapeGeometry.Create(contour);
            foreach (var parametersValue in parameters)
                foreach (var expression in Enum.GetValues<EyeShape>())
                    foreach (var openness in new[] { 0d, .4, 1d })
                    {
                        // Correlated rotation/gaze values keep this functional matrix bounded.
                        // Each expression and legal base-layout extreme is checked in every shape.
                        var angle = openness == 0 ? -Math.PI / 18 : openness == .4 ? .17 : Math.PI / 18;
                        var range = fixtureIndex % 2 == 0 ? 7d : 5d;
                        var left = new EyeMotionSample(new(1, -1), openness, expression, .8);
                        var right = new EyeMotionSample(new(-1, 1), 1 - openness, expression, 1);
                        var context = $"fixture={fixtureIndex}, params={parametersValue}, expression={expression}, openness={openness}";
                        var expected = FrozenFaceLayoutEdgeCacheBaseline.Solve(shape, left, right, angle, parametersValue, range);
                        var actual = FaceLayoutSolver.Solve(shape, left, right, angle, parametersValue, range);
                        AssertBits(expected, actual, context);
                        Assert.AreEqual(FrozenFaceLayoutEdgeCacheBaseline.IsSafe(shape, expected.Left),
                            FaceLayoutSolver.IsSafe(shape, actual.Left), context);
                        Assert.AreEqual(FrozenFaceLayoutEdgeCacheBaseline.IsSafe(shape, expected.Right),
                            FaceLayoutSolver.IsSafe(shape, actual.Right), context);
                        if (parametersValue.IsDefault)
                            AssertBits(FrozenFaceLayoutEdgeCacheBaseline.Solve(shape, left, right, angle, range),
                                FaceLayoutSolver.Solve(shape, left, right, angle, range), context + "; implicit legacy");
                    }
            fixtureIndex++;
        }
    }

    [TestMethod]
    public void CachedEdgesKeepTheExactSafetyDecisionOnAdjacentDoublesAtTheContourMargin()
    {
        foreach (var contour in new[]
        {
            AttachmentShapeGeometryTests.Oval(),
            AttachmentShapeGeometryTests.Oval(width: .28, height: .3),
            AttachmentShapeGeometryTests.Oval(shear: -.16),
        })
        {
            var shape = AttachmentShapeGeometry.Create(contour);
            EyePresentationFrame Eye(double x) => new(new(x, 0), 12d / 160, 12d / 160, .17, 1,
                EyeShape.Round, 0, true);
            var low = 0d;
            var high = shape.Right;
            Assert.IsTrue(FrozenFaceLayoutEdgeCacheBaseline.IsSafe(shape, Eye(low)));
            Assert.IsFalse(FrozenFaceLayoutEdgeCacheBaseline.IsSafe(shape, Eye(high)));
            for (var iteration = 0; iteration < 80 && Math.BitIncrement(low) < high; iteration++)
            {
                var middle = low + (high - low) / 2;
                if (middle == low || middle == high) break;
                if (FrozenFaceLayoutEdgeCacheBaseline.IsSafe(shape, Eye(middle))) low = middle;
                else high = middle;
            }
            Assert.AreEqual(Math.BitIncrement(low), high,
                "The safety fixture must reach adjacent actual safe/unsafe doubles.");
            Assert.IsTrue(FrozenFaceLayoutEdgeCacheBaseline.IsSafe(shape, Eye(low)));
            Assert.IsFalse(FrozenFaceLayoutEdgeCacheBaseline.IsSafe(shape, Eye(high)));
            foreach (var x in new[] { Math.BitDecrement(low), low, high, Math.BitIncrement(high) })
                Assert.AreEqual(FrozenFaceLayoutEdgeCacheBaseline.IsSafe(shape, Eye(x)),
                    FaceLayoutSolver.IsSafe(shape, Eye(x)),
                    $"The unchanged margin comparison must agree at x={x:R}.");
        }
    }

    private static IEnumerable<PresentationPoint[]> Contours()
    {
        yield return AttachmentShapeGeometryTests.Oval();
        yield return AttachmentShapeGeometryTests.Oval(shear: .16);
        yield return AttachmentShapeGeometryTests.Oval(width: .28, height: .3, shear: .16);
        yield return AttachmentShapeGeometryTests.Oval(height: .015);
        var rotated = AttachmentShapeGeometryTests.Oval(width: .7, height: .18, shear: -.08);
        yield return rotated.Skip(5).Concat(rotated.Take(5)).ToArray();
        var noisy = AttachmentShapeGeometryTests.Oval().Select((point, index) =>
            new PresentationPoint(point.X + .00001 * Math.Sin(index * 3), point.Y)).ToArray();
        yield return noisy.Skip(11).Concat(noisy.Take(11)).ToArray();
    }

    private static void AssertBits(FaceLayout expected, FaceLayout actual, string context)
    {
        Assert.AreEqual(expected.Safety, actual.Safety, context);
        AssertEye(expected.Left, actual.Left, context + "; left");
        AssertEye(expected.Right, actual.Right, context + "; right");
        AssertDouble(expected.GazeScale, actual.GazeScale, context + "; gaze scale");
        AssertDouble(expected.SpacingScale, actual.SpacingScale, context + "; spacing scale");
        AssertDouble(expected.SizeScale, actual.SizeScale, context + "; size scale");
    }

    private static void AssertEye(EyePresentationFrame expected, EyePresentationFrame actual, string context)
    {
        Assert.AreEqual(expected.Shape, actual.Shape, context);
        Assert.AreEqual(expected.Visible, actual.Visible, context);
        Assert.AreEqual(expected.IsRight, actual.IsRight, context);
        double[] expectedValues = [expected.Center.X, expected.Center.Y, expected.Width, expected.Height,
            expected.Angle, expected.Openness, expected.Strength];
        double[] actualValues = [actual.Center.X, actual.Center.Y, actual.Width, actual.Height,
            actual.Angle, actual.Openness, actual.Strength];
        for (var index = 0; index < expectedValues.Length; index++)
            AssertDouble(expectedValues[index], actualValues[index], context + "; field=" + index);
    }

    private static void AssertDouble(double expected, double actual, string context) =>
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual), context);
}

// Frozen verbatim from the current 5bc3215 FaceLayoutSolver before the edge-cache change.
// Only imports, namespace placement, accessibility and solver name differ.
// Includes the current custom-layout search and exact safety thresholds, unlike the older legacy-only oracle.





internal static class FrozenFaceLayoutEdgeCacheBaseline
{
    /// <summary>Saved base layout composed with the existing transient eye motion.</summary>
    public static FaceLayout Solve(AttachmentShapeGeometry shape, EyeMotionSample left, EyeMotionSample right,
        double angle, EyeLayoutParameters parameters, double horizontalRange = 5)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        // Keep the complete old search order and fallback path, including callers
        // that explicitly loaded all four default controls from appearance.json.
        if (parameters.IsDefault) return Solve(shape, left, right, angle, horizontalRange);
        Validate(left); Validate(right);
        if (!double.IsFinite(horizontalRange) || horizontalRange is <= 0 or > 7) throw new ArgumentOutOfRangeException(nameof(horizontalRange));
        if (!double.IsFinite(angle) || Math.Abs(angle) > Math.PI / 18 + 1e-10) throw new ArgumentOutOfRangeException(nameof(angle));

        var legacyY = Math.Clamp(1d / 160, shape.Bottom + .35 * shape.Height, shape.Bottom + .65 * shape.Height);
        var requestedY = legacyY - (parameters.HeightControlPercent - 51) / 100 * shape.Height;
        var requestedDistance = 38d / 160 + (parameters.SpacingControlPercent - 24.5) / 100 * shape.Width;
        var diameter = 12d / 160 * parameters.SizePercent / 100;
        var shortAxis = 1 - Math.Abs(parameters.OvalPercent) / 200;
        var width = diameter * (parameters.OvalPercent > 0 ? shortAxis : 1);
        var height = diameter * (parameters.OvalPercent < 0 ? shortAxis : 1);
        // A distorted contour may make a derived request impossible. Preserve the
        // controls and solve for a positive presentation distance instead.
        var distance = double.IsFinite(requestedDistance) && requestedDistance > 0 ? requestedDistance : width + 2d / 160 + 2 * EyeOutline.ChordError;
        // The requested height is first and is never clamped to the old 35–65%
        // strip. Alternatives are only attempted when the true outline is unsafe.
        var heights = new[] { requestedY, requestedY + .03 * shape.Height, requestedY - .03 * shape.Height,
            requestedY + .06 * shape.Height, requestedY - .06 * shape.Height,
            requestedY + .12 * shape.Height, requestedY - .12 * shape.Height, legacyY };
        var centers = heights.Distinct().Select(y => (y, interval: shape.CenterInterval(y)))
            .Where(value => value.interval.HasValue)
            .Select(value => new AttachmentPoint((value.interval!.Value.Left + value.interval.Value.Right) / 2, value.y)).ToArray();
        if (centers.Length == 0) centers = [new((shape.Left + shape.Right) / 2, legacyY)];

        FaceLayout Candidate(AttachmentPoint center, double gaze, double spacing, double size, bool closed = false)
        {
            EyePresentationFrame Eye(EyeMotionSample input, int side)
            {
                var local = new AttachmentPoint(side * distance / 2 * spacing + input.Gaze.X * horizontalRange / 160 * gaze,
                    input.Gaze.Y * 4d / 160 * gaze);
                var point = center + new AttachmentPoint(local.X * Math.Cos(angle) - local.Y * Math.Sin(angle),
                    local.X * Math.Sin(angle) + local.Y * Math.Cos(angle));
                return new(point, width * size, height * size, angle, closed ? 0 : input.Openness,
                    closed ? EyeShape.NaturalClosed : input.Shape, input.Strength, true, side > 0);
            }
            return new(Eye(left, -1), Eye(right, 1), gaze, spacing, size, "Normal");
        }

        FaceLayout? Try(double gaze, double spacing, double size, bool closed = false)
        {
            foreach (var center in centers)
            {
                var candidate = Candidate(center, gaze, spacing, size, closed);
                if (IsJointlySafe(shape, candidate)) return candidate;
            }
            return null;
        }

        var normal = Try(1, 1, 1);
        if (normal is not null) return normal;
        FaceLayout? Search(double minimum, Func<double, FaceLayout?> build, string reason)
        {
            var best = build(minimum);
            if (best is null) return null;
            var low = minimum; var high = 1d;
            for (var i = 0; i < 12; i++)
            {
                var mid = (low + high) / 2; var candidate = build(mid);
                if (candidate is null) high = mid; else { low = mid; best = candidate; }
            }
            return best with { Safety = reason };
        }
        var reduced = Search(0, gaze => Try(gaze, 1, 1), "GazeReduced") ??
            Search(.8, spacing => Try(0, spacing, 1), "SpacingReduced") ??
            Search(.7, size => Try(0, .8, size), "SizeReduced") ??
            Search(.35, size => Try(0, .6, size), "SizeReduced");
        if (reduced is not null) return reduced;
        var closed = Try(0, .6, .35, true);
        if (closed is not null) return closed with { Safety = "ClosedFallback" };
        var fallback = Candidate(centers[0], 0, .6, .35, true);
        // A per-eye containment result cannot establish the joint clearance.
        // Never emit two individually contained but overlapping fallback eyes.
        return fallback with { Left = fallback.Left with { Visible = false }, Right = fallback.Right with { Visible = false }, Safety = "EyeHidden" };
    }

    private static bool IsJointlySafe(AttachmentShapeGeometry shape, FaceLayout face)
    {
        var left = EyeOutline.Create(face.Left);
        if (!IsSafe(shape, left)) return false;
        var right = EyeOutline.Create(face.Right);
        // Exact open ellipses use curved WPF geometry, whereas the safety outline
        // uses chords. Reserve both outline errors in the final pair clearance.
        return IsSafe(shape, right) && right.Min(point => point.X) - left.Max(point => point.X) >= 2d / 160 + 2 * EyeOutline.ChordError;
    }

    public static FaceLayout Solve(AttachmentShapeGeometry shape, EyeMotionSample left, EyeMotionSample right, double angle, double horizontalRange = 5)
    {
        Validate(left); Validate(right);
        if (!double.IsFinite(horizontalRange) || horizontalRange is <= 0 or > 7) throw new ArgumentOutOfRangeException(nameof(horizontalRange));
        if (!double.IsFinite(angle) || Math.Abs(angle) > Math.PI / 18 + 1e-10) throw new ArgumentOutOfRangeException(nameof(angle));
        var y = Math.Clamp(1d / 160, shape.Bottom + 0.35 * shape.Height, shape.Bottom + 0.65 * shape.Height);
        var centers = new[] { y, Math.Min(y + shape.Height * 0.03, shape.Bottom + shape.Height * 0.65),
            Math.Max(y - shape.Height * 0.03, shape.Bottom + shape.Height * 0.35) }
            .Select(height => (height, interval: shape.CenterInterval(height)))
            .Where(v => v.interval.HasValue)
            .Select(v => new AttachmentPoint((v.interval!.Value.Left + v.interval.Value.Right) / 2, v.height)).ToArray();
        if (centers.Length == 0) centers = [new((shape.Left + shape.Right) / 2, y)];

        FaceLayout Candidate(AttachmentPoint center, double gaze, double spacing, double size, bool closed = false)
        {
            EyePresentationFrame Eye(EyeMotionSample input, int side)
            {
                var local = new AttachmentPoint(side * 19d / 160 * spacing + input.Gaze.X * horizontalRange / 160 * gaze, input.Gaze.Y * 4d / 160 * gaze);
                var point = center + new AttachmentPoint(local.X * Math.Cos(angle) - local.Y * Math.Sin(angle), local.X * Math.Sin(angle) + local.Y * Math.Cos(angle));
                var openness = closed ? 0 : input.Openness;
                return new(point, 12d / 160 * size, 12d / 160 * size, angle, openness,
                    closed ? EyeShape.NaturalClosed : input.Shape, input.Strength, true, side > 0);
            }
            return new(Eye(left, -1), Eye(right, 1), gaze, spacing, size, "Normal");
        }
        bool Safe(FaceLayout f)
        {
            var leftOutline = EyeOutline.Create(f.Left);
            if (!IsSafe(shape, leftOutline)) return false;
            var rightOutline = EyeOutline.Create(f.Right);
            return IsSafe(shape, rightOutline) && rightOutline.Min(p => p.X) - leftOutline.Max(p => p.X) >= 2d / 160;
        }
        FaceLayout? Try(double gaze, double spacing, double size)
        {
            foreach (var center in centers)
            {
                var candidate = Candidate(center, gaze, spacing, size);
                if (Safe(candidate)) return candidate;
            }
            return null;
        }
        var normal = Try(1, 1, 1);
        if (normal is not null) return normal;
        // Preserve targets. Search each reduction stage before reducing the next quantity.
        FaceLayout? Search(double minimum, Func<double, FaceLayout?> build, string reason)
        {
            var best = build(minimum);
            if (best is null) return null;
            var lo = minimum; var hi = 1d;
            for (var i = 0; i < 12; i++)
            {
                var mid = (lo + hi) / 2; var candidate = build(mid);
                if (candidate is null) hi = mid; else { lo = mid; best = candidate; }
            }
            return best with { Safety = reason };
        }
        var reduced = Search(0, g => Try(g, 1, 1), "GazeReduced") ??
            Search(0.8, s => Try(0, s, 1), "SpacingReduced") ??
            Search(0.7, s => Try(0, 0.8, s), "SizeReduced");
        if (reduced is not null) return reduced;
        foreach (var center in centers)
        {
            var closed = Candidate(center, 0, 0.8, 0.7, true);
            if (Safe(closed)) return closed with { Safety = "ClosedFallback" };
        }
        var fallback = Candidate(centers[0], 0, 0.8, 0.7, true);
        return fallback with
        {
            Left = fallback.Left with { Visible = IsSafe(shape, fallback.Left) },
            Right = fallback.Right with { Visible = IsSafe(shape, fallback.Right) },
            Safety = "EyeHidden"
        };
    }

    private static void Validate(EyeMotionSample value)
    {
        if (!double.IsFinite(value.Gaze.X) || !double.IsFinite(value.Gaze.Y) || Math.Abs(value.Gaze.X) > 1 || Math.Abs(value.Gaze.Y) > 1 ||
            !double.IsFinite(value.Openness) || value.Openness is < 0 or > 1 || !double.IsFinite(value.Strength) || value.Strength is < 0 or > 1 || !Enum.IsDefined(value.Shape))
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    public static bool IsSafe(AttachmentShapeGeometry shape, EyePresentationFrame eye)
        => IsSafe(shape, EyeOutline.Create(eye));

    private static bool IsSafe(AttachmentShapeGeometry shape, IReadOnlyList<AttachmentPoint> outline)
    {
        const double margin = 1d / 160 + EyeOutline.ChordError + AttachmentShapeGeometry.MaximumChordError;
        foreach (var point in outline)
        {
            var inside = false;
            for (var i = 0; i < shape.Boundary.Count; i++)
            {
                var a = shape.Boundary[i]; var b = shape.Boundary[(i + 1) % shape.Boundary.Count];
                if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
                var edge = b - a;
                var lengthSquared = edge.Dot(edge);
                var t = lengthSquared == 0 ? 0 : Math.Clamp((point - a).Dot(edge) / lengthSquared, 0, 1);
                var difference = point - (a + edge * t);
                if (difference.Dot(difference) < margin * margin) return false;
            }
            if (!inside) return false;
        }
        return true;
    }
}
