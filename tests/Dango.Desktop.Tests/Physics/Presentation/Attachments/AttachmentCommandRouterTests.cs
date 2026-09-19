using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class AttachmentCommandRouterTests
{
    [TestMethod]
    public void ResetReturnsCancellationBeforeClearingTokenAndSequenceHistory()
    {
        using var router = new AttachmentCommandRouter(); var target = router.Add(new(1)); var token = router.RegisterPreview(target);
        Assert.IsTrue(router.Submit(target, token, 1, Blink.Default(EyeSelection.Both)).Accepted);
        router.CommitStep(target);
        var next = router.Reset(target, out var cancelled);
        Assert.AreEqual(2, cancelled.Count);
        Assert.IsTrue(cancelled.All(e => e.Status == AttachmentEventStatus.Cancelled));
        Assert.AreEqual(0, router.ConsumeResults(next).Count);
        Assert.AreEqual(AttachmentRejection.InvalidToken, router.Submit(next, token, 2, Blink.Default(EyeSelection.Left)).Reason);
    }
    [TestMethod]
    public void RejectsStaleForeignPausedAndInvalidCommandsWithoutConsumingSequence()
    {
        using var router = new AttachmentCommandRouter();
        var target = router.Add(new(1)); var token = router.RegisterPreview(target);
        using var other = new AttachmentCommandRouter(); var foreign = other.Add(new(1));
        Assert.AreEqual(AttachmentRejection.InvalidTarget, router.Submit(foreign, token, 1, new SetHatSlide(0)).Reason);
        Assert.AreEqual(AttachmentRejection.InvalidParameter, router.Submit(target, token, 1, new SetGaze(EyeSelection.Left, double.NaN, 0)).Reason);
        router.SetPaused(true);
        Assert.AreEqual(AttachmentRejection.Stopped, router.Submit(target, token, 1, new SetHatSlide(0)).Reason);
        router.SetPaused(false);
        Assert.IsTrue(router.Submit(target, token, 1, new SetHatSlide(0.5)).Accepted);
        Assert.AreEqual(AttachmentRejection.OldSequence, router.Submit(target, token, 1, new Release(AttachmentChannel.All)).Reason);
        var reset = router.Reset(target);
        Assert.AreEqual(AttachmentRejection.InvalidTarget, router.Submit(target, token, 2, new SetHatSlide(0)).Reason);
        Assert.AreEqual(AttachmentRejection.InvalidToken, router.Submit(reset, token, 2, new SetHatSlide(0)).Reason);
        Assert.AreEqual(0, router.CommitStep(reset).Count);
        router.Remove(reset);
        var added = router.Add(new(1));
        Assert.AreNotEqual(reset, added);
    }

    [TestMethod]
    public void PreemptionCancelsEventsAndNeverResurrectsLowerPriorityTargets()
    {
        using var router = new AttachmentCommandRouter();
        var target = router.Add(new(1)); var low = router.RegisterAction(target); var high = router.RegisterPreview(target);
        Assert.IsTrue(router.Submit(target, low, 1, Blink.Default(EyeSelection.Both)).Accepted);
        router.CommitStep(target);
        Assert.IsTrue(router.Submit(target, high, 1, Blink.Default(EyeSelection.Left)).Accepted);
        var cancelled = router.ConsumeResults(target).Single();
        Assert.AreEqual(AttachmentChannel.LeftBlink, cancelled.Channel);
        Assert.AreEqual(AttachmentEventStatus.Cancelled, cancelled.Status);
        Assert.IsFalse(router.CompleteEvent(target, low, 1, AttachmentChannel.LeftBlink));
        Assert.IsTrue(router.Submit(target, high, 2, new Release(AttachmentChannel.LeftBlink)).Accepted);
        Assert.AreEqual(AttachmentChannel.RightBlink, router.CommitStep(target).Single().Channel);
        Assert.IsTrue(router.CompleteEvent(target, low, 1, AttachmentChannel.RightBlink));
        var results = router.ConsumeResults(target);
        Assert.AreEqual(1, results.Count(e => e.Status == AttachmentEventStatus.Completed));
        Assert.AreEqual(1, results.Count(e => e.Status == AttachmentEventStatus.Cancelled));
        Assert.AreEqual(0, router.CommitStep(target).Count);
    }

    [TestMethod]
    public void BoundedQueueAndUnsupportedHatEventsAreExplicit()
    {
        using var router = new AttachmentCommandRouter();
        var target = router.Add(new(1)); var token = router.RegisterPreview(target);
        foreach (var direction in Enum.GetValues<HatBounceDirection>())
            Assert.AreEqual(AttachmentRejection.Unsupported, router.Submit(target, token, 1, new BounceHat(direction, 0.2)).Reason);
        Assert.AreEqual(AttachmentRejection.Unsupported, router.Submit(target, token, 1, new PulseHat(0.1)).Reason);
        for (var i = 1; i <= 64; i++) Assert.IsTrue(router.Submit(target, token, i, new SetHatSlide(0)).Accepted);
        Assert.AreEqual(AttachmentRejection.Capacity, router.Submit(target, token, 65, new SetHatSlide(0)).Reason);
        router.CommitStep(target);
        Assert.IsTrue(router.Submit(target, token, 65, new SetHatSlide(0)).Accepted);
    }

    [TestMethod]
    public void ResultCapacityCannotDropOrFalselyCompleteUnconsumedEvents()
    {
        using var router = new AttachmentCommandRouter(); var target = router.Add(new(1)); var token = router.RegisterPreview(target);
        for (var i = 1; i <= 128; i++)
        {
            Assert.IsTrue(router.Submit(target, token, i, Blink.Default(EyeSelection.Left)).Accepted);
            Assert.IsFalse(router.CompleteEvent(target, token, i, AttachmentChannel.LeftBlink), "Queued events have not run.");
            router.CommitStep(target);
            Assert.IsTrue(router.CompleteEvent(target, token, i, AttachmentChannel.LeftBlink));
        }
        Assert.AreEqual(AttachmentRejection.Capacity, router.Submit(target, token, 129, Blink.Default(EyeSelection.Left)).Reason);
        Assert.AreEqual(128, router.ConsumeResults(target).Count);
        Assert.IsTrue(router.Submit(target, token, 129, Blink.Default(EyeSelection.Left)).Accepted);
    }
    [TestMethod]
    public void BothIsAtomicWhenOneEyeIsBusyAndDoesNotConsumeRejectedSequence()
    {
        var router = new AttachmentCommandRouter();
        var target = router.Add(new(1));
        var token = router.RegisterPreview(target);
        Assert.IsTrue(router.Submit(target, token, 1, Blink.Default(EyeSelection.Left)).Accepted);
        Assert.AreEqual(AttachmentRejection.Busy, router.Submit(target, token, 2, Blink.Default(EyeSelection.Both)).Reason);
        Assert.IsTrue(router.Submit(target, token, 2, Blink.Default(EyeSelection.Right)).Accepted);
        Assert.AreEqual(2, router.CommitStep(target).Count);
    }

    [TestMethod]
    public void IndependentGazeAndReleaseDoNotMutateTheOtherEyeOrEntity()
    {
        var router = new AttachmentCommandRouter();
        var a = router.Add(new(1)); var b = router.Add(new(2));
        var token = router.RegisterPreview(a);
        Assert.IsTrue(router.Submit(a, token, 1, new SetGaze(EyeSelection.Left, 1, -1)).Accepted);
        Assert.IsTrue(router.Submit(a, token, 2, new SetGaze(EyeSelection.Right, -1, 1)).Accepted);
        var before = router.CommitStep(a);
        Assert.AreEqual(2, before.Count);
        Assert.AreEqual(0, router.CommitStep(b).Count);
        Assert.IsTrue(router.Submit(a, token, 3, new Release(AttachmentChannel.LeftGaze)).Accepted);
        Assert.AreEqual(AttachmentChannel.RightGaze, router.CommitStep(a).Single().Channel);
        Assert.AreEqual(2, before.Count, "Published snapshots must stay immutable.");
    }
}
