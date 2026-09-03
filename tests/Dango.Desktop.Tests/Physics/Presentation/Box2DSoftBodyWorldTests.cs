using System.Diagnostics;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Presentation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Physics.Presentation;

[TestClass]
public sealed class Box2DSoftBodyWorldTests
{
    private static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60d);
    private static readonly SoftBodyConfiguration[] Candidates =
    [
        new(12, 2),
        new(12, 4),
        new(16, 2),
        new(16, 4),
    ];

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void AllCandidatesRemainBoundedAcrossTheSameStressSequence()
    {
        foreach (var configuration in Candidates)
        {
            using IPhysicsPresentationWorld world = new Box2DPhysicsPresentationAdapter(
                Box2DNativeLoader.DefaultLibraryPath,
                configuration);
            var stopwatch = Stopwatch.StartNew();
            var observations = new StabilityObservations();

            var frame = Run(world, DangoActionState.Idle, 3_600, 0, observations);
            Assert.IsTrue(frame.Diagnostics.SoftBody!.IsSleeping, configuration.ToString());

            frame = Run(world, DangoActionState.Landing, 600, 0, observations);
            frame = Run(world, DangoActionState.Dragged, 600, 0.3, observations);
            frame = world.Step(CreateInput(DangoActionState.Idle, 0), TimeSpan.FromMilliseconds(100));
            AssertStable(frame, configuration);
            observations.Observe(frame);

            stopwatch.Stop();
            Assert.IsTrue(observations.MaximumSpeed <= SoftBodyStabilityLimits.MaximumNodeSpeed, configuration.ToString());
            Assert.IsTrue(observations.MaximumRadius <= SoftBodyStabilityLimits.MaximumRadius, configuration.ToString());
            TestContext.WriteLine(
                $"nodes={configuration.NodeCount}; substeps={configuration.SubStepCount}; " +
                $"minArea={observations.MinimumArea:F6}; maxSpeed={observations.MaximumSpeed:F6}; " +
                $"maxRadius={observations.MaximumRadius:F6}; maxCentroid={observations.MaximumCentroid:F6}; " +
                $"maxEnergy={observations.MaximumEnergy:F6}; elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
    }

    [TestMethod]
    public void SameConfigurationAndInputsReplayWithinApprovedTolerance()
    {
        foreach (var configuration in Candidates)
        {
            var first = Replay(configuration);
            var second = Replay(configuration);

            Assert.AreEqual(first.Count, second.Count);
            for (var index = 0; index < first.Count; index++)
            {
                Assert.AreEqual(first[index].X, second[index].X, SoftBodyStabilityLimits.ReplayTolerance);
                Assert.AreEqual(first[index].Y, second[index].Y, SoftBodyStabilityLimits.ReplayTolerance);
            }
        }
    }

    [TestMethod]
    public void RepeatedInitializationAndDisposalReturnsNativeAllocationsToBaseline()
    {
        for (var iteration = 0; iteration < 50; iteration++)
        {
            using var world = new Box2DPhysicsPresentationAdapter(
                Box2DNativeLoader.DefaultLibraryPath,
                Candidates[iteration % Candidates.Length]);
            var frame = world.Step(CreateInput(DangoActionState.Idle, 0), FixedStep);
            AssertStable(frame, Candidates[iteration % Candidates.Length]);
        }
    }

    [TestMethod]
    public void DefaultUsesTheLowestSolverWorkloadCandidateThatPassedTheSharedSequence()
    {
        using var world = new Box2DPhysicsPresentationAdapter(Box2DNativeLoader.DefaultLibraryPath);

        var frame = world.Step(CreateInput(DangoActionState.Idle, 0), FixedStep);

        Assert.AreEqual(new SoftBodyConfiguration(12, 2), frame.Diagnostics.SoftBody!.Configuration);
    }

    [TestMethod]
    public void DefaultStructCannotBypassConfigurationValidation()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Box2DPhysicsPresentationAdapter(Box2DNativeLoader.DefaultLibraryPath, default));
    }

    [TestMethod]
    public void SoftBodyNeverChangesTheAuthoritativeInput()
    {
        var input = CreateInput(DangoActionState.Dragged, 0.3);
        var originalBounds = input.AuthoritativeBounds;
        var originalAction = input.Action;
        using var world = new Box2DPhysicsPresentationAdapter(
            Box2DNativeLoader.DefaultLibraryPath,
            new SoftBodyConfiguration(12, 4));

        var frame = world.Step(input, FixedStep);

        AssertStable(frame, new SoftBodyConfiguration(12, 4));
        Assert.AreEqual(originalBounds, input.AuthoritativeBounds);
        Assert.AreEqual(originalAction, input.Action);
        Assert.IsTrue(frame.Contour.All(point =>
            Math.Abs(point.X) <= SoftBodyStabilityLimits.MaximumRadius &&
            Math.Abs(point.Y) <= SoftBodyStabilityLimits.MaximumRadius));
    }

    [TestMethod]
    public void ExtremeAuthoritativeJumpIsRateLimitedInsidePresentationSpace()
    {
        using var world = new Box2DPhysicsPresentationAdapter(
            Box2DNativeLoader.DefaultLibraryPath,
            new SoftBodyConfiguration(12, 2));
        world.Step(CreateInput(DangoActionState.Idle, 0), FixedStep);
        var jumpedInput = CreateInput(DangoActionState.Dragged, 100);
        var originalBounds = jumpedInput.AuthoritativeBounds;

        var frame = world.Step(jumpedInput, FixedStep);

        AssertStable(frame, new SoftBodyConfiguration(12, 2));
        Assert.AreEqual(originalBounds, jumpedInput.AuthoritativeBounds);
        Assert.IsTrue(frame.Diagnostics.SoftBody!.MaximumRadius <= SoftBodyStabilityLimits.MaximumRadius);
        Assert.IsTrue(Math.Abs(frame.Diagnostics.SoftBody.Centroid.X) <= SoftBodyStabilityLimits.MaximumCentroidOffset);
    }

    private static PhysicsPresentationFrame Run(
        IPhysicsPresentationWorld world,
        DangoActionState action,
        int steps,
        double horizontalOffset,
        StabilityObservations observations)
    {
        PhysicsPresentationFrame? frame = null;
        for (var index = 0; index < steps; index++)
        {
            frame = world.Step(CreateInput(action, horizontalOffset), FixedStep);
            AssertStable(frame, null);
            observations.Observe(frame);
        }

        return frame!;
    }

    private static IReadOnlyList<PresentationPoint> Replay(SoftBodyConfiguration configuration)
    {
        using var world = new Box2DPhysicsPresentationAdapter(
            Box2DNativeLoader.DefaultLibraryPath,
            configuration);
        var observations = new StabilityObservations();
        Run(world, DangoActionState.Idle, 240, 0, observations);
        Run(world, DangoActionState.Landing, 120, 0, observations);
        return Run(world, DangoActionState.Dragged, 180, 0.25, observations).Contour;
    }

    private static void AssertStable(PhysicsPresentationFrame frame, SoftBodyConfiguration? configuration)
    {
        Assert.AreEqual(
            PhysicsPresentationFallbackReason.None,
            frame.FallbackReason,
            $"{configuration}; native={frame.Diagnostics.NativeErrorCode}");
        Assert.AreEqual(PhysicsPresentationBackend.Box2D, frame.Diagnostics.Backend);
        Assert.IsNotNull(frame.Diagnostics.SoftBody);
        Assert.IsTrue(frame.Diagnostics.SoftBody.IsOrientationValid);
        Assert.IsTrue(double.IsFinite(frame.Diagnostics.SoftBody.SignedArea));
        Assert.IsTrue(frame.Diagnostics.SoftBody.SignedArea >= SoftBodyStabilityLimits.MinimumSignedArea);
        Assert.IsTrue(
            Math.Sqrt(
                (frame.Diagnostics.SoftBody.Centroid.X * frame.Diagnostics.SoftBody.Centroid.X) +
                (frame.Diagnostics.SoftBody.Centroid.Y * frame.Diagnostics.SoftBody.Centroid.Y)) <=
            SoftBodyStabilityLimits.MaximumCentroidOffset);
        Assert.IsTrue(
            frame.Diagnostics.SoftBody.EnergyProxy <=
            frame.Diagnostics.SoftBody.Configuration.NodeCount * SoftBodyStabilityLimits.MaximumEnergyPerNode);
        Assert.IsTrue(frame.Contour.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y)));
    }

    private static PhysicsPresentationInput CreateInput(DangoActionState action, double horizontalOffset) =>
        new(
            "dango-soft-body",
            new LogicalRect(200 + (horizontalOffset * 96), 240, 96, 80),
            action,
            action == DangoActionState.Landing ? 2_000 : 1_000,
            17,
            "ground-contact",
            new LogicalVector(0, -1),
            action == DangoActionState.Landing ? 0.8 : 0,
            1,
            false,
            true,
            []);

    private sealed class StabilityObservations
    {
        public double MinimumArea { get; private set; } = double.MaxValue;

        public double MaximumSpeed { get; private set; }

        public double MaximumRadius { get; private set; }

        public double MaximumCentroid { get; private set; }

        public double MaximumEnergy { get; private set; }

        public void Observe(PhysicsPresentationFrame frame)
        {
            var metrics = frame.Diagnostics.SoftBody!;
            MinimumArea = Math.Min(MinimumArea, metrics.SignedArea);
            MaximumSpeed = Math.Max(MaximumSpeed, metrics.MaximumNodeSpeed);
            MaximumRadius = Math.Max(MaximumRadius, metrics.MaximumRadius);
            MaximumCentroid = Math.Max(
                MaximumCentroid,
                Math.Sqrt(
                    (metrics.Centroid.X * metrics.Centroid.X) +
                    (metrics.Centroid.Y * metrics.Centroid.Y)));
            MaximumEnergy = Math.Max(MaximumEnergy, metrics.EnergyProxy);
        }
    }
}
