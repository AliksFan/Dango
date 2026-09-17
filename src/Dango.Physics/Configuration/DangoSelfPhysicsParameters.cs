namespace Dango.Desktop.Physics.Tuning;

public sealed record DangoSelfPhysicsParameters
{
    public int NodeCount { get; init; } = 16;
    public int SubStepCount { get; init; } = 4;
    public double RadialRestLength { get; init; } = 0.68;
    public double TopInwardForce { get; init; } = 0.5;
    public double RadialMinimumRatio { get; init; } = 0.65;
    public double RadialMaximumRatio { get; init; } = 1.22;
    public double PerimeterMinimumRatio { get; init; } = 0.73;
    public double PerimeterMaximumRatio { get; init; } = 1.07;
    public double RadialElasticity { get; init; } = 5.2;
    public double PerimeterElasticity { get; init; } = 20.1;
    public double RadialDamping { get; init; } = 1.85;
    public double PerimeterDamping { get; init; } = 1.85;
    public double AreaPreservationStrength { get; init; } = 4.8;
    public double GroundDeformationStrength { get; init; } = 3.5;
    public double BoneRepulsionDistance { get; init; } = 0.09;
    public double BoneRepulsionStrength { get; init; } = 0.3;
    public double BoneRepulsionMaximumForce { get; init; } = 3.5;
    public double CoreExclusionRatio { get; init; } = 0.25;
    public double PerimeterAngleStrength { get; init; } = 10;
    public double LocalAngleBalanceStrength { get; init; } = 1.7;
    public double Gravity { get; init; } = 9.8;
    public double CoreMass { get; init; } = 0.12;
    public double NodeMass { get; init; } = 0.015;
    public double DropHeight { get; init; } = 0.42;
    public double GroundHeight { get; init; } = -0.72;
    public double NodeRadius { get; init; } = 0.045;
    public double GroundFriction { get; init; } = 0.72;
    public double Restitution { get; init; } = 0.02;
    public double ContactHertz { get; init; } = 28;
    public double OverallDamping { get; init; } = 1.8;
    public double DragStiffness { get; init; } = 10;
    public double DragDamping { get; init; } = 2.2;
    public double DragMaximumForce { get; init; } = 8;
    public double HorizontalForce { get; init; } = 2.4;
    public double MaximumHorizontalSpeed { get; init; } = 3.2;
    public double AirControlRatio { get; init; } = 0.35;
    public double DownwardForce { get; init; } = 2.2;
    public double JumpImpulse { get; init; } = 0.42;
    public double JumpCooldownMilliseconds { get; init; } = 450;
    public double MaximumSpeed { get; init; } = 12;
    public double MinimumArea { get; init; } = 0.42;

    public static DangoSelfPhysicsParameters CreateDefault() => new();

