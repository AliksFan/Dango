using System.Windows;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.Accessories.Rig;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessorySkeletonSolverTests
{
    private static readonly AccessoryRigPose Origin = new(new Point(0, 0));
    private static AccessorySkeletonNode Node(string id, double x, double y) => new(id, x, y);
    private static AccessorySkeletonConfiguration Single(AccessorySkeletonParameters? parameters = null) =>
        new(Node("P", 10, 50), [new("A", "Synthetic A", null,
            [Node("AR", 10, 50), Node("A1", 40, 50)], parameters ?? new(Bend: 1, Restoration: 0))], []);
    private static AccessorySkeletonConfiguration Connected(AccessorySkeletonParameters? child = null) =>
        Single() with { Groups = [Single().Groups[0], new("B", "Synthetic B", "A1",
            [Node("BR", 40, 50), Node("B1", 40, 80)], child ?? new(Bend: 1, Restoration: 0))] };
    private static AccessorySkeletonSolver Start(AccessorySkeletonConfiguration configuration, AccessoryRigPose? pose = null)
    {
        var solver = new AccessorySkeletonSolver(configuration, 100, 100);
        solver.Resume(0, pose ?? Origin);
        return solver;
    }
    private static void Run(AccessorySkeletonSolver solver, int frames, long first = 1)
    {
        for (var i = 0; i < frames; i++) solver.Advance(first + i, 1d / 60, Origin);
    }

    [TestMethod]
    public void RejectsInvalidNumbersReferencesCyclesZeroLengthsAndBudgets()
    {
        var valid = Single();
        var group = valid.Groups[0];
        AccessorySkeletonConfiguration[] invalid =
        [
            valid with { Version = 2 }, valid with { Pivot = Node("P", double.NaN, 0) },
            valid with { Groups = [group with { Nodes = [] }] },
            valid with { Groups = [group with { Nodes = [Node("AR", 10, 50), Node("A1", 10, 50)] }] },
            valid with { Groups = [group with { Nodes = [Node("P", 10, 50)] }] },
            valid with { Groups = [group with { Nodes = [Node("AR", 101, 50)] }] },
            valid with { Groups = [group with { TargetId = "P" }] },
            valid with { Groups = [group with { TargetId = "A1" }] },
            valid with { Groups = [group with { TargetId = "missing" }] },
            valid with { Groups = [group with { Parameters = new(Bend: double.NaN) }] },
            valid with { Groups = [group with { Parameters = new(Gravity: 101) }] },
            valid with { Groups = [group with { Parameters = new(Elasticity: .500001) }] },
            valid with { Groups = [group with { Parameters = new(Damping: -1) }] },
            valid with { Groups = [group with { Parameters = new(Restoration: double.PositiveInfinity) }] },
            valid with { Layers = [new("L", "missing")] },
            valid with { Layers = [new("L", null), new("L", null)] },
            valid with { Groups = Enumerable.Range(0, 13).Select(i => new AccessorySkeletonGroup($"G{i}", "Synthetic", null,
                [Node($"R{i}", 0, 0)], new())).ToArray() },
            valid with { Groups = [group with { Nodes = Enumerable.Range(0, 96).Select(i => Node($"N{i}", i, 0)).ToArray() }] },
            Connected() with { Groups = [Connected().Groups[0], Connected().Groups[1] with { Nodes = [Node("BR", 41, 50)] }] },
            new(Node("P", 0, 0), [new("A", "A", "BR", [Node("AR", 0, 0)], new()),
                new("B", "B", "AR", [Node("BR", 0, 0)], new())], [])
        ];
        foreach (var configuration in invalid) Assert.Throws<ArgumentException>(() => configuration.Validate(100, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => valid.Validate(0, 100));
    }

    [TestMethod]
    public void ConfigurationAndPublishedFramesOwnImmutableCollectionCopies()
    {
        var sourceNodes = Single().Groups[0].Nodes.ToList();
        var sourceGroups = new List<AccessorySkeletonGroup> { Single().Groups[0] with { Nodes = sourceNodes } };
        var sourceLayers = new List<AccessorySkeletonLayer> { new("L", "A") };
        var source = Single() with { Groups = sourceGroups, Layers = sourceLayers };
        var solver = Start(source);
        var before = solver.Refresh();
        sourceNodes[1] = Node("changed", 90, 90); sourceGroups.Clear(); sourceLayers.Clear();
        Assert.AreEqual("A1", before.Configuration.Groups[0].Nodes[1].Id);
        Assert.AreEqual(1, before.Configuration.Layers.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<Point>)before.Groups["A"])[0] = new());
        Assert.Throws<NotSupportedException>(() => ((IList<AccessorySkeletonNode>)before.Configuration.Groups[0].Nodes)[0] = Node("X", 0, 0));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, IReadOnlyList<Point>>)before.Groups).Clear());
        solver.ApplyImpulse("A1", new(0, .1)); Run(solver, 10);
        Assert.AreEqual(new Point(.3, 0), before.Groups["A"][1]);
        Assert.AreNotEqual(before.Groups["A"][1], solver.Refresh().Groups["A"][1]);
    }

    [TestMethod]
    public void EmptyDocumentRootOnlyAndUngroupedLayersAreValid()
    {
        var empty = new AccessorySkeletonConfiguration(Node("P", 50, 50), [], [new("L", null)]);
        var solver = Start(empty); Run(solver, 10);
        Assert.AreEqual(0, solver.Refresh().Groups.Count);
        var roots = empty with { Groups = [new("A", "Empty synthetic group", null, [Node("AR", 70, 60)], new()),
            new("B", "Attached empty synthetic group", "AR", [Node("BR", 70, 60)], new())] };
        solver = Start(roots, new(new(5, 6), Math.PI / 2, 2));
        var frame = solver.Refresh();
        Assert.AreEqual(5 - .1 * 2, frame.Groups["A"][0].X, 1e-12);
        Assert.AreEqual(6 + .2 * 2, frame.Groups["A"][0].Y, 1e-12);
        Assert.AreEqual(frame.Groups["A"][0], frame.Groups["B"][0]);
        Assert.IsFalse(solver.ApplyImpulse("BR", new(1, 1)), "A shared free root remains pinned to P.");
    }

    [TestMethod]
    public void RectangularImagePixelCoordinatesApplyPivotRotationAndScaleExactlyOnce()
    {
        var configuration = new AccessorySkeletonConfiguration(Node("P", 25, 10),
            [new("A", "A", null, [Node("AR", 50, 20), Node("A1", 100, 30)], new()),
                new("B", "B", "A1", [Node("BR", 100, 30), Node("B1", 180, 40)], new())], []);
        var solver = new AccessorySkeletonSolver(configuration, 200, 50);
        solver.Resume(0, new(new(3, 4), Math.PI / 2, 2));
        var frame = solver.Refresh();
        Assert.AreEqual(3 - .2 * 2, frame.Groups["A"][0].X, 1e-12);
        Assert.AreEqual(4 + .5 * 2, frame.Groups["A"][0].Y, 1e-12);
        Assert.AreEqual(3 - .6 * 2, frame.Groups["B"][1].X, 1e-12);
        Assert.AreEqual(4 + 3.1 * 2, frame.Groups["B"][1].Y, 1e-12);
        Assert.AreEqual(frame.Groups["A"][1], frame.Groups["B"][0]);
        Assert.AreEqual(Math.PI / 2, frame.GroupPoses["B"].Angle, 1e-12);
    }

    [TestMethod]
    public void DownstreamImpulseAndDragMoveUpstreamNodesAndConnectionStaysOneParticle()
    {
        var baseline = Start(Connected());
        var impulse = Start(Connected());
        var drag = Start(Connected());
        impulse.ApplyImpulse("B1", new(.1, .03));
        drag.DisplaceNode("B1", new(.05, .02));
        // Queued inputs do not step on a paint or a duplicate owner frame.
        CollectionAssert.AreEqual(baseline.Refresh().Groups["B"].ToArray(), impulse.Refresh().Groups["B"].ToArray());
        Assert.IsFalse(impulse.Advance(0, 1d / 60, Origin));
        Run(baseline, 20); Run(impulse, 20); Run(drag, 20);
        var expected = baseline.Refresh().Groups["A"][1];
        Assert.IsTrue((impulse.Refresh().Groups["A"][1] - expected).Length > .001,
            "A parent-first one-way solver must fail: downstream momentum must affect the upstream group.");
        Assert.IsTrue((drag.Refresh().Groups["A"][1] - expected).Length > .001,
            "Dragging a connected downstream endpoint must also transfer movement upstream.");
        foreach (var solver in new[] { baseline, impulse, drag })
        {
            var frame = solver.Refresh();
            Assert.AreEqual(frame.Groups["A"][1], frame.Groups["B"][0]);
            Assert.AreEqual(Origin.Root, frame.Groups["A"][0]);
        }
    }

    [TestMethod]
    public void ChildGravityAndAngularConstraintsReactOnBothSidesIndependentOfGroupOrder()
    {
        var idle = Start(Connected());
        var gravityConfig = Connected(new(Bend: 1, Restoration: 0, Gravity: 10));
        var gravity = Start(gravityConfig);
        var reordered = Start(gravityConfig with { Groups = gravityConfig.Groups.Reverse().ToArray() });
        Run(idle, 60); Run(gravity, 60); Run(reordered, 60);
        Assert.IsTrue((gravity.Refresh().Groups["A"][1] - idle.Refresh().Groups["A"][1]).Length > .01,
            "The child's force must change the upstream group even when that group's own gravity is zero.");
        foreach (var id in new[] { "A", "B" })
            CollectionAssert.AreEqual(gravity.Refresh().Groups[id].ToArray(), reordered.Refresh().Groups[id].ToArray());

        var soft = Start(Connected(new(Bend: 1, Restoration: 0)));
        var rigidRelativeAngle = Start(Connected(new(Bend: 0, Restoration: 0)));
        soft.ApplyImpulse("B1", new(.1, 0)); rigidRelativeAngle.ApplyImpulse("B1", new(.1, 0));
        Run(soft, 15); Run(rigidRelativeAngle, 15);
        Assert.IsTrue((soft.Refresh().Groups["A"][1] - rigidRelativeAngle.Refresh().Groups["A"][1]).Length > .005,
            "An attached angle constraint must correct its reference segment as well as the child segment.");
    }

    [TestMethod]
    public void SharedParticleGravityIsMassWeightedInsteadOfAddingGroupAccelerations()
    {
        var single = Single(new(Bend: 1, Restoration: 0, Gravity: 7));
        var both = single with { Groups = [single.Groups[0], new("B", "Point mass", "A1", [Node("BR", 40, 50)], new(Gravity: 7))] };
        var zero = both with { Groups = [both.Groups[0], both.Groups[1] with { Parameters = new(Gravity: 0) }] };
        var one = Start(single); var shared = Start(both); var weighted = Start(zero);
        Run(one, 4); Run(shared, 4); Run(weighted, 4);
        Assert.AreEqual(one.Refresh().Groups["A"][1].Y, shared.Refresh().Groups["A"][1].Y, 1e-12,
            "Two identical gravity coefficients on one degree of freedom must not double its acceleration.");
        Assert.IsTrue(weighted.Refresh().Groups["A"][1].Y < shared.Refresh().Groups["A"][1].Y);
        Assert.IsTrue(weighted.Refresh().Groups["A"][1].Y > 0);
    }

    [TestMethod]
    public void BendAndSymmetricElasticityAreIndependentAndDoNotAutomaticallySaturate()
    {
        foreach (var elasticity in new[] { 0d, .5 })
        {
            var parameters = new AccessorySkeletonParameters(Bend: 0, Damping: 30, Restoration: 0, Elasticity: elasticity);
            var rest = Start(Single(parameters)); Run(rest, 20);
            Assert.AreEqual(.3, (rest.Refresh().Groups["A"][1] - rest.Refresh().Groups["A"][0]).Length, 1e-12);
            foreach (var sign in new[] { -1, 1 })
            {
                var solver = Start(Single(parameters)); solver.DisplaceNode("A1", new(sign * .24, 0));
                Run(solver, 1);
                var frame = solver.Refresh();
                Assert.AreEqual(.3 * (1 + sign * elasticity), (frame.Groups["A"][1] - frame.Groups["A"][0]).Length, 1e-10);
                Assert.AreEqual(0, frame.Groups["A"][1].Y, 1e-12, "Zero bend fixes angle without disabling extension.");
            }
        }
        var bending = Start(Single(new(Bend: 1, Restoration: 0, Gravity: 100, Elasticity: 0)));
        Run(bending, 30);
        Assert.IsTrue(bending.Refresh().Groups["A"][1].Y > .05);
        Assert.AreEqual(.3, (bending.Refresh().Groups["A"][1] - bending.Refresh().Groups["A"][0]).Length, 1e-10);
    }

    [TestMethod]
    public void GravityControlHundredMapsToFourImageHeightsPerSecondSquaredInWorldDown()
    {
        const double dt = 1d / 120;
        foreach (var pose in new[] { Origin, new AccessoryRigPose(new(3, 4), Math.PI / 2, 2) })
        {
            var solver = Start(Single(new(Bend: 1, Damping: 0, Restoration: 0, Gravity: 100, Elasticity: .5)), pose);
            var before = solver.Refresh().Groups["A"][1];
            solver.Advance(1, dt, pose);
            var displacement = solver.Refresh().Groups["A"][1] - before;
            Assert.AreEqual(0, displacement.X, 1e-12);
            Assert.AreEqual(4 * pose.Scale * dt * dt, displacement.Y, 1e-12,
                "A direct 0..100 acceleration or rotated gravity must fail this calibrated world-space check.");
        }
    }

    [TestMethod]
    public void DampingReducesTravelAndRestorationRecoversRestAngleAndLength()
    {
        AccessorySkeletonSolver Excite(double damping, double restoration)
        {
            var solver = Start(Single(new(Bend: 1, Damping: damping, Restoration: restoration, Elasticity: .5)));
            solver.DisplaceNode("A1", new(.08, .1)); Run(solver, 1);
            return solver;
        }
        var undamped = Excite(0, 0); var damped = Excite(30, 0);
        double Travel(AccessorySkeletonSolver solver)
        {
            var distance = 0d; var previous = solver.Refresh().Groups["A"][1];
            for (var tick = 2; tick < 120; tick++)
            {
                solver.Advance(tick, 1d / 60, Origin);
                var next = solver.Refresh().Groups["A"][1]; distance += (next - previous).Length; previous = next;
            }
            return distance;
        }
        Assert.IsTrue(Travel(undamped) > Travel(damped) * 2);
        var restoring = Excite(12, 12); var unrestored = Excite(12, 0);
        Run(restoring, 600, 2); Run(unrestored, 600, 2);
        var expected = new Point(.3, 0);
        Assert.IsTrue((restoring.Refresh().Groups["A"][1] - expected).Length < .001);
        Assert.IsTrue((unrestored.Refresh().Groups["A"][1] - expected).Length > .03);
    }

    [TestMethod]
    public void OwnerEpochRefreshPauseLongFrameAndResetCannotIntegrateHiddenTime()
    {
        var solver = Start(Connected(new(Gravity: 20)));
        Run(solver, 3);
        var before = solver.Refresh();
        for (var i = 0; i < 8; i++) CollectionAssert.AreEqual(before.Groups["B"].ToArray(), solver.Refresh().Groups["B"].ToArray());
        Assert.IsFalse(solver.Advance(3, 1d / 60, new(new(2, 3))));
        Assert.IsFalse(solver.Advance(2, 1d / 60, Origin));
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.Advance(4, 1e-320, Origin));
        Assert.AreEqual(3L, solver.Refresh().Tick);
        solver.Pause(); Assert.IsFalse(solver.Advance(100, 1, Origin));
        solver.Resume(101, new(new(5, 6)));
        Assert.AreEqual(new Point(5.3, 6), solver.Refresh().Groups["B"][0]);
        solver.Advance(102, 1, Origin);
        Assert.AreEqual(new Point(.3, .3), solver.Refresh().Groups["B"][1]);
        solver.Reset(Single(), 100, 100, Origin);
        Assert.AreEqual(-1L, solver.Refresh().Tick);
        Assert.AreEqual(1, solver.Refresh().Groups.Count);
        Assert.Throws<ArgumentException>(() => solver.Reset(Single() with { Version = 9 }, 100, 100, Origin));
        Assert.AreEqual(1, solver.Refresh().Groups.Count, "A rejected replacement must retain the old solver state.");
    }

    [TestMethod]
    public void MaximumNodeBudgetAndExtremeParametersStayFiniteAndWithinLengthBounds()
    {
        foreach (var elasticity in new[] { 0d, .5 })
        {
            var configuration = new AccessorySkeletonConfiguration(Node("P", 1, 1),
                Enumerable.Range(0, 12).Select(group => new AccessorySkeletonGroup($"G{group:D2}", "Synthetic budget", null,
                    Enumerable.Range(0, group == 11 ? 7 : 8).Select(i => Node($"N{group}_{i}", 2 + group * 8, 2 + i * 12)).ToArray(),
                    new(Bend: 1, Damping: 0, Restoration: 30, Gravity: 100, Elasticity: elasticity))).ToArray(), []);
            configuration.Validate(100, 100); // Exactly 96 editing identities including P.
            var solver = Start(configuration);
            for (var tick = 1; tick <= 180; tick++)
            {
                solver.Advance(tick, 1d / 30, new(new Point(Math.Sin(tick * .1) * .1, Math.Cos(tick * .13) * .1), Math.Sin(tick * .07)));
                foreach (var nodes in solver.Refresh().Groups.Values)
                {
                    Assert.IsTrue(nodes.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y)));
                    for (var i = 1; i < nodes.Count; i++)
                    {
                        var length = (nodes[i] - nodes[i - 1]).Length;
                        Assert.IsTrue(length >= .12 * (1 - elasticity) - 1e-5 && length <= .12 * (1 + elasticity) + 1e-5,
                            $"Length {length} escaped elasticity {elasticity} at owner tick {tick}.");
                    }
                }
            }
        }
    }

    [TestMethod]
    public void NinetyFiveNodeSingleChainClosesLengthsWithoutLosingDownstreamReaction()
    {
        var configuration = new AccessorySkeletonConfiguration(Node("P", 10, 2),
            [new("A", "Long synthetic chain", null,
                Enumerable.Range(0, 95).Select(i => Node($"N{i}", 10, 2 + i)).ToArray(),
                new(Bend: 1, Damping: 8, Restoration: 5, Gravity: 100))], []);
        var baseline = Start(configuration); var excited = Start(configuration);
        excited.ApplyImpulse("N94", new(.03, 0));
        var upstreamReaction = 0d;
        for (var tick = 1; tick <= 30; tick++)
        {
            var pose = new AccessoryRigPose(new(Math.Sin(tick * .1) * .05, Math.Cos(tick * .1) * .02));
            baseline.Advance(tick, 1d / 60, pose); excited.Advance(tick, 1d / 60, pose);
            var frame = excited.Refresh();
            upstreamReaction = Math.Max(upstreamReaction, (frame.Groups["A"][93] - baseline.Refresh().Groups["A"][93]).Length);
            Assert.IsTrue(frame.Groups["A"].All(point => double.IsFinite(point.X) && double.IsFinite(point.Y)));
            for (var i = 1; i < frame.Groups["A"].Count; i++)
                Assert.AreEqual(.01, (frame.Groups["A"][i] - frame.Groups["A"][i - 1]).Length, 1e-12);
        }
        Assert.IsTrue(upstreamReaction > 1e-5,
            $"Length closure must retain reaction upstream of the perturbed endpoint; observed {upstreamReaction}.");
    }

    [TestMethod]
    public void RootToRootInheritsTheTargetRootFrameRatherThanItsFirstBendingSegment()
    {
        var configuration = Connected() with
        {
            Groups = [Single().Groups[0], new("B", "Root-linked synthetic", "AR",
                [Node("BR", 10, 50), Node("B1", 10, 80)], new(Bend: 0, Restoration: 0))]
        };
        var solver = Start(configuration);
        solver.ApplyImpulse("A1", new(0, .1)); Run(solver, 20);
        var frame = solver.Refresh();
        Assert.IsTrue(frame.Groups["A"][1].Y > .01);
        Assert.AreEqual(frame.GroupPoses["A"].Angle, frame.GroupPoses["B"].Angle, 1e-12);
        Assert.AreEqual(0, frame.Groups["B"][1].X, 1e-12);
        Assert.AreEqual(.3, frame.Groups["B"][1].Y, 1e-12);
    }

    [TestMethod]
    public void ZeroBendAndZeroElasticityKeepACurvedConnectedRestShapeUnderMovingRotatingP()
    {
        var configuration = new AccessorySkeletonConfiguration(Node("P", 10, 20),
            [new("A", "Rigid synthetic A", null, [Node("AR", 20, 20), Node("A1", 50, 30), Node("A2", 65, 60)],
                new(Bend: 0, Gravity: 100, Elasticity: 0)),
                new("B", "Rigid synthetic B", "A1", [Node("BR", 50, 30), Node("B1", 70, 20)],
                    new(Bend: 0, Gravity: 100, Elasticity: 0))], []);
        var solver = Start(configuration);
        solver.ApplyImpulse("B1", new(1, 1));
        for (var tick = 1; tick <= 60; tick++)
        {
            var pose = new AccessoryRigPose(new(tick * .001, tick * .002), tick * .01, 2);
            solver.Advance(tick, 1d / 60, pose);
            var frame = solver.Refresh();
            foreach (var group in configuration.Groups)
                for (var i = 0; i < group.Nodes.Count; i++)
                {
                    var node = group.Nodes[i];
                    var offset = new Vector((node.X - configuration.Pivot.X) / 100, (node.Y - configuration.Pivot.Y) / 100);
                    var expected = pose.Root + new Vector(offset.X * Math.Cos(pose.Angle) - offset.Y * Math.Sin(pose.Angle),
                        offset.X * Math.Sin(pose.Angle) + offset.Y * Math.Cos(pose.Angle)) * pose.Scale;
                    Assert.AreEqual(expected, frame.Groups[group.Id][i]);
                }
        }
    }
}
