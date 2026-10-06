using Dango.Core.Coordinates;

namespace Dango.Desktop.AppShell.Rooms;

internal enum RoomViewPreset { Standard, Near, Whole, Custom }
internal enum RoomCameraMode { Follow, EdgeFollow, Manual }

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
    private const double FollowBottomInset = 32;
    private const double FollowMilliseconds = 140;
    private const double NeutralBodyWidthDip = 154;
    private double width;
    private double height;
    private double groundWorldY;
    private double safeWorldHeight = 200;
    private double requestedPercent = 100;
    private double petWorldX;
    private double petWorldY;
    private bool hasViewport;
    private bool hasPet;
    private bool calibrated;
    private bool windowCameraEnabled;
    private double cameraVelocity;
    private double cameraTarget;
    private double cameraVelocityY;
    private double cameraTargetY;

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
    // World-space viewport top. The ground reference supplies the initial composition.
    internal double CameraY { get; private set; }
    internal RoomCameraMode CameraMode { get; private set; } = RoomCameraMode.Follow;
    internal RoomViewPreset Preset { get; private set; } = RoomViewPreset.Standard;
    internal double ZoomPercent => Scale / StandardScale * 100;
    internal RoomProjection Projection { get; private set; }

    // Other hosts retain their accepted legacy camera unless explicitly opted in.
    internal void EnableWindowCamera()
    {
        if (windowCameraEnabled) return;
        windowCameraEnabled = true;
        ResetMotion();
    }

    internal void SelectCameraMode(RoomCameraMode mode)
    {
        if (mode is not (RoomCameraMode.Follow or RoomCameraMode.EdgeFollow or RoomCameraMode.Manual))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (CameraMode == mode) return;
        CameraMode = mode;
        ResetMotion();
    }

    internal void ResetMotion()
    {
        cameraVelocity = 0;
        cameraTarget = CameraX;
        cameraVelocityY = 0;
        cameraTargetY = CameraY;
    }

    internal bool PanByScreenDelta(double deltaX, double deltaY = 0)
    {
        RequireFinite(deltaX, nameof(deltaX));
        RequireFinite(deltaY, nameof(deltaY));
        if (!windowCameraEnabled || CameraMode != RoomCameraMode.Manual || !hasViewport || (deltaX == 0 && deltaY == 0))
            return false;
        var nextX = WholeWorldVisible ? 0 : Math.Clamp(CameraX - deltaX / Scale, 0, WorldWidth - width / Scale);
        var nextY = ClampCameraY(CameraY - deltaY / Scale);
        if (nextX == CameraX && nextY == CameraY) return false;
        CameraX = nextX;
        CameraY = nextY;
        ResetMotion();
        ApplyProjection();
        return true;
    }

    internal void Resize(double width, double height, double groundWorldY, double safeWorldHeight = 200)
    {
        RequirePositive(width, nameof(width));
        RequirePositive(height, nameof(height));
        if (height <= GroundInset + TopInset) throw new ArgumentOutOfRangeException(nameof(height));
        RequireFinite(groundWorldY, nameof(groundWorldY));
        RequirePositive(safeWorldHeight, nameof(safeWorldHeight));
        if (hasViewport && this.width == width && this.height == height &&
            this.groundWorldY == groundWorldY && this.safeWorldHeight == safeWorldHeight) return;
        var retainedCenter = windowCameraEnabled && hasViewport ? ViewportCenter : (LogicalPoint?)null;
        this.width = width;
        this.height = height;
        this.groundWorldY = groundWorldY;
        this.safeWorldHeight = safeWorldHeight;
        hasViewport = true;
        Recalculate(retainedCenter);
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

    // Existing X-only callers supply no elevation trajectory and keep their Y view.
    internal RoomProjection Update(double petWorldX, double elapsedMs, bool reduceMotion = false) =>
        UpdateCore(new(petWorldX, petWorldY), elapsedMs, reduceMotion, advanceVertical: false, bodyWorldBottom: null);

    internal RoomProjection Update(LogicalPoint focus, double elapsedMs, bool reduceMotion = false, double? bodyWorldBottom = null) =>
        UpdateCore(focus, elapsedMs, reduceMotion, advanceVertical: true, bodyWorldBottom: bodyWorldBottom);

    private RoomProjection UpdateCore(LogicalPoint petWorldCenter, double elapsedMs, bool reduceMotion, bool advanceVertical,
        double? bodyWorldBottom)
    {
        RequireFinite(petWorldCenter.X, nameof(petWorldCenter));
        RequireFinite(petWorldCenter.Y, nameof(petWorldCenter));
        RequireFinite(elapsedMs, nameof(elapsedMs));
        if (bodyWorldBottom.HasValue) RequireFinite(bodyWorldBottom.Value, nameof(bodyWorldBottom));
        petWorldX = Math.Clamp(petWorldCenter.X, 0, WorldWidth);
        petWorldY = petWorldCenter.Y;
        var firstPet = !hasPet;
        if (!hasViewport)
        {
            if (!windowCameraEnabled) hasPet = true;
            return Projection;
        }
        hasPet = true;
        if (windowCameraEnabled)
        {
            UpdateWindowCamera(firstPet, elapsedMs, reduceMotion, advanceVertical, bodyWorldBottom);
            ApplyProjection();
            return Projection;
        }
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

    private void UpdateWindowCamera(bool firstPet, double elapsedMs, bool reduceMotion, bool advanceVertical,
        double? bodyWorldBottom)
    {
        var minimumFollowY = advanceVertical && CameraMode == RoomCameraMode.Follow && bodyWorldBottom.HasValue
            ? ClampCameraY(bodyWorldBottom.Value - (height - FollowBottomInset) / Scale)
            : double.NegativeInfinity;
        if (firstPet)
        {
            CenterCamera();
            if (reduceMotion || elapsedMs > 0) CameraY = Math.Max(CameraY, minimumFollowY);
            ResetMotion();
            return;
        }
        if (WholeWorldVisible)
        {
            CameraX = cameraTarget = cameraVelocity = 0;
        }
        if (!advanceVertical)
        {
            cameraVelocityY = 0;
            cameraTargetY = CameraY;
        }
        if (CameraMode == RoomCameraMode.Manual) return;
        var visibleWidth = width / Scale;
        var maximum = Math.Max(0, WorldWidth - visibleWidth);
        var target = cameraTarget;
        if (CameraMode == RoomCameraMode.Follow)
            target = petWorldX - visibleWidth / 2;
        else if (petWorldX < CameraX + visibleWidth * .2)
            target = petWorldX - visibleWidth * .2;
        else if (petWorldX > CameraX + visibleWidth * .8)
            target = petWorldX - visibleWidth * .8;
        target = Math.Clamp(target, 0, maximum);
        var targetY = cameraTargetY;
        if (advanceVertical)
        {
            if (CameraMode == RoomCameraMode.Follow)
                // Keep the ground composition until the stable body focus crosses
                // the midline, then follow that focus around the viewport center.
                targetY = Math.Min(DefaultGroundAnchorTop, petWorldY - height / (2 * Scale));
            else if (petWorldY < CameraY + height / Scale * .2)
                targetY = petWorldY - height / Scale * .2;
            else if (petWorldY > CameraY + height / Scale * .8)
                targetY = petWorldY - height / Scale * .8;
            targetY = Math.Max(ClampCameraY(targetY), minimumFollowY);
        }
        if (reduceMotion)
        {
            CameraX = target;
            if (advanceVertical) CameraY = targetY;
            ResetMotion();
            return;
        }

        var horizontal = AdvanceDampedAxis(CameraX, cameraVelocity, cameraTarget, target, elapsedMs);
        CameraX = Math.Clamp(horizontal.Position, 0, maximum);
        cameraVelocity = CameraX == horizontal.Position ? horizontal.Velocity : 0;
        cameraTarget = target;
        if (advanceVertical)
        {
            var vertical = AdvanceDampedAxis(CameraY, cameraVelocityY, cameraTargetY, targetY, elapsedMs);
            CameraY = ClampCameraY(vertical.Position);
            cameraVelocityY = CameraY == vertical.Position ? vertical.Velocity : 0;
            if (elapsedMs > 0 && CameraY < minimumFollowY)
            {
                // Damping can lag a descending body. Keep its rendered bottom
                // visible, and discard velocity that would push it out again.
                CameraY = minimumFollowY;
                cameraVelocityY = Math.Max(0, cameraVelocityY);
            }
            cameraTargetY = targetY;
        }
    }

    private static (double Position, double Velocity) AdvanceDampedAxis(
        double position, double velocity, double previousTarget, double target, double elapsedMs)
    {
        // Exact critically damped integration for the linear segment between
        // successive presentation targets. Retaining velocity avoids replacing it
        // when the target moves or reverses; linear integration avoids frame-rate
        // dependence from treating each new target as a full-duration step.
        var seconds = Math.Clamp(elapsedMs, 0, 100) / 1000;
        if (seconds > 0)
        {
            var omega = 1000 / FollowMilliseconds;
            var h = omega * seconds;
            var decay = Math.Exp(-h);
            double response, ramp;
            if (h < .01)
            {
                // Stable series avoid subtracting nearly equal values for short
                // presentation intervals, including the first interval on resume.
                response = h * h * (.5 + h * (-1d / 3 + h * (1d / 8 + h * (-1d / 30 + h / 144))));
                ramp = h * h * (1d / 6 + h * (-1d / 12 + h * (1d / 40 + h * (-1d / 180 + h / 1008))));
            }
            else
            {
                response = 1 - (1 + h) * decay;
                ramp = 1 - 2 / h + (1 + 2 / h) * decay;
            }
            var error = previousTarget - position;
            var targetDelta = target - previousTarget;
            var next = position + error * response + velocity * seconds * decay + targetDelta * ramp;
            velocity = decay * (velocity * (1 - h) + omega * error * h) +
                targetDelta * omega * (response / h);
            return (next, velocity);
        }
        return (position, velocity);
    }

    private double AvailableHeight => height - GroundInset - TopInset;
    private double MaximumScale => Math.Min(StandardScale * 1.3, AvailableHeight / safeWorldHeight);
    private double WholeScale => Math.Min(StandardScale,
        Math.Min(width / WorldWidth, AvailableHeight / Math.Max(WorldHeight, safeWorldHeight)));
    private bool WholeWorldVisible => width / Scale >= WorldWidth;
    private LogicalPoint ViewportCenter => new(
        (width / 2 - Projection.OffsetX) / Scale, (height / 2 - Projection.OffsetY) / Scale);
    private double DefaultGroundAnchorTop => groundWorldY - (height - GroundInset) / Scale;
    // A finite presentation margin includes the existing wall above/below the
    // ground framing and scales naturally with a future taller room.
    private double ClampCameraY(double top) =>
        Math.Clamp(top, DefaultGroundAnchorTop - WorldHeight, DefaultGroundAnchorTop + WorldHeight);

    private void Recalculate(LogicalPoint? retainedCenter = null)
    {
        if (!hasViewport)
        {
            Scale = StandardScale * requestedPercent / 100;
            Projection = new(Scale, 0, 0);
            return;
        }
        var center = retainedCenter ?? ViewportCenter;
        Scale = Preset == RoomViewPreset.Whole ? WholeScale :
            Math.Clamp(StandardScale * requestedPercent / 100, WholeScale, MaximumScale);
        if (windowCameraEnabled && hasPet)
        {
            CameraX = WholeWorldVisible ? 0 : Math.Clamp(center.X - width / Scale / 2, 0, WorldWidth - width / Scale);
            CameraY = ClampCameraY(center.Y - height / Scale / 2);
            ResetMotion();
        }
        else CenterCamera();
        ApplyProjection();
    }

    private void CenterCamera()
    {
        CameraX = WholeWorldVisible ? 0 : Math.Clamp(petWorldX - width / Scale / 2, 0, WorldWidth - width / Scale);
        CameraY = DefaultGroundAnchorTop;
    }

    private void ApplyProjection() => Projection = new(Scale,
        WholeWorldVisible ? (width - WorldWidth * Scale) / 2 : -CameraX * Scale,
        windowCameraEnabled ? -CameraY * Scale : height - GroundInset - groundWorldY * Scale);

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }

    private static void RequireFinite(double value, string name)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(name);
    }
}
