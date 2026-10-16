using System.IO;
using System.Runtime.InteropServices;
using Dango.Desktop.Physics.Tuning;

namespace Dango.PhysicsLab;

internal readonly record struct LabBeltInput(double Horizontal=0, double Stretch=0, double Downward=0,
    bool JumpPressed=false, bool DragActive=false, double DragTargetX=0, double DragTargetY=0,
    bool InputActive=false, bool CancelForces=false);

/// <summary>Native world coordinates. Scaffold has core then 16 massless attachments, each x/y/vx/vy.</summary>
internal sealed record LabBeltFrame(float[] Scaffold, float[] Curve, float[] Stats)
{
    public double CoreX => Stats[0];
    public double CoreY => Stats[1];
    public double ComX => Stats[4];
    public double ComY => Stats[5];
    public double TotalMass => Stats[6];
    public double Area => Stats[7];
    public double MinimumX => Stats[8];
    public double MinimumY => Stats[9];
    public double MaximumX => Stats[10];
    public double MaximumY => Stats[11];
    public double Width => MaximumX-MinimumX;
    public double Height => MaximumY-MinimumY;
    public double Gap => Stats[12];
    public double MaximumSpeed => Stats[13];
    public bool Grounded => Stats[14]!=0;
    public bool JumpAccepted => Stats[15]!=0;
    public double HorizontalForce => Stats[18];
    public double VerticalForce => Stats[19];
    public int ActiveContacts => (int)Stats[25];
    public double ContactEnergyChange => Stats[27]-Stats[26];
    public double MaximumPenetration => Stats[31];
    public bool ShortBones => Stats[40]!=0;
}

