namespace Dango.Core.Coordinates;

public readonly record struct LogicalPoint(double X, double Y)
{
    public static LogicalPoint operator +(LogicalPoint point, LogicalVector vector) =>
        new(point.X + vector.X, point.Y + vector.Y);
}

public readonly record struct LogicalVector(double X, double Y);

public readonly record struct PixelPoint(double X, double Y);

public readonly record struct DpiScale
{
    public static DpiScale Identity { get; } = new(1, 1);

    public DpiScale(double x, double y)
    {
        if (!double.IsFinite(x) || x <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "DPI X 比例必须是有限正数。");
        }

        if (!double.IsFinite(y) || y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "DPI Y 比例必须是有限正数。");
        }

        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}

public readonly record struct LogicalRect
{
    public LogicalRect(double x, double y, double width, double height)
    {
        Validate(x, nameof(x));
        Validate(y, nameof(y));
        ValidateLength(width, nameof(width));
        ValidateLength(height, nameof(height));

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public LogicalPoint Center => new(X + (Width / 2), Y + (Height / 2));

    public bool Contains(LogicalPoint point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    public bool Contains(LogicalRect other) =>
        other.X >= X && other.Right <= Right && other.Y >= Y && other.Bottom <= Bottom;

    public double IntersectionArea(LogicalRect other)
    {
        var width = Math.Max(0, Math.Min(Right, other.Right) - Math.Max(X, other.X));
        var height = Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y));
        return width * height;
    }

    public LogicalRect Translate(LogicalVector offset) =>
        new(X + offset.X, Y + offset.Y, Width, Height);

    public LogicalRect ClampInside(LogicalRect container)
    {
        if (Width > container.Width || Height > container.Height)
        {
            throw new InvalidOperationException("对象尺寸大于目标显示器工作区，无法安全迁移。");
        }

        var x = Math.Clamp(X, container.X, container.Right - Width);
        var y = Math.Clamp(Y, container.Y, container.Bottom - Height);
        return new LogicalRect(x, y, Width, Height);
    }

    private static void Validate(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "坐标必须是有限数值。");
        }
    }

    private static void ValidateLength(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "尺寸必须是有限正数。");
        }
    }
}

public readonly record struct PixelRect
{
    public PixelRect(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "物理像素坐标必须是有限数值。");
        }

        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "物理像素尺寸必须是有限正数。");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }
}
