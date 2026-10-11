using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dango.MaterialSample;

// Separate candidate admission. Original report hashes remain explicitly lost.
internal static class LandingBuildReceiptHistoryVerification
{
    internal const LandingBuildTable Table = LandingBuildTable.SizeDampingPreview;
    internal const string NativeRevision = "receipt-history-v1";
    // v10 fixed-input, negative, source, actual-DLL and independent review gates.
    internal const string CandidateSha256 = "01E9AD9CCC19E98F60F2B3C5A3987E59F4113E2B88E05E4DED94EF9644BF37E4";
    private const string FormerFailureSha256 = "878B22EE3805587B587DE7912B7163FBF11F59924C366B1C236C342249FCB9D0";
    private const string FormerToolSha256 = "14AD82F63061A8B53D89EAD201E938F5EC14BE38EE947C4291AF341935A7FBFF";
    private const string FormerDependenciesSha256 = "7E2D80A01BB9E4F39B58484F0201B1D83A7EB90309C79B1063E1422AC7168692";
    private const string LandingSha256 = "78E99A9BC94081A0C5AB3141A1955EBBFA56A9F3674283B46BCAEFD4651ED9E5";
    private const string LandingToolSha256 = "7139746A3023C057C08C58D02CD9B29EFDB9618303D15B48B3AE4C4D3F74D4D1";
    private const string LandingDependenciesSha256 = "7E2D80A01BB9E4F39B58484F0201B1D83A7EB90309C79B1063E1422AC7168692";
    private const string OldBaselineSha256 = "212A4805E8F63E3E8FD1EC1A907226500062AE03E108E1ABCF13158C0468321B";
    private const string OldBaselineToolSha256 = "C2FA0A1BA0BA376ECC5A7438029D08ABC261CAF30A72E47D6758A85A1674286E";
    private const string OldBaselineDependenciesSha256 = "5872C3054AD540523214D6EC95FC7485016615FF5C2C71C9950EDD0BCF4F7573";
    private const string LostOriginalLandingSha256 = "DC6C0AF2D85CC41F3133A4F82234EDF44990DFCF9B08DA6DD1332E2AEB3D28BC";
    private const string LostOriginalMotionSha256 = "193FB00EC8790BD5B0515DE0D5922B43AFCFEA32EEA4D59065C89D12469C599F";
    private static readonly JsonSerializerOptions Options = LandingBuildMatrixVerification.JsonOptions;
    private sealed record EvidenceSource(string FileName, string Sha256);
    private sealed record ToolEvidence(string FileName, string Sha256, string DependencySetSha256, IReadOnlyDictionary<string, string> Dependencies);
    private sealed record ShapePreservation(double WidthPixelsDelta, double HeightPixelsDelta, bool Passed);

