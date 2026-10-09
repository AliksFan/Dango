namespace Dango.RoundBallAdmission;

public static class Projection
{
    public static Point2 WorldToQ(Point2 world) => Finite(new(world.X * (4d / 3), world.Y));

    public static Point2 QToDip(Point2 q, ViewParameters view)
    {
        view.Validate();
        return Finite(new(view.OriginDip.X + 60 * view.Scale * q.X,
            view.OriginDip.Y - 60 * view.Scale * q.Y));
    }

    public static Point2 DipToWorld(Point2 dip, ViewParameters view)
    {
        view.Validate();
        return Finite(new((dip.X - view.OriginDip.X) / (80 * view.Scale),
            (view.OriginDip.Y - dip.Y) / (60 * view.Scale)));
    }

    public static Point2 DipToPixels(Point2 dip, ViewParameters view)
    {
        view.Validate();
        return Finite(new(dip.X * view.DpiScale, dip.Y * view.DpiScale));
    }

    public static Point2 PixelsToDip(Point2 pixels, ViewParameters view)
    {
        view.Validate();
        return Finite(new(pixels.X / view.DpiScale, pixels.Y / view.DpiScale));
    }

    public static Point2 BallBoundaryDip(BallSnapshot ball, double directionRadians, ViewParameters view)
    {
        if (!double.IsFinite(directionRadians)) throw new ArgumentOutOfRangeException(nameof(directionRadians));
        return QToDip(new(ball.CenterQX + ball.RadiusQ * Math.Cos(directionRadians),
            ball.CenterQY + ball.RadiusQ * Math.Sin(directionRadians)), view);
    }

    public static double WpfAngleDegrees(BallSnapshot ball)
    {
        var result = -ball.AngleRadians * 180 / Math.PI;
        return double.IsFinite(result) ? result : throw new InvalidDataException("Non-finite projected angle.");
    }

    public static Point2 MarkerDip(BallSnapshot ball, ViewParameters view) =>
        BallBoundaryDip(ball, ball.AngleRadians, view);

    public static Point2 DangoSplineDip(FrozenSnapshot snapshot, int node, double t, ViewParameters view)
    {
        if (node is < 0 or > 15 || !double.IsFinite(t) || t is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(node));
        var contour = snapshot.Body.Contour;
        var maximum = contour.Max(value => Math.Abs((double)value));
        var normalization = maximum > .98 ? .98 / maximum : 1;
        var before = .5 * (1 - t) * (1 - t);
        var middle = .5 + t - t * t;
        var after = .5 * t * t;
        double Coordinate(int axis) => normalization *
            (before * contour[((node + 15) % 16) * 2 + axis] +
             middle * contour[node * 2 + axis] + after * contour[((node + 1) % 16) * 2 + axis]);
        var world = new Point2(snapshot.Body.CoreX + .9 * Coordinate(0),
            snapshot.Body.CoreY + (58d / 60) * Coordinate(1));
        return QToDip(WorldToQ(world), view);
    }

    private static Point2 Finite(Point2 point) => double.IsFinite(point.X) && double.IsFinite(point.Y)
        ? point : throw new InvalidDataException("Projection produced a non-finite point.");
}
