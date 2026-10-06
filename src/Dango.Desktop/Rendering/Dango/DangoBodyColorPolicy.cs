namespace Dango.Desktop.Rendering.Visuals;

/// <summary>Deterministic presentation slots for the freely selected opaque body color.</summary>
public static class DangoBodyColorPolicy
{
    public static OpaqueSrgbColor DefaultColor => OpaqueSrgbColor.Default;

    public static IReadOnlyList<OpaqueSrgbColor> QuickColors { get; } =
        [OpaqueSrgbColor.Default, OpaqueSrgbColor.Parse("#C9E2FF"), OpaqueSrgbColor.Parse("#DDCCF8")];

    public static DangoBodyPalette CreatePalette(OpaqueSrgbColor mainColor)
    {
        var auxiliary = QuickColors
            .Select((color, order) => (color, order, distance: SquaredDistance(mainColor, color)))
            .OrderByDescending(candidate => candidate.distance)
            .ThenBy(candidate => candidate.order)
            .Take(2)
            .Select(candidate => candidate.color)
            .ToArray();
        return new(mainColor, auxiliary[0], auxiliary[1]);
    }

    private static long SquaredDistance(OpaqueSrgbColor left, OpaqueSrgbColor right)
    {
        var r = left.R - right.R; var g = left.G - right.G; var b = left.B - right.B;
        return r * r + g * g + b * b;
    }

}

public readonly record struct DangoBodyPalette(OpaqueSrgbColor Slot0, OpaqueSrgbColor Slot1, OpaqueSrgbColor Slot2)
{
    public OpaqueSrgbColor Resolve(int slot) => slot switch { 1 => Slot1, 2 => Slot2, _ => Slot0 };
}
