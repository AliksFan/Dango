using System.IO;

namespace Dango.Desktop.Accessories.Editing;

public sealed record AccessoryBrushPoint(double X, double Y, double Pressure);

public sealed class AccessoryBrushContent
{
    public double Size { get; }
    public bool Pressure { get; }
    public IReadOnlyList<IReadOnlyList<AccessoryBrushPoint>> Strokes { get; }
    public AccessoryBrushContent(double size, bool pressure, IReadOnlyList<IReadOnlyList<AccessoryBrushPoint>> strokes)
    {
        if (strokes is null || strokes.Count is < 1 or > 1024 || strokes.Any(s => s is null || s.Count == 0) ||
            strokes.Sum(s => (long)s.Count) > 32768 || !Bound(size, 1, 999)) throw new InvalidDataException("画笔数据超过范围。");
        var copy = strokes.Select(s => (IReadOnlyList<AccessoryBrushPoint>)Array.AsReadOnly(s.ToArray())).ToArray();
        if (copy.SelectMany(s => s).Any(p => p is null || !Bound(p.X, -8192, 8192) || !Bound(p.Y, -8192, 8192) || !Bound(p.Pressure, .001, 2048)))
            throw new InvalidDataException("画笔坐标无效。");
        Size = size; Pressure = pressure; Strokes = Array.AsReadOnly(copy);
    }
    internal static bool Bound(double v, double min, double max) => double.IsFinite(v) && v >= min && v <= max;
}

public sealed record AccessoryTextStyle(string Family = "Arial", double Size = 20, double Kerning = 0,
    double Leading = 0, bool Bold = false, bool Italic = false, bool Underline = false, bool Strikethrough = false,
    string FillColor = "#000000", string StrokeColor = "#000000", double StrokeSize = 0);
public sealed record AccessoryTextSpan(string Text, AccessoryTextStyle Meta);

public sealed class AccessoryTextContent
{
    public IReadOnlyList<IReadOnlyList<AccessoryTextSpan>> Lines { get; }
    public string Boundary { get; }
    public string Kerning { get; }
    public string TextDirection { get; }
    public string WrapDirection { get; }
    public string HorizontalAlign { get; }
    public string VerticalAlign { get; }
    public string Wrap { get; }

    public AccessoryTextContent(IReadOnlyList<IReadOnlyList<AccessoryTextSpan>> lines, string boundary = "dynamic",
        string kerning = "metrics", string textDirection = "ltr", string wrapDirection = "ttb",
        string horizontalAlign = "left", string verticalAlign = "top", string wrap = "letter")
    {
        if (lines is null || lines.Count is < 1 or > 2048 || lines.Any(l => l is null || l.Count == 0) ||
            lines.Sum(l => (long)l.Count) > 8192) throw new InvalidDataException("文字行或片段超过范围。");
        var copy = lines.Select(l => (IReadOnlyList<AccessoryTextSpan>)Array.AsReadOnly(l.ToArray())).ToArray();
        long characters = 0;
        foreach (var span in copy.SelectMany(l => l))
        {
            if (span is null || span.Text is null || span.Meta is null) throw new InvalidDataException("文字片段无效。");
            characters += span.Text.Length;
            var m = span.Meta;
            if (characters > 65536 || m.Family is not ("Arial" or "Segoe UI" or "Microsoft YaHei" or "Courier New" or "Times New Roman" or "Verdana" or "monospace") ||
                !AccessoryBrushContent.Bound(m.Size, 1, 512) || !AccessoryBrushContent.Bound(m.Kerning, -999, 999) ||
                !AccessoryBrushContent.Bound(m.Leading, -999, 999) || !AccessoryBrushContent.Bound(m.StrokeSize, 0, 64) ||
                !IsColor(m.FillColor) || !IsColor(m.StrokeColor)) throw new InvalidDataException("文字样式或长度无效。");
        }
        if (boundary is not ("dynamic" or "box") || kerning is not ("metrics" or "none") || textDirection is not ("ltr" or "rtl") ||
            wrapDirection != "ttb" || horizontalAlign is not ("left" or "center" or "right") || verticalAlign is not ("top" or "middle" or "bottom") ||
            wrap is not ("letter" or "word")) throw new InvalidDataException("文字排版模式无效。");
        Lines = Array.AsReadOnly(copy); Boundary = boundary; Kerning = kerning; TextDirection = textDirection;
        WrapDirection = wrapDirection; HorizontalAlign = horizontalAlign; VerticalAlign = verticalAlign; Wrap = wrap;
    }

    internal static bool IsColor(string? color) => color is { Length: 7 } && color[0] == '#' &&
        color.AsSpan(1).ToString().All(c => char.IsAsciiHexDigit(c));
}
