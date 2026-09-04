using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Tuning;

namespace Dango.Desktop.Physics.Presentation;

public sealed class Box2DPhysicsPresentationAdapter : PhysicsPresentationWorldBase
{
    private static readonly TimeSpan DragRecoveryDuration = TimeSpan.FromMilliseconds(750);

    private readonly string _libraryPath;
    private readonly SoftBodyConfiguration _configuration;
    private readonly SoftBodyTuningParameters? _tuning;
    private Box2DNativeSession? _session;
    private Box2DSoftBodyNativeSession? _softBody;
    private Core.Coordinates.LogicalPoint? _lastAuthoritativeCenter;
    private double _physicsAnchorX;
    private double _physicsAnchorY;
    private long _lastLandingStart = long.MinValue;
    private int _stepCount;
    private int _lastNativeError;
    private bool _wasDragged;
    private TimeSpan _dragRecoveryRemaining;

    public Box2DPhysicsPresentationAdapter(string libraryPath)
        : this(libraryPath, new SoftBodyConfiguration(12, 2))
    {
    }

    public Box2DPhysicsPresentationAdapter(string libraryPath, SoftBodyConfiguration configuration)
        : this(libraryPath, configuration, null)
    {
    }

    public static Box2DPhysicsPresentationAdapter CreateTuning(string libraryPath, SoftBodyTuningParameters tuning) =>
        new(libraryPath, new SoftBodyConfiguration(tuning.NodeCount, tuning.SubStepCount), tuning);

