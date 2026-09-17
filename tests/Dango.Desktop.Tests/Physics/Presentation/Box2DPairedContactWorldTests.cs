using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Physics.Presentation;

[TestClass]
public sealed class Box2DPairedContactWorldTests
{
    private static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60d);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void SharedCueProducesRealContactAndComplementaryCompression()
    {
        using var world = new Box2DPairedContactPresentationWorld(Box2DNativeLoader.DefaultLibraryPath);
        var separatedFirst = CreateInput("paired-a", 200);
        var separatedSecond = CreateInput("paired-b", 350);
        var first = CreateInput("paired-a", 244.75);
        var second = CreateInput("paired-b", 305.25);
        var cue = new PairedPresentationContactCue([first.DangoId, second.DangoId], new LogicalVector(1, 0), 1);

        PairedPhysicsPresentationFrame? baseline = null;
        for (var index = 0; index < 180; index++)
        {
            baseline = world.Step(separatedFirst, separatedSecond, null, FixedStep);
        }

        var frame = Approach(world, separatedFirst, separatedSecond, first, second, cue);

        Assert.IsGreaterThan(0, frame.ContactCount, "两个软体必须在同一个 Box2D world 中产生真实 contact。");
        Assert.AreEqual(first.AuthoritativeBounds, frame.FirstAuthoritativeBounds);
        Assert.AreEqual(second.AuthoritativeBounds, frame.SecondAuthoritativeBounds);
        var localGap = ((second.AuthoritativeBounds.Center.X - first.AuthoritativeBounds.Center.X) / 48) +
            frame.Second.Contour.Min(point => point.X) -
            frame.First.Contour.Max(point => point.X);
        var renderedGap = second.AuthoritativeBounds.X + frame.SecondRenderOffsetX -
            first.AuthoritativeBounds.X - frame.FirstRenderOffsetX +
            ((frame.Second.Contour.Min(point => point.X) - frame.First.Contour.Max(point => point.X)) *
                first.AuthoritativeBounds.Width * 0.45);
        var oppositeIndex = frame.Second.Contour.Count / 2;
        var firstNearCompression = baseline!.First.Contour[0].X - frame.First.Contour[0].X;
        var firstFarChange = Math.Abs(
            frame.First.Contour[oppositeIndex].X - baseline.First.Contour[oppositeIndex].X);
        var secondNearCompression = frame.Second.Contour[oppositeIndex].X -
            baseline.Second.Contour[oppositeIndex].X;
        var secondFarChange = Math.Abs(
            frame.Second.Contour[0].X - baseline.Second.Contour[0].X);
        TestContext.WriteLine(
            $"gap={localGap:F6}; renderedGap={renderedGap:F6}; first=[{frame.First.Contour.Min(point => point.X):F6},{frame.First.Contour.Max(point => point.X):F6}] " +
            $"second=[{frame.Second.Contour.Min(point => point.X):F6},{frame.Second.Contour.Max(point => point.X):F6}] " +
            $"compression=({firstNearCompression:F6},{secondNearCompression:F6}) " +
            $"farChange=({firstFarChange:F6},{secondFarChange:F6}) " +
            $"contactPoints=({frame.First.Contour[0].X:F6},{frame.Second.Contour[oppositeIndex].X:F6}) " +
            $"centroids=({frame.First.Diagnostics.SoftBody!.Centroid.X:F6},{frame.Second.Diagnostics.SoftBody!.Centroid.X:F6})");
        Assert.IsGreaterThanOrEqualTo(-0.16, localGap, "物理轮廓重叠不能超过自然贴贴所需的有限范围。");
        Assert.IsLessThanOrEqualTo(-0.09, localGap, "物理轮廓应允许少量重叠。");
        Assert.IsGreaterThanOrEqualTo(-4, renderedGap, "渲染接缝补偿不能造成明显的视觉穿入。");
        Assert.IsLessThanOrEqualTo(-0.5, renderedGap, "WPF 实际映射后必须消除可见缝隙。");
        Assert.IsGreaterThan(0.07, firstNearCompression, "第一只团子的接触侧应表现出明显的主动挤压。");
        Assert.IsGreaterThan(0.07, secondNearCompression, "第二只团子的接触侧应表现出明显的主动挤压。");
        Assert.IsLessThanOrEqualTo(
            (firstNearCompression * 0.30) + 0.01,
            firstFarChange,
            "第一只团子的远侧不应跟随接触侧发生整体形变。");
        Assert.IsLessThanOrEqualTo(
            (secondNearCompression * 0.30) + 0.01,
            secondFarChange,
            "第二只团子的远侧不应跟随接触侧发生整体形变。");
        Assert.IsLessThan(0, frame.First.Diagnostics.SoftBody!.Centroid.X);
        Assert.IsGreaterThan(0, frame.Second.Diagnostics.SoftBody!.Centroid.X);
    }

    [TestMethod]
    public void SeparatedPairSettlesOnTheSharedGround()
    {
        using var world = new Box2DPairedContactPresentationWorld(Box2DNativeLoader.DefaultLibraryPath);
        var first = CreateInput("paired-a", 200);
        var second = CreateInput("paired-b", 350);

        PairedPhysicsPresentationFrame? frame = null;
        for (var index = 0; index < 180; index++)
        {
            frame = world.Step(first, second, null, FixedStep);
            Assert.AreEqual(
                PhysicsPresentationFallbackReason.None,
                frame.First.FallbackReason,
                $"step={index}; native={frame.First.Diagnostics.NativeErrorCode}");
        }

        var firstBottom = frame!.First.Contour.Min(point => point.Y);
        var secondBottom = frame.Second.Contour.Min(point => point.Y);
        var approved = new SoftBodyTuningParameters();
        var firstWidth = frame.First.Contour.Max(point => point.X) - frame.First.Contour.Min(point => point.X);
        var firstHeight = frame.First.Contour.Max(point => point.Y) - firstBottom;
        TestContext.WriteLine($"groundedBottom=({firstBottom:F6},{secondBottom:F6}); speed={frame.MaximumNodeSpeed:F6}");
        Assert.AreEqual(16, frame.First.Contour.Count);
        Assert.AreEqual(16, frame.Second.Contour.Count);
        Assert.IsGreaterThanOrEqualTo(approved.GroundHeight - 0.02, firstBottom);
        Assert.IsLessThanOrEqualTo(approved.GroundHeight + 0.02, firstBottom);
        Assert.IsGreaterThanOrEqualTo(approved.GroundHeight - 0.02, secondBottom);
        Assert.IsLessThanOrEqualTo(approved.GroundHeight + 0.02, secondBottom);
        Assert.AreEqual(approved.GroundedAspectRatio, firstWidth / firstHeight, 0.09);
        Assert.AreEqual(0, frame.ContactCount, "地面 contact 不能被计入双体 contact。\n");
        Assert.IsLessThan(0.08, frame.MaximumNodeSpeed);
    }

    [TestMethod]
    public void CancellingCueAndSeparatingAnchorsReleasesContactWithinBoundedTime()
    {
        using var world = new Box2DPairedContactPresentationWorld(Box2DNativeLoader.DefaultLibraryPath);
        var separatedFirst = CreateInput("paired-a", 200);
        var separatedSecond = CreateInput("paired-b", 350);
        var first = CreateInput("paired-a", 244.75);
        var touchingSecond = CreateInput("paired-b", 305.25);
        var cue = new PairedPresentationContactCue([first.DangoId, touchingSecond.DangoId], new LogicalVector(1, 0), 1);

        Approach(world, separatedFirst, separatedSecond, first, touchingSecond, cue);

        PairedPhysicsPresentationFrame? released = null;
        for (var index = 0; index < 60; index++)
        {
            var amount = (index + 1d) / 60;
            var separatingFirst = CreateInput("paired-a", 244.75 + ((200 - 244.75) * amount));
            var separatingSecond = CreateInput("paired-b", 305.25 + ((350 - 305.25) * amount));
            released = world.Step(separatingFirst, separatingSecond, null, FixedStep);
        }
        for (var index = 0; index < 120; index++)
        {
            released = world.Step(separatedFirst, separatedSecond, null, FixedStep);
        }

        Assert.AreEqual(0, released!.ContactCount);
        Assert.IsTrue(released.ConstraintsReleased);
        Assert.IsLessThan(0.08, released.MaximumNodeSpeed);
        Assert.IsLessThan(0.04, Math.Abs(released.First.Diagnostics.SoftBody!.Centroid.X));
        Assert.IsLessThan(0.04, Math.Abs(released.Second.Diagnostics.SoftBody!.Centroid.X));
    }

    [TestMethod]
    public void FallbackReleasesSharedNativeWorldAndKeepsBothAuthoritativeBounds()
    {
        using var world = new Box2DPairedContactPresentationWorld(Box2DNativeLoader.DefaultLibraryPath);
        var first = CreateInput("paired-a", 200);
        var second = CreateInput("paired-b", 258);
        var cue = new PairedPresentationContactCue([first.DangoId, second.DangoId], new LogicalVector(1, 0), 0.8);
        world.Step(first, second, cue, FixedStep);

        var fallback = world.Step(first, second with { IsHostAvailable = false }, cue, FixedStep);

        Assert.AreEqual(PhysicsPresentationFallbackReason.HostUnavailable, fallback.First.FallbackReason);
        Assert.AreEqual(PhysicsPresentationFallbackReason.HostUnavailable, fallback.Second.FallbackReason);
        Assert.AreEqual(first.AuthoritativeBounds, fallback.FirstAuthoritativeBounds);
        Assert.AreEqual(second.AuthoritativeBounds, fallback.SecondAuthoritativeBounds);
        Assert.IsTrue(fallback.ConstraintsReleased);
    }

    [TestMethod]
    public void PairedProofContractContainsNoAccountFriendRoomOrNetworkSemantics()
    {
        var names = typeof(PairedPhysicsPresentationFrame).Assembly
            .GetTypes()
            .Where(type => type.Name.Contains("Paired", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}"))
            .ToArray();

        Assert.IsFalse(names.Any(name =>
            name.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Friend", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Room", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Network", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void DangoVisualExposesAReusableBodyFillOption()
    {
        var property = typeof(DeformableDangoVisual).GetProperty("BodyFill");

        Assert.IsNotNull(property);
        Assert.AreEqual(typeof(System.Windows.Media.Brush), property.PropertyType);
        Assert.IsTrue(property.CanWrite);
    }

    private static PhysicsPresentationInput CreateInput(string id, double x) =>
        new(
            id,
            new LogicalRect(x, 240, 96, 80),
            DangoActionState.Idle,
            1_000,
            17,
            "paired-proof-ground",
            new LogicalVector(0, -1),
            0,
            1,
            false,
            true,
            []);

    private static PairedPhysicsPresentationFrame Approach(
        Box2DPairedContactPresentationWorld world,
        PhysicsPresentationInput separatedFirst,
        PhysicsPresentationInput separatedSecond,
        PhysicsPresentationInput touchingFirst,
        PhysicsPresentationInput touchingSecond,
        PairedPresentationContactCue cue)
    {
        for (var index = 0; index < 60; index++)
        {
            world.Step(separatedFirst, separatedSecond, null, FixedStep);
        }

        PairedPhysicsPresentationFrame? frame = null;
        for (var index = 0; index < 120; index++)
        {
            var amount = (index + 1d) / 120;
            var firstX = separatedFirst.AuthoritativeBounds.X +
                ((touchingFirst.AuthoritativeBounds.X - separatedFirst.AuthoritativeBounds.X) * amount);
            var secondX = separatedSecond.AuthoritativeBounds.X +
                ((touchingSecond.AuthoritativeBounds.X - separatedSecond.AuthoritativeBounds.X) * amount);
            frame = world.Step(
                CreateInput(touchingFirst.DangoId, firstX),
                CreateInput(touchingSecond.DangoId, secondX),
                cue,
                FixedStep);
        }
        for (var index = 0; index < 150; index++)
        {
            frame = world.Step(touchingFirst, touchingSecond, cue, FixedStep);
        }
        return frame!;
    }

}
