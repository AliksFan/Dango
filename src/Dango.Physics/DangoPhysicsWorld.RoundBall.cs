using System.Runtime.InteropServices;

namespace Dango.Physics;

public sealed partial class DangoPhysicsWorld
{
    public const int RoundBallAbiVersion = 1;

    /// <summary>Native round-ball gravity multiplier; earlier ball modules use unit gravity.</summary>
    public double RoundBallGravityScale
    {
        get { VerifyAccess(); return api.RequireRoundBall().GravityScale; }
    }

    public bool SupportsRoundBall
    {
        get
        {
            VerifyAccess();
            try { _ = api.RequireRoundBall(); return true; }
            catch (DangoPhysicsException error) when (error.Kind is DangoPhysicsFailureKind.MissingExport or DangoPhysicsFailureKind.AbiMismatch)
            { return false; }
        }
    }

    public DangoRoundBallId AddRoundBall(DangoRoundBallDefinition definition)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        var ballApi = api.RequireRoundBall();
        var native = NativeRoundBallDefinition.From(definition);
        var error = ballApi.Add(world, in native, out var id);
        if (error != 0) throw Map(error, "创建圆球失败；当前与共同出生状态必须合法且不能与平台或多团子冲突。");
        return new(id);
    }

    public void RemoveRoundBall(DangoRoundBallId id)
    {
        VerifyAccess();
        var error = api.RequireRoundBall().Remove(world, id.Value);
        if (error != 0) throw Map(error, "移除圆球失败；标识可能已过期或世界已锁存故障。");
    }

    public DangoRoundBallFrame StepWithRoundBall(TimeSpan fixedStep, IReadOnlyList<DangoPhysicsInput> inputs)
    {
        VerifyAccess();
        _ = api.RequireRoundBall();
        _ = Step(fixedStep, inputs);
        return ReadRoundBallFrame();
    }

    public unsafe DangoRoundBallFrame ReadRoundBallFrame()
    {
        VerifyAccess();
        var error = api.RequireRoundBall().Read(world, out var native, sizeof(NativeRoundBallFrame));
        if (error != 0) throw Map(error, "读取圆球共同安全帧失败。");
        if ((native.ErrorCode == 0 && native.BodyCount != entities.Count) || native.BodyCount < 0 || native.BodyCount > capacity ||
            native.Reserved != 0 || native.ContactSteps < 0)
            throw InvalidRoundBallFrame();
        var bodies = new DangoBodyState[native.BodyCount];
        var ids = new HashSet<DangoEntityId>();
        for (var bodyIndex = 0; bodyIndex < native.BodyCount; bodyIndex++)
        {
            var id = new DangoEntityId(native.EntityIds[bodyIndex]);
            // The original no-ball ABI allows Add/Remove after a latched fault.
            // Its published safe frame retains the historical topology until Reset.
            if (id.Value <= 0 || (native.ErrorCode == 0 && !entities.ContainsKey(id)) || !ids.Add(id))
                throw InvalidRoundBallFrame();
            var offset = bodyIndex * NativeApi.StateFloatCount;
            for (var field = 0; field < NativeApi.StateFloatCount; field++)
                if (!float.IsFinite(native.States[offset + field])) throw InvalidRoundBallFrame();
            var contour = new float[32];
            for (var field = 0; field < contour.Length; field++) contour[field] = native.States[offset + field];
            bodies[bodyIndex] = new(id, Array.AsReadOnly(contour),
                native.States[offset + 32], native.States[offset + 33], native.States[offset + 34],
                native.States[offset + 35], native.States[offset + 36], native.States[offset + 37],
                (int)native.States[offset + 38], native.States[offset + 39] != 0,
                (int)native.States[offset + 40], (int)native.States[offset + 41]);
        }
        var snapshot = native.Snapshot;
        if (snapshot.Ball.SceneRevision != snapshot.SceneRevision || snapshot.Ball.Step != snapshot.Step ||
            snapshot.Ball.Enabled is < 0 or > 1 || snapshot.Ball.Sleeping is < 0 or > 1)
            throw InvalidRoundBallFrame();
        DangoRoundBallDefinition? definition = null;
        DangoRoundBallState? ball = null;
        if (snapshot.Ball.Enabled != 0)
        {
            var state = snapshot.Ball;
            definition = native.Definition.ToPublic();
            definition.Validate();
            if (state.Id <= 0 || !double.IsFinite(state.X) || !double.IsFinite(state.Y) ||
                !double.IsFinite(state.Vx) || !double.IsFinite(state.Vy) ||
                !double.IsFinite(state.Angle) || !double.IsFinite(state.Omega)) throw InvalidRoundBallFrame();
            ball = new(new(state.Id), state.SceneRevision, state.Step,
                state.X, state.Y, state.Vx, state.Vy, state.Angle, state.Omega, state.Sleeping != 0, state.Integrations);
        }
        var physics = new DangoPhysicsFrame(Array.AsReadOnly(bodies), native.ContactSteps, native.ErrorCode);
        return new(snapshot.SceneRevision, snapshot.Step, physics, definition, ball,
            snapshot.DangoContact.ToPublic(snapshot.SceneRevision, snapshot.Step),
            snapshot.FloorContact.ToPublic(snapshot.SceneRevision, snapshot.Step));
    }

    private static DangoPhysicsException InvalidRoundBallFrame() =>
        new(DangoPhysicsFailureKind.NativeFailure, "圆球模块返回了无效或不同版本的共同安全帧。");

    private sealed partial class NativeApi
    {
        private RoundBallApi? roundBall;
        internal RoundBallApi RequireRoundBall() => roundBall ??= new(library);

        internal sealed class RoundBallApi
        {
            internal readonly double GravityScale;
            internal readonly AddRoundBallDelegate Add;
            internal readonly RemoveDelegate Remove;
            internal readonly ReadRoundBallDelegate Read;

            internal RoundBallApi(nint handle)
            {
                var version = Get<GetIntegerDelegate>(handle, "dango_physics_get_round_ball_abi_version")();
                var size = Get<GetIntegerDelegate>(handle, "dango_physics_get_round_ball_frame_size")();
                if (version != RoundBallAbiVersion || size != Marshal.SizeOf<NativeRoundBallFrame>())
                    throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch,
                        $"圆球可选 ABI／帧布局不匹配：需要版本 {RoundBallAbiVersion}、大小 {Marshal.SizeOf<NativeRoundBallFrame>()}，实际 {version}／{size}。");
                GravityScale = GetOptional<RoundBallGravityScaleDelegate>(handle, "dango_physics_get_round_ball_gravity_scale")?.Invoke() ?? 1;
                if (!double.IsFinite(GravityScale) || GravityScale <= 0)
                    throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch, "圆球模块返回无效重力倍率。");
                Add = Get<AddRoundBallDelegate>(handle, "dango_physics_world_add_round_ball");
                Remove = Get<RemoveDelegate>(handle, "dango_physics_world_remove_round_ball");
                Read = Get<ReadRoundBallDelegate>(handle, "dango_physics_world_get_round_ball_frame");
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate double RoundBallGravityScaleDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int AddRoundBallDelegate(nint handle, in NativeRoundBallDefinition definition, out int id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int ReadRoundBallDelegate(nint handle, out NativeRoundBallFrame frame, int size);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRoundBallDefinition
    {
        public double X, Y, Radius, Mass, FloorFriction, DangoFriction, FloorRestitution, DangoRestitution;
        internal static NativeRoundBallDefinition From(DangoRoundBallDefinition value) => new()
        {
            X = value.CenterQX, Y = value.CenterQY, Radius = value.RadiusQ, Mass = value.Mass,
            FloorFriction = value.FloorFriction, DangoFriction = value.DangoFriction,
            FloorRestitution = value.FloorRestitution, DangoRestitution = value.DangoRestitution
        };
        internal readonly DangoRoundBallDefinition ToPublic() => new(X, Y, Radius, Mass,
            FloorFriction, DangoFriction, FloorRestitution, DangoRestitution);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRoundBallState
    {
        public int Id, Enabled, Sleeping;
        public ulong SceneRevision, Step, Integrations;
        public double X, Y, Vx, Vy, Angle, Omega;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRoundBallContact
    {
        public int ObjectId, Touching, Sample;
        public ulong SceneRevision, Step;
        public double Fraction, Nx, Ny, Gap, NormalImpulse, TangentImpulse, NormalSpeedBefore, TangentSpeedBefore, BallSpeedBefore;
        internal readonly DangoRoundBallContact ToPublic(ulong revision, ulong step)
        {
            if (SceneRevision != revision || Step != step || Touching is < 0 or > 1 ||
                !double.IsFinite(Fraction) || !double.IsFinite(Nx) || !double.IsFinite(Ny) || !double.IsFinite(Gap) ||
                !double.IsFinite(NormalImpulse) || !double.IsFinite(TangentImpulse) || !double.IsFinite(NormalSpeedBefore) ||
                !double.IsFinite(TangentSpeedBefore) || !double.IsFinite(BallSpeedBefore)) throw InvalidRoundBallFrame();
            return new(ObjectId, Touching != 0, Sample, SceneRevision, Step,
                Fraction, Nx, Ny, Gap, NormalImpulse, TangentImpulse, NormalSpeedBefore, TangentSpeedBefore, BallSpeedBefore);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRoundBallSnapshot
    {
        public ulong SceneRevision, Step;
        public NativeRoundBallState Ball;
        public NativeRoundBallContact DangoContact, FloorContact;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NativeRoundBallFrame
    {
        public NativeRoundBallSnapshot Snapshot;
        public NativeRoundBallDefinition Definition;
        public int ErrorCode, ContactSteps, BodyCount, Reserved;
        public fixed int EntityIds[3];
        public fixed float States[126];
    }
}
