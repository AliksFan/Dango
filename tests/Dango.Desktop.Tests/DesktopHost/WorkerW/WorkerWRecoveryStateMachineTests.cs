using Dango.Desktop.DesktopHost.WorkerW;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class WorkerWRecoveryStateMachineTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(8));

    [TestMethod]
    public void LostShellHandleCanRecoverToARebuiltWorkerW()
    {
        var state = new WorkerWRecoveryStateMachine(TimeSpan.FromSeconds(5), maximumAttempts: 3);
        state.BeginAttach(StartedAt);
        state.AttachSucceeded((nint)101);

        state.NotifyShellHandleLost(StartedAt.AddSeconds(1));
        Assert.AreEqual(WorkerWRecoveryState.Recovering, state.State);
        Assert.IsTrue(state.TryBeginRecoveryAttempt(StartedAt.AddSeconds(2)));

        state.AttachSucceeded((nint)202);

        Assert.AreEqual(WorkerWRecoveryState.Attached, state.State);
        Assert.AreEqual((nint)202, state.ParentHandle);
        Assert.AreEqual(0, state.RecoveryAttempts);
    }

    [TestMethod]
    public void RepeatedLossNotificationsDoNotRestartRecoveryWindow()
    {
        var state = AttachedState();
        var firstLoss = StartedAt.AddSeconds(1);

        state.NotifyShellHandleLost(firstLoss);
        state.NotifyShellHandleLost(StartedAt.AddSeconds(3));

        Assert.AreEqual(firstLoss, state.RecoveryStartedAt);
        Assert.AreEqual(WorkerWRecoveryState.Recovering, state.State);
        Assert.AreEqual(0, state.RecoveryAttempts);
    }

    [TestMethod]
    public void RecoveryTimeoutEntersSafeHiddenStateWithoutAnotherAttempt()
    {
        var state = AttachedState(recoveryTimeout: TimeSpan.FromSeconds(5));
        state.NotifyShellHandleLost(StartedAt);

        var shouldAttempt = state.TryBeginRecoveryAttempt(StartedAt.AddSeconds(5));

        Assert.IsFalse(shouldAttempt);
        Assert.AreEqual(WorkerWRecoveryState.SafeHidden, state.State);
        Assert.AreEqual(nint.Zero, state.ParentHandle);
    }

    [TestMethod]
    public void ExhaustedRecoveryAttemptsEnterSafeHiddenState()
    {
        var state = AttachedState(maximumAttempts: 2);
        state.NotifyShellHandleLost(StartedAt);

        Assert.IsTrue(state.TryBeginRecoveryAttempt(StartedAt.AddMilliseconds(100)));
        Assert.IsTrue(state.TryBeginRecoveryAttempt(StartedAt.AddMilliseconds(200)));
        Assert.IsFalse(state.TryBeginRecoveryAttempt(StartedAt.AddMilliseconds(300)));
        Assert.AreEqual(WorkerWRecoveryState.SafeHidden, state.State);
    }

    [TestMethod]
    public void InvalidParentHandleCannotBeAccepted()
    {
        var state = new WorkerWRecoveryStateMachine(TimeSpan.FromSeconds(5), maximumAttempts: 3);
        state.BeginAttach(StartedAt);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => state.AttachSucceeded(nint.Zero));
    }

    [TestMethod]
    public void StoppedStateIsTerminal()
    {
        var state = AttachedState();
        state.Stop();

        state.NotifyShellHandleLost(StartedAt.AddSeconds(1));

        Assert.AreEqual(WorkerWRecoveryState.Stopped, state.State);
        Assert.IsFalse(state.TryBeginRecoveryAttempt(StartedAt.AddSeconds(2)));
    }

    private static WorkerWRecoveryStateMachine AttachedState(
        TimeSpan? recoveryTimeout = null,
        int maximumAttempts = 3)
    {
        var state = new WorkerWRecoveryStateMachine(
            recoveryTimeout ?? TimeSpan.FromSeconds(5),
            maximumAttempts);
        state.BeginAttach(StartedAt);
        state.AttachSucceeded((nint)101);
        return state;
    }
}
