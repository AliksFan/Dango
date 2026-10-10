using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dango.MaterialSample;

// A traceable single-field repair. Previous tables, failures and gate contracts remain intact.
internal static class LandingBuildRecoveryVerification
{
    internal const LandingBuildTable Table = LandingBuildTable.RecoveryPreview;
    private sealed record EvidenceSource(string FileName, string Sha256);
    private sealed record ReusedLanding(LandingBuildControls Controls, string MappingVersion, EvidenceSource Source);
    private sealed record ShapePreservation(double Scale, double OriginalWidth, double OriginalHeight,
        double CandidateWidth, double CandidateHeight, double WidthDelta, double HeightDelta,
        double MaximumAllowedDelta, bool Passed);
    private static readonly System.Text.Json.JsonSerializerOptions Options = LandingBuildMatrixVerification.JsonOptions;

    internal static int Preview(string library, string landingPath, string stoppedMotionPath, string output)
    {
        try { return PreviewCore(library, landingPath, stoppedMotionPath, output); }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }

    private static int PreviewCore(string library, string landingPath, string stoppedMotionPath, string output)
    {
        var native = LandingBuildMatrixVerification.RequireNative(library);
        var landingBytes = File.ReadAllBytes(landingPath);
        var previous = LandingBuildStretchMatrixVerification.ImportLanding(landingBytes);
        var stopBytes = File.ReadAllBytes(stoppedMotionPath);
        var originalFailure = ImportStop(stopBytes, Hash(landingBytes))[^1];
        var originalFat = previous.Single(t => t.Controls == new LandingBuildControls(LandingBuild: 0));
        var tool = Tool();
        var checks = new List<CheckResult>();
        LandingMotionTrial? recovery = null;
        LandingTrial? landing = null;
        ShapePreservation? shapePreservation = null;
        var outcome = "running";
        var completed = false;
        string? executionError = null;
        using var report = CreateReport(output);
        void Save() => SaveReport(report, new
        {
            schema = 1, kind = "landing-build-recovery-preview", completed, outcome,
            candidate = LandingBuildMapping.Version(Table), frozenForProduction = false,
            native, tool, frameStepTicks = MaterialSession.FixedStep.Ticks,
            sources = new { landing = Source(landingPath, landingBytes), stoppedMotion = Source(stoppedMotionPath, stopBytes) },
            configurationChecks = checks, originalFailure, originalFat, recovery, landing, shapePreservation, executionError,
            acceptance = "Only one corner recovery and one default fat landing/one-pixel shape gate; full candidate matrix, long stability and live manual acceptance remain required.",
        });
        Save();
        try
        {
            ConfigurationChecks(checks);
            Save();
            if (checks.Any(c => !c.Passed)) { outcome = "configuration-failure-stop"; return 1; }
            recovery = LandingBuildMotionVerification.Recovery(library, new(120, 100, 0), Table);
            Console.WriteLine("recovery-preview/former-failed-recovery: " + (recovery.Passed ? "PASS" : $"FAIL {recovery.Failure}"));
            Save();
            if (!MotionComplete(recovery)) return Stop(recovery.Failure, ref outcome);
            landing = LandingBuildVerification.Land(library, new(LandingBuild: 0), Table);
            Console.WriteLine("recovery-preview/default-fat-landing: " + (landing.Failure is null ? "COMPLETE" : $"FAIL {landing.Failure}"));
            Save();
            if (landing.Failure is not null) return Stop(landing.Failure, ref outcome);
            Require(LandingBuildMatrixVerification.TrialComplete(landing), "Default fat landing samples incomplete.");
            shapePreservation = CompareShape(originalFat, landing);
            Console.WriteLine($"recovery-preview/default-shape: widthDelta={shapePreservation.WidthDelta:R}, heightDelta={shapePreservation.HeightDelta:R}, passed={shapePreservation.Passed}");
            Save();
            if (!shapePreservation.Passed) { outcome = "shape-preservation-failure-stop"; return 1; }
            outcome = "recovery-preview-passed-limited-validation-only";
            return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    }

    internal static int RunLanding(string library, string previewPath, string previousLandingPath, string stoppedMotionPath, string output)
    {
        try { return RunLandingCore(library, previewPath, previousLandingPath, stoppedMotionPath, output); }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }

    private static int RunLandingCore(string library, string previewPath, string previousLandingPath, string stoppedMotionPath, string output)
    {
        var native = LandingBuildMatrixVerification.RequireNative(library);
        var previousBytes = File.ReadAllBytes(previousLandingPath);
        var previous = LandingBuildStretchMatrixVerification.ImportLanding(previousBytes);
        var previewBytes = File.ReadAllBytes(previewPath);
        var stopBytes = File.ReadAllBytes(stoppedMotionPath);
        var (fat, _) = ImportPreview(previewBytes, previousBytes, stopBytes);
        var trials = previous.Where(t => t.Configuration == t.Controls.Configure(Table)).ToList();
        var reused = trials.Select(t => new ReusedLanding(t.Controls, t.MappingVersion, Source(previousLandingPath, previousBytes))).ToList();
        Require(trials.Count == 27 && trials.All(t => t.Controls.LandingBuild >= 50), "Unchanged landing source set differs.");
        trials.Add(fat);
        reused.Add(new(fat.Controls, fat.MappingVersion, Source(previewPath, previewBytes)));
        var tool = Tool();
        var effects = new List<LandingFailure>();
        var completed = false;
        var outcome = "running";
        string? executionError = null;
        using var report = CreateReport(output);
        void Save() => SaveReport(report, new
        {
            schema = 1, kind = "landing-build-recovery-landing-matrix", completed, outcome,
            candidate = LandingBuildMapping.Version(Table), frozenForProduction = false, native, tool,
            frameStepTicks = MaterialSession.FixedStep.Ticks, requiredTrials = 45,
            sources = new { preview = Source(previewPath, previewBytes), previousLanding = Source(previousLandingPath, previousBytes), stoppedMotion = Source(stoppedMotionPath, stopBytes) },
            reusedTrials = reused, trials, effectFailures = effects, executionError,
            acceptance = "Landing matrix only; motion, long stability and live manual acceptance remain required.",
        });
        Save();
        try
        {
            foreach (var size in new[] { 100d, 80d, 120d })
            {
                foreach (var controls in LandingBuildMatrixVerification.AllControls().Where(c => c.SizePercent == size))
                {
                    if (trials.Any(t => t.Controls == controls)) continue;
                    var trial = LandingBuildVerification.Land(library, controls, Table);
                    trials.Add(trial);
                    Console.WriteLine($"recovery/size={size}/soft={controls.Softness}/build={controls.LandingBuild}: " +
                        (trial.Failure is null ? "COMPLETE" : $"FAIL {trial.Failure}"));
                    Save();
                    if (trial.Failure is not null) return Stop(trial.Failure, ref outcome);
                    Require(LandingBuildMatrixVerification.TrialComplete(trial), "Landing trial ended without required samples.");
                }
                effects.AddRange(LandingBuildVerification.EvaluateEffects(trials.Where(t => t.Controls.SizePercent == size).ToArray(), size));
                Save();
                if (effects.Count != 0) { outcome = "effect-failure-stop"; return 1; }
            }
            Require(trials.Count == 45 && trials.Select(t => t.Controls).ToHashSet().SetEquals(LandingBuildMatrixVerification.AllControls()) &&
                trials.All(t => LandingBuildMatrixVerification.TrialComplete(t) && t.Configuration == t.Controls.Configure(Table)),
                "Recovery landing matrix incomplete.");
            outcome = "recovery-landing-matrix-passed-motion-and-long-required";
            return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    }

    private static IReadOnlyList<LandingMotionTrial> ImportStop(byte[] bytes, string landingHash)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        RequireRoot(root, "landing-build-stretch-motion-matrix", "hard-failure-stop", LandingBuildTable.StretchPreview);
        Require(root.GetProperty("requiredTrials").GetInt32() == 171 &&
            root.GetProperty("sources").GetProperty("landing").GetProperty("Sha256").GetString() == landingHash,
            "Stopped v3 motion source differs from landing predecessor.");
        var trials = root.GetProperty("trials").Deserialize<LandingMotionTrial[]>(Options)!;
        Require(trials.Length == 85 && trials.Select(t => (t.Kind, t.Controls, t.Profile)).Distinct().Count() == 85 &&
            trials.Take(84).All(t => LandingBuildStretchMatrixVerification.MotionComplete(t, LandingBuildTable.StretchPreview)),
            "Stopped v3 trial set is not the documented complete prefix.");
        var failure = trials[^1];
        Require(failure.Kind == "recovery" && failure.Controls == new LandingBuildControls(120, 100, 0) &&
            failure.MappingVersion == LandingBuildMapping.Version(LandingBuildTable.StretchPreview) &&
            failure.Configuration == failure.Controls.Configure(LandingBuildTable.StretchPreview) &&
            failure.ExecutionCompleted && !failure.Passed && failure.Failure?.Kind == LandingFailureKind.Recovery &&
            failure.CompletedFrames == 169 && failure.RenderedFrames == 169 && failure.LongestRoundRun == 17 &&
            failure.RecoveryStartReleaseIndex is null && failure.RecoverySeconds is null, "Original recovery failure differs.");
        return trials;
    }

    private static (LandingTrial Fat, LandingMotionTrial Recovery) ImportPreview(byte[] bytes, byte[] previousLandingBytes, byte[] stoppedMotionBytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        RequireRoot(root, "landing-build-recovery-preview", "recovery-preview-passed-limited-validation-only", Table);
        RequireTool(root);
        Require(root.GetProperty("sources").GetProperty("landing").GetProperty("Sha256").GetString() == Hash(previousLandingBytes) &&
            root.GetProperty("sources").GetProperty("stoppedMotion").GetProperty("Sha256").GetString() == Hash(stoppedMotionBytes) &&
            root.GetProperty("configurationChecks").GetArrayLength() == 1 &&
            root.GetProperty("configurationChecks")[0].GetProperty("Passed").GetBoolean(), "Recovery preview predecessor/checks differ.");
        var previous = LandingBuildStretchMatrixVerification.ImportLanding(previousLandingBytes);
        var originalFailure = ImportStop(stoppedMotionBytes, Hash(previousLandingBytes))[^1];
        Require(JsonSerializer.Serialize(root.GetProperty("originalFailure").Deserialize<LandingMotionTrial>(Options), Options) ==
            JsonSerializer.Serialize(originalFailure, Options), "Preview original failure differs from the stopped source.");
        var original = root.GetProperty("originalFat").Deserialize<LandingTrial>(Options)!;
        Require(JsonSerializer.Serialize(original, Options) == JsonSerializer.Serialize(previous.Single(t => t.Controls == new LandingBuildControls(LandingBuild: 0)), Options),
            "Preview original fat is not the exact previous source.");
        var fat = root.GetProperty("landing").Deserialize<LandingTrial>(Options)!;
        var recovery = root.GetProperty("recovery").Deserialize<LandingMotionTrial>(Options)!;
        Require(fat.Controls == new LandingBuildControls(LandingBuild: 0) && fat.MappingVersion == LandingBuildMapping.Version(Table) &&
            fat.Configuration == fat.Controls.Configure(Table) && LandingBuildMatrixVerification.TrialComplete(fat) &&
            recovery.Kind == "recovery" && recovery.Controls == new LandingBuildControls(120, 100, 0) &&
            recovery.MappingVersion == LandingBuildMapping.Version(Table) && MotionComplete(recovery), "New recovery preview trials incomplete.");
        var shape = root.GetProperty("shapePreservation").Deserialize<ShapePreservation>(Options)!;
        Require(shape.Passed && shape == CompareShape(original, fat), "Preview shape gate differs from independent recomputation.");
        return (fat, recovery);
    }

    private static void ConfigurationChecks(List<CheckResult> checks)
    {
        const string name = "recovery-only-perimeter-coefficient-and-unchanged-midpoint-slim";
        try
        {
            foreach (var size in new[] { 80d, 100d, 120d })
            foreach (var softness in new[] { 0d, 50d, 100d })
            for (var build = 0; build <= 100; build++)
            {
                var controls = new LandingBuildControls(size, softness, build);
                var old = controls.Configure(LandingBuildTable.StretchPreview);
                var candidate = controls.Configure(Table);
                Require(candidate with { PerimeterElasticity = old.PerimeterElasticity } == old, "Changed another physical property.");
                if (build >= 50) Require(candidate == old, "Changed midpoint/slim configuration.");
                var expectedFactor = build < 50 ? .72 + (1 - .72) * (build / 50d) : LandingBuildMapping.Factors(LandingBuildTable.StretchPreview, build).Perimeter;
                var baseline = new MaterialControls(size, softness, 90).Configure();
                Require(Math.Abs(candidate.PerimeterElasticity - baseline.PerimeterElasticity * expectedFactor) < 1e-13,
                    "Perimeter coefficient/interpolation differs.");
            }
            checks.Add(new(name, true, new { scope = "configuration only; no physical simulation" }));
        }
        catch (Exception error) { checks.Add(new(name, false, new { error = error.ToString() })); }
        Console.WriteLine($"{name}: {(checks[^1].Passed ? "PASS" : "FAIL")}");
    }

    private static ShapePreservation CompareShape(LandingTrial original, LandingTrial candidate)
    {
        const double scale = 190;
        var before = SampleGeometry.Create(original.LastHealthyFrame!, scale: scale, bodySize: original.Controls.SizePercent / 100).Bounds;
        var after = SampleGeometry.Create(candidate.LastHealthyFrame!, scale: scale, bodySize: candidate.Controls.SizePercent / 100).Bounds;
        var widthDelta = after.Width - before.Width;
        var heightDelta = after.Height - before.Height;
        return new(scale, before.Width, before.Height, after.Width, after.Height, widthDelta, heightDelta, 1,
            double.IsFinite(widthDelta) && double.IsFinite(heightDelta) && Math.Abs(widthDelta) <= 1 && Math.Abs(heightDelta) <= 1);
    }

    private static void RequireRoot(JsonElement root, string kind, string outcome, LandingBuildTable table)
    {
        Require(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("kind").GetString() == kind &&
            root.GetProperty("completed").GetBoolean() && root.GetProperty("outcome").GetString() == outcome &&
            root.GetProperty("candidate").GetString() == LandingBuildMapping.Version(table) && !root.GetProperty("frozenForProduction").GetBoolean() &&
            root.GetProperty("native").GetProperty("Sha256").GetString() == LandingBuildMapping.ApprovedNativeSha256 &&
            root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks &&
            root.GetProperty("executionError").ValueKind == JsonValueKind.Null, "Predecessor metadata/outcome/native differs.");
    }

    private static bool MotionComplete(LandingMotionTrial trial) => LandingBuildStretchMatrixVerification.MotionComplete(trial, Table);
    private static int Stop(LandingFailure? failure, ref string outcome)
    {
        var incomplete = failure is null || failure.Kind == LandingFailureKind.Incomplete;
        outcome = incomplete ? "execution-incomplete" : "hard-failure-stop";
        return incomplete ? 2 : 1;
    }
    private static void RequireTool(JsonElement root) => Require(root.GetProperty("tool").GetProperty("Sha256").GetString() is { Length: 64 } hash &&
        hash.All(Uri.IsHexDigit), "Predecessor tool fingerprint missing or invalid.");
    private static EvidenceSource Tool() => Source(typeof(LandingBuildRecoveryVerification).Assembly.Location,
        File.ReadAllBytes(typeof(LandingBuildRecoveryVerification).Assembly.Location));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static EvidenceSource Source(string path, byte[] bytes) => new(Path.GetFileName(path), Hash(bytes));
    private static FileStream CreateReport(string output)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        return new(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }
    private static void SaveReport(FileStream report, object value)
    {
        report.Position = 0; JsonSerializer.Serialize(report, value, Options);
        report.SetLength(report.Position); report.Flush();
    }
    private static void Require(bool condition, string detail) => LandingBuildMatrixVerification.Require(condition, detail);
}
