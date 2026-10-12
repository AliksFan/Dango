using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dango.MaterialSample;

// Separate touching-receipt admission. Four former failures and all physical trials are fresh.
internal static class LandingBuildTouchingReceiptVerification
{
    internal const LandingBuildTable Table = LandingBuildTable.SizeDampingPreview;
    internal const string NativeRevision = "touching-receipt-v1";
    // Each pin remains closed until its actual native or managed stage has passed independent review.
    internal const string CandidateSha256 = "4388F63610C01FA5413EDA20829EA9E6F33AC93EC36A6793DCE4DCB0E4EAF757";
    private const string FormerFailureSha256 = "642B7FC2D951DE37FEE8A34C9B9BB0B5861B91EDF15E0D6D9D4A3B643B68D218";
    private const string FormerToolSha256 = "54061CA8C0824AE24D7D832E620FFCAC9E53F7A06A2AB081A16301F10A38D3E6";
    private const string FormerDependenciesSha256 = "5EC433EC6876B239D90D3A69B1472D112663FC6F5A88DA389F18C0935D7058C8";
    private const string LandingSha256 = "260E3350F4386019D7A2A502E5B448433C3D4375CAE0B448201ECF1F809C1DA2";
    private const string LandingToolSha256 = "AB9CF51957907B61B28AC2673DB8E0E9FEB16B65B30EAE66A4620962F303653F";
    private const string LandingDependenciesSha256 = "BD1B03183480914BC8589EA4B55ACA3305679EB1E08E780C79B2FF9BF9B132D5";
    private const string MotionSha256 = "7819655A0EB60267798C1F80176C72A637447992ACEA0A1EC384A79E51922843";
    private const string MotionToolSha256 = "11798494610FE1241B6C5F644DDBD027E15F5F3F9A6192B0FA085BC1150C3777";
    private const string MotionDependenciesSha256 = "BD1B03183480914BC8589EA4B55ACA3305679EB1E08E780C79B2FF9BF9B132D5";
    private const string OldBaselineSha256 = "212A4805E8F63E3E8FD1EC1A907226500062AE03E108E1ABCF13158C0468321B";
    private const string OldBaselineToolSha256 = "C2FA0A1BA0BA376ECC5A7438029D08ABC261CAF30A72E47D6758A85A1674286E";
    private const string OldBaselineDependenciesSha256 = "5872C3054AD540523214D6EC95FC7485016615FF5C2C71C9950EDD0BCF4F7573";
    private const string SecondFailureSha256 = "252FC720633F1A73500979387521A4A17886020BC90591521FC01276F45B9C06";
    private const string SecondFailureToolSha256 = "90250276F7BF786DB73284C4D1E46604D16825B9F4D0A747EB886B388CAEBA91";
    private const string SecondFailureDependenciesSha256 = "7E2D80A01BB9E4F39B58484F0201B1D83A7EB90309C79B1063E1422AC7168692";
    private const string ReceiptHistorySha256 = "01E9AD9CCC19E98F60F2B3C5A3987E59F4113E2B88E05E4DED94EF9644BF37E4";
    private const string LostOriginalLandingSha256 = "DC6C0AF2D85CC41F3133A4F82234EDF44990DFCF9B08DA6DD1332E2AEB3D28BC";
    private const string LostOriginalMotionSha256 = "193FB00EC8790BD5B0515DE0D5922B43AFCFEA32EEA4D59065C89D12469C599F";
    private const string ThirdFailureSha256 = "DFCF1B672429CC6239AC5C58DD5A3B4FEA3DCFDBF7645B04E56F8BDF4F960DCB";
    private const string ThirdFailureToolSha256 = "5DE0E09BD9565C60009C2E56619FAC7F0AFB768131E2DC59E98A25949D64235A";
    private const string ThirdFailureDependenciesSha256 = "715F428B704769665BC09594FC5B9F74A89A83BA4E25F457A3DBDD8952B74EEB";
    private const string EndpointSelectionSha256 = "D70D1348C76F27E4EB8A122C771DA68D41A446EF71E1F32C0BB4B51E9CA6FBC0";
    private const string EndpointLandingSha256 = "5AB7B9921B9A312960F40A47985E902033915E9F9E0F31E7BC9E83618A1E699E";
    private const string FourthFailureSha256 = "4401D27891371B39098B2C4AD68D61B675F03794CBA751B8028B2A5E14814F5F";
    private const string FourthFailureToolSha256 = "CD7C6A6C1F15D69714F7C064509308934C97256A51F36158945844E2D1127E09";
    private const string FourthFailureDependenciesSha256 = "763C2142EB35FA6A863D7501E636F8599A586E5044CCC0EB4F01A84A08B22D02";
    private const string SingletonSha256 = "D6B82B9E6DBCC6CE0810330B08453DD37FAC2688AF0B98355ABF6E275CEEF0FE";
    private const string SingletonLandingSha256 = "5327D45465148618F12504A1C284A95804F1603CB1020BE45743491745A0FD67";
    private static readonly JsonSerializerOptions Options = LandingBuildMatrixVerification.JsonOptions;
    private sealed record EvidenceSource(string FileName, string Sha256);
    private sealed record ToolEvidence(string FileName, string Sha256, string DependencySetSha256, IReadOnlyDictionary<string, string> Dependencies);
    private sealed record ShapePreservation(double WidthPixelsDelta, double HeightPixelsDelta, bool Passed);

