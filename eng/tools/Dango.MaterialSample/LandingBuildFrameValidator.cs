using Dango.Physics;

namespace Dango.MaterialSample;

internal sealed record LandingObservation(ShapeMetrics Shape, double RenderedWidth,
    double RenderedHeight, double MaximumCanonicalRadius);

internal sealed class LandingGateFailure(LandingFailureKind kind, string detail) : Exception(detail)
{
    internal LandingFailureKind Kind { get; } = kind;
}

internal static class LandingBuildFrameValidator
{
    internal static LandingObservation Observe(MaterialSession session, DangoBodyState state)
    {
        var config = session.Configuration;
        var shape = ShapeMetrics.Measure(state);
        if (shape.LowestWorldY < config.GroundHeight - .002)
            throw new LandingGateFailure(LandingFailureKind.GroundPenetration,
                $"lowestY={shape.LowestWorldY:R}, ground={config.GroundHeight:R}");
        var maximumRadius = Enumerable.Range(0, state.PointCount).Max(n =>
            Math.Sqrt(Math.Pow(state.Contour[2 * n], 2) + Math.Pow(state.Contour[2 * n + 1], 2)));
        var limit = config.RadialRestLength * config.RadialMaximumRatio + config.NodeRadius;
        if (maximumRadius > limit + .00002)
            throw new LandingGateFailure(LandingFailureKind.RadialLimit,
                $"radius={maximumRadius:R}, limit={limit:R}, tolerance=2e-5");
        try
        {
            var bounds = SampleGeometry.Create(state, bodySize: session.BodySize).Bounds;
            return new(shape, bounds.Width, bounds.Height, maximumRadius / session.BodySize);
        }
        catch (Exception error) { throw new LandingGateFailure(LandingFailureKind.InvalidRender, error.Message); }
    }
}
