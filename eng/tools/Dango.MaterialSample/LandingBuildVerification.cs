using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.MaterialSample;

internal enum LandingFailureKind
{
    Mapping, HealthOrStop, GroundPenetration, InvalidRender, RadialLimit,
    ShortSettling, EffectReversal, EffectInsufficient, Incomplete,
    Flight, Recovery, ControlResponse, LongSettling, LongDrift,
}

internal sealed record LandingFailure(LandingFailureKind Kind, string Detail, int? Frame = null);
internal sealed record LandingMean(double Width, double Height, double Aspect, double Area);
internal sealed record LandingCheckpoint(int Frame, ShapeMetrics Shape, DangoBodyState NativeFrame);
internal sealed record LandingTrial(LandingBuildControls Controls, string MappingVersion,
    DangoSelfPhysicsParameters Configuration, int CompletedFrames, int AttemptedFrame,
    int RenderedFrames, int ShortGateSamples, int MeanSamples, double ShortGateDrift,
    double PeakSpeed, double ElapsedSeconds, LandingMean? Mean,
    IReadOnlyList<LandingCheckpoint> Checkpoints, DangoBodyState? LastHealthyFrame, LandingFailure? Failure);
internal sealed record LandingTableReport(string MappingVersion, IReadOnlyList<LandingTrial> Trials,
    IReadOnlyList<LandingFailure> EffectFailures, bool AllTrialsCompleted, bool HardFailure, bool Passed);

