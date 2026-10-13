using System.Text.Json;

namespace Dango.RoundBallAdmission;

public static class ProbeVerifier
{
    public const double Tolerance = 1e-10;
    public const int SamplesPerSegment = 64;
    private static readonly double[] Scales = [1, .5];
    private static readonly double[] Dpis = [1, 1.25, 1.5];

    public static ProbeReport Verify(ProbeInput input, string? inputSha256 = null, bool selfTests = false)
    {
        ValidateInput(input);
        var snapshots = input.Frames.Select(FrozenSnapshot.CopyFrom).ToArray();
        var checks = new List<CheckResult>();
        foreach (var scale in Scales)
        {
            foreach (var dpi in Dpis)
            {
                var view = new ViewParameters(new(320, 240), scale, dpi);
                Check(checks, $"A1-projection-s{scale}-dpi{dpi}", () => VerifyAnchor(view));
                Check(checks, $"A2-circle-anchor-s{scale}-dpi{dpi}", () => VerifyCircle(
                    new(2, 1.6, .7, .4, 0, 0, 0, 0, false), view, Projection.BallBoundaryDip));
                foreach (var snapshot in snapshots)
                {
                    Check(checks, $"A3-{snapshot.Name}-s{scale}-dpi{dpi}", () => VerifySpline(snapshot, view));
                    Check(checks, $"A2-{snapshot.Name}-s{scale}-dpi{dpi}", () => VerifyCircle(
                        snapshot.Ball, view, Projection.BallBoundaryDip));
                }
            }
        }
        foreach (var angle in new[] { 0d, Math.PI / 2, Math.PI, -Math.PI / 2, 2 * Math.PI + .2 })
        {
            Check(checks, $"A2-angle-{angle:R}", () => VerifyAngle(angle));
        }
        Check(checks, "A2-independent-position-angle", VerifyPositionAngleIndependence);
        foreach (var snapshot in snapshots)
            Check(checks, $"I9-pure-snapshot-{snapshot.Name}", () => VerifySnapshot(snapshot));
        Check(checks, "I9-no-world-step-clock-fields", VerifyNoOwnerFields);
        if (selfTests) ProbeSelfTests.Append(checks);
        return new(1, "Pure data projection contract only; not native dynamics, I8, raster/DPI hardware or HWND acceptance.",
            input.SourceKind, input.Description, input.SourceCommit, input.SourceModuleSha256, inputSha256,
            false, false, false, checks.Count(check => check.Passed), checks.Count(check => !check.Passed), checks.AsReadOnly(),
            Array.AsReadOnly(snapshots.Select(snapshot => new SnapshotEvidence(snapshot.Name, snapshot.SceneRevision,
                snapshot.SuccessfulStep, snapshot.Body.EntityId.Value, snapshot.Ball.EntityId,
                snapshot.BallSourceKind, snapshot.Ball.AngleRadians)).ToArray()));
    }

