using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.MaterialSample;

// Fixed interaction falsification report. Exit 0 means both trajectories ran
// completely; threshold failures remain recorded and do not become acceptance.
internal static class PostDragIdleDiagnostics
{
    private const int DragFrames = 6000;
    private const int IdleFrames = 36000;
    private const int RequiredGateSamples = 1180;
    private static readonly int[] CheckpointFrames = [600, 1800, 3600, 7200, 18000, 36000];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Run(string library, string output)
    {
        library = Path.GetFullPath(library);
        output = Path.GetFullPath(output);
        if (File.Exists(output))
            throw new IOException("甩动后静置诊断输出已存在；请提供新路径以保留既有报告。");
        var source = new MaterialLibrarySource(Path.GetFileName(library),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library))),
            "Primary CLI library; actual fixed-default material trajectories.");
        var controls = new MaterialControls(SizePercent: 100, Softness: 50, Settling: 90);
        var configuration = controls.Configure();
        var trials = new List<Trial>();
        var parent = Path.GetDirectoryName(output);
        if (parent is not null) Directory.CreateDirectory(parent);
        // CreateNew is the authoritative no-overwrite check. Initial and per-trial
        // reports also preserve completed evidence if a later native process fault occurs.
        using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        void SaveReport()
        {
            var step = MaterialSession.FixedStep;
            var report = new
            {
                kind = "fixed-post-drag-idle-diagnostic", schema = 1, diagnosticOnly = true,
                acceptance = "Exit 0 reports complete diagnostic execution only. ThresholdsPassed is separate; this command does not replace --verify, drag regression or manual acceptance.",
                executionCompleted = trials.Count == 2 && trials.All(trial => trial.ExecutionCompleted),
                thresholdsPassed = trials.Count == 2 && trials.All(trial => trial.ThresholdsPassed),
                nativeFileName = source.FileName, nativeSha256 = source.Sha256,
                librarySource = source, nativeAbiVersion = DangoPhysicsWorld.NativeAbiVersion,
                fixedControls = controls, configuration,
                fixedProfiles = new[] { "ground-swing", "ground-swing-x-mirror" },
                trajectory = new
                {
                    dragFrames = DragFrames, idleFrames = IdleFrames,
                    totalRequestedWorldFramesPerProfile = DragFrames + IdleFrames,
                    dragActive = "Zero-based i >= 600 && i % 600 < 480, for i = 0..5999.",
                    dragTargetX = "2.4 * sin(i * 0.12), negated only for the mirror profile.",
                    dragTargetY = "0.4 + 1.5 * cos(i * 0.17).",
                    lastActiveDragWorldFrame = 5880,
                    postTrajectory = "Same world and entity; all inputs zero/false for 36000 further frames. No reset, renewed control, reinitialization or pose correction.",
                },
                timing = new
                {
                    fixedStepTicks = step.Ticks, managedFixedStepSeconds = step.TotalSeconds,
                    nativeFixedStepFloatSeconds = (float)step.TotalSeconds,
                    nativeFixedStepFloatAsDoubleSeconds = (double)(float)step.TotalSeconds,
                    idleManagedRequestedSeconds = IdleFrames * step.TotalSeconds,
                    idleNativeRequestedSeconds = IdleFrames * (double)(float)step.TotalSeconds,
                    idleNominalRequestedSeconds = IdleFrames / 60d,
                },
                sampling = new
                {
                    existingGate = "Post-trajectory zero-based i >= 600 and (i % 60 == 0 or i >= 35400).",
                    requiredGateSamples = RequiredGateSamples, checkpointFrames = CheckpointFrames,
                    healthAndRendering = "Every successful drag and idle frame is checked; penetration tolerance is 0.002 world units.",
                    settled = "Existing gate samples require grounded and maximumSpeed < 0.03.",
                    span = "Maximum span of core X/Y and native expanded contour width/height must be < 0.003 over the complete existing sampling set.",
                    reference = "Unchanged SilhouetteMetrics.MatchesReference and isotropic candidate drawing.",
                    earlyStop = "Native/managed health or world failure stops its trajectory. Rendering, penetration, settling, reference and span failures are recorded while simulation continues.",
                },
                trials,
            };
            stream.Position = 0;
            stream.SetLength(0);
            JsonSerializer.Serialize(stream, report, JsonOptions);
            stream.Flush(flushToDisk: true);
        }
        SaveReport();
        foreach (var mirror in new[] { false, true })
        {
            var trial = Diagnose(library, configuration, mirror);
            trials.Add(trial);
            SaveReport();
            Console.WriteLine($"{trial.Name}: drag={trial.CompletedDragFrames}/{DragFrames}, " +
                $"idle={trial.CompletedIdleFrames}/{IdleFrames}, drift={trial.Spans.MaximumExistingGateDrift:F9}, " +
                $"thresholdsPassed={trial.ThresholdsPassed}, stopped={trial.StoppedReason ?? "no"}");
        }
        return trials.All(trial => trial.ExecutionCompleted) ? 0 : 1;
    }

    private static Trial Diagnose(string library, DangoSelfPhysicsParameters configuration, bool mirror)
    {
        var clock = Stopwatch.StartNew();
        var name = mirror ? "ground-swing-x-mirror" : "ground-swing";
        var failures = new Dictionary<string, Failure>();
        var samples = new List<Sample>();
        var checkpoints = new List<Checkpoint>();
        var spans = new SpanTracker();
        var completedDragFrames = 0;
        var completedIdleFrames = 0;
        var attemptedWorldFrame = 0;
        var sessionReportedFrames = 0;
        var renderedFrames = 0;
        var renderingFailures = 0;
        var penetrationFailures = 0;
        var referenceFailures = 0;
        var settlingFailures = 0;
        var globalPeakSpeed = 0d;
        var phase = "creation";
        string? stoppedReason = null;
        string? stoppedPhase = null;
        DangoBodyState? lastState = null;
        DangoBodyState? postTrajectoryStart = null;

        void Record(string code, int worldFrame, int? idleFrame, string detail)
        {
            var key = phase + ":" + code;
            if (!failures.ContainsKey(key))
                failures.Add(key, new(code, phase, worldFrame, idleFrame,
                    worldFrame * MaterialSession.FixedStep.TotalSeconds, detail));
        }

        try
        {
            using var session = new MaterialSession(library, configuration);
            DangoBodyState? Advance(bool drag = false, double x = 0, double y = 0)
            {
                attemptedWorldFrame = session.Frames + 1;
                try
                {
                    var state = session.Step(drag: drag, dragX: x, dragY: y);
                    sessionReportedFrames = session.Frames;
                    lastState = state;
                    globalPeakSpeed = Math.Max(globalPeakSpeed, state.MaximumSpeed);
                    return state;
                }
                catch (Exception error)
                {
                    sessionReportedFrames = session.Frames;
                    lastState = session.State;
                    stoppedReason = error.Message;
                    stoppedPhase = phase;
                    Record(error is DangoPhysicsException ? "native-call-failure" : "native-or-managed-health-failure",
                        attemptedWorldFrame, phase == "post-drag-idle" ? completedIdleFrames + 1 : null,
                        error.ToString());
                    return null;
                }
            }

            (ShapeMetrics Shape, Bounds? DisplayBounds) Observe(DangoBodyState state, int? idleFrame)
            {
                var shape = ShapeMetrics.Measure(state);
                if (!double.IsFinite(shape.Width) || !double.IsFinite(shape.Height) || !double.IsFinite(shape.LowestWorldY))
                    throw new InvalidOperationException("真实轮廓测量产生非有限值。");
                if (shape.LowestWorldY < configuration.GroundHeight - .002)
                {
                    penetrationFailures++;
                    Record("penetration", session.Frames, idleFrame,
                        $"lowest={shape.LowestWorldY:R}, ground={configuration.GroundHeight:R}, tolerance=0.002");
                }
                Bounds? displayBounds = null;
                try
                {
                    var bounds = SampleGeometry.Create(state, productionMapping: false, scale: 105, bodySize: session.BodySize).Bounds;
                    if (bounds.IsEmpty || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
                        throw new InvalidOperationException("真实绘制范围无效。");
                    displayBounds = new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, bounds.Width, bounds.Height);
                    renderedFrames++;
                }
                catch (Exception error)
                {
                    renderingFailures++;
                    Record("rendering", session.Frames, idleFrame, error.Message);
                }
                return (shape, displayBounds);
            }

            phase = "ground-swing";
            for (var i = 0; i < DragFrames; i++)
            {
                var active = i >= 600 && i % 600 < 480;
                var x = (mirror ? -1 : 1) * 2.4 * Math.Sin(i * .12);
                var y = .4 + 1.5 * Math.Cos(i * .17);
                var state = Advance(active, x, y);
                if (state is null) break;
                completedDragFrames++;
                _ = Observe(state, null);
            }
            if (stoppedReason is null && completedDragFrames == DragFrames)
            {
                postTrajectoryStart = lastState;
                phase = "post-drag-idle";
                for (var i = 0; i < IdleFrames; i++)
                {
                    var state = Advance();
                    if (state is null) break;
                    completedIdleFrames++;
                    var (shape, displayBounds) = Observe(state, completedIdleFrames);
                    var checkpoint = CheckpointFrames.Contains(completedIdleFrames);
                    var sampledByExistingGate = i >= 600 && (i % 60 == 0 || i >= 35400);
                    if (!checkpoint && !sampledByExistingGate) continue;
                    SilhouetteMetrics? silhouette = null;
                    try
                    {
                        var measured = SilhouetteMetrics.Measure(state);
                        if (!double.IsFinite(measured.Aspect) || !double.IsFinite(measured.WidestHeight) ||
                            !double.IsFinite(measured.BottomCoverage) || !double.IsFinite(measured.MeanReferenceError) ||
                            !double.IsFinite(measured.MaxReferenceError) || measured.SectionWidths.Any(value => !double.IsFinite(value)))
                            throw new InvalidOperationException("真实参考轮廓测量产生非有限值。");
                        silhouette = measured;
                    }
                    catch (Exception error)
                    {
                        Record("silhouette-measurement", session.Frames, completedIdleFrames, error.Message);
                    }
                    var sample = new Sample(completedIdleFrames, session.Frames,
                        completedIdleFrames * MaterialSession.FixedStep.TotalSeconds,
                        completedIdleFrames * (double)(float)MaterialSession.FixedStep.TotalSeconds,
                        state.CoreX, state.CoreY, shape.Width, shape.Height, state.SignedArea,
                        state.MaximumSpeed, state.GroundContactCount, state.IsGrounded,
                        shape.LowestWorldY, displayBounds, silhouette);
                    if (sampledByExistingGate)
                    {
                        samples.Add(sample); spans.Add(sample);
                        if (!state.IsGrounded || state.MaximumSpeed >= .03)
                        {
                            settlingFailures++;
                            Record("settling", session.Frames, completedIdleFrames,
                                $"grounded={state.IsGrounded}, maximumSpeed={state.MaximumSpeed:R}, limit=0.03");
                        }
                        if (silhouette?.MatchesReference != true)
                        {
                            referenceFailures++;
                            Record("reference-silhouette", session.Frames, completedIdleFrames,
                                silhouette is null ? "Measurement unavailable." :
                                $"aspect={silhouette.Aspect:R}, meanError={silhouette.MeanReferenceError:R}, maxError={silhouette.MaxReferenceError:R}, widestHeight={silhouette.WidestHeight:R}, bottomCoverage={silhouette.BottomCoverage:R}");
                        }
                        if (spans.MaximumExistingGateDrift >= .003)
                            Record("existing-span-threshold", session.Frames, completedIdleFrames,
                                $"span={spans.MaximumExistingGateDrift:R}, limit=0.003");
                    }
                    if (checkpoint) checkpoints.Add(new(completedIdleFrames / 60d, sample, state));
                }
            }
        }
        catch (Exception error)
        {
            stoppedReason = error.Message; stoppedPhase = phase;
            Record("diagnostic-world-failure", attemptedWorldFrame,
                phase == "post-drag-idle" ? completedIdleFrames : null, error.ToString());
        }
        clock.Stop();
        var executionCompleted = completedDragFrames == DragFrames && completedIdleFrames == IdleFrames && stoppedReason is null;
        var thresholdsPassed = executionCompleted && samples.Count == RequiredGateSamples && failures.Count == 0 &&
            renderingFailures == 0 && penetrationFailures == 0 && referenceFailures == 0 && settlingFailures == 0 &&
            spans.MaximumExistingGateDrift < .003;
        return new(name, mirror, completedDragFrames, completedIdleFrames, attemptedWorldFrame, sessionReportedFrames,
            executionCompleted, thresholdsPassed, renderedFrames, renderingFailures, penetrationFailures,
            referenceFailures, settlingFailures, globalPeakSpeed, stoppedPhase, stoppedReason,
            clock.Elapsed.TotalSeconds, spans.Summarize(), failures.Values.ToArray(), checkpoints, samples,
            postTrajectoryStart, lastState);
    }

    private sealed record Bounds(double Left, double Top, double Right, double Bottom, double Width, double Height);
    private sealed record Sample(int IdleFrame, int WorldFrame, double IdleManagedSeconds, double IdleNativeSeconds,
        double CoreX, double CoreY, double Width, double Height, double SignedArea, double MaximumSpeed,
        int GroundContactCount, bool IsGrounded, double LowestWorldY,
        Bounds? CoreRelativeDisplayPixelsBounds, SilhouetteMetrics? Silhouette);
    private sealed record Checkpoint(double IdleNominalSeconds, Sample Metrics, DangoBodyState FullFrame);
    private sealed record Failure(string Code, string Phase, int FirstWorldFrame, int? FirstIdleFrame,
        double WorldManagedSeconds, string Detail);
    private sealed record Spans(double CoreX, double CoreY, double Width, double Height, double SignedArea,
        double DisplayWidthPixels, double DisplayHeightPixels, double MaximumExistingGateDrift);
    private sealed record Trial(string Name, bool MirroredX, int CompletedDragFrames, int CompletedIdleFrames,
        int AttemptedWorldFrame, int SessionReportedFrames, bool ExecutionCompleted, bool ThresholdsPassed,
        int RenderedFrames, int RenderingFailures, int PenetrationFailures, int ReferenceFailures, int SettlingFailures,
        double GlobalPeakSpeed, string? StoppedPhase, string? StoppedReason, double WallSeconds, Spans Spans,
        IReadOnlyList<Failure> Failures, IReadOnlyList<Checkpoint> Checkpoints, IReadOnlyList<Sample> ExistingGateSamples,
        DangoBodyState? PostTrajectoryStart, DangoBodyState? LastFrame);

    private sealed class SpanTracker
    {
        private readonly double[] minimum = Enumerable.Repeat(double.PositiveInfinity, 7).ToArray();
        private readonly double[] maximum = Enumerable.Repeat(double.NegativeInfinity, 7).ToArray();
        internal double MaximumExistingGateDrift => Enumerable.Range(0, 4).Max(Span);
        private double Span(int index) => double.IsFinite(minimum[index]) && double.IsFinite(maximum[index]) ? maximum[index] - minimum[index] : 0;
        internal void Add(Sample sample)
        {
            var values = new[] { sample.CoreX, sample.CoreY, sample.Width, sample.Height, sample.SignedArea,
                sample.CoreRelativeDisplayPixelsBounds?.Width ?? double.NaN,
                sample.CoreRelativeDisplayPixelsBounds?.Height ?? double.NaN };
            for (var i = 0; i < values.Length; i++)
            {
                if (!double.IsFinite(values[i])) continue;
                minimum[i] = Math.Min(minimum[i], values[i]); maximum[i] = Math.Max(maximum[i], values[i]);
            }
        }
        internal Spans Summarize() => new(Span(0), Span(1), Span(2), Span(3), Span(4), Span(5), Span(6), MaximumExistingGateDrift);
    }
}
