using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using Dango.Desktop.Physics.Tuning;

namespace Dango.Physics;

public readonly record struct DangoBeltRoomPlatform(ulong Id, double X, double Y, double Width, double Height, double Friction);

/// <summary>A detached, immutable coherent native world snapshot, in meters and seconds.</summary>
public sealed record DangoBeltRoomFrame(ulong OwnerEpoch, ulong Generation, ulong Tick, ulong SceneRevision, ulong BodyId,
    ulong ConfigurationRevision, DangoBeltRoomPlatform? Platform, int Fault,
    ReadOnlyCollection<double> BodySnapshot, ReadOnlyCollection<float> Scaffold,
    ReadOnlyCollection<float> Curve, ReadOnlyCollection<float> Stats, ReadOnlyCollection<double> Diagnostics)
{
    public bool SolidWallsEnabled => Diagnostics[23] == 1;
}

public sealed class DangoBeltRoomException(string operation, int errorCode)
    : InvalidOperationException($"Belt room {operation} failed ({errorCode}); read the last accepted frame and separate fault diagnostics.")
{
    public int ErrorCode { get; } = errorCode;
}

/// <summary>Independent native room: one body, with a legacy platform or optional furniture collection.</summary>
public sealed unsafe partial class DangoBeltRoomWorld : IDisposable
{
    public const string Model = "continuous-belt-room-v1";
    public const string BodyModel = "continuous-belt-v1";
    public const string TuningVersion = "C455-F-v1";
    public const string ContactModel = "belt-solid-aabb-r1-v1";
    public const int AbiVersion = 1, RequiredCapabilities = 15, FrameSize = 4472;
    private static readonly object NativeGate = new();
    private static long ownerSequence;
    private readonly ulong ownerEpoch = AllocateOwnerEpoch();
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private nint library, handle;
    private readonly ulong worldGeneration;
    private readonly StepDelegate step;
    private readonly ReadDelegate read;
    private readonly SetPlatformDelegate setPlatform;
    private readonly RemovePlatformDelegate removePlatform;
    private readonly DestroyDelegate destroy;
    private readonly SetBoundsDelegate? setBounds;
    private readonly SetSolidWallsDelegate? setSolidWalls;
    private readonly SetPreviewJumpSpeedScaleDelegate? setPreviewJumpSpeedScale;
    private readonly StepMusclesDelegate? stepMuscles;
    private readonly StepMuscleProfileDelegate? stepMuscleProfile;
    private readonly StepMusclePatternDelegate? stepMusclePattern;
    private readonly ReadMusclesDelegate? readMuscles;
    private readonly ReadMusclesDelegate? readActiveForces;
    public bool SupportsMuscleActuation => stepMuscles is not null && readMuscles is not null;
    public bool SupportsMuscleProfiles => SupportsMuscleActuation && stepMuscleProfile is not null;
    public bool SupportsEyeAlignedRandom => SupportsMuscleActuation && stepMusclePattern is not null;
    public bool SupportsHorizontalBounds { get; private set; }
    public bool SupportsSolidWalls { get; private set; }

