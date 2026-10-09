using Dango.AccessoryAssets;
using Dango.Server.Accounts;
using Dango.Server.Configuration;

namespace Dango.Server.Accessories;

public sealed class AccessoryStorage(FormalServerOptions options)
{
    public FileAccessoryBlobStore RequireStore()
    {
        var directory = options.AccessoryBlobDirectory;
        if (string.IsNullOrEmpty(directory) || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
            throw new AccountFault("accessory_storage_unavailable", 503);
        for (DirectoryInfo? part = new(directory); part is not null; part = part.Parent)
            if (part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new AccountFault("accessory_storage_unavailable", 503);
        return new FileAccessoryBlobStore(directory);
    }
}

public interface IAccessoryCheckpoint
{
    Task ReachedAsync(string name, CancellationToken ct);
}
public sealed class AccessoryCheckpoint : IAccessoryCheckpoint
{
    public Task ReachedAsync(string name, CancellationToken ct) => Task.CompletedTask;
}

// Bound decoded-image memory before reading upload bodies, across all accounts in this host.
public sealed class AccessoryUploadGate : IDisposable
{
    internal SemaphoreSlim Semaphore { get; } = new(2, 2);
    public void Dispose() => Semaphore.Dispose();
}
