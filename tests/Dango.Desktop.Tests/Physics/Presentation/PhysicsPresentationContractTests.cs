using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Core.Scene;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Tests.Physics.Presentation.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Physics.Presentation;

[TestClass]
public sealed class PhysicsPresentationContractTests
{
    private static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60d);

    [TestMethod]
    public void FakeWorldIsDeterministicAndCannotChangeAuthoritativeSnapshot()
    {
        var snapshot = CreateSnapshot(DangoActionState.Landing);
        var original = snapshot with { Dango = snapshot.Dango with { } };
        var input = PhysicsPresentationInput.FromSnapshot(snapshot);

        using IPhysicsPresentationWorld firstWorld = new DeterministicFakePhysicsWorld();
        using IPhysicsPresentationWorld secondWorld = new DeterministicFakePhysicsWorld();
        var first = firstWorld.Step(input, FixedStep);
        var second = secondWorld.Step(input, FixedStep);

        Assert.AreEqual(original, snapshot);
        Assert.AreEqual(original.Dango.VisualBounds, snapshot.Dango.VisualBounds);
        Assert.AreEqual(original.Dango.State, snapshot.Dango.State);
        Assert.AreEqual(original.Dango.GroundContactPoint, snapshot.Dango.GroundContactPoint);
        CollectionAssert.AreEqual(first.Contour.ToArray(), second.Contour.ToArray());
        Assert.IsTrue(first.IsVisible);
        Assert.AreEqual(PhysicsPresentationFallbackReason.None, first.FallbackReason);
    }

    [TestMethod]
    public void OutputContainsOnlyLocalPresentationGeometry()
    {
        var snapshot = CreateSnapshot(DangoActionState.Idle);
        using IPhysicsPresentationWorld world = new DeterministicFakePhysicsWorld();

        var frame = world.Step(PhysicsPresentationInput.FromSnapshot(snapshot), FixedStep);

        Assert.IsTrue(frame.Contour.Count >= 8);
        Assert.IsTrue(frame.Contour.All(point =>
            point.X is >= -1 and <= 1 && point.Y is >= -1 and <= 1));
        Assert.IsTrue(frame.Eyes.Position.X is >= -1 and <= 1);
        Assert.IsTrue(frame.Hat.Position.Y is >= -1 and <= 1);
        Assert.IsFalse(typeof(PhysicsPresentationFrame).GetProperties().Any(property =>
            property.Name.Contains("Authoritative", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Landing", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Collision", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void HiddenReducedMotionAndUnavailableHostDoNotStepPhysics()
    {
        using IPhysicsPresentationWorld world = new DeterministicFakePhysicsWorld();

        var hidden = world.Step(
            PhysicsPresentationInput.FromSnapshot(CreateSnapshot(DangoActionState.Hidden)),
            FixedStep);
        var reduced = world.Step(
            PhysicsPresentationInput.FromSnapshot(CreateSnapshot(DangoActionState.Idle)) with
            {
                ReducedMotion = true,
            },
            FixedStep);
        var unavailable = world.Step(
            PhysicsPresentationInput.FromSnapshot(CreateSnapshot(DangoActionState.Idle)) with
            {
                IsHostAvailable = false,
            },
            FixedStep);

        Assert.IsFalse(hidden.IsVisible);
        Assert.AreEqual(PhysicsPresentationFallbackReason.Hidden, hidden.FallbackReason);
        Assert.AreEqual(PhysicsPresentationFallbackReason.ReducedMotion, reduced.FallbackReason);
        Assert.AreEqual(PhysicsPresentationFallbackReason.HostUnavailable, unavailable.FallbackReason);
        Assert.AreEqual(0, hidden.Diagnostics.SimulatedStepCount);
        Assert.AreEqual(0, reduced.Diagnostics.SimulatedStepCount);
        Assert.AreEqual(0, unavailable.Diagnostics.SimulatedStepCount);
    }

    [TestMethod]
    public void InvalidInputAndStepAreRejectedAndDisposedWorldCannotStep()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PhysicsPresentationInput(
                "dango-1",
                new LogicalRect(0, 0, 96, 80),
                DangoActionState.Idle,
                0,
                1,
                "ground",
                new LogicalVector(double.NaN, 0),
                0.5,
                1,
                false,
                true,
                []));

        var input = PhysicsPresentationInput.FromSnapshot(CreateSnapshot(DangoActionState.Idle));
        using var world = new DeterministicFakePhysicsWorld();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => world.Step(input, TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => world.Step(input, TimeSpan.FromSeconds(1)));

        world.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => world.Step(input, FixedStep));
    }

    [TestMethod]
    public void PairedContactCueHasOnlyParticipantsAndVisualContactData()
    {
        var cueType = typeof(PairedPresentationContactCue);
        var names = cueType.GetProperties().Select(property => property.Name).ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "ParticipantIds", "ContactNormal", "Strength" },
            names);
        Assert.IsFalse(names.Any(name =>
            name.Contains("Room", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Friend", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Relationship", StringComparison.OrdinalIgnoreCase)));
        Assert.ThrowsExactly<ArgumentException>(() => new PairedPresentationContactCue(
            ["dango-1"],
            new LogicalVector(1, 0),
            0.5));
    }

    [TestMethod]
    public void NativeAdapterUsesTheSameContractAndFallsBackWhenLibraryIsMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-box2d-{Guid.NewGuid():N}.dll");
        using IPhysicsPresentationWorld world = new Box2DPhysicsPresentationAdapter(missingPath);

        var frame = world.Step(
            PhysicsPresentationInput.FromSnapshot(CreateSnapshot(DangoActionState.Idle)),
            FixedStep);

        Assert.IsTrue(frame.IsVisible);
        Assert.AreEqual(PhysicsPresentationFallbackReason.NativeUnavailable, frame.FallbackReason);
        Assert.AreEqual(PhysicsPresentationBackend.AuthoredFallback, frame.Diagnostics.Backend);
        Assert.AreEqual(0, frame.Diagnostics.SimulatedStepCount);
        Assert.AreEqual(PhysicsPresentationWorldState.Suspended, world.State);
    }

    [TestMethod]
    public void AuthoredFallbackPreservesFailureReasonAndNativeDiagnostics()
    {
        var input = PhysicsPresentationInput.FromSnapshot(CreateSnapshot(DangoActionState.Idle));

        var frame = AuthoredPoseSampler.CreateFallback(
            input, PhysicsPresentationFallbackReason.NativeUnavailable, nativeErrorCode: 731);

        Assert.IsTrue(frame.IsVisible);
        Assert.IsTrue(frame.Contour.Count >= 8);
        Assert.AreEqual(PhysicsPresentationFallbackReason.NativeUnavailable, frame.FallbackReason);
        Assert.AreEqual(PhysicsPresentationBackend.AuthoredFallback, frame.Diagnostics.Backend);
        Assert.AreEqual(PhysicsPresentationDiagnosticCode.Fallback, frame.Diagnostics.Code);
        Assert.AreEqual(0, frame.Diagnostics.SimulatedStepCount);
        Assert.AreEqual(731, frame.Diagnostics.NativeErrorCode);
    }

    [TestMethod]
    public void NativeAdapterLoadsPinnedLibraryThroughTheSharedContract()
    {
        using IPhysicsPresentationWorld world = new Box2DPhysicsPresentationAdapter(
            Box2DNativeLoader.DefaultLibraryPath);

        var frame = world.Step(
            PhysicsPresentationInput.FromSnapshot(CreateSnapshot(DangoActionState.Idle)),
            FixedStep);

        Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.FallbackReason);
        Assert.AreEqual(PhysicsPresentationBackend.Box2D, frame.Diagnostics.Backend);
        Assert.AreEqual(1, frame.Diagnostics.SimulatedStepCount);
        Assert.AreEqual(PhysicsPresentationWorldState.Active, world.State);
    }

    [TestMethod]
    public void InputRejectsAContactGroupThatIsNotAnIsolatedPair()
    {
        var snapshot = CreateSnapshot(DangoActionState.Reacting);
        var baseline = PhysicsPresentationInput.FromSnapshot(snapshot);

        Assert.ThrowsExactly<ArgumentException>(() => new PhysicsPresentationInput(
            baseline.DangoId,
            baseline.AuthoritativeBounds,
            baseline.Action,
            baseline.AuthoritativeStartTimeMilliseconds,
            baseline.DeterministicSeed,
            baseline.SemanticAnchor,
            baseline.ContactNormal,
            baseline.Strength,
            baseline.ParameterVersion,
            baseline.ReducedMotion,
            baseline.IsHostAvailable,
            ["dango-1", "dango-2", "dango-3"]));
    }

    private static SceneSnapshot CreateSnapshot(DangoActionState state) =>
        new(
            SceneSnapshot.CurrentVersion,
            1_000,
            23,
            "test-assets-v1",
            new DangoSnapshot(
                "dango-1",
                new LogicalRect(120, 240, 96, 80),
                state,
                900,
                23,
                new LogicalPoint(168, 320),
                state.ToString().ToLowerInvariant(),
                "click-body",
                "drag-body",
                "collision-body",
                "island-edge"),
            new IslandSnapshot(
                "island-1",
                new LogicalRect(100, 320, 240, 80),
                new LogicalRect(112, 320, 216, 8),
                new LogicalRect(100, 304, 24, 32),
                new LogicalRect(112, 288, 216, 32),
                new LogicalRect(100, 320, 240, 80)));
}
