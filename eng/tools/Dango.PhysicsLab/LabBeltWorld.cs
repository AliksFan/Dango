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
    public bool OriginalGroundJumpEnabled { get; private set; }
    public string? CalibrationSchema { get; }
    public LabBeltWorld(string module,DangoSelfPhysicsParameters config,bool shortBones=true,LabBeltCalibrationParameters? calibration=null)
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
        }
        catch { NativeLibrary.Free(library); library=0; throw; }
    }
    public void Step(double dt,LabBeltInput input)
    {
        VerifyAccess();
        if(!Finite(dt)||dt<=0||dt>1d/30 || !Finite(input.Horizontal)||Math.Abs(input.Horizontal)>1 ||
            !Finite(input.Stretch)||input.Stretch is <0 or >1 || !Finite(input.Downward)||input.Downward is <0 or >1 ||
            !Finite(input.DragTargetX)||!Finite(input.DragTargetY)) throw new ArgumentOutOfRangeException(nameof(input));
        float[] controls=[(float)input.Horizontal,(float)input.Downward,input.JumpPressed?1:0,
            input.DragActive?1:0,(float)input.DragTargetX,(float)input.DragTargetY,(float)input.Stretch,
            input.InputActive?1:0,input.CancelForces?1:0];
        Check(step(handle,(float)dt,controls,controls.Length),"step");
    }
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
    { if(result!=0) throw new InvalidOperationException($"Belt {operation} failed ({result}); native rejected step preserves prior state"); }
    private T Export<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OriginalGroundJumpDelegate(nint handle,int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint SchemaDelegate();
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
