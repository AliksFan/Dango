using Dango.Desktop.Physics.Tuning;

namespace Dango.MaterialSample;

internal enum LandingBuildTable { A, B, C }

internal sealed record LandingBuildFactors(double Radial, double Perimeter, double Area);

// Isolated candidate controls. The legacy diagnostic mapping remains unchanged.
internal sealed record LandingBuildControls(double SizePercent = 100, double Softness = 50, double LandingBuild = 50)
{
    internal DangoSelfPhysicsParameters Configure(LandingBuildTable table)
    {
        var factors = LandingBuildMapping.Factors(table, LandingBuild);
        var baseline = new MaterialControls(SizePercent, Softness, Settling: 90).Configure();
        if (LandingBuild == 50) return baseline;
        var candidate = baseline with
        {
            RadialElasticity = baseline.RadialElasticity * factors.Radial,
            PerimeterElasticity = baseline.PerimeterElasticity * factors.Perimeter,
            AreaPreservationStrength = baseline.AreaPreservationStrength * factors.Area,
        };
        candidate.Validate();
        return candidate;
    }
}

internal static class LandingBuildMapping
{
    internal const string ApprovedNativeSha256 = "055BE2B5E6CD3816EB4D59F616577B653C00512B4750299A013D2CEBF676E2C5";
    internal static string Version(LandingBuildTable table) => table switch
    {
        LandingBuildTable.A => "landing-build-v1-A",
        LandingBuildTable.B => "landing-build-v1-B",
        LandingBuildTable.C => "landing-build-v1-C",
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    internal static LandingBuildFactors Factors(LandingBuildTable table, double build)
    {
        if (!double.IsFinite(build) || build is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(build));
        var (fat, slim) = table switch
        {
            LandingBuildTable.A => (new LandingBuildFactors(.80, .95, .95), new LandingBuildFactors(1.20, 1.05, 1.05)),
            LandingBuildTable.B => (new LandingBuildFactors(.65, .90, .90), new LandingBuildFactors(1.40, 1.10, 1.10)),
            LandingBuildTable.C => (new LandingBuildFactors(.50, .85, .85), new LandingBuildFactors(1.60, 1.15, 1.15)),
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        if (build == 50) return new(1, 1, 1);
        var start = build < 50 ? fat : new LandingBuildFactors(1, 1, 1);
        var end = build < 50 ? new LandingBuildFactors(1, 1, 1) : slim;
        var progress = build < 50 ? build / 50 : (build - 50) / 50;
        return new(start.Radial + (end.Radial - start.Radial) * progress,
            start.Perimeter + (end.Perimeter - start.Perimeter) * progress,
            start.Area + (end.Area - start.Area) * progress);
    }
}
