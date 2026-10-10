using System.Diagnostics;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.MaterialSample;

internal sealed record LandingMotionSample(int Frame, string Phase, ShapeMetrics Shape,
    double RenderedWidth, double RenderedHeight, bool IsRound, DangoBodyState NativeFrame);

internal sealed record LandingMotionTrial(string Kind, LandingBuildControls Controls, string MappingVersion,
    DangoSelfPhysicsParameters? Configuration, int RequestedFrames, int CompletedFrames, int AttemptedFrame,
    int SessionReportedFrames, int RenderedFrames, bool ExecutionCompleted, bool Passed, LandingFailure? Failure,
    string Phase, string? Profile, bool DragActive, double TargetX, double TargetY, double HorizontalInput,
    double PeakSpeed, double MaximumCanonicalRadius, int RoundWindowSamples, int RoundFrames,
    double? MaximumRoundWindowRadialSpread, double? MaximumRoundWindowNativeAspectError,
    double? MaximumRoundWindowRenderedAspectError, double? MinimumRoundWindowCircularity,
    double? ReleaseSpread, int? RecoveryStartReleaseIndex, double? RecoverySeconds, int LongestRoundRun,
    double? Moved, double ElapsedSeconds, int LastObservationFrame, ShapeMetrics? LastShape,
    int LastHealthyFrameNumber, DangoBodyState? LastHealthyFrame,
    IReadOnlyList<LandingMotionSample> Samples);

// One real trial per call. The stage coordinator owns ordering, persistence and stop-on-failure.
internal static class LandingBuildMotionVerification
{
    internal static LandingMotionTrial Flight(string library, LandingBuildControls controls, LandingBuildTable table) =>
        Run("flight", library, controls, table, height: 8, requestedFrames: 120, profile: null, trial =>
        {
            trial.Phase = "flight";
            for (var i = 0; i < 120; i++)
            {
                var (state, observation) = trial.Step();
                RequireAirborne(trial, state, observation.Shape, LandingFailureKind.Flight, clearance: .05);
                if (i >= 60)
                {
                    var round = trial.ObserveRound(observation);
                    trial.Sample(state, observation, round);
                    Require(round, LandingFailureKind.Flight,
                        $"Flight round gate failed: radialSpread={observation.Shape.RadialSpread:R}, " +
                        $"nativeAspect={observation.Shape.Aspect:R}, circularity={observation.Shape.Circularity:R}, " +
                        $"renderedAspect={RenderedAspect(observation):R}.");
                }
            }
        });

    internal static LandingMotionTrial Recovery(string library, LandingBuildControls controls, LandingBuildTable table) =>
        Run("recovery", library, controls, table, height: 30, requestedFrames: 169, profile: null, trial =>
        {
            trial.Phase = "initial";
            trial.Step();
            trial.Phase = "pull";
            for (var i = 0; i < 60; i++)
            {
                var previous = trial.LastHealthyFrame!;
                var (state, observation) = trial.Step(drag: true, dragX: previous.CoreX,
                    dragY: previous.CoreY + trial.Configuration!.RadialRestLength * 2.5);
                RequireAirborne(trial, state, observation.Shape, LandingFailureKind.Recovery);
            }
            trial.ReleaseSpread = trial.LastShape!.RadialSpread;
            trial.Sample(trial.LastHealthyFrame!, trial.LastObservation!, round: false);
            Require(trial.ReleaseSpread >= .10, LandingFailureKind.Recovery,
                $"Release deformation insufficient: radialSpread={trial.ReleaseSpread:R}, minimum=0.10.");

            trial.Phase = "release";
            var consecutiveRoundFrames = 0;
            for (var i = 0; i < 108; i++)
            {
                var (state, observation) = trial.Step();
                RequireAirborne(trial, state, observation.Shape, LandingFailureKind.Recovery, clearance: .05);
                var round = trial.ObserveRound(observation);
                trial.Sample(state, observation, round);
                consecutiveRoundFrames = round ? consecutiveRoundFrames + 1 : 0;
                trial.LongestRoundRun = Math.Max(trial.LongestRoundRun, consecutiveRoundFrames);
                // Preserve the old zero-based release index and its 1.5 s / 0.3 s gate.
                if (consecutiveRoundFrames >= 18 && trial.RecoveryStartReleaseIndex is null)
                    trial.RecoveryStartReleaseIndex = i - 17;
            }
            Require(trial.RecoveryStartReleaseIndex is >= 0 and <= 90, LandingFailureKind.Recovery,
                $"No sustained round recovery within 1.5s: startReleaseIndex={trial.RecoveryStartReleaseIndex}, " +
                $"longestRoundRun={trial.LongestRoundRun}, requiredRun=18.");
        });

