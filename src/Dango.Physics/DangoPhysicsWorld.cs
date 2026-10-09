using System.IO;
using System.Runtime.InteropServices;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics.Configuration;

namespace Dango.Physics;

public readonly record struct DangoEntityId(int Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct DangoPhysicsInput(
    DangoEntityId EntityId,
    double Horizontal = 0,
    double Downward = 0,
    bool JumpPressed = false,
    bool DragActive = false,
    double DragTargetX = 0,
    double DragTargetY = 0);

public sealed record DangoBodyState(
    DangoEntityId EntityId,
    IReadOnlyList<float> Contour,
    float CoreX,
    float CoreY,
    float SignedArea,
    float CentroidX,
    float CentroidY,
    float MaximumSpeed,
    int GroundContactCount,
    bool IsGrounded,
    int SelectedDragNode,
    int PointCount);

public sealed record DangoPhysicsFrame(
    IReadOnlyList<DangoBodyState> Bodies,
    int ContactStepCount,
    int ErrorCode)
{
    public bool Succeeded => ErrorCode == 0;
}

public enum DangoPhysicsFailureKind
{
    MissingLibrary,
    InvalidLibrary,
    MissingExport,
    AbiMismatch,
    InvalidConfiguration,
    CapacityExceeded,
    InvalidEntity,
    NativeFailure,
}

public sealed class DangoPhysicsException : InvalidOperationException
{
    public DangoPhysicsException(DangoPhysicsFailureKind kind, string message, int nativeErrorCode = 0)
        : base(message)
    {
        Kind = kind;
        NativeErrorCode = nativeErrorCode;
    }

    public DangoPhysicsFailureKind Kind { get; }

    public int NativeErrorCode { get; }
}

public sealed class DangoPhysicsWorld : IDisposable
{
    public const string NativeLibraryFileName = "dango_physics_module.dll";
    public const int NativeAbiVersion = 1;

    private readonly int ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly int capacity;
    private readonly Dictionary<DangoEntityId, byte> entities = [];
    private readonly NativeApi api;
    private nint world;
    private bool disposed;

    private DangoPhysicsWorld(NativeApi api, nint world, int capacity)
    {
        this.api = api;
        this.world = world;
        this.capacity = capacity;
    }

    public int Capacity => capacity;

    public int Count => entities.Count;

    public int LastErrorCode { get; private set; }

    public double PreviewJumpSpeedScale { get; private set; } = 1;

    public DangoStaticPlatformDefinition? StaticPlatform { get; private set; }

    public static DangoPhysicsWorld Create(
        string nativeLibraryPath,
        int capacity = DangoPhysicsDefaults.MaximumBodyCount,
        DangoSelfPhysicsParameters? configuration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeLibraryPath);
        if (capacity is < 1 or > DangoPhysicsDefaults.MaximumBodyCount)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        configuration ??= DangoPhysicsDefaults.Current;
        configuration.Validate();
        var api = NativeApi.Load(nativeLibraryPath);
        try
        {
            var native = NativeConfiguration.From(configuration);
            var error = api.Create(in native, capacity, out var handle);
            if (error != 0 || handle == 0)
                throw Map(error, $"创建物理世界失败（请求容量 {capacity}；请确认使用支持该容量的模块 DLL）。");
            return new(api, handle, capacity);
        }
        catch
        {
            api.Dispose();
            throw;
        }
    }

    public DangoEntityId Add(double anchorX, double anchorY)
    {
        VerifyAccess();
        if (!double.IsFinite(anchorX) || !double.IsFinite(anchorY)) throw new ArgumentOutOfRangeException(nameof(anchorX));
        var error = api.Add(world, (float)anchorX, (float)anchorY, out var value);
        if (error != 0) throw Map(error, "添加团子失败。");
        var id = new DangoEntityId(value);
        entities.Add(id, 0);
        return id;
    }

    public void Remove(DangoEntityId entityId)
    {
        VerifyAccess();
        if (!entities.ContainsKey(entityId)) throw Map(-412, "实体标识无效或已过期。");
        var error = api.Remove(world, entityId.Value);
        if (error != 0) throw Map(error, "移除团子失败。");
        entities.Remove(entityId);
    }

    public DangoPhysicsFrame Step(TimeSpan fixedStep, IReadOnlyList<DangoPhysicsInput> inputs)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(inputs);
        if (fixedStep <= TimeSpan.Zero || fixedStep > TimeSpan.FromMilliseconds(100))
            throw new ArgumentOutOfRangeException(nameof(fixedStep));
        if (inputs.Count != entities.Count || inputs.Select(value => value.EntityId).Distinct().Count() != inputs.Count ||
            inputs.Any(value => !entities.ContainsKey(value.EntityId)))
            throw Map(-412, "输入必须恰好覆盖世界中的有效实体。实体不能缺失、重复或过期。");
        foreach (var input in inputs)
        {
            if (!double.IsFinite(input.Horizontal) || input.Horizontal is < -1 or > 1 ||
                !double.IsFinite(input.Downward) || input.Downward is < 0 or > 1 ||
                !double.IsFinite(input.DragTargetX) || !double.IsFinite(input.DragTargetY))
                throw new ArgumentOutOfRangeException(nameof(inputs));
        }
        var ids = inputs.Select(value => value.EntityId.Value).ToArray();
        var controls = inputs.SelectMany(value => new[]
        {
            (float)value.Horizontal, (float)value.Downward, value.JumpPressed ? 1f : 0f,
            value.DragActive ? 1f : 0f, (float)value.DragTargetX, (float)value.DragTargetY,
        }).ToArray();
        var stateIds = new int[capacity];
        var states = new float[capacity * NativeApi.StateFloatCount];
        var error = api.Step(world, (float)fixedStep.TotalSeconds, ids, controls, ids.Length,
            stateIds, states, states.Length, out var stateCount, out var contactSteps, out var latchedError);
        if (error != 0) throw Map(error, "推进物理世界失败。");
        LastErrorCode = latchedError;
        var bodies = Enumerable.Range(0, stateCount).Select(index =>
        {
            var offset = index * NativeApi.StateFloatCount;
            return new DangoBodyState(
                new DangoEntityId(stateIds[index]), states[offset..(offset + 32)], states[offset + 32], states[offset + 33],
                states[offset + 34], states[offset + 35], states[offset + 36], states[offset + 37],
                (int)states[offset + 38], states[offset + 39] != 0, (int)states[offset + 40], (int)states[offset + 41]);
        }).ToArray();
        return new(bodies, contactSteps, latchedError);
    }

    public void Reset()
    {
        VerifyAccess();
        var error = api.Reset(world);
        if (error != 0) throw Map(error, "重置物理世界失败。");
        LastErrorCode = 0;
    }

    public void SetStaticPlatform(DangoStaticPlatformDefinition definition)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        if (StaticPlatform is not null && StaticPlatform != definition)
            throw Map(-410, "已有静态平台；请先清除或重置验收场景。");
        var call = api.SetStaticPlatform ?? throw MissingLocalExport("dango_physics_world_set_static_platform");
        _ = api.ClearStaticPlatform ?? throw MissingLocalExport("dango_physics_world_clear_static_platform");
        _ = api.ReadStaticPlatformContact ?? throw MissingLocalExport("dango_physics_world_get_static_platform_contact");
        var error = call(world, (float)definition.CenterX, (float)definition.CenterY,
            (float)definition.Width, (float)definition.Height, (float)definition.Friction);
        if (error != 0) throw Map(error, "设置静态平台失败；原场景保持不变。");
        StaticPlatform = definition;
    }

    public void ClearStaticPlatform()
    {
        VerifyAccess();
        var call = api.ClearStaticPlatform ?? throw MissingLocalExport("dango_physics_world_clear_static_platform");
        var error = call(world);
        if (error != 0) throw Map(error, "清除静态平台失败。");
        StaticPlatform = null;
    }

    public DangoStaticPlatformContact ReadStaticPlatformContact(DangoEntityId entityId)
    {
        VerifyAccess();
        if (!entities.ContainsKey(entityId)) throw Map(-412, "实体标识无效或已过期。");
        var call = api.ReadStaticPlatformContact ?? throw MissingLocalExport("dango_physics_world_get_static_platform_contact");
        var values = new float[6];
        var error = call(world, entityId.Value, values, values.Length);
        if (error != 0) throw Map(error, "读取平台接触失败。");
        if (values.Any(value => !float.IsFinite(value)))
            throw new DangoPhysicsException(DangoPhysicsFailureKind.NativeFailure, "平台接触包含非有限值。");
        return new(values[0] != 0, values[1] != 0, values[2] != 0, values[3], values[4], values[5]);
    }

    /// <summary>
    /// Opt-in transient single-body editor launch speed. Default worlds do not
    /// require this optional export, and restoring 1 is allowed after a fault.
    /// </summary>
    public void SetPreviewJumpSpeedScale(double scale)
    {
        VerifyAccess();
        if (!double.IsFinite(scale) || scale is < .1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(scale));
        if (scale == PreviewJumpSpeedScale) return;
        if (scale != 1 && Count != 1)
            throw new DangoPhysicsException(DangoPhysicsFailureKind.InvalidConfiguration,
                "低跳编辑预览只支持当前单团子。", -410);
        var call = api.SetPreviewJumpSpeedScale ?? throw MissingLocalExport("dango_physics_world_set_preview_jump_speed_scale");
        var error = call(world, (float)scale);
        if (error != 0) throw Map(error, "设置本地编辑预览跳跃速度失败。");
        PreviewJumpSpeedScale = scale;
    }

    /// <summary>Opt-in local stage geometry; absent in older ABI-1 modules.</summary>
    public void SetHorizontalBounds(double left, double right)
    {
        VerifyAccess();
        if (!IsFiniteFloat(left) || !IsFiniteFloat(right) || left >= right)
            throw new ArgumentOutOfRangeException(nameof(left));
        var call = api.SetHorizontalBounds ?? throw MissingLocalExport("dango_physics_world_set_horizontal_bounds");
        var error = call(world, (float)left, (float)right);
        if (error != 0) throw Map(error, "设置本地物理场景左右边界失败。");
    }

    public void ClearHorizontalBounds()
    {
        VerifyAccess();
        var call = api.ClearHorizontalBounds ?? throw MissingLocalExport("dango_physics_world_clear_horizontal_bounds");
        var error = call(world);
        if (error != 0) throw Map(error, "恢复本地物理场景地板失败。");
    }

    /// <summary>Translate the existing rigs without resetting shape, velocity or identity.</summary>
    public void TranslateHorizontal(double translation)
    {
        VerifyAccess();
        if (!IsFiniteFloat(translation)) throw new ArgumentOutOfRangeException(nameof(translation));
        var call = api.TranslateHorizontal ?? throw MissingLocalExport("dango_physics_world_translate_horizontal");
        var error = call(world, (float)translation);
        if (error != 0) throw Map(error, "本地物理场景水平坐标交接失败。");
    }

    private static bool IsFiniteFloat(double value) => double.IsFinite(value) && Math.Abs(value) <= float.MaxValue;

    private static DangoPhysicsException MissingLocalExport(string name) => new(DangoPhysicsFailureKind.MissingExport,
        $"物理模块缺少本地场景导出：{name}；请使用支持此能力的隔离候选。");

    public void Dispose()
    {
        if (disposed) return;
        VerifyThread();
        disposed = true;
        var handle = world;
        world = 0;
        var error = api.Destroy(handle);
        api.Dispose();
        entities.Clear();
        StaticPlatform = null;
        if (error != 0) throw Map(error, "释放物理世界失败。");
        GC.SuppressFinalize(this);
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(disposed || world == 0, this);
        VerifyThread();
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThreadId)
            throw new InvalidOperationException("物理世界只能由创建它的线程访问。");
    }

    private static DangoPhysicsException Map(int error, string message) => error switch
    {
        -410 => new(DangoPhysicsFailureKind.InvalidConfiguration, message, error),
        -411 => new(DangoPhysicsFailureKind.CapacityExceeded, message, error),
        -412 => new(DangoPhysicsFailureKind.InvalidEntity, message, error),
        _ => new(DangoPhysicsFailureKind.NativeFailure, message, error),
    };

    private sealed class NativeApi : IDisposable
    {
        internal const int StateFloatCount = 42;
        private nint library;
        internal readonly CreateDelegate Create;
        internal readonly AddDelegate Add;
        internal readonly RemoveDelegate Remove;
        internal readonly StepDelegate Step;
        internal readonly ResetDelegate Reset;
        internal readonly DestroyDelegate Destroy;
        internal readonly HorizontalBoundsDelegate? SetHorizontalBounds;
        internal readonly ResetDelegate? ClearHorizontalBounds;
        internal readonly TranslateHorizontalDelegate? TranslateHorizontal;
        internal readonly TranslateHorizontalDelegate? SetPreviewJumpSpeedScale;
        internal readonly StaticPlatformDelegate? SetStaticPlatform;
        internal readonly ResetDelegate? ClearStaticPlatform;
        internal readonly PlatformContactDelegate? ReadStaticPlatformContact;

        private NativeApi(nint library, CreateDelegate create, AddDelegate add, RemoveDelegate remove,
            StepDelegate step, ResetDelegate reset, DestroyDelegate destroy,
            HorizontalBoundsDelegate? setHorizontalBounds, ResetDelegate? clearHorizontalBounds,
            TranslateHorizontalDelegate? translateHorizontal, TranslateHorizontalDelegate? setPreviewJumpSpeedScale,
            StaticPlatformDelegate? setStaticPlatform, ResetDelegate? clearStaticPlatform, PlatformContactDelegate? readStaticPlatformContact)
        {
            this.library = library; Create = create; Add = add; Remove = remove; Step = step; Reset = reset; Destroy = destroy;
            SetHorizontalBounds = setHorizontalBounds; ClearHorizontalBounds = clearHorizontalBounds;
            TranslateHorizontal = translateHorizontal;
            SetPreviewJumpSpeedScale = setPreviewJumpSpeedScale;
            SetStaticPlatform = setStaticPlatform;
            ClearStaticPlatform = clearStaticPlatform;
            ReadStaticPlatformContact = readStaticPlatformContact;
        }

        internal static NativeApi Load(string path)
        {
            if (!File.Exists(path)) throw new DangoPhysicsException(DangoPhysicsFailureKind.MissingLibrary, $"物理模块不存在：{path}");
            nint library;
            try { library = NativeLibrary.Load(Path.GetFullPath(path)); }
            catch (BadImageFormatException error) { throw new DangoPhysicsException(DangoPhysicsFailureKind.InvalidLibrary, error.Message); }
            try
            {
                var abi = Get<GetIntegerDelegate>(library, "dango_physics_get_abi_version")();
                if (abi != NativeAbiVersion)
                    throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch, $"物理模块 ABI 不匹配：需要 {NativeAbiVersion}，实际 {abi}。");
                return new(library,
                    Get<CreateDelegate>(library, "dango_physics_world_create"), Get<AddDelegate>(library, "dango_physics_world_add"),
                    Get<RemoveDelegate>(library, "dango_physics_world_remove"), Get<StepDelegate>(library, "dango_physics_world_step"),
                    Get<ResetDelegate>(library, "dango_physics_world_reset"), Get<DestroyDelegate>(library, "dango_physics_world_destroy"),
                    GetOptional<HorizontalBoundsDelegate>(library, "dango_physics_world_set_horizontal_bounds"),
                    GetOptional<ResetDelegate>(library, "dango_physics_world_clear_horizontal_bounds"),
                    GetOptional<TranslateHorizontalDelegate>(library, "dango_physics_world_translate_horizontal"),
                    GetOptional<TranslateHorizontalDelegate>(library, "dango_physics_world_set_preview_jump_speed_scale"),
                    GetOptional<StaticPlatformDelegate>(library, "dango_physics_world_set_static_platform"),
                    GetOptional<ResetDelegate>(library, "dango_physics_world_clear_static_platform"),
                    GetOptional<PlatformContactDelegate>(library, "dango_physics_world_get_static_platform_contact"));
            }
            catch
            {
                NativeLibrary.Free(library);
                throw;
            }
        }

        private static T Get<T>(nint library, string name) where T : Delegate
        {
            if (!NativeLibrary.TryGetExport(library, name, out var address))
                throw new DangoPhysicsException(DangoPhysicsFailureKind.MissingExport, $"物理模块缺少导出：{name}");
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        private static T? GetOptional<T>(nint library, string name) where T : Delegate =>
            NativeLibrary.TryGetExport(library, name, out var address) ? Marshal.GetDelegateForFunctionPointer<T>(address) : null;

        public void Dispose()
        {
            var handle = Interlocked.Exchange(ref library, 0);
            if (handle != 0) NativeLibrary.Free(handle);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int GetIntegerDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CreateDelegate(in NativeConfiguration config, int capacity, out nint handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int AddDelegate(nint handle, float x, float y, out int id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RemoveDelegate(nint handle, int id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int StepDelegate(nint handle, float dt,
            [In] int[] ids, [In] float[] controls, int count, [Out] int[] stateIds, [Out] float[] states, int stateCapacity,
            out int stateCount, out int contactSteps, out int latchedError);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ResetDelegate(nint handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int DestroyDelegate(nint handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int HorizontalBoundsDelegate(nint handle, float left, float right);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int TranslateHorizontalDelegate(nint handle, float translation);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int StaticPlatformDelegate(
            nint handle, float centerX, float centerY, float width, float height, float friction);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int PlatformContactDelegate(
            nint handle, int entityId, [Out] float[] values, int capacity);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeConfiguration
    {
        public int NodeCount, SubStepCount;
        public float RadialRestLength, RadialMinimumRatio, RadialMaximumRatio, PerimeterMinimumRatio, PerimeterMaximumRatio;
        public float RadialHertz, PerimeterHertz, RadialDamping, PerimeterDamping, AreaStrength, GroundDeformationStrength;
        public float RepulsionDistance, RepulsionStrength, RepulsionMaximumForce, CoreExclusionRatio, AngleStrength;
        public float LocalAngleBalanceStrength, TopInwardForce, Gravity, CoreMass, NodeMass, DropHeight, GroundHeight;
        public float NodeRadius, Friction, Restitution, ContactHertz, LinearDamping, DragStiffness, DragDamping;
        public float DragMaximumForce, HorizontalForce, MaximumHorizontalSpeed, AirControlRatio, DownwardForce;
        public float JumpImpulse, JumpCooldownSeconds, MaximumSpeed, MinimumArea;

        public static NativeConfiguration From(DangoSelfPhysicsParameters value) => new()
        {
            NodeCount = value.NodeCount,
            SubStepCount = value.SubStepCount,
            RadialRestLength = (float)value.RadialRestLength,
            RadialMinimumRatio = (float)value.RadialMinimumRatio,
            RadialMaximumRatio = (float)value.RadialMaximumRatio,
            PerimeterMinimumRatio = (float)value.PerimeterMinimumRatio,
            PerimeterMaximumRatio = (float)value.PerimeterMaximumRatio,
            RadialHertz = (float)value.RadialElasticity,
            PerimeterHertz = (float)value.PerimeterElasticity,
            RadialDamping = (float)value.RadialDamping,
            PerimeterDamping = (float)value.PerimeterDamping,
            AreaStrength = (float)value.AreaPreservationStrength,
            GroundDeformationStrength = (float)value.GroundDeformationStrength,
            RepulsionDistance = (float)value.BoneRepulsionDistance,
            RepulsionStrength = (float)value.BoneRepulsionStrength,
            RepulsionMaximumForce = (float)value.BoneRepulsionMaximumForce,
            CoreExclusionRatio = (float)value.CoreExclusionRatio,
            AngleStrength = (float)value.PerimeterAngleStrength,
            LocalAngleBalanceStrength = (float)value.LocalAngleBalanceStrength,
            TopInwardForce = (float)value.TopInwardForce,
            Gravity = (float)value.Gravity,
            CoreMass = (float)value.CoreMass,
            NodeMass = (float)value.NodeMass,
            DropHeight = (float)value.DropHeight,
            GroundHeight = (float)value.GroundHeight,
            NodeRadius = (float)value.NodeRadius,
            Friction = (float)value.GroundFriction,
            Restitution = (float)value.Restitution,
            ContactHertz = (float)value.ContactHertz,
            LinearDamping = (float)value.OverallDamping,
            DragStiffness = (float)value.DragStiffness,
            DragDamping = (float)value.DragDamping,
            DragMaximumForce = (float)value.DragMaximumForce,
            HorizontalForce = (float)value.HorizontalForce,
            MaximumHorizontalSpeed = (float)value.MaximumHorizontalSpeed,
            AirControlRatio = (float)value.AirControlRatio,
            DownwardForce = (float)value.DownwardForce,
            JumpImpulse = (float)value.JumpImpulse,
            JumpCooldownSeconds = (float)(value.JumpCooldownMilliseconds / 1000),
            MaximumSpeed = (float)value.MaximumSpeed,
            MinimumArea = (float)value.MinimumArea,
        };
    }
}
