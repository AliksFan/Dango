using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dango.MaterialSample;

// The approved first increment is one landing and the former failing drag case.
// It does not resume the stopped B matrix or approve a full StretchPreview preset.
internal static class LandingBuildStretchPreviewVerification
{
    private const LandingBuildTable Table = LandingBuildTable.StretchPreview;
    private sealed record EvidenceSource(string FileName, string Sha256);

    internal static int Run(string library, string landingPath, string slimPath, string stopPath, string output)
    {
        try { return RunCore(library, landingPath, slimPath, stopPath, output); }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }

    private static int RunCore(string library, string landingPath, string slimPath, string stopPath, string output)
    {
        var native = LandingBuildMatrixVerification.RequireNative(library);
        var landingBytes = File.ReadAllBytes(landingPath);
        var baseline = LandingBuildMatrixVerification.ImportLanding(landingBytes);
        var midpoint = baseline.Single(t => t.Controls == new LandingBuildControls());
        var slimBytes = File.ReadAllBytes(slimPath);
        var slim = ImportSlim(slimBytes, landingBytes);
        var stopBytes = File.ReadAllBytes(stopPath);
        var oldStop = ImportStop(stopBytes);
        var checks = new List<CheckResult>();
        var trials = new List<LandingTrial> { midpoint, slim };
        LandingMotionTrial? drag = null;
        var outcome = "running";
        var completed = false;
        var shapePreviewPassed = false;
        string? executionError = null;
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        void Save()
        {
            report.Position = 0;
            JsonSerializer.Serialize(report, new
            {
                schema = 1, kind = "landing-build-stretch-preview", completed, outcome,
                candidate = LandingBuildMapping.Version(Table), frozenForProduction = false,
                native, frameStepTicks = MaterialSession.FixedStep.Ticks,
                sources = new { landing = Source(landingPath, landingBytes), slim = Source(slimPath, slimBytes), oldStop = Source(stopPath, stopBytes) },
                configurationChecks = checks, shapePreviewPassed,
                requiredNewLandingTrials = 1, requiredDragTrials = 1,
                reusedMidpoint = midpoint.Controls, reusedSlim = slim.Controls,
                oldDefaultEndpoints = baseline.Where(t => t.Controls.SizePercent == 100 && t.Controls.Softness == 50 && t.Controls.LandingBuild is 0 or 100).ToArray(),
                trials, formerFailedCase = oldStop, drag, executionError,
                acceptance = "Limited default landing and former-trigger drag only; other combinations, flight/recovery, long stability and interactive window remain unverified.",
            }, LandingBuildMatrixVerification.JsonOptions);
            report.SetLength(report.Position); report.Flush();
        }
        Save();
        try
        {
            ConfigurationChecks(checks);
            Save();
            if (checks.Any(c => !c.Passed)) { outcome = "configuration-failure-stop"; return 1; }
            var landing = LandingBuildVerification.Land(library, new(LandingBuild: 0), Table);
            trials.Add(landing);
            Console.WriteLine("stretch/default-fat-landing: " + (landing.Failure is null ? "COMPLETE" : $"FAIL {landing.Failure}"));
            Save();
            if (landing.Failure is not null) { outcome = "landing-hard-failure-stop"; return 1; }
            Require(LandingBuildMatrixVerification.TrialComplete(landing), "New landing did not complete required samples.");
            shapePreviewPassed = true;
            Save();
            drag = LandingBuildMotionVerification.Drag(library, new(80, 0, 0), Table, "ground-swing");
            Console.WriteLine("stretch/former-trigger-ground-swing: " + (drag.Passed ? "PASS" : $"FAIL {drag.Failure}"));
            Save();
            if (!drag.Passed)
            {
                outcome = drag.Failure is null || drag.Failure.Kind == LandingFailureKind.Incomplete
                    ? "execution-incomplete" : "drag-hard-failure-stop";
                return outcome == "execution-incomplete" ? 2 : 1;
            }
            Require(drag.ExecutionCompleted && drag.CompletedFrames == 6660 && drag.RenderedFrames == 6660,
                "Former-trigger drag did not complete.");
            outcome = "stretch-preview-passed-limited-validation-only";
            return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    }

    private static LandingTrial ImportSlim(byte[] bytes, byte[] landingBytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Require(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("kind").GetString() == "landing-build-stronger-shape-preview" &&
            root.GetProperty("completed").GetBoolean() && root.GetProperty("outcome").GetString() == "shape-preview-completed-full-validation-required" &&
            root.GetProperty("candidate").GetString() == LandingBuildMapping.Version(LandingBuildTable.C) &&
            root.GetProperty("nativeSha256").GetString() == LandingBuildMapping.ApprovedNativeSha256 &&
            root.GetProperty("sourceSha256").GetString() == Convert.ToHexString(SHA256.HashData(landingBytes)), "C preview source is not the completed compatible source.");
        var trials = root.GetProperty("newTrials").Deserialize<LandingTrial[]>(LandingBuildMatrixVerification.JsonOptions)!;
        Require(trials.Length == 2 && trials.All(LandingBuildMatrixVerification.TrialComplete), "C endpoint trials incomplete.");
        var slim = trials.Single(t => t.Controls == new LandingBuildControls(LandingBuild: 100));
        Require(slim.MappingVersion == LandingBuildMapping.Version(LandingBuildTable.C) && slim.Configuration == slim.Controls.Configure(Table),
            "Reused slim configuration changed.");
        return slim;
    }

    private static LandingMotionTrial ImportStop(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Require(root.GetProperty("kind").GetString() == "landing-build-motion-matrix" && root.GetProperty("completed").GetBoolean() &&
            root.GetProperty("outcome").GetString() == "hard-failure-stop" &&
            root.GetProperty("selectedTable").GetString() == LandingBuildMapping.Version(LandingBuildTable.B) &&
            root.GetProperty("native").GetProperty("Sha256").GetString() == LandingBuildMapping.ApprovedNativeSha256 &&
            root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks, "Prior B stop source differs.");
        var trial = root.GetProperty("trials").EnumerateArray().Last().Deserialize<LandingMotionTrial>(LandingBuildMatrixVerification.JsonOptions)!;
        Require(trial.Controls == new LandingBuildControls(80, 0, 0) && trial.Kind == "drag" && trial.Profile == "ground-swing" &&
            trial.Configuration == trial.Controls.Configure(LandingBuildTable.B) && !trial.Passed && !trial.ExecutionCompleted &&
            trial.Failure?.Kind == LandingFailureKind.HealthOrStop && trial.Failure.Frame == 4913 && trial.AttemptedFrame == 4913 &&
            trial.Failure.Detail.Contains("-96", StringComparison.Ordinal), "Former failure case is not the documented B trigger.");
        return trial;
    }

    private static void ConfigurationChecks(List<CheckResult> checks)
    {
        void Check(string name, Action action)
        {
            try { action(); checks.Add(new(name, true, new { scope = "configuration only; no physical simulation" })); }
            catch (Exception error) { checks.Add(new(name, false, new { error = error.ToString() })); }
            Console.WriteLine($"{name}: {(checks[^1].Passed ? "PASS" : "FAIL")}");
        }
        Check("stretch-midpoints-and-only-four-fields", () =>
        {
            foreach (var size in new[] { 80d, 100d, 120d })
            foreach (var softness in new[] { 0d, 50d, 100d })
            {
                var baseline = new MaterialControls(size, softness, 90).Configure();
                Require(new LandingBuildControls(size, softness, 50).Configure(Table) == baseline, "Midpoint changed.");
                foreach (var build in new[] { 0d, 25d, 50d, 75d, 100d })
                {
                    var candidate = new LandingBuildControls(size, softness, build).Configure(Table);
                    Require(candidate with { RadialElasticity = baseline.RadialElasticity, PerimeterElasticity = baseline.PerimeterElasticity,
                        AreaPreservationStrength = baseline.AreaPreservationStrength, RadialMaximumRatio = baseline.RadialMaximumRatio } == baseline,
                        "Natural geometry or another unapproved parameter changed.");
                    var expectedCap = build < 50 ? 1.30 + (1.22 - 1.30) * build / 50 : 1.22;
                    Require(Math.Abs(candidate.RadialMaximumRatio - expectedCap) < 1e-14, "Fat-side cap interpolation differs.");
                    if (build >= 50) Require(candidate == new LandingBuildControls(size, softness, build).Configure(LandingBuildTable.C), "Slim-side configuration changed.");
                }
            }
        });
        Check("stretch-declared-factors-and-continuity", () =>
        {
            foreach (var (build, radial, perimeter, area) in new[]
            {
                (0d, .50, .70, 1.00), (25d, .75, .85, 1.00), (50d, 1d, 1d, 1d),
                (75d, 1.30, 1.075, 1.075), (100d, 1.60, 1.15, 1.15),
            })
            {
                var controls = new LandingBuildControls(LandingBuild: build);
                var candidate = controls.Configure(Table);
                var baseline = new MaterialControls().Configure();
                Require(Math.Abs(candidate.RadialElasticity - baseline.RadialElasticity * radial) < 1e-13 &&
                    Math.Abs(candidate.PerimeterElasticity - baseline.PerimeterElasticity * perimeter) < 1e-13 &&
                    Math.Abs(candidate.AreaPreservationStrength - baseline.AreaPreservationStrength * area) < 1e-13,
                    "Declared material factors differ.");
            }
            var left = new LandingBuildControls(LandingBuild: 49.999999).Configure(Table);
            var mid = new LandingBuildControls().Configure(Table);
            var right = new LandingBuildControls(LandingBuild: 50.000001).Configure(Table);
            Require(Math.Abs(left.RadialMaximumRatio - mid.RadialMaximumRatio) < 2e-9 && right.RadialMaximumRatio == mid.RadialMaximumRatio &&
                Math.Abs(left.RadialElasticity - mid.RadialElasticity) < 1e-7 && Math.Abs(right.RadialElasticity - mid.RadialElasticity) < 1e-7,
                "Midpoint continuity changed.");
        });
    }

    private static EvidenceSource Source(string path, byte[] bytes) => new(Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(bytes)));
    private static void Require(bool condition, string detail) => LandingBuildMatrixVerification.Require(condition, detail);
}
