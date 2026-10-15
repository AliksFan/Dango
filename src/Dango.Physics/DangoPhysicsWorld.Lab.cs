using System.Runtime.InteropServices;

namespace Dango.Physics;

public enum DangoLabDrive { Core, Mass, HeightBias, PointHeight, DirectionRegion, DirectionPoint }
public enum DangoLabGroundModel { DiscreteNodes = 0, ContinuousEnvelope = 1, ElasticArcs = 2 }
public sealed record DangoLabArcStats(float StretchEnergy, float BendingEnergy, float DampingPower,
    float InternalForceX, float InternalForceY, float InternalTorque, float MinimumCurveSpeed,
    int ActiveContacts, float MaximumPenetration, float NormalImpulse, float AbsoluteTangentImpulse,
    float ContactKineticEnergyChange, float MaximumContactCorrection, float MaximumRadialCorrection,
    float NaturalCircleRelativeError, bool Fault, float PositionRepairArcPotentialEnergyChange,
    float ContactKineticEnergyBefore, float ContactKineticEnergyAfter, float RadialKineticEnergyChange,
    float MaximumPreSolvePenetration, float MaximumContactBlockEnergyIncrease, float MaximumFrictionConeViolation);
/// <summary>Schema 1 separates normal/friction KE. Schema 2 uses the normal KE pair
/// for the atomic coupled contact block; its separate friction KE pair is zero.</summary>
public sealed record DangoLabGroundStats(float FootX, float FootY, float Gap,
    float NormalImpulse, float AbsoluteTangentImpulse, float FrictionCoefficient,
    float SummedPositionCorrectionL2, float MaximumPositionCorrection, float ConstraintSweeps,
    bool Grounded, int ContactCount, float NormalEnergyBefore, float NormalEnergyAfter,
    float FrictionEnergyBefore, float FrictionEnergyAfter, float MomentumResidualX,
    float MomentumResidualY, float TorqueResidual, float MaximumShapePositionCorrection,
    float MinimumGapDuringStep, float MaximumPenetrationAfterSolve, float NormalSpeedBefore,
    float NormalSpeedAfter, float TangentSpeedBefore, float TangentSpeedAfter,
    int SegmentIndex, float SegmentT, int FloorBox2DContactCount,
    float ExactCurveMaximumY, float ShapeKineticEnergyDelta, bool SolverFault, int SchemaVersion);

public readonly record struct DangoLabInput(
    DangoEntityId EntityId, double Horizontal = 0, double Downward = 0,
    bool JumpPressed = false, bool DragActive = false, double DragTargetX = 0,
    double DragTargetY = 0, double Stretch = 0, bool InputActive = false, bool CancelForces = false);

public sealed record DangoLabRegionStats(float BoundsMinX, float BoundsMinY, float BoundsMaxX, float BoundsMaxY,
    int HorizontalMask, int VerticalMask, float HorizontalPointX, float HorizontalPointY,
    float VerticalPointX, float VerticalPointY, float ActualForceX, float ActualForceY,
    float TorqueAboutCenterOfMass, int SchemaVersion);

public sealed record DangoLabNode(int Index, float X, float Y, float VelocityX, float VelocityY,
    float Mass, int ContactCount);

public sealed record DangoLabStats(DangoLabDrive Mode, float Bias, float StretchRamp,
    float RequestedForceX, float ActualForceX, float ActualForceY, float TorqueAboutCenterOfMass,
    float ForceWeightedY, float CenterOfMassY, float StretchForceBudgetUsed,
    float StretchNetForceX, float StretchNetForceY, float StretchTorqueAboutCore,
    float MaximumStretchBudget, bool InputActive, float StretchRequested);

public sealed partial class DangoPhysicsWorld
{
    public int LabRegionDenominator { get { VerifyAccess(); return api.GetLabRegionDenominator(); } }

