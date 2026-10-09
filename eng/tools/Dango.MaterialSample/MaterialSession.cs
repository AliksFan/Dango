using Dango.Desktop.Physics.Tuning;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.MaterialSample;

// Diagnostic controls only. No production appearance schema or material preset is defined here.
internal sealed record MaterialControls(double SizePercent = 100, double Softness = 50, double Settling = 90)
{
    internal DangoSelfPhysicsParameters Configure()
    {
        Check(SizePercent, 80, 120); Check(Softness, 0, 100);
        Check(Settling, 0, 100);
        var scale = SizePercent / 100;
        var frequencyScale = Softness <= 50 ? 1.2 - Softness * .004 : 1 - (Softness - 50) * .009;
        var original = DangoPhysicsDefaults.Current;
        var result = original with
        {
            TopInwardForce = 0,
            GroundDeformationStrength = 0,
            RadialRestLength = original.RadialRestLength * scale,
            NodeRadius = original.NodeRadius * scale,
            BoneRepulsionDistance = original.BoneRepulsionDistance * scale,
            MinimumArea = original.MinimumArea * scale * scale,
            // Keep a constant 2D density in this isolated size experiment.
            CoreMass = original.CoreMass * scale * scale,
            NodeMass = original.NodeMass * scale * scale,
            RadialElasticity = original.RadialElasticity * frequencyScale,
            PerimeterElasticity = original.PerimeterElasticity * frequencyScale,
            RadialDamping = .5 + .015 * Settling,
            PerimeterDamping = .5 + .015 * Settling,
        };
        result.Validate();
        return result;
    }

    private static void Check(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}

internal sealed class MaterialSession : IDisposable
{
    internal static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60);
    private readonly DangoPhysicsWorld world;
    private readonly DangoEntityId id;
    internal DangoSelfPhysicsParameters Configuration { get; }
    internal DangoBodyState? State { get; private set; }
    internal int Frames { get; private set; }

    internal MaterialSession(string libraryPath, DangoSelfPhysicsParameters configuration, double height = 0)
    {
        Configuration = configuration;
        world = DangoPhysicsWorld.Create(libraryPath, capacity: 1, configuration: configuration);
        try { id = world.Add(0, height); }
        catch { world.Dispose(); throw; }
    }

    internal DangoBodyState Step(bool jump = false, bool downward = false,
        bool drag = false, double dragX = 0, double dragY = 0)
    {
        var frame = world.Step(FixedStep, [new(id, Downward: downward ? 1 : 0,
            JumpPressed: jump, DragActive: drag, DragTargetX: dragX, DragTargetY: dragY)]);
        State = frame.Bodies.Single();
        Frames++;
        if (!frame.Succeeded)
            throw new InvalidOperationException($"原生物理停止（{frame.ErrorCode}），保留最后合法帧。请显式重新下落。");
        var state = State;
        if (state.PointCount != 16 || state.Contour.Count != 32 ||
            state.Contour.Any(value => !float.IsFinite(value)) ||
            !float.IsFinite(state.CoreX) || !float.IsFinite(state.CoreY) ||
            !float.IsFinite(state.SignedArea) || state.SignedArea < Configuration.MinimumArea ||
            !float.IsFinite(state.MaximumSpeed) || state.MaximumSpeed > Configuration.MaximumSpeed + .002)
            throw new InvalidOperationException("物理健康门禁失败。");
        return state;
    }

    public void Dispose() => world.Dispose();
}

internal sealed record ShapeMetrics(double Width, double Height, double Aspect,
    double RadialSpread, double Circularity, double LowestWorldY)
{
    internal bool IsRound => RadialSpread <= .04 && Math.Abs(Aspect - 1) <= .04 && Circularity >= .975;

    internal static ShapeMetrics Measure(DangoBodyState state)
    {
        var points = Enumerable.Range(0, state.PointCount)
            .Select(i => (X: (double)state.Contour[2 * i], Y: (double)state.Contour[2 * i + 1])).ToArray();
        var width = points.Max(p => p.X) - points.Min(p => p.X);
        var height = points.Max(p => p.Y) - points.Min(p => p.Y);
        // Fit a circle center: the native centroid is a vertex average, which shifts
        // when nodes redistribute even if every node lies on the same circle.
        var meanX = points.Average(p => p.X); var meanY = points.Average(p => p.Y);
        double xx = 0, xy = 0, yy = 0, xq = 0, yq = 0;
        foreach (var point in points)
        {
            var x = point.X - meanX; var y = point.Y - meanY; var q = x * x + y * y;
            xx += x * x; xy += x * y; yy += y * y; xq += x * q; yq += y * q;
        }
        var determinant = xx * yy - xy * xy;
        if (!double.IsFinite(determinant) || determinant <= 1e-12)
            throw new InvalidOperationException("轮廓退化，无法测量圆度。");
        var circleX = meanX + (xq * yy - yq * xy) / (2 * determinant);
        var circleY = meanY + (yq * xx - xq * xy) / (2 * determinant);
        var radii = points.Select(p => Math.Sqrt(Math.Pow(p.X - circleX, 2) +
            Math.Pow(p.Y - circleY, 2))).ToArray();
        double perimeter = 0, twiceArea = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Length];
            perimeter += Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
            twiceArea += a.X * b.Y - b.X * a.Y;
        }
        return new(width, height, width / height, (radii.Max() - radii.Min()) / radii.Average(),
            2 * Math.PI * twiceArea / (perimeter * perimeter), state.CoreY + points.Min(p => p.Y));
    }
}
