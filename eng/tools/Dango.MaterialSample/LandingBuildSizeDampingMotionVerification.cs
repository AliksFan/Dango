using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dango.MaterialSample;

// S2 is admitted only by the reviewed, complete v6 S1 evidence. No fallback table.
internal static class LandingBuildSizeDampingMotionVerification
{
    internal const LandingBuildTable Table = LandingBuildTable.SizeDampingPreview;
    private const string LandingSha256 = "DC6C0AF2D85CC41F3133A4F82234EDF44990DFCF9B08DA6DD1332E2AEB3D28BC";
    private sealed record EvidenceSource(string FileName, string Sha256);
    private sealed record ReusedMotion(string Kind, string? Profile, LandingBuildControls Controls,
        string MappingVersion, EvidenceSource Source);
    private static readonly JsonSerializerOptions Options = LandingBuildMatrixVerification.JsonOptions;

    internal static int Run(string library, string landingPath, string originalLandingPath, string originalMotionPath, string output)
    {
        try { return RunCore(library, landingPath, originalLandingPath, originalMotionPath, output); }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }

    private static int RunCore(string library, string landingPath, string originalLandingPath, string originalMotionPath, string output)
    {
        var native = LandingBuildMatrixVerification.RequireNative(library);
        var landingBytes = File.ReadAllBytes(landingPath);
        var oldLandingBytes = File.ReadAllBytes(originalLandingPath);
        var oldMotionBytes = File.ReadAllBytes(originalMotionPath);
        Require(Hash(landingBytes) == LandingSha256, "S1 differs from the reviewed complete size-damping evidence.");
        var oldLanding = LandingBuildStretchMatrixVerification.ImportLanding(oldLandingBytes);
        var oldMotion = LandingBuildRecoveryVerification.ImportStop(oldMotionBytes, Hash(oldLandingBytes));
        using var document = JsonDocument.Parse(landingBytes);
        var root = document.RootElement;
        Require(root.GetProperty("kind").GetString() == "landing-build-size-damping-landing-matrix" &&
            root.GetProperty("completed").GetBoolean() && root.GetProperty("outcome").GetString() == "size-damping-landing-passed-motion-and-long-required" &&
            root.GetProperty("candidate").GetString() == LandingBuildMapping.Version(Table) &&
            !root.GetProperty("frozenForProduction").GetBoolean() && root.GetProperty("defaultShapePreserved").GetBoolean() &&
            root.GetProperty("native").GetProperty("Sha256").GetString() == LandingBuildMapping.ApprovedNativeSha256 &&
            root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks &&
            root.GetProperty("sources").GetProperty("previousLanding").GetProperty("Sha256").GetString() == Hash(oldLandingBytes) &&
            root.GetProperty("sources").GetProperty("previousMotion").GetProperty("Sha256").GetString() == Hash(oldMotionBytes) &&
            root.GetProperty("requiredTrials").GetInt32() == 45 && root.GetProperty("effectFailures").GetArrayLength() == 0 &&
            root.GetProperty("executionError").ValueKind == JsonValueKind.Null, "S1 metadata or provenance differs.");
        var landing = root.GetProperty("trials").Deserialize<LandingTrial[]>(Options)!;
        Require(landing.Length == 45 && landing.Select(t => t.Controls).ToHashSet().SetEquals(LandingBuildMatrixVerification.AllControls()) &&
            landing.All(t => LandingBuildMatrixVerification.TrialComplete(t) && t.Configuration == t.Controls.Configure(Table)),
            "S1 trial set incomplete or mapping changed.");
        var sameConfiguration = oldLanding.Where(t => t.Configuration == t.Controls.Configure(Table)).ToArray();
        Require(sameConfiguration.Length == 43 && sameConfiguration.All(t =>
            JsonSerializer.Serialize(t, Options) == JsonSerializer.Serialize(landing.Single(n => n.Controls == t.Controls), Options)),
            "S1 reused evidence no longer equals its original source.");
        foreach (var size in new[] { 80d, 100d, 120d })
            Require(LandingBuildVerification.EvaluateEffects(landing.Where(t => t.Controls.SizePercent == size).ToArray(), size).Count == 0,
                "S1 effect recomputation failed.");
        var recovery = root.GetProperty("recovery").Deserialize<LandingMotionTrial>(Options)!;
        Require(recovery.Controls == new LandingBuildControls(120, 100, 0) && recovery.Kind == "recovery" &&
            recovery.MappingVersion == LandingBuildMapping.Version(Table) && Complete(recovery), "S1 recovery evidence incomplete.");
        var trials = oldMotion.Take(84).Where(t => Complete(t)).ToList();
        Require(trials.Count == 83, "Exact compatible passing v3 motion prefix differs.");
        var reused = trials.Select(t => new ReusedMotion(t.Kind, t.Profile, t.Controls, t.MappingVersion,
            Source(originalMotionPath, oldMotionBytes))).ToList();
        trials.Add(recovery);
        reused.Add(new(recovery.Kind, recovery.Profile, recovery.Controls, recovery.MappingVersion, Source(landingPath, landingBytes)));
        var toolPath = typeof(LandingBuildSizeDampingMotionVerification).Assembly.Location;
        var tool = Source(toolPath, File.ReadAllBytes(toolPath));
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
                schema = 1, kind = "landing-build-size-damping-motion-matrix", candidate = LandingBuildMapping.Version(Table),
                completed, outcome, frozenForProduction = false, native, tool, frameStepTicks = MaterialSession.FixedStep.Ticks,
                requiredTrials = 171,
                sources = new { landing = Source(landingPath, landingBytes), originalLanding = Source(originalLandingPath, oldLandingBytes),
                    originalMotion = Source(originalMotionPath, oldMotionBytes) },
                originalRecoveryFailure = oldMotion[^1], reusedTrials = reused, trials, executionError,
                acceptance = "45 flight + 45 recovery + 81 drag; long stability and live manual acceptance still required.",
            }, Options);
            report.SetLength(report.Position); report.Flush();
        }
        int? Record(LandingMotionTrial trial)
        {
            trials.Add(trial);
            Console.WriteLine($"size-damping/{trial.Kind}/{trial.Profile}/size={trial.Controls.SizePercent}/soft={trial.Controls.Softness}/build={trial.Controls.LandingBuild}: " +
                (trial.Passed ? "PASS" : $"FAIL {trial.Failure?.Kind} frame={trial.Failure?.Frame}: {trial.Failure?.Detail}"));
            Save();
            if (Complete(trial)) return null;
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
            var expected = LandingBuildMatrixVerification.AllControls().SelectMany(c => new[] { ("flight", c, (string?)null), ("recovery", c, (string?)null) })
                .Concat(LandingBuildMatrixVerification.AllControls().Where(c => c.LandingBuild is 0 or 50 or 100)
                    .SelectMany(c => new[] { ("drag", c, (string?)"changing"), ("drag", c, (string?)"horizontal"), ("drag", c, (string?)"ground-swing") })).ToHashSet();
            Require(trials.Count == 171 && trials.All(Complete) && trials.Select(t => (t.Kind, t.Controls, t.Profile)).ToHashSet().SetEquals(expected),
                "S2 motion matrix incomplete.");
            outcome = "size-damping-motion-passed-long-required";
            return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    }

    private static bool Complete(LandingMotionTrial trial) => LandingBuildStretchMatrixVerification.MotionComplete(trial, Table);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static EvidenceSource Source(string path, byte[] bytes) => new(Path.GetFileName(path), Hash(bytes));
    private static void Require(bool condition, string detail) => LandingBuildMatrixVerification.Require(condition, detail);
}
