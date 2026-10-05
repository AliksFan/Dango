using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Rendering.Visuals;

/// <summary>One immutable local appearance snapshot. Hats have their own settings.</summary>
public sealed record DangoAppearanceConfiguration
{
    public static OpaqueSrgbColor DefaultEyeColor { get; } = OpaqueSrgbColor.Parse("#2B201E");
    public static DangoAppearanceConfiguration Default { get; } =
        new(OpaqueSrgbColor.Default, DefaultEyeColor, EyeLayoutParameters.Default);

    public OpaqueSrgbColor BodyColor { get; }
    public OpaqueSrgbColor EyeColor { get; }
    public EyeLayoutParameters EyeLayout { get; }

    public DangoAppearanceConfiguration(OpaqueSrgbColor bodyColor, OpaqueSrgbColor eyeColor,
        EyeLayoutParameters eyeLayout)
    {
        ArgumentNullException.ThrowIfNull(eyeLayout);
        BodyColor = bodyColor;
        EyeColor = eyeColor;
        EyeLayout = eyeLayout;
    }
}
