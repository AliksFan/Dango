using System.Diagnostics;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.MaterialSample;

internal enum LandingLongProfile { Idle, Forward, Mirror }

internal sealed record LandingLongBounds(double Left, double Top, double Right, double Bottom,
    double Width, double Height);
internal sealed record LandingLongSample(int IdleFrame, int WorldFrame,
    double IdleManagedSeconds, double IdleNativeSeconds, double CoreX, double CoreY,
    double Width, double Height, double SignedArea, double MaximumSpeed,
    int GroundContactCount, bool IsGrounded, double LowestWorldY,
    LandingLongBounds CoreRelativeDisplayPixelsBounds);
internal sealed record LandingLongCheckpoint(int IdleFrame, int WorldFrame,
    double IdleNominalSeconds, LandingLongSample Metrics, DangoBodyState NativeFrame);
internal sealed record LandingLongSpans(double CoreX, double CoreY, double Width, double Height,
    double SignedArea, double DisplayWidthPixels, double DisplayHeightPixels,
    double MaximumExistingGateDrift);
// Preserve a nonfinite failed frame without introducing nonfinite JSON numbers.
// FloatBits orders contour[0..31], CoreX, CoreY, SignedArea, CentroidX, CentroidY, MaximumSpeed.
internal sealed record LandingLongNativeBits(int EntityId, IReadOnlyList<int> FloatBits,
    int GroundContactCount, bool IsGrounded, int SelectedDragNode, int PointCount);
internal sealed record LandingLongFailureEvidence(LandingFailure Failure, string Phase,
    int FirstWorldFrame, int? FirstIdleFrame, double WorldManagedSeconds,
    double WorldNativeSeconds, DangoBodyState? NativeFrame, LandingLongNativeBits? NativeBits);
internal sealed record LandingLongTiming(long FixedStepTicks, double ManagedFixedStepSeconds,
    float NativeFixedStepFloatSeconds, double IdleNominalRequestedSeconds,
    double IdleManagedRequestedSeconds, double IdleNativeRequestedSeconds,
    double CompletedManagedWorldSeconds, double CompletedNativeWorldSeconds);
internal sealed record LandingLongTrial(string Kind, LandingLongProfile Profile, string Name,
    LandingBuildControls Controls, string MappingVersion, DangoSelfPhysicsParameters Configuration,
    int RequestedDragFrames, int RequestedIdleFrames, int CompletedDragFrames, int CompletedIdleFrames,
    int AttemptedWorldFrame, int SessionReportedFrames, int RenderedFrames, int GateSamples,
    bool ExecutionCompleted, bool Passed, bool ContinuedDefaultForwardDriftObservation,
    double PeakSpeed, double ElapsedSeconds, LandingLongTiming Timing, LandingLongSpans Spans,
    IReadOnlyList<LandingLongCheckpoint> Checkpoints, IReadOnlyList<LandingLongSample> ExistingGateSamples,
    DangoBodyState? PostTrajectoryStart, DangoBodyState? LastHealthyNativeFrame,
    LandingFailure? Failure, LandingLongFailureEvidence? FirstFailure,
    IReadOnlyList<LandingLongFailureEvidence> Failures);

// This verifier keeps the original world-space gate. Rendering spans are additional
// diagnostics, never a replacement for native width/height or core movement.
internal static class LandingBuildLongVerification
{
    internal const int DragFrames = 6000;
    internal const int IdleFrames = 36000;
    internal const int RequiredGateSamples = 1180;
    internal const double DisplayScale = 105;
    private static readonly int[] CheckpointFrames = [600, 1800, 3600, 7200, 18000, 36000];