    public void Validate()
    {
        Choice(NodeCount, 16, nameof(NodeCount));
        Choice(SubStepCount, 2, 4, nameof(SubStepCount));
        Range(RadialRestLength, 0.45, 0.90, nameof(RadialRestLength));
        Range(TopInwardForce, 0, 1, nameof(TopInwardForce));
        Range(RadialMinimumRatio, 0.65, 1, nameof(RadialMinimumRatio));
        Range(RadialMaximumRatio, 1, 1.35, nameof(RadialMaximumRatio));
        Range(PerimeterMinimumRatio, 0.65, 1, nameof(PerimeterMinimumRatio));
        Range(PerimeterMaximumRatio, 1, 1.35, nameof(PerimeterMaximumRatio));
        Range(RadialElasticity, 0.5, 20, nameof(RadialElasticity));
        Range(PerimeterElasticity, 0.5, 25, nameof(PerimeterElasticity));
        Range(RadialDamping, 0, 2, nameof(RadialDamping));
        Range(PerimeterDamping, 0, 2, nameof(PerimeterDamping));
        Range(AreaPreservationStrength, 0, 20, nameof(AreaPreservationStrength));
        Range(GroundDeformationStrength, 0, 10, nameof(GroundDeformationStrength));
        Range(BoneRepulsionDistance, 0.02, 0.30, nameof(BoneRepulsionDistance));
        Range(BoneRepulsionStrength, 0, 10, nameof(BoneRepulsionStrength));
        Range(BoneRepulsionMaximumForce, 0.1, 20, nameof(BoneRepulsionMaximumForce));
        Range(CoreExclusionRatio, 0.05, 0.60, nameof(CoreExclusionRatio));
        Range(PerimeterAngleStrength, 0, 20, nameof(PerimeterAngleStrength));
        Range(LocalAngleBalanceStrength, 0, 20, nameof(LocalAngleBalanceStrength));
        Range(Gravity, 0.5, 25, nameof(Gravity));
        Range(CoreMass, 0.02, 0.5, nameof(CoreMass));
        Range(NodeMass, 0.003, 0.1, nameof(NodeMass));
        Range(DropHeight, 0, 1.5, nameof(DropHeight));
        Range(GroundHeight, -1.4, -0.45, nameof(GroundHeight));
        Range(NodeRadius, 0.02, 0.10, nameof(NodeRadius));
        Range(GroundFriction, 0, 1.5, nameof(GroundFriction));
        Range(Restitution, 0, 0.5, nameof(Restitution));
        Range(ContactHertz, 1, 60, nameof(ContactHertz));
        Range(OverallDamping, 0, 8, nameof(OverallDamping));
        Range(DragStiffness, 0.1, 40, nameof(DragStiffness));
        Range(DragDamping, 0, 12, nameof(DragDamping));
        Range(DragMaximumForce, 0.2, 30, nameof(DragMaximumForce));
        Range(HorizontalForce, 0.1, 12, nameof(HorizontalForce));
        Range(MaximumHorizontalSpeed, 0.2, 8, nameof(MaximumHorizontalSpeed));
        Range(AirControlRatio, 0, 1, nameof(AirControlRatio));
        Range(DownwardForce, 0, 12, nameof(DownwardForce));
        Range(JumpImpulse, 0.05, 2, nameof(JumpImpulse));
        Range(JumpCooldownMilliseconds, 100, 1500, nameof(JumpCooldownMilliseconds));
        Range(MaximumSpeed, 2, 30, nameof(MaximumSpeed));
        Range(MinimumArea, 0.15, 1.2, nameof(MinimumArea));
        if (RadialMinimumRatio > RadialMaximumRatio) throw new ArgumentOutOfRangeException(nameof(RadialMinimumRatio));
        if (PerimeterMinimumRatio > PerimeterMaximumRatio) throw new ArgumentOutOfRangeException(nameof(PerimeterMinimumRatio));
    }

    public bool RequiresRebuildComparedWith(DangoSelfPhysicsParameters other) =>
        NodeCount != other.NodeCount || SubStepCount != other.SubStepCount || RadialRestLength != other.RadialRestLength ||
        RadialMinimumRatio != other.RadialMinimumRatio || RadialMaximumRatio != other.RadialMaximumRatio ||
        PerimeterMinimumRatio != other.PerimeterMinimumRatio || PerimeterMaximumRatio != other.PerimeterMaximumRatio ||
        Gravity != other.Gravity || CoreMass != other.CoreMass || NodeMass != other.NodeMass || DropHeight != other.DropHeight ||
        GroundHeight != other.GroundHeight || NodeRadius != other.NodeRadius || GroundFriction != other.GroundFriction ||
        Restitution != other.Restitution || ContactHertz != other.ContactHertz || OverallDamping != other.OverallDamping;

    public DangoSelfPhysicsParameters WithNumericValue(string propertyName, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        var property = typeof(DangoSelfPhysicsParameters).GetProperty(propertyName)
            ?? throw new ArgumentException("未知参数。", nameof(propertyName));
        if (property.PropertyType != typeof(double)) throw new ArgumentException("参数不是可调数值。", nameof(propertyName));
        var candidate = this with { };
        property.SetValue(candidate, value);
        candidate.Validate();
        return candidate;
    }

    private static void Range(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name, $"参数必须位于 [{minimum}, {maximum}]。");
    }

    private static void Choice(int value, int expected, string name)
    {
        if (value != expected) throw new ArgumentOutOfRangeException(name, $"参数必须是 {expected}。");
    }

    private static void Choice(int value, int first, int second, string name)
    {
        if (value != first && value != second) throw new ArgumentOutOfRangeException(name, $"参数必须是 {first} 或 {second}。");
    }
}
