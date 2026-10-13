using Dango.Physics;

namespace Dango.RoundBallAdmission;

internal static class ProbeSelfTests
{
    internal static ProbeInput SyntheticInput()
    {
        FrameInput Shape(string name, double width, double height, double shear, double angle)
        {
            var points = Enumerable.Range(0, 16).SelectMany(index =>
            {
                var a = index * Math.PI / 8;
                return new[] { (float)(width * Math.Cos(a) + shear * Math.Sin(a)), (float)(height * Math.Sin(a)) };
            }).ToArray();
            var body = new DangoBodyState(new(1), points, 1.2f, .7f, .6f, 1.2f, .7f, 0, 1, true, -1, 16);
            var ball = new BallSnapshot(2, 1.6, .7, .4, angle, 0, 0, 0, false);
            return new(name, 7, 123, body, ball);
        }
        return new(1, "synthetic", "Test-only mathematical geometry, not captured rest/down/drag or a proven reachable stable native rig. Includes sub-.98 controls and normalization stress.",
            null, null,
            [Shape("synthetic-oval-subthreshold", .8, .75, 0, 0),
             Shape("synthetic-compressed-subthreshold", .9, .45, .1, Math.PI / 2),
             Shape("synthetic-asymmetric-normalization-stress", 1.1, .7, .2, 2 * Math.PI + .2)]);
    }

    internal static void Append(List<CheckResult> checks)
    {
        ProbeVerifier.Check(checks, "I9-defensive-contour-copy", () =>
        {
            var source = SyntheticInput().Frames[0];
            var snapshot = FrozenSnapshot.CopyFrom(source);
            var original = snapshot.Body.Contour[0];
            ((float[])source.Body.Contour)[0] = 99;
            ProbeVerifier.Near(snapshot.Body.Contour[0], original, "source-array mutation isolation");
            if (snapshot.Body.Contour is not IList<float> list || !list.IsReadOnly)
                throw new InvalidOperationException("Frozen contour is not read-only.");
            try { list[0] = 42; }
            catch (NotSupportedException) { return "Input array mutation and exposed IList write cannot modify the frozen frame."; }
            throw new InvalidOperationException("Frozen contour accepted a write.");
        });
        var good = SyntheticInput().Frames[0];
        ProbeVerifier.Check(checks, "reject-nonfinite-body", () => RejectedInput(() =>
            FrozenSnapshot.CopyFrom(good with { Body = good.Body with { CoreX = float.NaN } })));
        ProbeVerifier.Check(checks, "reject-short-public-contour", () => RejectedInput(() =>
            FrozenSnapshot.CopyFrom(good with { Body = good.Body with { Contour = new float[30] } })));
        ProbeVerifier.Check(checks, "reject-nonfinite-ball", () => RejectedInput(() =>
            FrozenSnapshot.CopyFrom(good with { Ball = good.Ball with { AngleRadians = double.PositiveInfinity } })));
        ProbeVerifier.Check(checks, "reject-negative-version", () => RejectedInput(() =>
            FrozenSnapshot.CopyFrom(good with { SuccessfulStep = -1 })));
        ProbeVerifier.Check(checks, "reject-zero-view-scale", () => RejectedInput(() =>
            Projection.QToDip(new(0, 0), new(new(0, 0), 0, 1))));
        ProbeVerifier.Check(checks, "reject-unproven-capture-metadata", () => RejectedInput(() =>
        {
            ProbeVerifier.ValidateInput(SyntheticInput() with { SourceKind = "capturedPublicFrame" });
            return true;
        }));
        var view = new ViewParameters(new(320, 240), 1, 1.5);
        ProbeVerifier.Check(checks, "negative-control-original-world-circle-is-ellipse", () => RejectedGeometry(() =>
            ProbeVerifier.VerifyCircle(good.Ball, view, (ball, angle, v) => new(
                v.OriginDip.X + 60 * v.Scale * ball.CenterQX + 80 * v.Scale * ball.RadiusQ * Math.Cos(angle),
                v.OriginDip.Y - 60 * v.Scale * ball.CenterQY - 60 * v.Scale * ball.RadiusQ * Math.Sin(angle)))));
        ProbeVerifier.Check(checks, "negative-control-equal-bounds-wrong-diagonals", () => RejectedGeometry(() =>
            ProbeVerifier.VerifyCircle(good.Ball, view, (ball, angle, v) =>
            {
                // An equal-width/height outline with inward diagonal points must still fail.
                var radial = Math.Abs(Math.Sin(2 * angle)) > .5 ? .8 : 1;
                return new(v.OriginDip.X + 60 * v.Scale * ball.CenterQX + 60 * v.Scale * ball.RadiusQ * radial * Math.Cos(angle),
                    v.OriginDip.Y - 60 * v.Scale * ball.CenterQY - 60 * v.Scale * ball.RadiusQ * radial * Math.Sin(angle));
            })));
        ProbeVerifier.Check(checks, "negative-control-double-DPI", () => RejectedGeometry(() =>
            ProbeVerifier.VerifyCircle(good.Ball, view, (ball, angle, v) =>
            {
                var point = Projection.BallBoundaryDip(ball, angle, v);
                return new(point.X * v.DpiScale, point.Y * v.DpiScale);
            })));
        ProbeVerifier.Check(checks, "negative-control-missing-T", () => RejectedGeometry(() =>
        {
            ProbeVerifier.Near(new Point2(320 + 60 * 1.2, 240 - 60 * .7), new Point2(416, 198), "q projection without T");
            return "";
        }));
        ProbeVerifier.Check(checks, "negative-control-angle-sign", () => RejectedGeometry(() =>
        {
            ProbeVerifier.Near((Math.PI / 2) * 180 / Math.PI, -90, "wrong WPF sign");
            return "";
        }));
    }

    private static string RejectedInput(Func<object> run)
    {
        try { _ = run(); }
        catch (Exception error) when (error is InvalidDataException or ArgumentOutOfRangeException)
        {
            return "Invalid input explicitly rejected: " + error.Message;
        }
        throw new InvalidOperationException("Invalid input was silently accepted.");
    }

    private static string RejectedGeometry(Func<string> run)
    {
        try { _ = run(); }
        catch (InvalidOperationException error) when (error.Message.Contains("tolerance", StringComparison.Ordinal))
        {
            return "Test-only incorrect geometry triggered the independent behavioral assertion: " + error.Message;
        }
        throw new InvalidOperationException("Incorrect geometry was not rejected by the behavioral assertion.");
    }
}
