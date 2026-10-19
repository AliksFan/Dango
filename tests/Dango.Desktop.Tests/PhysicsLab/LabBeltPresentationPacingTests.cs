using Dango.PhysicsLab;

namespace Dango.Desktop.Tests.PhysicsLab;

[TestClass]
public sealed class LabBeltPresentationPacingTests
{
    [TestMethod]
    public void AlternatingZeroAndTwoProducedTicksBecomeOneCompleteTickPerCallbackAfterPriming()
    {
        var run = Simulate([0, 2, 0, 2, 0, 2, 0, 2]);
        // Draining every callback instead would produce 0,2,0,2,... and fails this assertion.
        CollectionAssert.AreEqual(new[] { 0, 1, 1, 1, 1, 1, 1, 1 }, run.AppliedPerCallback);
        CollectionAssert.AreEqual(Enumerable.Range(1, 7).ToArray(), run.AppliedTicks);
        CollectionAssert.AreEqual(new[] { 8 }, run.RemainingTicks);
    }

    [TestMethod]
    public void InitialPrimingWaitsForTwoCompleteFramesAndDoesNotConsumeTheFirstOneEarly()
    {
        Assert.AreEqual(new LabBeltPresentationDecision(false, 0, false), LabBeltPresentationPacing.Decide(false, 0, true, false));
        Assert.AreEqual(new LabBeltPresentationDecision(false, 0, false), LabBeltPresentationPacing.Decide(false, 1, true, false));
        Assert.AreEqual(new LabBeltPresentationDecision(true, 1, false), LabBeltPresentationPacing.Decide(false, 2, true, false));
        Assert.AreEqual(new LabBeltPresentationDecision(true, 1, false), LabBeltPresentationPacing.Decide(false, 3, true, false));
    }

    [TestMethod]
    public void AnEpochResetReprimesButATemporaryEmptyQueueDoesNot()
    {
        var previousEpoch = LabBeltPresentationPacing.Decide(false, 2, true, false);
        Assert.IsTrue(previousEpoch.Primed);
        var empty = LabBeltPresentationPacing.Decide(previousEpoch.Primed, 0, true, false);
        Assert.IsTrue(empty.Primed);
        Assert.AreEqual(0, empty.ApplyBudget);
        var resumedSameEpoch = LabBeltPresentationPacing.Decide(empty.Primed, 1, true, false);
        Assert.AreEqual(1, resumedSameEpoch.ApplyBudget, "A temporary producer gap cannot introduce another two-frame startup delay.");
        // The window passes false after observing a new epoch; the stateless policy must honor it.
        var newEpoch = LabBeltPresentationPacing.Decide(false, 1, true, false);
        Assert.IsFalse(newEpoch.Primed);
        Assert.AreEqual(0, newEpoch.ApplyBudget);
        Assert.AreEqual(1, LabBeltPresentationPacing.Decide(newEpoch.Primed, 2, true, false).ApplyBudget);
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(1, 1)]
    [DataRow(2, 1)]
    [DataRow(3, 1)]
    [DataRow(4, 2)]
    [DataRow(5, 2)]
    [DataRow(8, 2)]
    public void ARunningProducerUsesTwoTicksOnlyAtTheBacklogThreshold(int depth, int expectedBudget)
    {
        var decision = LabBeltPresentationPacing.Decide(true, depth, true, false);
        Assert.IsTrue(decision.Primed);
        Assert.AreEqual(expectedBudget, decision.ApplyBudget);
        Assert.IsFalse(decision.Flush);
    }