// The initial screen is finite calibration, not full stability or manual acceptance.
internal static class LandingBuildVerification
{
    private const int TrialFrames = 3600;
    private static readonly double[] Builds = [0, 25, 50, 75, 100];
    private static readonly double[] Softnesses = [0, 50, 100];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    internal static int Calibrate(string library, string output, bool mappingOnly = false)
    {
        try { return CalibrateCore(library, output, mappingOnly); }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 2;
        }
    }

    private static int CalibrateCore(string library, string output, bool mappingOnly)
    {
        var source = MaterialLibraries.Describe(library, null).Candidate;
        if (source.Sha256 != LandingBuildMapping.ApprovedNativeSha256)
            throw new InvalidOperationException("落地体型小样要求已认可的 final 材料 DLL；SHA256 不匹配。");
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var checks = new List<CheckResult>();
        var tables = new List<LandingTableReport>();
        string? selectedTable = null;
        var completed = false;
        string outcome = "running";
        void Save()
        {
            report.Position = 0;
            JsonSerializer.Serialize(report, new
            {
                schema = 1, kind = mappingOnly ? "landing-build-mapping-checks" : "landing-build-finite-initial-screen",
                completed, outcome, selectedTable, native = source,
                frameStepTicks = MaterialSession.FixedStep.Ticks,
                configurationChecks = checks, tables,
                acceptance = "Initial screen only; flight/recovery/drag/long-horizon and manual acceptance remain required.",
            }, JsonOptions);
            report.SetLength(report.Position);
            report.Flush();
        }
        Save();
        try
        {
            RunMappingChecks(checks);
            Save();
            if (checks.Any(c => !c.Passed)) { outcome = "mapping-failure"; return 1; }
            if (mappingOnly) { outcome = "mapping-checks-passed"; return 0; }
            foreach (var table in new[] { LandingBuildTable.A, LandingBuildTable.B, LandingBuildTable.C })
            {
                var trials = new List<LandingTrial>();
                foreach (var softness in Softnesses)
                {
                    foreach (var build in Builds)
                    {
                        var trial = Land(library, new(Softness: softness, LandingBuild: build), table);
                        trials.Add(trial);
                        Console.WriteLine($"{table}/size=100/soft={softness}/build={build}: " +
                            (trial.Failure is null ? "COMPLETE" : $"FAIL {trial.Failure.Kind} frame={trial.Failure.Frame}: {trial.Failure.Detail}"));
                        if (trial.Failure is not null) break;
                    }
                    if (trials.Any(t => t.Failure is not null)) break;
                }
                var allCompleted = trials.Count == 15 && trials.All(t => t.Failure is null && t.CompletedFrames == TrialFrames && t.MeanSamples == 600);
                var effectFailures = allCompleted ? EvaluateEffects(trials) : [];
                var hardFailure = !allCompleted || effectFailures.Any(f => f.Kind != LandingFailureKind.EffectInsufficient);
                var passed = allCompleted && effectFailures.Count == 0;
                tables.Add(new(LandingBuildMapping.Version(table), trials, effectFailures, allCompleted, hardFailure, passed));
                Save();
                if (hardFailure) { outcome = "hard-failure-stop"; return 1; }
                if (passed)
                {
                    selectedTable = LandingBuildMapping.Version(table);
                    outcome = "initial-screen-passed-full-validation-required";
                    return 0;
                }
            }
            outcome = "three-predeclared-tables-insufficient-stop";
            return 1;
        }
        catch (Exception error)
        {
            outcome = "execution-incomplete";
            checks.Add(new("execution-incomplete", false, new { error = error.ToString() }));
            return 2;
        }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    }

    internal static LandingTrial Land(string library, LandingBuildControls controls, LandingBuildTable table)
    {
        var config = controls.Configure(table);
        MaterialSession? session = null;
        var clock = Stopwatch.StartNew();
        var checkpoints = new List<LandingCheckpoint>();
        var spans = new FourSpans();
        int completedFrames = 0, attemptedFrame = 0, renderedFrames = 0, gateSamples = 0, meanSamples = 0;
        double sumWidth = 0, sumHeight = 0, sumAspect = 0, sumArea = 0, peakSpeed = 0;
        DangoBodyState? lastHealthyFrame = null;
        LandingFailure? failure = null;
        try
        {
            session = new(library, config);
            for (var i = 0; i < TrialFrames; i++)
            {
                attemptedFrame = i + 1;
                var state = session.Step();
                completedFrames++;
                lastHealthyFrame = state;
                peakSpeed = Math.Max(peakSpeed, state.MaximumSpeed);
                var shape = LandingBuildFrameValidator.Observe(session, state).Shape;
                renderedFrames++;
                if (i is >= 480 and < 600 || i >= 3000)
                {
                    if (!state.IsGrounded || state.MaximumSpeed >= .03)
                        throw new LandingGateFailure(LandingFailureKind.ShortSettling, $"grounded={state.IsGrounded}, speed={state.MaximumSpeed:R}, limit=0.03");
                }
                if (i is >= 480 and < 600)
                {
                    gateSamples++;
                    spans.Add(state.CoreX, state.CoreY, shape.Width, shape.Height);
                    if (spans.Maximum >= .003)
                        throw new LandingGateFailure(LandingFailureKind.ShortSettling, $"frame481..600 span={spans.Maximum:R}, limit=0.003");
                }
                if (i >= 3000)
                {
                    meanSamples++;
                    sumWidth += shape.Width; sumHeight += shape.Height;
                    sumAspect += shape.Aspect; sumArea += state.SignedArea;
                }
                if (completedFrames is 600 or 1800 or 3600) checkpoints.Add(new(completedFrames, shape, state));
            }
        }
        catch (LandingGateFailure error) { failure = new(error.Kind, error.Message, attemptedFrame); }
        catch (Exception error) { failure = new(LandingFailureKind.HealthOrStop, error.ToString(), attemptedFrame); }
        finally { session?.Dispose(); clock.Stop(); }
        var mean = meanSamples > 0 ? new LandingMean(sumWidth / meanSamples, sumHeight / meanSamples,
            sumAspect / meanSamples, sumArea / meanSamples) : null;
        return new(controls, LandingBuildMapping.Version(table), config, completedFrames, attemptedFrame,
            renderedFrames, gateSamples, meanSamples, spans.Maximum, peakSpeed, clock.Elapsed.TotalSeconds,
            mean, checkpoints, lastHealthyFrame, failure);
    }

    internal static IReadOnlyList<LandingFailure> EvaluateEffects(IReadOnlyList<LandingTrial> trials, double sizePercent = 100)
    {
        var failures = new List<LandingFailure>();
        foreach (var softness in Softnesses)
        {
            var row = trials.Where(t => t.Controls.Softness == softness).OrderBy(t => t.Controls.LandingBuild).ToArray();
            if (row.Length != 5 || !row.Select(t => t.Controls.LandingBuild).SequenceEqual(Builds) || row.Any(t => t.Mean is null))
            {
                failures.Add(new(LandingFailureKind.Incomplete, $"Missing effect row: softness={softness}"));
                continue;
            }
            failures.AddRange(DirectionFailures(row.Select(t => t.Mean!).ToArray(), sizePercent == 100 && softness == 50,
                2 * row[0].Configuration.RadialRestLength, $"size={sizePercent}/soft={softness}"));
        }
        foreach (var build in new[] { 0d, 50d, 100d })
        {
            var hard = trials.Single(t => t.Controls.Softness == 0 && t.Controls.LandingBuild == build);
            var soft = trials.Single(t => t.Controls.Softness == 100 && t.Controls.LandingBuild == build);
            if (soft.Mean!.Aspect - hard.Mean!.Aspect < .03 &&
                hard.Mean.Height - soft.Mean.Height < .03 * 2 * hard.Configuration.RadialRestLength)
                failures.Add(new(LandingFailureKind.EffectInsufficient, $"Softness effect insufficient at build={build}"));
        }
        return failures;
    }

    private static IReadOnlyList<LandingFailure> DirectionFailures(IReadOnlyList<LandingMean> row,
        bool isDefault, double naturalDiameter, string context)
    {
        var failures = new List<LandingFailure>();
        if (row.Count != 5 || row.Any(m => !double.IsFinite(m.Width) || !double.IsFinite(m.Height) ||
            !double.IsFinite(m.Aspect) || !double.IsFinite(m.Area) || m.Width <= 0 || m.Height <= 0 || m.Aspect <= 0 || m.Area <= 0))
            return [new(LandingFailureKind.Incomplete, $"Invalid measured effect row: {context}")];
        for (var i = 1; i < row.Count; i++)
            if (row[i].Aspect > row[i - 1].Aspect * 1.01)
                failures.Add(new(LandingFailureKind.EffectReversal, $"{context}: adjacent aspect {row[i - 1].Aspect:R} -> {row[i].Aspect:R} exceeds 1% reverse limit"));
        var endpointRatio = row[0].Aspect / row[^1].Aspect;
        var requiredRatio = isDefault ? 1.08 : 1.02;
        if (endpointRatio < requiredRatio)
            failures.Add(new(LandingFailureKind.EffectInsufficient, $"{context}: fat/slim aspect ratio={endpointRatio:R}, required={requiredRatio:R}"));
        if (isDefault && (row[0].Width - row[^1].Width < .005 * naturalDiameter ||
            row[^1].Height - row[0].Height < .005 * naturalDiameter))
            failures.Add(new(LandingFailureKind.EffectInsufficient, $"{context}: default axes fail 0.5% natural diameter changes; fat={row[0]}, slim={row[^1]}"));
        return failures;
    }

    private static void RunMappingChecks(List<CheckResult> checks)
    {
        void Check(string name, Action action)
        {
            try { action(); checks.Add(new(name, true, new { scope = "configuration or explicitly synthetic metric fixture; no physical simulation" })); }
            catch (Exception error) { checks.Add(new(name, false, new { error = error.ToString() })); }
            Console.WriteLine($"{name}: {(checks[^1].Passed ? "PASS" : "FAIL")}");
        }
        foreach (var table in new[] { LandingBuildTable.A, LandingBuildTable.B, LandingBuildTable.C })
        {
            Check($"{table}-midpoints-and-only-three-fields", () =>
            {
                foreach (var size in new[] { 80d, 100d, 120d })
                foreach (var softness in Softnesses)
                {
                    var baseline = new MaterialControls(size, softness, Settling: 90).Configure();
                    Require(new LandingBuildControls(size, softness).Configure(table) == baseline, "Midpoint changed legacy configuration.");
                    foreach (var build in Builds)
                    {
                        var candidate = new LandingBuildControls(size, softness, build).Configure(table);
                        var factors = LandingBuildMapping.Factors(table, build);
                        Require(candidate.RadialElasticity == baseline.RadialElasticity * factors.Radial &&
                            candidate.PerimeterElasticity == baseline.PerimeterElasticity * factors.Perimeter &&
                            candidate.AreaPreservationStrength == baseline.AreaPreservationStrength * factors.Area,
                            "Candidate did not apply the declared three-field material mapping.");
                        Require(candidate with { RadialElasticity = baseline.RadialElasticity,
                            PerimeterElasticity = baseline.PerimeterElasticity,
                            AreaPreservationStrength = baseline.AreaPreservationStrength } == baseline,
                            "Non-material fields changed.");
                        Require(candidate.RadialDamping == baseline.RadialDamping && candidate.PerimeterDamping == baseline.PerimeterDamping &&
                            Math.Abs(candidate.RadialDamping - 1.85) < 1e-14 && Math.Abs(candidate.PerimeterDamping - 1.85) < 1e-14,
                            "Candidate damping is not fixed at 1.85.");
                    }
                }
            });
            Check($"{table}-endpoints-interpolation-and-domain", () =>
            {
                var endpoints = table switch
                {
                    LandingBuildTable.A => (new LandingBuildFactors(.80, .95, .95), new LandingBuildFactors(1.20, 1.05, 1.05)),
                    LandingBuildTable.B => (new LandingBuildFactors(.65, .90, .90), new LandingBuildFactors(1.40, 1.10, 1.10)),
                    _ => (new LandingBuildFactors(.50, .85, .85), new LandingBuildFactors(1.60, 1.15, 1.15)),
                };
                Require(LandingBuildMapping.Factors(table, 0) == endpoints.Item1 &&
                    LandingBuildMapping.Factors(table, 100) == endpoints.Item2, "Endpoints differ from predeclared table.");
                foreach (var build in new[] { 25d, 49.999999, 50, 50.000001, 75 })
                {
                    var expected = build <= 50 ? endpoints.Item1 : endpoints.Item2;
                    var weight = Math.Abs(build - 50) / 50;
                    var actual = LandingBuildMapping.Factors(table, build);
                    Require(Math.Abs(actual.Radial - (1 + (expected.Radial - 1) * weight)) < 1e-14 &&
                        Math.Abs(actual.Perimeter - (1 + (expected.Perimeter - 1) * weight)) < 1e-14 &&
                        Math.Abs(actual.Area - (1 + (expected.Area - 1) * weight)) < 1e-14, "Interpolation discontinuity.");
                }
                // Range checks, not a physical parameter scan.
                for (var softness = 0; softness <= 100; softness++)
                for (var build = 0; build <= 100; build++)
                    _ = new LandingBuildControls(Softness: softness, LandingBuild: build).Configure(table);
            });
        }
        Check("invalid-controls-and-table-are-rejected", () =>
        {
            void Reject(Action action)
            {
                try { action(); }
                catch (ArgumentOutOfRangeException) { return; }
                throw new InvalidOperationException("Invalid control was accepted.");
            }
            foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d, 101d })
            {
                Reject(() => new LandingBuildControls(LandingBuild: bad).Configure(LandingBuildTable.A));
                Reject(() => new LandingBuildControls(Softness: bad).Configure(LandingBuildTable.A));
            }
            foreach (var bad in new[] { double.NaN, double.PositiveInfinity, 79d, 121d })
                Reject(() => new LandingBuildControls(SizePercent: bad).Configure(LandingBuildTable.A));
            Reject(() => new LandingBuildControls().Configure((LandingBuildTable)99));
        });
        Check("synthetic-effect-fixtures-reject-ignored-reversed-and-shrink-only", () =>
        {
            LandingMean Metric(double width, double height) => new(width, height, width / height, 1);
            var positive = Enumerable.Range(0, 5).Select(i => Metric(1.6 - .1 * i, 1 + .1 * i)).ToArray();
            Require(DirectionFailures(positive, true, 1.36, "synthetic-positive").Count == 0, "Positive metric fixture rejected.");
            var ignored = Enumerable.Repeat(Metric(1.5, 1), 5).ToArray();
            Require(DirectionFailures(ignored, true, 1.36, "synthetic-ignored").Any(f => f.Kind == LandingFailureKind.EffectInsufficient), "Ignored slider accepted.");
            Require(DirectionFailures(positive.Reverse().ToArray(), true, 1.36, "synthetic-reverse")
                .Any(f => f.Kind == LandingFailureKind.EffectReversal), "Reverse direction accepted.");
            var shrinkOnly = Enumerable.Range(0, 5).Select(i => Metric(1.5 - .1 * i, 1 - .1 * i)).ToArray();
            Require(DirectionFailures(shrinkOnly, true, 1.36, "synthetic-shrink-only").Count > 0, "Whole-body shrinking accepted.");
            var mixed = new[] { Metric(1.5, 1), Metric(1.3, 1), Metric(1.5, 1), Metric(1.5, 1), Metric(1.5, 1) };
            var mixedFailures = DirectionFailures(mixed, true, 1.36, "synthetic-reverse-and-insufficient");
            Require(mixedFailures.Any(f => f.Kind == LandingFailureKind.EffectInsufficient) &&
                mixedFailures.Any(f => f.Kind == LandingFailureKind.EffectReversal), "Effect insufficiency hid a hard failure.");
        });
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }

    private sealed class FourSpans
    {
        private readonly double[] minimum = [double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity];
        private readonly double[] maximum = [double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity];
        private int samples;
        internal double Maximum => samples == 0 ? 0 : Enumerable.Range(0, 4).Max(i => maximum[i] - minimum[i]);
        internal void Add(double x, double y, double width, double height)
        {
            var values = new[] { x, y, width, height };
            for (var i = 0; i < values.Length; i++) { minimum[i] = Math.Min(minimum[i], values[i]); maximum[i] = Math.Max(maximum[i], values[i]); }
            samples++;
        }
    }
}
