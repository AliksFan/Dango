using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.MaterialSample;

// One independently scheduled member of the reviewed 135-trial long matrix.
// The supervisor owns process scheduling and admits exactly one world per worker.
internal static class LandingBuildParallelLongTrial
{
    private const int FirstFreshIndex = 5;
    private const int PlanCount = 135;
    private const string MotionSha256 = "7819655A0EB60267798C1F80176C72A637447992ACEA0A1EC384A79E51922843";
    private const string OldBaselineSha256 = "212A4805E8F63E3E8FD1EC1A907226500062AE03E108E1ABCF13158C0468321B";
    private const string DependencySetSha256 = "BD1B03183480914BC8589EA4B55ACA3305679EB1E08E780C79B2FF9BF9B132D5";
    private static readonly LandingBuildControls Default = new(100, 50, 50);
    private static readonly int[] CheckpointFrames = [600, 1800, 3600, 7200, 18000, 36000];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal static int Run(string library, string motionPath, string oldPath, string controlDirectory,
        string output, int index)
    {
        output = Path.GetFullPath(output);
        controlDirectory = Path.GetFullPath(controlDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        Directory.CreateDirectory(controlDirectory);
        try
        {
            if (index is < FirstFreshIndex or >= PlanCount)
                throw new ArgumentOutOfRangeException(nameof(index), "Fresh worker indices must be 5..134.");
            var item = Plan().ElementAt(index);
            var motionBytes = File.ReadAllBytes(motionPath);
            var oldBytes = File.ReadAllBytes(oldPath);
            var configurations = LandingBuildTouchingReceiptVerification.ImportLongPredecessors(motionBytes, oldBytes);
            Require(Hash(motionBytes) == MotionSha256 && Hash(oldBytes) == OldBaselineSha256,
                "Fixed motion or old baseline input SHA differs.");
            var native = MaterialLibraries.Describe(library, null).Candidate;
            Require(native.Sha256 == LandingBuildTouchingReceiptVerification.CandidateSha256,
                "Reviewed touching-receipt candidate DLL hash mismatch.");

            using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var tool = Tool();
            Require(tool.DependencySetSha256 == DependencySetSha256, "Actual runtime dependency set differs from the reviewed stamp.");
            var gate = new WorkerGate(controlDirectory, index, Hash(motionBytes), Hash(oldBytes), tool, output);
            LandingLongTrial? trial = null;
            var outcome = "running";
            string? executionError = null;
            bool completed = false;
            void Save()
            {
                report.Position = 0;
                WriteExact(report, new
                {
                    schema = 1,
                    kind = "landing-build-touching-receipt-parallel-long-trial",
                    index,
                    requiredFreshIndices = new { first = FirstFreshIndex, last = PlanCount - 1, total = PlanCount - FirstFreshIndex },
                    freshPlan = Plan().Select((planned, planIndex) => new { index = planIndex,
                        controls = planned.Controls, profile = planned.Profile }).Where(p => p.index >= FirstFreshIndex).ToArray(),
                    reusedPrefix = new
                    {
                        indices = new[] { 0, 1, 2, 3, 4 },
                        meaning = "Indices 0..3 are the four accepted predecessor reports; index 4 is the complete dual-variant identical known-diagnosis result. They are explicit reused prefix and are not rerun by this worker."
                    },
                    plannedTrial = new { controls = item.Controls, profile = item.Profile },
                    native, tool,
                    sourceInputSha256 = new
                    {
                        candidate = LandingBuildTouchingReceiptVerification.CandidateSha256,
                        motion = Hash(motionBytes),
                        requiredMotion = MotionSha256,
                        oldBaseline = Hash(oldBytes),
                        requiredOldBaseline = OldBaselineSha256,
                    },
                    worker = new { pid = Environment.ProcessId, index, worldCreationAttempts = gate.WorldCreationAttempts,
                        worldsCreated = gate.WorldCreated ? 1 : 0, maxWorlds = 1, attemptedSteps = gate.AttemptedSteps,
                        healthyFrames = gate.HealthyFrames, renderedFrames = gate.RenderedFrames,
                        sessionReportedFrames = gate.SessionReportedFrames },
                    stoppedByPeer = gate.StoppedByPeer,
                    failedAttemptStatus = new
                    {
                        attemptedWorldFrame = gate.LastStepAttemptedFrame,
                        stepCallReturned = gate.LastStepCallReturned,
                        nativeEvidenceSource = gate.StoppedByPeer ? "none-peer-stop" :
                            gate.LastStepAttemptedFrame is not null && !gate.LastStepCallReturned
                                ? "session-state-fallback-may-be-stale" : "returned-step-state-or-no-step",
                        staleNativeRetained = !gate.StoppedByPeer && gate.LastStepAttemptedFrame is not null &&
                            !gate.LastStepCallReturned && trial?.FirstFailure?.NativeFrame is not null,
                        sessionReportedFrames = gate.SessionReportedFrames,
                    },
                    completed, outcome, trial, executionError,
                    originalCountsComplete = trial is not null && IsComplete(trial),
                    thresholdsPassed = trial is not null && trial.Passed && IsComplete(trial),
                    acceptance = "Original 1180-sample gate, six checkpoints, postdrag and last-healthy frames, and failure bits retained. Any LongDrift stops this trial and signals shared stop; independent final review and real-window acceptance remain required."
                });
                report.SetLength(report.Position);
                report.Flush(true);
            }

            Save();
            try
            {
                gate.Admit();
                trial = RunTrial(library, item.Controls, LandingBuildTouchingReceiptVerification.Table,
                    item.Profile, gate);
                gate.SessionReportedFrames = trial.SessionReportedFrames;
                Require(trial.Configuration == configurations[item.Controls].Configuration,
                    "Fresh long physical configuration differs from the frozen configuration.");
                if (trial.Failure is not null || !IsComplete(trial))
                {
                    outcome = trial.Failure?.Kind == LandingFailureKind.LongDrift ? "long-drift-stop" : "trial-failed-or-incomplete";
                    gate.SignalStop("Trial " + index + " failed: " + trial.Failure);
                    return outcome == "long-drift-stop" ? 1 : 2;
                }
                outcome = trial.Passed ? "trial-passed" : "trial-failed";
                if (!trial.Passed) gate.SignalStop("Trial " + index + " did not pass the original threshold.");
                return trial.Passed ? 0 : 1;
            }
            catch (Exception error)
            {
                executionError = error.ToString();
                outcome = "execution-incomplete";
                gate.SignalStop("Worker " + index + " exception: " + error.Message);
                return 2;
            }
            finally
            {
                completed = outcome is not "running" and not "execution-incomplete";
                Save();
            }
        }
        catch (Exception error)
        {
            // Even failure to create the report is a coordinated stop condition.
            SignalStop(controlDirectory, "Worker " + index + " could not initialize output/admission: " + error.Message);
            Console.Error.WriteLine(error);
            return 2;
        }
    }

    private static IEnumerable<(LandingBuildControls Controls, LandingLongProfile Profile)> Plan() =>
        new[] { Default }.Concat(LandingBuildMatrixVerification.AllControls().Where(c => c != Default))
            .SelectMany(c => new[] { LandingLongProfile.Idle, LandingLongProfile.Forward, LandingLongProfile.Mirror }
                .Select(p => (c, p)));

    private static LandingLongTrial RunTrial(string library, LandingBuildControls controls,
        LandingBuildTable table, LandingLongProfile profile, WorkerGate gate)
    {
        if (profile is not (LandingLongProfile.Idle or LandingLongProfile.Forward or LandingLongProfile.Mirror))
            throw new ArgumentOutOfRangeException(nameof(profile));
        var configuration = controls.Configure(table);
        var mappingVersion = LandingBuildMapping.Version(table);
        var requestedDragFrames = profile == LandingLongProfile.Idle ? 0 : LandingBuildLongVerification.DragFrames;
        var name = profile switch { LandingLongProfile.Idle => "idle", LandingLongProfile.Forward => "ground-swing", _ => "ground-swing-x-mirror" };
        var clock = Stopwatch.StartNew();
        MaterialSession? session = null;
        var checkpoints = new List<LandingLongCheckpoint>();
        var samples = new List<LandingLongSample>();
        var failures = new List<LandingLongFailureEvidence>();
        var spans = new SpanTracker();
        int completedDragFrames = 0, completedIdleFrames = 0, attemptedWorldFrame = 0;
        int renderedFrames = 0, sessionReportedFrames = 0;
        int? attemptedIdleFrame = null;
        var phase = "creation";
        double peakSpeed = 0;
        bool stopped = false;
        DangoBodyState? lastHealthy = null, currentNative = null, postTrajectoryStart = null;
        LandingFailure? failure = null;

        void RecordFailure(LandingFailureKind kind, string detail)
        {
            var item = new LandingFailure(kind, detail, attemptedWorldFrame);
            var nativeFrame = currentNative is not null && HasFiniteNativeFloats(currentNative) ? currentNative : null;
            failures.Add(new(item, phase, attemptedWorldFrame, attemptedIdleFrame,
                attemptedWorldFrame * MaterialSession.FixedStep.TotalSeconds,
                attemptedWorldFrame * (double)(float)MaterialSession.FixedStep.TotalSeconds,
                nativeFrame, currentNative is null ? null : Bits(currentNative)));
            failure = item;
            gate.SignalStop("Trial failure at index frame " + attemptedWorldFrame + ": " + item.Kind + " " + detail);
        }

        LandingObservation Advance(bool drag = false, double x = 0, double y = 0)
        {
            // Stop is checked immediately before every public Step call.
            currentNative = null;
            gate.LastStepAttemptedFrame = null;
            gate.LastStepCallReturned = false;
            gate.BeforeStep();
            attemptedWorldFrame = session!.Frames + 1;
            gate.AttemptedSteps++;
            gate.LastStepAttemptedFrame = attemptedWorldFrame;
            var state = session.Step(drag: drag, dragX: x, dragY: y);
            gate.LastStepCallReturned = true;
            currentNative = state;
            sessionReportedFrames = session.Frames;
            gate.SessionReportedFrames = sessionReportedFrames;
            if (phase == "ground-swing") completedDragFrames++; else completedIdleFrames++;
            if (!HasFiniteNativeFloats(state))
                throw new LandingGateFailure(LandingFailureKind.HealthOrStop, "Native frame contains a nonfinite float.");
            lastHealthy = state;
            gate.HealthyFrames++;
            peakSpeed = Math.Max(peakSpeed, state.MaximumSpeed);
            var observation = LandingBuildFrameValidator.Observe(session, state);
            var shape = observation.Shape;
            if (!double.IsFinite(shape.Width) || !double.IsFinite(shape.Height) ||
                !double.IsFinite(shape.Aspect) || !double.IsFinite(shape.RadialSpread) ||
                !double.IsFinite(shape.Circularity) || !double.IsFinite(shape.LowestWorldY) || shape.Width <= 0 || shape.Height <= 0)
                throw new LandingGateFailure(LandingFailureKind.HealthOrStop, "Native shape metrics are nonfinite or degenerate.");
            if (!double.IsFinite(observation.RenderedWidth) || !double.IsFinite(observation.RenderedHeight) ||
                observation.RenderedWidth <= 0 || observation.RenderedHeight <= 0 || !double.IsFinite(observation.MaximumCanonicalRadius))
                throw new LandingGateFailure(LandingFailureKind.InvalidRender, "Actual rendered bounds or canonical radius are nonfinite or empty.");
            renderedFrames++;
            gate.RenderedFrames++;
            if (gate.RenderedFrames % 6000 == 0)
                Console.WriteLine(JsonSerializer.Serialize(new { index = gate.Index, profile = profile.ToString(), sessionFrame = session.Frames,
                    healthyFrames = gate.HealthyFrames, renderedFrames = gate.RenderedFrames }));
            return observation;
        }

        try
        {
            gate.BeforeCreate();
            session = new(library, configuration);
            gate.WorldCreated = true;
            phase = "ground-swing";
            for (var i = 0; i < requestedDragFrames; i++)
            {
                var active = i >= 600 && i % 600 < 480;
                var x = (profile == LandingLongProfile.Mirror ? -1 : 1) * 2.4 * Math.Sin(i * .12);
                var y = .4 + 1.5 * Math.Cos(i * .17);
                _ = Advance(active, x, y);
            }
            if (requestedDragFrames > 0) postTrajectoryStart = lastHealthy;
            phase = profile == LandingLongProfile.Idle ? "idle" : "post-drag-idle";
            for (var i = 0; i < LandingBuildLongVerification.IdleFrames; i++)
            {
                attemptedIdleFrame = i + 1;
                var observation = Advance();
                var checkpoint = CheckpointFrames.Contains(completedIdleFrames);
                var gateSample = i >= 600 && (i % 60 == 0 || i >= 35400);
                if (!checkpoint && !gateSample) continue;
                var sample = Sample(session, currentNative!, observation.Shape, completedIdleFrames);
                if (checkpoint)
                    checkpoints.Add(new(completedIdleFrames, session.Frames, completedIdleFrames / 60d, sample, currentNative!));
                if (!gateSample) continue;
                samples.Add(sample);
                spans.Add(sample);
                if (!sample.IsGrounded || sample.MaximumSpeed >= .03)
                    throw new LandingGateFailure(LandingFailureKind.LongSettling,
                        $"grounded={sample.IsGrounded}, maximumSpeed={sample.MaximumSpeed:R}, limit=0.03");
                // LongDrift is always fatal in fresh parallel trials; no observation-only continuation.
                if (spans.MaximumExistingGateDrift >= .003)
                    throw new LandingGateFailure(LandingFailureKind.LongDrift,
                        $"core/native-contour span={spans.MaximumExistingGateDrift:R}, limit=0.003; idleFrame={completedIdleFrames}");
            }
            if (completedDragFrames != requestedDragFrames || completedIdleFrames != LandingBuildLongVerification.IdleFrames ||
                samples.Count != LandingBuildLongVerification.RequiredGateSamples || checkpoints.Count != CheckpointFrames.Length)
                throw new LandingGateFailure(LandingFailureKind.Incomplete,
                    $"drag={completedDragFrames}/{requestedDragFrames}, idle={completedIdleFrames}/{LandingBuildLongVerification.IdleFrames}, gateSamples={samples.Count}/{LandingBuildLongVerification.RequiredGateSamples}, checkpoints={checkpoints.Count}/{CheckpointFrames.Length}");
        }
        catch (LandingGateFailure error)
        {
            stopped = true;
            currentNative ??= session?.State;
            RecordFailure(error.Kind, error.Message);
        }
        catch (WorkerStopException error)
        {
            stopped = true;
            gate.StoppedByPeer = true;
            currentNative = null;
            attemptedIdleFrame = null;
            RecordFailure(LandingFailureKind.HealthOrStop, error.Message);
        }
        catch (Exception error)
        {
            stopped = true;
            if (gate.StoppedByPeer)
            {
                currentNative = null;
                attemptedIdleFrame = null;
            }
            else currentNative ??= session?.State;
            RecordFailure(LandingFailureKind.HealthOrStop, error.ToString());
        }
        finally
        {
            sessionReportedFrames = session?.Frames ?? 0;
            gate.SessionReportedFrames = sessionReportedFrames;
            try { session?.Dispose(); }
            catch (Exception error) { stopped = true; phase = "disposal"; RecordFailure(LandingFailureKind.HealthOrStop, error.ToString()); }
            clock.Stop();
        }
        var executionCompleted = !stopped && completedDragFrames == requestedDragFrames &&
            completedIdleFrames == LandingBuildLongVerification.IdleFrames &&
            samples.Count == LandingBuildLongVerification.RequiredGateSamples && checkpoints.Count == CheckpointFrames.Length;
        var passed = executionCompleted && failure is null && spans.MaximumExistingGateDrift < .003;
        var step = MaterialSession.FixedStep;
        var completedFrames = completedDragFrames + completedIdleFrames;
        var timing = new LandingLongTiming(step.Ticks, step.TotalSeconds, (float)step.TotalSeconds,
            LandingBuildLongVerification.IdleFrames / 60d,
            LandingBuildLongVerification.IdleFrames * step.TotalSeconds,
            LandingBuildLongVerification.IdleFrames * (double)(float)step.TotalSeconds,
            completedFrames * step.TotalSeconds, completedFrames * (double)(float)step.TotalSeconds);
        return new("landing-build-long-verification", profile, name, controls, mappingVersion, configuration,
            requestedDragFrames, LandingBuildLongVerification.IdleFrames, completedDragFrames, completedIdleFrames,
            attemptedWorldFrame, sessionReportedFrames, renderedFrames, samples.Count, executionCompleted, passed,
            false, peakSpeed, clock.Elapsed.TotalSeconds, timing, spans.Summarize(), checkpoints, samples,
            postTrajectoryStart, lastHealthy, failure, failures.FirstOrDefault(), failures);
    }

    private static LandingLongSample Sample(MaterialSession session, DangoBodyState state, ShapeMetrics shape, int idleFrame)
    {
        try
        {
            var bounds = SampleGeometry.Create(state, productionMapping: false,
                scale: LandingBuildLongVerification.DisplayScale, bodySize: session.BodySize).Bounds;
            if (bounds.IsEmpty || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top) ||
                !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom) || !double.IsFinite(bounds.Width) ||
                !double.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidOperationException("Actual pixel bounds are nonfinite or empty.");
            return new(idleFrame, session.Frames, idleFrame * MaterialSession.FixedStep.TotalSeconds,
                idleFrame * (double)(float)MaterialSession.FixedStep.TotalSeconds, state.CoreX, state.CoreY,
                shape.Width, shape.Height, state.SignedArea, state.MaximumSpeed, state.GroundContactCount,
                state.IsGrounded, shape.LowestWorldY,
                new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, bounds.Width, bounds.Height));
        }
        catch (Exception error) { throw new LandingGateFailure(LandingFailureKind.InvalidRender, error.ToString()); }
    }

    private static bool IsComplete(LandingLongTrial trial)
    {
        var method = typeof(LandingBuildTouchingReceiptLongVerification).GetMethod("CountsComplete",
            BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException("CountsComplete");
        return (bool)(method.Invoke(null, new object[] { trial }) ?? false);
    }

    private static bool HasFiniteNativeFloats(DangoBodyState state) => state.Contour.All(float.IsFinite) &&
        float.IsFinite(state.CoreX) && float.IsFinite(state.CoreY) && float.IsFinite(state.SignedArea) &&
        float.IsFinite(state.CentroidX) && float.IsFinite(state.CentroidY) && float.IsFinite(state.MaximumSpeed);
    private static LandingLongNativeBits Bits(DangoBodyState state) =>
        new(state.EntityId.Value, state.Contour.Concat(new[] { state.CoreX, state.CoreY, state.SignedArea,
            state.CentroidX, state.CentroidY, state.MaximumSpeed }).Select(BitConverter.SingleToInt32Bits).ToArray(),
            state.GroundContactCount, state.IsGrounded, state.SelectedDragNode, state.PointCount);

    private sealed class SpanTracker
    {
        private readonly double[] minimum = Enumerable.Repeat(double.PositiveInfinity, 7).ToArray();
        private readonly double[] maximum = Enumerable.Repeat(double.NegativeInfinity, 7).ToArray();
        private int count;
        internal double MaximumExistingGateDrift => count == 0 ? 0 : Enumerable.Range(0, 4).Max(Span);
        private double Span(int index) => count == 0 ? 0 : maximum[index] - minimum[index];
        internal void Add(LandingLongSample sample)
        {
            var values = new[] { sample.CoreX, sample.CoreY, sample.Width, sample.Height, sample.SignedArea,
                sample.CoreRelativeDisplayPixelsBounds.Width, sample.CoreRelativeDisplayPixelsBounds.Height };
            for (var i = 0; i < values.Length; i++) { minimum[i] = Math.Min(minimum[i], values[i]); maximum[i] = Math.Max(maximum[i], values[i]); }
            count++;
        }
        internal LandingLongSpans Summarize() => new(Span(0), Span(1), Span(2), Span(3), Span(4), Span(5), Span(6), MaximumExistingGateDrift);
    }

    private sealed class WorkerGate(string controlDirectory, int index, string motionSha, string oldSha,
        ToolStamp tool, string output)
    {
        private readonly string prefix = "index" + index;
        internal bool WorldCreated;
        internal bool StoppedByPeer, LastStepCallReturned;
        internal int? LastStepAttemptedFrame;
        internal int WorldCreationAttempts, AttemptedSteps, HealthyFrames, RenderedFrames, SessionReportedFrames;
        internal int Index => index;

        internal void Admit()
        {
            CheckStop();
            var startPath = Path.Combine(controlDirectory, "start-" + prefix + ".json");
            Require(!File.Exists(startPath), "Start barrier already exists before zero-world admission.");
            CreateJson("claim-" + prefix + ".json", new { schema = 1, index, pid = Environment.ProcessId });
            CreateJson("ready-" + prefix + ".json", new { schema = 1, index, pid = Environment.ProcessId,
                worldCreations = 0, worldCreationAttempts = 0, maxWorlds = 1, prefixValidated = true,
                candidateSha256 = LandingBuildTouchingReceiptVerification.CandidateSha256,
                motionSha256 = motionSha, oldBaselineSha256 = oldSha, tool, output = Path.GetFullPath(output) });
            var timer = Stopwatch.StartNew();
            while (!File.Exists(startPath))
            {
                CheckStop();
                Require(timer.Elapsed < TimeSpan.FromSeconds(60), "Start barrier timed out with zero worlds.");
                Thread.Sleep(20);
            }
            using var start = ReadStartBarrier(startPath, timer, CheckStop);
            var root = start.RootElement;
            Require(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("index").GetInt32() == index &&
                root.GetProperty("pid").GetInt32() == Environment.ProcessId && root.GetProperty("maxWorlds").GetInt32() == 1,
                "Start barrier index, worker pid or maxWorlds differs.");
            CheckStop();
        }

        internal void BeforeCreate()
        {
            CheckStop();
            Require(WorldCreationAttempts == 0, "Only one world constructor attempt is allowed per worker process.");
            WorldCreationAttempts++;
        }
        internal void BeforeStep() => CheckStop();
        internal void SignalStop(string reason) => LandingBuildParallelLongTrial.SignalStop(controlDirectory, reason);
        private void CheckStop()
        {
            var stop = Path.Combine(controlDirectory, "stop.json");
            if (File.Exists(stop))
            {
                StoppedByPeer = true;
                throw new WorkerStopException("Shared stop present; no subsequent Step is admitted.");
            }
        }
        private void CreateJson(string name, object value)
        {
            using var stream = new FileStream(Path.Combine(controlDirectory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, value, Json); stream.Flush(true);
        }
    }

    private static JsonDocument ReadStartBarrier(string path, Stopwatch timer, Action checkStop)
    {
        while (true)
        {
            checkStop();
            Require(timer.Elapsed < TimeSpan.FromSeconds(60), "Start barrier timed out with zero worlds.");
            try { return JsonDocument.Parse(File.ReadAllBytes(path)); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            {
                checkStop();
                Thread.Sleep(20);
            }
        }
    }

    private static void SignalStop(string directory, string reason)
    {
        try
        {
            Directory.CreateDirectory(directory);
            using var stream = new FileStream(Path.Combine(directory, "stop.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, new { schema = 1, reason, pid = Environment.ProcessId, utc = DateTimeOffset.UtcNow }, Json);
            stream.Flush(true);
        }
        catch (IOException) { /* A peer already signaled the shared stop. */ }
    }

    private sealed class WorkerStopException(string message) : IOException(message) { }

    private sealed record ToolStamp(string FileName, string Sha256, string DependencySetSha256,
        IReadOnlyDictionary<string, string> Dependencies);
    private static ToolStamp Tool()
    {
        var path = typeof(LandingBuildParallelLongTrial).Assembly.Location;
        var dependencies = new[] { "Dango.Core", "Dango.Desktop", "Dango.Physics", "Dango.Protocol" }
            .ToDictionary(n => n + ".dll", n => Hash(File.ReadAllBytes(Assembly.Load(n).Location)));
        return new(Path.GetFileName(path), Hash(File.ReadAllBytes(path)),
            Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", dependencies.Select(p => $"{p.Key}={p.Value}")))), dependencies);
    }

    // IEEE bits remain authoritative when JSON decimal formatting changes a double by 1 ULP.
    private static void WriteExact(Stream stream, object data)
    {
        var bits = DoubleBits(data);
        var roundtrip = bits.ToDictionary(p => p.Key, p => BitConverter.DoubleToInt64Bits(
            JsonSerializer.Deserialize<double>(JsonSerializer.Serialize(BitConverter.Int64BitsToDouble(p.Value)))) == p.Value);
        JsonSerializer.Serialize(stream, new { data, doubleBitsAuthority = bits, decimalRoundtripExact = roundtrip }, Json);
        stream.Flush();
    }
    private static Dictionary<string, long> DoubleBits(object value)
    {
        var result = new Dictionary<string, long>();
        void Walk(object? v, string path)
        {
            if (v is null) return;
            if (v is double number) { result.Add(path, BitConverter.DoubleToInt64Bits(number)); return; }
            if (v is string || v.GetType().IsPrimitive || v.GetType().IsEnum) return;
            if (v is System.Collections.IEnumerable list) { var i = 0; foreach (var item in list) Walk(item, path + "[" + i++ + "]"); return; }
            foreach (var property in v.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .Where(p => p.GetIndexParameters().Length == 0)) Walk(property.GetValue(v), path + "." + property.Name);
        }
        Walk(value, "$data"); return result;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidDataException(detail); }
}
