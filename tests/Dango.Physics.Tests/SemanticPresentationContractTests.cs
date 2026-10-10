using Dango.Physics.Presentation;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class SemanticPresentationContractTests
{
    private static SemanticBodyState Body(int id = 1, double x = 0, double y = 0, DangoEntityId? support = null) =>
        new(new(id), x, y, 0, 0, false, support);

    [TestMethod]
    public void IncomingSemanticSchemaRejectsUnknownVersionsNonfiniteAndInvalidIdentity()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SemanticPresentationSnapshot("one", 1, [Body()], [], 2));
        Assert.ThrowsExactly<ArgumentException>(() => new SemanticPresentationSnapshot("one", 1, [Body(), Body()], []));
        Assert.ThrowsExactly<ArgumentException>(() => new SemanticPresentationSnapshot("one", 1, [Body(x: double.NaN)], []));
        Assert.ThrowsExactly<ArgumentException>(() => new SemanticPresentationSnapshot("one", 1, [Body(support: new(2))], []));
        Assert.ThrowsExactly<ArgumentException>(() => new SemanticPresentationSnapshot("one", 1, [Body(), Body(2)],
            [new(new(1), new(2), 0, 0, 0, 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new SemanticPresentationSnapshot("one", 1,
            [Body(support: new(2)), Body(2, y: 1, support: new(1))], [new(new(1), new(2), 0, 1, 0, 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new SemanticPresentationSnapshot("one", 1,
            [Body(), Body(2, y: 1, support: new(1))], [new(new(1), new(2), 1, 0, 0, 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new SemanticPresentationSnapshot("one", 1,
            [Body(), Body(2, y: 1, support: new(1))], [new(new(1), new(2), 0, 1, .02, 1)]));
        var bodyFields = typeof(SemanticBodyState).GetProperties().Select(property => property.Name).ToArray();
        Assert.IsFalse(bodyFields.Any(name => name.Contains("Contour", StringComparison.Ordinal) || name.Contains("Node", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void SamplerIsBoundedRejectsOldTickAndChangesEpochAtomically()
    {
        var sampler = new SemanticPresentationSampler();
        Assert.IsFalse(sampler.Push(new("one", 0, [Body()], [])), "Only an explicit epoch binding accepts an incoming frame.");
        sampler.BindEpoch("one");
        for (var tick = 0; tick < 30; tick += 3) Assert.IsTrue(sampler.Push(new("one", tick, [Body(x: tick)], [])));
        Assert.AreEqual(8, sampler.Count);
        Assert.IsFalse(sampler.Push(new("one", 24, [Body(x: -500)], [])));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sampler.Sample(3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sampler.Sample(28));
        Assert.IsFalse(sampler.Push(new("two", 0, [Body(3)], [])), "An incoming foreign epoch cannot reset the verified binding.");
        sampler.BindEpoch("two");
        Assert.IsTrue(sampler.Push(new("two", 0, [Body(3)], [])));
        Assert.IsFalse(sampler.Push(new("one", 30, [Body()], [])), "An old epoch cannot roll back the new binding.");
        Assert.AreEqual(1, sampler.Count);
        Assert.AreEqual(new DangoEntityId(3), sampler.Sample(0).Bodies.Single().EntityId);
    }

    [TestMethod]
    public void TwentyHertzHermiteReconstructsConstantAccelerationAtSharedTime()
    {
        const double acceleration = -9.8;
        var sampler = new SemanticPresentationSampler();
        sampler.BindEpoch("one");
        foreach (var tick in new[] { 0, 3, 6 })
        {
            var seconds = tick * SemanticPresentationSampler.FixedStep.TotalSeconds;
            sampler.Push(new("one", tick, [Body(y: 2 + acceleration * seconds * seconds / 2) with { CoreVelocityY = acceleration * seconds }], []));
        }
        foreach (var tick in new[] { .2, .5, 1d, 2d, 3.4, 4.1, 5d })
        {
            var seconds = tick * SemanticPresentationSampler.FixedStep.TotalSeconds;
            Assert.AreEqual(2 + acceleration * seconds * seconds / 2, sampler.Sample(tick).Bodies.Single().CoreY, 1e-12);
            Assert.AreEqual(acceleration * seconds, sampler.Sample(tick).Bodies.Single().CoreVelocityY, 1e-11);
        }
    }

    [TestMethod]
    public void SampleVelocityIsTheDerivativeOfTheSharedHermitePosition()
    {
        var sampler = new SemanticPresentationSampler();
        sampler.BindEpoch("one");
        Assert.IsTrue(sampler.Push(new("one", 0, [Body(x: 0)], [])));
        Assert.IsTrue(sampler.Push(new("one", 6, [Body(x: .1)], [])));
        const double tick = 2;
        const double epsilon = .00001;
        var derivative = (sampler.Sample(tick + epsilon).Bodies[0].CoreX - sampler.Sample(tick - epsilon).Bodies[0].CoreX) /
            (2 * epsilon * SemanticPresentationSampler.FixedStep.TotalSeconds);
        var velocity = sampler.Sample(tick).Bodies[0].CoreVelocityX;
        Assert.AreEqual(derivative, velocity, 1e-8);
        Assert.IsTrue(velocity > 1, "The former linear blend of zero endpoint velocities must fail this curve derivative test.");
    }

    [TestMethod]
    public void SceneRevisionCannotGoBackOrChangeMembershipWithoutATopologyBoundary()
    {
        var sampler = new SemanticPresentationSampler();
        sampler.BindEpoch("one");
        Assert.IsTrue(sampler.Push(new("one", 0, [Body()], [], sceneRevision: 5)));
        Assert.IsFalse(sampler.Push(new("one", 3, [Body()], [], sceneRevision: 4)));
        Assert.IsFalse(sampler.Push(new("one", 3, [Body(), Body(2, x: 3)], [], sceneRevision: 5)));
        Assert.AreEqual(1, sampler.Count);
        Assert.IsTrue(sampler.Push(new("one", 3, [Body(), Body(2, x: 3)], [], sceneRevision: 6)));
        Assert.AreEqual(5ul, sampler.Sample(2).SceneRevision);
        Assert.AreEqual(1, sampler.Sample(2).Bodies.Count);
        Assert.AreEqual(6ul, sampler.Sample(3).SceneRevision);
        Assert.AreEqual(2, sampler.Sample(3).Bodies.Count);
    }

    [TestMethod]
    public void ReversedNormalsFalseContactGapsAndSideSupportAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SemanticPresentationSnapshot("one", 1,
            [Body(), Body(2, x: 1)], [new(new(1), new(2), -1, 0, 0, 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new SemanticPresentationSnapshot("one", 1,
            [Body(), Body(2, x: 1)], [new(new(1), new(2), 1, 0, .02, 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new SemanticPresentationSnapshot("one", 1,
            [Body(), Body(2, x: 1, support: new(1))], [new(new(1), new(2), 1, 0, 0, 1)]));
    }

    [TestMethod]
    public void SupportingBodyRemovalClearsSemanticSupportAtTheNextSampledStep()
    {
        var sampler = new SemanticPresentationSampler();
        sampler.BindEpoch("one");
        sampler.Push(new("one", 3, [Body(1), Body(2, y: 1, support: new(1))], [new(new(1), new(2), 0, 1, 0, 1)]));
        sampler.Push(new("one", 6, [Body(2, y: .99)], [], sceneRevision: 2));
        Assert.AreEqual(new DangoEntityId(1), sampler.Sample(5).Bodies.Single(body => body.EntityId.Value == 2).SupportEntityId);
        var next = sampler.Sample(6);
        Assert.AreEqual(1, next.Bodies.Count);
        Assert.IsNull(next.Bodies[0].SupportEntityId);
    }

    [TestMethod]
    public void IndependentRenderRatesUseIdenticalCommonSampleCentersAndSupport()
    {
        var histories = new List<List<SemanticPresentationSample>>();
        foreach (var rate in new[] { 30, 60, 144 })
        {
            var published = Enumerable.Range(0, 8).Select(index =>
            {
                var tick = index * 3;
                var x = tick / 60d;
                var upper = Body(2, x, 1, tick is >= 6 and < 18 ? new(1) : null) with { CoreVelocityX = 1 };
                return new SemanticPresentationSnapshot("one", tick,
                    [Body(1, x) with { CoreVelocityX = 1 }, upper], [new(new(1), new(2), 0, 1, 0, 1)]);
            }).ToArray();
            var sampler = new SemanticPresentationSampler();
            sampler.BindEpoch("one");
            sampler.Push(published[0]);
            var cursor = 1;
            var latest = 0L;
            var next = 1;
            var history = new List<SemanticPresentationSample>();
            for (var render = 1; render <= 21 * rate / 60 + 1; render++)
                while (next <= Math.Min(21, render * 60 / rate))
                {
                    while (latest < next)
                    {
                        var frame = published[cursor++];
                        sampler.Push(frame); latest = frame.Tick;
                    }
                    history.Add(sampler.Sample(next++));
                }
            Assert.AreEqual(21, history.Count);
            histories.Add(history);
        }
        foreach (var history in histories.Skip(1))
            for (var tick = 0; tick < 21; tick++)
            {
                Assert.AreEqual(histories[0][tick].Tick, history[tick].Tick);
                for (var body = 0; body < 2; body++)
                {
                    Assert.AreEqual(histories[0][tick].Bodies[body].CoreX, history[tick].Bodies[body].CoreX, 1e-5);
                    Assert.AreEqual(histories[0][tick].Bodies[body].CoreY, history[tick].Bodies[body].CoreY, 1e-5);
                    Assert.AreEqual(histories[0][tick].Bodies[body].SupportEntityId, history[tick].Bodies[body].SupportEntityId);
                    Assert.AreEqual(histories[0][tick].Bodies[body].FloorGrounded, history[tick].Bodies[body].FloorGrounded);
                }
            }
    }

    [TestMethod]
    public void ActualDrawnSatAndCenterGatesRejectWrongImplementations()
    {
        var contour = Enumerable.Range(0, 16).SelectMany(node => new[]
            { (float)(.72 * Math.Cos(node * Math.PI / 8)), (float)(.82 * Math.Sin(node * Math.PI / 8)) }).ToArray();
        var native = new DangoBodyState(new(1), contour, 500, 0, 1.8f, 0, 0, 0, 0, false, -1, 16);
        var firstGeometry = SemanticDrawnGeometry.SampleContour(contour, 0, 0);
        var width = firstGeometry.Max(point => point.X) - firstGeometry.Min(point => point.X);
        SemanticPresentedBody Presented(int id, double x, double wrongCenter = 0) => new(Body(id, x), native with { EntityId = new(id), CoreX = (float)x },
            x + wrongCenter, 0, SemanticDrawnGeometry.SampleContour(contour, x + wrongCenter, 0));
        var safeBodies = new[] { Presented(1, 0), Presented(2, width * .98) };
        var safe = new SemanticPresentedFrame("one", 1, safeBodies, [])
            { StepStartTick = 0, LocalContourTick = 1, NativeCoreProof = ConstantCoreProof(safeBodies) };
        Assert.IsTrue(SemanticPresentationGate.Evaluate(safe).Passed);
        var deep = safe with { Bodies = [Presented(1, 0), Presented(2, width * .95)] };
        CollectionAssert.Contains(SemanticPresentationGate.Evaluate(deep).Failures.ToArray(), "display-penetration");
        var nativeCenter = safe with { Bodies = [Presented(1, 0, 500), Presented(2, width)] };
        CollectionAssert.Contains(SemanticPresentationGate.Evaluate(nativeCenter).Failures.ToArray(), "authority-center");
        var floating = safe with { Bodies = [Presented(1, 0), Presented(2, width + .02) with
            { Authority = Body(2, width + .02, support: new(1)) }] };
        CollectionAssert.Contains(SemanticPresentationGate.Evaluate(floating).Failures.ToArray(), "supported-display-gap");
        var futureContour = safe with { StepStartTick = 1, LocalContourTick = 2 };
        CollectionAssert.Contains(SemanticPresentationGate.Evaluate(futureContour).Failures.ToArray(), "local-contour-phase");

        var dynamicCore = safe with { Bodies = [safeBodies[0] with { LocalBody = safeBodies[0].LocalBody with { CoreX = .01f } }, safeBodies[1]] };
        var dynamicGate = SemanticPresentationGate.Evaluate(dynamicCore);
        Assert.AreEqual(0, dynamicGate.MaximumCenterError, "A renderer filled with authority centers cannot conceal a moving native core.");
        CollectionAssert.Contains(dynamicGate.Failures.ToArray(), "native-core-path");
        var original = safe.NativeCoreProof!;
        var first = original.Bodies[0];
        var bothForged = safe with { NativeCoreProof = original with { Bodies = [first with { Samples = first.Samples.Select(sample => sample with
            { ActualCoreX = sample.ActualCoreX + .01, NativeTargetCoreX = sample.NativeTargetCoreX + .01 }).ToArray() }, original.Bodies[1]] } };
        CollectionAssert.Contains(SemanticPresentationGate.Evaluate(bothForged).Failures.ToArray(), "native-core-path");
        var movedDuringConstraint = safe with { NativeCoreProof = original with { Bodies = [first with { MaximumStageError = .0001 }, original.Bodies[1]] } };
        CollectionAssert.Contains(SemanticPresentationGate.Evaluate(movedDuringConstraint).Failures.ToArray(), "native-core-path");
        CollectionAssert.Contains(SemanticPresentationGate.Evaluate(safe with { NativeCoreProof = null }).Failures.ToArray(), "native-core-proof");
    }

    private static SemanticPrescribedCoreProof ConstantCoreProof(IEnumerable<SemanticPresentedBody> bodies) => new(1, 1, 1, 1, 0, 1,
        bodies.Select(body =>
        {
            var target = new DangoPresentationTarget(body.Authority.EntityId, (float)body.Authority.CoreX, (float)body.Authority.CoreY, 0, 0);
            return new SemanticPrescribedBodyProof(body.Authority.EntityId, target, target, 0,
                [new(0, target.CoreX, target.CoreY, target.CoreX, target.CoreY), new(1, target.CoreX, target.CoreY, target.CoreX, target.CoreY)]);
        }).ToArray());

    [TestMethod]
    public void StepStartTargetsMustProduceTheCurrentTimeRatherThanTheNextTime()
    {
        var seconds = SemanticPresentationSampler.FixedStep.TotalSeconds;
        var current = Body(x: 1) with { CoreVelocityX = 6 };
        var previous = current with { CoreX = 1 - 6 * seconds };
        var target = SemanticPresentationStepTiming.TargetAtStart(new(20), current, previous);
        var currentTimePosition = target.CoreX + target.CoreVelocityX * seconds;
        Assert.AreEqual(current.CoreX, currentTimePosition, 1e-5, "The simple free-flight oracle must end at the drawn time.");
        var wrongCurrentTarget = new DangoPresentationTarget(new(20), (float)current.CoreX, 0, (float)current.CoreVelocityX, 0);
        var wrongNextTimePosition = wrongCurrentTarget.CoreX + wrongCurrentTarget.CoreVelocityX * seconds;
        Assert.IsTrue(Math.Abs(wrongNextTimePosition - current.CoreX) > 1e-5, "The former target(T)+Step error must exceed the frozen center budget.");
        var newlyVisible = SemanticPresentationStepTiming.TargetAtStart(new(20), current, null);
        Assert.AreEqual(current.CoreX, newlyVisible.CoreX + newlyVisible.CoreVelocityX * seconds, 1e-5,
            "A new body back-projects one step instead of generating a future contour.");
    }
}