    private Box2DPhysicsPresentationAdapter(string libraryPath, SoftBodyConfiguration configuration, SoftBodyTuningParameters? tuning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        if (configuration.NodeCount is not (12 or 16) || configuration.SubStepCount is not (2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }

        _libraryPath = libraryPath;
        _configuration = configuration;
        tuning?.Validate();
        _tuning = tuning;
    }

    protected override PhysicsPresentationFrame StepActive(PhysicsPresentationInput input, TimeSpan fixedStep)
    {
        if (!TryEnsureSoftBody(out var fallbackReason))
        {
            return AuthoredPoseSampler.CreateFallback(input, fallbackReason, _lastNativeError);
        }

        var currentCenter = input.AuthoritativeBounds.Center;
        _lastAuthoritativeCenter ??= currentCenter;
        var frameDeltaX = (currentCenter.X - _lastAuthoritativeCenter.Value.X) /
            input.AuthoritativeBounds.Width;
        var frameDeltaY = (_lastAuthoritativeCenter.Value.Y - currentCenter.Y) /
            input.AuthoritativeBounds.Height;
        _lastAuthoritativeCenter = currentCenter;
        _physicsAnchorX = Math.Clamp(_physicsAnchorX - frameDeltaX, -0.2, 0.2);
        _physicsAnchorY = Math.Clamp(_physicsAnchorY - frameDeltaY, -0.15, 0.15);
        var followStep = (_tuning?.CoreFollowSpeed ?? 0.9) * fixedStep.TotalSeconds;
        _physicsAnchorX += Math.Clamp(-_physicsAnchorX, -followStep, followStep);
        _physicsAnchorY += Math.Clamp(-_physicsAnchorY, -followStep, followStep);
        var isDragged = input.Action == Core.Animation.DangoActionState.Dragged;
        var cue = 0;
        if (input.Action == Core.Animation.DangoActionState.Landing &&
            input.AuthoritativeStartTimeMilliseconds != _lastLandingStart)
        {
            cue = 1;
            _lastLandingStart = input.AuthoritativeStartTimeMilliseconds;
            _dragRecoveryRemaining = TimeSpan.Zero;
        }
        else if (isDragged)
        {
            _dragRecoveryRemaining = DragRecoveryDuration;
            const double stationaryDragThreshold = 0.002;
            cue = Math.Abs(frameDeltaX) <= stationaryDragThreshold &&
                Math.Abs(frameDeltaY) <= stationaryDragThreshold
                ? 3
                : 2;
        }
        else if (_wasDragged || _dragRecoveryRemaining > TimeSpan.Zero)
        {
            cue = 3;
            _dragRecoveryRemaining = _dragRecoveryRemaining > fixedStep
                ? _dragRecoveryRemaining - fixedStep
                : TimeSpan.Zero;
        }
        _wasDragged = isDragged;
        var dragGrabPoint = input.DragGrabPoint ?? new PresentationPoint(0, 0);

        var nativeFrame = _softBody!.Step(
            fixedStep,
            cue,
            (float)_physicsAnchorX,
            (float)_physicsAnchorY,
            (float)input.Strength,
            input.FacingDirection switch
            {
                Core.Animation.DangoFacingDirection.Left => -1,
                Core.Animation.DangoFacingDirection.Right => 1,
                _ => 0,
            },
            isDragged,
            (float)dragGrabPoint.X,
            (float)dragGrabPoint.Y);
        if (!nativeFrame.Succeeded || nativeFrame.PointCount != _configuration.NodeCount)
        {
            _lastNativeError = nativeFrame.ErrorCode;
            ReleaseSession();
            return AuthoredPoseSampler.CreateFallback(
                input,
                PhysicsPresentationFallbackReason.SimulationUnstable,
                _lastNativeError);
        }

        if (!HasFinitePresentationCoordinates(nativeFrame))
        {
            ReleaseSession();
            return AuthoredPoseSampler.CreateFallback(
                input,
                PhysicsPresentationFallbackReason.SimulationUnstable,
                _lastNativeError);
        }

        var contour = new PresentationPoint[nativeFrame.PointCount];
        for (var index = 0; index < contour.Length; index++)
        {
            contour[index] = CreateBoundedPoint(
                nativeFrame.Points[index * 2],
                nativeFrame.Points[index * 2 + 1]);
        }
        var metrics = new SoftBodyMetrics(
            _configuration,
            nativeFrame.SignedArea,
            CreateBoundedPoint(nativeFrame.CentroidX, nativeFrame.CentroidY),
            nativeFrame.MaximumRadius,
            nativeFrame.MaximumSpeed,
            nativeFrame.EnergyProxy,
            nativeFrame.SignedArea >= SoftBodyStabilityLimits.MinimumSignedArea,
            nativeFrame.IsSleeping);
        var eyeFollow = _tuning?.EyeFollow ?? 1;
        var eyeOffsetX = _tuning?.EyeOffsetX ?? 0;
        var eyeOffsetY = _tuning?.EyeOffsetY ?? 0;
        var leftEye = new PresentationAttachment(
            new PresentationPoint(
                Math.Clamp(
                    -PhysicsPresentationFrame.DefaultEyeHalfDistance +
                    ((nativeFrame.LeftEyeX + PhysicsPresentationFrame.DefaultEyeHalfDistance) * eyeFollow) +
                    eyeOffsetX,
                    -0.5,
                    0.5),
                Math.Clamp(-0.05 + ((nativeFrame.LeftEyeY + 0.05) * eyeFollow) + eyeOffsetY, -0.3, 0.2)),
            0);
        var rightEye = new PresentationAttachment(
            new PresentationPoint(
                Math.Clamp(
                    PhysicsPresentationFrame.DefaultEyeHalfDistance +
                    ((nativeFrame.RightEyeX - PhysicsPresentationFrame.DefaultEyeHalfDistance) * eyeFollow) +
                    eyeOffsetX,
                    -0.5,
                    0.5),
                Math.Clamp(-0.05 + ((nativeFrame.RightEyeY + 0.05) * eyeFollow) + eyeOffsetY, -0.3, 0.2)),
            0);
        var eyes = new PresentationAttachment(
            new PresentationPoint(
                (leftEye.Position.X + rightEye.Position.X) / 2,
                (leftEye.Position.Y + rightEye.Position.Y) / 2),
            0);
        var hat = new PresentationAttachment(
            new PresentationPoint(
                Math.Clamp(nativeFrame.HatX * (_tuning?.HatFollow ?? 1) + (_tuning?.HatOffsetX ?? 0), -0.3, 0.3),
                Math.Clamp(nativeFrame.HatY + (_tuning?.HatOffsetY ?? 0), -1, 1)),
            Math.Clamp(
                nativeFrame.HatAngle * (1 - (_tuning?.HatDamping ?? 0)),
                -(_tuning?.HatRotationLimit ?? 0.35),
                _tuning?.HatRotationLimit ?? 0.35));
        _stepCount++;
        return AuthoredPoseSampler.CreateSoftBodyActive(
            contour,
            eyes,
            leftEye,
            rightEye,
            hat,
            _stepCount,
            metrics);
    }

    protected override void OnSuspended() => ReleaseSession();

    protected override void DisposeCore() => ReleaseSession();

    private void ReleaseSession()
    {
        _softBody?.Dispose();
        _softBody = null;
        _session?.Dispose();
        _session = null;
        _lastAuthoritativeCenter = null;
        _physicsAnchorX = 0;
        _physicsAnchorY = 0;
        _lastLandingStart = long.MinValue;
        _wasDragged = false;
        _dragRecoveryRemaining = TimeSpan.Zero;
    }

    private static bool HasFinitePresentationCoordinates(Box2DSoftBodyNativeFrame frame)
    {
        if (!double.IsFinite(frame.CentroidX) || !double.IsFinite(frame.CentroidY) ||
            !double.IsFinite(frame.LeftEyeX) || !double.IsFinite(frame.LeftEyeY) ||
            !double.IsFinite(frame.RightEyeX) || !double.IsFinite(frame.RightEyeY) ||
            !double.IsFinite(frame.HatX) || !double.IsFinite(frame.HatY) || !double.IsFinite(frame.HatAngle))
        {
            return false;
        }

        for (var index = 0; index < frame.PointCount * 2; index++)
        {
            if (!double.IsFinite(frame.Points[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static PresentationPoint CreateBoundedPoint(double x, double y) =>
        new(Math.Clamp(x, -1, 1), Math.Clamp(y, -1, 1));

    private bool TryEnsureSoftBody(out PhysicsPresentationFallbackReason fallbackReason)
    {
        if (_session is null && !Box2DNativeLoader.TryLoad(
            _libraryPath,
            Box2DNativeLoader.PinnedRequirements,
            out _session,
            out _))
        {
            fallbackReason = PhysicsPresentationFallbackReason.NativeUnavailable;
            return false;
        }

        if (_softBody is null)
        {
            var created = _tuning is null
                ? _session!.TryCreateSoftBody(_configuration.NodeCount, _configuration.SubStepCount, out _softBody, out _lastNativeError)
                : _session!.TryCreateSoftBody(_tuning, out _softBody, out _lastNativeError);
            if (!created)
            {
                ReleaseSession();
                fallbackReason = PhysicsPresentationFallbackReason.SimulationUnstable;
                return false;
            }
        }

        if (_tuning is not null && _stepCount == 0)
        {
            _physicsAnchorY = _tuning.DropHeight;
        }

        fallbackReason = PhysicsPresentationFallbackReason.None;
        return true;
    }
}
