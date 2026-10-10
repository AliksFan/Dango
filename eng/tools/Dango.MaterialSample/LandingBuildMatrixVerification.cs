using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dango.MaterialSample;

// The selected initial-screen table is frozen. A later failure never selects another table.
internal static class LandingBuildMatrixVerification
{
    internal const LandingBuildTable SelectedTable = LandingBuildTable.B;
    private static readonly double[] Softnesses = [0, 50, 100];
    private static readonly double[] Builds = [0, 25, 50, 75, 100];
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static int RunLanding(string library, string screenPath, string output)
    {
        try { return RunLandingCore(library, screenPath, output); }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }

    private static int RunLandingCore(string library, string screenPath, string output)
    {
        var native = RequireNative(library);
        var screenBytes = File.ReadAllBytes(screenPath);
        var trials = ImportScreen(screenBytes);
        var reusedTrials = trials.Count;
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
                schema = 1, kind = "landing-build-complete-landing-matrix", completed, outcome,
                selectedTable = LandingBuildMapping.Version(SelectedTable), native,
                initialScreen = new { fileName = Path.GetFileName(screenPath), sha256 = Convert.ToHexString(SHA256.HashData(screenBytes)), reusedTrials },
                frameStepTicks = MaterialSession.FixedStep.Ticks,
                requiredTrials = 45, trials, effectFailures, executionError,
                acceptance = "Landing matrix only; flight/recovery/drag/long-horizon and manual acceptance remain required.",
            }, JsonOptions);
            report.SetLength(report.Position);
            report.Flush();
        }
        Save();
        try
        {
            effectFailures.AddRange(LandingBuildVerification.EvaluateEffects(trials, 100));
            if (effectFailures.Count != 0) { outcome = "imported-effect-failure-stop"; return 1; }
            foreach (var size in new[] { 80d, 120d })
            {
                foreach (var softness in Softnesses)
                foreach (var build in Builds)
                {
                    var trial = LandingBuildVerification.Land(library, new(size, softness, build), SelectedTable);
                    trials.Add(trial);
                    Console.WriteLine($"B/size={size}/soft={softness}/build={build}: " +
                        (trial.Failure is null ? "COMPLETE" : $"FAIL {trial.Failure.Kind} frame={trial.Failure.Frame}: {trial.Failure.Detail}"));
                    Save();
                    if (trial.Failure is not null)
                    {
                        outcome = trial.Failure.Kind == LandingFailureKind.Incomplete ? "execution-incomplete" : "hard-failure-stop";
                        return trial.Failure.Kind == LandingFailureKind.Incomplete ? 2 : 1;
                    }
                    if (!TrialComplete(trial)) throw new InvalidOperationException("Trial ended without all required samples.");
                }
                effectFailures.AddRange(LandingBuildVerification.EvaluateEffects(trials.Where(t => t.Controls.SizePercent == size).ToArray(), size));
                Save();
                if (effectFailures.Count != 0) { outcome = "effect-failure-stop"; return 1; }
            }
            if (trials.Count != 45 || trials.Any(t => !TrialComplete(t)))
                throw new InvalidOperationException("Landing matrix is incomplete.");
            outcome = "landing-matrix-passed-motion-and-long-validation-required";
            return 0;
        }
        catch (Exception error)
        {
            executionError = error.ToString();
            outcome = "execution-incomplete";
            return 2;
        }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    }

    internal static MaterialLibrarySource RequireNative(string library)
    {
        var native = MaterialLibraries.Describe(library, null).Candidate;
        Require(native.Sha256 == LandingBuildMapping.ApprovedNativeSha256, "Approved final DLL hash mismatch.");
        return native;
    }

    private static List<LandingTrial> ImportScreen(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Require(root.GetProperty("schema").GetInt32() == 1 &&
            root.GetProperty("kind").GetString() == "landing-build-finite-initial-screen" &&
            root.GetProperty("completed").GetBoolean() &&
            root.GetProperty("outcome").GetString() == "initial-screen-passed-full-validation-required" &&
            root.GetProperty("selectedTable").GetString() == LandingBuildMapping.Version(SelectedTable),
            "The initial screen did not complete and select the frozen B table.");
        Require(root.GetProperty("native").GetProperty("Sha256").GetString() == LandingBuildMapping.ApprovedNativeSha256 &&
            root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks,
            "The initial-screen DLL or fixed step differs from the approved baseline.");
        var checks = root.GetProperty("configurationChecks").EnumerateArray().ToArray();
        Require(checks.Length == 8 && checks.All(c => c.GetProperty("Passed").GetBoolean()), "Initial-screen configuration checks incomplete.");
        var tables = root.GetProperty("tables").Deserialize<LandingTableReport[]>(JsonOptions)
            ?? throw new InvalidOperationException("No table reports.");
        Require(tables.Length == 2 && tables[0].MappingVersion == LandingBuildMapping.Version(LandingBuildTable.A) &&
            tables[0].AllTrialsCompleted && !tables[0].HardFailure && !tables[0].Passed &&
            tables[0].EffectFailures.Count > 0 && tables[0].EffectFailures.All(f => f.Kind == LandingFailureKind.EffectInsufficient),
            "Screen did not follow the approved finite A-to-B calibration order.");
        var selected = tables[1];
        Require(selected.MappingVersion == LandingBuildMapping.Version(SelectedTable) && selected.AllTrialsCompleted &&
            !selected.HardFailure && selected.Passed && selected.EffectFailures.Count == 0 && selected.Trials.Count == 15,
            "Selected table report did not pass.");
        var expected = Softnesses.SelectMany(s => Builds.Select(b => new LandingBuildControls(100, s, b))).ToHashSet();
        Require(selected.Trials.Select(t => t.Controls).ToHashSet().SetEquals(expected) &&
            selected.Trials.All(t => TrialComplete(t) && t.MappingVersion == LandingBuildMapping.Version(SelectedTable) &&
                t.Configuration == t.Controls.Configure(SelectedTable)), "Imported trials or configurations differ from the required screen.");
        Require(LandingBuildVerification.EvaluateEffects(selected.Trials, 100).Count == 0, "Imported measured effects failed recomputation.");
        return selected.Trials.ToList();
    }

    internal static bool TrialComplete(LandingTrial trial) => trial.Failure is null &&
        trial.CompletedFrames == 3600 && trial.AttemptedFrame == 3600 && trial.RenderedFrames == 3600 &&
        trial.ShortGateSamples == 120 && trial.MeanSamples == 600 && trial.ShortGateDrift < .003 &&
        trial.Mean is not null && trial.LastHealthyFrame is not null &&
        trial.Checkpoints.Select(c => c.Frame).SequenceEqual(new[] { 600, 1800, 3600 });

    internal static IReadOnlyList<LandingTrial> ImportLanding(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Require(root.GetProperty("schema").GetInt32() == 1 &&
            root.GetProperty("kind").GetString() == "landing-build-complete-landing-matrix" &&
            root.GetProperty("completed").GetBoolean() &&
            root.GetProperty("outcome").GetString() == "landing-matrix-passed-motion-and-long-validation-required" &&
            root.GetProperty("selectedTable").GetString() == LandingBuildMapping.Version(SelectedTable) &&
            root.GetProperty("native").GetProperty("Sha256").GetString() == LandingBuildMapping.ApprovedNativeSha256 &&
            root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks &&
            root.GetProperty("requiredTrials").GetInt32() == 45 &&
            root.GetProperty("effectFailures").GetArrayLength() == 0, "Landing report is not a completed approved matrix.");
        var trials = root.GetProperty("trials").Deserialize<LandingTrial[]>(JsonOptions)
            ?? throw new InvalidOperationException("No landing trials.");
        var expected = AllControls().ToHashSet();
        Require(trials.Length == 45 && trials.Select(t => t.Controls).ToHashSet().SetEquals(expected) &&
            trials.All(t => TrialComplete(t) && t.MappingVersion == LandingBuildMapping.Version(SelectedTable) &&
                t.Configuration == t.Controls.Configure(SelectedTable)), "Landing trial set or current mapping differs.");
        foreach (var size in new[] { 80d, 100d, 120d })
            Require(LandingBuildVerification.EvaluateEffects(trials.Where(t => t.Controls.SizePercent == size).ToArray(), size).Count == 0,
                "Imported landing effects failed independent recomputation.");
        return trials;
    }

    internal static IEnumerable<LandingBuildControls> AllControls()
    {
        foreach (var size in new[] { 80d, 100d, 120d })
        foreach (var softness in Softnesses)
        foreach (var build in Builds)
            yield return new(size, softness, build);
    }

    internal static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }
}