    internal static LandingLongTrial Run(string library, LandingBuildControls controls,
        LandingBuildTable table, LandingLongProfile profile)
    {
        if (profile is not (LandingLongProfile.Idle or LandingLongProfile.Forward or LandingLongProfile.Mirror))
            throw new ArgumentOutOfRangeException(nameof(profile));
        var configuration = controls.Configure(table);
        var mappingVersion = LandingBuildMapping.Version(table);
        var requestedDragFrames = profile == LandingLongProfile.Idle ? 0 : DragFrames;
        var name = profile switch
        {
            LandingLongProfile.Idle => "idle",
            LandingLongProfile.Forward => "ground-swing",
            _ => "ground-swing-x-mirror",
        };
        // This only permits a complete observation. It does not approve the result,
        // check the DLL hash, or establish equivalence to the old accepted report.
        var observeDefaultForwardDrift = profile == LandingLongProfile.Forward &&
            controls.SizePercent == 100 && controls.Softness == 50 && controls.LandingBuild == 50;
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
        bool continuedDefaultForwardDriftObservation = false;
        bool stopped = false;
        DangoBodyState? lastHealthy = null;
        DangoBodyState? currentNative = null;
        DangoBodyState? postTrajectoryStart = null;
        LandingFailure? failure = null;

        void RecordFailure(LandingFailureKind kind, string detail)
        {
            var item = new LandingFailure(kind, detail, attemptedWorldFrame);
            var nativeFrame = currentNative is not null && HasFiniteNativeFloats(currentNative)
                ? currentNative : null;
            failures.Add(new(item, phase, attemptedWorldFrame, attemptedIdleFrame,
                attemptedWorldFrame * MaterialSession.FixedStep.TotalSeconds,
                attemptedWorldFrame * (double)(float)MaterialSession.FixedStep.TotalSeconds,
                nativeFrame, currentNative is null ? null : Bits(currentNative)));
            // A stopping failure takes precedence over an earlier observed drift.
            // FirstFailure and Failures retain chronological evidence separately.
            failure = item;
        }

        LandingObservation Advance(bool drag = false, double x = 0, double y = 0)
        {
            attemptedWorldFrame = session!.Frames + 1;
            currentNative = null;
            var state = session.Step(drag: drag, dragX: x, dragY: y);
            currentNative = state;
            sessionReportedFrames = session.Frames;
            if (phase == "ground-swing") completedDragFrames++;
            else completedIdleFrames++;
            if (!HasFiniteNativeFloats(state))
                throw new LandingGateFailure(LandingFailureKind.HealthOrStop, "Native frame contains a nonfinite float.");
            lastHealthy = state;
            peakSpeed = Math.Max(peakSpeed, state.MaximumSpeed);
            var observation = LandingBuildFrameValidator.Observe(session, state);
            var shape = observation.Shape;
            if (!double.IsFinite(shape.Width) || !double.IsFinite(shape.Height) ||
                !double.IsFinite(shape.Aspect) || !double.IsFinite(shape.RadialSpread) ||
                !double.IsFinite(shape.Circularity) || !double.IsFinite(shape.LowestWorldY) ||
                shape.Width <= 0 || shape.Height <= 0)
                throw new LandingGateFailure(LandingFailureKind.HealthOrStop, "Native shape metrics are nonfinite or degenerate.");
            if (!double.IsFinite(observation.RenderedWidth) || !double.IsFinite(observation.RenderedHeight) ||
                observation.RenderedWidth <= 0 || observation.RenderedHeight <= 0 ||
                !double.IsFinite(observation.MaximumCanonicalRadius))
                throw new LandingGateFailure(LandingFailureKind.InvalidRender, "Actual rendered bounds or canonical radius are nonfinite or empty.");
            renderedFrames++;
            return observation;
        }

        try
        {
            session = new(library, configuration);
            phase = "ground-swing";
            for (var i = 0; i < requestedDragFrames; i++)
            {
                // Exactly the existing fixed trajectory, including inactive targets.
                var active = i >= 600 && i % 600 < 480;
                var x = (profile == LandingLongProfile.Mirror ? -1 : 1) * 2.4 * Math.Sin(i * .12);
                var y = .4 + 1.5 * Math.Cos(i * .17);
                _ = Advance(active, x, y);
            }
            if (requestedDragFrames > 0) postTrajectoryStart = lastHealthy;
            phase = profile == LandingLongProfile.Idle ? "idle" : "post-drag-idle";
            for (var i = 0; i < IdleFrames; i++)
            {
                attemptedIdleFrame = i + 1;
                // Same session and entity; all controls are zero/false throughout.
                var observation = Advance();
                var checkpoint = CheckpointFrames.Contains(completedIdleFrames);
                var gateSample = i >= 600 && (i % 60 == 0 || i >= 35400);
                if (!checkpoint && !gateSample) continue;
                var sample = Sample(session, currentNative!, observation.Shape, completedIdleFrames);
                if (checkpoint)
                    checkpoints.Add(new(completedIdleFrames, session.Frames,
                        completedIdleFrames / 60d, sample, currentNative!));
                if (!gateSample) continue;
                samples.Add(sample);
                spans.Add(sample);
                if (!sample.IsGrounded || sample.MaximumSpeed >= .03)
                    throw new LandingGateFailure(LandingFailureKind.LongSettling,
                        $"grounded={sample.IsGrounded}, maximumSpeed={sample.MaximumSpeed:R}, limit=0.03");
                if (spans.MaximumExistingGateDrift >= .003 &&
                    !failures.Any(item => item.Failure.Kind == LandingFailureKind.LongDrift))
                {
                    var detail = $"core/native-contour span={spans.MaximumExistingGateDrift:R}, limit=0.003; idleFrame={completedIdleFrames}";
                    if (!observeDefaultForwardDrift)
                        throw new LandingGateFailure(LandingFailureKind.LongDrift, detail);
                    RecordFailure(LandingFailureKind.LongDrift, detail);
                    continuedDefaultForwardDriftObservation = true;
                }
            }
            if (completedDragFrames != requestedDragFrames || completedIdleFrames != IdleFrames ||
                samples.Count != RequiredGateSamples || checkpoints.Count != CheckpointFrames.Length)
                throw new LandingGateFailure(LandingFailureKind.Incomplete,
                    $"drag={completedDragFrames}/{requestedDragFrames}, idle={completedIdleFrames}/{IdleFrames}, gateSamples={samples.Count}/{RequiredGateSamples}, checkpoints={checkpoints.Count}/{CheckpointFrames.Length}");
        }
        catch (LandingGateFailure error)
        {
            stopped = true;
            currentNative ??= session?.State;
            RecordFailure(error.Kind, error.Message);
        }
        catch (Exception error)
        {
            stopped = true;
            currentNative ??= session?.State;
            RecordFailure(LandingFailureKind.HealthOrStop, error.ToString());
        }
        finally
        {
            sessionReportedFrames = session?.Frames ?? 0;
            try { session?.Dispose(); }
            catch (Exception error)
            {
                stopped = true;
                phase = "disposal";
                RecordFailure(LandingFailureKind.HealthOrStop, error.ToString());
            }
            clock.Stop();
        }
        var executionCompleted = !stopped && completedDragFrames == requestedDragFrames &&
            completedIdleFrames == IdleFrames && samples.Count == RequiredGateSamples &&
            checkpoints.Count == CheckpointFrames.Length;
        var passed = executionCompleted && failure is null && spans.MaximumExistingGateDrift < .003;
        var step = MaterialSession.FixedStep;
        var completedFrames = completedDragFrames + completedIdleFrames;
        var timing = new LandingLongTiming(step.Ticks, step.TotalSeconds, (float)step.TotalSeconds,
            IdleFrames / 60d, IdleFrames * step.TotalSeconds,
            IdleFrames * (double)(float)step.TotalSeconds,
            completedFrames * step.TotalSeconds, completedFrames * (double)(float)step.TotalSeconds);
        return new("landing-build-long-verification", profile, name, controls, mappingVersion, configuration,
            requestedDragFrames, IdleFrames, completedDragFrames, completedIdleFrames,
            attemptedWorldFrame, sessionReportedFrames, renderedFrames, samples.Count,
            executionCompleted, passed, continuedDefaultForwardDriftObservation,
            peakSpeed, clock.Elapsed.TotalSeconds, timing, spans.Summarize(), checkpoints, samples,
            postTrajectoryStart, lastHealthy, failure, failures.FirstOrDefault(), failures);
    }