    public DangoBeltRoomWorld(string module, DangoSelfPhysicsParameters configuration,
        double x = 0, double y = 0, double left = -10, double right = 10,
        DangoBeltCalibrationParameters? calibration = null)
    {
        configuration.Validate();
        if (configuration.SubStepCount != 4 || !Finite(x) || !Finite(y) || !Finite(left) || !Finite(right) || left >= right)
            throw new ArgumentOutOfRangeException(nameof(configuration));
        var native = NativeConfiguration.From(configuration);
        var values = (calibration ?? DangoBeltCalibrationParameters.ForConfiguration(configuration)).ToNative();
        lock (NativeGate)
        {
            library = NativeLibrary.Load(Path.GetFullPath(module));
            try
            {
                if (sizeof(NativeFrame) != FrameSize || sizeof(NativeInput) != 48 || sizeof(NativePlatform) != 48 ||
                    Export<VersionDelegate>("dango_belt_room_version")() != AbiVersion ||
                    Export<VersionDelegate>("dango_belt_room_frame_size")() != FrameSize ||
                    (Export<VersionDelegate>("dango_belt_room_capabilities")() & RequiredCapabilities) != RequiredCapabilities ||
                    Identity("dango_belt_room_model") != Model || Identity("dango_belt_room_tuning") != TuningVersion ||
                    Identity("dango_belt_room_contact_model") != ContactModel)
                    throw new NotSupportedException("Unsupported independent belt room ABI, model, contact model, tuning, capabilities or frame layout.");
                SupportsHorizontalBounds = (Export<VersionDelegate>("dango_belt_room_capabilities")() & 16) != 0;
                if (SupportsHorizontalBounds) setBounds = Export<SetBoundsDelegate>("dango_belt_room_set_bounds");
                SupportsSolidWalls = (Export<VersionDelegate>("dango_belt_room_capabilities")() & 32) != 0;
                if (SupportsSolidWalls) setSolidWalls = Export<SetSolidWallsDelegate>("dango_belt_room_set_solid_walls");
                if (NativeLibrary.TryGetExport(library, "dango_belt_room_set_preview_jump_speed_scale", out var previewJump))
                    setPreviewJumpSpeedScale = Marshal.GetDelegateForFunctionPointer<SetPreviewJumpSpeedScaleDelegate>(previewJump);
                LoadFurnitureCollection();
                step = Export<StepDelegate>("dango_belt_room_step");
                if (NativeLibrary.TryGetExport(library, "dango_belt_room_step_muscles", out var muscleStep) &&
                    NativeLibrary.TryGetExport(library, "dango_belt_room_read_muscle_diagnostics", out var muscleRead))
                {
                    stepMuscles = Marshal.GetDelegateForFunctionPointer<StepMusclesDelegate>(muscleStep);
                    readMuscles = Marshal.GetDelegateForFunctionPointer<ReadMusclesDelegate>(muscleRead);
                }
                if (NativeLibrary.TryGetExport(library, "dango_belt_room_step_muscles_profile", out var muscleProfile))
                    stepMuscleProfile = Marshal.GetDelegateForFunctionPointer<StepMuscleProfileDelegate>(muscleProfile);
                if (NativeLibrary.TryGetExport(library, "dango_belt_room_step_muscles_pattern", out var musclePattern))
                    stepMusclePattern = Marshal.GetDelegateForFunctionPointer<StepMusclePatternDelegate>(musclePattern);
                if (NativeLibrary.TryGetExport(library, "dango_belt_room_read_active_forces", out var activeForces))
                    readActiveForces = Marshal.GetDelegateForFunctionPointer<ReadMusclesDelegate>(activeForces);
                read = Export<ReadDelegate>("dango_belt_room_read_frame");
                setPlatform = Export<SetPlatformDelegate>("dango_belt_room_set_platform");
                removePlatform = Export<RemovePlatformDelegate>("dango_belt_room_remove_platform");
                destroy = Export<DestroyDelegate>("dango_belt_room_destroy");
                Check(Export<CreateDelegate>("dango_belt_room_create")(in native, values, values.Length,
                    (float)x, (float)y, (float)left, (float)right, out handle), "create");
                if (handle == 0) throw new InvalidOperationException("Room create returned a null owner.");
                NativeFrame initial = default;
                Check(read(handle, &initial, sizeof(NativeFrame)), "read initial frame");
                ValidateFrameIdentity(in initial);
                if (initial.Tick != 0 || initial.SceneRevision != 0 || initial.Fault != 0 || initial.PlatformEnabled != 0 || initial.Diagnostics[23] != 0)
                    throw new InvalidOperationException("Native room returned an invalid initial world state.");
                worldGeneration = initial.Generation;
            }
            catch
            {
                if (handle != 0) { destroy?.Invoke(handle); handle = 0; }
                NativeLibrary.Free(library); library = 0; throw;
            }
        }
    }

