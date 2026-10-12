using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.MaterialSample;

// A single explicitly retained default difference is evidence-bound. All other failures stop.
internal static class LandingBuildTouchingReceiptLongVerification
{
    private const string KnownDefaultSha256 = "360C1373DC9A7435D6A4F480EB590BC3885EDAF5D30D92ADC5D2942327E7FB13";
    private static readonly LandingBuildControls Default = new(100, 50, 50);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly int[] CheckpointFrames = [600, 1800, 3600, 7200, 18000, 36000];
    private static readonly int[] GateFrames = Enumerable.Range(0, 36000)
        .Where(i => i >= 600 && (i % 60 == 0 || i >= 35400)).Select(i => i + 1).ToArray();
    private sealed record Source(string FileName, string Sha256);
    private sealed record ToolEvidence(string FileName, string Sha256, string DependencySetSha256, IReadOnlyDictionary<string, string> Dependencies);
    private sealed record RetainedDifference(int TrialIndex, LandingBuildControls Controls, LandingLongProfile Profile,
        string ReferenceSha256, bool ExactSavedReferenceMatch, bool ThresholdPassed);

    internal static int Run(string library, string motionPath, string oldPath, string knownPath, string output) => Guard(() =>
    {
        var native = MaterialLibraries.Describe(library, null).Candidate;
        Require(native.Sha256 == LandingBuildTouchingReceiptVerification.CandidateSha256,
            "Reviewed touching-receipt candidate DLL hash mismatch.");
        var motionBytes = File.ReadAllBytes(motionPath);
        var oldBytes = File.ReadAllBytes(oldPath);
        var configurations = LandingBuildTouchingReceiptVerification.ImportLongPredecessors(motionBytes, oldBytes);
        var knownBytes = File.ReadAllBytes(knownPath);
        Require(Hash(knownBytes) == KnownDefaultSha256, "Default long reference differs from the independently reviewed exact report.");
        using var known = JsonDocument.Parse(knownBytes);
        ValidateKnownContent(known.RootElement);
        Require(known.RootElement.GetProperty("configuration").Deserialize<DangoSelfPhysicsParameters>(Options) == configurations[Default].Configuration,
            "Default long reference differs from the frozen physical configuration.");
        var reference = known.RootElement.GetProperty("trials")[0];
        var plan = Plan().ToArray();
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var tool = Tool();
        var trials = new List<LandingLongTrial>();
        var retained = new List<RetainedDifference>();
        var outcome = "running";
        var completed = false;
        string? executionError = null;
        void Save()
        {
            report.Position = 0;
            JsonSerializer.Serialize(report, new
            {
                schema = 1, kind = "landing-build-touching-receipt-long",
                nativeRevision = LandingBuildTouchingReceiptVerification.NativeRevision,
                candidate = LandingBuildMapping.Version(LandingBuildTouchingReceiptVerification.Table),
                completed, outcome, native, tool, frozenForProduction = false,
                frameStepTicks = MaterialSession.FixedStep.Ticks,
                sources = new { motion = Describe(motionPath, motionBytes), oldBaseline = Describe(oldPath, oldBytes), defaultLongReference = Describe(knownPath, knownBytes) },
                requiredTrials = 135, reusedTrials = 0, plannedTrials = plan.Select(p => new { controls = p.Controls, profile = p.Profile }),
                trials, retainedDifferences = retained, thresholdsPassed = trials.Count == 135 && trials.All(t => t.Passed),
                executionError,
                acceptance = "Finite long matrix only. A retained default drift stays failed against the original threshold; independent final review and real-window manual acceptance remain required.",
            }, Options);
            report.SetLength(report.Position); report.Flush();
        }
        Save();
        try
        {
            foreach (var item in plan)
            {
                var trace = item.Controls == Default && item.Profile == LandingLongProfile.Forward ? new KnownTrace(reference) : null;
                var trial = LandingBuildLongVerification.Run(library, item.Controls, LandingBuildTouchingReceiptVerification.Table,
                    item.Profile, trace is null ? null : trace.Observe);
                trials.Add(trial);
                Require(trial.Configuration == configurations[item.Controls].Configuration,
                    "Fresh long physical configuration differs from the frozen configuration.");
                var isRetained = trace is not null && IsRetained(trial, trace, reference);
                if (isRetained) retained.Add(new(trials.Count - 1, trial.Controls, trial.Profile, KnownDefaultSha256, true, false));
                Console.WriteLine($"touching-receipt/long/{item.Profile}/size={item.Controls.SizePercent}/soft={item.Controls.Softness}/build={item.Controls.LandingBuild}: " +
                    (trial.Passed && CountsComplete(trial) ? "PASS" : isRetained ? "KNOWN-DIFFERENCE (original LongDrift failed)" : $"FAIL {trial.Failure}"));
                Save();
                if (!(CountsComplete(trial) && (trial.Passed || isRetained)))
                {
                    outcome = trial.Failure is null || trial.Failure.Kind == LandingFailureKind.Incomplete ? "execution-incomplete" : "hard-failure-stop";
                    return outcome == "hard-failure-stop" ? 1 : 2;
                }
            }
            Require(trials.Count == 135 && trials.Select(t => (t.Controls, t.Profile)).SequenceEqual(plan) &&
                retained.Count <= 1 && trials.Count(t => !t.Passed) == retained.Count, "Long matrix incomplete or retained difference broadened.");
            outcome = retained.Count == 0 ? "long-passed-manual-required" : "long-complete-retained-default-difference-manual-required";
            return 0;
        }
        catch (Exception error) { executionError = error.ToString(); outcome = "execution-incomplete"; return 2; }
        finally { completed = outcome is not "running" and not "execution-incomplete"; Save(); }
    });

