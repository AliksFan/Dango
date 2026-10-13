using Dango.Physics.Configuration;

namespace Dango.Desktop.Physics.Tuning;

/// <summary>User body controls saved with the appearance: size, softness and landing build.</summary>
public sealed record DangoBodyControls
{
    public const double DefaultSizePercent = 100, DefaultSoftness = 50, DefaultLandingBuild = 50;
    public static DangoBodyControls Default { get; } = new();

    public DangoBodyControls(double sizePercent = DefaultSizePercent, double softness = DefaultSoftness,
        double landingBuild = DefaultLandingBuild)
    {
        SizePercent = Check(sizePercent, 80, 120, nameof(sizePercent));
        Softness = Check(softness, 0, 100, nameof(softness));
        LandingBuild = Check(landingBuild, 0, 100, nameof(landingBuild));
    }

    public double SizePercent { get; }
    public double Softness { get; }
    /// <summary>0 = fat (wider, flatter on landing), 100 = slim (stands taller).</summary>
    public double LandingBuild { get; }
    public bool IsDefault => this == Default;

    private static double Check(double value, double minimum, double maximum, string name) =>
        double.IsFinite(value) && value >= minimum && value <= maximum ? value : throw new ArgumentOutOfRangeException(name);
}

/// <summary>
/// The single mapping from body controls to physics. Version body-material-v1 is the experiment's
/// landing-build-v6 (size-damping preview) table, accepted on 2026-10-13. The natural geometry stays
/// a uniform circle: only size scales lengths, and only material parameters change the landed shape.
/// </summary>
public static class DangoBodyMaterial
{
    public const string ModelVersion = "body-material-v1";
    private const double FatRadial = .50, FatPerimeter = .70, FatArea = 1.00;
    private const double SlimRadial = 1.60, SlimPerimeter = 1.15, SlimArea = 1.15;
    private const double FatRadialMaximumRatio = 1.30;

    public static DangoSelfPhysicsParameters Configure(DangoBodyControls controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        var baseline = Material(controls.SizePercent, controls.Softness);
        var build = controls.LandingBuild;
        if (build == 50) return baseline;
        var fat = build < 50;
        var progress = fat ? build / 50 : (build - 50) / 50;
        double Lerp(double from, double to) => from + (to - from) * progress;
        var radial = fat ? Lerp(FatRadial, 1) : Lerp(1, SlimRadial);
        var perimeter = fat ? Lerp(FatPerimeter, 1) : Lerp(1, SlimPerimeter);
        var area = fat ? Lerp(FatArea, 1) : Lerp(1, SlimArea);
        var result = baseline with
        {
            RadialElasticity = baseline.RadialElasticity * radial,
            PerimeterElasticity = baseline.PerimeterElasticity * perimeter,
            AreaPreservationStrength = baseline.AreaPreservationStrength * area,
            RadialMaximumRatio = fat
                ? FatRadialMaximumRatio + (baseline.RadialMaximumRatio - FatRadialMaximumRatio) * (build / 50)
                : baseline.RadialMaximumRatio,
            // Large, soft, fat bodies recover with slightly less radial damping.
            RadialDamping = fat
                ? baseline.RadialDamping - .05 * Math.Max(0, (controls.Softness - 50) / 50) * (1 - build / 50) *
                    Math.Clamp((controls.SizePercent - 100) / 20, 0, 1)
                : baseline.RadialDamping,
        };
        result.Validate();
        return result;
    }

    /// <summary>Body size relative to the default rig, used for presentation scale.</summary>
    public static double SizeScale(DangoSelfPhysicsParameters parameters) =>
        parameters.RadialRestLength / DangoPhysicsDefaults.Current.RadialRestLength;

    private static DangoSelfPhysicsParameters Material(double sizePercent, double softness)
    {
        var scale = sizePercent / 100;
        // Independent frequencies: softer spokes allow flattening while the perimeter keeps the crown smooth.
        var radialScale = softness <= 50 ? .55 - softness * .004 : .35 - (softness - 50) * .002;
        var perimeterScale = softness <= 50 ? .90 - softness * .003 : .75 - (softness - 50) * .004;
        var original = DangoPhysicsDefaults.Current;
        var result = original with
        {
            TopInwardForce = 0,
            GroundDeformationStrength = 0,
            RadialRestLength = original.RadialRestLength * scale,
            NodeRadius = original.NodeRadius * scale,
            BoneRepulsionDistance = original.BoneRepulsionDistance * scale,
            MinimumArea = original.MinimumArea * scale * scale,
            // Constant 2D density.
            CoreMass = original.CoreMass * scale * scale,
            NodeMass = original.NodeMass * scale * scale,
            RadialElasticity = original.RadialElasticity * radialScale,
            PerimeterElasticity = original.PerimeterElasticity * perimeterScale,
            RadialDamping = 1.85,
            PerimeterDamping = 1.85,
        };
        result.Validate();
        return result;
    }
}
