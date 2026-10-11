using System.Security.Cryptography;
using Dango.Physics;
using Dango.Physics.Configuration;
using Dango.Physics.Presentation;

namespace Dango.SafeSpawnAdmission;

public sealed record SpawnSlot(Guid PetId, double X, double Y);
public static class SpawnPlanner
{
    // Each multiply/add rounds as native float; one outward float ULP covers
    // rounding before conversion to double. No tunable production parameters.
    public static double EnvelopeRadius => MathF.BitIncrement(
        (float)DangoPhysicsDefaults.Current.RadialRestLength *
        (float)DangoPhysicsDefaults.Current.RadialMaximumRatio + (float)DangoPhysicsDefaults.Current.NodeRadius);
    public static SpawnSlot[] Plan(IEnumerable<Guid> members) => members.OrderBy(id => id.ToString("D"), StringComparer.Ordinal)
        .Select((id, index) => new SpawnSlot(id, index == 0 ? -1.4 : 1.4, 0)).ToArray();
    public static bool IsSafe(IReadOnlyList<SpawnSlot> slots, double left = -13.2f, double right = 13.2f)
    {
        if (!double.IsFinite(left) || !double.IsFinite(right) || left >= right ||
            slots.Count is < 1 or > 2 || slots.Select(s => s.PetId).Distinct().Count() != slots.Count ||
            slots.Any(s => s.PetId == Guid.Empty || !double.IsFinite(s.X) || !double.IsFinite(s.Y))) return false;
        var r = EnvelopeRadius;
        foreach (var s in slots)
            foreach(var position in new[]{(X:s.X,Y:s.Y),(X:(double)(float)s.X,Y:(double)(float)s.Y)})
                if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) ||
                    position.X - r <= left || position.X + r >= right ||
                    position.Y + (float)DangoPhysicsDefaults.Current.DropHeight - r <= (float)DangoPhysicsDefaults.Current.GroundHeight) return false;
        for (var i = 0; i < slots.Count; i++) for (var j = i + 1; j < slots.Count; j++)
            if (Math.Sqrt(Math.Pow(slots[i].X - slots[j].X, 2) + Math.Pow(slots[i].Y - slots[j].Y, 2)) <= 2 * r ||
                Math.Sqrt(Math.Pow((double)(float)slots[i].X-(float)slots[j].X,2)+Math.Pow((double)(float)slots[i].Y-(float)slots[j].Y,2)) <= 2*r) return false;
        return true;
    }
    public static bool CanAdd(SpawnSlot slot, DangoPhysicsFrame current)
    {
        if (!current.Succeeded || current.Bodies.Count >= 2 || !IsSafe([slot]) ||
            current.Bodies.Any(body=>!SemanticDrawnGeometry.IsSafeLocalContour(body))) return false;
        var r = EnvelopeRadius;
        var x=(double)(float)slot.X;
        var y = (float)slot.Y + (float)DangoPhysicsDefaults.Current.DropHeight;
        // Actual current raw nodes plus node radius, with a conservative circle.
        return current.Bodies.All(body => Enumerable.Range(0, body.PointCount).All(n =>
            Math.Sqrt(Math.Pow(x - (body.CoreX + body.Contour[2*n]), 2) +
                Math.Pow(y - (body.CoreY + body.Contour[2*n+1]), 2)) > r + (float)DangoPhysicsDefaults.Current.NodeRadius));
    }
}

