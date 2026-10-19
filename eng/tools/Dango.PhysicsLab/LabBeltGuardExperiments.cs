using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;

namespace Dango.PhysicsLab;

/// <summary>Exactly eight material corners, with independent sampled geometry checks beside the native certificate.</summary>
public static class LabBeltGuardExperiments
{
    private const double Dt = 1d / 60;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record Phase(string Name, int Frames, double Horizontal = 0, double Down = 0, double Up = 0, bool Jump = false, bool Drag = false);
    private sealed record Sample(double Height, double Width, double CoreY, double ComY, double ComVx,
        double CoreClearance, double GuardClearance, double GuardCross, double StepMilliseconds,
        double RadialActiveSubsteps, double RadialPointCount, double RadialRequestedMagnitudeBudget,
        double RadialAppliedMagnitudeSum, double RadialNetFx, double RadialNetFy, double RadialTorque,
        double RadialUnitCrossError, double RadialUnitDotMinimum, double RadialBudgetError,
        double RadialEndpointWork, double? FrontThickness, double? RearThickness);

    public static bool Run(string guardedModule, string output)
    {
        Directory.CreateDirectory(output);
        var phases = new[] { new Phase("rest", 300), new Phase("right", 180, Horizontal: 1), new Phase("stop-right", 180),
            new Phase("left", 180, Horizontal: -1), new Phase("stop-left", 180), new Phase("S", 60, Down: 1),
            new Phase("S-release", 120), new Phase("W", 120, Up: 1), new Phase("W-release", 120), new Phase("Space", 180, Jump: true),
            new Phase("drag-compress", 90, Drag: true), new Phase("drag-release", 180) };
        var results = new List<object>(); var allPassed = true; var completedCases = 0;
        foreach (var size in new[] { 80d, 120d })
        foreach (var soft in new[] { 0d, 100d })
        foreach (var build in new[] { 0d, 100d })
        {
            var controls = new DangoBodyControls(size, soft, build);
            var config = DangoBodyMaterial.Configure(controls);
            var calibration = LabBeltCalibrationParameters.Default with { RadialStiffnessScale = 1.4, AreaScale = 1.4,
                BeltBendScale = 4, OverallDamping = config.OverallDamping, GroundedBrake = 35, GroundedHorizontalDamping = 60 };
            Console.WriteLine($"guard corner {size}/{soft}/{build}");
            var measurements = new List<object>(); var gates = new List<string>();
            var ticks = 0; var phaseName = "create"; var phaseTick = -1; string? error = null; object? lastSafe = null;
            float[]? failedGuard = null; var timings = new List<double>(); var restHeight = 0d; var restWidth = 0d;
            using var trace = new StreamWriter(Path.Combine(output, $"guard-{size:0}-{soft:0}-{build:0}.jsonl"));
            LabBeltWorld? world = null;
            try
            {
                world = new LabBeltWorld(guardedModule, config, true, calibration, enableShapeGuard: true, enableSpeedSaturation: true, enableWhaleDrive: false, enableRadialDrive: true);
                if (!world.RadialDriveAvailable || !world.RadialDriveEnabled || world.WhaleDriveEnabled)
                    throw new InvalidOperationException("Radial drive must be available and enabled while whale drive remains disabled.");
                world.SetOriginalGroundJump(); world.SetBounds(-256, 256);
                foreach (var phase in phases)
                {
                    phaseName = phase.Name;
                    var initial = world.Read(); var samples = new List<Sample>();
                    var dragX = initial.ComX; var dragY = config.GroundHeight + .05 * initial.Height;
                    var maxPenetration = 0d; var minClearance = double.PositiveInfinity; var maxWork = 0d;
                    var maxFallbacks = 0d; var maxRefusals = 0d; var maxGap = 0d; var airborne = 0; var longestAir = 0; var acceptedJump = false;
                    var maxAcceptedResidual = 0d; var maxIterations = 0d; var residualValidatedStops = 0d;
                    var limitedSubsteps = 0d; var speedLimitEnergy = 0d; var maxSpeedAfterLimit = 0d; var bfgsUpdates = 0d;
                    var radialActiveSubsteps = 0d; var radialEndpointWork = 0d; var radialMaxBudget = 0d; var radialMaxApplied = 0d;
                    var radialMaxNetFx = 0d; var radialMaxNetFy = 0d; var radialMaxTorque = 0d; var radialMaxUnitCrossError = 0d;
                    var radialMinUnitDot = 1d; var radialMaxBudgetError = 0d; var radialFinalActiveSubsteps = 0d;
                    for (var i = 0; i < phase.Frames; i++)
                    {
                        phaseTick = i;
                        var before = world.Read(); lastSafe = new { Tick = ticks, Phase = phase.Name, before.Scaffold, before.Curve, before.Stats };
                        var started = Stopwatch.GetTimestamp();
                        world.Step(Dt, new LabBeltInput(Horizontal: phase.Horizontal, Downward: phase.Down, Stretch: phase.Up,
                            JumpPressed: phase.Jump && i == 0, DragActive: phase.Drag, DragTargetX: dragX, DragTargetY: dragY,
                            InputActive: phase.Horizontal != 0 || phase.Down != 0 || phase.Up != 0 || phase.Drag || phase.Jump && i == 0));
                        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds; timings.Add(elapsed);
                        var frame = world.Read(); var guard = world.ReadShapeGuard(); ticks++;
                        var radial = world.ReadRadialDrive();
                        if (radial.Length != 16 || radial.Any(x => !double.IsFinite(x)))
                            throw new InvalidOperationException("Radial drive diagnostics must contain 16 finite values.");
                        if (radial[0] != 1 || radial[1] != 1 || radial[2] != ticks)
                            throw new InvalidOperationException("Radial drive was not enabled in the same attempted frame.");
                        var nativeHorizontalForce = (double)(float)config.HorizontalForce;
                        var nativeAirControlRatio = (double)(float)config.AirControlRatio;
                        var baseBudget = Math.Abs(nativeHorizontalForce * phase.Horizontal);
                        var airBudget = Math.Abs(nativeHorizontalForce * nativeAirControlRatio * phase.Horizontal);
                        var radialBudgetTolerance = 1e-7 * (1 + baseBudget);
                        var allowedBudgets = phase.Horizontal == 0 ? new[] { 0d } : new[] { 0d, baseBudget, airBudget };
                        if (!allowedBudgets.Any(allowed => Math.Abs(radial[5] - allowed) <= radialBudgetTolerance) ||
                            Math.Abs(radial[6] - radial[5]) > radialBudgetTolerance ||
                            radial[12] > radialBudgetTolerance)
                            throw new InvalidOperationException("Radial drive magnitude budget was not conserved.");
                        if (radial[10] > 1e-9 || radial[11] < 1 - 1e-9 || radial[9] > 1e-8 * (1 + radial[5]))
                            throw new InvalidOperationException("Radial force direction or torque exceeded its radial alignment tolerance.");
                        if (radial[5] == 0 && (radial[4] != 0 || radial[6] != 0 || radial[7] != 0 || radial[8] != 0))
                            throw new InvalidOperationException("Zero final-substep budget left nonzero radial force records.");
                        if (phase.Horizontal == 0 && (radial[5] != 0 || radial[3] != 0))
                            throw new InvalidOperationException("Radial force remained active after horizontal input stopped or during a non-horizontal phase.");
                        if (radial[5] != 0 && radial[4] != 64)
                            throw new InvalidOperationException("Radial drive did not use exactly 64 quadrature points.");
                        var ledger = world.ReadForceLedger();
                        if (ledger[2] != ticks) throw new InvalidOperationException("Radial force ledger was not from the same attempted frame.");
                        var radialLedgerPoints = 0; var ledgerMagnitudeSum = 0d; var ledgerNetFx = 0d; var ledgerNetFy = 0d;
                        for (var row = 0; row < (int)ledger[4]; row++)
                        {
                            var offset = 8 + 6 * row; var kind = ledger[offset + 4];
                            if (kind == 3) throw new InvalidOperationException("Legacy whale shaping points appeared in the radial runner ledger.");
                            if (kind == 1) throw new InvalidOperationException("Legacy horizontal half-region force appeared in the radial runner ledger.");
                            if (kind == 5)
                            {
                                radialLedgerPoints++;
                                var fx = ledger[offset + 2]; var fy = ledger[offset + 3];
                                ledgerMagnitudeSum += Math.Sqrt(fx * fx + fy * fy); ledgerNetFx += fx; ledgerNetFy += fy;
                            }
                        }
                        if (radialLedgerPoints != radial[4])
                            throw new InvalidOperationException("Radial diagnostics and active-force ledger point counts disagree.");
                        var ledgerTolerance = 1e-8 * (1 + radial[5]);
                        if (Math.Abs(ledgerMagnitudeSum - radial[6]) > ledgerTolerance ||
                            Math.Abs(ledgerNetFx - radial[7]) > ledgerTolerance || Math.Abs(ledgerNetFy - radial[8]) > ledgerTolerance)
                            throw new InvalidOperationException("Radial ledger force totals disagree with same-frame diagnostics.");
                        radialActiveSubsteps += radial[3]; radialEndpointWork += radial[13]; radialFinalActiveSubsteps = radial[3];
                        radialMaxBudget = Math.Max(radialMaxBudget, radial[5]); radialMaxApplied = Math.Max(radialMaxApplied, radial[6]);
                        radialMaxNetFx = Math.Max(radialMaxNetFx, Math.Abs(radial[7])); radialMaxNetFy = Math.Max(radialMaxNetFy, Math.Abs(radial[8]));
                        radialMaxTorque = Math.Max(radialMaxTorque, radial[9]); radialMaxUnitCrossError = Math.Max(radialMaxUnitCrossError, radial[10]);
                        radialMinUnitDot = Math.Min(radialMinUnitDot, radial[11]); radialMaxBudgetError = Math.Max(radialMaxBudgetError, radial[12]);
                        var solver = world.SolverDiagnosticsAvailable ? world.ReadSolverDiagnostics() : null;
                        var speedLimit = world.ReadSpeedLimit();
                        if (speedLimit[0] != 1 || speedLimit[1] != 1 || speedLimit[2] != ticks || speedLimit[6] > config.MaximumSpeed || speedLimit[7] > 1e-10)
                            throw new InvalidOperationException("Invalid same-frame speed limit or kinetic-energy increase");
                        var energy = world.ReadDiagnostics();
                        if (Math.Abs(energy[18] - speedLimit[7]) > 1e-7 * (1 + Math.Abs(speedLimit[7])))
                            throw new InvalidOperationException("Speed limit kinetic energy missing from the energy ledger");
                        limitedSubsteps += speedLimit[3]; speedLimitEnergy += speedLimit[7];
                        maxSpeedAfterLimit = Math.Max(maxSpeedAfterLimit, speedLimit[6]); bfgsUpdates += solver?[17] ?? 0;
                        maxAcceptedResidual = Math.Max(maxAcceptedResidual, frame.Stats[34]);
                        maxIterations = Math.Max(maxIterations, frame.Stats[56]);
                        residualValidatedStops += solver?[16] ?? 0;
                        if (frame.Stats[34] > 2e-7) throw new InvalidOperationException("Accepted nonlinear residual exceeded the unchanged 2e-7 cap");
                        if (guard[0] != 1 || guard[1] != 1 || guard[2] != ticks || guard[3] != 0 || guard[13] != 1 ||
                            guard[11] < guard[20] || guard[12] < guard[19] || guard[4] > guard[21])
                            throw new InvalidOperationException("Invalid same-frame continuous shape certificate or work budget");
                        if (frame.MaximumPenetration > 1e-4 || frame.Stats[39] != 0)
                            throw new InvalidOperationException("Ground penetration or independent perimeter collision bodies");
                        var (inside, clearance) = CoreGeometry(frame);
                        if (!inside || clearance < .8 * guard[19]) throw new InvalidOperationException("Independent polygon core-containment check failed");
                        // Independent screening only. The native interval certificate supplies the continuous guarantee.
                        if (i % 30 == 0 && HasCrossing(frame.Curve)) throw new InvalidOperationException("Independent boundary segment crossing check failed");
                        minClearance = Math.Min(minClearance, clearance); maxPenetration = Math.Max(maxPenetration, frame.MaximumPenetration);
                        maxWork = Math.Max(maxWork, guard[4]); maxFallbacks = Math.Max(maxFallbacks, guard[18]); maxRefusals = Math.Max(maxRefusals, guard[8]);
                        maxGap = Math.Max(maxGap, frame.Gap); acceptedJump |= frame.JumpAccepted;
                        airborne = frame.Gap > 1e-4 ? airborne + 1 : 0; longestAir = Math.Max(longestAir, airborne);
                        var (frontThickness, rearThickness) = BodyThickness(frame.Curve, phase.Horizontal);
                        var sample = new Sample(frame.Height, frame.Width, frame.CoreY, frame.ComY, frame.Stats[43] / frame.TotalMass,
                            clearance, guard[12], guard[11], elapsed, radial[3], radial[4], radial[5], radial[6], radial[7], radial[8], radial[9],
                            radial[10], radial[11], radial[12], radial[13],
                            frontThickness, rearThickness); samples.Add(sample);
                        if (i % 30 == 0) trace.WriteLine(JsonSerializer.Serialize(new { Tick = ticks, Phase = phase.Name, Sample = sample, Guard = guard }));
                    }
                    var tail = samples.TakeLast(Math.Min(60, samples.Count)).ToArray(); var height = tail.Average(x => x.Height);
                    var width = tail.Average(x => x.Width);
                    if (phase.Name == "rest") { restHeight = height; restWidth = width; }
                    var recoveryHeightError = height / restHeight - 1; var recoveryWidthError = width / restWidth - 1;
                    if (phase.Name is ("right" or "left") && radialActiveSubsteps <= 0)
                        gates.Add(phase.Name + ": radial drive had no active substeps");
                    if (phase.Name is ("stop-right" or "stop-left") && radialFinalActiveSubsteps != 0)
                        gates.Add(phase.Name + ": radial drive did not stop immediately with horizontal input");
                    if (phase.Name.EndsWith("release", StringComparison.Ordinal) &&
                        (Math.Abs(recoveryHeightError) > .02 || Math.Abs(recoveryWidthError) > .02))
                        gates.Add(phase.Name + ": tail height/width did not return within 2% of own rest");
                    if (phase.Name.StartsWith("stop", StringComparison.Ordinal) && tail.TakeLast(6).Any(x => Math.Abs(x.ComVx) >= .02))
                        gates.Add(phase.Name + ": true COM not stopped after three seconds");
                    if (phase.Jump && (!acceptedJump || longestAir < 3)) gates.Add("Space: no accepted true airborne jump");
                    measurements.Add(new { phase.Name, phase.Frames, TailHeight = height, TailWidth = width,
                        RadialNetFxPeak = radialMaxNetFx, RadialNetFyPeak = radialMaxNetFy, RadialTorquePeak = radialMaxTorque,
                        RadialActiveSubsteps = radialActiveSubsteps, RadialEndpointWork = radialEndpointWork,
                        RadialRequestedMagnitudeBudgetPeak = radialMaxBudget, RadialAppliedMagnitudeSumPeak = radialMaxApplied,
                        RadialUnitCrossErrorPeak = radialMaxUnitCrossError, RadialUnitDotAlignmentMinimum = radialMinUnitDot,
                        RadialBudgetErrorPeak = radialMaxBudgetError,
                        TailFrontThickness = tail.All(x => x.FrontThickness.HasValue) ? tail.Average(x => x.FrontThickness!.Value) : (double?)null,
                        TailRearThickness = tail.All(x => x.RearThickness.HasValue) ? tail.Average(x => x.RearThickness!.Value) : (double?)null,
                        TailFrontRearThicknessDifferenceOverHeight = tail.All(x => x.FrontThickness.HasValue && x.RearThickness.HasValue)
                            ? (tail.Average(x => x.FrontThickness!.Value) - tail.Average(x => x.RearThickness!.Value)) / height : (double?)null,
                        RecoveryHeightRelativeError = recoveryHeightError, RecoveryWidthRelativeError = recoveryWidthError,
                        TailHeightRms = Math.Sqrt(tail.Average(x => Math.Pow(x.Height - height, 2))), TailCoreY = tail.Average(x => x.CoreY),
                        TailComY = tail.Average(x => x.ComY), TailAbsoluteComVelocity = tail.Average(x => Math.Abs(x.ComVx)), FinalComVelocity = tail[^1].ComVx,
                        MinimumSampledCoreClearance = minClearance, MaximumPenetration = maxPenetration, MaximumCertificateWork = maxWork,
                        MaximumGroundFallbacksPerFrame = maxFallbacks, MaximumRefusedCandidatesPerFrame = maxRefusals,
                        MaximumAcceptedResidual = maxAcceptedResidual, MaximumNewtonIterations = maxIterations, ResidualValidatedLineSearchStops = residualValidatedStops,
                        SpeedLimitedSubsteps = limitedSubsteps, SpeedLimitKineticEnergyDelta = speedLimitEnergy,
                        MaximumSpeedAfterLimit = maxSpeedAfterLimit, QuasiNewtonUpdates = bfgsUpdates,
                        JumpAccepted = acceptedJump, LongestAirborneFrames = longestAir, MaximumGap = maxGap });
                    trace.Flush();
                }
            }
            catch (Exception e)
            {
                error = e.ToString();
                if (world is not null) try { failedGuard = world.ReadShapeGuard(); } catch (Exception readError) { gates.Add("Failed telemetry: " + readError.Message); }
                trace.WriteLine(JsonSerializer.Serialize(new { Error = error, Phase = phaseName, PhaseTick = phaseTick, LastSafe = lastSafe, Guard = failedGuard }));
                Console.WriteLine($"FAIL {size}/{soft}/{build}: {phaseName}/{phaseTick}: {e.Message}");
            }
            finally { world?.Dispose(); }
            var passed = error is null && gates.Count == 0; allPassed &= passed;
            if (error is null) completedCases++;
            var sorted = timings.Order().ToArray(); double Percentile(double p) => sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling((sorted.Length - 1) * p)];
            results.Add(new { Controls = controls, Parameters = config, Calibration = calibration, Passed = passed, Completed = error is null,
                Error = error, GateFailures = gates, TotalTicks = ticks, PhaseAtEnd = phaseName, PhaseTick = phaseTick,
                StepTimingMilliseconds = new { Mean = timings.Count == 0 ? 0 : timings.Average(), P50 = Percentile(.5), P95 = Percentile(.95),
                    P99 = Percentile(.99), Maximum = Percentile(1), Over250ms = timings.Count(x => x > 250) },
                Phases = measurements, FailedGuard = failedGuard, FailedLastSafeState = error is null ? null : lastSafe });
            File.WriteAllText(Path.Combine(output, "guard-eight-corner-results.json"), JsonSerializer.Serialize(new { Kind = "guard-eight-material-corners",
                NativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(guardedModule))), FixedDt = Dt, Substeps = 4,
                ExpectedCases = 8, AttemptedCases = results.Count, CompletedCases = completedCases, Passed = allPassed && completedCases == 8, GuardSchema = LabBeltWorld.ShapeGuardSchema,
                Scope = "exactly 80/120 x 0/100 x 0/100; no intermediate materials; A/D radial outward drive, short bones, whole-body Space, explicit speed saturation; no periodic gait; no appearance or OS input",
                RadialDriveSchema = LabBeltWorld.RadialDriveSchema,
                RadialEndpointWorkNote = "Per-phase sum of diagnostic radialEndpointWork; this is a subset of total external work and is not added to the energy ledger again.",
                Geometry = "native continuous interval/path certificate plus independent sampled polygon core and crossing screening",
                Performance = "wall-clock duration around each native Step only, excludes reads, output, independent screening and WPF; not a hard realtime guarantee",
                RecoveryGate = "last 60 frames of each S/W/drag release: height and width each within 2% of own initial rest tail; separate from manual softness acceptance",
                Results = results }, Json));
        }
        return allPassed;
    }

    private static (bool Inside, double Clearance) CoreGeometry(LabBeltFrame f)
    {
        var inside = false; var distance = double.PositiveInfinity; var p = f.Curve;
        for (int i = 0, j = 255; i < 256; j = i++)
        {
            double ax = p[2 * i], ay = p[2 * i + 1], bx = p[2 * j], by = p[2 * j + 1];
            if ((ay > f.CoreY) != (by > f.CoreY) && f.CoreX < (bx - ax) * (f.CoreY - ay) / (by - ay) + ax) inside = !inside;
            var dx = bx - ax; var dy = by - ay; var squared = dx * dx + dy * dy;
            var t = squared == 0 ? 0 : Math.Clamp(((f.CoreX - ax) * dx + (f.CoreY - ay) * dy) / squared, 0, 1);
            distance = Math.Min(distance, Math.Sqrt(Math.Pow(f.CoreX - ax - t * dx, 2) + Math.Pow(f.CoreY - ay - t * dy, 2)));
        }
        return (inside, distance);
    }

    private static (double? Front, double? Rear) BodyThickness(float[] points, double direction)
    {
        if (direction == 0) return (null, null);
        var minX = double.PositiveInfinity; var maxX = double.NegativeInfinity;
        for (var i = 0; i < 256; i++) { minX = Math.Min(minX, points[2 * i]); maxX = Math.Max(maxX, points[2 * i]); }
        var midX = (minX + maxX) * .5; var halfWidth = (maxX - minX) * .5;
        static double? ThicknessAt(float[] curve, double x)
        {
            var ys = new List<double>(8);
            for (var i = 0; i < 256; i++)
            {
                var j = (i + 1) % 256; var ax = (double)curve[2 * i]; var ay = (double)curve[2 * i + 1];
                var bx = (double)curve[2 * j]; var by = (double)curve[2 * j + 1];
                if ((ax <= x && bx > x) || (bx <= x && ax > x)) ys.Add(ay + (by - ay) * ((x - ax) / (bx - ax)));
            }
            return ys.Count < 2 ? null : ys.Max() - ys.Min();
        }
        var left = ThicknessAt(points, midX - .5 * halfWidth); var right = ThicknessAt(points, midX + .5 * halfWidth);
        return direction > 0 ? (right, left) : (left, right);
    }

    private static bool HasCrossing(float[] points)
    {
        static double Cross(double ax, double ay, double bx, double by, double cx, double cy) => (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        for (var i = 0; i < 256; i++)
        for (var j = i + 2; j < 256; j++)
        {
            if (i == 0 && j == 255) continue;
            var ni = (i + 1) % 256; var nj = (j + 1) % 256;
            double ax = points[2 * i], ay = points[2 * i + 1], bx = points[2 * ni], by = points[2 * ni + 1],
                cx = points[2 * j], cy = points[2 * j + 1], dx = points[2 * nj], dy = points[2 * nj + 1];
            if (Cross(ax, ay, bx, by, cx, cy) * Cross(ax, ay, bx, by, dx, dy) < -1e-18 &&
                Cross(cx, cy, dx, dy, ax, ay) * Cross(cx, cy, dx, dy, bx, by) < -1e-18) return true;
        }
        return false;
    }
}
