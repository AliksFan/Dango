using System.Runtime.InteropServices;

namespace Dango.Physics;

public sealed partial class DangoPhysicsWorld
{
    public const int FurnitureAbiVersion = 1;
    public const double FurnitureAdmissionEpsilon = .000001;

    public bool SupportsLiveFurnitureEditing
    {
        get
        {
            VerifyAccess();
            try { _ = api.RequireFurniture(); return true; }
            catch (DangoPhysicsException error) when (error.Kind is DangoPhysicsFailureKind.MissingExport or DangoPhysicsFailureKind.AbiMismatch)
            { return false; }
        }
    }

    public DangoStaticPlatformProbe ProbeStaticPlatformChange(DangoStaticPlatformChangeKind kind,
        ulong targetInstanceId, DangoStaticPlatformDefinition? candidate, ulong expectedSceneRevision)
    {
        VerifyAccess();
        var native = FurnitureCandidate(kind, candidate);
        var error = api.RequireFurniture().Probe(world, (int)kind, targetInstanceId, expectedSceneRevision,
            in native, out var result, Marshal.SizeOf<NativeFurnitureProbe>());
        if (error != 0) throw Map(error, "读取家具碰撞预览失败；真实家具未改变。");
        return result.ToPublic();
    }

    public DangoStaticPlatformPreparation PrepareStaticPlatformChange(DangoStaticPlatformChangeKind kind,
        ulong targetInstanceId, DangoStaticPlatformDefinition? candidate, ulong expectedSceneRevision)
    {
        VerifyAccess();
        var native = FurnitureCandidate(kind, candidate);
        var error = api.RequireFurniture().Prepare(world, (int)kind, targetInstanceId, expectedSceneRevision,
            in native, out var result, Marshal.SizeOf<NativeFurnitureProbe>(), out var token);
        if (error != 0) throw Map(error, "准备家具变更失败；真实家具未改变。");
        var probe = result.ToPublic();
        if (probe.Allowed && (token.TokenId == 0 || token.WorldGeneration != probe.WorldGeneration ||
            token.SceneRevision != probe.SceneRevision || token.PhysicsStep != probe.PhysicsStep)) throw InvalidFurnitureFrame();
        return new(probe, probe.Allowed ? token.ToPublic() : null);
    }

    public DangoFurnitureFrame ApplyPreparedStaticPlatformChange(DangoPreparedStaticPlatformChange token)
    {
        VerifyAccess();
        var native = NativeFurnitureToken.From(token);
        var error = api.RequireFurniture().Apply(world, in native);
        if (error != 0) throw Map(error, "应用家具变更失败；准备许可可能已因步进、取消或其他场景变化失效。");
        var frame = ReadFurnitureFrame();
        if (frame.WorldGeneration != token.WorldGeneration || frame.LastAppliedToken != token.TokenId ||
            frame.SceneRevision != token.SceneRevision + 1 || frame.PhysicsStep != token.PhysicsStep || !frame.Physics.Succeeded)
            throw InvalidFurnitureFrame();
        StaticPlatform = frame.Platform;
        return frame;
    }

    public void CancelPreparedStaticPlatformChange(DangoPreparedStaticPlatformChange token)
    {
        VerifyAccess();
        var native = NativeFurnitureToken.From(token);
        var error = api.RequireFurniture().Cancel(world, in native);
        if (error != 0) throw Map(error, "取消家具准备失败；许可不属于当前物理世界。");
    }

    public unsafe DangoFurnitureFrame ReadFurnitureFrame()
    {
        VerifyAccess();
        var error = api.RequireFurniture().Read(world, out var native, sizeof(NativeFurnitureFrame));
        if (error != 0) throw Map(error, "读取家具与团子的共同状态失败。");
        return DecodeFurnitureFrame(native);
    }