    public void Step(ulong bodyId, DangoBeltInput input = default)
    {
        VerifyAccess();
        if (!Finite(input.Horizontal) || Math.Abs(input.Horizontal) > 1 ||
            !Finite(input.MuscleShakeTime) || input.MuscleShakeTime is < 0 or > 2 ||
            !Finite(input.MuscleShakeStrength) || input.MuscleShakeStrength is < 0 or > 2 ||
            !Enum.IsDefined(input.MuscleShakeProfile) ||
            (input.MuscleShakeProfile != DangoMuscleShakeProfile.EyeAlignedRandom && input.MuscleShakeSeed != 0) ||
            (input.MuscleShakeProfile == DangoMuscleShakeProfile.EyeAlignedRandom &&
                (!Finite(input.MuscleShakeAxisX) || !Finite(input.MuscleShakeAxisY) ||
                 Math.Abs(Math.Sqrt(input.MuscleShakeAxisX * input.MuscleShakeAxisX + input.MuscleShakeAxisY * input.MuscleShakeAxisY) - 1) > 1e-4)) ||
            !Finite(input.Stretch) || input.Stretch is < 0 or > 1 ||
            !Finite(input.Downward) || input.Downward is < 0 or > 1 ||
            !Finite(input.DragTargetX) || !Finite(input.DragTargetY)) throw new ArgumentOutOfRangeException(nameof(input));
        var controls = DangoBeltWorld.NativeControls(input);
        var native = new NativeInput { BodyId = bodyId };
        for (var i = 0; i < controls.Length; i++) native.Controls[i] = controls[i];
        lock (NativeGate)
        {
            if (input.MuscleShakeStrength > 0)
            {
                if (!SupportsMuscleActuation) throw new NotSupportedException("当前物理模块不支持内部肌肉抖身。");
                if (input.MuscleShakeProfile == DangoMuscleShakeProfile.EyeAlignedRandom)
                {
                    if (!SupportsEyeAlignedRandom) throw new NotSupportedException("当前物理模块不支持沿眼线随机内收。");
                    Check(stepMusclePattern!(handle, (float)DangoBeltWorld.FixedDt, in native, 1,
                        (float)input.MuscleShakeTime, (float)input.MuscleShakeStrength, (int)input.MuscleShakeProfile,
                        (float)input.MuscleShakeAxisX, (float)input.MuscleShakeAxisY, input.MuscleShakeSeed), "step random muscle pattern");
                }
                else if (input.MuscleShakeProfile == DangoMuscleShakeProfile.Original)
                    Check(stepMuscles!(handle, (float)DangoBeltWorld.FixedDt, in native, 1,
                        (float)input.MuscleShakeTime, (float)input.MuscleShakeStrength), "step internal muscles");
                else
                {
                    if (stepMuscleProfile is null) throw new NotSupportedException("当前物理模块不支持肩部柔和抖身。");
                    Check(stepMuscleProfile(handle, (float)DangoBeltWorld.FixedDt, in native, 1,
                        (float)input.MuscleShakeTime, (float)input.MuscleShakeStrength, (int)input.MuscleShakeProfile), "step muscle profile");
                }
            }
            else Check(step(handle, (float)DangoBeltWorld.FixedDt, in native, 1), "step");
        }
    }

    public DangoMuscleDiagnostics ReadMuscleDiagnostics()
    {
        VerifyAccess();
        if (!SupportsMuscleActuation) throw new NotSupportedException("当前物理模块不支持内部肌肉抖身。");
        var values = new double[32];
        lock (NativeGate) Check(readMuscles!(handle, values, values.Length), "read muscle diagnostics");
        if (values.Any(v => !Finite(v)) || values[0] != 1 || values[1] != 32 ||
            values[24] != Math.Truncate(values[24]) || !Enum.IsDefined((DangoMuscleShakeProfile)(int)values[24]) ||
            values[25] is < 0 or > uint.MaxValue || values[25] != Math.Truncate(values[25]))
            throw new InvalidOperationException("肌肉诊断格式无效。");
        return new(values[3], values[4], (int)values[5], (int)values[6], values[7], values[8],
            values[9], values[10], values[11], values[12], values[13], (DangoMuscleShakeProfile)(int)values[24],
            (uint)values[25], values[26], values[27], values[28], values[29], values[30]);
    }

