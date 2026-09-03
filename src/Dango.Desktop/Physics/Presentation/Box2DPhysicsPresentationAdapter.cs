using Dango.Desktop.Physics.Native;

namespace Dango.Desktop.Physics.Presentation;

public sealed class Box2DPhysicsPresentationAdapter : PhysicsPresentationWorldBase
{
    private readonly string _libraryPath;
    private readonly SoftBodyConfiguration _configuration;
    private Box2DNativeSession? _session;
    private Box2DSoftBodyNativeSession? _softBody;
    private Core.Coordinates.LogicalPoint? _originCenter;
    private double _physicsAnchorX;
    private double _physicsAnchorY;
    private long _lastLandingStart = long.MinValue;
    private int _stepCount;

    public Box2DPhysicsPresentationAdapter(string libraryPath)
        : this(libraryPath, new SoftBodyConfiguration(12, 2))
    {
    }

    public Box2DPhysicsPresentationAdapter(string libraryPath, SoftBodyConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        if (configuration.NodeCount is not (12 or 16) || configuration.SubStepCount is not (2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }

        _libraryPath = libraryPath;
        _configuration = configuration;
    }

    protected override PhysicsPresentationFrame StepActive(PhysicsPresentationInput input, TimeSpan fixedStep)
    {
        if (!TryEnsureSoftBody(out var fallbackReason))
        {
            return AuthoredPoseSampler.CreateFallback(input, fallbackReason);
        }

        _originCenter ??= input.AuthoritativeBounds.Center;
        var desiredAnchorX = (input.AuthoritativeBounds.Center.X - _originCenter.Value.X) /
            input.AuthoritativeBounds.Width;
        var desiredAnchorY = (_originCenter.Value.Y - input.AuthoritativeBounds.Center.Y) /
            input.AuthoritativeBounds.Height;
        _physicsAnchorX += Math.Clamp(desiredAnchorX - _physicsAnchorX, -0.015, 0.015);
        _physicsAnchorY += Math.Clamp(desiredAnchorY - _physicsAnchorY, -0.015, 0.015);
        var cue = 0;
        if (input.Action == Core.Animation.DangoActionState.Landing &&
            input.AuthoritativeStartTimeMilliseconds != _lastLandingStart)
        {
            cue = 1;
            _lastLandingStart = input.AuthoritativeStartTimeMilliseconds;
        }
        else if (input.Action == Core.Animation.DangoActionState.Dragged)
        {
            cue = 2;
        }

        var nativeFrame = _softBody!.Step(
            fixedStep,
            cue,
            (float)_physicsAnchorX,
            (float)_physicsAnchorY,
            (float)input.Strength);
        if (!nativeFrame.Succeeded || nativeFrame.PointCount != _configuration.NodeCount)
        {
            ReleaseSession();
            return AuthoredPoseSampler.CreateFallback(
                input,
                PhysicsPresentationFallbackReason.SimulationUnstable,
                nativeFrame.ErrorCode);
        }

        var contour = new PresentationPoint[nativeFrame.PointCount];
        for (var index = 0; index < contour.Length; index++)
        {
            contour[index] = new PresentationPoint(
                nativeFrame.Points[index * 2],
                nativeFrame.Points[index * 2 + 1]);
        }
        var metrics = new SoftBodyMetrics(
            _configuration,
            nativeFrame.SignedArea,
            new PresentationPoint(nativeFrame.CentroidX, nativeFrame.CentroidY),
            nativeFrame.MaximumRadius,
            nativeFrame.MaximumSpeed,
            nativeFrame.EnergyProxy,
            nativeFrame.SignedArea >= SoftBodyStabilityLimits.MinimumSignedArea,
            nativeFrame.IsSleeping);
        _stepCount++;
        return AuthoredPoseSampler.CreateSoftBodyActive(contour, _stepCount, metrics);
    }

    protected override void OnSuspended() => ReleaseSession();

    protected override void DisposeCore() => ReleaseSession();

    private void ReleaseSession()
    {
        _softBody?.Dispose();
        _softBody = null;
        _session?.Dispose();
        _session = null;
        _originCenter = null;
        _physicsAnchorX = 0;
        _physicsAnchorY = 0;
        _lastLandingStart = long.MinValue;
    }

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

        if (_softBody is null && !_session!.TryCreateSoftBody(
            _configuration.NodeCount,
            _configuration.SubStepCount,
            out _softBody,
            out _))
        {
            ReleaseSession();
            fallbackReason = PhysicsPresentationFallbackReason.SimulationUnstable;
            return false;
        }

        fallbackReason = PhysicsPresentationFallbackReason.None;
        return true;
    }
}
