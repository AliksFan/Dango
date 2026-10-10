using System.Runtime.InteropServices;

namespace Dango.Physics;

[Flags]
public enum DangoAuthorityCapabilities
{
    None = 0,
    SemanticFrames = 1,
    CompleteCheckpoint = 2,
}

public enum DangoAuthorityJumpResult
{
    None = 0,
    AppliedFloor = 1,
    AppliedSupport = 2,
    RejectedAirborne = 3,
    RejectedCooldown = 4,
}

public sealed record DangoAuthorityBodyState(
    DangoEntityId EntityId, DangoEntityId? SupportEntityId, bool FloorGrounded, bool CanJump,
    DangoAuthorityJumpResult JumpResult, float CoreX, float CoreY, float CoreVelocityX, float CoreVelocityY,
    float SupportNormalX, float SupportNormalY, float SupportGap, float CooldownRemainingSeconds);

public sealed record DangoAuthorityContactState(
    DangoEntityId EntityA, DangoEntityId EntityB, bool Touching, bool SupportsA, bool SupportsB,
    float NormalX, float NormalY, float Gap, float MinimumProjectedWidth);

public sealed record DangoAuthorityFrame(
    ulong Tick, ulong SceneRevision, int ErrorCode,
    IReadOnlyList<DangoAuthorityBodyState> Bodies, IReadOnlyList<DangoAuthorityContactState> Pairs)
{
    public bool Succeeded => ErrorCode == 0;
}

public readonly record struct DangoPresentationTarget(
    DangoEntityId EntityId, float CoreX, float CoreY, float CoreVelocityX, float CoreVelocityY);

public sealed partial class DangoPhysicsWorld
{
    public const int AuthorityAbiVersion = 1;
    public const int AuthorityFrameSize = 280;
    private bool authorityEnabled;
    private bool presentationMode;

    public DangoAuthorityCapabilities AuthorityCapabilities
    {
        get
        {
            VerifyAccess();
            return api.ReadAuthorityCapabilities is null ? DangoAuthorityCapabilities.None :
                (DangoAuthorityCapabilities)api.ReadAuthorityCapabilities();
        }
    }

    public void EnableAuthority()
    {
        VerifyAccess();
        VerifyAuthorityAbi();
        var call = api.EnableAuthority ?? throw MissingLocalExport("dango_physics_world_enable_authority");
        var error = call(world);
        if (error != 0) throw Map(error, "启用权威语义失败：仅支持无家具双体世界，且须在首次步进前启用。");
        authorityEnabled = true;
    }

    public DangoAuthorityFrame ReadAuthorityFrame()
    {
        VerifyAccess();
        VerifyAuthorityAbi();
        var call = api.ReadAuthorityFrame ?? throw MissingLocalExport("dango_physics_world_get_authority_frame");
        var native = new NativeAuthorityFrame { Bodies = new NativeAuthorityBody[3], Pairs = new NativeAuthorityPair[3] };
        var error = call(world, ref native, AuthorityFrameSize);
        if (error != 0) throw Map(error, "读取权威语义失败。");
        ValidateAuthorityFrame(native);
        var bodies = native.Bodies.Take(native.BodyCount).Select(body => new DangoAuthorityBodyState(
            new(body.EntityId), body.SupportEntityId == 0 ? null : new DangoEntityId(body.SupportEntityId),
            (body.Flags & 1) != 0, (body.Flags & 4) != 0, (DangoAuthorityJumpResult)body.JumpResult,
            body.CoreX, body.CoreY, body.CoreVelocityX, body.CoreVelocityY,
            body.SupportNormalX, body.SupportNormalY, body.SupportGap, body.CooldownRemainingSeconds)).ToArray();
        var pairs = native.Pairs.Take(native.PairCount).Select(pair => new DangoAuthorityContactState(
            new(pair.EntityA), new(pair.EntityB), (pair.Flags & 1) != 0, (pair.Flags & 2) != 0, (pair.Flags & 4) != 0,
            pair.NormalX, pair.NormalY, pair.Gap, pair.MinimumProjectedWidth)).ToArray();
        return new(native.Tick, native.SceneRevision, native.ErrorCode, bodies, pairs);
    }

