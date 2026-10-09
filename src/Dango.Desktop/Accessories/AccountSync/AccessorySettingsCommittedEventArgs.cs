namespace Dango.Desktop.Accessories.AccountSync;

public enum AccessoryCommitOrigin { Apply, Delete, AccountRestore }

/// <summary>A notification emitted only after a local manifest commit.</summary>
public sealed class AccessorySettingsCommittedEventArgs(AccessoryConfiguration configuration,
    long localCommitVersion, AccessoryCommitOrigin origin) : EventArgs
{
    public AccessoryConfiguration Configuration { get; } = configuration;
    public long LocalCommitVersion { get; } = localCommitVersion;
    public AccessoryCommitOrigin Origin { get; } = origin;
}
