using Npgsql;
using System.Text.Json.Serialization;

namespace Dango.Server.Configuration;

// Configuration is supplied outside the repository; it is never an API input or a log payload.
public sealed class FormalServerOptions
{
    private FormalServerOptions(string databaseName, string connectionString, string keyDirectory, Uri endpoint, string? accessoryBlobDirectory)
    {
        DatabaseName = databaseName;
        ConnectionString = connectionString;
        KeyDirectory = keyDirectory;
        Endpoint = endpoint;
        AccessoryBlobDirectory = accessoryBlobDirectory;
    }

    public string DatabaseName { get; }
    [JsonIgnore]
    public string ConnectionString { get; }
    public string KeyDirectory { get; }
    public Uri Endpoint { get; }
    public string? AccessoryBlobDirectory { get; }
    public override string ToString() => "FormalServerOptions [redacted]";

    public static bool TryCreate(
        string? databaseName, string? connectionString, string? keyDirectory, string? endpoint,
        string repositoryDirectory, out FormalServerOptions? options, out string errorCode, string? accessoryBlobDirectory = null)
    {
        options = null;
        errorCode = "formal_configuration_missing";
        if (string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(connectionString)
            || string.IsNullOrWhiteSpace(keyDirectory) || string.IsNullOrWhiteSpace(endpoint)) return false;

        errorCode = "formal_database_target_invalid";
        if (!databaseName.StartsWith("dango_", StringComparison.Ordinal) || databaseName.Length > 63
            || databaseName.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '_')) return false;

        NpgsqlConnectionStringBuilder database;
        try { database = new NpgsqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { errorCode = "formal_database_configuration_invalid"; return false; }
        if (database.Database != databaseName || database.Host != "127.0.0.1"
            || database.Port < 1 || database.Port > 65535
            || string.IsNullOrWhiteSpace(database.Username) || string.IsNullOrEmpty(database.Password)) return false;

        errorCode = "formal_endpoint_invalid";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var address) || address.Scheme != "https"
            || address.Host != "127.0.0.1" || address.Port < 1 || address.Port > 65535
            || address.UserInfo.Length != 0 || address.AbsolutePath != "/"
            || address.Query.Length != 0 || address.Fragment.Length != 0) return false;

        errorCode = "formal_key_directory_invalid";
        string keyPath;
        try
        {
            if (!Path.IsPathFullyQualified(keyDirectory) || !Path.IsPathFullyQualified(repositoryDirectory)) return false;
            keyPath = Path.GetFullPath(keyDirectory);
            var repositoryPath = Path.GetFullPath(repositoryDirectory);
            if (Within(keyPath, repositoryPath) || Within(repositoryPath, keyPath)
                || string.Equals(Path.TrimEndingDirectorySeparator(keyPath), Path.GetPathRoot(keyPath), StringComparison.OrdinalIgnoreCase))
                return false;
            // An existing reparse point can redirect a seemingly external key ring into the checkout.
            for (DirectoryInfo? directory = new(keyPath); directory is not null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return false; }

        string? blobPath = null;
        if (accessoryBlobDirectory is not null)
        {
            errorCode = "formal_accessory_directory_invalid";
            try
            {
                if (!Path.IsPathFullyQualified(accessoryBlobDirectory)) return false;
                blobPath = Path.GetFullPath(accessoryBlobDirectory);
                if (!Directory.Exists(blobPath) || Within(blobPath, repositoryDirectory) || Within(repositoryDirectory, blobPath)
                    || Within(blobPath, keyPath) || Within(keyPath, blobPath)
                    || string.Equals(Path.TrimEndingDirectorySeparator(blobPath), Path.GetPathRoot(blobPath), StringComparison.OrdinalIgnoreCase))
                    return false;
                for (DirectoryInfo? directory = new(blobPath); directory is not null; directory = directory.Parent)
                    if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            { return false; }
        }

        database.Pooling = true;
        database.MinPoolSize = 0;
        database.MaxPoolSize = 32;
        database.Timeout = 5;
        database.CommandTimeout = 15;
        database.IncludeErrorDetail = false;
        database.LogParameters = false;
        database.ApplicationName = "DangoFormalServer";
        database.Timezone = "UTC";
        options = new FormalServerOptions(databaseName, database.ConnectionString, keyPath, address, blobPath);
        errorCode = string.Empty;
        return true;
    }

    private static bool Within(string candidate, string directory)
    {
        var relative = Path.GetRelativePath(directory, candidate);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