    private unsafe DangoFurnitureFrame DecodeFurnitureFrame(NativeFurnitureFrame native)
    {
        if (native.WorldGeneration == 0 || native.Reserved != 0 || native.AlignmentPadding != 0 ||
            native.PlatformEnabled is < 0 or > 1 || native.BodyCount < 0 || native.BodyCount > capacity ||
            (native.ErrorCode == 0 && native.BodyCount != entities.Count) || native.ContactSteps < 0 ||
            (native.PlatformEnabled == 0) != (native.PlatformInstance == 0)) throw InvalidFurnitureFrame();
        DangoStaticPlatformDefinition? platform = native.PlatformEnabled != 0 ? native.Platform.ToPublic() : null;
        platform?.Validate();
        var bodies = new DangoBodyState[native.BodyCount];
        var diagnostics = new DangoFurnitureBodyDiagnostics[native.BodyCount];
        var seen = new HashSet<DangoEntityId>();
        for (var index = 0; index < native.BodyCount; index++)
        {
            var id = new DangoEntityId(native.EntityIds[index]);
            if (id.Value <= 0 || !seen.Add(id) || (native.ErrorCode == 0 && !entities.ContainsKey(id))) throw InvalidFurnitureFrame();
            var offset = index * NativeApi.StateFloatCount;
            for (var field = 0; field < NativeApi.StateFloatCount; field++)
                if (!float.IsFinite(native.States[offset + field])) throw InvalidFurnitureFrame();
            var contour = new float[32];
            for (var field = 0; field < contour.Length; field++) contour[field] = native.States[offset + field];
            bodies[index] = new(id, Array.AsReadOnly(contour), native.States[offset + 32], native.States[offset + 33],
                native.States[offset + 34], native.States[offset + 35], native.States[offset + 36], native.States[offset + 37],
                (int)native.States[offset + 38], native.States[offset + 39] != 0,
                (int)native.States[offset + 40], (int)native.States[offset + 41]);
            var nodes = new DangoFurnitureNodeState[17];
            for (var n = 0; n < nodes.Length; n++)
            {
                var rawOffset = index * 68 + n * 4;
                for (var field = 0; field < 4; field++)
                    if (!float.IsFinite(native.RawPositionsVelocities[rawOffset + field])) throw InvalidFurnitureFrame();
                nodes[n] = new(native.RawBodyIds[index * 17 + n], native.RawPositionsVelocities[rawOffset],
                    native.RawPositionsVelocities[rawOffset + 1], native.RawPositionsVelocities[rawOffset + 2],
                    native.RawPositionsVelocities[rawOffset + 3]);
            }
            var contactOffset = index * 6;
            for (var field = 0; field < 6; field++)
                if (!float.IsFinite(native.Contacts[contactOffset + field])) throw InvalidFurnitureFrame();
            if (!float.IsFinite(native.SecondsSinceJump[index]) || native.DragActive[index] is < 0 or > 1 ||
                native.Grounded[index] is < 0 or > 1) throw InvalidFurnitureFrame();
            diagnostics[index] = new(id, Array.AsReadOnly(nodes), native.SecondsSinceJump[index],
                native.DragActive[index] != 0, native.Grounded[index] != 0,
                new(native.Contacts[contactOffset] != 0, native.Contacts[contactOffset + 1] != 0,
                    native.Contacts[contactOffset + 2] != 0, native.Contacts[contactOffset + 3],
                    native.Contacts[contactOffset + 4], native.Contacts[contactOffset + 5]));
        }
        return new(native.WorldGeneration, native.SceneRevision, native.PhysicsStep, native.PlatformInstance,
            native.LastAppliedToken, platform, new(Array.AsReadOnly(bodies), native.ContactSteps, native.ErrorCode),
            Array.AsReadOnly(diagnostics));
    }

