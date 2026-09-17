using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Tuning;

namespace Dango.Desktop.Physics.Presentation;

public sealed record PairedPhysicsPresentationFrame(
    PhysicsPresentationFrame First,
    PhysicsPresentationFrame Second,
    LogicalRect FirstAuthoritativeBounds,
    LogicalRect SecondAuthoritativeBounds,
    int ContactCount,
    bool ConstraintsReleased,
    double MaximumNodeSpeed,
    double FirstRenderOffsetX,
    double SecondRenderOffsetX);

public sealed class Box2DPairedContactPresentationWorld : IDisposable
{
    private static readonly SoftBodyConfiguration Configuration = new(16, 4);
    private static readonly SoftBodyTuningParameters ApprovedGroundedTuning = new();
    private static readonly TimeSpan MaximumFixedStep = TimeSpan.FromMilliseconds(100);

    private readonly string libraryPath;
    private Box2DNativeSession? session;
    private Box2DPairedSoftBodyNativeSession? pairedSoftBodies;
    private int stepCount;
    private bool disposed;

    public Box2DPairedContactPresentationWorld(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        this.libraryPath = libraryPath;
    }

    public PairedPhysicsPresentationFrame Step(
        PhysicsPresentationInput first,
        PhysicsPresentationInput second,
        PairedPresentationContactCue? cue,
        TimeSpan fixedStep)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.DangoId == second.DangoId)
        {
            throw new ArgumentException("双体技术证明必须包含两个不同的团子。", nameof(second));
        }
        if (fixedStep <= TimeSpan.Zero || fixedStep > MaximumFixedStep)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedStep));
        }
        ValidateCue(first, second, cue);

        var fallbackReason = GetFallbackReason(first, second);
        if (fallbackReason != PhysicsPresentationFallbackReason.None || !TryEnsureNativeWorld())
        {
            if (fallbackReason == PhysicsPresentationFallbackReason.None)
            {
                fallbackReason = PhysicsPresentationFallbackReason.NativeUnavailable;
            }
            ReleaseNativeWorld();
            return CreateFallback(first, second, fallbackReason);
        }

        var firstCenter = first.AuthoritativeBounds.Center;
        var secondCenter = second.AuthoritativeBounds.Center;
        var scale = Math.Max(1, (first.AuthoritativeBounds.Width + second.AuthoritativeBounds.Width) / 4);
        var anchorSeparationX = (float)((secondCenter.X - firstCenter.X) / scale);
        var anchorSeparationY = (float)((firstCenter.Y - secondCenter.Y) / scale);
        var firstAnchorX = -anchorSeparationX / 2;
        var firstAnchorY = -anchorSeparationY / 2;
        var secondAnchorX = anchorSeparationX / 2;
        var secondAnchorY = anchorSeparationY / 2;
        var normal = cue?.ContactNormal ?? new LogicalVector(0, 0);
        var normalLength = Math.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y));
        var nativeFrame = pairedSoftBodies!.Step(
            fixedStep,
            firstAnchorX,
            firstAnchorY,
            secondAnchorX,
            secondAnchorY,
            normalLength > 0 ? (float)(normal.X / normalLength) : 0,
            normalLength > 0 ? (float)(-normal.Y / normalLength) : 0,
            (float)(cue?.Strength ?? 0),
            cue is not null);
        if (!nativeFrame.Succeeded || nativeFrame.PointCount != Configuration.NodeCount)
        {
            ReleaseNativeWorld();
            return CreateFallback(first, second, PhysicsPresentationFallbackReason.SimulationUnstable, nativeFrame.ErrorCode);
        }

        stepCount++;
        var firstFrame = CreateFrame(
            nativeFrame.FirstPoints,
            nativeFrame.FirstSignedArea,
            nativeFrame.FirstCentroidX,
            nativeFrame.FirstCentroidY,
            nativeFrame.MaximumSpeed,
            facing: 1);
        var secondFrame = CreateFrame(
            nativeFrame.SecondPoints,
            nativeFrame.SecondSignedArea,
            nativeFrame.SecondCentroidX,
            nativeFrame.SecondCentroidY,
            nativeFrame.MaximumSpeed,
            facing: -1);
        var seamClosure = cue is null
            ? 0
            : Math.Min(first.AuthoritativeBounds.Width, second.AuthoritativeBounds.Width) *
                0.04 * cue.Strength;
        return new(
            firstFrame,
            secondFrame,
            first.AuthoritativeBounds,
            second.AuthoritativeBounds,
            nativeFrame.ContactCount,
            cue is null && nativeFrame.ContactCount == 0,
            nativeFrame.MaximumSpeed,
            seamClosure / 2,
            -seamClosure / 2);
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

    private static void ValidateCue(
        PhysicsPresentationInput first,
        PhysicsPresentationInput second,
        PairedPresentationContactCue? cue)
    {
        if (cue is null)
        {
            return;
        }
        var expected = new HashSet<string>([first.DangoId, second.DangoId], StringComparer.Ordinal);
        if (!expected.SetEquals(cue.ParticipantIds))
        {
            throw new ArgumentException("共同接触 cue 的参与者必须与本次双体输入完全一致。", nameof(cue));
        }
        var length = Math.Sqrt(
            (cue.ContactNormal.X * cue.ContactNormal.X) +
            (cue.ContactNormal.Y * cue.ContactNormal.Y));
        if (length < 0.9 || length > 1.1)
        {
            throw new ArgumentException("共同接触 cue 必须提供单位接触方向。", nameof(cue));
        }
        var firstCenter = first.AuthoritativeBounds.Center;
        var secondCenter = second.AuthoritativeBounds.Center;
        var anchorX = secondCenter.X - firstCenter.X;
        var anchorY = secondCenter.Y - firstCenter.Y;
        var anchorLength = Math.Sqrt((anchorX * anchorX) + (anchorY * anchorY));
        var alignment = ((anchorX * cue.ContactNormal.X) + (anchorY * cue.ContactNormal.Y)) /
            Math.Max(anchorLength, 0.0001);
        if (alignment < 0.9)
        {
            throw new ArgumentException("共同接触 cue 的方向必须由第一个参与者指向第二个参与者。", nameof(cue));
        }
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
        return pairedSoftBodies is not null ||
            session!.TryCreatePairedSoftBodies(
                ApprovedGroundedTuning,
                out pairedSoftBodies,
                out _);
    }

    private void ReleaseNativeWorld()
    {
        pairedSoftBodies?.Dispose();
        pairedSoftBodies = null;
        stepCount = 0;
    }

    private PhysicsPresentationFrame CreateFrame(
        float[] points,
        float signedArea,
        float centroidX,
        float centroidY,
        float maximumSpeed,
        int facing)
    {
        var contour = Enumerable.Range(0, Configuration.NodeCount)
            .Select(index => new PresentationPoint(
                Math.Clamp(points[index * 2], -1, 1),
                Math.Clamp(points[(index * 2) + 1], -1, 1)))
            .ToArray();
        var maximumRadius = contour.Max(point => Math.Sqrt((point.X * point.X) + (point.Y * point.Y)));
        var metrics = new SoftBodyMetrics(
            Configuration,
            signedArea,
            new PresentationPoint(Math.Clamp(centroidX, -1, 1), Math.Clamp(centroidY, -1, 1)),
            maximumRadius,
            maximumSpeed,
            maximumSpeed * maximumSpeed * Configuration.NodeCount,
            signedArea > 0,
            maximumSpeed < 0.02);
        var eyeCenter = 0.075 * facing;
        var leftEye = new PresentationAttachment(new PresentationPoint(eyeCenter - 0.24, -0.05), 0);
        var rightEye = new PresentationAttachment(new PresentationPoint(eyeCenter + 0.24, -0.05), 0);
        return AuthoredPoseSampler.CreateSoftBodyActive(
            contour,
            new PresentationAttachment(new PresentationPoint(eyeCenter, -0.05), 0),
            leftEye,
            rightEye,
            new PresentationAttachment(new PresentationPoint(0.04 * facing, 0.82), 0),
            stepCount,
            metrics);
    }

    private PairedPhysicsPresentationFrame CreateFallback(
        PhysicsPresentationInput first,
        PhysicsPresentationInput second,
        PhysicsPresentationFallbackReason reason,
        int nativeErrorCode = 0) =>
        new(
            AuthoredPoseSampler.CreateFallback(first, reason, nativeErrorCode),
            AuthoredPoseSampler.CreateFallback(second, reason, nativeErrorCode),
            first.AuthoritativeBounds,
            second.AuthoritativeBounds,
            0,
            true,
            0,
            0,
            0);

    private static PhysicsPresentationFallbackReason GetFallbackReason(
        PhysicsPresentationInput first,
        PhysicsPresentationInput second)
    {
        if (first.Action == Core.Animation.DangoActionState.Hidden ||
            second.Action == Core.Animation.DangoActionState.Hidden)
        {
            return PhysicsPresentationFallbackReason.Hidden;
        }
        if (first.ReducedMotion || second.ReducedMotion)
        {
            return PhysicsPresentationFallbackReason.ReducedMotion;
        }
        return first.IsHostAvailable && second.IsHostAvailable
            ? PhysicsPresentationFallbackReason.None
            : PhysicsPresentationFallbackReason.HostUnavailable;
    }
}
