namespace Dango.Desktop.DesktopHost.Win32;

internal readonly record struct OutstandingQueryResult<T>(bool IsKnown, T Value);

/// <summary>Bounds background queries while keeping every result local to its caller.</summary>
internal sealed class SingleOutstandingQueryGate<TRequest, TResult> : IDisposable
{
    private const int Busy = 1;
    private const int Disposed = 2;
    private readonly Func<TRequest, OutstandingQueryResult<TResult>> operation;
    private int state;

    public SingleOutstandingQueryGate(Func<TRequest, OutstandingQueryResult<TResult>> operation)
    {
        this.operation = operation;
    }

    internal bool IsBusy => (Volatile.Read(ref state) & Busy) != 0;

    public bool TryQuery(TRequest request, uint timeoutMilliseconds, out TResult result)
    {
        result = default!;
        if (timeoutMilliseconds == 0 || Interlocked.CompareExchange(ref state, Busy, 0) != 0)
        {
            return false;
        }

        Task<OutstandingQueryResult<TResult>> pending;
        try
        {
            pending = ScheduleQuery(request);
        }
        catch (Exception)
        {
            Interlocked.And(ref state, ~Busy);
            return false;
        }

        if (!pending.Wait(TimeSpan.FromMilliseconds(timeoutMilliseconds))) return false;
        var completed = pending.Result;
        if (!completed.IsKnown) return false;
        result = completed.Value;
        return true;
    }

    public void Dispose() => Interlocked.Or(ref state, Disposed);

    // Keeping the capturing lambda in a separate method also defers its closure allocation until after the CAS.
    private Task<OutstandingQueryResult<TResult>> ScheduleQuery(TRequest request) => Task.Run(() => Execute(request));

    private OutstandingQueryResult<TResult> Execute(TRequest request)
    {
        try
        {
            return operation(request);
        }
        catch (Exception)
        {
            return default;
        }
        finally
        {
            // The operation owns any resources; its finally has finished before this slot reopens.
            Interlocked.And(ref state, ~Busy);
        }
    }
}
