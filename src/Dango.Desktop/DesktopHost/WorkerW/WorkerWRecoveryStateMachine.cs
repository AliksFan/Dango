namespace Dango.Desktop.DesktopHost.WorkerW;

public enum WorkerWRecoveryState
{
    Detached,
    Recovering,
    Attached,
    SafeHidden,
    Stopped,
}

public sealed class WorkerWRecoveryStateMachine
{
    private readonly TimeSpan recoveryTimeout;
    private readonly int maximumAttempts;

    public WorkerWRecoveryStateMachine(TimeSpan recoveryTimeout, int maximumAttempts)
    {
        if (recoveryTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(recoveryTimeout));
        }

        if (maximumAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        this.recoveryTimeout = recoveryTimeout;
        this.maximumAttempts = maximumAttempts;
    }

    public WorkerWRecoveryState State { get; private set; } = WorkerWRecoveryState.Detached;

    public nint ParentHandle { get; private set; }

    public DateTimeOffset? RecoveryStartedAt { get; private set; }

    public int RecoveryAttempts { get; private set; }

    public void BeginAttach(DateTimeOffset now)
    {
        if (State == WorkerWRecoveryState.Stopped)
        {
            return;
        }

        ParentHandle = nint.Zero;
        RecoveryStartedAt = now;
        RecoveryAttempts = 0;
        State = WorkerWRecoveryState.Recovering;
    }

    public void AttachSucceeded(nint parentHandle)
    {
        if (parentHandle == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(parentHandle));
        }

        if (State == WorkerWRecoveryState.Stopped)
        {
            return;
        }

        ParentHandle = parentHandle;
        RecoveryStartedAt = null;
        RecoveryAttempts = 0;
        State = WorkerWRecoveryState.Attached;
    }

    public void NotifyShellHandleLost(DateTimeOffset now)
    {
        if (State != WorkerWRecoveryState.Attached)
        {
            return;
        }

        ParentHandle = nint.Zero;
        RecoveryStartedAt = now;
        RecoveryAttempts = 0;
        State = WorkerWRecoveryState.Recovering;
    }

    public bool TryBeginRecoveryAttempt(DateTimeOffset now)
    {
        if (State != WorkerWRecoveryState.Recovering)
        {
            return false;
        }

        if (RecoveryStartedAt is null ||
            now - RecoveryStartedAt.Value >= recoveryTimeout ||
            RecoveryAttempts >= maximumAttempts)
        {
            EnterSafeHidden();
            return false;
        }

        RecoveryAttempts++;
        return true;
    }

    public void EnterSafeHidden()
    {
        if (State == WorkerWRecoveryState.Stopped)
        {
            return;
        }

        ParentHandle = nint.Zero;
        RecoveryStartedAt = null;
        State = WorkerWRecoveryState.SafeHidden;
    }

    public void Stop()
    {
        ParentHandle = nint.Zero;
        RecoveryStartedAt = null;
        State = WorkerWRecoveryState.Stopped;
    }
}
