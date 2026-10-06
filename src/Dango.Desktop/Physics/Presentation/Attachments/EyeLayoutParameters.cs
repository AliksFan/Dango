namespace Dango.Desktop.Physics.Presentation.Attachments;

/// <summary>Saved eye controls, independent of transient expression and safety reductions.</summary>
public sealed record EyeLayoutParameters
{
    public static EyeLayoutParameters Default { get; } = new();

    public double SizePercent { get; }
    public double OvalPercent { get; }
    public double HeightControlPercent { get; }
    public double SpacingControlPercent { get; }
    public bool IsDefault => Equals(Default);

    public EyeLayoutParameters(double sizePercent = 100, double ovalPercent = 0,
        double heightControlPercent = 51, double spacingControlPercent = 24.5)
    {
        Validate(sizePercent, 70, 140, nameof(sizePercent));
        Validate(ovalPercent, -100, 100, nameof(ovalPercent));
        Validate(heightControlPercent, 32, 66, nameof(heightControlPercent));
        Validate(spacingControlPercent, 12, 34, nameof(spacingControlPercent));
        SizePercent = sizePercent;
        OvalPercent = ovalPercent;
        HeightControlPercent = heightControlPercent;
        SpacingControlPercent = spacingControlPercent;
    }

    private static void Validate(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentOutOfRangeException(name, value, "Eye control must be finite and inside its supported range.");
    }
}
