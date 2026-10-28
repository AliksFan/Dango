using System.IO;
using System.Runtime.InteropServices;
using Dango.Desktop.Physics.Tuning;

namespace Dango.Physics;

public enum DangoMuscleShakeProfile { Original = 0, SoftShoulders = 1, EyeAlignedRandom = 2, EyeAlignedRandomWide = 3 }

public readonly record struct DangoBeltInput(double Horizontal=0, double Stretch=0, double Downward=0,
    bool JumpPressed=false, bool DragActive=false, double DragTargetX=0, double DragTargetY=0,
    bool InputActive=false, bool CancelForces=false)
{
    public double MuscleShakeTime { get; init; }
    public double MuscleShakeStrength { get; init; }
    public DangoMuscleShakeProfile MuscleShakeProfile { get; init; }
    public double MuscleShakeAxisX { get; init; }
    public double MuscleShakeAxisY { get; init; }
    public uint MuscleShakeSeed { get; init; }
}

/// <summary>Native world coordinates. Scaffold has core then 16 massless attachments, each x/y/vx/vy.</summary>
public sealed record DangoBeltFrame(float[] Scaffold, float[] Curve, float[] Stats)
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

/// <summary>Product continuous belt ABI; explicit model identity and single native owner.</summary>
public sealed class DangoBeltWorld : IDisposable
{
    // The inherited native token registry is module-global. Serialize calls from
    // independent managed owners without changing source solver arithmetic.
    private static readonly object NativeGate=new();
    private readonly struct NativeScope : IDisposable
    {
        public NativeScope(){Monitor.Enter(NativeGate);}
        public void Dispose()=>Monitor.Exit(NativeGate);
    }
    private NativeScope EnterNative(){VerifyAccess();return new();}
    public const string Model = "continuous-belt-v1";
    public const string TuningVersion = "C455-F-v1";
    public const double FixedDt = 0.0166666;
    public DangoSelfPhysicsParameters Configuration { get; private set; }
    public DangoBeltCalibrationParameters Calibration { get; private set; }
    private readonly ReconfigureDelegate reconfigure;
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
    private readonly DoubleDiagnosticsDelegate? debugState, solverDiagnostics, speedLimitDiagnostics, whaleDriveDiagnostics, radialDriveDiagnostics, rearDriveDiagnostics, forceLedgerDiagnostics;
    private readonly OriginalGroundJumpDelegate? speedSaturation, whaleDrive, radialDrive;
    private readonly ResetProfileDelegate? resetProfile;
    private readonly DoubleDiagnosticsDelegate? readProfile;
    public bool ProfileAvailable => resetProfile is not null && readProfile is not null;
    public bool WhaleDriveAvailable => whaleDrive is not null && whaleDriveDiagnostics is not null;
    public bool WhaleDriveEnabled { get; private set; }
    public bool RadialDriveAvailable => radialDrive is not null && radialDriveDiagnostics is not null;
    public bool RadialDriveEnabled { get; private set; }
    public const string RadialDriveSchema = "v3:16;0=version3,1=enabled,2=attemptTick,3=activeSubsteps,4=lastPointCount8,5=lastOriginalMagnitudeBudget,6=lastAppliedMagnitudeSum,7=lastNetFx,8=lastNetFy,9=maxAbsActualTorqueCore,10=maxHorizontalReferenceError,11=maxVerticalTwoThirdsReferenceError,12=lastReferenceNetFx64,13=mainEndpointWorkSubset,14=guardWorkPeak,15=lastReferenceNetFy64;scope=mainEightForcesOnly;rejectedFrame=rollbackExceptAttempt;lastFields=lastSubstep;zeroBudget=clear4..8and12and15;frameAggregates=3and9..11";
    public bool RearDriveAvailable => rearDriveDiagnostics is not null;
    public bool RearDriveEnabled => RadialDriveEnabled && RearDriveAvailable;
    public const string RearDriveSchema = "v1:16;0=version1,1=enabledWithRadial,2=attemptTick,3=activeSubsteps,4=lastPointCount0to16,5=lastRequestedDownMagnitude,6=lastAppliedDownMagnitude,7=lastNetFxZero,8=lastNetFyNonpositive,9=maxAbsActualTorqueCore,10=maxBudgetError,11=lastSourceEightPushFy,12=rearEndpointWorkSubset,13=guardWorkPeak,14=lastMovementSign,15=lastBodyMidX;budget=abs(mainEightPushFy)/3;position=actualScaffoldAttachment;mapping=scaleCurvePlusOneMinusScaleCore;rejectedFrame=rollbackExceptAttempt;zeroBudget=clear4..8and11and14and15;frameAggregates=3and9and10";
    public bool ForceLedgerAvailable => forceLedgerDiagnostics is not null;
    public const string ForceLedgerSchema = "v1:1024;header0=version,1=capacity,2=lastSuccessfulSolverAttemptTick,3=lastSubstepZeroBased,4=rowCount,5=sourceMask,6=rowWidth6,7=referenceMaterialQuadrature64;rowsFrom8:x,y,fx,fy,kind,thetaOrPart;kind1=horizontal16MaterialBinResultants,2=vertical16MaterialBinResultants,3=shape64ActualQuadratureForces,4=dragActualPointForce,5=8ActualGroupedForces,6=rearScaffoldActualPointForce;positions=actualPreIntegration;scope=activeForcesOnly,noGravityOrContact;rejectedFrame=previousSuccessfulLedger";
    public const string WhaleDriveSchema = "v1:16;0=version,1=enabled,2=attemptTick,3=signedGain,4=activeSubsteps,5=maxAbsNetFx,6=maxAbsNetFy,7=maxAbsTorque,8=shapeEndpointWorkSubset,9=maxPointTraction,10=maxGeneralizedForce,11=suppressionBits,12=gainBefore,13=gainAfter,14=guardWorkPeak,15=reserved;suppression:1=air,2=drag,4=jump,8=WSramp,16=cancel";
    public bool SpeedSaturationAvailable => speedSaturation is not null && speedLimitDiagnostics is not null;
    public bool SpeedSaturationEnabled { get; private set; }
    public const string SpeedLimitSchema = "v1:12;0=version,1=enabled,2=attemptTick,3=limitedSubsteps,4=minScale,5=maxBeforeUpper,6=maxAfterUpper,7=deltaKE,8=deltaPx,9=deltaPy,10=deltaL,11=reserved";
    public const string DebugStateSchema = "v1:200;0=version,1=count,2..37=q36,38..73=v36,74=radius,75=scale,76=left,77=right,78=ramp,79=cooldown,80=dragTheta,81=shortBones,82=grounded,83=dragging,84=originalJump,85=hasStepped,86=shapeGuard,87..127=config41,128..138=calibration11,139=guardAttemptTick,140..175=mass36,176=speedSaturation,177=whaleDrive,178=whaleSignedGain,179=radialDrive,180..199=reserved";
    public const string SolverDiagnosticsSchema = "v1:24;0=version,1=attemptTick,2=returnCode,3=returnSite,4=substep,5=GNiteration,6=lineSearch,7=residual,8=objective,9=directional,10=trialObjective,11=factor,12=contactResidual,13=penetration,14=speed,15=stage,16=residualValidatedLineSearchStops,17=dampedBfgsUpdates,18=gaussNewtonResets,19=speedCertificateSamples,20=speedCoreOrCurveSampleMaximum,21=speedCertifiedUpper,22=lastRefinementCoarseUpper,23=lastRefinementCertifiedUpper";
    public const string SolverFailureSites = "1=initialResidual,2=nonfiniteObjective,3=Cholesky,4=ArmijoExhausted,5=GNResidualCap,6=integratedPathGuard,7=NewtonWorkBudget,8=groundWorkBudget,9=groundFallbackGuard,10=groundPenetration,11=contactResidual,12=contactEnergy,13=speed,14=bounds,15=finalResidual,16=area,17=statsNonfinite,18=energyDiagNonfinite,19=initialGuard,20=regionForce,21=speedSaturationUnresolved,22=whaleForce,23=radialForceDomain,24=rearForceDomain,31=initialDerivative,32=initialRadialLength,33=initialShortEdge,34=initialArea,35=initialBarrierMaterialDomain;stage:1=forces,2=integration,3=contact,4=speedBounds,5=final";
    public bool DebugStateAvailable => ShapeGuardEnabled && debugState is not null;
    public bool SolverDiagnosticsAvailable => ShapeGuardEnabled && solverDiagnostics is not null;
    public const string ShapeGuardSchema = "v1:version,enabled,stepAttemptTick,lastStepReturnCode,certificateWork,certificateCalls,maxDepth,certifiedLeaves,refusedCandidates,lastRefusalReason,refusalStage,acceptedCrossLower,acceptedClearanceLower,winding,initialGuardElastic,finalGuardElastic,peakGuardCoreForce,guardEndpointForceWork,groundRigidFallbacks,hardClearance,hardCross,workBudget,acceptedSubsteps,reserved;refusalReason:1=cross,2=clearance,3=budget,4=winding,5=nonfinite,6=solver,7=ground,8=uncertain,9=materialDomain";
    public bool ShapeGuardAvailable => shapeGuard is not null && shapeGuardDiagnostics is not null;
    public bool ShapeGuardEnabled { get; private set; }
    public bool OriginalGroundJumpEnabled { get; private set; }
    public string? CalibrationSchema { get; }
    public DangoBeltWorld(string module,DangoSelfPhysicsParameters config,bool shortBones=true,DangoBeltCalibrationParameters? calibration=null,bool enableShapeGuard=true,bool enableSpeedSaturation=true,bool enableWhaleDrive=false,bool enableRadialDrive=true)
    {
        config.Validate();
        if(config.SubStepCount!=4) throw new ArgumentOutOfRangeException(nameof(config),"The product belt requires four substeps.");
        Configuration=config;
        Calibration=calibration??DangoBeltCalibrationParameters.ForConfiguration(config);
        calibration=Calibration;
        using var nativeConstruction=new NativeScope();
        library=NativeLibrary.Load(Path.GetFullPath(module));
        try
        {
            if(Export<VersionDelegate>("dango_belt_version")()!=1) throw new InvalidOperationException("Unsupported belt ABI");
            if(Export<VersionDelegate>("dango_belt_product_version")()!=1 ||
                Marshal.PtrToStringUTF8(Export<SchemaDelegate>("dango_belt_product_model")())!=Model ||
                Marshal.PtrToStringUTF8(Export<SchemaDelegate>("dango_belt_product_tuning")())!=TuningVersion ||
                Export<VersionDelegate>("dango_belt_product_capabilities")()!=127)
                throw new NotSupportedException("Unsupported product belt model, tuning or capabilities.");
            reconfigure=Export<ReconfigureDelegate>("dango_belt_reconfigure");
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
            if(NativeLibrary.TryGetExport(library,"dango_belt_set_radial_drive",out var radialAddress))
                radialDrive=Marshal.GetDelegateForFunctionPointer<OriginalGroundJumpDelegate>(radialAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_read_radial_drive",out var radialDiagnosticsAddress))
                radialDriveDiagnostics=Marshal.GetDelegateForFunctionPointer<DoubleDiagnosticsDelegate>(radialDiagnosticsAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_read_rear_drive",out var rearDiagnosticsAddress))
                rearDriveDiagnostics=Marshal.GetDelegateForFunctionPointer<DoubleDiagnosticsDelegate>(rearDiagnosticsAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_read_force_ledger",out var forceLedgerAddress))
                forceLedgerDiagnostics=Marshal.GetDelegateForFunctionPointer<DoubleDiagnosticsDelegate>(forceLedgerAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_reset_profile",out var resetProfileAddress))
                resetProfile=Marshal.GetDelegateForFunctionPointer<ResetProfileDelegate>(resetProfileAddress);
            if(NativeLibrary.TryGetExport(library,"dango_belt_read_profile",out var readProfileAddress))
                readProfile=Marshal.GetDelegateForFunctionPointer<DoubleDiagnosticsDelegate>(readProfileAddress);
            if(enableWhaleDrive && !WhaleDriveAvailable)
                throw new NotSupportedException("Native belt module lacks whale-drive capabilities.");
            if(enableRadialDrive && !RadialDriveAvailable)
                throw new NotSupportedException("Native belt module lacks radial-drive capabilities.");
            if(enableRadialDrive && !RearDriveAvailable)
                throw new NotSupportedException("Native belt module lacks rear-pressure capabilities.");
            if(enableRadialDrive && enableWhaleDrive)
                throw new ArgumentException("Radial driving and extra shaping forces are mutually exclusive.");
            if(enableSpeedSaturation && !SpeedSaturationAvailable)
                throw new NotSupportedException("Native belt module lacks speed-saturation capabilities.");
            if(enableShapeGuard && !ShapeGuardAvailable)
                throw new NotSupportedException("Native belt module lacks shape-guard capabilities.");
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
            if(enableRadialDrive)
            {
                Check(radialDrive!(handle,1),"enable radial drive before first step");
                RadialDriveEnabled=true;
            }
            SetOriginalGroundJump();
        }
        catch { if(handle!=0) { destroy?.Invoke(handle); handle=0; } NativeLibrary.Free(library); library=0; throw; }
    }
    /// <summary>Atomically migrate this live native body. Failure preserves its complete prior state.</summary>
    public void Reconfigure(DangoSelfPhysicsParameters config,DangoBeltCalibrationParameters? calibration=null)
    {
        using var nativeAccess=EnterNative();
        config.Validate();
        if(config.SubStepCount!=4) throw new ArgumentOutOfRangeException(nameof(config));
        calibration??=DangoBeltCalibrationParameters.ForConfiguration(config);
        var values=calibration.ToNative();var native=NativeConfiguration.From(config);
        Check(reconfigure(handle,in native,values,values.Length),"reconfigure");
        Configuration=config;Calibration=calibration;
    }
    public void Step(double dt,DangoBeltInput input)
    {
        using var nativeAccess=EnterNative();
        if (input.MuscleShakeStrength != 0 || input.MuscleShakeTime != 0 || input.MuscleShakeProfile != DangoMuscleShakeProfile.Original || input.MuscleShakeSeed != 0)
            throw new NotSupportedException("Internal muscles require the room's explicit single-step capability.");
        if(!Finite(dt)||dt<=0||dt>1d/30 || !Finite(input.Horizontal)||Math.Abs(input.Horizontal)>1 ||
            !Finite(input.Stretch)||input.Stretch is <0 or >1 || !Finite(input.Downward)||input.Downward is <0 or >1 ||
            !Finite(input.DragTargetX)||!Finite(input.DragTargetY)) throw new ArgumentOutOfRangeException(nameof(input));
        var controls=NativeControls(input);
        Check(step(handle,(float)dt,controls,controls.Length),"step");
    }
    internal static float[] NativeControls(DangoBeltInput input) => [(float)input.Horizontal,(float)input.Downward,input.JumpPressed?1:0,
        input.DragActive?1:0,(float)input.DragTargetX,(float)input.DragTargetY,(float)input.Stretch,input.InputActive?1:0,input.CancelForces?1:0];
    public DangoBeltFrame Read()
    {
        using var nativeAccess=EnterNative();
        float[] scaffold=new float[68],curve=new float[512],stats=new float[64];
        Check(read(handle,scaffold,scaffold.Length,curve,curve.Length,stats,stats.Length),"read");
        if(scaffold.Concat(curve).Concat(stats).Any(x=>!float.IsFinite(x))) throw new InvalidOperationException("Belt returned nonfinite state");
        return new(scaffold,curve,stats);
    }
    public void SetOriginalGroundJump(bool enabled=true)
    {
        using var nativeAccess=EnterNative();
        if(originalGroundJump is null)throw new NotSupportedException("Native belt module does not support the original grounded jump setting.");
        Check(originalGroundJump(handle,enabled?1:0),"set original grounded jump");
        OriginalGroundJumpEnabled=enabled;
    }
    public float[] ReadDiagnostics()
    {
        using var nativeAccess=EnterNative();
        if(diagnostics is null)throw new NotSupportedException("Native belt module does not export calibration diagnostics.");
        float[] values=new float[48];
        Check(diagnostics(handle,values,values.Length),"read diagnostics");
        if(values.Any(x=>!float.IsFinite(x)))throw new InvalidOperationException("Belt returned nonfinite diagnostics");
        return values;
    }
    public float[] ReadShapeGuard()
    {
        using var nativeAccess=EnterNative();
        if(!ShapeGuardAvailable) throw new NotSupportedException("Native belt module does not export the complete shape guard ABI.");
        float[] values=new float[24];
        Check(shapeGuardDiagnostics!(handle,values,values.Length),"read shape guard");
        if(values.Any(x=>!float.IsFinite(x))) throw new InvalidOperationException("Belt returned nonfinite shape guard diagnostics");
        return values;
    }
    public double[] ReadSpeedLimit()
    {
        using var nativeAccess=EnterNative();
        if(!SpeedSaturationAvailable) throw new NotSupportedException("Native belt module does not export the complete speed saturation ABI.");
        var values=ReadDoubleDiagnostics(speedLimitDiagnostics!,12,"read speed limit");
        if(values[0]!=1) throw new NotSupportedException("Unsupported belt speed limit schema.");
        return values;
    }
    public double[] ReadForceLedger()
    {
        using var nativeAccess=EnterNative();
        if(!ForceLedgerAvailable) throw new NotSupportedException("Native belt module does not export active-force distribution diagnostics.");
        var values=ReadDoubleDiagnostics(forceLedgerDiagnostics!,1024,"read active-force ledger");
        if(values[0]!=1 || values[1]!=1024 || values[6]!=6 || values[7]!=64)
            throw new NotSupportedException("Unsupported active-force ledger schema.");
        if(values[2]<0 || values[2]!=Math.Truncate(values[2]) || values[3]<-1 || values[3]!=Math.Truncate(values[3]) ||
            values[4] is <0 or >97 || values[4]!=Math.Truncate(values[4]))
            throw new InvalidOperationException("Invalid active-force ledger header.");
        var mask=0;
        for(var i=0;i<(int)values[4];i++)
        {
            var kind=values[8+6*i+4];
            if(kind is <1 or >6 || kind!=Math.Truncate(kind)) throw new InvalidOperationException("Invalid active-force source.");
            mask|=1<<((int)kind-1);
        }
        if(values[5]!=mask) throw new InvalidOperationException("Active-force ledger source mask disagrees with its rows.");
        return values;
    }
    public double[] ReadWhaleDrive()
    {
        using var nativeAccess=EnterNative();
        if(!WhaleDriveAvailable) throw new NotSupportedException("Native belt module does not export the complete whale drive ABI.");
        var values=ReadDoubleDiagnostics(whaleDriveDiagnostics!,16,"read whale drive");
        if(values[0]!=1) throw new NotSupportedException("Unsupported whale drive diagnostic schema.");
        return values;
    }
    public double[] ReadRadialDrive()
    {
        using var nativeAccess=EnterNative();
        if(!RadialDriveAvailable) throw new NotSupportedException("Native belt module does not export the complete radial drive ABI.");
        var values=ReadDoubleDiagnostics(radialDriveDiagnostics!,16,"read radial drive");
        if(values[0]!=3) throw new NotSupportedException("Unsupported radial drive diagnostic schema (expected v3).");
        return values;
    }
    public double[] ReadRearDrive()
    {
        using var nativeAccess=EnterNative();
        if(!RearDriveAvailable) throw new NotSupportedException("Native belt module does not export rear endpoint-force diagnostics.");
        var values=ReadDoubleDiagnostics(rearDriveDiagnostics!,16,"read rear endpoint drive");
        if(values[0]!=1) throw new NotSupportedException("Unsupported rear endpoint-force diagnostic schema.");
        return values;
    }
    public double[] ReadDebugState()
    {
        using var nativeAccess=EnterNative();
        if(!DebugStateAvailable) throw new NotSupportedException("Native guarded belt module does not export the numeric snapshot ABI.");
        var values=ReadDoubleDiagnostics(debugState!,200,"read snapshot");
        if(values[0]!=1 || values[1]!=200) throw new NotSupportedException("Unsupported numeric belt snapshot schema.");
        return values;
    }
    public double[] ReadSolverDiagnostics()
    {
        using var nativeAccess=EnterNative();
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
    // Profile counters belong to the calling thread, rather than a body or its rollback state.
    public void ResetProfile()
    {
        using var nativeAccess=EnterNative();
        if(!ProfileAvailable) throw new NotSupportedException("Native belt module does not export the optional profile ABI.");
        Check(resetProfile!(handle),"reset profile");
    }
    public double[] ReadProfile()
    {
        using var nativeAccess=EnterNative();
        if(!ProfileAvailable) throw new NotSupportedException("Native belt module does not export the optional profile ABI.");
        var values=ReadDoubleDiagnostics(readProfile!,48,"read profile");
        if(values[0]!=1 || values[1]!=16) throw new NotSupportedException("Unsupported belt profile schema.");
        return values;
    }
    // Raw bytes preserve signed zero and NaN payloads, including rejected-step telemetry.
    internal Dictionary<string,byte[]> ReadPerformanceState()
    {
        using var nativeAccess=EnterNative();
        float[] scaffold=new float[68],curve=new float[512],stats=new float[64],energy=new float[48],guard=new float[24];
        Check(read(handle,scaffold,scaffold.Length,curve,curve.Length,stats,stats.Length),"read performance frame");
        if(diagnostics is null || shapeGuardDiagnostics is null || debugState is null || solverDiagnostics is null ||
            speedLimitDiagnostics is null || radialDriveDiagnostics is null || rearDriveDiagnostics is null || forceLedgerDiagnostics is null)
            throw new NotSupportedException("Performance comparison requires every rear-pressure diagnostic export.");
        Check(diagnostics(handle,energy,energy.Length),"read performance diagnostics");
        Check(shapeGuardDiagnostics(handle,guard,guard.Length),"read performance guard");
        byte[] Doubles(DoubleDiagnosticsDelegate reader,int count) =>
            MemoryMarshal.AsBytes(ReadDoubleDiagnostics(reader,count,"read performance state",requireFinite:false).AsSpan()).ToArray();
        return new()
        {
            ["scaffold"]=MemoryMarshal.AsBytes(scaffold.AsSpan()).ToArray(),
            ["curve"]=MemoryMarshal.AsBytes(curve.AsSpan()).ToArray(),
            ["stats"]=MemoryMarshal.AsBytes(stats.AsSpan()).ToArray(),
            ["diagnostics"]=MemoryMarshal.AsBytes(energy.AsSpan()).ToArray(),
            ["shape"]=MemoryMarshal.AsBytes(guard.AsSpan()).ToArray(),
            ["debug"]=Doubles(debugState,200), ["solver"]=Doubles(solverDiagnostics,24),
            ["speed"]=Doubles(speedLimitDiagnostics,12), ["radial"]=Doubles(radialDriveDiagnostics,16),
            ["rear"]=Doubles(rearDriveDiagnostics,16), ["ledger"]=Doubles(forceLedgerDiagnostics,1024),
        };
    }
    public void SetBounds(double left,double right)
    {
        using var nativeAccess=EnterNative();
        if(!Finite(left)||!Finite(right)||left>=right) throw new ArgumentOutOfRangeException(nameof(left));
        Check(bounds(handle,(float)left,(float)right),"bounds");
    }
    public void Dispose()
    {
        if(library==0)return;
        using var nativeAccess=EnterNative(); var error=destroy(handle); handle=0; NativeLibrary.Free(library);library=0;
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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ResetProfileDelegate(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OriginalGroundJumpDelegate(nint handle,int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint SchemaDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DoubleDiagnosticsDelegate(nint handle,[Out]double[] values,int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DiagnosticsDelegate(nint handle,[Out]float[] values,int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateCalibratedDelegate(in NativeConfiguration config,float x,float y,int bones,[In]float[] parameters,int count,out nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReconfigureDelegate(nint handle,in NativeConfiguration config,[In]float[] parameters,int count);
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
