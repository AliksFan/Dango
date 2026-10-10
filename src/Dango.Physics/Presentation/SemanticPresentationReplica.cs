using Dango.Physics.Configuration;

namespace Dango.Physics.Presentation;

public sealed record SemanticPresentedBody(SemanticBodyState Authority, DangoBodyState LocalBody,
    double DrawnCenterX, double DrawnCenterY, IReadOnlyList<SemanticPoint> DrawnContour);

public sealed record SemanticPresentedFrame(string Epoch, double Tick, IReadOnlyList<SemanticPresentedBody> Bodies,
    IReadOnlyList<SemanticPairState> Pairs)
{
    public double StepStartTick { get; init; } = double.NaN;
    public double LocalContourTick { get; init; } = double.NaN;
}

/// <summary>Isolated admission replica. Output cannot influence authoritative gameplay or produce input eligibility.</summary>
public sealed class SemanticPresentationReplica : IDisposable
{
    private readonly string libraryPath;
    private readonly Dictionary<DangoEntityId, DangoEntityId> mapping = [];
    private readonly Dictionary<DangoEntityId, SemanticBodyState> previousBodies = [];
    private DangoPhysicsWorld? world;
    private string? epoch;
    private double previousTick = double.NegativeInfinity;
    private bool disposed;

    public SemanticPresentationReplica(string libraryPath) => this.libraryPath = libraryPath;
    public DangoPhysicsFrame? LastLocalFrame { get; private set; }
    public IReadOnlyList<DangoPresentationTarget> LastStepStartTargets { get; private set; } = [];

    public SemanticPresentedFrame Advance(SemanticPresentationSample sample)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(sample);
        if (!double.IsFinite(sample.Tick) || sample.Tick < 0) throw new ArgumentException("Invalid presentation sample time.");
        _ = new SemanticPresentationSnapshot(sample.Epoch, (long)sample.Tick, sample.Bodies, sample.Pairs);
        if (epoch != sample.Epoch)
        {
            world?.Dispose(); world = null; mapping.Clear(); previousBodies.Clear(); LastLocalFrame = null;
            LastStepStartTargets = []; previousTick = double.NegativeInfinity; epoch = sample.Epoch;
        }
        if (double.IsFinite(previousTick) && Math.Abs(sample.Tick - previousTick - 1) > 1e-8)
            throw new ArgumentException("Presentation contours require adjacent common fixed times; do not pair a later contour with an earlier center.");
        world ??= DangoPhysicsWorld.Create(libraryPath, capacity: 2);
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
        world.ApplyPresentationTargets(targets);
        var local = world.Step(SemanticPresentationSampler.FixedStep, mapping.Values.Select(id => new DangoPhysicsInput(id)).ToArray());
        LastLocalFrame = local;
        if (!local.Succeeded) throw new DangoPhysicsException(DangoPhysicsFailureKind.NativeFailure,
            "Semantic presentation admission stopped at a latched native fault.", local.ErrorCode);
        var bodies = sample.Bodies.Select(body =>
        {
            var state = local.Bodies.Single(localBody => localBody.EntityId == mapping[body.EntityId]);
            if (!SemanticDrawnGeometry.IsSafeLocalContour(state))
                throw new InvalidOperationException("Semantic presentation admission stopped at an unsafe local contour; no fallback counts as success.");
            return new SemanticPresentedBody(body, state, body.CoreX, body.CoreY,
                SemanticDrawnGeometry.SampleContour(state.Contour, body.CoreX, body.CoreY));
        }).ToArray();
        previousTick = sample.Tick;
        previousBodies.Clear();
        foreach (var body in sample.Bodies) previousBodies.Add(body.EntityId, body);
        return new(sample.Epoch, sample.Tick, Array.AsReadOnly(bodies), sample.Pairs)
        {
            StepStartTick = stepStartTick,
            LocalContourTick = stepStartTick + 1,
        };
    }

    public void Dispose()
    {
        if (disposed) return;
        world?.Dispose(); world = null; mapping.Clear(); previousBodies.Clear(); disposed = true;
    }
}