    internal static LandingMotionTrial Drag(string library, LandingBuildControls controls, LandingBuildTable table,
        string profile) =>
        Run("drag", library, controls, table, height: profile == "changing" ? 5d / 3 : 0,
            requestedFrames: 6660, profile, trial =>
        {
            Require(profile is "changing" or "horizontal" or "ground-swing", LandingFailureKind.Incomplete,
                $"Unsupported drag profile: {profile}.");
            for (var i = 0; i < 6000; i++)
            {
                trial.Phase = i < 600 ? "settle" : "swing";
                var active = i >= 600 && i % 600 < 480;
                var (targetX, targetY) = profile switch
                {
                    "changing" => (-3.25 + 3.75 * Math.Sin(i * .043), 10d / 3 - 25d / 6 * Math.Cos(i * .071)),
                    "horizontal" => (i / 6 % 2 == 0 ? -2d : 2d, 1.5),
                    _ => (2.4 * Math.Sin(i * .12), .4 + 1.5 * Math.Cos(i * .17)),
                };
                var (state, observation) = trial.Step(drag: active, dragX: targetX, dragY: targetY);
                if (i is 599 or 5999) trial.Sample(state, observation, IsRound(observation));
            }
            trial.Phase = "release";
            for (var i = 0; i < 600; i++)
            {
                var (state, observation) = trial.Step();
                if (i == 599) trial.Sample(state, observation, IsRound(observation));
            }
            var startX = trial.LastHealthyFrame!.CoreX;
            trial.Phase = "renewed-control";
            for (var i = 0; i < 60; i++)
            {
                var (state, observation) = trial.Step(horizontal: 1);
                if (i == 59) trial.Sample(state, observation, IsRound(observation));
            }
            trial.Moved = trial.LastHealthyFrame!.CoreX - startX;
            Require(trial.Moved >= .04, LandingFailureKind.ControlResponse,
                $"Renewed direction control failed: moved={trial.Moved:R}, minimum=0.04.");
        });

    private static LandingMotionTrial Run(string kind, string library, LandingBuildControls controls,
        LandingBuildTable table, double height, int requestedFrames, string? profile, Action<MotionTrialState> execute)
    {
        var trial = new MotionTrialState(kind, controls, requestedFrames, profile);
        var clock = Stopwatch.StartNew();
        LandingFailure? failure = null;
        try
        {
            trial.MappingVersion = LandingBuildMapping.Version(table);
            trial.Configuration = controls.Configure(table);
            trial.Phase = "create-world";
            trial.Session = new(library, trial.Configuration, height);
            execute(trial);
            if (trial.CompletedFrames != requestedFrames || trial.RenderedFrames != requestedFrames)
                failure = new(LandingFailureKind.Incomplete,
                    $"Trial did not complete: completed={trial.CompletedFrames}, rendered={trial.RenderedFrames}, " +
                    $"requested={requestedFrames}.", trial.AttemptedFrame);
        }
        catch (LandingGateFailure error) { failure = new(error.Kind, error.Message, trial.AttemptedFrame); }
        catch (Exception error)
        {
            failure = new(trial.Phase is "configure" or "create-world"
                ? LandingFailureKind.Incomplete : LandingFailureKind.HealthOrStop,
                error.ToString(), trial.AttemptedFrame);
        }
        finally
        {
            if (trial.Session is not null)
            {
                trial.SessionReportedFrames = trial.Session.Frames;
                try { trial.Session.Dispose(); }
                catch (Exception error)
                {
                    failure = failure is null
                        ? new(LandingFailureKind.Incomplete, $"Dispose failed: {error}", trial.AttemptedFrame)
                        : failure with { Detail = $"{failure.Detail}\nDispose also failed: {error}" };
                }
            }
            clock.Stop();
        }
        return trial.Result(failure, clock.Elapsed.TotalSeconds);
    }

