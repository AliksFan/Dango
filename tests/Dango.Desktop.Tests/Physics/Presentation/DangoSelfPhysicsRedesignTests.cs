using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;

namespace Dango.Desktop.Tests.Physics.Presentation;

[TestClass]
public sealed class DangoSelfPhysicsRedesignTests
{
    private static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60d);

    [TestMethod]
    [DataRow(0.5d)]
    [DataRow(1d)]
    public void RepeatedChangingDragTargetsDoNotLatch(double strength)
    {
        using var world = CreateWorld(DangoSelfPhysicsParameters.CreateDefault() with { TopInwardForce = strength });
        StackedPhysicsPresentationFrame? last = null;
        for (var step = 0; step < 6000; step++)
        {
            var phase = step % 600;
            var active = step >= 600 && phase < 480;
            var target = new LogicalPoint(180 + 300 * Math.Sin(step * 0.043), 260 + 250 * Math.Cos(step * 0.071));
            var frame = world.Step(CreateInput(), FixedStep, new(0, 0, false, active, target));
            Assert.IsFalse(world.IsRestartBlocked, $"strength={strength}, step={step}, error={world.LastNativeErrorCode}");
            Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.Bodies[0].FallbackReason);
            Assert.IsLessThanOrEqualTo(12, frame.MaximumNodeSpeed);
            var angles = InteriorAnglesDegrees(frame.Bodies[0].Contour);
            Assert.IsGreaterThanOrEqualTo(90, angles.Min());
            Assert.IsLessThanOrEqualTo(180, angles.Max());
            Assert.AreEqual(0, CountNonAdjacentEdgeCrossings(frame.Bodies[0].Contour));
            last = frame;
        }
        for (var step = 0; step < 600; step++)
        {
            last = world.Step(CreateInput(), FixedStep, DangoSelfPhysicsControlState.None);
            Assert.IsFalse(world.IsRestartBlocked);
        }
        var before = last!.RenderOffsets[0].X;
        for (var step = 0; step < 60; step++)
        {
            last = world.Step(CreateInput(), FixedStep, new(1, 0, false, false, default));
            Assert.IsFalse(world.IsRestartBlocked);
        }
        Assert.IsGreaterThan(before + 3, last.RenderOffsets[0].X, "甩动结束后仍须响应方向力，不能以冻结伪装稳定。");
    }

    [TestMethod]
    public void FastDragFailureIsFiniteVelocityExceedingTheSafetyThreshold()
    {
        using var world = CreateWorld(DangoSelfPhysicsParameters.CreateDefault() with { MaximumSpeed = 30 });
        var maximum = 0d;
        for (var step = 0; step <= 986; step++)
        {
            var phase = step % 600;
            var active = step >= 600 && phase < 480;
            var target = new LogicalPoint(180 + 300 * Math.Sin(step * 0.043), 260 + 250 * Math.Cos(step * 0.071));
            var frame = world.Step(CreateInput(), FixedStep, new(0, 0, false, active, target));
            Assert.IsFalse(world.IsRestartBlocked, $"step={step}, error={world.LastNativeErrorCode}");
            Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.Bodies[0].FallbackReason);
            maximum = Math.Max(maximum, frame.MaximumNodeSpeed);
        }
        Console.WriteLine($"Peak node speed at original failure: {maximum:F6}");
        Assert.IsTrue(double.IsFinite(maximum));
        Assert.IsGreaterThan(12, maximum);
    }

    [TestMethod]
    public void TopControlsAreReplacedByOneLiveInwardForce()
    {
        var type = typeof(DangoSelfPhysicsParameters);
        Assert.IsNull(type.GetProperty("TopSoftness"));
        Assert.IsNull(type.GetProperty("CrownRoundness"));
        Assert.IsNotNull(type.GetProperty("TopInwardForce"));
        var parameters = DangoSelfPhysicsParameters.CreateDefault();
        var disabled = parameters.WithNumericValue("TopInwardForce", 0);
        Assert.IsFalse(disabled.RequiresRebuildComparedWith(parameters));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => parameters.WithNumericValue("TopInwardForce", -0.01));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => parameters.WithNumericValue("TopInwardForce", 1.01));
    }

    [TestMethod]
    public void DefaultsPreserveTheUserApprovedParameterSnapshot()
    {
        var parameters = DangoSelfPhysicsParameters.CreateDefault();
        var expected = new Dictionary<string, double>
        {
            [nameof(parameters.RadialRestLength)] = 0.68,
            [nameof(parameters.RadialMinimumRatio)] = 0.65,
            [nameof(parameters.RadialMaximumRatio)] = 1.22,
            [nameof(parameters.PerimeterMinimumRatio)] = 0.73,
            [nameof(parameters.PerimeterMaximumRatio)] = 1.07,
            [nameof(parameters.RadialElasticity)] = 5.2,
            [nameof(parameters.PerimeterElasticity)] = 20.1,
            [nameof(parameters.RadialDamping)] = 1.85,
            [nameof(parameters.PerimeterDamping)] = 1.85,
            [nameof(parameters.AreaPreservationStrength)] = 4.8,
            [nameof(parameters.BoneRepulsionDistance)] = 0.09,
            [nameof(parameters.BoneRepulsionStrength)] = 0.3,
            [nameof(parameters.BoneRepulsionMaximumForce)] = 3.5,
            [nameof(parameters.CoreExclusionRatio)] = 0.25,
            [nameof(parameters.PerimeterAngleStrength)] = 10,
            [nameof(parameters.LocalAngleBalanceStrength)] = 1.7,
        };
        foreach (var (name, value) in expected)
        {
            Assert.AreEqual(value, (double)typeof(DangoSelfPhysicsParameters).GetProperty(name)!.GetValue(parameters)!, name);
        }
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(0.5d)]
    [DataRow(1d)]
    public void HardAnglesSurviveControlsEvenWithSoftBarrierDisabled(double inwardForce)
    {
        using var world = CreateWorld(DangoSelfPhysicsParameters.CreateDefault() with
        {
            TopInwardForce = inwardForce,
            PerimeterAngleStrength = 0,
            DragStiffness = 40,
            DragMaximumForce = 30,
        });
        for (var index = 0; index < 1200; index++)
        {
            var control = index switch
            {
                >= 300 and < 420 => new DangoSelfPhysicsControlState(0, 0, false, true, new(360, 460)),
                >= 600 and < 660 => new DangoSelfPhysicsControlState(1, 0, false, false, default),
                >= 660 and < 720 => new DangoSelfPhysicsControlState(-1, 0, false, false, default),
                >= 720 and < 780 => new DangoSelfPhysicsControlState(0, 1, false, false, default),
                840 => new DangoSelfPhysicsControlState(0, 0, true, false, default),
                _ => DangoSelfPhysicsControlState.None,
            };
            var frame = world.Step(CreateInput(), FixedStep, control);
            Assert.IsFalse(world.IsRestartBlocked, $"inwardForce={inwardForce}, step={index}");
            Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.Bodies[0].FallbackReason);
            var angles = InteriorAnglesDegrees(frame.Bodies[0].Contour);
            Assert.IsGreaterThanOrEqualTo(90, angles.Min(), $"step={index}");
            Assert.IsLessThanOrEqualTo(180, angles.Max(), $"step={index}");
            Assert.AreEqual(0, CountNonAdjacentEdgeCrossings(frame.Bodies[0].Contour), $"step={index}");
        }
    }

    [TestMethod]
    public void SettledCrownSpreadsCurvatureWhileKeepingAnArch()
    {
        using var world = CreateWorld(DangoSelfPhysicsParameters.CreateDefault() with { TopInwardForce = 0.5 });
        StackedPhysicsPresentationFrame? frame = null;
        for (var step = 0; step < 900; step++)
        {
            frame = world.Step(CreateInput(), FixedStep, DangoSelfPhysicsControlState.None);
            Assert.IsFalse(world.IsRestartBlocked, $"step={step}");
            Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.Bodies[0].FallbackReason);
            if (step >= 480)
            {
                var sample = CrownMetrics(frame);
                Assert.IsLessThanOrEqualTo(5, sample.Gap, $"step={step}");
                Assert.IsGreaterThan(0.01, sample.Rise, $"step={step}");
            }
        }
        var points = frame!.Bodies[0].Contour;
        var peak = Enumerable.Range(0, points.Count).MaxBy(i => points[i].Y);
        var angles = InteriorAnglesDegrees(points);
        var previous = (peak + 15) % 16;
        var next = (peak + 1) % 16;
        var peakTurn = 180 - angles[peak];
        var neighborTurn = 180 - (angles[previous] + angles[next]) / 2;
        var rise = points[peak].Y - (points[previous].Y + points[next].Y) / 2;
        Console.WriteLine($"peak={peak}, turn={peakTurn:F3}, neighborTurn={neighborTurn:F3}, rise={rise:F5}");
        for (var i = 0; i < points.Count; i++)
        {
            Console.WriteLine($"node={i}, x={points[i].X:F5}, y={points[i].Y:F5}, angle={angles[i]:F3}");
        }
        Assert.IsLessThanOrEqualTo(neighborTurn + 5, peakTurn, "顶部曲率不能明显集中于单个节点。");
        Assert.IsGreaterThan(0.01, rise, "保留轻微圆拱，不压成平顶。");
    }

    private static (double Gap, double Rise) CrownMetrics(StackedPhysicsPresentationFrame frame)
    {
        var points = frame.Bodies[0].Contour;
        var peak = Enumerable.Range(0, points.Count).MaxBy(i => points[i].Y);
        var previous = (peak + 15) % 16;
        var next = (peak + 1) % 16;
        var angles = InteriorAnglesDegrees(points);
        return ((angles[previous] + angles[next]) / 2 - angles[peak],
            points[peak].Y - (points[previous].Y + points[next].Y) / 2);
    }

    [TestMethod]
    public void TopInwardForceCanBeAdjustedWithoutRebuilding()
    {
        var parameters = DangoSelfPhysicsParameters.CreateDefault();
        using var world = CreateWorld(parameters with { TopInwardForce = 0 });
        StackedPhysicsPresentationFrame? frame = null;
        for (var i = 0; i < 600; i++)
        {
            frame = world.Step(CreateInput(), FixedStep, DangoSelfPhysicsControlState.None);
            Assert.IsFalse(world.IsRestartBlocked);
            Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.Bodies[0].FallbackReason);
        }
        var before = frame!.Bodies[0].Contour.Max(point => point.Y);
        world.ApplyParameters(parameters);
        for (var i = 0; i < 600; i++)
        {
            frame = world.Step(CreateInput(), FixedStep, DangoSelfPhysicsControlState.None);
            Assert.IsFalse(world.IsRestartBlocked);
            Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.Bodies[0].FallbackReason);
        }
        var after = frame!.Bodies[0].Contour.Max(point => point.Y);
        Assert.IsLessThan(before - 0.005, after);
    }

    [TestMethod]
    public void TopInwardForceLowersTheSettledCrownWithoutFreezing()
    {
        double Crown(double softness)
        {
            var parameters = DangoSelfPhysicsParameters.CreateDefault().WithNumericValue("TopInwardForce", softness);
            using var world = CreateWorld(parameters);
            StackedPhysicsPresentationFrame? frame = null;
            for (var step = 0; step < 600; step++)
            {
                frame = world.Step(CreateInput(), FixedStep, DangoSelfPhysicsControlState.None);
                Assert.IsFalse(world.IsRestartBlocked, $"step={step}");
                Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.Bodies[0].FallbackReason);
            }
            return frame!.Bodies[0].Contour.Max(point => point.Y);
        }

        var rigid = Crown(0);
        var soft = Crown(0.5);
        Assert.IsLessThan(rigid - 0.005, soft, $"关闭={rigid:F4}, 开启={soft:F4}");
    }

    [TestMethod]
    public void DefaultStructureUsesSixteenNodesAndTwoEqualLengthGroups()
    {
        var parameters = DangoSelfPhysicsParameters.CreateDefault();
        parameters.Validate();

        Assert.AreEqual(16, parameters.NodeCount);
        Assert.IsGreaterThan(0, parameters.RadialRestLength);
        Assert.IsNull(
            typeof(DangoSelfPhysicsParameters).GetProperty("PerimeterLengthRatio"),
            "外围静止长度必须由团子大小和 16 边形弦长唯一确定。");

        using var world = CreateWorld(parameters);
        var frame = world.Step(CreateInput(), FixedStep, DangoSelfPhysicsControlState.None);
        var contour = frame.Bodies[0].Contour;
        var radialLengths = contour.Select(point => Math.Sqrt((point.X * point.X) + (point.Y * point.Y))).ToArray();
        var perimeterLengths = contour.Select((point, index) => Distance(point, contour[(index + 1) % contour.Count])).ToArray();

        Assert.HasCount(16, contour);
        Assert.IsLessThan(0.03, radialLengths.Max() - radialLengths.Min());
        Assert.IsLessThan(0.03, perimeterLengths.Max() - perimeterLengths.Min());
    }

    [TestMethod]
    public void GroundedBodySettlesLowerWiderThanUpperWithoutCrossingEdges()
    {
        using var world = CreateWorld(DangoSelfPhysicsParameters.CreateDefault());
        StackedPhysicsPresentationFrame? frame = null;
        for (var index = 0; index < 600; index++)
        {
            frame = world.Step(CreateInput(), FixedStep, DangoSelfPhysicsControlState.None);
            Assert.IsFalse(world.IsRestartBlocked, $"settle step={index}");
            Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.Bodies[0].FallbackReason, $"step={index}");
        }

        Assert.IsNotNull(frame);
        var contour = frame.Bodies[0].Contour;
        var lowerWidth = WidthAtHalf(contour, lower: true);
        var upperWidth = WidthAtHalf(contour, lower: false);
        Assert.IsGreaterThan(upperWidth + 0.04, lowerWidth, "落地稳定后必须由物理产生下宽上窄。");
        Assert.AreEqual(0, CountNonAdjacentEdgeCrossings(contour), "外围骨架不得形成 Z 字形自交。");
        Assert.IsGreaterThanOrEqualTo(88, MinimumInteriorAngleDegrees(contour), "90 度硬下限只允许求解容差。");
        Assert.IsLessThanOrEqualTo(180, MaximumSignedInteriorAngleDegrees(contour), "外围骨架不得产生超过 180 度的内凹反角。");
    }

    [TestMethod]
    public void NativeSolverEnforcesTheMaximumPerimeterAngle()
    {
        var path = Path.Combine(
            TestRepository.Root,
            "eng",
            "native",
            "box2d",
            "dango_box2d_bridge.c");
        var source = File.ReadAllText(path);

        StringAssert.Contains(source, "dango_self_apply_convexity_barrier");
        StringAssert.Contains(source, "activation_threshold = 0.12f");
    }

    [TestMethod]
    public void ControlCenterExposesOneSizeInsteadOfTwoIndependentLengths()
    {
        var path = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "AppShell",
            "ControlCenter",
            "ControlCenterWindow.xaml.cs");
        var source = File.ReadAllText(path);

        StringAssert.Contains(source, "团子大小");
        Assert.IsFalse(source.Contains("外围长度比例", StringComparison.Ordinal));
    }

    [TestMethod]
    public void StrongDragDoesNotCreateAnInteriorAngleAboveOneHundredEightyDegrees()
    {
        var parameters = DangoSelfPhysicsParameters.CreateDefault() with
        {
            DragStiffness = 40,
            DragMaximumForce = 30,
        };
        using var world = CreateWorld(parameters);
        var input = CreateInput();
        for (var index = 0; index < 300; index++)
        {
            world.Step(input, FixedStep, DangoSelfPhysicsControlState.None);
            Assert.IsFalse(world.IsRestartBlocked, $"before drag step={index}");
        }

        for (var index = 0; index < 120; index++)
        {
            var frame = world.Step(
                input,
                FixedStep,
                new DangoSelfPhysicsControlState(0, 0, false, true, new LogicalPoint(360, 460)));
            Assert.IsFalse(world.IsRestartBlocked, $"drag step={index}");
            Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.Bodies[0].FallbackReason, $"drag step={index}");
            Assert.IsLessThanOrEqualTo(
                180,
                MaximumSignedInteriorAngleDegrees(frame.Bodies[0].Contour),
                $"drag step={index}");
        }
    }

    [TestMethod]
    public void RepeatedSizeChangesStayInsidePresentationCoordinateBounds()
    {
        using var world = CreateWorld(DangoSelfPhysicsParameters.CreateDefault());
        var input = CreateInput();
        var sizes = new[] { 0.45, 0.90, 0.62, 0.84, 0.68 };

        foreach (var size in sizes)
        {
            world.ApplyParameters(world.Parameters with { RadialRestLength = size });
            for (var index = 0; index < 120; index++)
            {
                var frame = world.Step(input, FixedStep, DangoSelfPhysicsControlState.None);
                Assert.IsFalse(world.IsRestartBlocked, $"size={size}, step={index}");
                var angles = InteriorAnglesDegrees(frame.Bodies[0].Contour);
                Assert.IsGreaterThanOrEqualTo(90, angles.Min());
                Assert.IsLessThanOrEqualTo(180, angles.Max());
                Assert.IsTrue(frame.Bodies[0].Contour.All(point =>
                    point.X is >= -1 and <= 1 && point.Y is >= -1 and <= 1));
            }
        }
    }

    [TestMethod]
    public void LocalAngleBalanceLimitsAdjacentCurvatureJumpsDuringStrongDrag()
    {
        Assert.IsNotNull(typeof(DangoSelfPhysicsParameters).GetProperty("LocalAngleBalanceStrength"));
        var defaults = DangoSelfPhysicsParameters.CreateDefault();
        var withoutBalance = MeasureStrongDragAngleJump(defaults with { LocalAngleBalanceStrength = 0 });
        var withBalance = MeasureStrongDragAngleJump(defaults);

        Assert.IsLessThanOrEqualTo(35, withBalance, "均衡后的局部角度跳变必须受控。");
        Assert.IsLessThan(
            withoutBalance - 2,
            withBalance,
            $"均衡力必须产生可测改善；关闭={withoutBalance:F2}°，开启={withBalance:F2}°。");
    }

    private static double MeasureStrongDragAngleJump(DangoSelfPhysicsParameters parameters)
    {
        using var world = CreateWorld(parameters);
        var input = CreateInput();
        for (var index = 0; index < 300; index++)
        {
            world.Step(input, FixedStep, DangoSelfPhysicsControlState.None);
            Assert.IsFalse(world.IsRestartBlocked, $"angle balance settle step={index}");
        }

        StackedPhysicsPresentationFrame? frame = null;
        for (var index = 0; index < 90; index++)
        {
            frame = world.Step(
                input,
                FixedStep,
                new DangoSelfPhysicsControlState(0, 0, false, true, new LogicalPoint(360, 460)));
            Assert.IsFalse(world.IsRestartBlocked, $"angle balance drag step={index}");
        }

        Assert.IsNotNull(frame);
        return MaximumAdjacentInteriorAngleDifferenceDegrees(frame.Bodies[0].Contour);
    }

    [TestMethod]
    public void ControlCenterRestoresAdvancedParametersInsideCollapsibleGroups()
    {
        var path = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "AppShell",
            "ControlCenter",
            "ControlCenterWindow.xaml.cs");
        var source = File.ReadAllText(path);

        foreach (var restoredLabel in new[]
        {
            "径向压缩下限",
            "径向拉伸上限",
            "外围压缩下限",
            "外围拉伸上限",
            "径向阻尼",
            "外围阻尼",
            "面积保持",
            "落地形变力",
        })
        {
            StringAssert.Contains(source, restoredLabel);
        }
        StringAssert.Contains(source, "局部角度均衡力");
        StringAssert.Contains(source, "顶部内收力");
        Assert.IsFalse(source.Contains("顶部柔软度", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("顶部圆润度", StringComparison.Ordinal));
        StringAssert.Contains(source, "new Expander");
        StringAssert.Contains(source, "IsExpanded = sectionIndex == 0");
    }

    [TestMethod]
    public void ForceInputMovesCoreAndJumpIsEdgeTriggered()
    {
        using var world = CreateWorld(DangoSelfPhysicsParameters.CreateDefault());
        var input = CreateInput();
        for (var index = 0; index < 300; index++)
        {
            world.Step(input, FixedStep, DangoSelfPhysicsControlState.None);
            Assert.IsFalse(world.IsRestartBlocked, $"input settle step={index}");
        }

        var baseline = world.Step(input, FixedStep, DangoSelfPhysicsControlState.None);
        StackedPhysicsPresentationFrame? moved = null;
        for (var index = 0; index < 60; index++)
        {
            moved = world.Step(input, FixedStep, new(1, 0, false, false, default));
            Assert.IsFalse(world.IsRestartBlocked, $"move step={index}");
            Assert.AreEqual(PhysicsPresentationFallbackReason.None, moved.Bodies[0].FallbackReason, $"move step={index}");
        }
        Assert.IsNotNull(moved);
        Assert.IsGreaterThan(baseline.RenderOffsets[0].X + 3, moved.RenderOffsets[0].X);

        var jumped = world.Step(input, FixedStep, new(0, 0, true, false, default));
        var held = world.Step(input, FixedStep, new(0, 0, false, false, default));
        Assert.IsFalse(world.IsRestartBlocked, "jump and release");
        Assert.IsLessThan(moved.RenderOffsets[0].Y, jumped.RenderOffsets[0].Y, "跳跃冲量必须使团子向上。");
        Assert.IsLessThan(jumped.RenderOffsets[0].Y + 6, held.RenderOffsets[0].Y, "按住不得每帧叠加跳跃冲量。");
    }

    private static Box2DSelfPhysicsPresentationWorld CreateWorld(DangoSelfPhysicsParameters parameters) =>
        new(Box2DNativeLoader.DefaultLibraryPath, parameters, groundAnchorCenterX: 440, groundAnchorCenterY: 460);

    private static PhysicsPresentationInput CreateInput() =>
        new(
            "self-physics-primary",
            new LogicalRect(360, 300, 160, 120),
            DangoActionState.Idle,
            1_000,
            17,
            "self-physics-ground",
            new LogicalVector(0, -1),
            0,
            1,
            false,
            true,
            []);

    private static double Distance(PresentationPoint first, PresentationPoint second) =>
        Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));

    private static double WidthAtHalf(IReadOnlyList<PresentationPoint> contour, bool lower)
    {
        var points = contour.Where(point => lower ? point.Y < 0 : point.Y > 0).ToArray();
        return points.Max(point => point.X) - points.Min(point => point.X);
    }

    private static int CountNonAdjacentEdgeCrossings(IReadOnlyList<PresentationPoint> contour)
    {
        var count = 0;
        for (var first = 0; first < contour.Count; first++)
        {
            var firstNext = (first + 1) % contour.Count;
            for (var second = first + 2; second < contour.Count; second++)
            {
                var secondNext = (second + 1) % contour.Count;
                if (first == secondNext || firstNext == second)
                {
                    continue;
                }
                if (SegmentsIntersect(contour[first], contour[firstNext], contour[second], contour[secondNext]))
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static bool SegmentsIntersect(PresentationPoint a, PresentationPoint b, PresentationPoint c, PresentationPoint d) =>
        Cross(a, b, c) * Cross(a, b, d) < 0 && Cross(c, d, a) * Cross(c, d, b) < 0;

    private static double Cross(PresentationPoint a, PresentationPoint b, PresentationPoint c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    private static double MinimumInteriorAngleDegrees(IReadOnlyList<PresentationPoint> contour) =>
        Enumerable.Range(0, contour.Count).Select(index =>
        {
            var current = contour[index];
            var previous = contour[(index - 1 + contour.Count) % contour.Count];
            var next = contour[(index + 1) % contour.Count];
            var firstX = previous.X - current.X;
            var firstY = previous.Y - current.Y;
            var secondX = next.X - current.X;
            var secondY = next.Y - current.Y;
            var cosine = ((firstX * secondX) + (firstY * secondY)) /
                (Math.Sqrt((firstX * firstX) + (firstY * firstY)) * Math.Sqrt((secondX * secondX) + (secondY * secondY)));
            return Math.Acos(Math.Clamp(cosine, -1, 1)) * 180 / Math.PI;
        }).Min();

    private static double MaximumSignedInteriorAngleDegrees(IReadOnlyList<PresentationPoint> contour) =>
        InteriorAnglesDegrees(contour).Max();

    private static double MaximumAdjacentInteriorAngleDifferenceDegrees(IReadOnlyList<PresentationPoint> contour)
    {
        var angles = InteriorAnglesDegrees(contour);
        return angles.Select((angle, index) =>
            Math.Abs(angle - angles[(index + 1) % angles.Length])).Max();
    }

    private static double[] InteriorAnglesDegrees(IReadOnlyList<PresentationPoint> contour) =>
        Enumerable.Range(0, contour.Count).Select(index =>
        {
            var current = contour[index];
            var previous = contour[(index - 1 + contour.Count) % contour.Count];
            var next = contour[(index + 1) % contour.Count];
            var incomingX = current.X - previous.X;
            var incomingY = current.Y - previous.Y;
            var outgoingX = next.X - current.X;
            var outgoingY = next.Y - current.Y;
            var cross = (incomingX * outgoingY) - (incomingY * outgoingX);
            var dot = (incomingX * outgoingX) + (incomingY * outgoingY);
            var signedTurn = Math.Atan2(cross, dot) * 180 / Math.PI;
            return 180 - signedTurn;
        }).ToArray();
}
