using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Tuning;

namespace Dango.Desktop.Physics.Presentation;

public sealed record StackedPhysicsPresentationFrame(
    IReadOnlyList<PhysicsPresentationFrame> Bodies,
    IReadOnlyList<LogicalRect> AuthoritativeBounds,
    IReadOnlyList<LogicalVector> RenderOffsets,
    int ContactCount,
    IReadOnlyList<bool> AdjacentContacts,
    bool ConstraintsReleased,
    double MaximumNodeSpeed)
{
    public IReadOnlyList<Dango.Physics.DangoEntityId> EntityIds { get; init; } = [];
    public IReadOnlyList<int> ColorSlots { get; init; } = [];
    public GaitPresentationFrame? Gait { get; init; }
}

public sealed class Box2DStackedContactPresentationWorld : IDisposable
{
    public const int MaximumBodyCount = 3;

    private static readonly SoftBodyConfiguration Configuration = new(16, 4);
    private static readonly SoftBodyTuningParameters ApprovedGroundedTuning = new();
    private static readonly TimeSpan MaximumFixedStep = TimeSpan.FromMilliseconds(100);

    private readonly string libraryPath;
    private readonly double? groundAnchorCenterX;
    private readonly double? groundAnchorCenterY;
    private Box2DNativeSession? session;
    private Box2DStackedSoftBodyNativeSession? stackedSoftBodies;
    private StackedPhysicsPresentationFrame? lastStableFrame;
    private int stepCount;
    private bool restartBlocked;
    private bool disposed;

    public Box2DStackedContactPresentationWorld(
        string libraryPath,
        double? groundAnchorCenterX = null,
        double? groundAnchorCenterY = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        this.libraryPath = libraryPath;
        this.groundAnchorCenterX = groundAnchorCenterX;
        this.groundAnchorCenterY = groundAnchorCenterY;
    }

