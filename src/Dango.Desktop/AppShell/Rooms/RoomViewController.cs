using Dango.Core.Coordinates;

namespace Dango.Desktop.AppShell.Rooms;

internal enum RoomViewPreset { Standard, Near, Whole, Custom }

internal readonly record struct RoomProjection(double Scale, double OffsetX, double OffsetY)
{
    internal LogicalPoint WorldToScreen(LogicalPoint point)
    {
        Validate(point);
        return new(point.X * Scale + OffsetX, point.Y * Scale + OffsetY);
    }

    internal LogicalPoint ScreenToWorld(LogicalPoint point)
    {
        Validate(point);
        return new((point.X - OffsetX) / Scale, (point.Y - OffsetY) / Scale);
    }

    private void Validate(LogicalPoint point)
    {
        if (!double.IsFinite(Scale) || Scale <= 0 || !double.IsFinite(OffsetX) || !double.IsFinite(OffsetY))
            throw new ArgumentOutOfRangeException(nameof(Scale));
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(point));
    }
}

/// <summary>Presentation only: never rehosts a runtime, resizes a body or changes physics.</summary>
internal sealed class RoomViewController
{
    private const double GroundInset = 64;
    private const double TopInset = 24;
    private const double FollowMilliseconds = 140;
    private const double NeutralBodyWidthDip = 154;
    private double width;
    private double height;
    private double groundWorldY;
    private double safeWorldHeight = 200;
    private double requestedPercent = 100;
    private double petWorldX;
    private bool hasViewport;
    private bool hasPet;
    private bool calibrated;

    internal RoomViewController(double worldWidth = 2160, double worldHeight = 360, double standardScale = 1.4)
    {
        RequirePositive(worldWidth, nameof(worldWidth));
        RequirePositive(worldHeight, nameof(worldHeight));
        RequirePositive(standardScale, nameof(standardScale));
        WorldWidth = worldWidth;
        WorldHeight = worldHeight;
        StandardScale = Scale = standardScale;
        petWorldX = worldWidth / 2;
        Projection = new(Scale, 0, 0);
    }

    internal double WorldWidth { get; }
    internal double WorldHeight { get; }
    internal double StandardScale { get; private set; }
    internal double Scale { get; private set; }
    internal double CameraX { get; private set; }
    internal RoomViewPreset Preset { get; private set; } = RoomViewPreset.Standard;
    internal double ZoomPercent => Scale / StandardScale * 100;
    internal RoomProjection Projection { get; private set; }

    internal void Resize(double width, double height, double groundWorldY, double safeWorldHeight = 200)
    {
        RequirePositive(width, nameof(width));
        RequirePositive(height, nameof(height));
        if (height <= GroundInset + TopInset) throw new ArgumentOutOfRangeException(nameof(height));
        RequireFinite(groundWorldY, nameof(groundWorldY));
        RequirePositive(safeWorldHeight, nameof(safeWorldHeight));
        if (hasViewport && this.width == width && this.height == height &&
            this.groundWorldY == groundWorldY && this.safeWorldHeight == safeWorldHeight) return;
        this.width = width;
        this.height = height;
        this.groundWorldY = groundWorldY;
        this.safeWorldHeight = safeWorldHeight;
        hasViewport = true;
        Recalculate();
    }

    // The caller supplies one healthy, neutral visible contour width, not the 160-DIP frame box.
    internal bool CalibrateStandardScale(double visibleWidth)
    {
        RequirePositive(visibleWidth, nameof(visibleWidth));
        var scale = NeutralBodyWidthDip / visibleWidth;
        RequirePositive(scale, nameof(visibleWidth));
        if (calibrated) return false;
        StandardScale = scale;
        calibrated = true;
        Recalculate();
        return true;
    }

    internal void SelectPreset(RoomViewPreset preset)
    {
        var percent = preset switch
        {
            RoomViewPreset.Standard => 100,
            RoomViewPreset.Near => 130,
            RoomViewPreset.Whole => requestedPercent,
            RoomViewPreset.Custom => ZoomPercent,
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };
        Preset = preset;
        requestedPercent = percent;
        Recalculate();
    }

    internal bool ApplyZoomSteps(int steps)
    {
        if (!hasViewport || steps == 0) return false;
        var next = Math.Clamp(Scale + steps * StandardScale * .1, WholeScale, MaximumScale);
        if (Math.Abs(next - Scale) <= 1e-12 * Math.Max(1, Scale)) return false;
        Preset = RoomViewPreset.Custom;
        requestedPercent = next / StandardScale * 100;
        Recalculate();
        return true;
    }

    internal RoomProjection Update(double petWorldX, double elapsedMs, bool reduceMotion = false)
    {
        RequireFinite(petWorldX, nameof(petWorldX));
        RequireFinite(elapsedMs, nameof(elapsedMs));
        this.petWorldX = Math.Clamp(petWorldX, 0, WorldWidth);
        var firstPet = !hasPet;
        hasPet = true;
        if (!hasViewport) return Projection;
        if (WholeWorldVisible)
            CameraX = 0;
        else if (firstPet)
            CenterCamera();
        else
        {
            var visibleWidth = width / Scale;
            var maximum = WorldWidth - visibleWidth;
            var previous = Math.Clamp(CameraX, 0, maximum);
            var target = previous;
            if (this.petWorldX < previous + visibleWidth * .4)
                target = this.petWorldX - visibleWidth * .4;
            else if (this.petWorldX > previous + visibleWidth * .6)
                target = this.petWorldX - visibleWidth * .6;
            target = Math.Clamp(target, 0, maximum);
            var alpha = reduceMotion ? 1 : 1 - Math.Exp(-Math.Clamp(elapsedMs, 0, 100) / FollowMilliseconds);
            CameraX = Math.Clamp(previous + (target - previous) * alpha, 0, maximum);
        }
        ApplyProjection();
        return Projection;
    }

    private double AvailableHeight => height - GroundInset - TopInset;
    private double MaximumScale => Math.Min(StandardScale * 1.3, AvailableHeight / safeWorldHeight);
    private double WholeScale => Math.Min(StandardScale,
        Math.Min(width / WorldWidth, AvailableHeight / Math.Max(WorldHeight, safeWorldHeight)));
    private bool WholeWorldVisible => width / Scale >= WorldWidth;

    private void Recalculate()
    {
        if (!hasViewport)
        {
            Scale = StandardScale * requestedPercent / 100;
            Projection = new(Scale, 0, 0);
            return;
        }
        Scale = Preset == RoomViewPreset.Whole ? WholeScale :
            Math.Clamp(StandardScale * requestedPercent / 100, WholeScale, MaximumScale);
        CenterCamera();
        ApplyProjection();
    }

    private void CenterCamera() => CameraX = WholeWorldVisible ? 0 :
        Math.Clamp(petWorldX - width / Scale / 2, 0, WorldWidth - width / Scale);

    private void ApplyProjection() => Projection = new(Scale,
        WholeWorldVisible ? (width - WorldWidth * Scale) / 2 : -CameraX * Scale,
        height - GroundInset - groundWorldY * Scale);

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }

    private static void RequireFinite(double value, string name)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(name);
    }
}
