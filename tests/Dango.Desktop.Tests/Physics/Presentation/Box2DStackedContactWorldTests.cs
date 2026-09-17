using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Presentation;

namespace Dango.Desktop.Tests.Physics.Presentation;

[TestClass]
public sealed class Box2DStackedContactWorldTests
{
    private static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60d);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ThreeSoftBodiesShareRealContactsAndPreserveAuthoritativeBounds()
    {
        using var world = new Box2DStackedContactPresentationWorld(Box2DNativeLoader.DefaultLibraryPath);
        var inputs = CreateStableStack();

        var frame = Settle(world, inputs);

        Assert.HasCount(3, frame.Bodies);
        Assert.IsGreaterThan(0, frame.ContactCount, "三个软体必须在同一个 Box2D world 中产生真实 contact。");
        CollectionAssert.AreEqual(inputs.Select(input => input.AuthoritativeBounds).ToArray(), frame.AuthoritativeBounds.ToArray());
        Assert.IsTrue(frame.AdjacentContacts[0], "底层与中层必须真实接触。");
        Assert.IsTrue(frame.AdjacentContacts[1], "中层与顶层必须真实接触。");
    }

    [TestMethod]
    public void ActivePopulationCanChangeFromOneToThreeAndBackWithoutHiddenContacts()
    {
        using var world = new Box2DStackedContactPresentationWorld(
            Box2DNativeLoader.DefaultLibraryPath,
            groundAnchorCenterY: 460);
        var inputs = new[]
        {
            CreatePreviewInput("population-primary", 360, 400),
            CreatePreviewInput("population-middle", 360, 220),
            CreatePreviewInput("population-top", 360, 100),
        };

        var one = world.Step(inputs.Take(1).ToArray(), FixedStep);
        Assert.HasCount(1, one.Bodies);
        Assert.HasCount(0, one.AdjacentContacts);
        Assert.AreEqual(0, one.ContactCount);

        var two = world.Step(inputs.Take(2).ToArray(), FixedStep);
        Assert.HasCount(2, two.Bodies);
        Assert.HasCount(1, two.AdjacentContacts);

        var three = world.Step(inputs, FixedStep);
        Assert.HasCount(3, three.Bodies);
        Assert.HasCount(2, three.AdjacentContacts);

        var removedToOne = world.Step(inputs.Take(1).ToArray(), FixedStep);
        Assert.HasCount(1, removedToOne.Bodies);
        Assert.HasCount(0, removedToOne.AdjacentContacts);
        Assert.AreEqual(0, removedToOne.ContactCount, "禁用的第二、第三只不得继续参与碰撞。");

        var readded = world.Step(inputs.Take(2).ToArray(), FixedStep);
        Assert.HasCount(2, readded.Bodies);
        Assert.IsLessThan(10, Math.Abs(readded.RenderOffsets[1].Y), "重新添加必须从第二只的出生锚点开始。");
    }

    [TestMethod]
    public void BottomCarriesMoreCompressionWhileFarContoursRemainBounded()
    {
        using var world = new Box2DStackedContactPresentationWorld(Box2DNativeLoader.DefaultLibraryPath);
        var separated = CreateSeparatedInputs();
        var baseline = Settle(world, separated);

        world.Reset();
        var stacked = Settle(world, CreateStableStack());

        var bottomCompression = Height(baseline.Bodies[0]) - Height(stacked.Bodies[0]);
        var topCompression = Height(baseline.Bodies[2]) - Height(stacked.Bodies[2]);
        var bottomSideChange = Math.Abs(Width(stacked.Bodies[0]) - Width(baseline.Bodies[0]));

        TestContext.WriteLine(
            $"compression=bottom:{bottomCompression:F6},top:{topCompression:F6}; " +
            $"bottomSideChange={bottomSideChange:F6}; minArea={stacked.Bodies.Min(body => body.Diagnostics.SoftBody!.SignedArea):F6}; " +
            $"offsets={string.Join(';', stacked.RenderOffsets.Select(offset => $"({offset.X:F2},{offset.Y:F2})"))}; " +
            $"contacts={stacked.ContactCount}/{string.Join(',', stacked.AdjacentContacts)}");

        Assert.IsGreaterThan(0.025, bottomCompression, "底层承重必须产生可见但有限的纵向压缩。");
        Assert.IsGreaterThan(topCompression + 0.012, bottomCompression, "底层承重压缩必须大于顶层。");
        Assert.IsLessThan(0.18, bottomSideChange, "非接触侧轮廓不能随堆叠发生严重塌陷。");
        Assert.IsGreaterThan(0.65, stacked.Bodies.Min(body => body.Diagnostics.SoftBody!.SignedArea), "所有轮廓必须保持正向且不能严重塌陷。");
    }

    [TestMethod]
    public void CenteredStackSettlesWithoutUnboundedEnergyOrPersistentJitter()
    {
        using var world = new Box2DStackedContactPresentationWorld(Box2DNativeLoader.DefaultLibraryPath);
        var inputs = CreateStableStack();
        StackedPhysicsPresentationFrame? frame = null;
        StackedPhysicsPresentationFrame? lateReference = null;

        for (var index = 0; index < 420; index++)
        {
            frame = world.Step(inputs, FixedStep);
            Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.Bodies[0].FallbackReason, $"step={index}");
            Assert.IsLessThan(8, frame.MaximumNodeSpeed, "堆叠能量不得无界增长。");
            if (index >= 360)
            {
                lateReference ??= frame;
            }
        }

        Assert.IsNotNull(frame);
        Assert.IsNotNull(lateReference);
        var maximumLateContourDrift = frame.Bodies
            .SelectMany((body, bodyIndex) => body.Contour.Select((point, pointIndex) =>
            {
                var before = lateReference.Bodies[bodyIndex].Contour[pointIndex];
                return Math.Sqrt(Math.Pow(point.X - before.X, 2) + Math.Pow(point.Y - before.Y, 2));
            }))
            .Max();
        Assert.IsLessThan(0.04, maximumLateContourDrift, "稳定保持末段轮廓不得继续发生可见漂移。");
        Assert.IsLessThan(
            0.04,
            Math.Abs(frame.Bodies[1].Diagnostics.SoftBody!.Centroid.X),
            "局部轮廓应围绕自己的权威核心收束。");
        TestContext.WriteLine($"lateContourDrift={maximumLateContourDrift:F6}; maxNodeSpeed={frame.MaximumNodeSpeed:F6}; contacts={frame.ContactCount}");
    }

    [TestMethod]
    public void GravityDropsSpawnedBodiesWithoutChangingCoreAuthority()
    {
        using var world = new Box2DStackedContactPresentationWorld(
            Box2DNativeLoader.DefaultLibraryPath,
            groundAnchorCenterY: 460);
        var inputs = new[]
        {
            CreatePreviewInput("stack-proof-bottom", 360, 400),
            CreatePreviewInput("stack-proof-middle", 360, 220),
            CreatePreviewInput("stack-proof-top", 360, 100),
        };

        StackedPhysicsPresentationFrame? frame = null;
        for (var index = 0; index < 420; index++)
        {
            frame = world.Step(inputs, FixedStep);
            Assert.IsTrue(
                frame.Bodies.All(body => body.FallbackReason == PhysicsPresentationFallbackReason.None),
                $"step={index}; fallback={frame.Bodies[0].FallbackReason}; native={frame.Bodies[0].Diagnostics.NativeErrorCode}");
            Assert.IsLessThan(8, frame.MaximumNodeSpeed);
        }

        Assert.IsNotNull(frame);
        CollectionAssert.AreEqual(inputs.Select(input => input.AuthoritativeBounds).ToArray(), frame.AuthoritativeBounds.ToArray());
        Assert.IsGreaterThan(60, frame.RenderOffsets[1].Y, "中层团子必须在重力下从出生位置明显下落。");
        Assert.IsGreaterThan(100, frame.RenderOffsets[2].Y, "顶层团子必须在重力下从出生位置明显下落。");
        Assert.IsTrue(frame.AdjacentContacts.Any(value => value), "下落后必须形成真实团子接触。");
    }

    [TestMethod]
    public void FreeFallAcceleratesDownwardBeforeGroundContact()
    {
        using var world = new Box2DStackedContactPresentationWorld(
            Box2DNativeLoader.DefaultLibraryPath,
            groundAnchorCenterY: 460);
        var inputs = new[]
        {
            CreatePreviewInput("gravity-a", 180, 400),
            CreatePreviewInput("gravity-b", 360, 100),
            CreatePreviewInput("gravity-c", 540, 100),
        };

        var samples = new List<double>();
        for (var index = 0; index <= 40; index++)
        {
            var frame = world.Step(inputs, FixedStep);
            if (index is 0 or 20 or 40)
            {
                samples.Add(frame.RenderOffsets[1].Y);
            }
        }

        Assert.IsGreaterThan(samples[0] + 5, samples[1]);
        Assert.IsGreaterThan(samples[1] + 10, samples[2]);
    }

    [TestMethod]
    public void OffCenterBodyCanSlideOffAndFallInsteadOfRemainingPinned()
    {
        using var world = new Box2DStackedContactPresentationWorld(
            Box2DNativeLoader.DefaultLibraryPath,
            groundAnchorCenterY: 460);
        var inputs = new[]
        {
            CreatePreviewInput("slide-bottom", 360, 400),
            CreatePreviewInput("slide-middle", 430, 220),
            CreatePreviewInput("slide-observer", 650, 400),
        };
        StackedPhysicsPresentationFrame? frame = null;
        var maximumMiddleHorizontalOffset = 0d;
        var contactWasObserved = false;
        var consecutiveSeparatedFramesAfterContact = 0;
        var maximumConsecutiveSeparatedFramesAfterContact = 0;
        for (var index = 0; index < 480; index++)
        {
            frame = world.Step(inputs, FixedStep);
            maximumMiddleHorizontalOffset = Math.Max(
                maximumMiddleHorizontalOffset,
                Math.Abs(frame.RenderOffsets[1].X));
            contactWasObserved |= frame.AdjacentContacts[0];
            if (contactWasObserved && !frame.AdjacentContacts[0])
            {
                consecutiveSeparatedFramesAfterContact++;
                maximumConsecutiveSeparatedFramesAfterContact = Math.Max(
                    maximumConsecutiveSeparatedFramesAfterContact,
                    consecutiveSeparatedFramesAfterContact);
            }
            else
            {
                consecutiveSeparatedFramesAfterContact = 0;
            }
        }

        Assert.IsNotNull(frame);
        TestContext.WriteLine(
            $"slideMaxX={maximumMiddleHorizontalOffset:F3}; finalOffset={frame.RenderOffsets[1]}; " +
            $"separatedFrames={maximumConsecutiveSeparatedFramesAfterContact}; contacts={frame.ContactCount}/{string.Join(',', frame.AdjacentContacts)}");
        Assert.IsTrue(contactWasObserved, "偏心团子必须先与下层发生真实接触，测试才能验证接触后的滑落与分离。");
        Assert.IsGreaterThan(5, maximumMiddleHorizontalOffset, "偏心团子的下落轨迹必须包含可见的侧向滑动，不能垂直穿过支撑团子。");
        Assert.IsGreaterThan(150, frame.RenderOffsets[1].Y, "滑落的团子必须继续受重力影响并明显落向地面。");
        Assert.IsTrue(frame.Bodies.All(body => body.FallbackReason == PhysicsPresentationFallbackReason.None));
    }

    [TestMethod]
    public void StackedInterBodyContactRestoresPairedMaterialAndCollisionRadius()
    {
        var sourcePath = Path.Combine(
            TestRepository.Root,
            "eng",
            "native",
            "box2d",
            "dango_box2d_bridge.c");
        var source = File.ReadAllText(sourcePath);
        var start = source.IndexOf("static int dango_create_stacked_ring", StringComparison.Ordinal);
        var end = source.IndexOf("DANGO_API int dango_box2d_stacked_soft_body_create_configured", start, StringComparison.Ordinal);
        var stackedRing = source[start..end];

        StringAssert.Contains(stackedRing, "proxy_shape_def.material.friction = 0.0f;", "内部防穿透代理不得制造表面粘连。");
        StringAssert.Contains(stackedRing, "node_shape_def.material.friction = stack->config.friction;");
        StringAssert.Contains(stackedRing, "stack->config.node_radius * 0.10f");
        Assert.IsFalse(stackedRing.Contains("0.02f", StringComparison.Ordinal), "不得保留 P2D-007A 的 0.02 实验摩擦。");
    }

    [TestMethod]
    public void CoreAuthoritativeMovementMovesOnlyTheControlledBodyAndCanDestabilizeTheStack()
    {
        using var world = new Box2DStackedContactPresentationWorld(
            Box2DNativeLoader.DefaultLibraryPath,
            groundAnchorCenterX: 440,
            groundAnchorCenterY: 460);
        var inputs = new[]
        {
            CreatePreviewInput("controlled-bottom", 360, 400),
            CreatePreviewInput("stack-middle", 360, 220),
            CreatePreviewInput("stack-top", 360, 100),
        };

        for (var index = 0; index < 360; index++)
        {
            world.Step(inputs, FixedStep);
        }
        StackedPhysicsPresentationFrame? frame = null;
        var initialControlledX = inputs[0].AuthoritativeBounds.X;
        var maximumControlledDisplacement = 0d;
        var maximumUpperDisplacement = 0d;
        var contactWasLost = false;
        var firstContactLossStep = -1;
        var consecutiveSeparatedFrames = 0;
        var maximumConsecutiveSeparatedFrames = 0;
        for (var index = 0; index < 240; index++)
        {
            inputs[0] = WithBounds(
                inputs[0],
                inputs[0].AuthoritativeBounds.Translate(new LogicalVector(55d / 60d, 0)));
            frame = world.Step(inputs, FixedStep);
            maximumControlledDisplacement = Math.Max(
                maximumControlledDisplacement,
                inputs[0].AuthoritativeBounds.X + frame.RenderOffsets[0].X - initialControlledX);
            maximumUpperDisplacement = Math.Max(
                maximumUpperDisplacement,
                Math.Max(Math.Abs(frame.RenderOffsets[1].X), Math.Abs(frame.RenderOffsets[2].X)));
            contactWasLost |= !frame.AdjacentContacts.All(value => value);
            if (!frame.AdjacentContacts[0])
            {
                firstContactLossStep = firstContactLossStep < 0 ? index : firstContactLossStep;
                consecutiveSeparatedFrames++;
                maximumConsecutiveSeparatedFrames = Math.Max(maximumConsecutiveSeparatedFrames, consecutiveSeparatedFrames);
            }
            else
            {
                consecutiveSeparatedFrames = 0;
            }
        }
        for (var index = 0; index < 240; index++)
        {
            frame = world.Step(inputs, FixedStep);
            maximumUpperDisplacement = Math.Max(
                maximumUpperDisplacement,
                Math.Max(Math.Abs(frame.RenderOffsets[1].X), Math.Abs(frame.RenderOffsets[2].X)));
            contactWasLost |= !frame.AdjacentContacts.All(value => value);
            if (!frame.AdjacentContacts[0])
            {
                consecutiveSeparatedFrames++;
                maximumConsecutiveSeparatedFrames = Math.Max(maximumConsecutiveSeparatedFrames, consecutiveSeparatedFrames);
            }
            else
            {
                consecutiveSeparatedFrames = 0;
            }
        }

        Assert.IsNotNull(frame);
        TestContext.WriteLine(
            $"controlledMaxX={maximumControlledDisplacement:F3}; upperMaxX={maximumUpperDisplacement:F3}; " +
            $"contactLost={contactWasLost}; firstLoss={firstContactLossStep}; separatedFrames={maximumConsecutiveSeparatedFrames}");
        Assert.IsGreaterThan(20, maximumControlledDisplacement, "D 键控制必须让第一只团子的表现中心向右移动。");
        Assert.IsGreaterThanOrEqualTo(0, firstContactLossStep, "移开底层团子后必须解除与中层的接触。");
        Assert.IsLessThanOrEqualTo(240, firstContactLossStep, "按 P2D-006 正式速度持续移开后必须在四秒内解除接触，不能永久粘连上层。");
        Assert.IsGreaterThanOrEqualTo(30, maximumConsecutiveSeparatedFrames, "解除接触后必须持续分离至少半秒，不能立即重新粘连。");
        CollectionAssert.AreEqual(inputs.Select(input => input.AuthoritativeBounds).ToArray(), frame.AuthoritativeBounds.ToArray());
    }

    [TestMethod]
    public void CoreAuthoritativeJumpMovesTheControlledBodyWithoutPhysicsWriteback()
    {
        using var world = new Box2DStackedContactPresentationWorld(
            Box2DNativeLoader.DefaultLibraryPath,
            groundAnchorCenterX: 260,
            groundAnchorCenterY: 460);
        var inputs = new[]
        {
            CreatePreviewInput("controlled", 180, 400),
            CreatePreviewInput("other-a", 360, 400),
            CreatePreviewInput("other-b", 540, 400),
        };
        var settled = Settle(world, inputs);
        var minimumY = settled.RenderOffsets[0].Y;

        for (var index = 0; index < 90; index++)
        {
            inputs[0] = WithBounds(
                inputs[0],
                inputs[0].AuthoritativeBounds.Translate(new LogicalVector(0, -48d / 45d)));
            var frame = world.Step(inputs, FixedStep);
            minimumY = Math.Min(minimumY, inputs[0].AuthoritativeBounds.Y + frame.RenderOffsets[0].Y);
        }

        Assert.IsLessThan(inputs[0].AuthoritativeBounds.Y + 1, minimumY + 96, "正式跳跃由 Core 权威边界驱动，不得依赖二维物理力。");
    }

    [TestMethod]
    public void ResetFallbackAndDisposeReleaseTheSharedWorld()
    {
        using var world = new Box2DStackedContactPresentationWorld(Box2DNativeLoader.DefaultLibraryPath);
        var inputs = CreateStableStack();
        Settle(world, inputs);

        world.Reset();
        var resetFrame = world.Step(CreateSeparatedInputs(), FixedStep);
        Assert.AreEqual(0, resetFrame.ContactCount);

        var fallbackInputs = inputs.ToArray();
        fallbackInputs[1] = fallbackInputs[1] with { IsHostAvailable = false };
        var fallback = world.Step(fallbackInputs, FixedStep);
        Assert.IsTrue(fallback.ConstraintsReleased);
        Assert.IsTrue(fallback.Bodies.All(body => body.FallbackReason == PhysicsPresentationFallbackReason.HostUnavailable));
        CollectionAssert.AreEqual(fallbackInputs.Select(input => input.AuthoritativeBounds).ToArray(), fallback.AuthoritativeBounds.ToArray());
    }

    [TestMethod]
    public void ExistingPairedContactProofRemainsAvailable()
    {
        using var paired = new Box2DPairedContactPresentationWorld(Box2DNativeLoader.DefaultLibraryPath);
        var first = CreateInput("paired-regression-a", 200, 260);
        var second = CreateInput("paired-regression-b", 350, 260);

        var frame = paired.Step(first, second, null, FixedStep);

        Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.First.FallbackReason);
        Assert.AreEqual(first.AuthoritativeBounds, frame.FirstAuthoritativeBounds);
        Assert.AreEqual(second.AuthoritativeBounds, frame.SecondAuthoritativeBounds);
    }

    [TestMethod]
    public void StackedProofContractContainsNoAccountFriendRoomOrNetworkSemantics()
    {
        var names = typeof(StackedPhysicsPresentationFrame).Assembly
            .GetTypes()
            .Where(type => type.Name.Contains("Stacked", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}"))
            .ToArray();

        Assert.IsFalse(names.Any(name =>
            name.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Friend", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Room", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Network", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void VerticalAnchorsUseTheRenderedBodyHeightScale()
    {
        var inputs = new[]
        {
            CreatePreviewInput("stack-bottom", 360, 400),
            CreatePreviewInput("stack-middle", 367, 340),
            CreatePreviewInput("stack-top", 355, 280),
        };

        var anchors = StackedPresentationAnchorMapper.Map(inputs, referenceCenterX: 440, groundAnchorCenterY: 460);

        Assert.AreEqual(0, anchors[1], 0.0001);
        Assert.AreEqual(1, anchors[3], 0.0001, "60 DIPs 的纵向间距应按 120 DIPs 控件高度映射为 1 个物理单位。");
        Assert.AreEqual(2, anchors[5], 0.0001);
    }

    [TestMethod]
    public void StablePreviewHasBodyToBodyVisualContactWithoutAccessorySupport()
    {
        using var world = new Box2DStackedContactPresentationWorld(
            Box2DNativeLoader.DefaultLibraryPath,
            groundAnchorCenterY: 460);
        var inputs = new[]
        {
            CreatePreviewInput("stack-proof-bottom", 360, 400),
            CreatePreviewInput("stack-proof-middle", 360, 220),
            CreatePreviewInput("stack-proof-top", 360, 100),
        };

        var frame = Settle(world, inputs);
        var bottomMiddleGap = ScreenGap(inputs[1], frame.Bodies[1], frame.RenderOffsets[1], inputs[0], frame.Bodies[0], frame.RenderOffsets[0]);
        var middleTopGap = ScreenGap(inputs[2], frame.Bodies[2], frame.RenderOffsets[2], inputs[1], frame.Bodies[1], frame.RenderOffsets[1]);

        TestContext.WriteLine($"bodyGaps=bottom/middle:{bottomMiddleGap:F3},middle/top:{middleTopGap:F3}");
        Assert.IsTrue(frame.AdjacentContacts.All(value => value));
        Assert.IsLessThanOrEqualTo(1, bottomMiddleGap, "真实 contact 时画面不得在身体之间留下由帽子填充的缝隙。");
        Assert.IsLessThanOrEqualTo(1, middleTopGap, "真实 contact 时画面不得在身体之间留下由帽子填充的缝隙。");
        Assert.IsGreaterThanOrEqualTo(-8, bottomMiddleGap, "身体轮廓不能为遮住帽子而严重穿模。");
        Assert.IsGreaterThanOrEqualTo(-8, middleTopGap, "身体轮廓不能为遮住帽子而严重穿模。");
    }

    [TestMethod]
    public void StackedNativeWorldContainsNoHatBodyOrHatCollisionShape()
    {
        var sourcePath = Path.Combine(
            TestRepository.Root,
            "eng",
            "native",
            "box2d",
            "dango_box2d_bridge.c");
        var source = File.ReadAllText(sourcePath);
        var start = source.IndexOf("typedef struct dango_stacked_soft_bodies", StringComparison.Ordinal);
        var end = source.IndexOf("dango_box2d_stacked_soft_body_destroy", start, StringComparison.Ordinal);
        var stackedSection = source[start..end];

        Assert.IsFalse(stackedSection.Contains("hat_body", StringComparison.Ordinal));
        Assert.IsFalse(stackedSection.Contains("hat_shape", StringComparison.Ordinal));
        Assert.IsFalse(stackedSection.Contains("hat_joint", StringComparison.Ordinal));
    }

    private static StackedPhysicsPresentationFrame Settle(
        Box2DStackedContactPresentationWorld world,
        IReadOnlyList<PhysicsPresentationInput> inputs)
    {
        StackedPhysicsPresentationFrame? frame = null;
        for (var index = 0; index < 300; index++)
        {
            frame = world.Step(inputs, FixedStep);
        }
        return frame!;
    }

    private static PhysicsPresentationInput[] CreateStableStack() =>
    [
        CreateInput("stack-bottom", 310, 300),
        CreateInput("stack-middle", 310, 200),
        CreateInput("stack-top", 310, 100),
    ];

    private static PhysicsPresentationInput[] CreateSeparatedInputs() =>
    [
        CreateInput("stack-bottom", 180, 300),
        CreateInput("stack-middle", 300, 300),
        CreateInput("stack-top", 420, 300),
    ];

    private static PhysicsPresentationInput CreateInput(string id, double x, double y) =>
        new(
            id,
            new LogicalRect(x, y, 96, 80),
            DangoActionState.Idle,
            1_000,
            17,
            "stack-proof-ground",
            new LogicalVector(0, -1),
            0,
            1,
            false,
            true,
            []);

    private static PhysicsPresentationInput CreatePreviewInput(string id, double x, double y) =>
        new(
            id,
            new LogicalRect(x, y, 160, 120),
            DangoActionState.Idle,
            1_000,
            17,
            "stack-proof-ground",
            new LogicalVector(0, -1),
            0,
            1,
            false,
            true,
            []);

    private static PhysicsPresentationInput WithBounds(PhysicsPresentationInput input, LogicalRect bounds) =>
        new(
            input.DangoId,
            bounds,
            input.Action,
            input.AuthoritativeStartTimeMilliseconds,
            input.DeterministicSeed,
            input.SemanticAnchor,
            input.ContactNormal,
            input.Strength,
            input.ParameterVersion,
            input.ReducedMotion,
            input.IsHostAvailable,
            input.ParticipantIds,
            input.SquashStrength,
            input.GroundFrictionCue,
            input.FacingDirection,
            input.DragGrabPoint);

    private static double Width(PhysicsPresentationFrame frame) =>
        frame.Contour.Max(point => point.X) - frame.Contour.Min(point => point.X);

    private static double Height(PhysicsPresentationFrame frame) =>
        frame.Contour.Max(point => point.Y) - frame.Contour.Min(point => point.Y);

    private static double ScreenGap(
        PhysicsPresentationInput upperInput,
        PhysicsPresentationFrame upperFrame,
        LogicalVector upperOffset,
        PhysicsPresentationInput lowerInput,
        PhysicsPresentationFrame lowerFrame,
        LogicalVector lowerOffset)
    {
        var upperBottom = upperInput.AuthoritativeBounds.Y + upperOffset.Y +
            ((1 - upperFrame.Contour.Min(point => point.Y)) * upperInput.AuthoritativeBounds.Height / 2);
        var lowerTop = lowerInput.AuthoritativeBounds.Y + lowerOffset.Y +
            ((1 - lowerFrame.Contour.Max(point => point.Y)) * lowerInput.AuthoritativeBounds.Height / 2);
        return lowerTop - upperBottom;
    }
}
