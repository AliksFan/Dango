using System.Runtime.InteropServices;

namespace Dango.Physics;

public sealed partial class DangoPhysicsWorld
{
    public const int FurnitureCollectionAbiVersion = 1;
    public const int FurnitureCollectionMaximumItems = 30;
    public bool SupportsFurnitureCollection
    {
        get
        {
            VerifyAccess();
            try { _ = api.RequireFurnitureCollection(); return true; }
            catch (DangoPhysicsException error) when (error.Kind is DangoPhysicsFailureKind.MissingExport or DangoPhysicsFailureKind.AbiMismatch)
            { return false; }
        }
    }
    public DangoFurnitureProbe ProbeFurnitureChange(DangoFurnitureChangeKind kind, ulong targetInstanceId,
        DangoFurnitureDefinition? candidate, ulong expectedSceneRevision)
    {
        VerifyAccess(); var native = CollectionCandidate(kind, candidate);
        var error = api.RequireFurnitureCollection().Probe(world, (int)kind, targetInstanceId, expectedSceneRevision,
            in native, out var probe, Marshal.SizeOf<NativeCollectionProbe>());
        if (error != 0) throw Map(error, "读取家具集合预览失败。");
        return probe.ToPublic();
    }
    public DangoFurniturePreparation PrepareFurnitureChange(DangoFurnitureChangeKind kind, ulong targetInstanceId,
        DangoFurnitureDefinition? candidate, ulong expectedSceneRevision)
    {
        VerifyAccess(); var native = CollectionCandidate(kind, candidate);
        var error = api.RequireFurnitureCollection().Prepare(world, (int)kind, targetInstanceId, expectedSceneRevision,
            in native, out var probe, Marshal.SizeOf<NativeCollectionProbe>(), out var token);
        if (error != 0) throw Map(error, "准备家具集合变更失败。");
        var result = probe.ToPublic();
        if (result.Allowed && (token.TokenId == 0 || token.WorldGeneration != result.WorldGeneration ||
            token.SceneRevision != result.SceneRevision || token.PhysicsStep != result.PhysicsStep)) throw InvalidFurnitureFrame();
        return new(result, result.Allowed ? token.ToCollection() : null);
    }
    public DangoFurnitureCollectionFrame ApplyPreparedFurnitureChange(DangoPreparedFurnitureChange token)
    {
        VerifyAccess(); var native = NativeFurnitureToken.FromCollection(token);
        var error = api.RequireFurnitureCollection().Apply(world, in native);
        if (error != 0) throw Map(error, "应用家具集合变更失败；许可可能已过期。");
        var frame = ReadFurnitureCollectionFrame();
        if (frame.WorldGeneration != token.WorldGeneration || frame.LastAppliedToken != token.TokenId ||
            frame.SceneRevision != token.SceneRevision + 1 || frame.PhysicsStep != token.PhysicsStep || !frame.Physics.Succeeded)
            throw InvalidFurnitureFrame();
        return frame;
    }
    public void CancelPreparedFurnitureChange(DangoPreparedFurnitureChange token)
    {
        VerifyAccess(); var native = NativeFurnitureToken.FromCollection(token);
        var error = api.RequireFurnitureCollection().Cancel(world, in native);
        if (error != 0) throw Map(error, "家具集合许可不属于当前世界。");
    }
    public unsafe DangoFurnitureCollectionFrame ReadFurnitureCollectionFrame()
    {
        VerifyAccess();
        var error = api.RequireFurnitureCollection().Read(world, out var native, sizeof(NativeCollectionFrame));
        if (error != 0) throw Map(error, "读取家具集合共同安全帧失败。");
        if (native.Reserved != 0 || native.ItemCount is < 0 or > FurnitureCollectionMaximumItems) throw InvalidFurnitureFrame();
        var body = DecodeFurnitureFrame(native.Bodies);
        var items = new DangoFurnitureCollectionItem[native.ItemCount]; var seen = new HashSet<ulong>();
        var rawItems = (NativeCollectionItem*)native.Items;
        for (var i = 0; i < items.Length; i++)
        {
            var raw = rawItems[i];
            if (raw.Instance == 0 || !seen.Add(raw.Instance)) throw InvalidFurnitureFrame();
            var definition = raw.Definition.ToPublic();
            DangoRoundBallState? state = null; DangoRoundBallContact? dango = null, surface = null;
            if (definition.Kind == DangoFurnitureKind.RoundBall)
            {
                var b = raw.Ball;
                if (b.Id <= 0 || (ulong)b.Id != raw.Instance || b.Enabled != 1 || b.Sleeping is < 0 or > 1 ||
                    b.SceneRevision != body.SceneRevision || b.Step != body.PhysicsStep ||
                    !double.IsFinite(b.X) || !double.IsFinite(b.Y) || !double.IsFinite(b.Vx) || !double.IsFinite(b.Vy) ||
                    !double.IsFinite(b.Angle) || !double.IsFinite(b.Omega)) throw InvalidFurnitureFrame();
                state = new(new(b.Id), b.SceneRevision, b.Step, b.X, b.Y, b.Vx, b.Vy, b.Angle, b.Omega, b.Sleeping != 0, b.Integrations);
                dango = raw.Dango.ToPublic(body.SceneRevision, body.PhysicsStep);
                surface = raw.Surface.ToPublic(body.SceneRevision, body.PhysicsStep);
            }
            else if (raw.Ball.Enabled != 0) throw InvalidFurnitureFrame();
            items[i] = new(raw.Instance, definition, state, dango, surface);
        }
        return new(body.WorldGeneration, body.SceneRevision, body.PhysicsStep, body.LastAppliedToken,
            body.Physics, body.BodyDiagnostics, Array.AsReadOnly(items));
    }
    private static NativeCollectionDefinition CollectionCandidate(DangoFurnitureChangeKind kind, DangoFurnitureDefinition? candidate)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == DangoFurnitureChangeKind.Delete)
        {
            if (candidate is not null) throw new ArgumentException("删除请求不能携带家具定义。", nameof(candidate));
            return default;
        }
        ArgumentNullException.ThrowIfNull(candidate); candidate.Validate(); return NativeCollectionDefinition.From(candidate);
    }
    private sealed partial class NativeApi
    {
        private FurnitureCollectionApi? collection;
        internal FurnitureCollectionApi RequireFurnitureCollection() => collection ??= new(library);
        internal sealed class FurnitureCollectionApi
        {
            internal readonly CollectionProbeDelegate Probe;
            internal readonly CollectionPrepareDelegate Prepare;
            internal readonly FurnitureTokenDelegate Apply, Cancel;
            internal readonly CollectionReadDelegate Read;
            internal FurnitureCollectionApi(nint handle)
            {
                var version = Get<GetIntegerDelegate>(handle, "dango_physics_get_furniture_collection_abi_version")();
                var size = Get<GetIntegerDelegate>(handle, "dango_physics_get_furniture_collection_frame_size")();
                if (version != FurnitureCollectionAbiVersion || size != Marshal.SizeOf<NativeCollectionFrame>())
                    throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch, "家具集合 ABI 或帧大小不匹配。");
                Probe = Get<CollectionProbeDelegate>(handle, "dango_physics_world_probe_furniture_change");
                Prepare = Get<CollectionPrepareDelegate>(handle, "dango_physics_world_prepare_furniture_change");
                Apply = Get<FurnitureTokenDelegate>(handle, "dango_physics_world_apply_prepared_furniture_change");
                Cancel = Get<FurnitureTokenDelegate>(handle, "dango_physics_world_cancel_prepared_furniture_change");
                Read = Get<CollectionReadDelegate>(handle, "dango_physics_world_get_furniture_collection_frame");
            }
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int CollectionProbeDelegate(nint handle, int kind, ulong target, ulong revision,
            in NativeCollectionDefinition definition, out NativeCollectionProbe probe, int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int CollectionPrepareDelegate(nint handle, int kind, ulong target, ulong revision,
            in NativeCollectionDefinition definition, out NativeCollectionProbe probe, int size, out NativeFurnitureToken token);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int CollectionReadDelegate(nint handle, out NativeCollectionFrame frame, int size);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCollectionDefinition
    {
        public int Kind, Reserved; public NativeFurniturePlatform Platform; public int Padding; public NativeRoundBallDefinition Ball;
        internal static NativeCollectionDefinition From(DangoFurnitureDefinition value) => new()
        { Kind = (int)value.Kind, Platform = value.Platform is { } p ? NativeFurniturePlatform.From(p) : default,
            Ball = value.Ball is { } b ? NativeRoundBallDefinition.From(b) : default };
        internal readonly DangoFurnitureDefinition ToPublic()
        {
            if (Reserved != 0 || Padding != 0 || Kind is < 1 or > 2) throw InvalidFurnitureFrame();
            var result = new DangoFurnitureDefinition((DangoFurnitureKind)Kind,
                Kind == 1 ? Platform.ToPublic() : null, Kind == 2 ? Ball.ToPublic() : null);
            result.Validate(); return result;
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCollectionItem
    { public ulong Instance; public NativeCollectionDefinition Definition; public NativeRoundBallState Ball; public NativeRoundBallContact Dango, Surface; }
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NativeCollectionFrame
    { public NativeFurnitureFrame Bodies; public int ItemCount, Reserved; public fixed byte Items[400 * FurnitureCollectionMaximumItems]; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCollectionProbe
    {
        public ulong WorldGeneration, SceneRevision, PhysicsStep; public double MinimumGap; public int Rejection, Reserved;
        internal readonly DangoFurnitureProbe ToPublic()
        {
            if (WorldGeneration == 0 || !double.IsFinite(MinimumGap) || Reserved != 0 || Rejection is < 0 or > 9) throw InvalidFurnitureFrame();
            return new(WorldGeneration, SceneRevision, PhysicsStep, MinimumGap, (DangoFurnitureChangeRejection)Rejection);
        }
    }
}
