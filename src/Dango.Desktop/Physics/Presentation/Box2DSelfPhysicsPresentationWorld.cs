using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Tuning;

namespace Dango.Desktop.Physics.Presentation;

public readonly record struct DangoSelfPhysicsControlState(
    double Horizontal,
    double Downward,
    bool JumpPressed,
    bool DragActive,
    LogicalPoint DragTarget)
{
    public static DangoSelfPhysicsControlState None => default;
}

public sealed class Box2DSelfPhysicsPresentationWorld : IDisposable
{
    private static readonly TimeSpan MaximumFixedStep = TimeSpan.FromMilliseconds(100);

    private readonly string libraryPath;
    private readonly double? groundAnchorCenterX;
    private readonly double? groundAnchorCenterY;
    private Box2DNativeSession? session;
    private Box2DSelfSoftBodyNativeSession? selfSoftBody;
    private DangoSelfPhysicsParameters parameters;
    private StackedPhysicsPresentationFrame? lastStableFrame;
    private PresentationPoint leftEye = new(-PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05);
    private PresentationPoint rightEye = new(PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05);
    private PresentationPoint hat = new(0, 0.82);
    private double hatAngle;
    private int stepCount;
    private bool restartBlocked;
    private bool disposed;

    public Box2DSelfPhysicsPresentationWorld(
        string libraryPath,
        DangoSelfPhysicsParameters parameters,
        double? groundAnchorCenterX = null,
        double? groundAnchorCenterY = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        this.libraryPath = libraryPath;
        this.parameters = parameters;
        this.groundAnchorCenterX = groundAnchorCenterX;
        this.groundAnchorCenterY = groundAnchorCenterY;
    }

    public DangoSelfPhysicsParameters Parameters => parameters;

    public bool IsRestartBlocked => restartBlocked;

    public int LastNativeErrorCode { get; private set; }