    private static LandingLongSample Sample(MaterialSession session, DangoBodyState state,
        ShapeMetrics shape, int idleFrame)
    {
        try
        {
            var bounds = SampleGeometry.Create(state, productionMapping: false,
                scale: DisplayScale, bodySize: session.BodySize).Bounds;
            if (bounds.IsEmpty || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top) ||
                !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom) ||
                !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
                bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidOperationException("Actual pixel bounds are nonfinite or empty.");
            return new(idleFrame, session.Frames, idleFrame * MaterialSession.FixedStep.TotalSeconds,
                idleFrame * (double)(float)MaterialSession.FixedStep.TotalSeconds,
                state.CoreX, state.CoreY, shape.Width, shape.Height, state.SignedArea,
                state.MaximumSpeed, state.GroundContactCount, state.IsGrounded, shape.LowestWorldY,
                new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, bounds.Width, bounds.Height));
        }
        catch (Exception error)
        {
            throw new LandingGateFailure(LandingFailureKind.InvalidRender, error.ToString());
        }
    }

    private static bool HasFiniteNativeFloats(DangoBodyState state) =>
        state.Contour.All(float.IsFinite) && float.IsFinite(state.CoreX) && float.IsFinite(state.CoreY) &&
        float.IsFinite(state.SignedArea) && float.IsFinite(state.CentroidX) &&
        float.IsFinite(state.CentroidY) && float.IsFinite(state.MaximumSpeed);

    private static LandingLongNativeBits Bits(DangoBodyState state) =>
        new(state.EntityId.Value, state.Contour.Concat(new[] { state.CoreX, state.CoreY,
            state.SignedArea, state.CentroidX, state.CentroidY, state.MaximumSpeed })
            .Select(BitConverter.SingleToInt32Bits).ToArray(), state.GroundContactCount,
            state.IsGrounded, state.SelectedDragNode, state.PointCount);

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
            for (var i = 0; i < values.Length; i++)
            {
                minimum[i] = Math.Min(minimum[i], values[i]);
                maximum[i] = Math.Max(maximum[i], values[i]);
            }
            count++;
        }
        internal LandingLongSpans Summarize() =>
            new(Span(0), Span(1), Span(2), Span(3), Span(4), Span(5), Span(6), MaximumExistingGateDrift);
    }
}
