using System.IO;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics.Configuration;

namespace Dango.MaterialSample;

internal sealed record CalibrationResult(double RadialFrequencyScale, double PerimeterFrequencyScale,
    double AreaStrength, bool Healthy, string? Error, SilhouetteMetrics? Silhouette,
    double RawAspect, double AreaRetention, double Drift, double MinimumRadialRatio, double MaximumRadialRatio);

internal static class Calibration
{
    internal static int Stability(string library, string output, string variantSet = "frequency")
    {
        var results = new List<object>();
        var defaults = DangoPhysicsDefaults.Current;
        var variants = variantSet switch
        {
            "damping" => (from damping in new[] { .5, .875, 1.25 } from overall in new[] { .3, .9, 1.8 }
                select (.35, .75, 1.7, 10d, damping, overall)).ToArray(),
            "balance" => (from balance in new[] { 2.2, 2.8, 3.4 } from perimeter in new[] { .55, .75, .95 }
                select (.35, perimeter, balance, 10d, 1.85, 1.8)).ToArray(),
            _ => (from radial in new[] { .34, .35, .36 } from perimeter in new[] { .55, .65, .75 }
                select (radial, perimeter, 1.7, 10d, 1.85, 1.8)).ToArray(),
        };
        foreach (var (radial, perimeter, balance, barrier, damping, overall) in variants)
        {
            var config = new MaterialControls().Configure() with
            {
                RadialElasticity = defaults.RadialElasticity * radial,
                PerimeterElasticity = defaults.PerimeterElasticity * perimeter,
                LocalAngleBalanceStrength = balance, PerimeterAngleStrength = barrier,
                RadialDamping = damping, PerimeterDamping = damping, OverallDamping = overall,
            };
            try
            {
                using var session = new MaterialSession(library, config);
                var samples = new List<(double X, double Y, ShapeMetrics Shape)>();
                var matches = true;
                var stopped = true;
                for (var i = 0; i < 36000; i++)
                {
                    var state = session.Step();
                    if (i < 600 || i % 60 != 0) continue;
                    samples.Add((state.CoreX, state.CoreY, ShapeMetrics.Measure(state)));
                    matches &= SilhouetteMetrics.Measure(state).MatchesReference;
                    stopped &= state.IsGrounded && state.MaximumSpeed < .03;
                    var span = new[] { samples.Max(p => p.X) - samples.Min(p => p.X), samples.Max(p => p.Y) - samples.Min(p => p.Y),
                        samples.Max(p => p.Shape.Width) - samples.Min(p => p.Shape.Width), samples.Max(p => p.Shape.Height) - samples.Min(p => p.Shape.Height) }.Max();
                    if (!matches || !stopped || span >= .003) break;
                }
                var xDrift = samples.Max(p => p.X) - samples.Min(p => p.X);
                var yDrift = samples.Max(p => p.Y) - samples.Min(p => p.Y);
                var widthDrift = samples.Max(p => p.Shape.Width) - samples.Min(p => p.Shape.Width);
                var heightDrift = samples.Max(p => p.Shape.Height) - samples.Min(p => p.Shape.Height);
                var drift = new[] { xDrift, yDrift, widthDrift, heightDrift }.Max();
                var silhouette = SilhouetteMetrics.Measure(session.State!);
                var passed = session.Frames == 36000 && matches && stopped && drift < .003;
                results.Add(new { radial, perimeter, balance, barrier, damping, overall, passed, matches, stopped, frames = session.Frames,
                    drift, xDrift, yDrift, widthDrift, heightDrift, silhouette });
                Console.WriteLine($"radial={radial:F2} perimeter={perimeter:F2} balance={balance:F1} barrier={barrier:F0} damping={damping:F3} overall={overall:F1}: pass={passed}, match={matches}, drift={drift:F6}, aspect={silhouette.Aspect:F4}, frames={session.Frames}");
            }
            catch (Exception error) { results.Add(new { radial, perimeter, balance, barrier, damping, overall, passed = false, error = error.Message }); }
            File.WriteAllText(output, JsonSerializer.Serialize(new { kind = "bounded-long-run-calibration", results },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { kind = "bounded-long-run-calibration", results },
            new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    internal static int Run(string library, string output)
    {
        var results = new List<CalibrationResult>();
        var defaults = DangoPhysicsDefaults.Current;
        using var baseline = new MaterialSession(library, defaults);
        for (var i = 0; i < 600; i++) baseline.Step();
        var original = SilhouetteMetrics.Measure(baseline.State!, productionMapping: true);
        foreach (var radial in new[] { .55, .45, .35, .25, .18 })
        foreach (var perimeter in new[] { .35, .55, .75, 1.0 })
        foreach (var area in new[] { 4.8, 1.8 })
        {
            try
            {
                var config = defaults with { TopInwardForce = 0, GroundDeformationStrength = 0,
                    RadialElasticity = defaults.RadialElasticity * radial,
                    PerimeterElasticity = defaults.PerimeterElasticity * perimeter, AreaPreservationStrength = area };
                using var session = new MaterialSession(library, config);
                var initialArea = session.Step().SignedArea;
                var tail = new List<(double X, double Y, ShapeMetrics Shape)>();
                for (var i = 1; i < 600; i++)
                {
                    var state = session.Step(); var shape = ShapeMetrics.Measure(state);
                    if (shape.LowestWorldY < config.GroundHeight - .002) throw new InvalidOperationException("穿地");
                    if (i >= 480)
                    {
                        if (!state.IsGrounded || state.MaximumSpeed >= .03) throw new InvalidOperationException("未接地停稳");
                        tail.Add((state.CoreX, state.CoreY, shape));
                    }
                }
                var drift = new[] { tail.Max(p => p.X) - tail.Min(p => p.X), tail.Max(p => p.Y) - tail.Min(p => p.Y),
                    tail.Max(p => p.Shape.Width) - tail.Min(p => p.Shape.Width), tail.Max(p => p.Shape.Height) - tail.Min(p => p.Shape.Height) }.Max();
                if (drift >= .003) throw new InvalidOperationException("轮廓持续漂移");
                var final = session.State!;
                var ratios = Enumerable.Range(0, 16).Select(i =>
                    (Math.Sqrt(Math.Pow(final.Contour[2 * i], 2) + Math.Pow(final.Contour[2 * i + 1], 2)) - config.NodeRadius) / config.RadialRestLength).ToArray();
                var silhouette = SilhouetteMetrics.Measure(final);
                results.Add(new(radial, perimeter, area, true, null, silhouette, tail[^1].Shape.Aspect,
                    final.SignedArea / initialArea, drift, ratios.Min(), ratios.Max()));
                Console.WriteLine($"radial={radial:F2} perimeter={perimeter:F2} area={area:F1}: aspect={silhouette.Aspect:F3}, mean={silhouette.MeanReferenceError:F3}, peak={silhouette.WidestHeight:F2}, bottom={silhouette.BottomCoverage:F3}, match={silhouette.MatchesReference}");
            }
            catch (Exception error)
            {
                results.Add(new(radial, perimeter, area, false, error.Message, null, 0, 0, 0, 0, 0));
                Console.WriteLine($"radial={radial:F2} perimeter={perimeter:F2} area={area:F1}: FAIL {error.Message}");
            }
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            kind = "bounded-material-calibration", original, targetAspect = SilhouetteMetrics.ReferenceAspect,
            results = results.OrderBy(r => r.Silhouette is null ? double.MaxValue :
                Math.Abs(r.Silhouette.Aspect - SilhouetteMetrics.ReferenceAspect) + r.Silhouette.MeanReferenceError).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        // Exploration is not a gate pass: full flight/recovery and long-run checks follow separately.
        return 0;
    }
}
