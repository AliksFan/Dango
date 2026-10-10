using System.IO;
using Dango.Client.Sync.Accounts;

namespace Dango.Desktop.AppShell;

/// <summary>Explicit internal composition for an owned loopback integration host. Production construction never uses it.</summary>
internal sealed class DangoAccountDependencies
{
    internal DangoAccountDependencies(Uri origin, IAccountSecretStore secretStore,
        Func<Uri, IAccountSecretStore, FormalAccountClient> createClient)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(createClient);
        if (!origin.IsAbsoluteUri || origin.Scheme != Uri.UriSchemeHttps || origin.Host != "127.0.0.1" || origin.Port <= 0
            || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/"
            || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new ArgumentException("Account integration requires an explicit loopback HTTPS origin.", nameof(origin));
        Origin = origin;
        SecretStore = secretStore;
        CreateClient = createClient;
    }

    internal Uri Origin { get; }
    internal IAccountSecretStore SecretStore { get; }
    internal Func<Uri, IAccountSecretStore, FormalAccountClient> CreateClient { get; }
}

internal sealed partial class DangoApplicationHost
{
    private DangoAccountDependencies? accountDependencies;

    // No CLI, environment variable or persisted setting can enable the integration composition.
    internal static DangoApplicationHost CreateForAccountIntegration(string settingsFile, string nativeLibraryPath,
        DangoAccountDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeLibraryPath);
        if (!Path.IsPathFullyQualified(settingsFile) || !Path.IsPathFullyQualified(nativeLibraryPath))
            throw new ArgumentException("Account integration requires explicitly selected absolute file paths.");
        return new DangoApplicationHost(settingsFile: settingsFile, nativeLibraryPath: nativeLibraryPath)
        {
            accountDependencies = dependencies
        };
    }
}
