using Dango.Desktop.Physics.Tuning;

namespace Dango.Physics;

public sealed record DangoBeltCalibrationParameters
{
    public double RadialStiffnessScale { get; init; }=1;
    public double RadialDampingScale { get; init; }=1;
    public double BeltStretchScale { get; init; }=1;
    public double BeltBendScale { get; init; }=1;
    public double AreaScale { get; init; }=1;
    public double ShortBoneStiffnessScale { get; init; }=1;
    public double ShortBoneDampingScale { get; init; }=1;
    public double ShapeDampingScale { get; init; }=1;
    public double OverallDamping { get; init; }
    public double GroundedBrake { get; init; }
    public double GroundedHorizontalDamping { get; init; }
    public static DangoBeltCalibrationParameters Default { get; }=new();
    public static DangoBeltCalibrationParameters ForConfiguration(DangoSelfPhysicsParameters config) => new()
    {
        RadialStiffnessScale=1.4, AreaScale=1.4, BeltBendScale=4,
        OverallDamping=config.OverallDamping, GroundedBrake=35, GroundedHorizontalDamping=60
    };
    internal float[] ToNative()
    {
        double[] values=[RadialStiffnessScale,RadialDampingScale,BeltStretchScale,BeltBendScale,AreaScale,
            ShortBoneStiffnessScale,ShortBoneDampingScale,ShapeDampingScale,OverallDamping,GroundedBrake,GroundedHorizontalDamping];
        if(values.Any(x=>!double.IsFinite(x)||x<0||x>10000))
            throw new ArgumentOutOfRangeException(nameof(values),"Calibration values must be finite nonnegative floats; native schema defines additional ranges.");
        return values.Select(x=>(float)x).ToArray();
    }
}
