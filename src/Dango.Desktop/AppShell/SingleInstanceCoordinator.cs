namespace Dango.Desktop.AppShell;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activationEvent;
    private readonly RegisteredWaitHandle activationRegistration;
    private readonly Action activationRequested;
    private bool disposed;

    private SingleInstanceCoordinator(
        Mutex mutex,
        EventWaitHandle activationEvent,
        Action activationRequested)
    {
        this.mutex = mutex;
        this.activationEvent = activationEvent;
        this.activationRequested = activationRequested;
        activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            OnActivationRequested,
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public static SingleInstanceCoordinator? TryBecomePrimary(
        string name,
        Action activationRequested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(activationRequested);

        var mutex = new Mutex(initiallyOwned: true, $"{name}.Mutex", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        try
        {
            var activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                $"{name}.Activate");
            return new SingleInstanceCoordinator(mutex, activationEvent, activationRequested);
        }
        catch
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
    }

    public static bool TrySignalPrimary(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting($"{name}.Activate");
            return activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        activationRegistration.Unregister(null);
        activationEvent.Dispose();
        mutex.ReleaseMutex();
        mutex.Dispose();
    }

    private void OnActivationRequested(object? state, bool timedOut)
    {
        if (timedOut || disposed)
        {
            return;
        }

        try
        {
            activationRequested();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to activate Dango control center: {exception.Message}");
        }
    }
}
