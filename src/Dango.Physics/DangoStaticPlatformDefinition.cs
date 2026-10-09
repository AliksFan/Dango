namespace Dango.Physics;

/// <summary>A single fixed, solid, axis-aligned platform in physical world units.</summary>
public sealed record DangoStaticPlatformDefinition(
    double CenterX, double CenterY, double Width, double Height, double Friction = .72)
{
    public double Left => CenterX - Width / 2;
    public double Right => CenterX + Width / 2;
    public double Bottom => CenterY - Height / 2;
    public double Top => CenterY + Height / 2;

    public void Validate()
    {
        if (!double.IsFinite(CenterX) || !double.IsFinite(CenterY) ||
            !double.IsFinite(Width) || Width is < .1 or > 16 ||
            !double.IsFinite(Height) || Height is < .1 or > 16 ||
            !double.IsFinite(Friction) || Friction is < 0 or > 1.5 ||
            Math.Abs(Left) > 1024 || Math.Abs(Right) > 1024 ||
            Math.Abs(Bottom) > 1024 || Math.Abs(Top) > 1024)
            throw new ArgumentOutOfRangeException(nameof(CenterX), "平台的坐标、尺寸或摩擦超出允许范围。");
    }
}

public sealed record DangoStaticPlatformContact(
    bool HasPlatform, bool IsTouching, bool IsSupported, float NormalX, float NormalY, float Gap);