    // The default comes first so its previously retained exception is tested early.
    private static IEnumerable<(LandingBuildControls Controls, LandingLongProfile Profile)> Plan() =>
        new[] { Default }.Concat(LandingBuildMatrixVerification.AllControls().Where(c => c != Default))
            .SelectMany(c => new[] { LandingLongProfile.Idle, LandingLongProfile.Forward, LandingLongProfile.Mirror }.Select(p => (c, p)));

    private static bool CountsComplete(LandingLongTrial t)
    {
        var drag = t.Profile == LandingLongProfile.Idle ? 0 : 6000;
        var total = drag + 36000;
        return t.Kind == "landing-build-long-verification" &&
            t.Profile is LandingLongProfile.Idle or LandingLongProfile.Forward or LandingLongProfile.Mirror &&
            t.MappingVersion == LandingBuildMapping.Version(LandingBuildTouchingReceiptVerification.Table) &&
            t.Configuration == t.Controls.Configure(LandingBuildTouchingReceiptVerification.Table) &&
            double.IsFinite(t.PeakSpeed) && t.PeakSpeed >= t.ExistingGateSamples.Select(s => s.MaximumSpeed).DefaultIfEmpty(0).Max() &&
            t.PeakSpeed <= t.Configuration.MaximumSpeed + .002 &&
            t.RequestedDragFrames == drag && t.RequestedIdleFrames == 36000 && t.CompletedDragFrames == drag && t.CompletedIdleFrames == 36000 &&
            t.AttemptedWorldFrame == total && t.SessionReportedFrames == total && t.RenderedFrames == total && t.GateSamples == 1180 &&
            t.ExecutionCompleted && t.ExistingGateSamples.Count == 1180 && t.Checkpoints.Count == 6 &&
            t.ExistingGateSamples.Select(s => s.IdleFrame).SequenceEqual(GateFrames) &&
            t.ExistingGateSamples.All(s => s.WorldFrame == drag + s.IdleFrame && s.IdleManagedSeconds == s.IdleFrame * MaterialSession.FixedStep.TotalSeconds &&
                s.IdleNativeSeconds == s.IdleFrame * (double)(float)MaterialSession.FixedStep.TotalSeconds &&
                s.IsGrounded && s.MaximumSpeed < .03 && s.LowestWorldY >= t.Configuration.GroundHeight - .002 &&
                s.SignedArea >= t.Configuration.MinimumArea && s.Width > 0 && s.Height > 0 &&
                s.CoreRelativeDisplayPixelsBounds.Width > 0 && s.CoreRelativeDisplayPixelsBounds.Height > 0 &&
                new[] { s.CoreX, s.CoreY, s.Width, s.Height, s.SignedArea, s.MaximumSpeed, s.LowestWorldY,
                    s.CoreRelativeDisplayPixelsBounds.Left, s.CoreRelativeDisplayPixelsBounds.Top,
                    s.CoreRelativeDisplayPixelsBounds.Right, s.CoreRelativeDisplayPixelsBounds.Bottom,
                    s.CoreRelativeDisplayPixelsBounds.Width, s.CoreRelativeDisplayPixelsBounds.Height }.All(double.IsFinite)) &&
            t.Checkpoints.Select(c => c.IdleFrame).SequenceEqual(CheckpointFrames) &&
            t.Checkpoints.All(c => c.WorldFrame == drag + c.IdleFrame && c.IdleNominalSeconds == c.IdleFrame / 60d &&
                c.Metrics.IdleFrame == c.IdleFrame && c.Metrics.WorldFrame == c.WorldFrame) &&
            t.Timing == new LandingLongTiming(MaterialSession.FixedStep.Ticks, MaterialSession.FixedStep.TotalSeconds,
                (float)MaterialSession.FixedStep.TotalSeconds, 600, 36000 * MaterialSession.FixedStep.TotalSeconds,
                36000 * (double)(float)MaterialSession.FixedStep.TotalSeconds, total * MaterialSession.FixedStep.TotalSeconds,
                total * (double)(float)MaterialSession.FixedStep.TotalSeconds) &&
            t.LastHealthyNativeFrame is not null && FrameEqual(t.LastHealthyNativeFrame, t.Checkpoints[^1].NativeFrame) &&
            (drag == 0 ? t.PostTrajectoryStart is null : t.PostTrajectoryStart is not null) &&
            t.Spans == RecomputeSpans(t.ExistingGateSamples) &&
            t.Passed == (t.Failure is null && t.Spans.MaximumExistingGateDrift < .003) &&
            (!t.Passed || t.Failures.Count == 0 && t.FirstFailure is null && !t.ContinuedDefaultForwardDriftObservation);
    }