    private static void RequireAirborne(MotionTrialState trial, DangoBodyState state, ShapeMetrics shape,
        LandingFailureKind kind, double? clearance = null) =>
        Require(!state.IsGrounded && state.GroundContactCount == 0 &&
            (!clearance.HasValue || shape.LowestWorldY > trial.Configuration!.GroundHeight + clearance.Value), kind,
            $"Airborne gate failed: grounded={state.IsGrounded}, contacts={state.GroundContactCount}, " +
            $"lowestY={shape.LowestWorldY:R}, ground={trial.Configuration!.GroundHeight:R}, clearance={clearance}.");

    private static double RenderedAspect(LandingObservation observation) =>
        observation.RenderedWidth / observation.RenderedHeight;

    private static bool IsRound(LandingObservation observation) =>
        observation.Shape.IsRound && Math.Abs(RenderedAspect(observation) - 1) <= .04;

    private static void Require(bool condition, LandingFailureKind kind, string detail)
    {
        if (!condition) throw new LandingGateFailure(kind, detail);
    }

    private sealed class MotionTrialState(string kind, LandingBuildControls controls, int requestedFrames, string? profile)
    {
        internal string MappingVersion { get; set; } = "unresolved";
        internal DangoSelfPhysicsParameters? Configuration { get; set; }
        internal MaterialSession? Session { get; set; }
        internal string Phase { get; set; } = "configure";
        internal int CompletedFrames { get; private set; }
        internal int AttemptedFrame { get; private set; }
        internal int SessionReportedFrames { get; set; }
        internal int RenderedFrames { get; private set; }
        internal bool DragActive { get; private set; }
        internal double TargetX { get; private set; }
        internal double TargetY { get; private set; }
        internal double HorizontalInput { get; private set; }
        internal double PeakSpeed { get; private set; }
        internal double MaximumCanonicalRadius { get; private set; }
        internal int RoundWindowSamples { get; private set; }
        internal int RoundFrames { get; private set; }
        internal double? MaximumRoundWindowRadialSpread { get; private set; }
        internal double? MaximumRoundWindowNativeAspectError { get; private set; }
        internal double? MaximumRoundWindowRenderedAspectError { get; private set; }
        internal double? MinimumRoundWindowCircularity { get; private set; }
        internal double? ReleaseSpread { get; set; }
        internal int? RecoveryStartReleaseIndex { get; set; }
        internal int LongestRoundRun { get; set; }
        internal double? Moved { get; set; }
        internal ShapeMetrics? LastShape => LastObservation?.Shape;
        internal LandingObservation? LastObservation { get; private set; }
        internal int LastObservationFrame { get; private set; }
        internal DangoBodyState? LastHealthyFrame { get; private set; }
        internal int LastHealthyFrameNumber { get; private set; }
        private readonly List<LandingMotionSample> samples = [];

