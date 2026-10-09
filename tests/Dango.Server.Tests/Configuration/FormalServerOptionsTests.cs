using Dango.Server.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using System.Text.Json;

namespace Dango.Server.Tests.Configuration;

[TestClass]
public sealed class FormalServerOptionsTests
{
    private const string DatabaseName = "dango_configuration_test";
    // Fictional marker to catch disclosure; never used to open a connection.
    private const string Connection = "Host=127.0.0.1;Database=dango_configuration_test;Username=fictional_user;Password=fictional_secret_marker;Port=5432";
    private static string Repository => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dango-config-check", "repository"));
    private static string Keys => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dango-config-check", "external-keys"));

    [TestMethod]
    public void ExplicitLocalConfigurationIsBoundedAndRedactedWithoutCreatingFiles()
    {
        Assert.IsTrue(FormalServerOptions.TryCreate(DatabaseName, Connection + ";Include Error Detail=true;Maximum Pool Size=500",
            Keys, "https://127.0.0.1:443/", Repository, out var options, out var error));
        Assert.IsNotNull(options);
        Assert.AreEqual(string.Empty, error);
        var database = new NpgsqlConnectionStringBuilder(options.ConnectionString);
        Assert.AreEqual(32, database.MaxPoolSize);
        Assert.AreEqual(5, database.Timeout);
        Assert.AreEqual(15, database.CommandTimeout);
        Assert.IsFalse(database.IncludeErrorDetail);
        Assert.IsFalse(database.LogParameters);
        Assert.AreEqual("FormalServerOptions [redacted]", options.ToString());
        Assert.AreEqual("UTC", database.Timezone);
        Assert.DoesNotContain("fictional_secret_marker", JsonSerializer.Serialize(options));
    }

    [TestMethod]
    public void MissingMalformedOrMismatchedDatabaseConfigurationReturnsOnlyStableCodes()
    {
        Reject(null, Connection, Keys, "https://127.0.0.1/", "formal_configuration_missing");
        Reject("postgres", Connection, Keys, "https://127.0.0.1/", "formal_database_target_invalid");
        Reject("dango_other", Connection, Keys, "https://127.0.0.1/", "formal_database_target_invalid");
        Reject(DatabaseName, "Host=fictional_secret_marker;Unknown=fictional_secret_marker", Keys, "https://127.0.0.1/", "formal_database_configuration_invalid");
        Reject(DatabaseName, Connection.Replace("127.0.0.1", "example.invalid", StringComparison.Ordinal), Keys,
            "https://127.0.0.1/", "formal_database_target_invalid");
        Reject(DatabaseName, "Host=127.0.0.1;Database=dango_configuration_test;Username=fictional_user", Keys,
            "https://127.0.0.1/", "formal_database_target_invalid");
    }

    [TestMethod]
    [DataRow("http://127.0.0.1/")]
    [DataRow("https://example.invalid/")]
    [DataRow("https://127.0.0.1/path")]
    [DataRow("https://127.0.0.1/?secret=fictional_secret_marker")]
    [DataRow("https://fictional_secret_marker@127.0.0.1/")]
    public void UnsafeEndpointIsRejected(string endpoint) =>
        Reject(DatabaseName, Connection, Keys, endpoint, "formal_endpoint_invalid");

    [TestMethod]
    public void RepositoryAncestorNestedAndRelativeKeyPathsAreRejected()
    {
        foreach (var keys in new[] { Repository, Path.Combine(Repository, "keys"), Path.GetDirectoryName(Repository)!, "relative-keys", Path.GetPathRoot(Repository)! })
            Reject(DatabaseName, Connection, keys, "https://127.0.0.1/", "formal_key_directory_invalid");
        Assert.IsTrue(FormalServerOptions.TryCreate(DatabaseName, Connection, Keys, "https://127.0.0.1/", Repository,
            out _, out _));
    }

    private static void Reject(string? databaseName, string connection, string keys, string endpoint, string expected)
    {
        Assert.IsFalse(FormalServerOptions.TryCreate(databaseName, connection, keys, endpoint, Repository, out var options, out var error));
        Assert.IsNull(options);
        Assert.AreEqual(expected, error);
        Assert.DoesNotContain("fictional_secret_marker", error);
    }
}
