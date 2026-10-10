using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dango.MaterialSample;

internal static class LandingBuildMotionMatrixVerification
{
    internal static int Run(string library, string landingPath, string output)
    {
        try { return RunCore(library, landingPath, output); }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }

    private static int RunCore(string library, string landingPath, string output)
    {
        var native = LandingBuildMatrixVerification.RequireNative(library);
        var landingBytes = File.ReadAllBytes(landingPath);
        _ = LandingBuildMatrixVerification.ImportLanding(landingBytes);
        var trials = new List<LandingMotionTrial>();
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
                schema = 1, kind = "landing-build-motion-matrix", completed, outcome,
                selectedTable = LandingBuildMapping.Version(LandingBuildMatrixVerification.SelectedTable), native,
                landingMatrix = new { fileName = Path.GetFileName(landingPath), sha256 = Convert.ToHexString(SHA256.HashData(landingBytes)) },
                frameStepTicks = MaterialSession.FixedStep.Ticks, requiredTrials = 171,
                trials, executionError,
                acceptance = "45 flight + 45 recovery + 27 controls x 3 drag profiles; long-horizon and manual acceptance remain required.",
            }, LandingBuildMatrixVerification.JsonOptions);
            report.SetLength(report.Position);
            report.Flush();
        }
        int? Record(LandingMotionTrial trial)
        {
            trials.Add(trial);
            Console.WriteLine($"{trial.Kind}/{trial.Profile}/size={trial.Controls.SizePercent}/soft={trial.Controls.Softness}/build={trial.Controls.LandingBuild}: " +
                (trial.Passed ? "PASS" : $"FAIL {trial.Failure?.Kind} frame={trial.Failure?.Frame}: {trial.Failure?.Detail}"));
            Save();
            if (trial.Passed && trial.ExecutionCompleted) return null;
            var incomplete = trial.Failure is null || trial.Failure.Kind == LandingFailureKind.Incomplete;
            outcome = incomplete ? "execution-incomplete" : "hard-failure-stop";
            return incomplete ? 2 : 1;
        }
        Save();
        try
        {
            foreach (var controls in LandingBuildMatrixVerification.AllControls())
            {
                var result = Record(LandingBuildMotionVerification.Flight(library, controls, LandingBuildMatrixVerification.SelectedTable));
                if (result.HasValue) return result.Value;
                result = Record(LandingBuildMotionVerification.Recovery(library, controls, LandingBuildMatrixVerification.SelectedTable));
                if (result.HasValue) return result.Value;
            }
            foreach (var controls in LandingBuildMatrixVerification.AllControls().Where(c => c.LandingBuild is 0 or 50 or 100))
            foreach (var profile in new[] { "changing", "horizontal", "ground-swing" })
            {
                var result = Record(LandingBuildMotionVerification.Drag(library, controls, LandingBuildMatrixVerification.SelectedTable, profile));
                if (result.HasValue) return result.Value;
            }
            LandingBuildMatrixVerification.Require(trials.Count == 171 && trials.All(t => t.Passed && t.ExecutionCompleted), "Motion matrix incomplete.");
            outcome = "motion-matrix-passed-long-validation-required";
            return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    }
}
