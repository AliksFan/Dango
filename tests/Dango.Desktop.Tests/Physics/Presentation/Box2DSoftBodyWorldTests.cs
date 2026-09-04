using System.Diagnostics;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
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

    [TestMethod]
    public void SustainedAuthoritativeTravelStaysInsideLocalPresentationSpace()
    {
        using var world = Box2DPhysicsPresentationAdapter.CreateTuning(
            Box2DNativeLoader.DefaultLibraryPath,
            new SoftBodyTuningParameters());

        for (var index = 0; index < 180; index++)
        {
            var input = new PhysicsPresentationInput(
                "moving-dango",
                new LogicalRect(200 + (index * 4), 240 - (index * 8), 160, 120),
                DangoActionState.ClimbUp,
                1_000,
                17,
                "island-grab",
                new LogicalVector(0, -1),
                0,
                1,
                false,
                true,
                []);

            var frame = world.Step(input, FixedStep);

            Assert.AreEqual(
                PhysicsPresentationFallbackReason.None,
                frame.FallbackReason,
                $"step={index}; native={frame.Diagnostics.NativeErrorCode}");
            Assert.IsTrue(frame.Contour.All(point =>
                point.X is >= -1 and <= 1 && point.Y is >= -1 and <= 1));
        }

        for (var index = 0; index < 90; index++)
        {
            var landing = new PhysicsPresentationInput(
                "moving-dango",
                new LogicalRect(916, -1_192, 160, 120),
                DangoActionState.Landing,
                2_000,
                17,
                "ground",
                new LogicalVector(0, -1),
                1,
                1,
                false,
                true,
                []);
            var frame = world.Step(landing, FixedStep);
            Assert.AreEqual(
                PhysicsPresentationFallbackReason.None,
                frame.FallbackReason,
                $"landingStep={index}; native={frame.Diagnostics.NativeErrorCode}");
        }
    }

    [TestMethod]
    public void NativeAccessoryBodiesTurnThroughDampedConnectionsInsteadOfTeleporting()
    {
        using var world = new Box2DPhysicsPresentationAdapter(
            Box2DNativeLoader.DefaultLibraryPath,
            new SoftBodyConfiguration(12, 2));

        PhysicsPresentationFrame? centered = null;
        for (var index = 0; index < 90; index++)
        {
            centered = world.Step(CreateInput(DangoActionState.Idle, 0), FixedStep);
        }

        PhysicsPresentationFrame? left = null;
        for (var index = 0; index < 90; index++)
        {
            left = world.Step(CreateInput(DangoActionState.Idle, 0, DangoFacingDirection.Left), FixedStep);
        }

        var firstRight = world.Step(
            CreateInput(DangoActionState.Idle, 0, DangoFacingDirection.Right),
            FixedStep);
        PhysicsPresentationFrame? settledRight = null;
        for (var index = 0; index < 90; index++)
        {
            settledRight = world.Step(CreateInput(DangoActionState.Idle, 0, DangoFacingDirection.Right), FixedStep);
        }

        Assert.IsGreaterThan(left!.Eyes.Position.X, firstRight.Eyes.Position.X);
        Assert.IsGreaterThan(firstRight.Eyes.Position.X, settledRight!.Eyes.Position.X);
        Assert.IsGreaterThan(0.08, settledRight.Eyes.Position.X - left.Eyes.Position.X);
        var centeredEyeDistance = centered!.RightEye.Position.X - centered.LeftEye.Position.X;
        var turnedEyeDistance = left.RightEye.Position.X - left.LeftEye.Position.X;
        Assert.IsGreaterThan(turnedEyeDistance, centeredEyeDistance);
        Assert.AreEqual(34d / 72d, turnedEyeDistance, 0.04);
        Assert.IsGreaterThan(left.Hat.Position.X, firstRight.Hat.Position.X);
        Assert.IsGreaterThan(firstRight.Hat.Position.X, settledRight.Hat.Position.X);
        Assert.IsGreaterThan(0.015, settledRight.Hat.Position.X - left.Hat.Position.X);
    }

    [TestMethod]
    public void ViolentDragReleaseRecoversToAStableShapeDuringFalling()
    {
        using var world = new Box2DPhysicsPresentationAdapter(
            Box2DNativeLoader.DefaultLibraryPath,
            new SoftBodyConfiguration(12, 2));
        PhysicsPresentationFrame? baseline = null;
        for (var index = 0; index < 90; index++)
        {
            baseline = world.Step(CreateInput(DangoActionState.Idle, 0), FixedStep);
        }

        for (var index = 0; index < 240; index++)
        {
            var x = index % 2 == 0 ? 80 : 720;
            var y = index % 4 < 2 ? 80 : 420;
            world.Step(
                new PhysicsPresentationInput(
                    "violently-dragged-dango",
                    new LogicalRect(x, y, 96, 80),
                    DangoActionState.Dragged,
                    1_000 + index,
                    17,
                    "drag",
                    new LogicalVector(0, -1),
                    0,
                    1,
                    false,
                    true,
                    []),
                FixedStep);
        }

        PhysicsPresentationFrame? recovered = null;
        for (var index = 0; index < 60; index++)
        {
            recovered = world.Step(CreateInput(DangoActionState.Falling, 0), FixedStep);
        }

        AssertStable(recovered!, new SoftBodyConfiguration(12, 2));
        var maximumPointError = baseline!.Contour.Zip(
            recovered!.Contour,
            (expected, actual) => Math.Sqrt(
                Math.Pow(expected.X - actual.X, 2) +
                Math.Pow(expected.Y - actual.Y, 2))).Max();
        TestContext.WriteLine(
            $"sleeping={recovered!.Diagnostics.SoftBody!.IsSleeping}; " +
            $"speed={recovered.Diagnostics.SoftBody.MaximumNodeSpeed:F6}; " +
            $"centroid=({recovered.Diagnostics.SoftBody.Centroid.X:F6},{recovered.Diagnostics.SoftBody.Centroid.Y:F6}); " +
            $"maxPointError={maximumPointError:F6}");
        Assert.IsLessThan(0.005, maximumPointError);
        Assert.IsLessThan(0.05, recovered.Diagnostics.SoftBody.MaximumNodeSpeed);
        Assert.IsLessThan(0.03, Math.Abs(recovered.Diagnostics.SoftBody.Centroid.X));
        Assert.IsLessThan(0.03, Math.Abs(recovered.Diagnostics.SoftBody.Centroid.Y));
    }

    [TestMethod]
    public void ViolentDragSettlesIntoABoundedHeldStretch()
    {
        using var world = new Box2DPhysicsPresentationAdapter(
            Box2DNativeLoader.DefaultLibraryPath,
            new SoftBodyConfiguration(12, 2));
        PhysicsPresentationFrame? baseline = null;
        for (var index = 0; index < 90; index++)
        {
            baseline = world.Step(CreateInput(DangoActionState.Idle, 0), FixedStep);
        }

        for (var index = 0; index < 240; index++)
        {
            var x = index % 2 == 0 ? 80 : 720;
            var y = index % 4 < 2 ? 80 : 420;
            world.Step(CreateDragInput(x, y, 1_000 + index), FixedStep);
        }

        PhysicsPresentationFrame? recoveredWhileHeld = null;
        PhysicsPresentationFrame? settlingReference = null;
        for (var index = 0; index < 45; index++)
        {
            recoveredWhileHeld = world.Step(CreateDragInput(720, 420, 2_000 + index), FixedStep);
            if (index == 29)
            {
                settlingReference = recoveredWhileHeld;
            }
        }

        var settlingDelta = settlingReference!.Contour.Zip(
            recoveredWhileHeld!.Contour,
            (expected, actual) => Math.Sqrt(
                Math.Pow(expected.X - actual.X, 2) +
                Math.Pow(expected.Y - actual.Y, 2))).Max();
        var maximumPointError = baseline!.Contour.Zip(
            recoveredWhileHeld.Contour,
            (expected, actual) => Math.Sqrt(
                Math.Pow(expected.X - actual.X, 2) +
                Math.Pow(expected.Y - actual.Y, 2))).Max();
        TestContext.WriteLine(
            $"settlingDelta={settlingDelta:F6}; " +
            $"heldMaxPointError={maximumPointError:F6}; " +
            $"heldSpeed={recoveredWhileHeld.Diagnostics.SoftBody!.MaximumNodeSpeed:F6}");
        Assert.IsLessThan(0.02, settlingDelta);
        Assert.IsLessThan(0.25, maximumPointError);
        Assert.IsLessThan(0.15, recoveredWhileHeld.Diagnostics.SoftBody.MaximumNodeSpeed);
    }

    [TestMethod]
    public void DragAnchorCreatesVisibleButBoundedLocalStretch()
    {
        using var world = new Box2DPhysicsPresentationAdapter(
            Box2DNativeLoader.DefaultLibraryPath,
            new SoftBodyConfiguration(12, 2));
        PhysicsPresentationFrame? baseline = null;
        for (var index = 0; index < 90; index++)
        {
            baseline = world.Step(CreateInput(DangoActionState.Idle, 0), FixedStep);
        }

        PhysicsPresentationFrame? stretched = null;
        var topGrab = new PresentationPoint(0, 0.68);
        for (var index = 0; index < 90; index++)
        {
            stretched = world.Step(CreateDragInput(200, 240, 1_000 + index, topGrab), FixedStep);
        }

        AssertStable(stretched!, new SoftBodyConfiguration(12, 2));
        var baselineTop = baseline!.Contour.Max(point => point.Y);
        var baselineBottom = baseline.Contour.Min(point => point.Y);
        var stretchedTop = stretched!.Contour.Max(point => point.Y);
        var stretchedBottom = stretched.Contour.Min(point => point.Y);
        var addedHeight = (stretchedTop - stretchedBottom) - (baselineTop - baselineBottom);
        TestContext.WriteLine(
            $"addedHeight={addedHeight:F6}; topDelta={stretchedTop - baselineTop:F6}; " +
            $"bottomDelta={stretchedBottom - baselineBottom:F6}");
        Assert.IsGreaterThan(0.08, addedHeight, "提起时应能看出局部拉伸。");
        Assert.IsLessThan(0.20, addedHeight, "拉伸不得超过克制形变上限。");
        Assert.IsLessThan(
            Math.Abs(stretchedBottom - baselineBottom),
            Math.Abs(stretchedTop - baselineTop),
            "点击处应比悬垂的另一端更稳定。");
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

    private static PhysicsPresentationInput CreateInput(
        DangoActionState action,
        double horizontalOffset,
        DangoFacingDirection facingDirection = DangoFacingDirection.Center) =>
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
            [],
            facingDirection: facingDirection);

    private static PhysicsPresentationInput CreateDragInput(
        double x,
        double y,
        long timestamp,
        PresentationPoint? dragGrabPoint = null) =>
        new(
            "violently-dragged-dango",
            new LogicalRect(x, y, 96, 80),
            DangoActionState.Dragged,
            timestamp,
            17,
            "drag",
            new LogicalVector(0, -1),
            0,
            1,
            false,
            true,
            [],
            dragGrabPoint: dragGrabPoint);

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