    public StackedPhysicsPresentationFrame Step(
        PhysicsPresentationInput input,
        TimeSpan fixedStep,
        DangoSelfPhysicsControlState control)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(input);
        if (fixedStep <= TimeSpan.Zero || fixedStep > MaximumFixedStep)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedStep));
        }
        if (!double.IsFinite(control.Horizontal) || control.Horizontal is < -1 or > 1 ||
            !double.IsFinite(control.Downward) || control.Downward is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(control));
        }

        var fallbackReason = GetFallbackReason(input);
        if (fallbackReason != PhysicsPresentationFallbackReason.None)
        {
            ReleaseNativeWorld();
            return CreateFallback(input, fallbackReason);
        }
        if (restartBlocked)
        {
            return CreateLatchedFrame(input);
        }
        if (!TryEnsureNativeWorld())
        {
            return CreateFallback(input, PhysicsPresentationFallbackReason.NativeUnavailable);
        }

        var horizontalScale = Math.Max(1, input.AuthoritativeBounds.Width / 2);
        var verticalScale = Math.Max(1, input.AuthoritativeBounds.Height / 2);
        var referenceX = groundAnchorCenterX ?? input.AuthoritativeBounds.Center.X;
        var referenceY = groundAnchorCenterY ?? input.AuthoritativeBounds.Center.Y;
        var center = input.AuthoritativeBounds.Center;
        var anchorX = (float)((center.X - referenceX) / horizontalScale);
        var anchorY = (float)((referenceY - center.Y) / verticalScale);
        var dragTargetX = control.DragActive
            ? (float)((control.DragTarget.X - referenceX) / horizontalScale)
            : 0;
        var dragTargetY = control.DragActive
            ? (float)((referenceY - control.DragTarget.Y) / verticalScale)
            : 0;
        var nativeFrame = selfSoftBody!.Step(
            fixedStep,
            parameters,
            anchorX,
            anchorY,
            (float)control.Horizontal,
            (float)control.Downward,
            control.JumpPressed,
            control.DragActive,
            dragTargetX,
            dragTargetY);
        if (!nativeFrame.Succeeded || nativeFrame.PointCount != parameters.NodeCount)
        {
            LastNativeErrorCode = nativeFrame.ErrorCode;
            ReleaseNativeWorld();
            restartBlocked = true;
            return CreateLatchedFrame(input, nativeFrame.ErrorCode);
        }

        var body = PresentNativeFrame(nativeFrame, fixedStep, input.FacingDirection);
        var renderOffset = new LogicalVector(
            (nativeFrame.CoreX - anchorX) * horizontalScale,
            -(nativeFrame.CoreY - anchorY) * verticalScale);
        lastStableFrame = new(
            [body],
            [input.AuthoritativeBounds],
            [renderOffset],
            nativeFrame.GroundContactCount,
            [],
            false,
            nativeFrame.MaximumSpeed);
        return lastStableFrame;
    }

    public void ApplyParameters(DangoSelfPhysicsParameters next)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(next);
        next.Validate();
        var rebuild = next.RequiresRebuildComparedWith(parameters);
        parameters = next;
        if (rebuild)
        {
            Reset();
        }
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ReleaseNativeWorld();
        lastStableFrame = null;
        restartBlocked = false;
        LastNativeErrorCode = 0;
        leftEye = new(-PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05);
        rightEye = new(PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05);
        hat = new(0, 0.82);
        hatAngle = 0;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        ReleaseNativeWorld();
        session?.Dispose();
        session = null;
        disposed = true;
        GC.SuppressFinalize(this);
    }

    private bool TryEnsureNativeWorld()
    {
        if (session is null && !Box2DNativeLoader.TryLoad(
            libraryPath,
            Box2DNativeLoader.PinnedRequirements,
            out session,
            out _))
        {
            return false;
        }
        return selfSoftBody is not null || session!.TryCreateSelfSoftBody(parameters, out selfSoftBody, out _);
    }

    private void ReleaseNativeWorld()
    {
        selfSoftBody?.Dispose();
        selfSoftBody = null;
        stepCount = 0;
    }

    internal PhysicsPresentationFrame PresentNativeFrame(
        Box2DSelfSoftBodyNativeFrame nativeFrame,
        TimeSpan fixedStep,
        Core.Animation.DangoFacingDirection facing)
    {
        stepCount++;
        var maximumCoordinate = nativeFrame.Points
            .Take(parameters.NodeCount * 2)
            .Max(value => Math.Abs(value));
        var presentationScale = maximumCoordinate > 0.98
            ? 0.98 / maximumCoordinate
            : 1;
        var contour = Enumerable.Range(0, parameters.NodeCount)
            .Select(index => new PresentationPoint(
                nativeFrame.Points[index * 2] * presentationScale,
                nativeFrame.Points[(index * 2) + 1] * presentationScale))
            .ToArray();
        var maximumRadius = contour.Max(point => Math.Sqrt((point.X * point.X) + (point.Y * point.Y)));
        var facingValue = facing switch
        {
            Core.Animation.DangoFacingDirection.Left => -1,
            Core.Animation.DangoFacingDirection.Right => 1,
            _ => 0,
        };
        var eyeCenter = 0.06 * facingValue;
        var leftTarget = new PresentationPoint(eyeCenter - PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05);
        var rightTarget = new PresentationPoint(eyeCenter + PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05);
        var highest = contour.MaxBy(point => point.Y);
        var hatTarget = new PresentationPoint(highest.X * 0.08, Math.Min(1.05, highest.Y));
        var eyeBlend = 1 - Math.Exp(-8 * fixedStep.TotalSeconds);
        var hatBlend = 1 - Math.Exp(-6 * fixedStep.TotalSeconds);
        leftEye = Blend(leftEye, leftTarget, eyeBlend);
        rightEye = Blend(rightEye, rightTarget, eyeBlend);
        hat = Blend(hat, hatTarget, hatBlend);
        var leftTop = contour.OrderByDescending(point => point.Y).Take(3).MinBy(point => point.X);
        var rightTop = contour.OrderByDescending(point => point.Y).Take(3).MaxBy(point => point.X);
        var targetHatAngle = Math.Atan2(rightTop.Y - leftTop.Y, rightTop.X - leftTop.X);
        hatAngle += (targetHatAngle - hatAngle) * hatBlend;
        var metrics = new SoftBodyMetrics(
            new SoftBodyConfiguration(parameters.NodeCount, parameters.SubStepCount),
            nativeFrame.SignedArea * presentationScale * presentationScale,
            new PresentationPoint(
                nativeFrame.CentroidX * presentationScale,
                nativeFrame.CentroidY * presentationScale),
            maximumRadius,
            nativeFrame.MaximumSpeed,
            nativeFrame.MaximumSpeed * nativeFrame.MaximumSpeed * parameters.NodeCount,
            nativeFrame.SignedArea > 0,
            nativeFrame.MaximumSpeed < 0.03);
        return AuthoredPoseSampler.CreateSoftBodyActive(
            contour,
            new PresentationAttachment(Blend(leftEye, rightEye, 0.5), 0),
            new PresentationAttachment(leftEye, 0),
            new PresentationAttachment(rightEye, 0),
            new PresentationAttachment(hat, hatAngle),
            stepCount,
            metrics);
    }

    private StackedPhysicsPresentationFrame CreateFallback(
        PhysicsPresentationInput input,
        PhysicsPresentationFallbackReason reason,
        int nativeErrorCode = 0) =>
        new(
            [AuthoredPoseSampler.CreateFallback(input, reason, nativeErrorCode)],
            [input.AuthoritativeBounds],
            [new LogicalVector(0, 0)],
            0,
            [],
            true,
            0);

    private StackedPhysicsPresentationFrame CreateLatchedFrame(
        PhysicsPresentationInput input,
        int nativeErrorCode = 0) =>
        lastStableFrame is null
            ? CreateFallback(input, PhysicsPresentationFallbackReason.SimulationUnstable, nativeErrorCode)
            : lastStableFrame with
            {
                AuthoritativeBounds = [input.AuthoritativeBounds],
                ContactCount = 0,
                ConstraintsReleased = true,
                MaximumNodeSpeed = 0,
            };

    private static PhysicsPresentationFallbackReason GetFallbackReason(PhysicsPresentationInput input)
    {
        if (input.Action == Core.Animation.DangoActionState.Hidden)
        {
            return PhysicsPresentationFallbackReason.Hidden;
        }
        if (input.ReducedMotion)
        {
            return PhysicsPresentationFallbackReason.ReducedMotion;
        }
        return input.IsHostAvailable
            ? PhysicsPresentationFallbackReason.None
            : PhysicsPresentationFallbackReason.HostUnavailable;
    }

    private static PresentationPoint Blend(PresentationPoint from, PresentationPoint to, double amount) =>
        new(from.X + ((to.X - from.X) * amount), from.Y + ((to.Y - from.Y) * amount));
}