    public static void ValidateInput(ProbeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Schema != 1 || input.SourceKind is not ("synthetic" or "capturedPublicFrame") ||
            string.IsNullOrWhiteSpace(input.Description) || input.Frames is null || input.Frames.Length == 0 ||
            input.Frames.Length > 256 || input.Frames.Any(frame => frame is null))
            throw new InvalidDataException("Schema 1, explicit source kind/description and 1..256 frames are required.");
        if (input.Frames.Select(frame => frame.Name).Distinct(StringComparer.Ordinal).Count() != input.Frames.Length)
            throw new InvalidDataException("Frame names must be unique.");
        if (input.SourceKind == "capturedPublicFrame" &&
            (input.SourceCommit is null || !IsHex(input.SourceCommit, 40) ||
             input.SourceModuleSha256 is null || !IsHex(input.SourceModuleSha256, 64)))
            throw new InvalidDataException("Captured frames require a full source commit and module SHA-256; metadata alone is not source verification.");
    }

    private static bool IsHex(string text, int length) => text.Length == length && text.All(Uri.IsHexDigit);

    private static string VerifyAnchor(ViewParameters view)
    {
        // Frozen numeric anchors do not use the production helper to generate expectations.
        var expectedDip = view.Scale == 1 ? new Point2(416, 198) : new Point2(368, 219);
        var q = Projection.WorldToQ(new(1.2, .7));
        Near(q, new(1.6, .7), "q anchor");
        var dip = Projection.QToDip(q, view);
        Near(dip, expectedDip, "DIP anchor");
        Near(Projection.DipToWorld(dip, view), new(1.2, .7), "inverse drag mapping");
        var pixels = Projection.DipToPixels(dip, view);
        Near(pixels, new(expectedDip.X * view.DpiScale, expectedDip.Y * view.DpiScale), "pixel-only DPI");
        Near(Projection.PixelsToDip(pixels, view), expectedDip, "pixel to DIP");
        return "Frozen q/DIP anchors, inverse and pixel-only DPI passed.";
    }

    internal static string VerifyCircle(BallSnapshot ball, ViewParameters view,
        Func<BallSnapshot, double, ViewParameters, Point2> boundary)
    {
        var center = new Point2(view.OriginDip.X + 60 * view.Scale * ball.CenterQX,
            view.OriginDip.Y - 60 * view.Scale * ball.CenterQY);
        var radius = 60 * view.Scale * ball.RadiusQ;
        var diagonal = Math.Sqrt(.5);
        Point2[] directions = [new(1, 0), new(diagonal, diagonal), new(0, 1), new(-diagonal, diagonal),
            new(-1, 0), new(-diagonal, -diagonal), new(0, -1), new(diagonal, -diagonal)];
        var points = new Point2[8];
        for (var k = 0; k < directions.Length; k++)
        {
            var point = boundary(ball, k * Math.PI / 4, view);
            var expected = new Point2(center.X + radius * directions[k].X, center.Y - radius * directions[k].Y);
            Near(point, expected, $"direction {k} center/radius");
            Near(Math.Sqrt(Math.Pow(point.X - center.X, 2) + Math.Pow(point.Y - center.Y, 2)), radius, "radial distance");
            Near(Projection.DipToPixels(point, view),
                new(expected.X * view.DpiScale, expected.Y * view.DpiScale), "pixel radius");
            points[k] = point;
        }
        Near(points.Max(point => point.X) - points.Min(point => point.X), 2 * radius, "width");
        Near(points.Max(point => point.Y) - points.Min(point => point.Y), 2 * radius, "height");
        return $"Eight directions; DIP radius {radius:R}, pixel radius {radius * view.DpiScale:R}.";
    }

    private static string VerifySpline(FrozenSnapshot snapshot, ViewParameters view)
    {
        var maximumError = 0d;
        for (var node = 0; node < 16; node++)
        {
            for (var sample = 0; sample <= SamplesPerSegment; sample++)
            {
                var t = sample / (double)SamplesPerSegment;
                var expected = LegacyCanvasOracle(snapshot, node, t, view);
                var actual = Projection.DangoSplineDip(snapshot, node, t, view);
                Near(actual, expected, $"spline segment {node}, sample {sample}");
                var actualPixels = Projection.DipToPixels(actual, view);
                var expectedPixels = new Point2(expected.X * view.DpiScale, expected.Y * view.DpiScale);
                Near(actualPixels, expectedPixels, "spline pixel projection");
                maximumError = Math.Max(maximumError, Math.Max(Math.Abs(actual.X - expected.X), Math.Abs(actual.Y - expected.Y)));
            }
        }
        return $"Independent old-canvas Bernstein oracle; 1040 points, maximum DIP coordinate error {maximumError:R}.";
    }

    // Independent reference: original 160x120 canvas controls, midpoint Bezier segments,
    // then the accepted uniform visual transform. No call into Projection or renderer.
    private static Point2 LegacyCanvasOracle(FrozenSnapshot snapshot, int node, double t, ViewParameters view)
    {
        double maximum = 0;
        foreach (var value in snapshot.Body.Contour) maximum = Math.Max(maximum, Math.Abs((double)value));
        var factor = maximum > .98 ? .98 / maximum : 1;
        Point2 Control(int index) => new(80 + 72 * factor * snapshot.Body.Contour[index * 2],
            66 - 58 * factor * snapshot.Body.Contour[index * 2 + 1]);
        var previous = Control((node + 15) % 16);
        var middle = Control(node);
        var next = Control((node + 1) % 16);
        var start = new Point2((previous.X + middle.X) / 2, (previous.Y + middle.Y) / 2);
        var end = new Point2((middle.X + next.X) / 2, (middle.Y + next.Y) / 2);
        var a = (1 - t) * (1 - t);
        var b = 2 * t * (1 - t);
        var c = t * t;
        var canvas = new Point2(a * start.X + b * middle.X + c * end.X,
            a * start.Y + b * middle.Y + c * end.Y);
        return new(view.OriginDip.X + 80 * view.Scale * snapshot.Body.CoreX + view.Scale * (canvas.X - 80),
            view.OriginDip.Y - 60 * view.Scale * snapshot.Body.CoreY + view.Scale * (canvas.Y - 66));
    }

    private static string VerifyAngle(double angle)
    {
        var ball = new BallSnapshot(2, 1.6, .7, .4, angle, 0, 0, 0, false);
        var degrees = Projection.WpfAngleDegrees(ball);
        Near(degrees, -angle / Math.PI * 180, "WPF angle sign/unwrapped value");
        foreach (var scale in Scales)
        {
            var view = new ViewParameters(new(320, 240), scale, 1);
            var center = new Point2(320 + 60 * scale * 1.6, 240 - 60 * scale * .7);
            // WPF's positive screen-space rotation maps (r,0) to (r*cos(a),r*sin(a)).
            var radians = degrees / 180 * Math.PI;
            Near(Projection.MarkerDip(ball, view), new(center.X + 24 * scale * Math.Cos(radians),
                center.Y + 24 * scale * Math.Sin(radians)), "WPF marker endpoint");
        }
        return "Native CCW to WPF clockwise sign, marker endpoint and cross-turn angle passed.";
    }

    private static string VerifyPositionAngleIndependence()
    {
        var view = new ViewParameters(new(320, 240), 1, 1);
        var ball = new BallSnapshot(2, 1.6, .7, .4, 0, 0, 0, 0, false);
        var rotated = ball with { AngleRadians = Math.PI / 2 };
        Near(Projection.QToDip(new(ball.CenterQX, ball.CenterQY), view),
            Projection.QToDip(new(rotated.CenterQX, rotated.CenterQY), view), "unchanged position");
        Near(Projection.WpfAngleDegrees(rotated), -90, "changed angle at fixed position");
        var translated = ball with { CenterQX = 3.6 };
        Near(Projection.WpfAngleDegrees(translated), 0, "unchanged angle at new position");
        return "Position and rotation are independent snapshot inputs.";
    }

    private static string VerifySnapshot(FrozenSnapshot snapshot)
    {
        var before = JsonSerializer.Serialize(snapshot);
        foreach (var origin in new[] { new Point2(320, 240), new Point2(-18, 39), new Point2(0, 0) })
            foreach (var scale in Scales)
                foreach (var dpi in Dpis)
                    for (var repeat = 0; repeat < 20; repeat++)
                    {
                        var view = new ViewParameters(origin, scale, dpi);
                        _ = Projection.MarkerDip(snapshot.Ball, view);
                        _ = Projection.WpfAngleDegrees(snapshot.Ball);
                        _ = Projection.DangoSplineDip(snapshot, repeat % 16, .37, view);
                        _ = Projection.BallBoundaryDip(snapshot.Ball, repeat, view);
                        // Overlay on/off is a consumer choice to request or omit boundary data.
                        if ((repeat & 1) == 0) _ = Projection.BallBoundaryDip(snapshot.Ball, 0, view);
                    }
        if (!string.Equals(before, JsonSerializer.Serialize(snapshot), StringComparison.Ordinal))
            throw new InvalidOperationException("Projection mutated the immutable snapshot.");
        return "360 display-only cycles preserved all serialized state, IDs, revisions, step and angle; no native world was present.";
    }

    private static string VerifyNoOwnerFields()
    {
        var fields = typeof(FrozenSnapshot).GetFields(System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Type[] allowed = [typeof(string), typeof(long), typeof(Dango.Physics.DangoBodyState), typeof(BallSnapshot)];
        if (fields.Any(field => !allowed.Contains(field.FieldType)) ||
            typeof(Projection).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).Length != 0)
            throw new InvalidOperationException("Snapshot/projector unexpectedly retains an owner, clock or other mutable state.");
        if (typeof(FrozenSnapshot).GetProperties().Any(property => property.CanWrite))
            throw new InvalidOperationException("Snapshot has a publicly writable property.");
        return "Snapshot fields are data only; projector has no fields, world, Step delegate, clock or library handle.";
    }

    internal static void Near(Point2 actual, Point2 expected, string name)
    {
        Near(actual.X, expected.X, name + " X");
        Near(actual.Y, expected.Y, name + " Y");
    }

    internal static void Near(double actual, double expected, string name)
    {
        if (!double.IsFinite(actual) || !double.IsFinite(expected) || Math.Abs(actual - expected) > Tolerance)
            throw new InvalidOperationException($"{name}: expected {expected:R}, actual {actual:R}, tolerance {Tolerance:R}.");
    }

    internal static void Check(List<CheckResult> checks, string name, Func<string> run)
    {
        try { checks.Add(new(name, true, run())); }
        catch (Exception error) { checks.Add(new(name, false, error.Message)); }
    }
}