    [TestMethod]
    public void SustainedTwoTickProductionCatchesUpWithoutDrainingOrLosingIntermediateTicks()
    {
        var run = Simulate(Enumerable.Repeat(2, 24).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 1 }.Concat(Enumerable.Repeat(2, 22)).ToArray(), run.AppliedPerCallback);
        Assert.AreEqual(4, run.MaximumQueueDepth);
        CollectionAssert.AreEqual(Enumerable.Range(1, 46).ToArray(), run.AppliedTicks);
        CollectionAssert.AreEqual(new[] { 47, 48 }, run.RemainingTicks);
        var tail = LabBeltPresentationPacing.Decide(run.Primed, run.RemainingTicks.Length, false, false);
        Assert.IsTrue(tail.Flush);
        Assert.AreEqual(2, tail.ApplyBudget, "A replay tail cannot remain buffered after production ends.");
    }

    [TestMethod]
    public void ASlowProducerNeverInventsDuplicatesOrReordersACompleteTick()
    {
        var run = Simulate([1, 0, 1, 0, 1, 0, 1, 0]);
        CollectionAssert.AreEqual(new[] { 0, 0, 1, 1, 1, 0, 1, 0 }, run.AppliedPerCallback);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, run.AppliedTicks);
        Assert.AreEqual(0, run.RemainingTicks.Length);
    }

    [TestMethod]
    public void AFullEightFrameQueueStillAppliesOnlyTwoAndRetainsEveryOtherFrameInFifoOrder()
    {
        var run = Simulate([8]);
        Assert.AreEqual(8, run.MaximumQueueDepth, "Pacing must work with the existing bounded worker FIFO.");
        CollectionAssert.AreEqual(new[] { 1, 2 }, run.AppliedTicks);
        CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7, 8 }, run.RemainingTicks);
        Assert.AreEqual(6, run.RemainingTicks.Length, "Pacing cannot create room by silently discarding intermediate ticks.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AStoppedProducerFlushesOneRemainingFrameWithoutWaitingForPriming(bool primed)
    {
        var decision = LabBeltPresentationPacing.Decide(primed, 1, false, false);
        Assert.AreEqual(new LabBeltPresentationDecision(primed, 1, true), decision);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReplayEndOrFailureFlushesTheEntireFiniteTailInOrder(bool primed)
    {
        var queue = new Queue<int>(Enumerable.Range(1, 8));
        var decision = LabBeltPresentationPacing.Decide(primed, queue.Count, false, false);
        Assert.IsTrue(decision.Flush);
        Assert.AreEqual(8, decision.ApplyBudget);
        var applied = Enumerable.Range(0, decision.ApplyBudget).Select(_ => queue.Dequeue()).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(1, 8).ToArray(), applied);
        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 2)]
    [DataRow(8, 9)]
    public void AnOutOfBandTerminalFaultIsConsumedInTheSameCallbackAsItsPrecedingSuccesses(int queuedSuccesses, int expectedBudget)
    {
        var decision = LabBeltPresentationPacing.Decide(false, queuedSuccesses, true, true);
        Assert.IsTrue(decision.Flush, "A pending terminal fault must bypass normal priming even before the running flag settles.");
        Assert.AreEqual(expectedBudget, decision.ApplyBudget, "The bounded success FIFO does not include the separate terminal fault slot.");
        var messages = new Queue<string>(Enumerable.Range(1, queuedSuccesses).Select(x => $"tick-{x}").Append("terminal"));
        var delivered = Enumerable.Range(0, decision.ApplyBudget).Select(_ => messages.Dequeue()).ToArray();
        Assert.AreEqual("terminal", delivered[^1]);
        Assert.AreEqual(0, messages.Count);
    }

    [TestMethod]
    public void AStoppedEmptyProducerDoesNotFabricateAFrameOrATerminalMessage()
    {
        Assert.AreEqual(new LabBeltPresentationDecision(false, 0, true), LabBeltPresentationPacing.Decide(false, 0, false, false));
    }

    private sealed record Simulation(int[] AppliedPerCallback, int[] AppliedTicks, int[] RemainingTicks, int MaximumQueueDepth, bool Primed);
    private static Simulation Simulate(int[] arrivals)
    {
        var queue = new Queue<int>();
        var budgets = new List<int>(); var applied = new List<int>();
        var primed = false; var produced = 0; var maximumDepth = 0;
        foreach (var arrival in arrivals)
        {
            for (var i = 0; i < arrival; i++) queue.Enqueue(++produced);
            Assert.IsTrue(queue.Count <= 8, "This fixture must stay inside the unchanged eight-frame worker capacity.");
            maximumDepth = Math.Max(maximumDepth, queue.Count);
            var decision = LabBeltPresentationPacing.Decide(primed, queue.Count, true, false);
            primed = decision.Primed;
            Assert.IsFalse(decision.Flush);
            Assert.IsTrue(decision.ApplyBudget <= queue.Count, "Pacing requested a frame that the producer has not completed.");
            budgets.Add(decision.ApplyBudget);
            for (var i = 0; i < decision.ApplyBudget; i++) applied.Add(queue.Dequeue());
        }
        return new(budgets.ToArray(), applied.ToArray(), queue.ToArray(), maximumDepth, primed);
    }
}