    public DangoActiveForceFrame ReadActiveForces()
    {
        VerifyAccess();
        if (readActiveForces is null) throw new NotSupportedException("当前物理模块不支持真实主动施力诊断。");
        var values = new double[1024];
        lock (NativeGate) Check(readActiveForces(handle, values, values.Length), "read active forces");
        if (values.Any(v => !Finite(v)) || values[0] != 1 || values[1] != 1024 || values[6] != 6 ||
            values[2] < 0 || values[2] > 9007199254740991 || values[2] != Math.Truncate(values[2]) ||
            values[3] is < -1 or > 3 || values[3] != Math.Truncate(values[3]) ||
            values[4] is < 0 or > 97 || values[4] != Math.Truncate(values[4]))
            throw new InvalidOperationException("主动施力诊断格式无效。");
        var forces = new DangoActiveForce[(int)values[4]];
        for (var i = 0; i < forces.Length; i++)
        {
            var p = 8 + 6 * i;
            if (values[p + 4] is < 1 or > 6 || values[p + 4] != Math.Truncate(values[p + 4]))
                throw new InvalidOperationException("主动施力来源无效。");
            forces[i] = new(values[p], values[p + 1], values[p + 2], values[p + 3], (int)values[p + 4], values[p + 5]);
        }
        return new((ulong)values[2], (int)values[3], Array.AsReadOnly(forces));
    }

    public void SetHorizontalBounds(double left, double right, ulong expectedRevision)
    {
        VerifyAccess();
        if (!SupportsHorizontalBounds) throw new NotSupportedException("This belt room module does not support horizontal bounds.");
        if (!Finite(left) || !Finite(right) || left >= right) throw new ArgumentOutOfRangeException(nameof(right));
        lock (NativeGate) Check(setBounds!(handle, (float)left, (float)right, expectedRevision), "set bounds");
    }

    public void SetSolidWalls(bool enabled, ulong expectedRevision)
    {
        VerifyAccess();
        if (!SupportsSolidWalls) throw new NotSupportedException("This belt room module does not support solid walls.");
        lock (NativeGate) Check(setSolidWalls!(handle, enabled ? 1 : 0, expectedRevision), "set solid walls");
    }

    /// <summary>Isolated preview actuation. Existing modules keep their default jump behavior.</summary>
    public void SetPreviewJumpSpeedScale(double scale)
    {
        VerifyAccess();
        if (!Finite(scale) || scale is < .1 or > 1) throw new ArgumentOutOfRangeException(nameof(scale));
        if (setPreviewJumpSpeedScale is null)
            throw new NotSupportedException("This belt room module does not support preview jump speed scaling.");
        lock (NativeGate) Check(setPreviewJumpSpeedScale(handle, (float)scale), "set preview jump speed scale");
    }

    public void SetPlatform(DangoBeltRoomPlatform platform, ulong expectedRevision)
    {
        VerifyAccess();
        var native = new NativePlatform { Id = platform.Id, X = platform.X, Y = platform.Y,
            Width = platform.Width, Height = platform.Height, Friction = platform.Friction };
        lock (NativeGate) Check(setPlatform(handle, in native, expectedRevision), "set platform");
    }

    public void RemovePlatform(ulong platformId, ulong expectedRevision)
    {
        VerifyAccess();
        lock (NativeGate) Check(removePlatform(handle, platformId, expectedRevision), "remove platform");
    }

    public DangoBeltRoomFrame Read()
    {
        VerifyAccess();
        NativeFrame native = default;
        lock (NativeGate) Check(read(handle, &native, sizeof(NativeFrame)), "read frame");
        return DecodeFrame(native);
    }