    private static LandingLongSpans RecomputeSpans(IReadOnlyList<LandingLongSample> samples)
    {
        double Span(Func<LandingLongSample, double> field) => samples.Max(field) - samples.Min(field);
        var x = Span(s => s.CoreX); var y = Span(s => s.CoreY);
        var width = Span(s => s.Width); var height = Span(s => s.Height);
        return new(x, y, width, height, Span(s => s.SignedArea),
            Span(s => s.CoreRelativeDisplayPixelsBounds.Width), Span(s => s.CoreRelativeDisplayPixelsBounds.Height),
            new[] { x, y, width, height }.Max());
    }

    private static bool IsRetained(LandingLongTrial t, KnownTrace trace, JsonElement old) =>
        t.Controls == Default && t.Profile == LandingLongProfile.Forward && CountsComplete(t) && !t.Passed &&
        t.ContinuedDefaultForwardDriftObservation && t.Failure?.Kind == LandingFailureKind.LongDrift && t.Failures.Count == 1 &&
        t.FirstFailure == t.Failures[0] && t.FirstFailure.Failure == t.Failure &&
        RetainedEvidenceEqual(t.FirstFailure, old) &&
        trace.Complete && DoubleEqual(t.PeakSpeed, old.GetProperty("GlobalPeakSpeed").GetDouble()) &&
        SpanDoubles(t.Spans).Select(BitConverter.DoubleToInt64Bits).SequenceEqual(
            SpanDoubles(old.GetProperty("Spans").Deserialize<LandingLongSpans>(Options)!).Select(BitConverter.DoubleToInt64Bits)) &&
        FrameEqual(t.LastHealthyNativeFrame!, old.GetProperty("LastFrame").Deserialize<DangoBodyState>(Options)!) &&
        FrameEqual(t.PostTrajectoryStart!, old.GetProperty("PostTrajectoryStart").Deserialize<DangoBodyState>(Options)!);

    // The old diagnostic saved the first drift time and gate metrics, not its full
    // native frame. Compare those saved fields; only check the new frame/bits for
    // internal consistency, without claiming an unavailable old float comparison.
    private static bool RetainedEvidenceEqual(LandingLongFailureEvidence evidence, JsonElement old)
    {
        var saved = old.GetProperty("Failures")[0];
        var worldFrame = saved.GetProperty("FirstWorldFrame").GetInt32();
        var idleFrame = saved.GetProperty("FirstIdleFrame").GetInt32();
        var samples = old.GetProperty("ExistingGateSamples").Deserialize<LandingLongSample[]>(Options)!;
        var firstSpan = RecomputeSpans(samples.Where(s => s.IdleFrame <= idleFrame).ToArray()).MaximumExistingGateDrift;
        var frame = evidence.NativeFrame;
        var bits = evidence.NativeBits;
        return evidence.FirstWorldFrame == worldFrame && evidence.FirstIdleFrame == idleFrame &&
            evidence.Phase == saved.GetProperty("Phase").GetString() && evidence.Failure.Kind == LandingFailureKind.LongDrift &&
            evidence.Failure.Frame == worldFrame &&
            evidence.Failure.Detail == $"core/native-contour span={firstSpan:R}, limit=0.003; idleFrame={idleFrame}" &&
            saved.GetProperty("Detail").GetString() == $"span={firstSpan:R}, limit=0.003" &&
            DoubleEqual(evidence.WorldManagedSeconds, saved.GetProperty("WorldManagedSeconds").GetDouble()) &&
            DoubleEqual(evidence.WorldNativeSeconds, worldFrame * (double)(float)MaterialSession.FixedStep.TotalSeconds) &&
            frame is not null && bits is not null && bits.EntityId == frame.EntityId.Value &&
            bits.GroundContactCount == frame.GroundContactCount && bits.IsGrounded == frame.IsGrounded &&
            bits.SelectedDragNode == frame.SelectedDragNode && bits.PointCount == frame.PointCount &&
            bits.FloatBits.SequenceEqual(frame.Contour.Concat(new[] { frame.CoreX, frame.CoreY,
                frame.SignedArea, frame.CentroidX, frame.CentroidY, frame.MaximumSpeed }).Select(BitConverter.SingleToInt32Bits));
    }

