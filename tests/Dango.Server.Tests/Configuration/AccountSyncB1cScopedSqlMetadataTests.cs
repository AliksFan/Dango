using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dango.Protocol;
using Dango.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Configuration;

// Review only the actual PowerShell getter artifact. No PostgreSQL connection is opened.
[TestClass]
[DoNotParallelize]
[TestCategory("OfflineAccountSyncScopedSql")]
public sealed class AccountSyncB1cScopedSqlMetadataTests
{
    private const string Owner = "dddddddd-dddd-dddd-dddd-dddddddddddd";
    private const string Epoch = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private static readonly string[][] Ids =
    [
        ["a1111111-1111-4111-8111-111111111111", "a2222222-2222-4222-8222-222222222222", "a3333333-3333-4333-8333-333333333333", "a4444444-4444-4444-8444-444444444444", "a5555555-5555-4555-8555-555555555555"],
        ["b1111111-1111-4111-8111-111111111111", "b2222222-2222-4222-8222-222222222222", "b3333333-3333-4333-8333-333333333333", "b4444444-4444-4444-8444-444444444444", "b5555555-5555-4555-8555-555555555555"]
    ];
    private static readonly string[] ScopeNames = ["users", "valid_users", "named_users", "complete_graphs", "pending_unsent_emails", "sessions", "valid_sessions", "revoked_sessions", "exact_sessions"];
    private static readonly string[] ConfigurationFields = ["appearanceSchemaVersion", "bodyColorHex", "eyeColorHex", "eyeSizePercent", "eyeOvalityPercent", "eyeHeightControlPercent", "eyeSpacingControlPercent"];
    private const string ConfigurationA = "{\"appearanceSchemaVersion\":3,\"bodyColorHex\":\"#FFE8C2\",\"eyeColorHex\":\"#272321\",\"eyeSizePercent\":111,\"eyeOvalityPercent\":0,\"eyeHeightControlPercent\":51,\"eyeSpacingControlPercent\":24.5}";
    private const string ConfigurationB = "{\"appearanceSchemaVersion\":3,\"bodyColorHex\":\"#9ECBEF\",\"eyeColorHex\":\"#272321\",\"eyeSizePercent\":130,\"eyeOvalityPercent\":0,\"eyeHeightControlPercent\":51,\"eyeSpacingControlPercent\":24.5}";