    public byte[] ExportCheckpoint()
    {
        VerifyAccess();
        var call = api.ExportCheckpoint ?? throw MissingLocalExport("dango_physics_world_export_checkpoint");
        var error = call(world, 0, 0, out var written);
        if (error != 0) throw Map(error, "当前固定 solver 的完整 checkpoint 恢复能力尚未准入。");
        if (written != 0) throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch, "未协商 checkpoint 格式。");
        return [];
    }

    public void ImportCheckpoint(ReadOnlySpan<byte> checkpoint)
    {
        VerifyAccess();
        if (checkpoint.Length > 65536) throw new ArgumentOutOfRangeException(nameof(checkpoint));
        var call = api.ImportCheckpoint ?? throw MissingLocalExport("dango_physics_world_import_checkpoint");
        var error = call(world, checkpoint.ToArray(), checkpoint.Length);
        if (error != 0) throw Map(error, "当前固定 solver 的完整 checkpoint 恢复能力尚未准入；原世界保留。");
    }

    public void ApplyPresentationTargets(IReadOnlyList<DangoPresentationTarget> targets)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(targets);
        if (authorityEnabled) throw Map(-421, "权威世界不能接受客户端呈现目标。");
        if (targets.Count != entities.Count || targets.Count is < 1 or > 2 ||
            targets.Select(value => value.EntityId).Distinct().Count() != targets.Count ||
            targets.Any(value => !entities.ContainsKey(value.EntityId)))
            throw Map(-412, "呈现目标必须一次覆盖全部有效实体，不能缺失、重复或使用旧身份。");
        foreach (var target in targets)
            if (!float.IsFinite(target.CoreX) || !float.IsFinite(target.CoreY) ||
                !float.IsFinite(target.CoreVelocityX) || !float.IsFinite(target.CoreVelocityY))
                throw new ArgumentOutOfRangeException(nameof(targets));
        var size = Marshal.SizeOf<NativePresentationTarget>();
        if (size != 20 || api.ReadPresentationTargetSize?.Invoke() != size)
            throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch, "呈现目标 ABI 不匹配。");
        var call = api.ApplyPresentationTargets ?? throw MissingLocalExport("dango_physics_world_apply_presentation_targets");
        var native = targets.Select(value => new NativePresentationTarget
        {
            EntityId = value.EntityId.Value, CoreX = value.CoreX, CoreY = value.CoreY,
            CoreVelocityX = value.CoreVelocityX, CoreVelocityY = value.CoreVelocityY,
        }).ToArray();
        var error = call(world, native, native.Length, size);
        if (error != 0) throw Map(error, "应用整组呈现目标失败；输入组未部分应用。");
        presentationMode = true;
    }

    private void VerifyAuthorityAbi()
    {
        if (api.ReadAuthorityAbi?.Invoke() != AuthorityAbiVersion ||
            api.ReadAuthorityFrameSize?.Invoke() != AuthorityFrameSize || Marshal.SizeOf<NativeAuthorityFrame>() != AuthorityFrameSize)
            throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch, "模块不支持匹配的可选权威语义 ABI。");
    }

    private static void ValidateAuthorityFrame(NativeAuthorityFrame frame)
    {
        static bool Finite(params float[] values) => values.All(float.IsFinite);
        static void Invalid() => throw new DangoPhysicsException(DangoPhysicsFailureKind.NativeFailure, "权威语义帧违反身份、有限值或跨字段合同。");
        if (frame.AbiVersion != AuthorityAbiVersion || frame.FrameSize != AuthorityFrameSize || frame.Reserved != 0 ||
            frame.BodyCount is < 0 or > 2 || frame.PairCount is < 0 or > 1) Invalid();
        var ids = frame.Bodies.Take(frame.BodyCount).Select(value => value.EntityId).ToHashSet();
        if (ids.Count != frame.BodyCount || ids.Any(value => value <= 0)) Invalid();
        for (var index = 0; index < frame.BodyCount; index++)
        {
            var body = frame.Bodies[index];
            if ((body.Flags & ~7u) != 0 || body.JumpResult > 4 || body.CooldownRemainingSeconds < 0 ||
                !Finite(body.CoreX, body.CoreY, body.CoreVelocityX, body.CoreVelocityY,
                    body.SupportNormalX, body.SupportNormalY, body.SupportGap, body.CooldownRemainingSeconds) ||
                ((body.Flags & 2) != 0) != (body.SupportEntityId != 0) ||
                body.SupportEntityId != 0 && (!ids.Contains(body.SupportEntityId) || body.SupportEntityId == body.EntityId) ||
                (body.Flags & 4) != 0 && ((body.Flags & 3) == 0 || body.CooldownRemainingSeconds != 0)) Invalid();
        }
        for (var index = 0; index < frame.PairCount; index++)
        {
            var pair = frame.Pairs[index];
            if (pair.EntityA == pair.EntityB || !ids.Contains(pair.EntityA) || !ids.Contains(pair.EntityB) ||
                pair.Reserved != 0 || (pair.Flags & ~7u) != 0 || (pair.Flags & 1) == 0 ||
                !Finite(pair.NormalX, pair.NormalY, pair.Gap, pair.MinimumProjectedWidth) || pair.MinimumProjectedWidth <= 0 ||
                Math.Abs(pair.NormalX * pair.NormalX + pair.NormalY * pair.NormalY - 1) > .0002) Invalid();
            var a = frame.Bodies.First(value => value.EntityId == pair.EntityA);
            var b = frame.Bodies.First(value => value.EntityId == pair.EntityB);
            if (((pair.Flags & 2) != 0) != (a.SupportEntityId == pair.EntityB) ||
                ((pair.Flags & 4) != 0) != (b.SupportEntityId == pair.EntityA)) Invalid();
        }
        if (frame.Bodies.Skip(frame.BodyCount).Any(value => !value.Equals(default(NativeAuthorityBody))) ||
            frame.Pairs.Skip(frame.PairCount).Any(value => !value.Equals(default(NativeAuthorityPair)))) Invalid();
    }

    private sealed partial class NativeApi
    {
        internal GetIntegerDelegate? ReadAuthorityAbi, ReadAuthorityFrameSize, ReadAuthorityCapabilities, ReadPresentationTargetSize;
        internal ResetDelegate? EnableAuthority;
        internal AuthorityFrameDelegate? ReadAuthorityFrame;
        internal ExportCheckpointDelegate? ExportCheckpoint;
        internal ImportCheckpointDelegate? ImportCheckpoint;
        internal PresentationTargetsDelegate? ApplyPresentationTargets;
        private void LoadAuthorityExports()
        {
            ReadAuthorityAbi = GetOptional<GetIntegerDelegate>(library, "dango_physics_get_authority_abi_version");
            ReadAuthorityFrameSize = GetOptional<GetIntegerDelegate>(library, "dango_physics_get_authority_frame_size");
            ReadAuthorityCapabilities = GetOptional<GetIntegerDelegate>(library, "dango_physics_get_authority_capabilities");
            EnableAuthority = GetOptional<ResetDelegate>(library, "dango_physics_world_enable_authority");
            ReadAuthorityFrame = GetOptional<AuthorityFrameDelegate>(library, "dango_physics_world_get_authority_frame");
            ExportCheckpoint = GetOptional<ExportCheckpointDelegate>(library, "dango_physics_world_export_checkpoint");
            ImportCheckpoint = GetOptional<ImportCheckpointDelegate>(library, "dango_physics_world_import_checkpoint");
            ReadPresentationTargetSize = GetOptional<GetIntegerDelegate>(library, "dango_physics_get_presentation_target_size");
            ApplyPresentationTargets = GetOptional<PresentationTargetsDelegate>(library, "dango_physics_world_apply_presentation_targets");
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int AuthorityFrameDelegate(nint handle, ref NativeAuthorityFrame frame, int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int ExportCheckpointDelegate(nint handle, nint buffer, int capacity, out int written);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int ImportCheckpointDelegate(nint handle, [In] byte[] buffer, int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int PresentationTargetsDelegate(nint handle, [In] NativePresentationTarget[] targets, int count, int size);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAuthorityBody
    {
        public int EntityId, SupportEntityId;
        public uint Flags, JumpResult;
        public float CoreX, CoreY, CoreVelocityX, CoreVelocityY;
        public float SupportNormalX, SupportNormalY, SupportGap, CooldownRemainingSeconds;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAuthorityPair
    {
        public int EntityA, EntityB;
        public uint Flags, Reserved;
        public float NormalX, NormalY, Gap, MinimumProjectedWidth;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAuthorityFrame
    {
        public uint AbiVersion, FrameSize;
        public ulong Tick, SceneRevision;
        public int ErrorCode, BodyCount, PairCount, Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public NativeAuthorityBody[] Bodies;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public NativeAuthorityPair[] Pairs;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePresentationTarget
    {
        public int EntityId;
        public float CoreX, CoreY, CoreVelocityX, CoreVelocityY;
    }
}