        internal (DangoBodyState State, LandingObservation Observation) Step(bool drag = false,
            double dragX = 0, double dragY = 0, double horizontal = 0)
        {
            AttemptedFrame = CompletedFrames + 1;
            DragActive = drag; TargetX = dragX; TargetY = dragY; HorizontalInput = horizontal;
            var state = Session!.Step(drag: drag, dragX: dragX, dragY: dragY, horizontal: horizontal);
            CompletedFrames++;
            // MaterialSession already checks native contour/core/area/speed. Keep every stored float finite,
            // including centroid fields, and never read Session.State after a failed native step.
            Require(float.IsFinite(state.CentroidX) && float.IsFinite(state.CentroidY),
                LandingFailureKind.HealthOrStop, "Native centroid is not finite.");
            LastHealthyFrame = state;
            LastHealthyFrameNumber = CompletedFrames;
            PeakSpeed = Math.Max(PeakSpeed, state.MaximumSpeed);
            var observation = LandingBuildFrameValidator.Observe(Session, state);
            Require(double.IsFinite(observation.Shape.Width) && observation.Shape.Width > 0 &&
                double.IsFinite(observation.Shape.Height) && observation.Shape.Height > 0 &&
                double.IsFinite(observation.Shape.Aspect) &&
                double.IsFinite(observation.Shape.RadialSpread) &&
                double.IsFinite(observation.Shape.Circularity) &&
                double.IsFinite(observation.Shape.LowestWorldY) &&
                double.IsFinite(observation.RenderedWidth) && observation.RenderedWidth > 0 &&
                double.IsFinite(observation.RenderedHeight) && observation.RenderedHeight > 0 &&
                double.IsFinite(RenderedAspect(observation)) &&
                double.IsFinite(observation.MaximumCanonicalRadius), LandingFailureKind.InvalidRender,
                "Native or rendered shape measurement is degenerate or non-finite.");
            RenderedFrames++;
            LastObservation = observation;
            LastObservationFrame = CompletedFrames;
            MaximumCanonicalRadius = Math.Max(MaximumCanonicalRadius, observation.MaximumCanonicalRadius);
            return (state, observation);
        }

        internal bool ObserveRound(LandingObservation observation)
        {
            RoundWindowSamples++;
            MaximumRoundWindowRadialSpread = Math.Max(MaximumRoundWindowRadialSpread ?? 0, observation.Shape.RadialSpread);
            MaximumRoundWindowNativeAspectError = Math.Max(MaximumRoundWindowNativeAspectError ?? 0,
                Math.Abs(observation.Shape.Aspect - 1));
            MaximumRoundWindowRenderedAspectError = Math.Max(MaximumRoundWindowRenderedAspectError ?? 0,
                Math.Abs(RenderedAspect(observation) - 1));
            MinimumRoundWindowCircularity = Math.Min(MinimumRoundWindowCircularity ?? observation.Shape.Circularity,
                observation.Shape.Circularity);
            var round = IsRound(observation);
            if (round) RoundFrames++;
            return round;
        }

        internal void Sample(DangoBodyState state, LandingObservation observation, bool round) =>
            samples.Add(new(CompletedFrames, Phase, observation.Shape,
                observation.RenderedWidth, observation.RenderedHeight, round, state));

        internal LandingMotionTrial Result(LandingFailure? failure, double elapsedSeconds)
        {
            var completed = Session is not null && Configuration is not null &&
                CompletedFrames == requestedFrames && RenderedFrames == requestedFrames;
            return new(kind, controls, MappingVersion, Configuration, requestedFrames, CompletedFrames, AttemptedFrame,
                SessionReportedFrames, RenderedFrames, completed, completed && failure is null, failure,
                Phase, profile, DragActive, TargetX, TargetY, HorizontalInput, PeakSpeed, MaximumCanonicalRadius, RoundWindowSamples,
                RoundFrames, MaximumRoundWindowRadialSpread, MaximumRoundWindowNativeAspectError,
                MaximumRoundWindowRenderedAspectError, MinimumRoundWindowCircularity, ReleaseSpread,
                RecoveryStartReleaseIndex, RecoveryStartReleaseIndex.HasValue ? RecoveryStartReleaseIndex.Value / 60d : null,
                LongestRoundRun, Moved, elapsedSeconds, LastObservationFrame, LastShape,
                LastHealthyFrameNumber, LastHealthyFrame, samples);
        }
    }
}
