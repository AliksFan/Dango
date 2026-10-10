using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dango.MaterialSample;

// Fixed StretchPreview validation; never resumes B calibration or selects a fallback.
internal static class LandingBuildStretchMatrixVerification
{
    private const LandingBuildTable Table = LandingBuildTable.StretchPreview;
    private sealed record EvidenceSource(string FileName, string Sha256);
    private sealed record ReusedLanding(LandingBuildControls Controls, string MappingVersion, EvidenceSource Source);
    private sealed record ReusedMotion(string Kind, string? Profile, LandingBuildControls Controls, string MappingVersion, EvidenceSource Source);

    internal static int RunLanding(string library, string previewPath, string baselinePath, string output)
    {
        try { return RunLandingCore(library, previewPath, baselinePath, output); }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }

    private static int RunLandingCore(string library, string previewPath, string baselinePath, string output)
    {
        var native = LandingBuildMatrixVerification.RequireNative(library);
        var baselineBytes = File.ReadAllBytes(baselinePath);
        var baseline = LandingBuildMatrixVerification.ImportLanding(baselineBytes);
        var previewBytes = File.ReadAllBytes(previewPath);
        var preview = ImportPreview(previewBytes, Hash(baselineBytes));
        var trials = baseline.Where(t => t.Controls.LandingBuild == 50).ToList();
        var reused = trials.Select(t => new ReusedLanding(t.Controls, t.MappingVersion, Source(baselinePath, baselineBytes))).ToList();
        var previewMidpoint = preview.Single(t => t.Controls == new LandingBuildControls());
        var originalMidpoint = trials.Single(t => t.Controls == new LandingBuildControls());
        Require(JsonSerializer.Serialize(previewMidpoint, LandingBuildMatrixVerification.JsonOptions) ==
            JsonSerializer.Serialize(originalMidpoint, LandingBuildMatrixVerification.JsonOptions), "Preview midpoint differs from its original B source.");
        foreach (var trial in preview.Where(t => t.Controls.LandingBuild != 50))
        {
            trials.Add(trial);
            reused.Add(new(trial.Controls, trial.MappingVersion, Source(previewPath, previewBytes)));
        }
        Require(trials.Count == 11 && trials.Select(t => t.Controls).Distinct().Count() == 11 &&
            trials.All(t => LandingBuildMatrixVerification.TrialComplete(t) && t.Configuration == t.Controls.Configure(Table)),
            "Reused landing configurations or complete trial set differ.");
        var effectFailures = new List<LandingFailure>();
        var completed = false;
        var outcome = "running";
        string? executionError = null;
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        void Save()
        {
            report.Position = 0;
            JsonSerializer.Serialize(report, new
            {
                schema = 1, kind = "landing-build-stretch-landing-matrix", completed, outcome,
                candidate = LandingBuildMapping.Version(Table), frozenForProduction = false, native,
                frameStepTicks = MaterialSession.FixedStep.Ticks, requiredTrials = 45,
                sources = new { preview = Source(previewPath, previewBytes), baseline = Source(baselinePath, baselineBytes) },
                reusedTrials = reused, trials, effectFailures, executionError,
                acceptance = "Landing matrix only; motion, long stability, interactive window and physical manual acceptance remain required.",
            }, LandingBuildMatrixVerification.JsonOptions);
            report.SetLength(report.Position); report.Flush();
        }
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
                    Console.WriteLine($"stretch/size={size}/soft={controls.Softness}/build={controls.LandingBuild}: " +
                        (trial.Failure is null ? "COMPLETE" : $"FAIL {trial.Failure.Kind} frame={trial.Failure.Frame}: {trial.Failure.Detail}"));
                    Save();
                    if (trial.Failure is not null)
                    {
                        outcome = trial.Failure.Kind == LandingFailureKind.Incomplete ? "execution-incomplete" : "hard-failure-stop";
                        return trial.Failure.Kind == LandingFailureKind.Incomplete ? 2 : 1;
                    }
                    Require(LandingBuildMatrixVerification.TrialComplete(trial), "New landing trial ended without all required samples.");
                }
                effectFailures.AddRange(LandingBuildVerification.EvaluateEffects(trials.Where(t => t.Controls.SizePercent == size).ToArray(), size));
                Save();
                if (effectFailures.Count != 0) { outcome = "effect-failure-stop"; return 1; }
            }
            Require(trials.Count == 45 && trials.Select(t => t.Controls).ToHashSet().SetEquals(LandingBuildMatrixVerification.AllControls()) &&
                trials.All(t => LandingBuildMatrixVerification.TrialComplete(t) && t.Configuration == t.Controls.Configure(Table)),
                "Stretch landing matrix is incomplete.");
            outcome = "stretch-landing-matrix-passed-motion-and-long-required";
            return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    }

    internal static int RunMotion(string library, string landingPath, string previewPath, string oldMotionPath, string output)
    {
        try { return RunMotionCore(library, landingPath, previewPath, oldMotionPath, output); }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }

    private static int RunMotionCore(string library, string landingPath, string previewPath, string oldMotionPath, string output)
    {
        var native = LandingBuildMatrixVerification.RequireNative(library);
        var landingBytes = File.ReadAllBytes(landingPath);
        _ = ImportLanding(landingBytes);
        using var landingDocument = JsonDocument.Parse(landingBytes);
        var previewBytes = File.ReadAllBytes(previewPath);
        Require(landingDocument.RootElement.GetProperty("sources").GetProperty("preview").GetProperty("Sha256").GetString() == Hash(previewBytes),
            "Landing predecessor preview source differs.");
        _ = ImportPreview(previewBytes, landingDocument.RootElement.GetProperty("sources").GetProperty("baseline").GetProperty("Sha256").GetString()!);
        using var previewDocument = JsonDocument.Parse(previewBytes);
        var oldMotionBytes = File.ReadAllBytes(oldMotionPath);
        Require(previewDocument.RootElement.GetProperty("sources").GetProperty("oldStop").GetProperty("Sha256").GetString() == Hash(oldMotionBytes),
            "Old motion source differs from the limited-preview provenance.");
        var trials = ImportMidpointMotion(oldMotionBytes).ToList();
        var reused = trials.Select(t => new ReusedMotion(t.Kind, t.Profile, t.Controls, t.MappingVersion, Source(oldMotionPath, oldMotionBytes))).ToList();
        var reusedDrag = previewDocument.RootElement.GetProperty("drag").Deserialize<LandingMotionTrial>(LandingBuildMatrixVerification.JsonOptions)!;
        trials.Add(reusedDrag);
        reused.Add(new(reusedDrag.Kind, reusedDrag.Profile, reusedDrag.Controls, reusedDrag.MappingVersion, Source(previewPath, previewBytes)));
        Require(trials.Count == 19 && trials.All(MotionComplete), "Reused motion trial set incomplete.");
        var completed = false;
        var outcome = "running";
        string? executionError = null;
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        void Save()
        {
            report.Position = 0;
            JsonSerializer.Serialize(report, new
            {
                schema = 1, kind = "landing-build-stretch-motion-matrix", completed, outcome,
                candidate = LandingBuildMapping.Version(Table), frozenForProduction = false, native,
                frameStepTicks = MaterialSession.FixedStep.Ticks, requiredTrials = 171,
                sources = new { landing = Source(landingPath, landingBytes), preview = Source(previewPath, previewBytes), oldMotion = Source(oldMotionPath, oldMotionBytes) },
                reusedTrials = reused, trials, executionError,
                acceptance = "45 flight + 45 recovery + 81 drag; long stability, interactive window and physical manual acceptance remain required.",
            }, LandingBuildMatrixVerification.JsonOptions);
            report.SetLength(report.Position); report.Flush();
        }
        int? Record(LandingMotionTrial trial)
        {
            trials.Add(trial);
            Console.WriteLine($"stretch/{trial.Kind}/{trial.Profile}/size={trial.Controls.SizePercent}/soft={trial.Controls.Softness}/build={trial.Controls.LandingBuild}: " +
                (trial.Passed ? "PASS" : $"FAIL {trial.Failure?.Kind} frame={trial.Failure?.Frame}: {trial.Failure?.Detail}"));
            Save();
            if (MotionComplete(trial)) return null;
            var incomplete = trial.Failure is null || trial.Failure.Kind == LandingFailureKind.Incomplete;
            outcome = incomplete ? "execution-incomplete" : "hard-failure-stop";
            return incomplete ? 2 : 1;
        }
        Save();
        try
        {
            foreach (var controls in LandingBuildMatrixVerification.AllControls())
            {
                if (!trials.Any(t => t.Kind == "flight" && t.Controls == controls))
                {
                    var result = Record(LandingBuildMotionVerification.Flight(library, controls, Table));
                    if (result.HasValue) return result.Value;
                }
                if (!trials.Any(t => t.Kind == "recovery" && t.Controls == controls))
                {
                    var result = Record(LandingBuildMotionVerification.Recovery(library, controls, Table));
                    if (result.HasValue) return result.Value;
                }
            }
            foreach (var controls in LandingBuildMatrixVerification.AllControls().Where(c => c.LandingBuild is 0 or 50 or 100))
            foreach (var profile in new[] { "changing", "horizontal", "ground-swing" })
            {
                if (trials.Any(t => t.Kind == "drag" && t.Controls == controls && t.Profile == profile)) continue;
                var result = Record(LandingBuildMotionVerification.Drag(library, controls, Table, profile));
                if (result.HasValue) return result.Value;
            }
            Require(trials.Count == 171 && trials.All(MotionComplete) &&
                trials.Select(t => (t.Kind, t.Controls, t.Profile)).Distinct().Count() == 171, "Stretch motion matrix incomplete.");
            outcome = "stretch-motion-matrix-passed-long-required";
            return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    }

    internal static IReadOnlyList<LandingTrial> ImportLanding(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Require(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("kind").GetString() == "landing-build-stretch-landing-matrix" &&
            root.GetProperty("completed").GetBoolean() && root.GetProperty("outcome").GetString() == "stretch-landing-matrix-passed-motion-and-long-required" &&
            root.GetProperty("candidate").GetString() == LandingBuildMapping.Version(Table) && !root.GetProperty("frozenForProduction").GetBoolean() &&
            root.GetProperty("native").GetProperty("Sha256").GetString() == LandingBuildMapping.ApprovedNativeSha256 &&
            root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks && root.GetProperty("requiredTrials").GetInt32() == 45 &&
            root.GetProperty("effectFailures").GetArrayLength() == 0 && root.GetProperty("executionError").ValueKind == JsonValueKind.Null,
            "Stretch landing predecessor is not a complete compatible passing matrix.");
        var trials = root.GetProperty("trials").Deserialize<LandingTrial[]>(LandingBuildMatrixVerification.JsonOptions)!;
        Require(trials.Length == 45 && trials.Select(t => t.Controls).ToHashSet().SetEquals(LandingBuildMatrixVerification.AllControls()) &&
            trials.All(t => LandingBuildMatrixVerification.TrialComplete(t) && t.Configuration == t.Controls.Configure(Table) &&
                (t.MappingVersion == LandingBuildMapping.Version(Table) ||
                    t.Controls.LandingBuild == 50 && t.MappingVersion == LandingBuildMapping.Version(LandingBuildTable.B) ||
                    t.Controls == new LandingBuildControls(LandingBuild: 100) && t.MappingVersion == LandingBuildMapping.Version(LandingBuildTable.C))),
            "Stretch landing trials or origin configurations differ.");
        foreach (var size in new[] { 80d, 100d, 120d })
            Require(LandingBuildVerification.EvaluateEffects(trials.Where(t => t.Controls.SizePercent == size).ToArray(), size).Count == 0,
                "Imported stretch effects failed recomputation.");
        return trials;
    }

    private static IReadOnlyList<LandingMotionTrial> ImportMidpointMotion(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Require(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("kind").GetString() == "landing-build-motion-matrix" &&
            root.GetProperty("completed").GetBoolean() && root.GetProperty("outcome").GetString() == "hard-failure-stop" &&
            root.GetProperty("selectedTable").GetString() == LandingBuildMapping.Version(LandingBuildTable.B) &&
            root.GetProperty("native").GetProperty("Sha256").GetString() == LandingBuildMapping.ApprovedNativeSha256 &&
            root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks && root.GetProperty("requiredTrials").GetInt32() == 171,
            "Old motion source is not the documented B stopped matrix.");
        var oldTrials = root.GetProperty("trials").Deserialize<LandingMotionTrial[]>(LandingBuildMatrixVerification.JsonOptions)!;
        Require(oldTrials.Length == 93 && oldTrials[^1].Controls == new LandingBuildControls(80, 0, 0) &&
            oldTrials[^1].Profile == "ground-swing" && !oldTrials[^1].Passed && oldTrials[^1].Failure?.Frame == 4913 &&
            oldTrials[^1].Failure!.Kind == LandingFailureKind.HealthOrStop && oldTrials[^1].Failure!.Detail.Contains("-96", StringComparison.Ordinal),
            "Original B failure evidence differs.");
        var trials = oldTrials.Where(t => t.Controls.LandingBuild == 50 && t.Kind is "flight" or "recovery").ToArray();
        var expected = LandingBuildMatrixVerification.AllControls().Where(c => c.LandingBuild == 50)
            .SelectMany(c => new[] { ("flight", c), ("recovery", c) }).ToHashSet();
        Require(trials.Length == 18 && trials.Select(t => (t.Kind, t.Controls)).ToHashSet().SetEquals(expected) &&
            trials.All(t => t.MappingVersion == LandingBuildMapping.Version(LandingBuildTable.B) && MotionComplete(t)),
            "Old midpoint motion trials are not complete compatible evidence.");
        return trials;
    }

    private static bool MotionComplete(LandingMotionTrial trial) => MotionComplete(trial, Table);

    internal static bool MotionComplete(LandingMotionTrial trial, LandingBuildTable table)
    {
        var frames = trial.Kind switch { "flight" => 120, "recovery" => 169, "drag" => 6660, _ => 0 };
        if (frames == 0 || !trial.ExecutionCompleted || !trial.Passed || trial.Failure is not null ||
            trial.RequestedFrames != frames || trial.CompletedFrames != frames || trial.AttemptedFrame != frames ||
            trial.SessionReportedFrames != frames || trial.RenderedFrames != frames || trial.LastHealthyFrameNumber != frames ||
            trial.LastHealthyFrame is null || trial.Configuration != trial.Controls.Configure(table)) return false;
        return trial.Kind switch
        {
            "flight" => trial.Profile is null && trial.RoundWindowSamples == 60 && trial.RoundFrames == 60 &&
                trial.MaximumRoundWindowRadialSpread <= .04 && trial.MaximumRoundWindowNativeAspectError <= .04 &&
                trial.MaximumRoundWindowRenderedAspectError <= .04 && trial.MinimumRoundWindowCircularity >= .975,
            "recovery" => trial.Profile is null && trial.RoundWindowSamples == 108 && trial.ReleaseSpread >= .10 &&
                trial.RecoverySeconds <= 1.5 && trial.RecoveryStartReleaseIndex is >= 0 and <= 90 && trial.LongestRoundRun >= 18,
            "drag" => trial.Profile is "changing" or "horizontal" or "ground-swing" && trial.Moved >= .04,
            _ => false,
        };
    }

    private static IReadOnlyList<LandingTrial> ImportPreview(byte[] bytes, string baselineSha256)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Require(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("kind").GetString() == "landing-build-stretch-preview" &&
            root.GetProperty("completed").GetBoolean() && root.GetProperty("outcome").GetString() == "stretch-preview-passed-limited-validation-only" &&
            root.GetProperty("candidate").GetString() == LandingBuildMapping.Version(Table) &&
            !root.GetProperty("frozenForProduction").GetBoolean() && root.GetProperty("shapePreviewPassed").GetBoolean() &&
            root.GetProperty("native").GetProperty("Sha256").GetString() == LandingBuildMapping.ApprovedNativeSha256 &&
            root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks &&
            root.GetProperty("requiredNewLandingTrials").GetInt32() == 1 && root.GetProperty("requiredDragTrials").GetInt32() == 1 &&
            root.GetProperty("executionError").ValueKind == JsonValueKind.Null &&
            root.GetProperty("sources").GetProperty("landing").GetProperty("Sha256").GetString() == baselineSha256,
            "Predecessor is not the completed compatible limited stretch preview.");
        var checks = root.GetProperty("configurationChecks").EnumerateArray().ToArray();
        Require(checks.Length == 2 && checks.All(c => c.GetProperty("Passed").GetBoolean()), "Stretch configuration checks incomplete.");
        var trials = root.GetProperty("trials").Deserialize<LandingTrial[]>(LandingBuildMatrixVerification.JsonOptions)!;
        var expected = new[] { new LandingBuildControls(LandingBuild: 0), new LandingBuildControls(), new LandingBuildControls(LandingBuild: 100) };
        Require(trials.Length == 3 && trials.Select(t => t.Controls).ToHashSet().SetEquals(expected) &&
            trials.All(t => LandingBuildMatrixVerification.TrialComplete(t) && t.Configuration == t.Controls.Configure(Table) &&
                t.MappingVersion == LandingBuildMapping.Version(t.Controls.LandingBuild switch
                { 0 => Table, 50 => LandingBuildTable.B, 100 => LandingBuildTable.C, _ => throw new InvalidOperationException("Unexpected preview controls.") })),
            "Preview landing trials, origin versions or current configurations differ.");
        var drag = root.GetProperty("drag").Deserialize<LandingMotionTrial>(LandingBuildMatrixVerification.JsonOptions)!;
        Require(drag.Controls == new LandingBuildControls(80, 0, 0) && drag.MappingVersion == LandingBuildMapping.Version(Table) &&
            drag.Configuration == drag.Controls.Configure(Table) && drag.Kind == "drag" && drag.Profile == "ground-swing" &&
            drag.ExecutionCompleted && drag.Passed && drag.Failure is null && drag.RequestedFrames == 6660 &&
            drag.CompletedFrames == 6660 && drag.AttemptedFrame == 6660 && drag.SessionReportedFrames == 6660 && drag.RenderedFrames == 6660 && drag.Moved >= .04,
            "Limited predecessor drag did not complete with the fixed configuration and control gate.");
        return trials;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static EvidenceSource Source(string path, byte[] bytes) => new(Path.GetFileName(path), Hash(bytes));
    private static void Require(bool condition, string detail) => LandingBuildMatrixVerification.Require(condition, detail);
}