    private static NativeFurniturePlatform FurnitureCandidate(DangoStaticPlatformChangeKind kind, DangoStaticPlatformDefinition? candidate)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == DangoStaticPlatformChangeKind.Delete)
        {
            if (candidate is not null) throw new ArgumentException("删除请求不能携带家具定义。", nameof(candidate));
            return default;
        }
        ArgumentNullException.ThrowIfNull(candidate);
        candidate.Validate();
        return NativeFurniturePlatform.From(candidate);
    }

    private static DangoPhysicsException InvalidFurnitureFrame() =>
        new(DangoPhysicsFailureKind.NativeFailure, "家具模块返回无效许可、状态或健康观测。");

    private sealed partial class NativeApi
    {
        private FurnitureApi? furniture;
        internal FurnitureApi RequireFurniture() => furniture ??= new(library);
        internal sealed class FurnitureApi
        {
            internal readonly FurnitureProbeDelegate Probe;
            internal readonly FurniturePrepareDelegate Prepare;
            internal readonly FurnitureTokenDelegate Apply, Cancel;
            internal readonly FurnitureReadDelegate Read;
            internal FurnitureApi(nint handle)
            {
                var version = Get<GetIntegerDelegate>(handle, "dango_physics_get_furniture_abi_version")();
                var size = Get<GetIntegerDelegate>(handle, "dango_physics_get_furniture_frame_size")();
                if (version != FurnitureAbiVersion || size != Marshal.SizeOf<NativeFurnitureFrame>())
                    throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch,
                        $"家具可选 ABI／状态大小不匹配：需要 {FurnitureAbiVersion}／{Marshal.SizeOf<NativeFurnitureFrame>()}，实际 {version}／{size}。");
                Probe = Get<FurnitureProbeDelegate>(handle, "dango_physics_world_probe_static_platform_change");
                Prepare = Get<FurniturePrepareDelegate>(handle, "dango_physics_world_prepare_static_platform_change");
                Apply = Get<FurnitureTokenDelegate>(handle, "dango_physics_world_apply_prepared_static_platform_change");
                Cancel = Get<FurnitureTokenDelegate>(handle, "dango_physics_world_cancel_prepared_static_platform_change");
                Read = Get<FurnitureReadDelegate>(handle, "dango_physics_world_get_furniture_frame");
            }
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int FurnitureProbeDelegate(nint handle, int kind, ulong target, ulong revision,
            in NativeFurniturePlatform candidate, out NativeFurnitureProbe result, int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int FurniturePrepareDelegate(nint handle, int kind, ulong target, ulong revision,
            in NativeFurniturePlatform candidate, out NativeFurnitureProbe result, int size, out NativeFurnitureToken token);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int FurnitureTokenDelegate(nint handle, in NativeFurnitureToken token);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int FurnitureReadDelegate(nint handle, out NativeFurnitureFrame frame, int size);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFurniturePlatform
    {
        public float X, Y, Width, Height, Friction;
        internal static NativeFurniturePlatform From(DangoStaticPlatformDefinition p) => new()
        { X = (float)p.CenterX, Y = (float)p.CenterY, Width = (float)p.Width, Height = (float)p.Height, Friction = (float)p.Friction };
        internal readonly DangoStaticPlatformDefinition ToPublic() => new(X, Y, Width, Height, Friction);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFurnitureToken
    {
        public ulong WorldGeneration, TokenId, SceneRevision, PhysicsStep;
        internal static NativeFurnitureToken From(DangoPreparedStaticPlatformChange p) => new()
        { WorldGeneration = p.WorldGeneration, TokenId = p.TokenId, SceneRevision = p.SceneRevision, PhysicsStep = p.PhysicsStep };
        internal readonly DangoPreparedStaticPlatformChange ToPublic() => new(WorldGeneration, TokenId, SceneRevision, PhysicsStep);
        internal static NativeFurnitureToken FromCollection(DangoPreparedFurnitureChange p) => new()
        { WorldGeneration = p.WorldGeneration, TokenId = p.TokenId, SceneRevision = p.SceneRevision, PhysicsStep = p.PhysicsStep };
        internal readonly DangoPreparedFurnitureChange ToCollection() => new(WorldGeneration, TokenId, SceneRevision, PhysicsStep);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFurnitureProbe
    {
        public ulong WorldGeneration, SceneRevision, PhysicsStep;
        public float MinimumGap;
        public int Rejection;
        internal readonly DangoStaticPlatformProbe ToPublic()
        {
            if (WorldGeneration == 0 || !float.IsFinite(MinimumGap) || Rejection is < 0 or > 8) throw InvalidFurnitureFrame();
            return new(WorldGeneration, SceneRevision, PhysicsStep, MinimumGap, (DangoStaticPlatformChangeRejection)Rejection);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NativeFurnitureFrame
    {
        public ulong WorldGeneration, SceneRevision, PhysicsStep, PlatformInstance, LastAppliedToken;
        public NativeFurniturePlatform Platform;
        public int PlatformEnabled, ErrorCode, ContactSteps, BodyCount, Reserved;
        public fixed int EntityIds[3];
        public fixed float States[126], Contacts[18];
        public int AlignmentPadding;
        public fixed ulong RawBodyIds[51];
        public fixed float RawPositionsVelocities[204], SecondsSinceJump[3];
        public fixed int DragActive[3], Grounded[3];
    }
}