public sealed class NativeCounts
{
    public int Created { get; set; }
    public int Disposed { get; set; }
    public int Reset { get; set; }
    public int Add { get; set; }
    public int Remove { get; set; }
    public int Steps { get; set; }
}
public sealed record CommonFrame(DangoAuthorityFrame Authority, DangoPhysicsFrame Raw);
public sealed class NativeRecoveryOwner : IDisposable
{
    public static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly DangoPhysicsWorld world;
    private readonly NativeCounts counts;
    private bool disposed;
    public List<DangoEntityId> Ids { get; } = [];
    public Guid Epoch { get; }
    public CommonFrame? LastSafeFrame { get; private set; }
    public bool FaultLatched { get; private set; }
    public NativeRecoveryOwner(string dll, NativeCounts counts, int capacity = 2, Guid? epoch = null)
    {
        this.counts = counts;
        Epoch = epoch ?? Guid.NewGuid();
        world = DangoPhysicsWorld.Create(dll, capacity); counts.Created++;
        try { world.EnableAuthority(); world.SetHorizontalBounds(-13.2, 13.2); }
        catch { Dispose(); throw; }
    }
    public void Add(double x, double y) { CheckThread(); Ids.Add(world.Add(x,y)); counts.Add++; }
    public void Remove(DangoEntityId id) { CheckThread(); world.Remove(id); Ids.Remove(id); counts.Remove++; }
    public CommonFrame Read()
    {
        CheckThread();
        var a = world.ReadAuthorityFrame(); var r = world.ReadRoundBallFrame();
        if (a.Tick != r.Step || a.SceneRevision != r.SceneRevision) throw new InvalidOperationException("Mixed native frame.");
        return new(a,r.Physics);
    }
    public CommonFrame Step(Func<DangoEntityId,int,DangoPhysicsInput>? input = null)
    {
        CheckThread();
        if (FaultLatched) throw new InvalidOperationException("Fault remains latched.");
        try
        {
            world.Step(Dt, Ids.Select((id,index) => input?.Invoke(id,index) ?? new(id)).ToArray()); counts.Steps++;
            var frame = Read();
            if (!NativeFrameVerifier.Healthy(frame, Ids)) { FaultLatched = true; throw new InvalidOperationException("Unsafe common frame."); }
            LastSafeFrame = frame; return frame;
        }
        catch { FaultLatched = true; throw; }
    }
    public void WrongResetForMutation() { CheckThread(); world.Reset(); counts.Reset++; }
    public bool CheckpointUnsupported()
    {
        CheckThread(); var before = NativeFrameVerifier.Digest(Read());
        try { world.ExportCheckpoint(); return false; } catch (DangoPhysicsException e) when(e.Kind == DangoPhysicsFailureKind.UnsupportedCapability) { }
        try { world.ImportCheckpoint([1,2,3]); return false; } catch (DangoPhysicsException e) when(e.Kind == DangoPhysicsFailureKind.UnsupportedCapability) { }
        return before == NativeFrameVerifier.Digest(Read());
    }
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Unique owner thread required.");
        ObjectDisposedException.ThrowIf(disposed,this);
    }
    public void Dispose() { if(disposed) return; CheckThread(); world.Dispose(); disposed = true; counts.Disposed++; }
}

