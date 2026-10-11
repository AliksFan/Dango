using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dango.MaterialSample;

// One bounded damping candidate; a failed gate never starts a coefficient scan.
internal static class LandingBuildRadialDampingVerification
{
    internal const LandingBuildTable Table = LandingBuildTable.RadialDampingPreview;
    private sealed record EvidenceSource(string FileName, string Sha256);
    private sealed record ReusedLanding(LandingBuildControls Controls, string MappingVersion, EvidenceSource Source);
    private static readonly JsonSerializerOptions Options = LandingBuildMatrixVerification.JsonOptions;

    internal static int Run(string library, string previousLandingPath, string previousMotionPath, string stoppedLandingPath, string output)
    {
        try { return RunCore(library, previousLandingPath, previousMotionPath, stoppedLandingPath, output, Table); }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }

    internal static int RunSizeCompensated(string library, string previousLandingPath, string previousMotionPath, string stoppedLandingPath, string output)
    {
        try { return RunCore(library, previousLandingPath, previousMotionPath, stoppedLandingPath, output, LandingBuildTable.SizeDampingPreview); }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }

    private static int RunCore(string library, string previousLandingPath, string previousMotionPath, string stoppedLandingPath, string output, LandingBuildTable table)
    {
        var native = LandingBuildMatrixVerification.RequireNative(library);
        var previousBytes = File.ReadAllBytes(previousLandingPath);
        var previous = LandingBuildStretchMatrixVerification.ImportLanding(previousBytes);
        var motionBytes = File.ReadAllBytes(previousMotionPath);
        var originalRecoveryFailure = LandingBuildRecoveryVerification.ImportStop(motionBytes, Hash(previousBytes))[^1];
        var stopBytes = File.ReadAllBytes(stoppedLandingPath);
        Require(Hash(stopBytes) == "63A25A2EF2C56D976659939AAB822CAAE9EAB3FBAE0FE79A30CAF540D715C4FC", "Stopped v4 report is not the preserved exact source.");
        using var stopDocument = JsonDocument.Parse(stopBytes);
        var originalLandingFailure = stopDocument.RootElement.GetProperty("trials").EnumerateArray().Last().Deserialize<LandingTrial>(Options)!;
        Require(originalLandingFailure.Controls == new LandingBuildControls(100, 100, 0) &&
            originalLandingFailure.Configuration == originalLandingFailure.Controls.Configure(LandingBuildTable.RecoveryPreview) &&
            originalLandingFailure.Failure?.Kind == LandingFailureKind.ShortSettling && originalLandingFailure.Failure.Frame == 581,
            "Original v4 failure configuration differs.");
        var toolPath = typeof(LandingBuildRadialDampingVerification).Assembly.Location;
        var tool = Source(toolPath, File.ReadAllBytes(toolPath));
        var checks = new List<CheckResult>();
        var trials = new List<LandingTrial>();
        var reused = new List<ReusedLanding>();
        var effects = new List<LandingFailure>();
        LandingMotionTrial? recovery = null;
        LandingTrial? defaultFatReused = null;
        var defaultShapePreserved = false;
        var stage = "configuration";
        var outcome = "running";
        var completed = false;
        string? executionError = null;
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        void Save()
        {
            report.Position = 0;
            JsonSerializer.Serialize(report, new
            {
                schema = 1, kind = table == Table ? "landing-build-radial-damping-landing-matrix" : "landing-build-size-damping-landing-matrix", candidate = LandingBuildMapping.Version(table),
                completed, outcome, stage, frozenForProduction = false, native, tool, frameStepTicks = MaterialSession.FixedStep.Ticks,
                requiredTrials = 45, configurationChecks = checks, originalRecoveryFailure, originalLandingFailure,
                sources = new { previousLanding = Source(previousLandingPath, previousBytes), previousMotion = Source(previousMotionPath, motionBytes), stoppedLanding = Source(stoppedLandingPath, stopBytes) },
                recovery, defaultFatReused, defaultShapePreserved, reusedTrials = reused, trials, effectFailures = effects, executionError,
                acceptance = "Finite repair and landing only; motion, long stability and manual acceptance still required. Original failures remain failures.",
            }, Options);
            report.SetLength(report.Position); report.Flush();
        }
        Save();
        try
        {
            CheckConfiguration(checks, table);
            Save();
            if (checks.Any(c => !c.Passed)) { outcome = "configuration-failure-stop"; return 1; }
            stage = "corner-recovery";
            recovery = LandingBuildMotionVerification.Recovery(library, new(120, 100, 0), table);
            Console.WriteLine("radial-damping/former-recovery: " + (recovery.Passed ? "PASS" : $"FAIL {recovery.Failure}"));
            Save();
            if (!MotionComplete(recovery, table)) return Stop(recovery.Failure, ref outcome);
            stage = "regression-landing";
            var regression = LandingBuildVerification.Land(library, new(table == Table ? 100 : 120, 100, 0), table);
            trials.Add(regression);
            Console.WriteLine("radial-damping/former-short-drift: " + (regression.Failure is null ? "COMPLETE" : $"FAIL {regression.Failure}"));
            Save();
            if (regression.Failure is not null) return Stop(regression.Failure, ref outcome);
            Require(LandingBuildMatrixVerification.TrialComplete(regression), "Finite regression landing incomplete.");
            defaultFatReused = previous.Single(t => t.Controls == new LandingBuildControls(LandingBuild: 0));
            Require(defaultFatReused.Configuration == defaultFatReused.Controls.Configure(table), "Approved default fat configuration changed.");
            var bounds = SampleGeometry.Create(defaultFatReused.LastHealthyFrame!, scale: 190, bodySize: 1).Bounds;
            Require(double.IsFinite(bounds.Width) && bounds.Width > 0 && double.IsFinite(bounds.Height) && bounds.Height > 0,
                "Reused default fat drawing invalid.");
            defaultShapePreserved = true;
            // Exact configuration and native allow the completed default source to be reused, not relabelled as new simulation.
            foreach (var trial in previous.Where(t => t.Configuration == t.Controls.Configure(table)))
            {
                Require(!trials.Any(t => t.Controls == trial.Controls), "Duplicate reuse would hide a changed configuration.");
                trials.Add(trial);
                reused.Add(new(trial.Controls, trial.MappingVersion, Source(previousLandingPath, previousBytes)));
            }
            var expectedReuse = table == Table ? 39 : 43;
            Require(reused.Count == expectedReuse && trials.Count == expectedReuse + 1, "Unchanged v3 reuse set differs.");
            stage = "landing-matrix";
            Save();
            foreach (var size in new[] { 100d, 80d, 120d })
            {
                foreach (var controls in LandingBuildMatrixVerification.AllControls().Where(c => c.SizePercent == size))
                {
                    if (trials.Any(t => t.Controls == controls)) continue;
                    var trial = LandingBuildVerification.Land(library, controls, table);
                    trials.Add(trial);
                    Console.WriteLine($"radial-damping/size={size}/soft={controls.Softness}/build={controls.LandingBuild}: " +
                        (trial.Failure is null ? "COMPLETE" : $"FAIL {trial.Failure}"));
                    Save();
                    if (trial.Failure is not null) return Stop(trial.Failure, ref outcome);
                    Require(LandingBuildMatrixVerification.TrialComplete(trial), "New matrix landing incomplete.");
                }
                effects.AddRange(LandingBuildVerification.EvaluateEffects(trials.Where(t => t.Controls.SizePercent == size).ToArray(), size));
                Save();
                if (effects.Count != 0) { outcome = "effect-failure-stop"; return 1; }
            }
            Require(trials.Count == 45 && trials.Select(t => t.Controls).ToHashSet().SetEquals(LandingBuildMatrixVerification.AllControls()) &&
                trials.All(t => LandingBuildMatrixVerification.TrialComplete(t) && t.Configuration == t.Controls.Configure(table)), "Landing matrix incomplete.");
            stage = "landing-complete";
            outcome = table == Table ? "radial-damping-landing-passed-motion-and-long-required" : "size-damping-landing-passed-motion-and-long-required";
            return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    }

    private static void CheckConfiguration(List<CheckResult> checks, LandingBuildTable table)
    {
        const string name = "v3-geometry-stiffness-unchanged-and-only-soft-fat-radial-damping";
        try
        {
            foreach (var size in new[] { 80d, 90d, 100d, 110d, 120d })
            foreach (var softness in new[] { 0d, 25d, 50d, 75d, 100d })
            for (var build = 0; build <= 100; ++build)
            {
                var controls = new LandingBuildControls(size, softness, build);
                var original = controls.Configure(LandingBuildTable.StretchPreview);
                var candidate = controls.Configure(table);
                Require(candidate with { RadialDamping = original.RadialDamping } == original, "Another physical property changed.");
                if (softness <= 50 || build >= 50 || table == LandingBuildTable.SizeDampingPreview && size <= 100)
                    Require(candidate == original, "Default/hard/slim/small configuration changed.");
                var delta = original.RadialDamping - candidate.RadialDamping;
                var expected = softness > 50 && build < 50 ? .05 * ((softness - 50) / 50) * (1 - build / 50d) : 0;
                if (table == LandingBuildTable.SizeDampingPreview) expected *= Math.Clamp((size - 100) / 20, 0, 1);
                Require(delta >= -1e-14 && delta <= .05 + 1e-14 && Math.Abs(delta - expected) < 1e-14, "Damping interpolation differs.");
            }
            checks.Add(new(name, true, new { scope = "configuration only; no physical simulation" }));
        }
        catch (Exception error) { checks.Add(new(name, false, new { error = error.ToString() })); }
        Console.WriteLine($"{name}: {(checks[^1].Passed ? "PASS" : "FAIL")}");
    }
    private static bool MotionComplete(LandingMotionTrial trial, LandingBuildTable table) => LandingBuildStretchMatrixVerification.MotionComplete(trial, table);
    private static int Stop(LandingFailure? failure, ref string outcome)
    {
        var incomplete = failure is null || failure.Kind == LandingFailureKind.Incomplete;
        outcome = incomplete ? "execution-incomplete" : "hard-failure-stop";
        return incomplete ? 2 : 1;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static EvidenceSource Source(string path, byte[] bytes) => new(Path.GetFileName(path), Hash(bytes));
    private static void Require(bool condition, string detail) => LandingBuildMatrixVerification.Require(condition, detail);
}
