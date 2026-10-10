namespace Dango.Physics.Presentation;

public sealed record SemanticPresentedBody(SemanticBodyState Authority, DangoBodyState LocalBody,
    double DrawnCenterX, double DrawnCenterY, IReadOnlyList<SemanticPoint> DrawnContour);

public sealed record SemanticPresentedFrame(string Epoch, double Tick, IReadOnlyList<SemanticPresentedBody> Bodies,
    IReadOnlyList<SemanticPairState> Pairs)
{
    public double StepStartTick { get; init; } = double.NaN;
    public double LocalContourTick { get; init; } = double.NaN;
    public SemanticPrescribedCoreProof? NativeCoreProof { get; init; }
}

/// <summary>Isolated admission replica. Output cannot influence authoritative gameplay or produce input eligibility.</summary>
public sealed class SemanticPresentationReplica : IDisposable
{
    private readonly string libraryPath;
    private readonly Dictionary<DangoEntityId, DangoEntityId> mapping = [];
    private readonly Dictionary<DangoEntityId, SemanticBodyState> previousBodies = [];
    private IReadOnlyList<SemanticPairState> previousPairs = [];
    private DangoPhysicsWorld? world;
    private string? epoch;
    private ulong nativeEpoch;
    private ulong ordinal;
    private ulong previousSceneRevision;
    private double previousTick = double.NegativeInfinity;
    private bool stopped;
    private bool disposed;

    public SemanticPresentationReplica(string libraryPath) => this.libraryPath = libraryPath;
    public DangoPhysicsFrame? LastLocalFrame { get; private set; }
    public IReadOnlyList<DangoPresentationTarget> LastStepStartTargets { get; private set; } = [];
    public DangoPrescribedPresentationStep? LastPrescribedInput { get; private set; }
    public DangoPrescribedPresentationTrace? LastNativeTrace { get; private set; }
    public SemanticPresentedFrame? LastPresentedFrame { get; private set; }
    public SemanticPresentedFrame? LastFailedPresentedFrame { get; private set; }

