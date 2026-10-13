using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Tuning;

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
    /// <summary>Body size, softness and landing build. Physics follows these only after an explicit body preview.</summary>
    public DangoBodyControls Body { get; }

    public DangoAppearanceConfiguration(OpaqueSrgbColor bodyColor, OpaqueSrgbColor eyeColor,
        EyeLayoutParameters eyeLayout, DangoBodyControls? body = null)
    {
        ArgumentNullException.ThrowIfNull(eyeLayout);
        BodyColor = bodyColor;
        EyeColor = eyeColor;
        EyeLayout = eyeLayout;
        Body = body ?? DangoBodyControls.Default;
    }

    // Partial edits keep every other field, so a colour or eye change never resets the saved body.
    public DangoAppearanceConfiguration WithBodyColor(OpaqueSrgbColor color) => new(color, EyeColor, EyeLayout, Body);
    public DangoAppearanceConfiguration WithEyeColor(OpaqueSrgbColor color) => new(BodyColor, color, EyeLayout, Body);
    public DangoAppearanceConfiguration WithEyeLayout(EyeLayoutParameters eyes) => new(BodyColor, EyeColor, eyes, Body);
    public DangoAppearanceConfiguration WithBody(DangoBodyControls body) => new(BodyColor, EyeColor, EyeLayout, body);
}