    public DangoLabArcStats? ReadLabArcStats(DangoEntityId id)
    {
        VerifyLabEntity(id);
        if (api.GetLabArcVersion() == 0) return null;
        if (api.GetLabArcVersion() != 1)
            throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch, "Unknown elastic arc experiment version.");
        var call = api.GetLabArcStats() ?? throw new DangoPhysicsException(DangoPhysicsFailureKind.MissingExport,
            "Elastic arc experiment statistics are missing.");
        var v = new float[32];
        var error = call(world, id.Value, v, v.Length);
        if (error != 0) throw Map(error, "读取弹性弧段统计失败。");
        CheckLabValues(v);
        return new(v[0],v[1],v[2],v[3],v[4],v[5],v[6],(int)v[7],v[8],v[9],v[10],v[11],
            v[12],v[13],v[14],v[15]!=0,v[16],v[17],v[18],v[19],v[23],v[24],v[25]);
    }

    /// <summary>Optional lab capability. Legacy modules without the export use discrete node contacts.</summary>
    public DangoLabGroundModel LabGroundModel
    {
        get
        {
            VerifyAccess();
            var value = api.GetLabGroundModel();
            if (!Enum.IsDefined(typeof(DangoLabGroundModel), value))
                throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch, $"Unknown laboratory ground model {value}.");
            return (DangoLabGroundModel)value;
        }
    }

    /// <summary>Explicit opt-in; ordinary worlds never resolve or require laboratory exports.</summary>
    public void RequireLab()
    {
        VerifyAccess();
        api.RequireLab();
    }

    public DangoPhysicsFrame StepLab(double dt, IReadOnlyList<DangoLabInput> inputs,
        DangoLabDrive mode, double bias = 0)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(inputs);
        if (!IsFiniteFloat(dt) || dt <= 0 || dt > .1) throw new ArgumentOutOfRangeException(nameof(dt));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!IsFiniteFloat(bias) || bias is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(bias));
        if (inputs.Count != entities.Count || inputs.Select(v => v.EntityId).Distinct().Count() != inputs.Count ||
            inputs.Any(v => !entities.ContainsKey(v.EntityId)))
            throw Map(-412, "实验输入必须恰好覆盖有效实体，不能缺失、重复或过期。");
        foreach (var v in inputs)
            if (!IsFiniteFloat(v.Horizontal) || v.Horizontal is < -1 or > 1 ||
                !IsFiniteFloat(v.Downward) || v.Downward is < 0 or > 1 ||
                !IsFiniteFloat(v.Stretch) || v.Stretch is < 0 or > 1 ||
                !IsFiniteFloat(v.DragTargetX) || !IsFiniteFloat(v.DragTargetY))
                throw new ArgumentOutOfRangeException(nameof(inputs));
        api.RequireLab();
        if (mode >= DangoLabDrive.DirectionRegion && api.GetLabRegionVersion() != 1)
            throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch,
                "当前实验 DLL 不支持方向区域施力；请加载独立 regions 实验模块。");
        if (mode == DangoLabDrive.DirectionPoint && api.GetLabRegionDenominator() == 2)
            throw new ArgumentOutOfRangeException(nameof(mode), "本轮仅支持半侧区域共同受力，单节点实验已取消。");
        var ids = inputs.Select(v => v.EntityId.Value).ToArray();
        var controls = inputs.SelectMany(v => new[] {
            (float)v.Horizontal, (float)v.Downward, v.JumpPressed ? 1f : 0f,
            v.DragActive ? 1f : 0f, (float)v.DragTargetX, (float)v.DragTargetY,
            (float)v.Stretch, v.InputActive ? 1f : 0f, v.CancelForces ? 1f : 0f,
        }).ToArray();
        var stateIds = new int[capacity];
        var states = new float[capacity * NativeApi.StateFloatCount];
        var error = api.StepLab!(world, (float)dt, ids, controls, ids.Length, (int)mode, (float)bias,
            stateIds, states, states.Length, out var count, out var contacts, out var latched);
        if (error != 0) throw Map(error, "推进独立物理实验失败。");
        if (count < 0 || count > capacity || states.Take(count * NativeApi.StateFloatCount).Any(v => !float.IsFinite(v)))
            throw new DangoPhysicsException(DangoPhysicsFailureKind.NativeFailure, "实验状态包含无效数量或非有限值。");
        LastErrorCode = latched;
        var bodies = Enumerable.Range(0, count).Select(i => {
            var o = i * NativeApi.StateFloatCount;
            return new DangoBodyState(new(stateIds[i]), states[o..(o + 32)], states[o + 32], states[o + 33],
                states[o + 34], states[o + 35], states[o + 36], states[o + 37], (int)states[o + 38],
                states[o + 39] != 0, (int)states[o + 40], (int)states[o + 41]);
        }).ToArray();
        return new(bodies, contacts, latched);
    }

    public IReadOnlyList<DangoLabNode> ReadLabNodes(DangoEntityId id)
    {
        VerifyLabEntity(id);
        var values = new float[102];
        var error = api.LabNodes!(world, id.Value, values, values.Length);
        if (error != 0) throw Map(error, "读取实验节点失败。");
        CheckLabValues(values);
        return Array.AsReadOnly(Enumerable.Range(0, 17).Select(i => {
            var o = i * 6;
            return new DangoLabNode(i, values[o], values[o + 1], values[o + 2], values[o + 3], values[o + 4], (int)values[o + 5]);
        }).ToArray());
    }

    public DangoLabStats ReadLabStats(DangoEntityId id)
    {
        VerifyLabEntity(id);
        var v = new float[16];
        var error = api.LabStats!(world, id.Value, v, v.Length);
        if (error != 0) throw Map(error, "读取实验受力统计失败。");
        CheckLabValues(v);
        return new((DangoLabDrive)(int)v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8],
            v[9], v[10], v[11], v[12], v[13], v[14] != 0, v[15]);
    }

    public DangoLabRegionStats? ReadLabRegionStats(DangoEntityId id)
    {
        VerifyLabEntity(id);
        var call = api.GetLabRegionStats();
        if (call is null) return null;
        var v = new float[16];
        var error = call(world, id.Value, v, v.Length);
        if (error != 0) throw Map(error, "读取实验方向施力统计失败。");
        CheckLabValues(v);
        if (v[15] == 0) return null;
        if (v[15] != 1) throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch,
            $"Laboratory region statistics schema must be 1; got {v[15]}.");
        return new(v[0],v[1],v[2],v[3],(int)v[4],(int)v[5],v[6],v[7],v[8],v[9],v[10],v[11],v[12],(int)v[15]);
    }

    public DangoLabGroundStats? ReadLabGroundStats(DangoEntityId id)
    {
        VerifyLabEntity(id);
        var call = api.GetLabGroundStats();
        if (call is null) return null;
        var v = new float[32];
        var error = call(world, id.Value, v, v.Length);
        if (error != 0) throw Map(error, "读取实验地面统计失败。");
        CheckLabValues(v);
        var expectedSchema = LabGroundModel == DangoLabGroundModel.ElasticArcs ? 2 : 1;
        if (v[31] != expectedSchema) throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch,
            $"Laboratory ground statistics schema must be {expectedSchema}; got {v[31]}.");
        return new(v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],v[8],v[9]!=0,(int)v[10],
            v[11],v[12],v[13],v[14],v[15],v[16],v[17],v[18],v[19],v[20],v[21],v[22],
            v[23],v[24],(int)v[25],v[26],(int)v[27],v[28],v[29],v[30]!=0,(int)v[31]);
    }

    private void VerifyLabEntity(DangoEntityId id)
    {
        VerifyAccess();
        if (!entities.ContainsKey(id)) throw Map(-412, "实验实体标识无效或已过期。");
        api.RequireLab();
    }

    private static void CheckLabValues(float[] values)
    {
        if (values.Any(v => !float.IsFinite(v)))
            throw new DangoPhysicsException(DangoPhysicsFailureKind.NativeFailure, "实验读数包含非有限值。");
    }

    private sealed partial class NativeApi
    {
        internal int GetLabArcVersion() => GetOptional<GetIntegerDelegate>(library, "dango_physics_lab_arc_model")?.Invoke() ?? 0;
        internal int GetLabRegionDenominator() => GetOptional<GetIntegerDelegate>(library, "dango_physics_lab_region_fraction")?.Invoke() ?? 3;
        internal PlatformContactDelegate? GetLabArcStats() => GetOptional<PlatformContactDelegate>(library,
            "dango_physics_world_get_lab_arc_stats");
        internal int GetLabRegionVersion() => GetOptional<GetIntegerDelegate>(library, "dango_physics_lab_region_version")?.Invoke() ?? 0;
        internal PlatformContactDelegate? GetLabRegionStats() => GetOptional<PlatformContactDelegate>(library,
            "dango_physics_world_get_lab_region_stats");
        internal int GetLabGroundModel() => GetOptional<GetIntegerDelegate>(library, "dango_physics_lab_ground_model")?.Invoke() ?? 0;
        internal PlatformContactDelegate? GetLabGroundStats() => GetOptional<PlatformContactDelegate>(library,
            "dango_physics_world_get_lab_ground_stats");
        internal LabStepDelegate? StepLab { get; private set; }
        internal PlatformContactDelegate? LabNodes { get; private set; }
        internal PlatformContactDelegate? LabStats { get; private set; }

        internal void RequireLab()
        {
            if (StepLab is not null) return;
            var version = Get<GetIntegerDelegate>(library, "dango_physics_get_lab_version")();
            if (version != 1) throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch,
                $"物理实验接口需要版本 1，实际 {version}。");
            var step = Get<LabStepDelegate>(library, "dango_physics_world_step_lab");
            var nodes = Get<PlatformContactDelegate>(library, "dango_physics_world_get_drive_review");
            var stats = Get<PlatformContactDelegate>(library, "dango_physics_world_get_lab_stats");
            LabNodes = nodes;
            LabStats = stats;
            StepLab = step;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int LabStepDelegate(nint handle, float dt, [In] int[] ids, [In] float[] controls,
            int count, int mode, float bias, [Out] int[] stateIds, [Out] float[] states, int capacity,
            out int stateCount, out int contactSteps, out int latchedError);
    }
}
