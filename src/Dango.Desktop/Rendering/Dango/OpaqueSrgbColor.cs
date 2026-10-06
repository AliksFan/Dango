namespace Dango.Desktop.Rendering.Visuals;

/// <summary>A fully opaque 8-bit sRGB color accepted by the appearance domain.</summary>
public readonly record struct OpaqueSrgbColor(byte R, byte G, byte B)
{
    public static OpaqueSrgbColor Default { get; } = new(0xFF, 0xE8, 0xC2);

    public static OpaqueSrgbColor Parse(string value)
    {
        if (value is null || value.Length != 7 || value[0] != '#' ||
            !TryHexByte(value.AsSpan(1, 2), out var r) ||
            !TryHexByte(value.AsSpan(3, 2), out var g) ||
            !TryHexByte(value.AsSpan(5, 2), out var b))
            throw new FormatException("颜色必须是大写 #RRGGBB。");

        return new(r, g, b);
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    private static bool TryHexByte(ReadOnlySpan<char> value, out byte result)
    {
        result = 0;
        if (value.Length != 2) return false;
        var high = HexValue(value[0]);
        var low = HexValue(value[1]);
        if (high < 0 || low < 0) return false;
        result = (byte)((high << 4) | low);
        return true;
    }

    private static int HexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1,
    };
}