    public void BindEpoch(string value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) throw new ArgumentException("Invalid presentation epoch.", nameof(value));
        if (epoch == value) return;
        world?.Dispose(); world = null; mapping.Clear(); previousBodies.Clear(); previousPairs = [];
        LastLocalFrame = null; LastNativeTrace = null; LastPresentedFrame = null; LastFailedPresentedFrame = null; LastPrescribedInput = null;
        LastStepStartTargets = []; previousTick = double.NegativeInfinity; previousSceneRevision = 0; ordinal = 0;
        nativeEpoch = checked(nativeEpoch + 1); epoch = value; stopped = false;
    }

    public SemanticPresentedFrame Advance(SemanticPresentationSample sample)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(sample);
        if (epoch is null || epoch != sample.Epoch) throw new ArgumentException("Only the explicitly bound epoch may advance this replica.");
        if (stopped) throw new InvalidOperationException("This isolated presentation has stopped at its first failed native or drawn gate.");
        if (!double.IsFinite(sample.Tick) || sample.Tick < 0) throw new ArgumentException("Invalid presentation sample time.");
        _ = new SemanticPresentationSnapshot(sample.Epoch, (long)sample.Tick, sample.Bodies, sample.Pairs, sceneRevision: sample.SceneRevision);
        if (double.IsFinite(previousTick) && Math.Abs(sample.Tick - previousTick - 1) > 1e-8)
            throw new ArgumentException("Presentation contours require adjacent common fixed times; do not pair a later contour with an earlier center.");
        if (sample.SceneRevision < previousSceneRevision || previousSceneRevision != 0 && sample.SceneRevision == previousSceneRevision &&
            !mapping.Keys.ToHashSet().SetEquals(sample.Bodies.Select(body => body.EntityId)))
            throw new ArgumentException("A presentation topology cannot roll back or change within one scene revision.");
        if (world is null)
        {
            world = DangoPhysicsWorld.Create(libraryPath, capacity: 2);
            world.BindPrescribedEpoch(nativeEpoch);
        }
        foreach (var removed in mapping.Keys.Except(sample.Bodies.Select(body => body.EntityId)).ToArray())
        {
            world.Remove(mapping[removed]);
            mapping.Remove(removed);
        }
        foreach (var added in sample.Bodies.Where(body => !mapping.ContainsKey(body.EntityId)))
        {
            // Safe temporary creation positions are immediately corrected as a complete group.
            // They are never published as shared spawns and avoid Add rejecting an already-contacting late snapshot.
            var temporaryX = mapping.Count == 0 ? -10d : 10d;
            mapping.Add(added.EntityId, world.Add(temporaryX, 0));
        }
        var stepStartTick = sample.Tick - 1;
        var targets = sample.Bodies.Select(body => SemanticPresentationStepTiming.TargetAtStart(mapping[body.EntityId],
            body, previousBodies.GetValueOrDefault(body.EntityId))).ToArray();
        LastStepStartTargets = Array.AsReadOnly(targets);
        var startBodies = sample.Bodies.ToDictionary(body => body.EntityId, body =>
            previousBodies.TryGetValue(body.EntityId, out var previous) ? previous with
            { SupportEntityId = previous.SupportEntityId is { } support && mapping.ContainsKey(support) ? support : null } :
            body with { FloorGrounded = false, SupportEntityId = null });
        var inputBodies = sample.Bodies.Select((body, index) => new DangoPrescribedPresentationBody(mapping[body.EntityId],
            previousBodies.ContainsKey(body.EntityId) && startBodies[body.EntityId].FloorGrounded, body.FloorGrounded,
            MapSupport(startBodies[body.EntityId].SupportEntityId), MapSupport(body.SupportEntityId), targets[index],
            new DangoPresentationTarget(mapping[body.EntityId], (float)body.CoreX, (float)body.CoreY,
                (float)body.CoreVelocityX, (float)body.CoreVelocityY))).ToArray();
        var starts = previousPairs.Where(pair => mapping.ContainsKey(pair.First) && mapping.ContainsKey(pair.Second))
            .Select(pair => MapPair(pair, startBodies)).ToArray();
        var ends = sample.Pairs.Select(pair => MapPair(pair, sample.Bodies.ToDictionary(body => body.EntityId))).ToArray();
        var input = new DangoPrescribedPresentationStep(nativeEpoch, checked(ordinal + 1), stepStartTick, sample.Tick,
            previousSceneRevision == 0 ? sample.SceneRevision : previousSceneRevision, sample.SceneRevision, inputBodies, starts, ends);
        LastPrescribedInput = input;
        DangoPrescribedPresentationResult result;
        try { result = world.StepPrescribedPresentation(input); }
        catch { stopped = true; throw; }
        var local = result.Frame;
        LastNativeTrace = result.Trace;
        LastLocalFrame = local;
        if (!local.Succeeded || result.Trace.ErrorCode != 0)
        {
            stopped = true;
            throw new DangoPhysicsException(DangoPhysicsFailureKind.NativeFailure,
                "Semantic presentation admission stopped at a latched native fault.", local.ErrorCode != 0 ? local.ErrorCode : result.Trace.ErrorCode);
        }
        var bodies = sample.Bodies.Select(body =>
        {
            var state = local.Bodies.Single(localBody => localBody.EntityId == mapping[body.EntityId]);
            if (!SemanticDrawnGeometry.IsSafeLocalContour(state))
            {
                stopped = true;
                throw new InvalidOperationException("Semantic presentation admission stopped at an unsafe local contour; no fallback counts as success.");
            }
            return new SemanticPresentedBody(body, state, body.CoreX, body.CoreY,
                SemanticDrawnGeometry.SampleContour(state.Contour, body.CoreX, body.CoreY));
        }).ToArray();
        previousTick = sample.Tick;
        previousSceneRevision = sample.SceneRevision;
        ordinal = input.StepOrdinal;
        previousBodies.Clear();
        foreach (var body in sample.Bodies) previousBodies.Add(body.EntityId, body);
        previousPairs = sample.Pairs;
        var trace = result.Trace;
        var proofBodies = trace.BodyIds.Select((id, index) =>
        {
            var sourceId = mapping.Single(pair => pair.Value == id).Key;
            var prescribed = inputBodies.Single(body => body.EntityId == id);
            return new SemanticPrescribedBodyProof(sourceId, prescribed.Start, prescribed.End, trace.MaximumCoreErrors[index],
                trace.Samples[index].Select(point => new SemanticPrescribedCoreSample(point.Fraction, point.ActualCoreX,
                    point.ActualCoreY, point.TargetCoreX, point.TargetCoreY)).ToArray());
        }).ToArray();
        var presented = new SemanticPresentedFrame(sample.Epoch, sample.Tick, Array.AsReadOnly(bodies), sample.Pairs)
        {
            StepStartTick = stepStartTick,
            LocalContourTick = stepStartTick + 1,
            NativeCoreProof = new(nativeEpoch, trace.Epoch, input.StepOrdinal, trace.StepOrdinal, trace.ErrorCode,
                trace.SubstepCount, proofBodies),
        };
        if (!SemanticPresentationGate.Evaluate(presented).Passed)
        {
            stopped = true;
            LastFailedPresentedFrame = presented;
            throw new InvalidOperationException("The frozen actual-drawn or native-core gate failed; the last legal output is retained.");
        }
        LastPresentedFrame = presented;
        return presented;
    }

    private DangoEntityId? MapSupport(DangoEntityId? support) => support is { } id ? mapping[id] : null;

    private DangoPrescribedPresentationPair MapPair(SemanticPairState pair, IReadOnlyDictionary<DangoEntityId, SemanticBodyState> bodies) =>
        new(mapping[pair.First], mapping[pair.Second], true, bodies[pair.First].SupportEntityId == pair.Second,
            bodies[pair.Second].SupportEntityId == pair.First, (float)pair.NormalX, (float)pair.NormalY,
            (float)pair.Gap, (float)pair.ProjectedWidth);

    public void Dispose()
    {
        if (disposed) return;
        world?.Dispose(); world = null; mapping.Clear(); previousBodies.Clear(); disposed = true;
    }
}
