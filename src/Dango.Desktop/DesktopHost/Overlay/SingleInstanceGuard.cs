namespace Dango.Desktop.DesktopHost.Overlay;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex mutex;
    private bool ownsMutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        this.mutex = mutex;
        ownsMutex = true;
    }

    public static SingleInstanceGuard? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceGuard(mutex);
    }

    public void Dispose()
    {
        if (ownsMutex)
        {
            mutex.ReleaseMutex();
            ownsMutex = false;
        }

        mutex.Dispose();
    }
}