    public StackedPhysicsPresentationFrame Step(
        IReadOnlyList<PhysicsPresentationInput> inputs,
        TimeSpan fixedStep)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count is < 1 or > MaximumBodyCount)
        {
            throw new ArgumentException("多体堆叠场景必须包含一至三个团子。", nameof(inputs));
        }
        if (inputs.Select(input => input.DangoId).Distinct(StringComparer.Ordinal).Count() != inputs.Count)
        {
            throw new ArgumentException("多体堆叠场景中的团子标识必须互不相同。", nameof(inputs));
        }
        if (fixedStep <= TimeSpan.Zero || fixedStep > MaximumFixedStep)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedStep));
        }
        var fallbackReason = GetFallbackReason(inputs);
        if (fallbackReason != PhysicsPresentationFallbackReason.None)
        {
            ReleaseNativeWorld();
            return CreateFallback(inputs, fallbackReason);
        }
        if (restartBlocked)
        {
            return CreateLatchedFrame(inputs);
        }
        if (!TryEnsureNativeWorld())
        {
            return CreateFallback(inputs, PhysicsPresentationFallbackReason.NativeUnavailable);
        }

        var anchors = StackedPresentationAnchorMapper.Map(
            inputs,
            groundAnchorCenterX ?? inputs[0].AuthoritativeBounds.Center.X,
            groundAnchorCenterY ?? inputs[0].AuthoritativeBounds.Center.Y);
        var nativeFrame = stackedSoftBodies!.Step(
            fixedStep,
            anchors,
            0,
            0);
        if (!nativeFrame.Succeeded || nativeFrame.PointCount != Configuration.NodeCount)
        {
            ReleaseNativeWorld();
            restartBlocked = true;
            return CreateLatchedFrame(inputs, nativeFrame.ErrorCode);
        }

        stepCount++;
        var bodies = Enumerable.Range(0, inputs.Count)
            .Select(index => CreateFrame(
                nativeFrame.Points[index],
                nativeFrame.SignedAreas[index],
                nativeFrame.CentroidXs[index],
                nativeFrame.CentroidYs[index],
                nativeFrame.MaximumSpeed,
                index == 1 ? -1 : 1))
            .ToArray();
        var horizontalScale = StackedPresentationAnchorMapper.HorizontalScale(inputs);
        var verticalScale = StackedPresentationAnchorMapper.VerticalScale(inputs);
        var renderOffsets = Enumerable.Range(0, inputs.Count)
            .Select(index => new LogicalVector(
                (nativeFrame.CoreXs[index] - anchors[index * 2]) * horizontalScale,
                -(nativeFrame.CoreYs[index] - anchors[(index * 2) + 1]) * verticalScale))
            .ToArray();
        CloseAdjacentVisualSeams(inputs, bodies, renderOffsets, nativeFrame.AdjacentContacts);
        lastStableFrame = new(
            bodies,
            inputs.Select(input => input.AuthoritativeBounds).ToArray(),
            renderOffsets,
            nativeFrame.ContactCount,
            nativeFrame.AdjacentContacts,
            false,
            nativeFrame.MaximumSpeed);
        return lastStableFrame;
    }

    private static void CloseAdjacentVisualSeams(
        IReadOnlyList<PhysicsPresentationInput> inputs,
        IReadOnlyList<PhysicsPresentationFrame> bodies,
        LogicalVector[] offsets,
        IReadOnlyList<bool> adjacentContacts)
    {
        const double targetOverlap = -2;
        for (var lower = 0; lower < adjacentContacts.Count; lower++)
        {
            if (!adjacentContacts[lower])
            {
                continue;
            }

            var upper = lower + 1;
            var upperBottom = inputs[upper].AuthoritativeBounds.Y + offsets[upper].Y +
                ((1 - bodies[upper].Contour.Min(point => point.Y)) * inputs[upper].AuthoritativeBounds.Height / 2);
            var lowerTop = inputs[lower].AuthoritativeBounds.Y + offsets[lower].Y +
                ((1 - bodies[lower].Contour.Max(point => point.Y)) * inputs[lower].AuthoritativeBounds.Height / 2);
            var gap = lowerTop - upperBottom;
            offsets[upper] = new LogicalVector(offsets[upper].X, offsets[upper].Y + gap - targetOverlap);
        }
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ReleaseNativeWorld();
        restartBlocked = false;
        lastStableFrame = null;
    }

    public void ResumeAfterExplicitPopulationChange()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!restartBlocked)
        {
            return;
        }

        Reset();
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
        return stackedSoftBodies is not null ||
            session!.TryCreateStackedSoftBodies(
                ApprovedGroundedTuning,
                out stackedSoftBodies,
                out _);
    }

    private void ReleaseNativeWorld()
    {
        stackedSoftBodies?.Dispose();
        stackedSoftBodies = null;
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
            maximumSpeed < 0.03);
        var eyeCenter = 0.06 * facing;
        return AuthoredPoseSampler.CreateSoftBodyActive(
            contour,
            new PresentationAttachment(new PresentationPoint(eyeCenter, -0.05), 0),
            new PresentationAttachment(new PresentationPoint(eyeCenter - 0.24, -0.05), 0),
            new PresentationAttachment(new PresentationPoint(eyeCenter + 0.24, -0.05), 0),
            new PresentationAttachment(new PresentationPoint(0.04 * facing, 0.82), 0),
            stepCount,
            metrics);
    }

    private StackedPhysicsPresentationFrame CreateFallback(
        IReadOnlyList<PhysicsPresentationInput> inputs,
        PhysicsPresentationFallbackReason reason,
        int nativeErrorCode = 0) =>
        new(
            inputs.Select(input => AuthoredPoseSampler.CreateFallback(input, reason, nativeErrorCode)).ToArray(),
            inputs.Select(input => input.AuthoritativeBounds).ToArray(),
            inputs.Select(_ => new LogicalVector(0, 0)).ToArray(),
            0,
            Enumerable.Repeat(false, Math.Max(0, inputs.Count - 1)).ToArray(),
            true,
            0);

    private StackedPhysicsPresentationFrame CreateLatchedFrame(
        IReadOnlyList<PhysicsPresentationInput> inputs,
        int nativeErrorCode = 0)
    {
        if (lastStableFrame is null || lastStableFrame.Bodies.Count != inputs.Count)
        {
            return CreateFallback(inputs, PhysicsPresentationFallbackReason.SimulationUnstable, nativeErrorCode);
        }

        return lastStableFrame with
        {
            AuthoritativeBounds = inputs.Select(input => input.AuthoritativeBounds).ToArray(),
            ContactCount = 0,
            AdjacentContacts = Enumerable.Repeat(false, Math.Max(0, inputs.Count - 1)).ToArray(),
            ConstraintsReleased = true,
            MaximumNodeSpeed = 0,
        };
    }

    private static PhysicsPresentationFallbackReason GetFallbackReason(
        IReadOnlyList<PhysicsPresentationInput> inputs)
    {
        if (inputs.Any(input => input.Action == Core.Animation.DangoActionState.Hidden))
        {
            return PhysicsPresentationFallbackReason.Hidden;
        }
        if (inputs.Any(input => input.ReducedMotion))
        {
            return PhysicsPresentationFallbackReason.ReducedMotion;
        }
        return inputs.All(input => input.IsHostAvailable)
            ? PhysicsPresentationFallbackReason.None
            : PhysicsPresentationFallbackReason.HostUnavailable;
    }
}

internal static class StackedPresentationAnchorMapper
{
    public static float[] Map(
        IReadOnlyList<PhysicsPresentationInput> inputs,
        double referenceCenterX,
        double groundAnchorCenterY)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            throw new ArgumentException("至少需要一个表现输入。", nameof(inputs));
        }
        var horizontalScale = HorizontalScale(inputs);
        var verticalScale = VerticalScale(inputs);
        return inputs
            .SelectMany(input =>
            {
                var center = input.AuthoritativeBounds.Center;
                return new[]
                {
                    (float)((center.X - referenceCenterX) / horizontalScale),
                    (float)((groundAnchorCenterY - center.Y) / verticalScale),
                };
            })
            .ToArray();
    }

    public static double HorizontalScale(IReadOnlyList<PhysicsPresentationInput> inputs) =>
        Math.Max(1, inputs.Average(input => input.AuthoritativeBounds.Width) / 2);

    public static double VerticalScale(IReadOnlyList<PhysicsPresentationInput> inputs) =>
        Math.Max(1, inputs.Average(input => input.AuthoritativeBounds.Height) / 2);
}
