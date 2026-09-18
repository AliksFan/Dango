namespace Dango.Desktop.Rendering.Visuals;

public static class DangoHatPresets
{
    public static bool IsKnown(string? id) => id is "sprout" or "top-hat" or "bowler";

    public static HatAppearance GetAppearance(string id) => id switch
    {
        "sprout" => HatAppearance.Sprout,
        "top-hat" => HatAppearance.TopHat,
        "bowler" => HatAppearance.Bowler,
        _ => throw new ArgumentException("帽型不受支持。", nameof(id)),
    };

    public static string GetId(HatAppearance appearance) => appearance switch
    {
        HatAppearance.Sprout => "sprout",
        HatAppearance.TopHat => "top-hat",
        HatAppearance.Bowler => "bowler",
        _ => throw new ArgumentException("帽型不受支持。", nameof(appearance)),
    };
}