/// <summary>Isolated half-region belt ABI; no production native resolver or fake node masses.</summary>
internal sealed class LabBeltWorld : IDisposable
{
    private nint library, handle;
    private readonly int ownerThread=Environment.CurrentManagedThreadId;
    private readonly StepDelegate step;
    private readonly ReadDelegate read;
    private readonly BoundsDelegate bounds;
    private readonly DestroyDelegate destroy;
    private readonly DiagnosticsDelegate? diagnostics;
    private readonly OriginalGroundJumpDelegate? originalGroundJump;
    private readonly OriginalGroundJumpDelegate? shapeGuard;
    private readonly DiagnosticsDelegate? shapeGuardDiagnostics;
    private readonly DoubleDiagnosticsDelegate? debugState, solverDiagnostics, speedLimitDiagnostics, whaleDriveDiagnostics;
    private readonly OriginalGroundJumpDelegate? speedSaturation, whaleDrive;
    public bool WhaleDriveAvailable => whaleDrive is not null && whaleDriveDiagnostics is not null;
    public bool WhaleDriveEnabled { get; private set; }
    public const string WhaleDriveSchema = "v1:16;0=version,1=enabled,2=attemptTick,3=signedGain,4=activeSubsteps,5=maxAbsNetFx,6=maxAbsNetFy,7=maxAbsTorque,8=shapeEndpointWorkSubset,9=maxPointTraction,10=maxGeneralizedForce,11=suppressionBits,12=gainBefore,13=gainAfter,14=guardWorkPeak,15=reserved;suppression:1=air,2=drag,4=jump,8=WSramp,16=cancel";
    public bool SpeedSaturationAvailable => speedSaturation is not null && speedLimitDiagnostics is not null;
    public bool SpeedSaturationEnabled { get; private set; }
    public const string SpeedLimitSchema = "v1:12;0=version,1=enabled,2=attemptTick,3=limitedSubsteps,4=minScale,5=maxBeforeUpper,6=maxAfterUpper,7=deltaKE,8=deltaPx,9=deltaPy,10=deltaL,11=reserved";
    public const string DebugStateSchema = "v1:200;0=version,1=count,2..37=q36,38..73=v36,74=radius,75=scale,76=left,77=right,78=ramp,79=cooldown,80=dragTheta,81=shortBones,82=grounded,83=dragging,84=originalJump,85=hasStepped,86=shapeGuard,87..127=config41,128..138=calibration11,139=guardAttemptTick,140..175=mass36,176=speedSaturation,177=whaleDrive,178=whaleSignedGain,179..199=reserved";
    public const string SolverDiagnosticsSchema = "v1:24;0=version,1=attemptTick,2=returnCode,3=returnSite,4=substep,5=GNiteration,6=lineSearch,7=residual,8=objective,9=directional,10=trialObjective,11=factor,12=contactResidual,13=penetration,14=speed,15=stage,16=residualValidatedLineSearchStops,17=dampedBfgsUpdates,18=gaussNewtonResets,19=speedCertificateSamples,20=speedCoreOrCurveSampleMaximum,21=speedCertifiedUpper,22=lastRefinementCoarseUpper,23=lastRefinementCertifiedUpper";
    public const string SolverFailureSites = "1=initialResidual,2=nonfiniteObjective,3=Cholesky,4=ArmijoExhausted,5=GNResidualCap,6=integratedPathGuard,7=NewtonWorkBudget,8=groundWorkBudget,9=groundFallbackGuard,10=groundPenetration,11=contactResidual,12=contactEnergy,13=speed,14=bounds,15=finalResidual,16=area,17=statsNonfinite,18=energyDiagNonfinite,19=initialGuard,20=regionForce,21=speedSaturationUnresolved,22=whaleForce,31=initialDerivative,32=initialRadialLength,33=initialShortEdge,34=initialArea,35=initialBarrierMaterialDomain;stage:1=forces,2=integration,3=contact,4=speedBounds,5=final";
    public bool DebugStateAvailable => ShapeGuardEnabled && debugState is not null;
    public bool SolverDiagnosticsAvailable => ShapeGuardEnabled && solverDiagnostics is not null;
    public const string ShapeGuardSchema = "v1:version,enabled,stepAttemptTick,lastStepReturnCode,certificateWork,certificateCalls,maxDepth,certifiedLeaves,refusedCandidates,lastRefusalReason,refusalStage,acceptedCrossLower,acceptedClearanceLower,winding,initialGuardElastic,finalGuardElastic,peakGuardCoreForce,guardEndpointForceWork,groundRigidFallbacks,hardClearance,hardCross,workBudget,acceptedSubsteps,reserved;refusalReason:1=cross,2=clearance,3=budget,4=winding,5=nonfinite,6=solver,7=ground,8=uncertain,9=materialDomain";
    public bool ShapeGuardAvailable => shapeGuard is not null && shapeGuardDiagnostics is not null;
    public bool ShapeGuardEnabled { get; private set; }
    public bool OriginalGroundJumpEnabled { get; private set; }
    public string? CalibrationSchema { get; }
    public LabBeltWorld(string module,DangoSelfPhysicsParameters config,bool shortBones=true,LabBeltCalibrationParameters? calibration=null,bool enableShapeGuard=false,bool enableSpeedSaturation=false,bool enableWhaleDrive=false)
    {
        config.Validate();
        library=NativeLibrary.Load(Path.GetFullPath(module));
        try
        {
            if(Export<VersionDelegate>("dango_belt_version")()!=1) throw new InvalidOperationException("Unsupported belt ABI");
            step=Export<StepDelegate>("dango_belt_step"); read=Export<ReadDelegate>("dango_belt_read");
            bounds=Export<BoundsDelegate>("dango_belt_set_bounds"); destroy=Export<DestroyDelegate>("dango_belt_destroy");
            if(NativeLibrary.TryGetExport(library,"dango_belt_set_original_ground_jump",out var originalJumpAddress))
                originalGroundJump=Marshal.GetDelegateForFunctionPointer<OriginalGroundJumpDelegate>(originalJumpAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_set_shape_guard",out var guardAddress))
                shapeGuard=Marshal.GetDelegateForFunctionPointer<OriginalGroundJumpDelegate>(guardAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_read_shape_guard",out var guardDiagnosticsAddress))
                shapeGuardDiagnostics=Marshal.GetDelegateForFunctionPointer<DiagnosticsDelegate>(guardDiagnosticsAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_read_snapshot",out var snapshotAddress))
                debugState=Marshal.GetDelegateForFunctionPointer<DoubleDiagnosticsDelegate>(snapshotAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_read_solver_diagnostics",out var solverAddress))
                solverDiagnostics=Marshal.GetDelegateForFunctionPointer<DoubleDiagnosticsDelegate>(solverAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_set_speed_saturation",out var speedAddress))
                speedSaturation=Marshal.GetDelegateForFunctionPointer<OriginalGroundJumpDelegate>(speedAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_read_speed_limit",out var speedDiagnosticsAddress))
                speedLimitDiagnostics=Marshal.GetDelegateForFunctionPointer<DoubleDiagnosticsDelegate>(speedDiagnosticsAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_set_whale_drive",out var whaleAddress))
                whaleDrive=Marshal.GetDelegateForFunctionPointer<OriginalGroundJumpDelegate>(whaleAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_read_whale_drive",out var whaleDiagnosticsAddress))
                whaleDriveDiagnostics=Marshal.GetDelegateForFunctionPointer<DoubleDiagnosticsDelegate>(whaleDiagnosticsAddress);
            if(enableWhaleDrive && !WhaleDriveAvailable)
                throw new NotSupportedException("此模块缺少行走受力 ABI；请构建本轮鲸形行走实验组件。");
            if(enableSpeedSaturation && !SpeedSaturationAvailable)
                throw new NotSupportedException("此模块缺少完整限速 ABI；请构建 belt-guard-robust-native 后点击应用并重置。");
            if(enableShapeGuard && !ShapeGuardAvailable)
                throw new NotSupportedException("此模块缺少完整形状保护 ABI；请构建 belt-guard-robust-native 后点击应用并重置。");
            var native=NativeConfiguration.From(config);
            if(NativeLibrary.TryGetExport(library,"dango_belt_calibration_schema",out var schemaAddress) &&
                NativeLibrary.TryGetExport(library,"dango_belt_read_diagnostics",out var diagnosticsAddress) &&
                NativeLibrary.TryGetExport(library,"dango_belt_create_calibrated",out var createAddress))
            {
                CalibrationSchema=Marshal.PtrToStringUTF8(Marshal.GetDelegateForFunctionPointer<SchemaDelegate>(schemaAddress)());
                diagnostics=Marshal.GetDelegateForFunctionPointer<DiagnosticsDelegate>(diagnosticsAddress);
                if(calibration is not null)
                {
                    var values=calibration.ToNative();
                    Check(Marshal.GetDelegateForFunctionPointer<CreateCalibratedDelegate>(createAddress)(in native,0,0,shortBones?1:0,values,values.Length,out handle),"create calibrated");
                }
            }
            else if(calibration is not null) throw new NotSupportedException("Native belt module does not support calibration; calibrated parameters were not applied.");
            if(calibration is null) Check(Export<CreateDelegate>("dango_belt_create")(in native,0,0,shortBones?1:0,out handle),"create");
            if(handle==0) throw new InvalidOperationException("Belt create returned null handle");
            if(enableShapeGuard)
            {
                Check(shapeGuard!(handle,1),"enable shape guard before first step");
                ShapeGuardEnabled=true;
            }
            if(enableSpeedSaturation)
            {
                Check(speedSaturation!(handle,1),"enable speed saturation before first step");
                SpeedSaturationEnabled=true;
            }
            if(enableWhaleDrive)
            {
                Check(whaleDrive!(handle,1),"enable whale drive before first step");
                WhaleDriveEnabled=true;
            }
        }
        catch { if(handle!=0) { destroy?.Invoke(handle); handle=0; } NativeLibrary.Free(library); library=0; throw; }
    }
    public void Step(double dt,LabBeltInput input)
    {
        VerifyAccess();
        if(!Finite(dt)||dt<=0||dt>1d/30 || !Finite(input.Horizontal)||Math.Abs(input.Horizontal)>1 ||
            !Finite(input.Stretch)||input.Stretch is <0 or >1 || !Finite(input.Downward)||input.Downward is <0 or >1 ||
            !Finite(input.DragTargetX)||!Finite(input.DragTargetY)) throw new ArgumentOutOfRangeException(nameof(input));
        var controls=NativeControls(input);
        Check(step(handle,(float)dt,controls,controls.Length),"step");
    }
    internal static float[] NativeControls(LabBeltInput input) => [(float)input.Horizontal,(float)input.Downward,input.JumpPressed?1:0,
        input.DragActive?1:0,(float)input.DragTargetX,(float)input.DragTargetY,(float)input.Stretch,input.InputActive?1:0,input.CancelForces?1:0];
    public LabBeltFrame Read()
    {
        VerifyAccess();
        float[] scaffold=new float[68],curve=new float[512],stats=new float[64];
        Check(read(handle,scaffold,scaffold.Length,curve,curve.Length,stats,stats.Length),"read");
        if(scaffold.Concat(curve).Concat(stats).Any(x=>!float.IsFinite(x))) throw new InvalidOperationException("Belt returned nonfinite state");
        return new(scaffold,curve,stats);
    }
    public void SetOriginalGroundJump(bool enabled=true)
    {
        VerifyAccess();
        if(originalGroundJump is null)throw new NotSupportedException("Native belt module does not support the original grounded jump setting.");
        Check(originalGroundJump(handle,enabled?1:0),"set original grounded jump");
        OriginalGroundJumpEnabled=enabled;
    }
    public float[] ReadDiagnostics()
    {
        VerifyAccess();
        if(diagnostics is null)throw new NotSupportedException("Native belt module does not export calibration diagnostics.");
        float[] values=new float[48];
        Check(diagnostics(handle,values,values.Length),"read diagnostics");
        if(values.Any(x=>!float.IsFinite(x)))throw new InvalidOperationException("Belt returned nonfinite diagnostics");
        return values;
    }
    public float[] ReadShapeGuard()
    {
        VerifyAccess();
        if(!ShapeGuardAvailable) throw new NotSupportedException("Native belt module does not export the complete shape guard ABI.");
        float[] values=new float[24];
        Check(shapeGuardDiagnostics!(handle,values,values.Length),"read shape guard");
        if(values.Any(x=>!float.IsFinite(x))) throw new InvalidOperationException("Belt returned nonfinite shape guard diagnostics");
        return values;
    }
    public double[] ReadSpeedLimit()
    {
        VerifyAccess();
        if(!SpeedSaturationAvailable) throw new NotSupportedException("Native belt module does not export the complete speed saturation ABI.");
        var values=ReadDoubleDiagnostics(speedLimitDiagnostics!,12,"read speed limit");
        if(values[0]!=1) throw new NotSupportedException("Unsupported belt speed limit schema.");
        return values;
    }
    public double[] ReadWhaleDrive()
    {
        VerifyAccess();
        if(!WhaleDriveAvailable) throw new NotSupportedException("Native belt module does not export the complete whale drive ABI.");
        var values=ReadDoubleDiagnostics(whaleDriveDiagnostics!,16,"read whale drive");
        if(values[0]!=1) throw new NotSupportedException("Unsupported whale drive diagnostic schema.");
        return values;
    }
    public double[] ReadDebugState()
    {
        VerifyAccess();
        if(!DebugStateAvailable) throw new NotSupportedException("Native guarded belt module does not export the numeric snapshot ABI.");
        var values=ReadDoubleDiagnostics(debugState!,200,"read snapshot");
        if(values[0]!=1 || values[1]!=200) throw new NotSupportedException("Unsupported numeric belt snapshot schema.");
        return values;
    }
    public double[] ReadSolverDiagnostics()
    {
        VerifyAccess();
        if(!SolverDiagnosticsAvailable) throw new NotSupportedException("Native guarded belt module does not export solver diagnostics.");
        // Nonfinite objective values are useful failure evidence; the physical snapshot remains finite-only.
        var values=ReadDoubleDiagnostics(solverDiagnostics!,24,"read solver diagnostics",requireFinite:false);
        if(values[0]!=1) throw new NotSupportedException("Unsupported belt solver diagnostics schema.");
        return values;
    }
    private double[] ReadDoubleDiagnostics(DoubleDiagnosticsDelegate reader,int count,string operation,bool requireFinite=true)
    {
        var values=new double[count]; Check(reader(handle,values,count),operation);
        if(requireFinite && values.Any(x=>!double.IsFinite(x))) throw new InvalidOperationException("Belt returned nonfinite numeric diagnostics");
        return values;
    }
    public void SetBounds(double left,double right)
    {
        VerifyAccess();
        if(!Finite(left)||!Finite(right)||left>=right) throw new ArgumentOutOfRangeException(nameof(left));
        Check(bounds(handle,(float)left,(float)right),"bounds");
    }
    public void Dispose()
    {
        if(library==0)return;
        VerifyAccess(); var error=destroy(handle); handle=0; NativeLibrary.Free(library);library=0;
        Check(error,"destroy");
    }
    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(library==0||handle==0,this);
        if(ownerThread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Belt world thread ownership violation");
    }
    private static bool Finite(double value)=>double.IsFinite(value)&&Math.Abs(value)<=float.MaxValue;
    private static void Check(int result,string operation)
    {
        if(result==0) return;
        var reason=result switch { -605 => "形状保护或求解器拒绝本帧", -606 => "形状认证工作预算耗尽", _ => "原生模块拒绝操作" };
        throw new InvalidOperationException($"Belt {operation} failed ({result}): {reason}; prior accepted state preserved");
    }
    private T Export<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OriginalGroundJumpDelegate(nint handle,int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint SchemaDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DoubleDiagnosticsDelegate(nint handle,[Out]double[] values,int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DiagnosticsDelegate(nint handle,[Out]float[] values,int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateCalibratedDelegate(in NativeConfiguration config,float x,float y,int bones,[In]float[] parameters,int count,out nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int VersionDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateDelegate(in NativeConfiguration config,float x,float y,int bones,out nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StepDelegate(nint handle,float dt,[In]float[] controls,int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadDelegate(nint handle,[Out]float[] scaffold,int sc,[Out]float[] curve,int cc,[Out]float[] stats,int ss);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BoundsDelegate(nint handle,float left,float right);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DestroyDelegate(nint handle);
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
