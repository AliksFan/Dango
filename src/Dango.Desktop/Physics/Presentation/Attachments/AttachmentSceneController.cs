using Dango.Core.Animation;
using Dango.Physics;

namespace Dango.Desktop.Physics.Presentation.Attachments;

/// <summary>Scene-owned presentation state; never advances or writes physics.</summary>
public sealed class AttachmentSceneController(PhysicsPresentationFrame baseline) : IDisposable
{
    private sealed record Entry(AttachmentTarget Target, AttachmentToken Token, AttachmentToken QuietToken, AttachmentMotionController Motion)
    {
        public long Sequence;
        public long QuietSequence;
        public string? Error;
    }
    private readonly AttachmentCommandRouter router = new(true);
    private readonly Dictionary<DangoEntityId, Entry> entries = [];
    private readonly Dictionary<DangoEntityId, EyeLayoutParameters> eyeLayouts = [];
    private bool paused;
    private long step;
    internal long FaultRevision { get; private set; }
    public string? LastError => entries.Values.Select(e => e.Error).FirstOrDefault(e => e is not null);
    public bool IsHealthy(DangoEntityId id) => entries.TryGetValue(id, out var entry) && entry.Error is null;

    public void Synchronize(IReadOnlyList<DangoEntityId> ids)
    {
        foreach (var id in entries.Keys.Except(ids).ToArray()) { router.Remove(entries[id].Target); entries.Remove(id); }
        foreach (var id in ids.Where(id => !entries.ContainsKey(id)))
        {
            var target = router.Add(id);
            // Token registration is allowed only while running; new entities are
            // created by the same UI thread, before the next successful step.
            router.SetPaused(false);
            var token = router.RegisterPreview(target);
            var quietToken = router.RegisterAction(target);
            router.SetPaused(paused);
            var motion = new AttachmentMotionController(router, target, baseline) { Paused = paused };
            if (eyeLayouts.TryGetValue(id, out var layout)) motion.SetEyeLayout(layout);
            entries.Add(id, new(target, token, quietToken, motion));
        }
    }

    public void SetPaused(bool value)
    {
        paused = value; router.SetPaused(value);
        foreach (var entry in entries.Values) entry.Motion.Paused = value;
    }

    public void Reset()
    {
        var ids = entries.Keys.ToArray();
        foreach (var entry in entries.Values) router.Remove(entry.Target);
        entries.Clear(); step = 0; Synchronize(ids);
    }

    /// <summary>Stores an entity's base request separately from the animation command channels.</summary>
    public void SetEyeLayout(DangoEntityId entity, EyeLayoutParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (entity.Value <= 0) throw new ArgumentOutOfRangeException(nameof(entity));
        eyeLayouts[entity] = parameters;
        if (entries.TryGetValue(entity, out var entry)) entry.Motion.SetEyeLayout(parameters);
    }

    public AttachmentRequestResult Submit(DangoEntityId id, AttachmentIntent intent)
    {
        if (!entries.TryGetValue(id, out var entry)) throw new ArgumentException("附件实体已失效。", nameof(id));
        var result = router.Submit(entry.Target, entry.Token, ++entry.Sequence, intent);
        return result;
    }

    internal AttachmentRequestResult SubmitQuietIdle(DangoEntityId id, AttachmentIntent intent)
    {
        if (!entries.TryGetValue(id, out var entry)) throw new ArgumentException("附件实体已失效。", nameof(id));
        return router.Submit(entry.Target, entry.QuietToken, ++entry.QuietSequence, intent);
    }

    public IReadOnlyList<PhysicsPresentationFrame> Advance(DesktopPhysicsSceneFrame frame, double seconds,
        DangoEntityId selected, DangoFacingDirection facing, DirectionalControlIntent input = default)
    {
        Synchronize(frame.Bodies.Select(b => b.EntityId).ToArray());
        var advance = frame.Physics.Succeeded && !paused;
        var result = frame.Bodies.Select(body =>
        {
            var entry = entries[body.EntityId];
            if (advance && entry.Error is null)
            {
                var physical = frame.Physics.Bodies.Single(p => p.EntityId == body.EntityId);
                try
                {
                    if (body.Presentation.Diagnostics.Backend == PhysicsPresentationBackend.ContinuousBelt)
                        entry.Motion.UpdateBoundary(body.Presentation, AttachmentShapeGeometry.FromFrame(body.Presentation),
                            step, seconds, body.EntityId == selected ? facing : DangoFacingDirection.Center,
                            new(physical.CoreX, physical.CoreY), body.EntityId == selected ? input.VerticalDeformationAxis : 0);
                    else
                        entry.Motion.Update(body.Presentation, step, seconds,
                            body.EntityId == selected ? facing : DangoFacingDirection.Center,
                            new(physical.CoreX, physical.CoreY), verticalDeformationAxis: body.EntityId == selected ? input.VerticalDeformationAxis : 0);
                    router.ConsumeResults(entry.Target);
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException)
                {
                    entry.Error = $"实体 {body.EntityId} 附件已冻结：{error.Message}";
                    FaultRevision++;
                }
            }
            return body.Presentation with { Attachments = entry.Motion.Current };
        }).ToArray();
        if (advance) step++;
        return result;
    }

    public void Dispose() { router.Dispose(); entries.Clear(); eyeLayouts.Clear(); }
}