    private DangoBeltRoomFrame DecodeFrame(NativeFrame native)
    {
        ValidateFrameIdentity(in native);
        if (native.Generation != worldGeneration) throw new InvalidOperationException("Native room generation changed during its owner lifetime.");
        var p = native.Platform;
        return new(ownerEpoch, native.Generation, native.Tick, native.SceneRevision, native.BodyId, native.ConfigurationRevision,
            native.PlatformEnabled != 0 ? new(p.Id, p.X, p.Y, p.Width, p.Height, p.Friction) : null, native.Fault,
            Array.AsReadOnly(new ReadOnlySpan<double>(native.BodySnapshot, 200).ToArray()),
            Array.AsReadOnly(new ReadOnlySpan<float>(native.Scaffold, 68).ToArray()),
            Array.AsReadOnly(new ReadOnlySpan<float>(native.Curve, 512).ToArray()),
            Array.AsReadOnly(new ReadOnlySpan<float>(native.Stats, 64).ToArray()),
            Array.AsReadOnly(new ReadOnlySpan<double>(native.Diagnostics, 24).ToArray()));
    }

    public void Dispose()
    {
        if (library == 0) return;
        VerifyAccess();
        lock (NativeGate)
        {
            var error = destroy(handle); handle = 0;
            NativeLibrary.Free(library); library = 0;
            Check(error, "destroy");
        }
    }
    // Native generation is scoped to one DLL load. Retained frames also need a process owner identity.
    private static ulong AllocateOwnerEpoch()
    {
        while (true)
        {
            var current = Volatile.Read(ref ownerSequence);
            if (current == long.MaxValue) throw new InvalidOperationException("Belt room owner epoch exhausted.");
            if (Interlocked.CompareExchange(ref ownerSequence, current + 1, current) == current) return (ulong)(current + 1);
        }
    }
    private static void ValidateFrameIdentity(in NativeFrame frame)
    {
        if (frame.Size != FrameSize || frame.Abi != AbiVersion || frame.Generation == 0 || frame.BodyId != 1 ||
            frame.ConfigurationRevision == 0 || frame.PlatformEnabled is < 0 or > 1 ||
            (frame.PlatformEnabled != 0 && frame.Platform.Id == 0) ||
            (frame.Diagnostics[23] != 0 && frame.Diagnostics[23] != 1))
            throw new InvalidOperationException("Native room returned an invalid coherent frame identity.");
    }
    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(library == 0 || handle == 0, this);
        if (ownerThread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Belt room thread ownership violation.");
    }
    private static bool Finite(double value) => double.IsFinite(value) && Math.Abs(value) <= float.MaxValue;
    private static void Check(int result, string operation) { if (result != 0) throw new DangoBeltRoomException(operation, result); }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    private string? Identity(string name) => Marshal.PtrToStringUTF8(Export<IdentityDelegate>(name)());
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int VersionDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint IdentityDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateDelegate(in NativeConfiguration config, [In] float[] calibration, int count, float x, float y, float left, float right, out nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StepDelegate(nint handle, float dt, in NativeInput input, int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadDelegate(nint handle, NativeFrame* frame, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetPlatformDelegate(nint handle, in NativePlatform platform, ulong expectedRevision);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RemovePlatformDelegate(nint handle, ulong id, ulong expectedRevision);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetBoundsDelegate(nint handle, float left, float right, ulong expectedRevision);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSolidWallsDelegate(nint handle, int enabled, ulong expectedRevision);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetPreviewJumpSpeedScaleDelegate(nint handle, float scale);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StepMusclesDelegate(nint handle, float dt, in NativeInput input, int count, float time, float strength);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StepMuscleProfileDelegate(nint handle, float dt, in NativeInput input, int count, float time, float strength, int profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StepMusclePatternDelegate(nint handle, float dt, in NativeInput input, int count, float time, float strength, int profile, float axisX, float axisY, uint seed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadMusclesDelegate(nint handle, [Out] double[] values, int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DestroyDelegate(nint handle);
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public ulong BodyId; public fixed float Controls[9]; public int Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePlatform { public ulong Id; public double X, Y, Width, Height, Friction; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeFrame
    {
        public uint Size, Abi;
        public ulong Generation, Tick, SceneRevision, BodyId, ConfigurationRevision;
        public int PlatformEnabled, Fault;
        public NativePlatform Platform;
        public fixed double BodySnapshot[200];
        public fixed float Scaffold[68], Curve[512], Stats[64];
        public fixed double Diagnostics[24];
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