    private static bool DoubleEqual(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
    private static double[] SpanDoubles(LandingLongSpans s) =>
        [s.CoreX, s.CoreY, s.Width, s.Height, s.SignedArea, s.DisplayWidthPixels, s.DisplayHeightPixels, s.MaximumExistingGateDrift];

    private static void ValidateKnownContent(JsonElement root)
    {
        Require(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("kind").GetString() == "fixed-post-drag-idle-diagnostic" &&
            root.GetProperty("diagnosticOnly").GetBoolean() && root.GetProperty("executionCompleted").GetBoolean() &&
            !root.GetProperty("thresholdsPassed").GetBoolean() && root.GetProperty("nativeSha256").GetString() == LandingBuildMapping.ApprovedNativeSha256 &&
            root.GetProperty("librarySource").GetProperty("Sha256").GetString() == LandingBuildMapping.ApprovedNativeSha256 &&
            root.GetProperty("nativeAbiVersion").GetInt32() == DangoPhysicsWorld.NativeAbiVersion &&
            root.GetProperty("fixedProfiles").EnumerateArray().Select(p => p.GetString()).SequenceEqual(new[] { "ground-swing", "ground-swing-x-mirror" }) &&
            root.GetProperty("fixedControls").Deserialize<MaterialControls>(Options) == new MaterialControls(100, 50, 90) &&
            root.GetProperty("configuration").Deserialize<DangoSelfPhysicsParameters>(Options) == Default.Configure(LandingBuildTouchingReceiptVerification.Table),
            "Old default long reference identity or physical configuration differs.");
        var trajectory = root.GetProperty("trajectory");
        Require(trajectory.GetProperty("dragFrames").GetInt32() == 6000 && trajectory.GetProperty("idleFrames").GetInt32() == 36000 &&
            trajectory.GetProperty("totalRequestedWorldFramesPerProfile").GetInt32() == 42000 && trajectory.GetProperty("lastActiveDragWorldFrame").GetInt32() == 5880 &&
            trajectory.GetProperty("dragActive").GetString() == "Zero-based i >= 600 && i % 600 < 480, for i = 0..5999." &&
            trajectory.GetProperty("dragTargetX").GetString() == "2.4 * sin(i * 0.12), negated only for the mirror profile." &&
            trajectory.GetProperty("dragTargetY").GetString() == "0.4 + 1.5 * cos(i * 0.17)." &&
                root.GetProperty("timing").GetProperty("fixedStepTicks").GetInt64() == MaterialSession.FixedStep.Ticks &&
                root.GetProperty("timing").GetProperty("managedFixedStepSeconds").GetDouble() == MaterialSession.FixedStep.TotalSeconds &&
                root.GetProperty("timing").GetProperty("nativeFixedStepFloatSeconds").GetSingle() == (float)MaterialSession.FixedStep.TotalSeconds &&
                root.GetProperty("sampling").GetProperty("requiredGateSamples").GetInt32() == 1180 &&
                root.GetProperty("sampling").GetProperty("checkpointFrames").EnumerateArray().Select(f => f.GetInt32()).SequenceEqual(CheckpointFrames),
            "Old default fixed trajectory or step differs.");
        var old = root.GetProperty("trials");
        Require(old.GetArrayLength() == 2, "Old default forward/mirror pair incomplete.");
        for (var i = 0; i < 2; i++)
        {
            var t = old[i];
            Require(t.GetProperty("Name").GetString() == (i == 0 ? "ground-swing" : "ground-swing-x-mirror") &&
                t.GetProperty("MirroredX").GetBoolean() == (i == 1) && t.GetProperty("ExecutionCompleted").GetBoolean() &&
                t.GetProperty("ThresholdsPassed").GetBoolean() == (i == 1) &&
                new[] { "CompletedDragFrames", "CompletedIdleFrames", "AttemptedWorldFrame", "SessionReportedFrames", "RenderedFrames" }
                    .Select(n => t.GetProperty(n).GetInt32()).SequenceEqual(new[] { 6000, 36000, 42000, 42000, 42000 }) &&
                new[] { "RenderingFailures", "PenetrationFailures", "ReferenceFailures", "SettlingFailures" }.All(n => t.GetProperty(n).GetInt32() == 0) &&
                t.GetProperty("StoppedPhase").ValueKind == JsonValueKind.Null && t.GetProperty("StoppedReason").ValueKind == JsonValueKind.Null &&
                t.GetProperty("ExistingGateSamples").EnumerateArray().Select(s => s.GetProperty("IdleFrame").GetInt32()).SequenceEqual(GateFrames) &&
                t.GetProperty("Checkpoints").EnumerateArray().Select(c => c.GetProperty("Metrics").GetProperty("IdleFrame").GetInt32()).SequenceEqual(CheckpointFrames),
                "Old default long execution, samples or failures differ.");
            var span = t.GetProperty("Spans").GetProperty("MaximumExistingGateDrift").GetDouble();
            Require(double.IsFinite(span) && (i == 0 ? span >= .003 && Math.Round(span, 9) == .010632098 : span < .003 && Math.Round(span, 9) == .000845393),
                "Old default threshold result differs from the retained observation.");
            Require(t.GetProperty("Failures").GetArrayLength() == (i == 0 ? 1 : 0) &&
                (i == 1 || t.GetProperty("Failures")[0].GetProperty("Code").GetString() == "existing-span-threshold"),
                "Old default contains a new failure or broader exception.");
        }
    }

    private sealed class KnownTrace(JsonElement reference)
    {
        private readonly Dictionary<int, LandingLongSample> samples = reference.GetProperty("ExistingGateSamples").Deserialize<LandingLongSample[]>(Options)!.ToDictionary(s => s.IdleFrame);
        private readonly Dictionary<int, JsonElement> checkpoints = reference.GetProperty("Checkpoints").EnumerateArray()
            .ToDictionary(c => c.GetProperty("Metrics").GetProperty("IdleFrame").GetInt32(), c => c.Clone());
        private readonly DangoBodyState start = reference.GetProperty("PostTrajectoryStart").Deserialize<DangoBodyState>(Options)!;
        private readonly HashSet<int> observed = [];
        private bool startObserved;
        private bool matches = true;
        internal bool Complete => startObserved && matches && observed.SetEquals(GateFrames.Concat(CheckpointFrames));
        internal void Observe(LandingLongSample? sample, DangoBodyState frame, double drift)
        {
            if (sample is null)
            {
                matches &= !startObserved && FrameEqual(frame, start); startObserved = true; return;
            }
            matches &= startObserved && observed.Add(sample.IdleFrame);
            var expected = samples.TryGetValue(sample.IdleFrame, out var value) ? value :
                checkpoints.TryGetValue(sample.IdleFrame, out var checkpoint) ? checkpoint.GetProperty("Metrics").Deserialize<LandingLongSample>(Options) : null;
            matches &= expected is not null && SampleEqual(sample, expected);
            if (checkpoints.TryGetValue(sample.IdleFrame, out var saved))
                matches &= FrameEqual(frame, saved.GetProperty("FullFrame").Deserialize<DangoBodyState>(Options)!);
            // The exception never masks a new observation. Stop at the same drift gate,
            // including a new mismatch after the earlier retained failure was recorded.
            if (!matches && drift >= .003)
                throw new LandingGateFailure(LandingFailureKind.LongDrift, "Default drift differs from the independently reviewed old observation; original limit=0.003.");
        }
    }

    private static bool FrameEqual(DangoBodyState a, DangoBodyState b) =>
        a.EntityId == b.EntityId && a.PointCount == b.PointCount && a.GroundContactCount == b.GroundContactCount &&
        a.IsGrounded == b.IsGrounded && a.SelectedDragNode == b.SelectedDragNode &&
        a.Contour.Concat(new[] { a.CoreX, a.CoreY, a.SignedArea, a.CentroidX, a.CentroidY, a.MaximumSpeed })
            .Select(BitConverter.SingleToInt32Bits).SequenceEqual(b.Contour.Concat(new[] { b.CoreX, b.CoreY, b.SignedArea, b.CentroidX, b.CentroidY, b.MaximumSpeed }).Select(BitConverter.SingleToInt32Bits));

    private static bool SampleEqual(LandingLongSample a, LandingLongSample b) =>
        a.IdleFrame == b.IdleFrame && a.WorldFrame == b.WorldFrame &&
        a.GroundContactCount == b.GroundContactCount && a.IsGrounded == b.IsGrounded &&
        SampleDoubles(a).Select(BitConverter.DoubleToInt64Bits).SequenceEqual(SampleDoubles(b).Select(BitConverter.DoubleToInt64Bits));
    private static double[] SampleDoubles(LandingLongSample s) =>
        [s.IdleManagedSeconds, s.IdleNativeSeconds, s.CoreX, s.CoreY, s.Width, s.Height,
         s.SignedArea, s.MaximumSpeed, s.LowestWorldY, s.CoreRelativeDisplayPixelsBounds.Left,
         s.CoreRelativeDisplayPixelsBounds.Top, s.CoreRelativeDisplayPixelsBounds.Right,
         s.CoreRelativeDisplayPixelsBounds.Bottom, s.CoreRelativeDisplayPixelsBounds.Width,
         s.CoreRelativeDisplayPixelsBounds.Height];

    internal static int VerifyContract(string knownPath) => Guard(() =>
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(knownPath));
        ValidateKnownContent(document.RootElement);
        var reference = document.RootElement.GetProperty("trials")[0];
        var trace = new KnownTrace(reference);
        var start = reference.GetProperty("PostTrajectoryStart").Deserialize<DangoBodyState>(Options)!;
        var last = reference.GetProperty("LastFrame").Deserialize<DangoBodyState>(Options)!;
        trace.Observe(null, start, 0);
        var gate = reference.GetProperty("ExistingGateSamples").Deserialize<LandingLongSample[]>(Options)!;
        var checkpoints = reference.GetProperty("Checkpoints").EnumerateArray().ToDictionary(c => c.GetProperty("Metrics").GetProperty("IdleFrame").GetInt32(), c => c.Clone());
        var sequence = gate.Concat(checkpoints.Values.Select(c => c.GetProperty("Metrics").Deserialize<LandingLongSample>(Options)!))
            .DistinctBy(s => s.IdleFrame).OrderBy(s => s.IdleFrame).ToArray();
        foreach (var sample in sequence)
            trace.Observe(sample, checkpoints.TryGetValue(sample.IdleFrame, out var c) ? c.GetProperty("FullFrame").Deserialize<DangoBodyState>(Options)! : last, .02);
        Require(trace.Complete, "Equivalent saved default reference rejected.");
        var negatives = 0;
        void Reject(Action action)
        {
            try { action(); } catch (LandingGateFailure) { negatives++; return; }
            throw new InvalidOperationException("Long contract negative was accepted.");
        }
        Reject(() => { var t = new KnownTrace(reference); t.Observe(null, start, 0); t.Observe(gate[0] with { Width = double.BitIncrement(gate[0].Width) }, last, .003); });
        Reject(() => { var t = new KnownTrace(reference); t.Observe(gate[0], last, .003); });
        Reject(() => { var t = new KnownTrace(reference); t.Observe(null, start, 0); t.Observe(gate[0], last, 0); t.Observe(gate[0], last, .003); });
        var firstCheckpointFrame = checkpoints[sequence[0].IdleFrame].GetProperty("FullFrame").Deserialize<DangoBodyState>(Options)!;
        Reject(() => { var t = new KnownTrace(reference); t.Observe(null, start, 0); t.Observe(sequence[0] with { WorldFrame = 0 }, firstCheckpointFrame, .003); });
        // A mismatch below the original drift limit is remembered, and must fail
        // as soon as the unmodified threshold is reached.
        Reject(() => { var t = new KnownTrace(reference); t.Observe(null, start, 0); t.Observe(gate[0] with { CoreX = double.BitIncrement(gate[0].CoreX) }, last, .002); t.Observe(gate[1], last, .003); });
        Reject(() => { var t = new KnownTrace(reference); t.Observe(null, start with { CoreX = MathF.BitIncrement(start.CoreX) }, 0); t.Observe(gate[0], last, .003); });
        Reject(() => { var t = new KnownTrace(reference); t.Observe(null, start, 0); t.Observe(sequence[0], firstCheckpointFrame with { CoreX = MathF.BitIncrement(firstCheckpointFrame.CoreX) }, .003); });
        var fixture = CleanFixture(document.RootElement);
        Require(CountsComplete(fixture), "Complete original mirror evidence rejected by long counter gate.");
        var counterCases = new List<LandingLongTrial>
        {
            fixture with { RequestedDragFrames = 5999 }, fixture with { RequestedIdleFrames = 35999 },
            fixture with { CompletedDragFrames = 5999 }, fixture with { CompletedIdleFrames = 35999 },
            fixture with { AttemptedWorldFrame = 42001 }, fixture with { SessionReportedFrames = 41999 },
            fixture with { RenderedFrames = 41999 }, fixture with { GateSamples = 1179 },
            fixture with { ExecutionCompleted = false }, fixture with { Passed = false },
            fixture with { PeakSpeed = double.NaN }, fixture with { LastHealthyNativeFrame = null },
            fixture with { PostTrajectoryStart = null }, fixture with { Checkpoints = fixture.Checkpoints.Take(5).ToArray() },
            fixture with { ExistingGateSamples = fixture.ExistingGateSamples.Take(1179).ToArray() },
            fixture with { Timing = fixture.Timing with { FixedStepTicks = 166667 } },
            fixture with { Timing = fixture.Timing with { NativeFixedStepFloatSeconds = MathF.BitIncrement(fixture.Timing.NativeFixedStepFloatSeconds) } },
            fixture with { Controls = new(101, 50, 50) }, fixture with { Profile = LandingLongProfile.Idle },
            fixture with { Configuration = fixture.Configuration with { RadialElasticity = fixture.Configuration.RadialElasticity + 1 } },
            fixture with { MappingVersion = "wrong-mapping" }, fixture with { Kind = "wrong-kind" },
            fixture with { Spans = fixture.Spans with { MaximumExistingGateDrift = 0 } },
            fixture with { ContinuedDefaultForwardDriftObservation = true },
        };
        foreach (var change in new Func<LandingLongSample, LandingLongSample>[]
        {
            s => s with { WorldFrame = s.WorldFrame + 1 }, s => s with { IdleManagedSeconds = s.IdleManagedSeconds + 1 },
            s => s with { IdleNativeSeconds = s.IdleNativeSeconds + 1 }, s => s with { IsGrounded = false },
            s => s with { MaximumSpeed = .03 }, s => s with { Width = double.NaN },
            s => s with { CoreRelativeDisplayPixelsBounds = s.CoreRelativeDisplayPixelsBounds with { Width = 0 } },
        })
        {
            var altered = fixture.ExistingGateSamples.ToArray(); altered[0] = change(altered[0]);
            counterCases.Add(fixture with { ExistingGateSamples = altered });
        }
        Require(counterCases.All(c => !CountsComplete(c)), "Damaged long counters, sample or pass flag accepted.");
        var oldFailure = reference.GetProperty("Failures")[0];
        var firstWorld = oldFailure.GetProperty("FirstWorldFrame").GetInt32();
        var firstIdle = oldFailure.GetProperty("FirstIdleFrame").GetInt32();
        var firstSpan = RecomputeSpans(gate.Where(s => s.IdleFrame <= firstIdle).ToArray()).MaximumExistingGateDrift;
        // This is a synthetic zero-world evidence fixture. The full first-drift
        // frame was not saved by the old tool; last supplies an internally coherent
        // frame/bits pair only, never an assertion about the old missing frame.
        var nativeBits = new LandingLongNativeBits(last.EntityId.Value, last.Contour.Concat(new[] { last.CoreX, last.CoreY,
            last.SignedArea, last.CentroidX, last.CentroidY, last.MaximumSpeed }).Select(BitConverter.SingleToInt32Bits).ToArray(),
            last.GroundContactCount, last.IsGrounded, last.SelectedDragNode, last.PointCount);
        var evidence = new LandingLongFailureEvidence(new(LandingFailureKind.LongDrift,
            $"core/native-contour span={firstSpan:R}, limit=0.003; idleFrame={firstIdle}", firstWorld),
            "post-drag-idle", firstWorld, firstIdle, oldFailure.GetProperty("WorldManagedSeconds").GetDouble(),
            firstWorld * (double)(float)MaterialSession.FixedStep.TotalSeconds, last, nativeBits);
        Require(RetainedEvidenceEqual(evidence, reference), "Coherent saved-time synthetic evidence rejected.");
        var evidenceCases = new[]
        {
            evidence with { FirstWorldFrame = firstWorld + 1 }, evidence with { FirstIdleFrame = firstIdle + 1 },
            evidence with { WorldManagedSeconds = double.BitIncrement(evidence.WorldManagedSeconds) },
            evidence with { WorldNativeSeconds = double.BitIncrement(evidence.WorldNativeSeconds) },
            evidence with { Phase = "idle" }, evidence with { Failure = evidence.Failure with { Frame = firstWorld + 1 } },
            evidence with { Failure = evidence.Failure with { Kind = LandingFailureKind.LongSettling } },
            evidence with { Failure = evidence.Failure with { Detail = "wrong-span" } },
            evidence with { NativeFrame = null }, evidence with { NativeBits = null },
            evidence with { NativeFrame = last with { CoreX = MathF.BitIncrement(last.CoreX) } },
            evidence with { NativeBits = nativeBits with { PointCount = nativeBits.PointCount + 1 } },
        };
        Require(evidenceCases.All(e => !RetainedEvidenceEqual(e, reference)), "Damaged retained failure evidence accepted.");
        Require(Plan().Count() == 135 && Plan().Distinct().Count() == 135 && GateFrames.Length == 1180 && negatives == 7,
            "Long plan or finite observer contract incomplete.");
        Console.WriteLine(JsonSerializer.Serialize(new { passed = true, worldCreations = 0, worldSteps = 0,
            physicalTrialsRun = 0, candidateAdmitted = false, observerNegativeCases = negatives, counterNegativeCases = counterCases.Count,
            retainedEvidenceNegativeCases = evidenceCases.Length, firstDriftFullOldFrameAvailable = false,
            equivalentSavedReferenceAccepted = true, plannedTrials = 135, originalGateSamples = 1180 }));
        return 0;
    });

    private static LandingLongTrial CleanFixture(JsonElement root)
    {
        var old = root.GetProperty("trials")[1];
        var samples = old.GetProperty("ExistingGateSamples").Deserialize<LandingLongSample[]>(Options)!;
        var checkpoints = old.GetProperty("Checkpoints").EnumerateArray().Select(c =>
        {
            var sample = c.GetProperty("Metrics").Deserialize<LandingLongSample>(Options)!;
            return new LandingLongCheckpoint(sample.IdleFrame, sample.WorldFrame, sample.IdleFrame / 60d, sample,
                c.GetProperty("FullFrame").Deserialize<DangoBodyState>(Options)!);
        }).ToArray();
        var step = MaterialSession.FixedStep;
        return new("landing-build-long-verification", LandingLongProfile.Mirror, "ground-swing-x-mirror", Default,
            LandingBuildMapping.Version(LandingBuildTouchingReceiptVerification.Table), Default.Configure(LandingBuildTouchingReceiptVerification.Table),
            6000, 36000, 6000, 36000, 42000, 42000, 42000, 1180, true, true, false,
            old.GetProperty("GlobalPeakSpeed").GetDouble(), 0,
            new(step.Ticks, step.TotalSeconds, (float)step.TotalSeconds, 600, 36000 * step.TotalSeconds,
                36000 * (double)(float)step.TotalSeconds, 42000 * step.TotalSeconds, 42000 * (double)(float)step.TotalSeconds),
            old.GetProperty("Spans").Deserialize<LandingLongSpans>(Options)!, checkpoints, samples,
            old.GetProperty("PostTrajectoryStart").Deserialize<DangoBodyState>(Options), old.GetProperty("LastFrame").Deserialize<DangoBodyState>(Options),
            null, null, []);
    }

    private static ToolEvidence Tool()
    {
        var path = typeof(LandingBuildTouchingReceiptLongVerification).Assembly.Location;
        var dependencies = new[] { "Dango.Core", "Dango.Desktop", "Dango.Physics", "Dango.Protocol" }
            .ToDictionary(n => n + ".dll", n => Hash(File.ReadAllBytes(System.Reflection.Assembly.Load(n).Location)));
        return new(Path.GetFileName(path), Hash(File.ReadAllBytes(path)),
            Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", dependencies.Select(p => $"{p.Key}={p.Value}")))), dependencies);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static Source Describe(string path, byte[] bytes) => new(Path.GetFileName(path), Hash(bytes));
    private static void Require(bool condition, string detail) => LandingBuildMatrixVerification.Require(condition, detail);
    private static int Guard(Func<int> action) { try { return action(); } catch (Exception error) { Console.Error.WriteLine(error); return 2; } }
}