    [TestMethod]
    public void Actual_two_actor_query_resolves_every_scope_against_model_and_snapshot_without_database_access()
    {
        var artifact = ReadArtifact(); using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var report = Check(artifact.Sql, artifact.Scope, db.Model);
        Assert.AreEqual(9, report.Scopes.Count);
        Assert.AreEqual("pet_room_states", report.Scopes["complete_graphs"]["s"]);
        Assert.AreEqual("auth_sessions", report.Scopes["exact_sessions"]["s"]);
        Assert.IsTrue(report.Columns.Contains("pets.configuration"));
        Assert.IsTrue(report.Columns.Contains("email_challenges.generation"));
        Assert.IsTrue(report.Columns.Contains("auth_sessions.id"));
        Assert.AreEqual(10, report.Columns.Select(column => column.Split('.')[0]).Distinct().Count());
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.IsNotNull(snapshot);
        var snapshotReport = Check(artifact.Sql, artifact.Scope, snapshot.Model);
        CollectionAssert.AreEqual(report.Columns.Order(StringComparer.Ordinal).ToArray(), snapshotReport.Columns.Order(StringComparer.Ordinal).ToArray());
        Assert.IsNull(db.Database.GetConnectionString());
        Assert.AreEqual(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [TestMethod]
    public void Actual_query_carries_twelve_canonical_identities_and_two_full_appearance_oracles_with_ascii_transport()
    {
        var artifact = ReadArtifact(); using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        _ = Check(artifact.Sql, artifact.Scope, db.Model);
        var identities = Regex.Matches(artifact.Sql, "'([0-9a-f-]{36})'::uuid").Select(match => match.Groups[1].Value).ToArray();
        CollectionAssert.AreEqual(new[] { Owner, Epoch }.Concat(Ids.SelectMany(values => values)).ToArray(), identities);
        foreach (var configuration in new[] { ConfigurationA, ConfigurationB })
        {
            using var document = JsonDocument.Parse(configuration);
            _ = AppearanceContract.ValidateAndClone(document.RootElement);
            CollectionAssert.AreEqual(ConfigurationFields, document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        CollectionAssert.AreEqual(Encoding.GetEncoding(936).GetBytes(artifact.Sql), new UTF8Encoding(false, true).GetBytes(artifact.Sql));
    }

    [TestMethod]
    [DataRow("user")]
    [DataRow("pet")]
    [DataRow("room")]
    [DataRow("session")]
    [DataRow("epoch")]
    [DataRow("owner")]
    [DataRow("actor-order")]
    [DataRow("nickname")]
    [DataRow("body-color")]
    [DataRow("eye-color")]
    [DataRow("eye-size")]
    [DataRow("eye-ovality")]
    [DataRow("eye-height")]
    [DataRow("eye-spacing")]
    [DataRow("schema")]
    [DataRow("profile-version")]
    [DataRow("configuration-version")]
    [DataRow("unknown-column")]
    [DataRow("unknown-table")]
    [DataRow("crossed-alias")]
    [DataRow("missing-exact-session-scope")]
    [DataRow("unknown-output-key")]
    [DataRow("weakened-predicate")]
    [DataRow("missing-readonly")]
    [DataRow("extra-write")]
    [DataRow("truncated")]
    public void Mutating_any_part_of_the_actual_complete_query_is_rejected_before_database_access(string fault)
    {
        var artifact = ReadArtifact(); var sql = artifact.Sql;
        var mutation = fault switch
        {
            "user" => Replace(sql, Ids[0][0], Ids[1][0]),
            "pet" => Replace(sql, Ids[0][1], Ids[1][1]),
            "room" => Replace(sql, Ids[0][2], Ids[1][2]),
            "session" => Replace(sql, Ids[0][3], Ids[1][3]),
            "epoch" => Replace(sql, Epoch, Owner),
            "owner" => Replace(sql, Owner, Epoch),
            "actor-order" => Replace(sql, "('a','" + Ids[0][0], "('b','" + Ids[0][0]),
            "nickname" => Replace(sql, @"U&'\65E7\8D26\53F7\56E2\5B50'", "'old-account'"),
            "body-color" => Replace(sql, "#FFE8C2", "#9ECBEF"),
            "eye-color" => Replace(sql, "#272321", "#272322"),
            "eye-size" => Replace(sql, "\"eyeSizePercent\":111", "\"eyeSizePercent\":118"),
            "eye-ovality" => Replace(sql, "\"eyeOvalityPercent\":0", "\"eyeOvalityPercent\":1"),
            "eye-height" => Replace(sql, "\"eyeHeightControlPercent\":51", "\"eyeHeightControlPercent\":52"),
            "eye-spacing" => Replace(sql, "\"eyeSpacingControlPercent\":24.5", "\"eyeSpacingControlPercent\":25.5"),
            "schema" => Replace(sql, "\"appearanceSchemaVersion\":3", "\"appearanceSchemaVersion\":2"),
            "profile-version" => Replace(sql, ",2,2,'" + ConfigurationA, ",3,2,'" + ConfigurationA),
            "configuration-version" => Replace(sql, ",2,3,'" + ConfigurationB, ",2,2,'" + ConfigurationB),
            "unknown-column" => Replace(sql, "s.control_generation", "s.control_generations"),
            "unknown-table" => Replace(sql, "dango.pet_room_states s", "dango.pet_room_state s"),
            "crossed-alias" => Replace(sql, "p.user_id=u.id", "p.user_id=s.id"),
            "missing-exact-session-scope" => Replace(sql, "s.id IN (e.session_one,e.session_two)", "s.id IS NOT NULL"),
            "unknown-output-key" => Replace(sql, "'exactSessions'", "'otherSessions'"),
            "weakened-predicate" => Replace(sql, "AND u.email=e.email", "OR u.email=e.email"),
            "missing-readonly" => Replace(sql, "BEGIN READ ONLY;", "BEGIN;"),
            "extra-write" => sql + " DELETE FROM dango.users;",
            "truncated" => sql[..^5],
            _ => throw new AssertFailedException("Unknown mutation")
        };
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        Assert.ThrowsExactly<InvalidDataException>(() => Check(mutation, artifact.Scope, db.Model));
        Assert.AreEqual(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [TestMethod]
    [DataRow("shared-user")]
    [DataRow("shared-session")]
    [DataRow("session-order")]
    [DataRow("crossed-pet")]
    [DataRow("owner")]
    [DataRow("epoch")]
    [DataRow("configuration")]
    [DataRow("extra-field")]
    [DataRow("actor-order")]
    [DataRow("noninteger-version")]
    [DataRow("string-profile-version")]
    [DataRow("null-session")]
    [DataRow("duplicate-field")]
    public void Scope_mutations_cannot_rebind_the_fixed_fake_getter_artifact(string fault)
    {
        var artifact = ReadArtifact(); var json = artifact.Scope.GetRawText();
        var mutation = fault switch
        {
            "shared-user" => Replace(json, Ids[1][0], Ids[0][0]),
            "shared-session" => Replace(json, Ids[1][3], Ids[0][3]),
            "session-order" => Replace(Replace(json, Ids[0][3], "TEMP"), Ids[0][4], Ids[0][3]).Replace("TEMP", Ids[0][4], StringComparison.Ordinal),
            "crossed-pet" => Replace(json, Ids[0][1], Ids[1][1]),
            "owner" => Replace(json, Owner, Epoch),
            "epoch" => Replace(json, Epoch, Owner),
            "configuration" => Replace(json, "#FFE8C2", "#9ECBEF"),
            "extra-field" => json.Insert(1, "\"Unexpected\":1,"),
            "actor-order" => Replace(json, "\"Actor\":\"a\"", "\"Actor\":\"b\""),
            "noninteger-version" => Replace(json, "\"Version\":1,", "\"Version\":1.5,"),
            "string-profile-version" => Replace(json, "\"ProfileVersion\":2", "\"ProfileVersion\":\"2\""),
            "null-session" => Replace(json, "\"" + Ids[0][3] + "\"", "null"),
            "duplicate-field" => json.Insert(1, "\"Version\":1,"),
            _ => throw new AssertFailedException("Unknown mutation")
        };
        using var document = JsonDocument.Parse(mutation); using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        Assert.ThrowsExactly<InvalidDataException>(() => Check(artifact.Sql, document.RootElement, db.Model));
    }

    private sealed record Report(IReadOnlyDictionary<string, Dictionary<string, string>> Scopes, HashSet<string> Columns);
    private static Report Check(string sql, JsonElement scope, IModel model)
    {
        VerifyScope(scope);
        if (sql.Length is < 1 or > 16384 || sql.Any(c => c > 127) || Normalize(sql) != Normalize(ExpectedSql())) throw Reject();
        var scopes = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal); var columns = new HashSet<string>(StringComparer.Ordinal);
        var tables = model.GetEntityTypes().Where(e => e.GetSchema() == "dango" && e.GetTableName() is not null)
            .GroupBy(e => e.GetTableName()!, StringComparer.Ordinal).ToDictionary(g => g.Key,
                g => g.SelectMany(e => e.GetProperties().Select(p => p.GetColumnName(StoreObjectIdentifier.Table(g.Key, "dango"))))
                    .Where(name => name is not null).Select(name => name!).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var expectedFields = new HashSet<string>(["actor", "user_id", "pet_id", "room_id", "session_one", "session_two", "email", "nickname", "profile_version", "configuration_version", "configuration"], StringComparer.Ordinal);
        foreach (Match count in Regex.Matches(sql, @"\(SELECT count\(\*\) (?<body>.*?)\) AS (?<scope>[a-z_]+)", RegexOptions.Singleline))
        {
            var name = count.Groups["scope"].Value; var body = count.Groups["body"].Value;
            if (!ScopeNames.Contains(name, StringComparer.Ordinal)) throw Reject();
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match table in Regex.Matches(body, @"\b(?:FROM|JOIN) dango\.(?<table>[a-z_]+) (?<alias>[a-z]+)\b"))
                if (!tables.ContainsKey(table.Groups["table"].Value) || !aliases.TryAdd(table.Groups["alias"].Value, table.Groups["table"].Value)) throw Reject();
            foreach (Match reference in Regex.Matches(body, @"(?<![a-z_.])(?<alias>[a-z]+)\.(?<column>[a-z_]+)\b"))
            {
                var alias = reference.Groups["alias"].Value; var column = reference.Groups["column"].Value;
                if (alias == "e") { if (!expectedFields.Contains(column)) throw Reject(); continue; }
                if (alias == "run") { if (column != "epoch") throw Reject(); continue; }
                if (alias == "dango") continue;
                if (!aliases.TryGetValue(alias, out var table) || !tables[table].Contains(column)) throw Reject();
                columns.Add(table + "." + column);
            }
            if (!scopes.TryAdd(name, aliases)) throw Reject();
        }
        if (!scopes.Keys.Order(StringComparer.Ordinal).SequenceEqual(ScopeNames.Order(StringComparer.Ordinal), StringComparer.Ordinal)) throw Reject();
        return new(scopes, columns);
    }

    private static void VerifyScope(JsonElement scope)
    {
        try { VerifyCanonicalScope(scope); }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw Reject(); }
    }

    private static void VerifyCanonicalScope(JsonElement scope)
    {
        Shape(scope, "Version", "OwnerId", "RecoveryEpoch", "Accounts");
        if (scope.GetProperty("Version").GetInt64() != 1 || scope.GetProperty("OwnerId").GetString() != Owner || scope.GetProperty("RecoveryEpoch").GetString() != Epoch) throw Reject();
        var accounts = scope.GetProperty("Accounts"); if (accounts.ValueKind != JsonValueKind.Array || accounts.GetArrayLength() != 2) throw Reject();
        for (var i = 0; i < 2; i++)
        {
            var account = accounts[i]; Shape(account, "Actor", "UserId", "PetId", "DefaultRoomId", "SessionIds", "Nickname", "ProfileVersion", "ConfigurationVersion", "Configuration");
            if (account.GetProperty("Actor").GetString() != (i == 0 ? "a" : "b") || account.GetProperty("UserId").GetString() != Ids[i][0]
                || account.GetProperty("PetId").GetString() != Ids[i][1] || account.GetProperty("DefaultRoomId").GetString() != Ids[i][2]
                || account.GetProperty("Nickname").GetString() != (i == 0 ? "旧账号团子" : "新账号团子")
                || account.GetProperty("ProfileVersion").GetInt64() != 2 || account.GetProperty("ConfigurationVersion").GetInt64() != i + 2) throw Reject();
            var sessions = account.GetProperty("SessionIds");
            if (sessions.ValueKind != JsonValueKind.Array || sessions.GetArrayLength() != 2 || !sessions.EnumerateArray().Select(s => s.GetString()).SequenceEqual(Ids[i][3..])) throw Reject();
            var configuration = account.GetProperty("Configuration"); Shape(configuration, ConfigurationFields);
            using var expected = JsonDocument.Parse(i == 0 ? ConfigurationA : ConfigurationB);
            foreach (var field in ConfigurationFields)
            {
                var value = configuration.GetProperty(field); var expectedValue = expected.RootElement.GetProperty(field);
                if (value.ValueKind != expectedValue.ValueKind || (value.ValueKind == JsonValueKind.String ? value.GetString() != expectedValue.GetString() : value.GetDouble() != expectedValue.GetDouble())) throw Reject();
            }
        }
    }

    private static void Shape(JsonElement element, params string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Reject();
        var actual = element.EnumerateObject().Select(p => p.Name).ToArray();
        if (actual.Length != keys.Length || !actual.Order(StringComparer.Ordinal).SequenceEqual(keys.Order(StringComparer.Ordinal), StringComparer.Ordinal)) throw Reject();
    }
    private static string Normalize(string sql) => sql.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    private static string Replace(string input, string before, string after)
    {
        Assert.IsTrue(input.Contains(before, StringComparison.Ordinal), "Actual getter artifact lacks the mutation target.");
        return input.Replace(before, after, StringComparison.Ordinal);
    }
    private static InvalidDataException Reject() => new("accountsync_b1c_sql_metadata_rejected");

    private sealed record Artifact(JsonElement Scope, string Sql);
    private static Artifact ReadArtifact()
    {
        var configured = Environment.GetEnvironmentVariable("DANGO_SQL_B1C_REVIEW_PATH");
        if (string.IsNullOrWhiteSpace(configured)) Assert.Inconclusive("Set DANGO_SQL_B1C_REVIEW_PATH to the actual B1c getter artifact.");
        var path = Path.GetFullPath(configured!); DirectoryInfo? root = new(Directory.GetCurrentDirectory());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root); var artifactRoot = Path.Combine(root.FullName, "artifacts", "account-sync-b1c1-offline"); var relative = Path.GetRelativePath(artifactRoot, path);
        Assert.IsFalse(Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.AreEqual("actual-b1c-scoped-sql.json", Path.GetFileName(path));
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Assert.AreEqual(2, parts.Length);
        Assert.IsTrue(Guid.TryParseExact(parts[0], "N", out var directoryId) && directoryId != Guid.Empty
            && parts[0] == directoryId.ToString("N"));
        for (FileSystemInfo? part = new FileInfo(path); part is not null; part = part is FileInfo file ? file.Directory : ((DirectoryInfo)part).Parent)
        {
            Assert.IsFalse(part.Attributes.HasFlag(FileAttributes.ReparsePoint));
            if (string.Equals(part.FullName, artifactRoot, StringComparison.OrdinalIgnoreCase)) break;
        }
        Assert.IsTrue(new FileInfo(path).Length is > 0 and <= 131072); var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes), new() { MaxDepth = 16, AllowDuplicateProperties = false });
        Shape(document.RootElement, "Version", "Scope", "Sql"); Assert.AreEqual(1, document.RootElement.GetProperty("Version").GetInt32());
        return new(document.RootElement.GetProperty("Scope").Clone(), document.RootElement.GetProperty("Sql").GetString() ?? throw Reject());
    }

    private static string ExpectedSql()
    {
        var rows = Enumerable.Range(0, 2).Select(i => $"('{(i == 0 ? "a" : "b")}','{Ids[i][0]}'::uuid,'{Ids[i][1]}'::uuid,'{Ids[i][2]}'::uuid,'{Ids[i][3]}'::uuid,'{Ids[i][4]}'::uuid,'accountsyncb1c-{Guid.Parse(Owner):N}-{(i == 0 ? "a" : "b")}@example.invalid',{(i == 0 ? @"U&'\65E7\8D26\53F7\56E2\5B50'" : @"U&'\65B0\8D26\53F7\56E2\5B50'")},2,{i + 2},'{(i == 0 ? ConfigurationA : ConfigurationB)}'::jsonb)");
        return $$"""
BEGIN READ ONLY;
WITH run(owner_id,epoch) AS (VALUES ('{{Owner}}'::uuid,'{{Epoch}}'::uuid)),
expected(actor,user_id,pet_id,room_id,session_one,session_two,email,nickname,profile_version,configuration_version,configuration) AS (VALUES {{string.Join(',', rows)}}),
facts AS (SELECT e.actor,
 (SELECT count(*) FROM dango.users u WHERE u.id=e.user_id) AS users,
 (SELECT count(*) FROM dango.users u WHERE u.id=e.user_id AND u.email=e.email AND u.normalized_email=upper(e.email) AND u.email_confirmed AND u.disabled_at IS NULL AND NOT u.recovery_locked) AS valid_users,
 (SELECT count(*) FROM dango.users u WHERE u.id=e.user_id AND u.nickname=e.nickname AND u.profile_version=e.profile_version) AS named_users,
 (SELECT count(*) FROM dango.users u
 JOIN dango.pets p ON p.user_id=u.id AND p.id=e.pet_id AND p.configuration_version=e.configuration_version AND p.configuration=e.configuration
 JOIN dango.rooms r ON r.owner_id=u.id AND r.id=e.room_id AND r.kind=0 AND r.status=0 AND r.version=1 AND r.capacity=2 AND r.template_id='desktop-pet-default' AND r.template_version=1 AND r.admission_generation=1
 JOIN dango.room_memberships m ON m.room_id=r.id AND m.user_id=u.id AND m.role=0
 JOIN dango.pet_room_states s ON s.pet_id=p.id AND s.room_id=r.id AND s.version=1 AND s.control_generation=0 AND s.control_lease_id IS NULL AND s.position_x=0 AND s.position_y=0 AND NOT s.is_sleeping
 JOIN dango.pending_registrations pr ON pr.user_id=u.id AND pr.pet_id=p.id AND pr.room_id=r.id AND pr.state=2 AND pr.completed_at IS NOT NULL
 JOIN dango.test_invitations i ON i.id=pr.invitation_id AND i.user_id=u.id AND i.consumed_at IS NOT NULL AND i.revoked_at IS NULL
 JOIN dango.email_challenges c ON c.pending_registration_id=pr.id AND c.purpose=0 AND c.consumed_at IS NOT NULL AND c.revoked_at IS NULL AND c.generation=pr.generation
 JOIN dango.email_jobs j ON j.challenge_id=c.id
 WHERE u.id=e.user_id AND pr.normalized_email=u.normalized_email AND i.normalized_email=u.normalized_email AND c.normalized_email=u.normalized_email
 AND pr.recovery_epoch=run.epoch AND i.recovery_epoch=run.epoch AND c.recovery_epoch=run.epoch) AS complete_graphs,
 (SELECT count(*) FROM dango.email_jobs j JOIN dango.email_challenges c ON c.id=j.challenge_id JOIN dango.pending_registrations pr ON pr.id=c.pending_registration_id
 WHERE pr.user_id=e.user_id AND pr.pet_id=e.pet_id AND pr.room_id=e.room_id AND pr.recovery_epoch=run.epoch
 AND j.state=0 AND j.attempts=0 AND j.accepted_at IS NULL AND j.lease_id IS NULL AND j.lease_until IS NULL AND j.failure_code IS NULL) AS pending_unsent_emails,
 (SELECT count(*) FROM dango.auth_sessions s WHERE s.user_id=e.user_id) AS sessions,
 (SELECT count(*) FROM dango.auth_sessions s JOIN dango.users u ON u.id=s.user_id WHERE s.user_id=e.user_id AND s.security_stamp=u.security_stamp AND s.recovery_epoch=run.epoch) AS valid_sessions,
 (SELECT count(*) FROM dango.auth_sessions s WHERE s.user_id=e.user_id AND s.revoked_at IS NOT NULL) AS revoked_sessions,
 (SELECT count(*) FROM dango.auth_sessions s JOIN dango.users u ON u.id=s.user_id WHERE s.user_id=e.user_id AND s.id IN (e.session_one,e.session_two)
 AND s.security_stamp=u.security_stamp AND s.recovery_epoch=run.epoch AND s.revoked_at IS NOT NULL) AS exact_sessions
 FROM expected e CROSS JOIN run)
SELECT json_object_agg(actor,json_build_object('users',users,'validUsers',valid_users,'namedUsers',named_users,'completeGraphs',complete_graphs,
 'pendingUnsentEmails',pending_unsent_emails,'sessions',sessions,'validSessions',valid_sessions,'revokedSessions',revoked_sessions,'exactSessions',exact_sessions)) FROM facts;
COMMIT;
""";
    }
}