    internal static int RunFormerFailures(string library, string oldBaselinePath, string secondFailurePath, string thirdFailurePath, string fourthFailurePath, string output) => Guard(() =>
    {
        var native = RequireNative(library);
        var oldBytes = File.ReadAllBytes(oldBaselinePath);
        using var oldDocument = ImportOldBaseline(oldBytes);
        var oldRoot = oldDocument.RootElement;
        var frozen = FrozenConfigurations(oldRoot);
        var secondBytes = File.ReadAllBytes(secondFailurePath);
        using var secondDocument = ImportSecondFailure(secondBytes);
        var thirdBytes = File.ReadAllBytes(thirdFailurePath);
        using var thirdDocument = ImportThirdFailure(thirdBytes);
        var fourthBytes = File.ReadAllBytes(fourthFailurePath);
        using var fourthDocument = ImportFourthFailure(fourthBytes);
        var former = new[]
        {
            oldRoot.GetProperty("formerFailedCase").Deserialize<LandingMotionTrial>(Options)!,
            secondDocument.RootElement.GetProperty("trials")[107].Deserialize<LandingMotionTrial>(Options)!,
            thirdDocument.RootElement.GetProperty("trials")[143].Deserialize<LandingMotionTrial>(Options)!,
            fourthDocument.RootElement.GetProperty("trials")[162].Deserialize<LandingMotionTrial>(Options)!,
        };
        Require(former.All(t => t.Configuration == frozen[t.Controls].Configuration), "Former failed configuration differs from the approved frozen configuration.");
        output = PrepareOutput(output);
        using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var trials = new List<LandingMotionTrial>();
        string? executionError = null;
        var outcome = "running";
        var completed = false;
        var tool = Tool();
        void Save() => SaveReport(report, new
        {
            schema = 1, kind = "landing-build-touching-receipt-former-failures", nativeRevision = NativeRevision,
            candidate = LandingBuildMapping.Version(Table), completed, outcome, native, tool,
            frozenForProduction = false, frameStepTicks = MaterialSession.FixedStep.Ticks,
            sources = new { oldBaseline = Source(oldBaselinePath, oldBytes), secondFailure = Source(secondFailurePath, secondBytes), thirdFailure = Source(thirdFailurePath, thirdBytes), fourthFailure = Source(fourthFailurePath, fourthBytes) },
            formerFailedCases = former, requiredTrials = 4, reusedTrials = 0, trials, executionError,
            acceptance = "Four fresh former failures with release and renewed-control gates; the complete matrices remain required.",
        });
        Save();
        try
        {
            foreach (var failed in former)
            {
                var trial = LandingBuildMotionVerification.Drag(library, failed.Controls, Table, failed.Profile!);
                trials.Add(trial); Print(trial); Save();
                Require(trial.Configuration == frozen[trial.Controls].Configuration, "Fresh former configuration differs from the frozen reference.");
                if (!MotionComplete(trial)) { outcome = StopOutcome(trial.Failure); return FailureCode(trial.Failure); }
            }
            outcome = "former-failures-passed-s1-required"; return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    });

    internal static int RunLanding(string library, string formerPath, string oldLandingPath, string output) => Guard(() =>
    {
        var native = RequireNative(library);
        var formerBytes = File.ReadAllBytes(formerPath);
        using var formerDocument = ImportStage(formerBytes, "former-failures", "former-failures-passed-s1-required", FormerFailureSha256, FormerToolSha256, FormerDependenciesSha256);
        var formerRoot = formerDocument.RootElement;
        Require(formerRoot.GetProperty("sources").GetProperty("oldBaseline").GetProperty("Sha256").GetString() == OldBaselineSha256 &&
            formerRoot.GetProperty("sources").GetProperty("secondFailure").GetProperty("Sha256").GetString() == SecondFailureSha256 &&
            formerRoot.GetProperty("sources").GetProperty("thirdFailure").GetProperty("Sha256").GetString() == ThirdFailureSha256 &&
            formerRoot.GetProperty("sources").GetProperty("fourthFailure").GetProperty("Sha256").GetString() == FourthFailureSha256 &&
            formerRoot.GetProperty("requiredTrials").GetInt32() == 4 && formerRoot.GetProperty("reusedTrials").GetInt32() == 0,
            "Former-failure sources or trial counts differ.");
        var former = formerRoot.GetProperty("trials").Deserialize<LandingMotionTrial[]>(Options)!;
        var failed = formerRoot.GetProperty("formerFailedCases").Deserialize<LandingMotionTrial[]>(Options)!;
        Require(former.Length == 4 && former.All(MotionComplete) &&
            former[0].Kind == "drag" && former[0].Profile == "changing" && former[0].Controls == new LandingBuildControls(80, 50, 0) &&
            former[1].Kind == "drag" && former[1].Profile == "ground-swing" && former[1].Controls == new LandingBuildControls(80, 50, 100) &&
            former[2].Kind == "drag" && former[2].Profile == "ground-swing" && former[2].Controls == new LandingBuildControls(100, 100, 100) &&
            former[3].Kind == "drag" && former[3].Profile == "changing" && former[3].Controls == new LandingBuildControls(120, 100, 0) &&
            failed.Length == 4 && ExpectedOldStop(failed[0]) && ExpectedSecondStop(failed[1]) && ExpectedThirdStop(failed[2]) &&
            ExpectedFourthStop(failed[3]), "Four former-failure gates are incomplete or reordered.");
        var oldBytes = File.ReadAllBytes(oldLandingPath);
        using var oldDocument = ImportOldBaseline(oldBytes);
        var oldTrialsByControls = FrozenConfigurations(oldDocument.RootElement);
        Require(former.Concat(failed).All(t => t.Configuration == oldTrialsByControls[t.Controls].Configuration), "Former-failure physical configurations changed.");
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
            schema = 1, kind = "landing-build-touching-receipt-landing", nativeRevision = NativeRevision,
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
                    Console.WriteLine($"touching-receipt/landing/size={size}/soft={controls.Softness}/build={controls.LandingBuild}: " +
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
            schema = 1, kind = "landing-build-touching-receipt-motion", nativeRevision = NativeRevision,
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

    internal static IReadOnlyDictionary<LandingBuildControls, LandingTrial> ImportLongPredecessors(byte[] motionBytes, byte[] oldBytes)
    {
        using var motion = ImportStage(motionBytes, "motion", "motion-passed-s3-required", MotionSha256, MotionToolSha256, MotionDependenciesSha256);
        var root = motion.RootElement;
        Require(root.GetProperty("requiredTrials").GetInt32() == 171 && root.GetProperty("reusedTrials").GetInt32() == 0 &&
            root.GetProperty("landing").GetProperty("Sha256").GetString() == LandingSha256 &&
            root.GetProperty("oldBaseline").GetProperty("Sha256").GetString() == OldBaselineSha256,
            "Reviewed S2 sources or complete trial counts differ.");
        using var baseline = ImportOldBaseline(oldBytes);
        var configurations = FrozenConfigurations(baseline.RootElement);
        var trials = root.GetProperty("trials").Deserialize<LandingMotionTrial[]>(Options)!;
        var expected = LandingBuildMatrixVerification.AllControls()
            .SelectMany(c => new[] { ("flight", (string?)null, c), ("recovery", (string?)null, c) })
            .Concat(LandingBuildMatrixVerification.AllControls().Where(c => c.LandingBuild is 0 or 50 or 100)
                .SelectMany(c => new[] { "changing", "horizontal", "ground-swing" }.Select(p => ("drag", (string?)p, c))));
        Require(trials.Length == 171 && trials.Select(t => (t.Kind, t.Profile, t.Controls)).SequenceEqual(expected) &&
            trials.All(t => MotionComplete(t) && t.Configuration == configurations[t.Controls].Configuration),
            "Reviewed S2 ordered matrix, health or frozen configuration differs.");
        return configurations;
    }

    private static JsonDocument ImportStage(byte[] bytes, string stage, string outcome, string expectedSha256, string expectedToolSha256, string expectedDependenciesSha256)
    {
        Require(Hash(bytes) == expectedSha256, "Previous stage differs from the independently reviewed exact report.");
        var document = JsonDocument.Parse(bytes);
        try
        {
            var root = document.RootElement;
            Require(root.GetProperty("schema").GetInt32() == 1 &&
                root.GetProperty("kind").GetString() == $"landing-build-touching-receipt-{stage}" &&
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
        Require(native.Sha256 == CandidateSha256, "Reviewed touching-receipt candidate DLL hash mismatch.");
        return native;
    }
    private static bool ExpectedOldStop(LandingMotionTrial trial) =>
        trial.Controls == new LandingBuildControls(80, 50, 0) && trial.MappingVersion == LandingBuildMapping.Version(Table) &&
        trial.Configuration == trial.Controls.Configure(Table) && trial.Kind == "drag" && trial.Profile == "changing" &&
        !trial.Passed && !trial.ExecutionCompleted && trial.RequestedFrames == 6660 && trial.AttemptedFrame == 5865 &&
        trial.CompletedFrames == 5864 && trial.RenderedFrames == 5864 && trial.SessionReportedFrames == 5865 &&
        trial.LastHealthyFrameNumber == 5864 && trial.LastHealthyFrame is not null && trial.Failure?.Kind == LandingFailureKind.HealthOrStop &&
        trial.Failure.Frame == 5865 && IsExpectedOldError(trial.Failure.Detail);
    private static bool ExpectedSecondStop(LandingMotionTrial trial) =>
        trial.Controls == new LandingBuildControls(80, 50, 100) && trial.MappingVersion == LandingBuildMapping.Version(Table) &&
        trial.Configuration == trial.Controls.Configure(Table) && trial.Kind == "drag" && trial.Profile == "ground-swing" &&
        !trial.Passed && !trial.ExecutionCompleted && trial.RequestedFrames == 6660 && trial.AttemptedFrame == 4089 &&
        trial.CompletedFrames == 4088 && trial.RenderedFrames == 4088 && trial.SessionReportedFrames == 4089 &&
        trial.LastHealthyFrameNumber == 4088 && trial.LastHealthyFrame is not null && trial.Failure?.Kind == LandingFailureKind.HealthOrStop &&
        trial.Failure.Frame == 4089 && IsExpectedOldError(trial.Failure.Detail);
    private static bool ExpectedThirdStop(LandingMotionTrial trial) =>
        trial.Controls == new LandingBuildControls(100, 100, 100) && trial.MappingVersion == LandingBuildMapping.Version(Table) &&
        trial.Configuration == trial.Controls.Configure(Table) && trial.Kind == "drag" && trial.Profile == "ground-swing" &&
        !trial.Passed && !trial.ExecutionCompleted && trial.RequestedFrames == 6660 && trial.AttemptedFrame == 3463 &&
        trial.CompletedFrames == 3462 && trial.RenderedFrames == 3462 && trial.SessionReportedFrames == 3463 &&
        trial.LastObservationFrame == 3462 && trial.LastHealthyFrameNumber == 3462 && trial.LastHealthyFrame is not null &&
        trial.Phase == "swing" && trial.DragActive && trial.HorizontalInput == 0 && trial.Moved is null &&
        trial.Failure?.Kind == LandingFailureKind.HealthOrStop && trial.Failure.Frame == 3463 && IsExpectedOldError(trial.Failure.Detail);
    private static bool ExpectedFourthStop(LandingMotionTrial trial) =>
        trial.Controls == new LandingBuildControls(120, 100, 0) && trial.MappingVersion == LandingBuildMapping.Version(Table) &&
        trial.Configuration == trial.Controls.Configure(Table) && trial.Kind == "drag" && trial.Profile == "changing" &&
        !trial.Passed && !trial.ExecutionCompleted && trial.RequestedFrames == 6660 && trial.AttemptedFrame == 4446 &&
        trial.CompletedFrames == 4445 && trial.RenderedFrames == 4445 && trial.SessionReportedFrames == 4446 &&
        trial.LastObservationFrame == 4445 && trial.LastHealthyFrameNumber == 4445 && trial.LastHealthyFrame is not null &&
        trial.Phase == "swing" && trial.DragActive && trial.HorizontalInput == 0 && trial.Moved is null &&
        trial.TargetX == -1.4450767381087757 && trial.TargetY == 2.772285686387237 &&
        trial.Failure?.Kind == LandingFailureKind.HealthOrStop && trial.Failure.Frame == 4446 && IsExpectedOldError(trial.Failure.Detail);
    private static JsonDocument ImportFourthFailure(byte[] bytes)
    {
        Require(Hash(bytes) == FourthFailureSha256, "Fourth-failure source differs from its independently reviewed exact report.");
        var document = JsonDocument.Parse(bytes);
        try
        {
            var root = document.RootElement;
            Require(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("kind").GetString() == "landing-build-singleton-mode-motion" &&
                root.GetProperty("nativeRevision").GetString() == "singleton-mode-v1" && root.GetProperty("candidate").GetString() == LandingBuildMapping.Version(Table) &&
                root.GetProperty("completed").GetBoolean() && root.GetProperty("outcome").GetString() == "hard-failure-stop" &&
                !root.GetProperty("frozenForProduction").GetBoolean() && root.GetProperty("executionError").ValueKind == JsonValueKind.Null &&
                root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks &&
                root.GetProperty("native").GetProperty("Sha256").GetString() == SingletonSha256 &&
                root.GetProperty("tool").GetProperty("Sha256").GetString() == FourthFailureToolSha256 &&
                root.GetProperty("tool").GetProperty("DependencySetSha256").GetString() == FourthFailureDependenciesSha256 &&
                root.GetProperty("landing").GetProperty("Sha256").GetString() == SingletonLandingSha256 &&
                root.GetProperty("oldBaseline").GetProperty("Sha256").GetString() == OldBaselineSha256 &&
                root.GetProperty("requiredTrials").GetInt32() == 171 && root.GetProperty("reusedTrials").GetInt32() == 0,
                "Fourth-failure metadata, DLL, actual tool or source differs.");
            var trials = root.GetProperty("trials").Deserialize<LandingMotionTrial[]>(Options)!;
            var plan = LandingBuildMatrixVerification.AllControls()
                .SelectMany(c => new[] { (Kind: "flight", Profile: (string?)null, Controls: c), (Kind: "recovery", Profile: (string?)null, Controls: c) })
                .Concat(LandingBuildMatrixVerification.AllControls().Where(c => c.LandingBuild is 0 or 50 or 100)
                    .SelectMany(c => new[] { "changing", "horizontal", "ground-swing" }.Select(p => (Kind: "drag", Profile: (string?)p, Controls: c))))
                .ToArray();
            Require(plan.Length == 171 && trials.Length == 163 && trials.Take(162).All(MotionComplete) && ExpectedFourthStop(trials[162]) &&
                trials.Select((t, i) => t.Kind == plan[i].Kind && t.Profile == plan[i].Profile && t.Controls == plan[i].Controls).All(v => v),
                "Fourth failure or original ordered stop boundary differs.");
            return document;
        }
        catch { document.Dispose(); throw; }
    }
    private static JsonDocument ImportThirdFailure(byte[] bytes)
    {
        Require(Hash(bytes) == ThirdFailureSha256, "Third-failure source differs from its independently reviewed exact report.");
        var document = JsonDocument.Parse(bytes);
        try
        {
            var root = document.RootElement;
            Require(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("kind").GetString() == "landing-build-endpoint-selection-motion" &&
                root.GetProperty("nativeRevision").GetString() == "endpoint-selection-v1" && root.GetProperty("candidate").GetString() == LandingBuildMapping.Version(Table) &&
                root.GetProperty("completed").GetBoolean() && root.GetProperty("outcome").GetString() == "hard-failure-stop" &&
                !root.GetProperty("frozenForProduction").GetBoolean() && root.GetProperty("executionError").ValueKind == JsonValueKind.Null &&
                root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks &&
                root.GetProperty("native").GetProperty("Sha256").GetString() == EndpointSelectionSha256 &&
                root.GetProperty("tool").GetProperty("Sha256").GetString() == ThirdFailureToolSha256 &&
                root.GetProperty("tool").GetProperty("DependencySetSha256").GetString() == ThirdFailureDependenciesSha256 &&
                root.GetProperty("landing").GetProperty("Sha256").GetString() == EndpointLandingSha256 &&
                root.GetProperty("oldBaseline").GetProperty("Sha256").GetString() == OldBaselineSha256 &&
                root.GetProperty("requiredTrials").GetInt32() == 171 && root.GetProperty("reusedTrials").GetInt32() == 0,
                "Third-failure metadata, DLL, actual tool or source differs.");
            var trials = root.GetProperty("trials").Deserialize<LandingMotionTrial[]>(Options)!;
            Require(trials.Length == 144 && trials.Take(143).All(MotionComplete) && ExpectedThirdStop(trials[143]),
                "Third failure or original stop boundary differs.");
            return document;
        }
        catch { document.Dispose(); throw; }
    }
    private static JsonDocument ImportSecondFailure(byte[] bytes)
    {
        Require(Hash(bytes) == SecondFailureSha256, "Second-failure source differs from its independently reviewed exact report.");
        var document = JsonDocument.Parse(bytes);
        try
        {
            var root = document.RootElement;
            Require(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("kind").GetString() == "landing-build-receipt-history-motion" &&
                root.GetProperty("nativeRevision").GetString() == "receipt-history-v1" && root.GetProperty("candidate").GetString() == LandingBuildMapping.Version(Table) &&
                root.GetProperty("completed").GetBoolean() && root.GetProperty("outcome").GetString() == "hard-failure-stop" &&
                !root.GetProperty("frozenForProduction").GetBoolean() && root.GetProperty("executionError").ValueKind == JsonValueKind.Null &&
                root.GetProperty("frameStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks &&
                root.GetProperty("native").GetProperty("Sha256").GetString() == ReceiptHistorySha256 &&
                root.GetProperty("tool").GetProperty("Sha256").GetString() == SecondFailureToolSha256 &&
                root.GetProperty("tool").GetProperty("DependencySetSha256").GetString() == SecondFailureDependenciesSha256 &&
                root.GetProperty("oldBaseline").GetProperty("Sha256").GetString() == OldBaselineSha256 &&
                root.GetProperty("requiredTrials").GetInt32() == 171 && root.GetProperty("reusedTrials").GetInt32() == 0,
                "Second-failure metadata, DLL, actual tool or source differs.");
            var trials = root.GetProperty("trials").Deserialize<LandingMotionTrial[]>(Options)!;
            Require(trials.Length == 108 && trials.Take(107).All(MotionComplete) && ExpectedSecondStop(trials[107]),
                "Second failure or original stop boundary differs.");
            return document;
        }
        catch { document.Dispose(); throw; }
    }
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
        var path = typeof(LandingBuildTouchingReceiptVerification).Assembly.Location;
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
        $"touching-receipt/{trial.Kind}/{trial.Profile}/size={trial.Controls.SizePercent}/soft={trial.Controls.Softness}/build={trial.Controls.LandingBuild}: " +
        (trial.Passed ? "PASS" : $"FAIL {trial.Failure}"));
}
