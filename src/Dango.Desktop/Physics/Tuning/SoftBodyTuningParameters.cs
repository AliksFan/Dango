using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dango.Desktop.Physics.Tuning;

public sealed record SoftBodyTuningParameters
{
    public double RestWidth { get; init; } = 1.4196926032660917;
    public double RestHeight { get; init; } = 1.192026897214217;
    public double TopRoundness { get; init; } = 0.17662760166506597;
    public double LowerBulge { get; init; } = 0.2008888888888887;
    public double BottomFlatness { get; init; } = 0.1173333333333337;
    public double GroundCompression { get; init; } = 0.0925335894972781;
    public double DropHeight { get; init; } = 0.5;
    public double ImpactSpeed { get; init; } = 1.7371757925072033;
    public double MaximumCompression { get; init; } = 0.27300672430355444;
    public double LateralExpansion { get; init; } = 0.10920268972142176;
    public double ReboundStrength { get; init; } = 0.06;
    public double LiftOffLimit { get; init; } = 0.035;
    public double SettleTime { get; init; } = 0.75;
    public double RadialElasticity { get; init; } = 7.953890489913556;
    public double ContourElasticity { get; init; } = 7.0172910662823895;
    public double RadialDamping { get; init; } = 0.92;
    public double ContourDamping { get; init; } = 0.88;
    public double OverallDamping { get; init; } = 2.3439000960614784;
    public double NodeMass { get; init; } = 0.032;
    public double GroundHeight { get; init; } = -0.64;
    public double NodeRadius { get; init; } = 0.06;
    public double Friction { get; init; } = 0.55;
    public double Restitution { get; init; } = 0.025;
    public double ContactSoftness { get; init; } = 21.00672430355428;
    public int NodeCount { get; init; } = 16;
    public int SubStepCount { get; init; } = 4;
    public double FixedStepSeconds { get; init; } = 1d / 60d;
    public double CoreFollowSpeed { get; init; } = 1.2;
    public double EyeFollow { get; init; } = 0.45;
    public double EyeOffsetX { get; init; }
    public double EyeOffsetY { get; init; } = -0.04;
    public double HatFollow { get; init; } = 0.72;
    public double HatOffsetX { get; init; }
    public double HatOffsetY { get; init; }
    public double HatRotationLimit { get; init; } = 0.24;
    public double HatDamping { get; init; } = 0.82;
    public double MaximumDeformation { get; init; } = 0.98;
    public double MaximumCentroidOffset { get; init; } = 0.28;
    public double MaximumSpeed { get; init; } = 8;
    public double MinimumContourArea { get; init; } = 0.38;

    [JsonIgnore]
    public double GroundedAspectRatio => RestWidth / RestHeight;

    public void Validate()
    {
        Range(RestWidth, 1.1, 1.9, nameof(RestWidth)); Range(RestHeight, 0.9, 1.7, nameof(RestHeight));
        Range(TopRoundness, 0, 1, nameof(TopRoundness)); Range(LowerBulge, 0, 0.4, nameof(LowerBulge));
        Range(BottomFlatness, 0, 1, nameof(BottomFlatness)); Range(GroundCompression, 0, 0.35, nameof(GroundCompression));
        Range(DropHeight, 0.05, 1.5, nameof(DropHeight)); Range(ImpactSpeed, 0.1, 4, nameof(ImpactSpeed));
        Range(MaximumCompression, 0.02, 0.4, nameof(MaximumCompression)); Range(LateralExpansion, 0, 0.4, nameof(LateralExpansion));
        Range(ReboundStrength, 0, 0.4, nameof(ReboundStrength)); Range(LiftOffLimit, 0, 0.2, nameof(LiftOffLimit));
        Range(SettleTime, 0.2, 3, nameof(SettleTime)); Range(RadialElasticity, 1, 20, nameof(RadialElasticity));
        Range(ContourElasticity, 1, 25, nameof(ContourElasticity)); Range(RadialDamping, 0, 2, nameof(RadialDamping));
        Range(ContourDamping, 0, 2, nameof(ContourDamping)); Range(OverallDamping, 0, 8, nameof(OverallDamping));
        Range(NodeMass, 0.005, 0.2, nameof(NodeMass)); Range(GroundHeight, -1.2, -0.55, nameof(GroundHeight));
        Range(NodeRadius, 0.025, 0.12, nameof(NodeRadius)); Range(Friction, 0, 1.5, nameof(Friction));
        Range(Restitution, 0, 0.5, nameof(Restitution)); Range(ContactSoftness, 1, 60, nameof(ContactSoftness));
        Choice(NodeCount, 12, 16, nameof(NodeCount)); Choice(SubStepCount, 2, 4, nameof(SubStepCount));
        Range(FixedStepSeconds, 1d / 240d, 1d / 30d, nameof(FixedStepSeconds)); Range(CoreFollowSpeed, 0.5, 12, nameof(CoreFollowSpeed));
        Range(EyeFollow, 0, 1, nameof(EyeFollow)); Range(EyeOffsetX, -0.3, 0.3, nameof(EyeOffsetX)); Range(EyeOffsetY, -0.3, 0.3, nameof(EyeOffsetY));
        Range(HatFollow, 0, 1, nameof(HatFollow)); Range(HatOffsetX, -0.3, 0.3, nameof(HatOffsetX)); Range(HatOffsetY, -0.3, 0.3, nameof(HatOffsetY));
        Range(HatRotationLimit, 0, 0.7, nameof(HatRotationLimit)); Range(HatDamping, 0, 1, nameof(HatDamping));
        Range(MaximumDeformation, 0.7, 1, nameof(MaximumDeformation)); Range(MaximumCentroidOffset, 0.1, 0.5, nameof(MaximumCentroidOffset));
        Range(MaximumSpeed, 2, 20, nameof(MaximumSpeed)); Range(MinimumContourArea, 0.2, 0.8, nameof(MinimumContourArea));
        if (LiftOffLimit > ReboundStrength) throw new ArgumentOutOfRangeException(nameof(LiftOffLimit), "离地上限不能大于回弹强度。");
    }

    public string ToJson()
    {
        Validate();
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public SoftBodyTuningParameters WithNumericValue(string propertyName, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        var property = typeof(SoftBodyTuningParameters).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new ArgumentException("未知参数。", nameof(propertyName));
        if (property.PropertyType != typeof(double) && property.PropertyType != typeof(int))
        {
            throw new ArgumentException("参数不是数值类型。", nameof(propertyName));
        }

        var candidate = this with { };
        object convertedValue;
        if (property.PropertyType == typeof(int))
        {
            convertedValue = checked((int)Math.Round(value));
        }
        else
        {
            convertedValue = value;
        }
        property.SetValue(candidate, convertedValue);
        candidate.Validate();
        return candidate;
    }

    public static SoftBodyTuningParameters FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var value = JsonSerializer.Deserialize<SoftBodyTuningParameters>(json, JsonOptions)
            ?? throw new JsonException("参数文件为空。");
        value.Validate();
        return value;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
    };

    private static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentOutOfRangeException(name, $"参数必须位于 [{min}, {max}]。");
    }

    private static void Choice(int value, int first, int second, string name)
    {
        if (value != first && value != second) throw new ArgumentOutOfRangeException(name, $"参数只能是 {first} 或 {second}。");
    }
}