// Independent assertions consume the actual read-only raw and semantic frame.
public static class NativeFrameVerifier
{
    // Pinned Box2D 3.1.1 LINEAR_SLOP=.005f*units. Production never changes
    // units from 1. The existing native bounds/rig-containment gates use this
    // same tolerance; it is separate from .031 body penetration and .008 support.
    public const float NativeContainmentSlop = .005f;
    public static bool TickZero(CommonFrame f, IReadOnlyList<DangoEntityId> ids) =>
        f.Authority.Tick == 0 && f.Authority.Bodies.Count == ids.Count &&
        f.Authority.Bodies.All(b => b.CoreVelocityX == 0 && b.CoreVelocityY == 0 &&
            b.SupportEntityId is null && b.CooldownRemainingSeconds == 0) && Healthy(f,ids);
    public static bool Healthy(CommonFrame f, IReadOnlyList<DangoEntityId> ids)
    {
        if (!f.Authority.Succeeded || !f.Raw.Succeeded ||
            !f.Authority.Bodies.Select(b=>b.EntityId).ToHashSet().SetEquals(ids) ||
            !f.Raw.Bodies.Select(b=>b.EntityId).ToHashSet().SetEquals(ids)) return false;
        foreach(var body in f.Raw.Bodies)
        {
            if (!SemanticDrawnGeometry.IsSafeLocalContour(body)) return false;
            var a=f.Authority.Bodies.Single(b=>b.EntityId==body.EntityId);
            if (a.CoreX != body.CoreX || a.CoreY != body.CoreY) return false;
            for(int node=0;node<body.PointCount;node++)
            {
                // Native raw contour already expands nodePos-core radially by
                // NodeRadius. Use Core+Contour; adding radius again is wrong.
                double x=(double)body.CoreX+body.Contour[2*node];
                double y=(double)body.CoreY+body.Contour[2*node+1];
                if(x < -13.2f-NativeContainmentSlop || x > 13.2f+NativeContainmentSlop ||
                    y < (float)DangoPhysicsDefaults.Current.GroundHeight-NativeContainmentSlop) return false;
            }
            var drawn=SemanticDrawnGeometry.SampleContour(body.Contour,body.CoreX,body.CoreY);
            if(drawn.Any(p=>p.X < -13.2-1e-5 || p.X > 13.2+1e-5)) return false;
        }
        for(var i=0;i<f.Raw.Bodies.Count;i++) for(var j=i+1;j<f.Raw.Bodies.Count;j++)
        {
            var a=f.Raw.Bodies[i]; var b=f.Raw.Bodies[j];
            var c=SemanticDrawnGeometry.Contact(SemanticDrawnGeometry.SampleContour(a.Contour,a.CoreX,a.CoreY),
                SemanticDrawnGeometry.SampleContour(b.Contour,b.CoreX,b.CoreY));
            if(c.PenetrationRatio > .031) return false;
            if(f.Authority.Bodies.Any(v => v.EntityId==a.EntityId && v.SupportEntityId==b.EntityId ||
                v.EntityId==b.EntityId && v.SupportEntityId==a.EntityId) && c.Gap > .008) return false;
        }
        return true;
    }
    private static byte[] ExactBytes(CommonFrame f)
    {
        using var stream=new MemoryStream(); using var w=new BinaryWriter(stream);
        var a=f.Authority; w.Write(a.Tick);w.Write(a.SceneRevision);w.Write(a.ErrorCode);w.Write(a.Bodies.Count);
        foreach(var b in a.Bodies)
        {
            w.Write(b.EntityId.Value);w.Write(b.SupportEntityId?.Value??0);w.Write(b.FloorGrounded);w.Write(b.CanJump);w.Write((int)b.JumpResult);
            foreach(var v in new[]{b.CoreX,b.CoreY,b.CoreVelocityX,b.CoreVelocityY,b.SupportNormalX,b.SupportNormalY,b.SupportGap,b.CooldownRemainingSeconds})w.Write(BitConverter.SingleToInt32Bits(v));
        }
        w.Write(a.Pairs.Count);foreach(var p in a.Pairs)
        {
            w.Write(p.EntityA.Value);w.Write(p.EntityB.Value);w.Write(p.Touching);w.Write(p.SupportsA);w.Write(p.SupportsB);
            foreach(var v in new[]{p.NormalX,p.NormalY,p.Gap,p.MinimumProjectedWidth})w.Write(BitConverter.SingleToInt32Bits(v));
        }
        w.Write(f.Raw.ErrorCode);w.Write(f.Raw.ContactStepCount);w.Write(f.Raw.Bodies.Count);
        foreach(var b in f.Raw.Bodies)
        {
            w.Write(b.EntityId.Value);w.Write(b.Contour.Count);foreach(var v in b.Contour)w.Write(BitConverter.SingleToInt32Bits(v));
            foreach(var v in new[]{b.CoreX,b.CoreY,b.SignedArea,b.CentroidX,b.CentroidY,b.MaximumSpeed})w.Write(BitConverter.SingleToInt32Bits(v));
            w.Write(b.GroundContactCount);w.Write(b.IsGrounded);w.Write(b.SelectedDragNode);w.Write(b.PointCount);
        }
        w.Flush();return stream.ToArray();
    }
    public static string Digest(CommonFrame f) => Convert.ToHexString(SHA256.HashData(ExactBytes(f)));
    public static bool BitEqual(CommonFrame a, CommonFrame b) => ExactBytes(a).AsSpan().SequenceEqual(ExactBytes(b));
}