    internal static int RunFreshOldBaseline(string library, string output) => Guard(() =>
    {
        var native = LandingBuildMatrixVerification.RequireNative(library);
        output = PrepareOutput(output);
        using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var tool = Tool();
        var trials = new List<LandingTrial>();
        var effects = new List<LandingFailure>();
        LandingMotionTrial? recovery = null, former = null;
        var outcome = "running";
        var completed = false;
        string? executionError = null;
        void Save() => SaveReport(report, new
        {
            schema = 1, kind = "landing-build-receipt-history-fresh-old-baseline", candidate = LandingBuildMapping.Version(Table),
            completed, outcome, native, tool, frameStepTicks = MaterialSession.FixedStep.Ticks,
            frozenForProduction = false, freshRebaseline = true, requiredTrials = 45, reusedTrials = 0,
            lostOriginalLandingSha256 = LostOriginalLandingSha256, lostOriginalMotionSha256 = LostOriginalMotionSha256,
            trials, effectFailures = effects, recovery, formerFailedCase = former, executionError,
            acceptance = "Fresh old-native baseline only; expected old stop is a reproduced failure, not a physical pass or replacement of the lost original reports.",
        });
        Save();
        try
        {
            recovery = LandingBuildMotionVerification.Recovery(library, new(120, 100, 0), Table);
            Print(recovery); Save();
            if (!MotionComplete(recovery)) { outcome = StopOutcome(recovery.Failure); return FailureCode(recovery.Failure); }
            foreach (var size in new[] { 100d, 80d, 120d })
            {
                foreach (var controls in LandingBuildMatrixVerification.AllControls().Where(c => c.SizePercent == size))
                {
                    var trial = LandingBuildVerification.Land(library, controls, Table);
                    trials.Add(trial); Save();
                    Console.WriteLine($"fresh-old-baseline/landing/size={size}/soft={controls.Softness}/build={controls.LandingBuild}: " +
                        (trial.Failure is null ? "COMPLETE" : $"FAIL {trial.Failure}"));
                    if (!LandingBuildMatrixVerification.TrialComplete(trial))
                    { outcome = StopOutcome(trial.Failure); return FailureCode(trial.Failure); }
                }
                effects.AddRange(LandingBuildVerification.EvaluateEffects(trials.Where(t => t.Controls.SizePercent == size).ToArray(), size));
                Save();
                if (effects.Count != 0) { outcome = "effect-failure-stop"; return 1; }
            }
            former = LandingBuildMotionVerification.Drag(library, new(80, 50, 0), Table, "changing");
            Print(former); Save();
            if (!ExpectedOldStop(former)) { outcome = "old-failure-reproduction-mismatch"; return 1; }
            outcome = "fresh-old-baseline-reproduced"; return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    });

    internal static int RunFormerFailure(string library, string oldMotionPath, string output) => Guard(() =>
    {
        var native = RequireNative(library);
        var oldBytes = File.ReadAllBytes(oldMotionPath);
        using var oldDocument = ImportOldBaseline(oldBytes);
        var oldRoot = oldDocument.RootElement;
        var former = oldRoot.GetProperty("formerFailedCase").Deserialize<LandingMotionTrial>(Options)!;
        output = PrepareOutput(output);
        using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        LandingMotionTrial? trial = null;
        string? executionError = null;
        var outcome = "running";
        var completed = false;
        var tool = Tool();
        void Save() => SaveReport(report, new
        {
            schema = 1, kind = "landing-build-receipt-history-former-failure", nativeRevision = NativeRevision,
            candidate = LandingBuildMapping.Version(Table), completed, outcome, native, tool,
            frozenForProduction = false, frameStepTicks = MaterialSession.FixedStep.Ticks,
            oldBaseline = Source(oldMotionPath, oldBytes), formerFailedCase = former, trial, executionError,
            acceptance = "One former failure with release and renewed-control gates; the complete matrices remain required.",
        });
        Save();
        try
        {
            trial = LandingBuildMotionVerification.Drag(library, former.Controls, Table, "changing");
            Print(trial);
            var code = MotionComplete(trial) ? 0 : FailureCode(trial.Failure);
            outcome = code == 0 ? "former-failure-passed-s1-required" : code == 1 ? "hard-failure-stop" : "execution-incomplete";
            return code;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    });

    internal static int RunLanding(string library, string formerPath, string oldLandingPath, string output) => Guard(() =>
    {
        var native = RequireNative(library);
        var formerBytes = File.ReadAllBytes(formerPath);
        using var formerDocument = ImportStage(formerBytes, "former-failure", "former-failure-passed-s1-required", FormerFailureSha256, FormerToolSha256, FormerDependenciesSha256);
        Require(formerDocument.RootElement.GetProperty("oldBaseline").GetProperty("Sha256").GetString() == OldBaselineSha256,
            "Former-failure source does not bind the fresh old-native baseline.");
        var former = formerDocument.RootElement.GetProperty("trial").Deserialize<LandingMotionTrial>(Options)!;
        Require(MotionComplete(former) && former.Kind == "drag" && former.Profile == "changing" &&
            former.Controls == new LandingBuildControls(80, 50, 0), "Former-failure gate is incomplete.");
        var oldBytes = File.ReadAllBytes(oldLandingPath);
        using var oldDocument = ImportOldBaseline(oldBytes);
        var oldTrialsByControls = FrozenConfigurations(oldDocument.RootElement);
        var oldFat = oldTrialsByControls[new LandingBuildControls(100, 50, 0)];
        Require(LandingBuildMatrixVerification.TrialComplete(oldFat) && oldFat.Configuration == oldFat.Controls.Configure(Table),
            "Approved default fat reference is incomplete or changed.");
        output = PrepareOutput(output);
        using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var trials = new List<LandingTrial>();
        var effects = new List<LandingFailure>();
        LandingMotionTrial? recovery = null;
        ShapePreservation? shape = null;
        var outcome = "running";
        var completed = false;
        string? executionError = null;
        var tool = Tool();
        void Save() => SaveReport(report, new
        {
            schema = 1, kind = "landing-build-receipt-history-landing", nativeRevision = NativeRevision,
            candidate = LandingBuildMapping.Version(Table), completed, outcome, native, tool,
            frozenForProduction = false, frameStepTicks = MaterialSession.FixedStep.Ticks,
            sources = new { formerFailure = Source(formerPath, formerBytes), oldBaseline = Source(oldLandingPath, oldBytes) },
            requiredTrials = 45, reusedTrials = 0, trials, effectFailures = effects, recovery,
            defaultFatShape = shape, executionError,
            acceptance = "Fresh S1 only; complete motion, long stability and manual acceptance remain required.",
        });
        Save();
        try
        {
            recovery = LandingBuildMotionVerification.Recovery(library, new(120, 100, 0), Table);
            Print(recovery); Save();
            if (!MotionComplete(recovery)) { outcome = StopOutcome(recovery.Failure); return FailureCode(recovery.Failure); }
            foreach (var size in new[] { 100d, 80d, 120d })
            {
                foreach (var controls in LandingBuildMatrixVerification.AllControls().Where(c => c.SizePercent == size))
                {
                    var trial = LandingBuildVerification.Land(library, controls, Table);
                    trials.Add(trial);
                    Require(trial.Configuration == oldTrialsByControls[controls].Configuration,
                        "Fresh landing physical configuration differs from the frozen approved configuration.");
                    Console.WriteLine($"receipt-history/landing/size={size}/soft={controls.Softness}/build={controls.LandingBuild}: " +
                        (trial.Failure is null ? "COMPLETE" : $"FAIL {trial.Failure}"));
                    Save();
                    if (!LandingBuildMatrixVerification.TrialComplete(trial))
                    { outcome = StopOutcome(trial.Failure); return FailureCode(trial.Failure); }
                    if (controls == oldFat.Controls)
                    {
                        var oldBounds = SampleGeometry.Create(oldFat.LastHealthyFrame!, scale: 190, bodySize: 1).Bounds;
                        var newBounds = SampleGeometry.Create(trial.LastHealthyFrame!, scale: 190, bodySize: 1).Bounds;
                        var dw = newBounds.Width - oldBounds.Width;
                        var dh = newBounds.Height - oldBounds.Height;
                        shape = new(dw, dh, Math.Abs(dw) <= 1 && Math.Abs(dh) <= 1);
                        Save();
                        if (!shape.Passed) { outcome = "approved-shape-change-stop"; return 1; }
                    }
                }
                effects.AddRange(LandingBuildVerification.EvaluateEffects(trials.Where(t => t.Controls.SizePercent == size).ToArray(), size));
                Save();
                if (effects.Count != 0) { outcome = "effect-failure-stop"; return 1; }
            }
            Require(trials.Count == 45 && trials.Select(t => t.Controls).ToHashSet().SetEquals(LandingBuildMatrixVerification.AllControls()) &&
                trials.All(t => LandingBuildMatrixVerification.TrialComplete(t) && t.Configuration == t.Controls.Configure(Table)) && shape?.Passed == true,
                "Fresh landing set or default shape gate incomplete.");
            outcome = "landing-passed-s2-required"; return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    });

    internal static int RunMotion(string library, string landingPath, string oldLandingPath, string output) => Guard(() =>
    {
        var native = RequireNative(library);
        var bytes = File.ReadAllBytes(landingPath);
        using var document = ImportStage(bytes, "landing", "landing-passed-s2-required", LandingSha256, LandingToolSha256, LandingDependenciesSha256);
        var root = document.RootElement;
        var landing = root.GetProperty("trials").Deserialize<LandingTrial[]>(Options)!;
        Require(root.GetProperty("requiredTrials").GetInt32() == 45 && root.GetProperty("reusedTrials").GetInt32() == 0 &&
            root.GetProperty("effectFailures").GetArrayLength() == 0 && root.GetProperty("executionError").ValueKind == JsonValueKind.Null &&
            landing.Length == 45 && landing.Select(t => t.Controls).ToHashSet().SetEquals(LandingBuildMatrixVerification.AllControls()) &&
            landing.All(t => LandingBuildMatrixVerification.TrialComplete(t) && t.MappingVersion == LandingBuildMapping.Version(Table) &&
                t.Configuration == t.Controls.Configure(Table)), "S1 trial set incomplete or mapping differs.");
        foreach (var size in new[] { 80d, 100d, 120d })
            Require(LandingBuildVerification.EvaluateEffects(landing.Where(t => t.Controls.SizePercent == size).ToArray(), size).Count == 0,
                "S1 measured effect recomputation failed.");
        var shape = root.GetProperty("defaultFatShape").Deserialize<ShapePreservation>(Options)!;
        Require(shape.Passed && double.IsFinite(shape.WidthPixelsDelta) && double.IsFinite(shape.HeightPixelsDelta) &&
            Math.Abs(shape.WidthPixelsDelta) <= 1 && Math.Abs(shape.HeightPixelsDelta) <= 1, "Default fat shape gate failed.");
        var oldBytes = File.ReadAllBytes(oldLandingPath);
        Require(Hash(oldBytes) == OldBaselineSha256 &&
            root.GetProperty("sources").GetProperty("oldBaseline").GetProperty("Sha256").GetString() == OldBaselineSha256 &&
            root.GetProperty("sources").GetProperty("formerFailure").GetProperty("Sha256").GetString() == FormerFailureSha256,
            "S1 source fingerprints differ from the independently reviewed sources.");
        using var oldDocument = ImportOldBaseline(oldBytes);
        var oldTrialsByControls = FrozenConfigurations(oldDocument.RootElement);
        Require(landing.All(t => t.Configuration == oldTrialsByControls[t.Controls].Configuration),
            "S1 physical configurations differ from the frozen approved configurations.");
        var fatControls = new LandingBuildControls(100, 50, 0);
        var oldFat = oldTrialsByControls[fatControls];
        var newFat = landing.Single(t => t.Controls == fatControls);
        Require(LandingBuildMatrixVerification.TrialComplete(oldFat) && oldFat.Configuration == newFat.Configuration,
            "Approved fat shape reference or configuration differs.");
        var oldBounds = SampleGeometry.Create(oldFat.LastHealthyFrame!, scale: 190, bodySize: 1).Bounds;
        var newBounds = SampleGeometry.Create(newFat.LastHealthyFrame!, scale: 190, bodySize: 1).Bounds;
        Require(shape.WidthPixelsDelta == newBounds.Width - oldBounds.Width &&
            shape.HeightPixelsDelta == newBounds.Height - oldBounds.Height,
            "S1 fat shape deltas differ from independent drawing recomputation.");
        var recovery = root.GetProperty("recovery").Deserialize<LandingMotionTrial>(Options)!;
        Require(MotionComplete(recovery) && recovery.Kind == "recovery" && recovery.Controls == new LandingBuildControls(120, 100, 0),
            "Original recovery corner gate incomplete.");
        output = PrepareOutput(output);
        using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var trials = new List<LandingMotionTrial>();
        var outcome = "running";
        var completed = false;
        string? executionError = null;
        var tool = Tool();
        void Save() => SaveReport(report, new
        {
            schema = 1, kind = "landing-build-receipt-history-motion", nativeRevision = NativeRevision,
            candidate = LandingBuildMapping.Version(Table), completed, outcome, native, tool,
            frozenForProduction = false, frameStepTicks = MaterialSession.FixedStep.Ticks,
            landing = Source(landingPath, bytes), oldBaseline = Source(oldLandingPath, oldBytes),
            requiredTrials = 171, reusedTrials = 0, trials, executionError,
            acceptance = "Fresh 45 flight + 45 recovery + 81 drag; long stability and manual acceptance remain required.",
        });
        int? Record(LandingMotionTrial trial)
        {
            trials.Add(trial); Print(trial); Save();
            if (MotionComplete(trial)) return null;
            outcome = StopOutcome(trial.Failure); return FailureCode(trial.Failure);
        }
        Save();
        try
        {
            foreach (var controls in LandingBuildMatrixVerification.AllControls())
            {
                var code = Record(LandingBuildMotionVerification.Flight(library, controls, Table));
                if (code.HasValue) return code.Value;
                code = Record(LandingBuildMotionVerification.Recovery(library, controls, Table));
                if (code.HasValue) return code.Value;
            }
            foreach (var controls in LandingBuildMatrixVerification.AllControls().Where(c => c.LandingBuild is 0 or 50 or 100))
            foreach (var profile in new[] { "changing", "horizontal", "ground-swing" })
            {
                var code = Record(LandingBuildMotionVerification.Drag(library, controls, Table, profile));
                if (code.HasValue) return code.Value;
            }
            Require(trials.Count == 171 && trials.Select(t => (t.Kind, t.Profile, t.Controls)).Distinct().Count() == 171 &&
                trials.All(MotionComplete), "Fresh motion trial set incomplete or duplicated.");
            outcome = "motion-passed-s3-required"; return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    });

    private static JsonDocument ImportStage(byte[] bytes, string stage, string outcome, string expectedSha256, string expectedToolSha256, string expectedDependenciesSha256)
    {
        Require(Hash(bytes) == expectedSha256, "Previous stage differs from the independently reviewed exact report.");
        var document = JsonDocument.Parse(bytes);
        try
        {
            var root = document.RootElement;
            Require(root.GetProperty("schema").GetInt32() == 1 &&
                root.GetProperty("kind").GetString() == $"landing-build-receipt-history-{stage}" &&
                root.GetProperty("nativeRevision").GetString() == NativeRevision &&
                root.GetProperty("candidate").GetString() == LandingBuildMapping.Version(Table) &&
                root.GetProperty("completed").GetBoolean() && root.GetProperty("outcome").GetString() == outcome &&
                root.GetProperty("executionError").ValueKind == JsonValueKind.Null &&
                !root.GetProperty("frozenForProduction").GetBoolean() &&
                root.GetProperty("native").GetProperty("Sha256").GetString() == CandidateSha256 &&
                root.GetProperty("tool").GetProperty("Sha256").GetString() == expectedToolSha256 &&
                root.GetProperty("tool").GetProperty("DependencySetSha256").GetString() == expectedDependenciesSha256 &&
                root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks,
                "Previous stage metadata, candidate DLL, actual tool or fixed step differs.");
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    private static MaterialLibrarySource RequireNative(string library)
    {
        var native = MaterialLibraries.Describe(library, null).Candidate;
        Require(native.Sha256 == CandidateSha256, "Reviewed receipt-history candidate DLL hash mismatch.");
        return native;
    }
    private static bool ExpectedOldStop(LandingMotionTrial trial) =>
        trial.Controls == new LandingBuildControls(80, 50, 0) && trial.MappingVersion == LandingBuildMapping.Version(Table) &&
        trial.Configuration == trial.Controls.Configure(Table) && trial.Kind == "drag" && trial.Profile == "changing" &&
        !trial.Passed && !trial.ExecutionCompleted && trial.RequestedFrames == 6660 && trial.AttemptedFrame == 5865 &&
        trial.CompletedFrames == 5864 && trial.RenderedFrames == 5864 && trial.SessionReportedFrames == 5865 &&
        trial.LastHealthyFrameNumber == 5864 && trial.LastHealthyFrame is not null && trial.Failure?.Kind == LandingFailureKind.HealthOrStop &&
        trial.Failure.Frame == 5865 && IsExpectedOldError(trial.Failure.Detail);
    internal static bool IsExpectedOldError(string detail) => detail.Split(new[] { '\r', '\n' }, StringSplitOptions.None)[0] ==
        "System.InvalidOperationException: 原生物理停止（-96），保留最后合法帧。请显式重新下落。";
    internal static int VerifyOldErrorGate()
    {
        var prefix = "System.InvalidOperationException: 原生物理停止（";
        var suffix = "），保留最后合法帧。请显式重新下落。";
        var cases = new[]
        {
            (prefix + "-96" + suffix, true), (prefix + "-96" + suffix + "\r\n   at MaterialSession.Step", true),
            (prefix + "-960" + suffix, false), (prefix + "-961" + suffix, false),
            ("Other error mentioning -96", false), ("Other error\r\n" + prefix + "-96" + suffix, false),
        };
        var passed = cases.All(test => IsExpectedOldError(test.Item1) == test.Item2);
        Console.WriteLine($"Exact old error gate: {cases.Length} cases, passed={passed}.");
        return passed ? 0 : 1;
    }
    private static JsonDocument ImportOldBaseline(byte[] bytes)
    {
        Require(Hash(bytes) == OldBaselineSha256, "Fresh old-native baseline differs from its reviewed exact report.");
        var document = JsonDocument.Parse(bytes);
        try
        {
            var root = document.RootElement;
            Require(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("kind").GetString() == "landing-build-receipt-history-fresh-old-baseline" &&
                root.GetProperty("candidate").GetString() == LandingBuildMapping.Version(Table) && root.GetProperty("completed").GetBoolean() &&
                root.GetProperty("outcome").GetString() == "fresh-old-baseline-reproduced" && root.GetProperty("freshRebaseline").GetBoolean() &&
                !root.GetProperty("frozenForProduction").GetBoolean() && root.GetProperty("requiredTrials").GetInt32() == 45 &&
                root.GetProperty("reusedTrials").GetInt32() == 0 && root.GetProperty("effectFailures").GetArrayLength() == 0 &&
                root.GetProperty("executionError").ValueKind == JsonValueKind.Null && root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks &&
                root.GetProperty("native").GetProperty("Sha256").GetString() == LandingBuildMapping.ApprovedNativeSha256 &&
                root.GetProperty("tool").GetProperty("Sha256").GetString() == OldBaselineToolSha256 &&
                root.GetProperty("tool").GetProperty("DependencySetSha256").GetString() == OldBaselineDependenciesSha256 &&
                root.GetProperty("lostOriginalLandingSha256").GetString() == LostOriginalLandingSha256 &&
                root.GetProperty("lostOriginalMotionSha256").GetString() == LostOriginalMotionSha256,
                "Fresh old-native baseline metadata, source, tool or fixed step differs.");
            var old = FrozenConfigurations(root);
            Require(old.Values.All(t => t.MappingVersion == LandingBuildMapping.Version(Table)), "Fresh baseline contains reused old trials.");
            foreach (var size in new[] { 80d, 100d, 120d })
                Require(LandingBuildVerification.EvaluateEffects(old.Values.Where(t => t.Controls.SizePercent == size).ToArray(), size).Count == 0,
                    "Fresh baseline effects recomputation failed.");
            var recovery = root.GetProperty("recovery").Deserialize<LandingMotionTrial>(Options)!;
            Require(MotionComplete(recovery) && recovery.Kind == "recovery" && recovery.Controls == new LandingBuildControls(120, 100, 0),
                "Fresh old-native recovery corner incomplete.");
            Require(ExpectedOldStop(root.GetProperty("formerFailedCase").Deserialize<LandingMotionTrial>(Options)!), "Expected old-native failure differs.");
            return document;
        }
        catch { document.Dispose(); throw; }
    }
    private static Dictionary<LandingBuildControls, LandingTrial> FrozenConfigurations(JsonElement root)
    {
        var trials = root.GetProperty("trials").Deserialize<LandingTrial[]>(Options)!;
        Require(trials.Length == 45 && trials.Select(t => t.Controls).ToHashSet().SetEquals(LandingBuildMatrixVerification.AllControls()) &&
            trials.All(t => LandingBuildMatrixVerification.TrialComplete(t) && t.Configuration == t.Controls.Configure(Table)),
            "Approved frozen physical configurations or trial set differ.");
        return trials.ToDictionary(t => t.Controls);
    }
    private static bool MotionComplete(LandingMotionTrial trial) => trial.MappingVersion == LandingBuildMapping.Version(Table) &&
        LandingBuildStretchMatrixVerification.MotionComplete(trial, Table);
    private static int FailureCode(LandingFailure? failure) => failure is null || failure.Kind == LandingFailureKind.Incomplete ? 2 : 1;
    private static string StopOutcome(LandingFailure? failure) => FailureCode(failure) == 1 ? "hard-failure-stop" : "execution-incomplete";
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static EvidenceSource Source(string path, byte[] bytes) => new(Path.GetFileName(path), Hash(bytes));
    private static ToolEvidence Tool()
    {
        var path = typeof(LandingBuildReceiptHistoryVerification).Assembly.Location;
        var dependencies = new[] { "Dango.Core", "Dango.Desktop", "Dango.Physics", "Dango.Protocol" }
            .ToDictionary(name => name + ".dll", name => Hash(File.ReadAllBytes(System.Reflection.Assembly.Load(name).Location)));
        var dependencyBytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", dependencies.Select(pair => $"{pair.Key}={pair.Value}")));
        return new(Path.GetFileName(path), Hash(File.ReadAllBytes(path)), Hash(dependencyBytes), dependencies);
    }
    private static string PrepareOutput(string path)
    {
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }
    private static void SaveReport(FileStream report, object value)
    {
        report.Position = 0; JsonSerializer.Serialize(report, value, Options); report.SetLength(report.Position); report.Flush();
    }
    private static int Guard(Func<int> run)
    { try { return run(); } catch (Exception error) { Console.Error.WriteLine(error); return 2; } }
    private static void Require(bool condition, string detail) => LandingBuildMatrixVerification.Require(condition, detail);
    private static void Print(LandingMotionTrial trial) => Console.WriteLine(
        $"receipt-history/{trial.Kind}/{trial.Profile}/size={trial.Controls.SizePercent}/soft={trial.Controls.Softness}/build={trial.Controls.LandingBuild}: " +
        (trial.Passed ? "PASS" : $"FAIL {trial.Failure}"));
}
