using System.Globalization;
using System.Text;
using System.Text.Json;
using Dango.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Configuration;

// This is an offline check of the actual review-query artifact and the formal EF model.
// It neither opens a database connection nor claims to execute PostgreSQL syntax.
[TestClass]
[DoNotParallelize]
[TestCategory("OfflineAccountSyncScopedSql")]
public sealed class AccountSyncScopedSqlMetadataTests
{
    private static readonly Guid ClosureUser = Guid.Parse("c0179320-d30f-421f-a43a-7fd83f72999b");
    private static readonly Guid ClosurePet = Guid.Parse("aac9ef69-694d-49bf-940d-3fc0d5992cbd");
    private static readonly Guid ClosureRoom = Guid.Parse("c317c4f6-07a6-4cf2-8bde-8ca5e9fc4d8f");
    private static readonly Guid ClosureOwner = Guid.Parse("4fd0debb-576b-4410-a8da-f8f434a2e390");
    private static readonly Guid ClosureEpoch = Guid.Parse("67559361-c6b5-487d-b4da-948bb049000a");
    private static readonly Guid WrongIdentity = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestMethod]
    public void Actual_generated_query_resolves_all_columns_in_the_formal_model_without_a_connection()
    {
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var report = AccountSyncScopedSqlReview.Check(ReadArtifact(), db.Model);
        Assert.AreEqual(8, report.Scopes.Count);
        Assert.AreEqual(60, report.Columns.Count);
        Assert.AreEqual(10, report.Columns.Select(column => column.Split('.')[0]).Distinct().Count());
        Assert.AreEqual(72, report.ScopedColumns.Count, "71 scalar references plus the selected CTE's users.id.");
        Assert.IsNull(db.Database.GetConnectionString());
        Assert.AreEqual(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [TestMethod]
    public void Actual_scope_aliases_and_selected_cte_inheritance_are_checked_separately()
    {
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var report = AccountSyncScopedSqlReview.Check(ReadArtifact(), db.Model);
        Assert.AreEqual("pet_room_states", report.Scopes["completeGraphs"]["s"]);
        foreach (var scope in new[] { "sessions", "validSessions", "revokedSessions" })
            Assert.AreEqual("auth_sessions", report.Scopes[scope]["s"]);
        foreach (var scope in new[] { "users", "validUsers", "namedUsers" })
            Assert.AreEqual("users", report.Scopes[scope]["selected"]);
        foreach (var scope in new[] { "completeGraphs", "pendingUnsentEmails", "sessions", "validSessions", "revokedSessions" })
            Assert.AreEqual("users", report.Scopes[scope]["u"]);
        Assert.IsTrue(report.Columns.Contains("users.nickname"));
        Assert.IsTrue(report.Columns.Contains("pet_room_states.control_generation"));
        Assert.IsTrue(report.Columns.Contains("auth_sessions.security_stamp"));
    }

    [TestMethod]
    public void Actual_nickname_literal_is_ascii_transport_with_the_required_unicode_value()
    {
        var sql = ReadArtifact();
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var report = AccountSyncScopedSqlReview.Check(sql, db.Model);
        Assert.AreEqual("新同步团子", report.Nickname);
        CollectionAssert.AreEqual(new[] { 0x65B0, 0x540C, 0x6B65, 0x56E2, 0x5B50 },
            report.Nickname.EnumerateRunes().Select(rune => rune.Value).ToArray());
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(sql), new UTF8Encoding(false, true).GetBytes(sql));
    }

    [TestMethod]
    public void Actual_frozen_query_with_a_raw_chinese_literal_is_rejected()
    {
        var sql = ReadArtifact("SourceScopedSql");
        Assert.IsTrue(sql.Contains("nickname='新同步团子'", StringComparison.Ordinal),
            "The negative artifact must be the old generated query, not an unrelated file.");
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        Assert.ThrowsExactly<InvalidDataException>(() => AccountSyncScopedSqlReview.Check(sql, db.Model));
    }

    [TestMethod]
    public void Actual_closure_query_checks_fourteen_keys_history_order_snapshot_and_fixed_identities()
    {
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var history = db.GetService<IHistoryRepository>().GetCreateScript();
        Assert.IsTrue(history.Contains("dango.__ef_migrations_history", StringComparison.Ordinal));
        Assert.IsTrue(history.Contains("\"" + nameof(HistoryRow.MigrationId) + "\"", StringComparison.Ordinal));
        var sql = ReadArtifact("ClosureSql");
        var report = CheckClosure(sql, db.Model, history);
        Assert.AreEqual(14, report.Keys.Count);
        Assert.AreEqual(8, report.Scopes.Count);
        Assert.AreEqual(60, report.Columns.Count);
        Assert.AreEqual(72, report.ScopedColumns.Count);
        Assert.AreEqual(ClosureUser, report.UserId);
        Assert.AreEqual(ClosurePet, report.PetId);
        Assert.AreEqual(ClosureRoom, report.RoomId);
        Assert.AreEqual(ClosureOwner, report.SourceOwnerId);
        Assert.AreEqual(ClosureEpoch, report.RecoveryEpoch);

        var assembly = db.GetService<IMigrationsAssembly>();
        Assert.IsNotNull(assembly.ModelSnapshot);
        var snapshot = CheckClosure(sql, assembly.ModelSnapshot.Model, history);
        CollectionAssert.AreEqual(report.Columns.Order(StringComparer.Ordinal).ToArray(), snapshot.Columns.Order(StringComparer.Ordinal).ToArray());
        var migrations = assembly.Migrations.Keys.ToArray();
        CollectionAssert.AreEqual(migrations.Order(StringComparer.Ordinal).ToArray(), migrations);
        Assert.AreEqual("20261010055529_AccountProfile", migrations[^1]);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(sql), new UTF8Encoding(false, true).GetBytes(sql));
        Assert.IsNull(db.Database.GetConnectionString());
        Assert.AreEqual(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [TestMethod]
    [DataRow("missing-pet-id")]
    [DataRow("missing-room-id")]
    [DataRow("wrong-pet-id")]
    [DataRow("wrong-room-id")]
    [DataRow("wrong-user-id")]
    [DataRow("unquoted-history-column")]
    [DataRow("wrong-history-column-case")]
    [DataRow("missing-history-order")]
    [DataRow("descending-history-order")]
    [DataRow("wrong-history-schema")]
    [DataRow("missing-metadata-key")]
    [DataRow("unknown-metadata-key")]
    [DataRow("unknown-closure-column")]
    [DataRow("missing-read-only")]
    [DataRow("extra-statement")]
    [DataRow("wrong-owner")]
    [DataRow("wrong-epoch")]
    [DataRow("missing-registration-epoch")]
    [DataRow("missing-session-epoch")]
    public void Mutations_of_actual_closure_query_cannot_drop_identity_or_metadata_contracts(string fault)
    {
        var sql = ReadArtifact("ClosureSql");
        var pet = " AND p.id='" + ClosurePet.ToString("D") + "'::uuid";
        var room = " AND r.id='" + ClosureRoom.ToString("D") + "'::uuid";
        var mutation = fault switch
        {
            "missing-pet-id" => Replace(sql, pet, ""),
            "missing-room-id" => Replace(sql, room, ""),
            "wrong-pet-id" => Replace(sql, ClosurePet.ToString("D"), WrongIdentity.ToString("D")),
            "wrong-room-id" => Replace(sql, ClosureRoom.ToString("D"), WrongIdentity.ToString("D")),
            "wrong-user-id" => Replace(sql, ClosureUser.ToString("D"), WrongIdentity.ToString("D")),
            "unquoted-history-column" => Replace(sql, "\"MigrationId\"", "MigrationId"),
            "wrong-history-column-case" => Replace(sql, "\"MigrationId\"", "\"migration_id\""),
            "missing-history-order" => Replace(sql, " ORDER BY \"MigrationId\"", ""),
            "descending-history-order" => Replace(sql, "ORDER BY \"MigrationId\"", "ORDER BY \"MigrationId\" DESC"),
            "wrong-history-schema" => Replace(sql, "dango.__ef_migrations_history", "public.__ef_migrations_history"),
            "missing-metadata-key" => Replace(sql, "'database',current_database(),", ""),
            "unknown-metadata-key" => Replace(sql, "'dataDirectory'", "'otherDirectory'"),
            "unknown-closure-column" => Replace(sql, "p.configuration_version", "p.not_a_column"),
            "missing-read-only" => Replace(sql, "BEGIN READ ONLY;", "BEGIN;"),
            "extra-statement" => sql + "DELETE FROM dango.users;",
            "wrong-owner" => Replace(sql, "accountsync-" + ClosureOwner.ToString("N") + "@example.invalid",
                "accountsync-" + WrongIdentity.ToString("N") + "@example.invalid"),
            "wrong-epoch" => Replace(sql, ClosureEpoch.ToString("D"), WrongIdentity.ToString("D")),
            "missing-registration-epoch" => Replace(sql, " AND pr.recovery_epoch='" + ClosureEpoch.ToString("D") + "'::uuid", ""),
            "missing-session-epoch" => Replace(sql, " AND s.recovery_epoch='" + ClosureEpoch.ToString("D") + "'::uuid", ""),
            _ => throw new AssertFailedException("Unknown controlled closure mutation.")
        };
        Assert.AreNotEqual(sql, mutation);
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var history = db.GetService<IHistoryRepository>().GetCreateScript();
        Assert.ThrowsExactly<InvalidDataException>(() => CheckClosure(mutation, db.Model, history));
    }

    private static AccountSyncScopedSqlReport CheckClosure(string sql, IModel model, string history) =>
        AccountSyncScopedSqlReview.CheckClosure(sql, model, history, ClosureUser, ClosurePet, ClosureRoom, ClosureOwner, ClosureEpoch);

    [TestMethod]
    [DataRow("wrong-codepoint")]
    [DataRow("truncated-unicode")]
    [DataRow("raw-chinese")]
    [DataRow("non-ascii-comment")]
    [DataRow("unknown-table")]
    [DataRow("unknown-column")]
    [DataRow("unknown-alias")]
    [DataRow("wrong-schema")]
    [DataRow("duplicate-alias")]
    [DataRow("wrong-scope-alias")]
    [DataRow("unknown-unqualified-column")]
    [DataRow("wrong-unicode-prefix")]
    [DataRow("missing-read-only")]
    [DataRow("missing-commit")]
    [DataRow("extra-statement")]
    [DataRow("unknown-key")]
    [DataRow("scalar-not-count")]
    [DataRow("extra-cte")]
    [DataRow("scope-comment")]
    [DataRow("unscoped-users")]
    [DataRow("cross-scope-column")]
    [DataRow("keyword-in-predicate")]
    [DataRow("nested-select")]
    public void Mutations_of_the_actual_generated_query_fail_closed(string fault)
    {
        var sql = ReadArtifact();
        var mutation = fault switch
        {
            "wrong-codepoint" => Replace(sql, @"\65B0", @"\65B1"),
            "truncated-unicode" => Replace(sql, @"\5B50", @"\5B5"),
            "raw-chinese" => Replace(sql, AccountSyncScopedSqlReview.NicknameLiteral, "'新同步团子'"),
            "non-ascii-comment" => sql + "\n-- 中",
            "unknown-table" => Replace(sql, "dango.pets p", "dango.not_a_table p"),
            "unknown-column" => Replace(sql, "p.configuration_version", "p.not_a_column"),
            "unknown-alias" => Replace(sql, "p.user_id=u.id", "unknown.user_id=u.id"),
            "wrong-schema" => Replace(sql, "dango.pets p", "public.pets p"),
            "duplicate-alias" => Replace(sql, "dango.rooms r", "dango.rooms p"),
            "wrong-scope-alias" => Replace(sql, "dango.auth_sessions s", "dango.pet_room_states s"),
            "unknown-unqualified-column" => Replace(sql, "email_confirmed AND", "not_a_column AND"),
            "wrong-unicode-prefix" => Replace(sql, "nickname=U&", "nickname=U"),
            "missing-read-only" => Replace(sql, "BEGIN READ ONLY;", "BEGIN;"),
            "missing-commit" => Replace(sql, "COMMIT;", ""),
            "extra-statement" => sql + "DELETE FROM dango.users;",
            "unknown-key" => Replace(sql, "'validUsers'", "'otherUsers'"),
            "scalar-not-count" => Replace(sql, "SELECT count(*)", "SELECT sum(*)"),
            "extra-cte" => Replace(sql, "SELECT json_build_object", ", other AS (SELECT * FROM dango.users) SELECT json_build_object"),
            "scope-comment" => Replace(sql, "JOIN dango.pets", "JOIN /* discarded */ dango.pets"),
            "unscoped-users" => Replace(sql, "FROM selected u", "FROM dango.users u"),
            "cross-scope-column" => Replace(sql, "s.security_stamp=u.security_stamp", "s.control_lease_id=u.security_stamp"),
            "keyword-in-predicate" => Replace(sql, "email_confirmed AND", "email_confirmed AND uuid AND"),
            "nested-select" => Replace(sql, "p.configuration_version=6", "p.configuration_version=(SELECT 6)"),
            _ => throw new AssertFailedException("Unknown controlled SQL mutation.")
        };
        Assert.AreNotEqual(sql, mutation, "Every negative case must actually alter the generated query.");
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        Assert.ThrowsExactly<InvalidDataException>(() => AccountSyncScopedSqlReview.Check(mutation, db.Model));
    }

    private static string Replace(string sql, string before, string after)
    {
        Assert.IsTrue(sql.Contains(before, StringComparison.Ordinal), "The actual generator no longer contains the negative case's target.");
        return sql.Replace(before, after, StringComparison.Ordinal);
    }

    private static string ReadArtifact(string field = "FixedScopedSql")
    {
        var configured = Environment.GetEnvironmentVariable("DANGO_SQL_REVIEW_PATH");
        if (string.IsNullOrWhiteSpace(configured))
            Assert.Inconclusive("Set DANGO_SQL_REVIEW_PATH to this task's actual generated SQL review artifact.");
        var path = Path.GetFullPath(configured!);
        var root = FindRepositoryRoot();
        var artifactRoot = Path.Combine(root, "artifacts");
        var relative = Path.GetRelativePath(artifactRoot, path);
        Assert.IsFalse(Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.AreEqual(".json", Path.GetExtension(path), "Only task SQL review artifacts are read.");
        for (var current = new FileInfo(path) as FileSystemInfo; current is not null; current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
        {
            Assert.IsFalse(current.Attributes.HasFlag(FileAttributes.ReparsePoint), "Artifact links are not followed.");
            if (string.Equals(current.FullName, artifactRoot, StringComparison.OrdinalIgnoreCase)) break;
        }
        Assert.IsTrue(new FileInfo(path).Length is > 0 and <= 262144);
        var bytes = File.ReadAllBytes(path);
        Assert.IsTrue(bytes.Length is > 0 and <= 262144);
        using var artifact = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes));
        foreach (var name in new[] { "SourceScopedSql", "FixedScopedSql", "ClosureSql" })
            Assert.IsTrue(artifact.RootElement.TryGetProperty(name, out var sql) && sql.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(sql.GetString()), "Missing actual generated SQL field: " + name);
        return artifact.RootElement.GetProperty(field).GetString()!;
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                    && File.Exists(Path.Combine(directory.FullName, "eng", "Dango.AccountSyncPostgres.psm1"))) return directory.FullName;
        throw new AssertFailedException("The SQL review repository root was not found.");
    }
}

internal sealed record AccountSyncScopedSqlReport(
    IReadOnlyDictionary<string, Dictionary<string, string>> Scopes,
    IReadOnlySet<string> Columns, IReadOnlySet<string> ScopedColumns, string Nickname,
    IReadOnlyList<string> Keys, Guid UserId, Guid? PetId, Guid? RoomId, Guid? SourceOwnerId, Guid? RecoveryEpoch);

// Deliberately restricted to Get-DangoAccountSyncScopedSql's one CTE and eight count scopes.
// A lexical/metadata review is not a general SQL parser or a PostgreSQL execution substitute.
internal static class AccountSyncScopedSqlReview
{
    internal const string NicknameLiteral = @"U&'\65B0\540C\6B65\56E2\5B50'";
    private static readonly string[] Keys = ["users", "validUsers", "namedUsers", "completeGraphs", "pendingUnsentEmails", "sessions", "validSessions", "revokedSessions"];
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
        { "SELECT", "FROM", "JOIN", "ON", "WHERE", "AND", "NOT", "IS", "NULL", "count", "json", "numeric", "uuid" };
    private static readonly Dictionary<string, string[]> Relations = new(StringComparer.Ordinal)
    {
        ["selected"] = ["users:dango.users"],
        ["users"] = ["selected:selected"], ["validUsers"] = ["selected:selected"], ["namedUsers"] = ["selected:selected"],
        ["completeGraphs"] = ["u:selected", "p:dango.pets", "r:dango.rooms", "m:dango.room_memberships", "s:dango.pet_room_states", "pr:dango.pending_registrations", "i:dango.test_invitations", "c:dango.email_challenges", "e:dango.email_jobs"],
        ["pendingUnsentEmails"] = ["e:dango.email_jobs", "c:dango.email_challenges", "pr:dango.pending_registrations", "u:selected"],
        ["sessions"] = ["s:dango.auth_sessions", "u:selected"], ["validSessions"] = ["s:dango.auth_sessions", "u:selected"], ["revokedSessions"] = ["s:dango.auth_sessions", "u:selected"]
    };

    internal static AccountSyncScopedSqlReport Check(string sql, IModel model) =>
        CheckCore(sql, model, null, null, null, null, null, null);

    internal static AccountSyncScopedSqlReport CheckClosure(string sql, IModel model, string history,
        Guid user, Guid pet, Guid room, Guid owner, Guid epoch) => CheckCore(sql, model, history, user, pet, room, owner, epoch);

    private static AccountSyncScopedSqlReport CheckCore(string sql, IModel model, string? history,
        Guid? expectedUser, Guid? expectedPet, Guid? expectedRoom, Guid? expectedOwner, Guid? expectedEpoch)
    {
        if (sql.Length is 0 or > 16384 || sql.Any(character => character > 127
            || (char.IsControl(character) && character is not ('\r' or '\n' or '\t')))) throw Reject();
        var metadata = model.GetEntityTypes().Where(entity => entity.GetTableName() is not null)
            .GroupBy(entity => (Schema: entity.GetSchema(), Table: entity.GetTableName()!))
            .ToDictionary(group => group.Key, group => group.SelectMany(entity => entity.GetProperties()
                .Select(property => property.GetColumnName(StoreObjectIdentifier.Table(group.Key.Table, group.Key.Schema))))
                .Where(column => column is not null).Select(column => column!).ToHashSet(StringComparer.Ordinal));
        var closure = history is not null;
        if (closure && (expectedUser is null || expectedUser == Guid.Empty || expectedPet is null
            || expectedPet == Guid.Empty || expectedRoom is null || expectedRoom == Guid.Empty
            || expectedOwner is null || expectedOwner == Guid.Empty || expectedEpoch is null || expectedEpoch == Guid.Empty)) throw Reject();
        var tokens = Tokenize(sql, closure);
        var reader = new Reader(tokens);
        reader.Expect("BEGIN", "READ", "ONLY", ";", "WITH", "selected", "AS");
        var cte = reader.Scope();
        var cteReader = new Reader(cte);
        cteReader.Expect("SELECT", "*", "FROM", "dango", ".", "users", "WHERE", "id", "=");
        var userId = cteReader.String();
        if (!Guid.TryParseExact(userId, "D", out var id) || id == Guid.Empty) throw Reject();
        if (closure && id != expectedUser) throw Reject();
        cteReader.Expect("::", "uuid"); cteReader.End();
        reader.Expect("SELECT", "json_build_object", "(");
        var keys = new List<string>();
        if (closure) ReadClosureMetadata(reader, history!, keys);
        var scopes = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var columns = new HashSet<string>(StringComparer.Ordinal);
        var scopedColumns = new HashSet<string>(StringComparer.Ordinal);
        ValidateScope("selected", cte, metadata, columns, scopedColumns);
        string? nickname = null;
        for (var index = 0; index < Keys.Length; index++)
        {
            if (index > 0) reader.Expect(",");
            var name = reader.String();
            if (name != Keys[index]) throw Reject();
            keys.Add(name);
            reader.Expect(",");
            var scope = reader.Scope();
            if (scope.Any(token => token.Kind == Kind.QuotedIdentifier || token.Text == ";")) throw Reject();
            var prefix = new Reader(scope); prefix.Expect("SELECT", "count", "(", "*", ")", "FROM");
            scopes.Add(name, ValidateScope(name, scope, metadata, columns, scopedColumns));
            if (closure && name == "completeGraphs")
            {
                RequireIdentity(scope, "pets", "p", "user_id", expectedPet!.Value);
                RequireIdentity(scope, "rooms", "r", "owner_id", expectedRoom!.Value);
                RequireEpoch(scope, "pr", expectedEpoch!.Value);
            }
            if (closure && name == "validUsers") RequireOwner(scope, expectedOwner!.Value);
            if (closure && name == "validSessions") RequireEpoch(scope, "s", expectedEpoch!.Value);
            if (name == "namedUsers")
            {
                var values = scope.Select((token, position) => (token, position))
                    .Where(pair => pair.token.Text == "nickname").ToArray();
                if (values.Length != 1 || values[0].position + 2 >= scope.Count
                    || scope[values[0].position + 1].Text != "=") throw Reject();
                var value = scope[values[0].position + 2];
                if (value.Kind != Kind.Unicode || value.Value != "新同步团子") throw Reject();
                nickname = value.Value;
            }
        }
        reader.Expect(")", ";", "COMMIT", ";"); reader.End();
        if (nickname is null || tokens.Count(token => token.Kind == Kind.Unicode) != 1) throw Reject();
        return new(scopes, columns, scopedColumns, nickname, keys, id, expectedPet, expectedRoom, expectedOwner, expectedEpoch);
    }

    private static void RequireOwner(List<Token> tokens, Guid expected)
    {
        var reader = new Reader(tokens);
        reader.Expect("SELECT", "count", "(", "*", ")", "FROM", "selected", "WHERE", "email", "=");
        if (reader.String() != "accountsync-" + expected.ToString("N") + "@example.invalid") throw Reject();
        reader.Expect("AND");
        if (tokens.Count(token => token.Text == "email") != 1) throw Reject();
    }

    private static void RequireEpoch(List<Token> tokens, string alias, Guid expected)
    {
        var matches = Enumerable.Range(0, tokens.Count - 3).Where(index => tokens[index].Text == alias
            && tokens[index + 1].Text == "." && tokens[index + 2].Text == "recovery_epoch" && tokens[index + 3].Text == "=").ToArray();
        if (matches.Length != 1 || matches[0] == 0 || tokens[matches[0] - 1].Text != "AND") throw Reject();
        var reader = new Reader(tokens.GetRange(matches[0], tokens.Count - matches[0]));
        reader.Expect(alias, ".", "recovery_epoch", "=");
        if (!Guid.TryParseExact(reader.String(), "D", out var actual) || actual != expected) throw Reject();
        reader.Expect("::", "uuid");
    }

    private static void ReadClosureMetadata(Reader reader, string history, List<string> keys)
    {
        if (!history.Contains("dango.__ef_migrations_history", StringComparison.Ordinal)
            || !history.Contains("\"" + nameof(HistoryRow.MigrationId) + "\"", StringComparison.Ordinal)) throw Reject();
        Key("database"); reader.Expect(",", "current_database", "(", ")", ",");
        Key("role"); reader.Expect(",", "current_user", ",");
        Key("version"); reader.Expect(",", "current_setting", "("); Value("server_version_num");
        reader.Expect(")", "::", "integer", ",");
        Key("listenAddresses"); reader.Expect(",", "current_setting", "("); Value("listen_addresses"); reader.Expect(")", ",");
        Key("dataDirectory"); reader.Expect(",", "current_setting", "("); Value("data_directory"); reader.Expect(")", ",");
        Key("migrations"); reader.Expect(",");
        var migrations = new Reader(reader.Scope());
        migrations.Expect("SELECT", "COALESCE", "(", "json_agg", "(");
        migrations.Quoted(nameof(HistoryRow.MigrationId)); migrations.Expect("ORDER", "BY");
        migrations.Quoted(nameof(HistoryRow.MigrationId)); migrations.Expect(")", ",");
        if (migrations.String() != "[]") throw Reject();
        migrations.Expect("::", "json", ")", "FROM", "dango", ".", "__ef_migrations_history");
        migrations.End(); reader.Expect(",");

        void Key(string expected) { Value(expected); keys.Add(expected); }
        void Value(string expected) { if (reader.String() != expected) throw Reject(); }
    }

    private static void RequireIdentity(List<Token> tokens, string table, string alias, string foreignColumn, Guid expected)
    {
        var joins = Enumerable.Range(0, tokens.Count - 4).Where(index => tokens[index].Text == "JOIN"
            && tokens[index + 1].Text == "dango" && tokens[index + 2].Text == "."
            && tokens[index + 3].Text == table && tokens[index + 4].Text == alias).ToArray();
        if (joins.Length != 1) throw Reject();
        var anchor = new Reader(tokens.GetRange(joins[0], tokens.Count - joins[0]));
        anchor.Expect("JOIN", "dango", ".", table, alias, "ON", alias, ".", foreignColumn,
            "=", "u", ".", "id", "AND", alias, ".", "id", "=");
        if (!Guid.TryParseExact(anchor.String(), "D", out var actual) || actual != expected) throw Reject();
        anchor.Expect("::", "uuid");
        if (Enumerable.Range(0, tokens.Count - 3).Count(index => tokens[index].Text == alias
            && tokens[index + 1].Text == "." && tokens[index + 2].Text == "id"
            && tokens[index + 3].Text == "=") != 1) throw Reject();
    }

    private static Dictionary<string, string> ValidateScope(string scope, List<Token> tokens,
        Dictionary<(string? Schema, string Table), HashSet<string>> metadata,
        HashSet<string> columns, HashSet<string> scopedColumns)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var declarations = new HashSet<int>();
        var ordered = new List<string>();
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Text is not ("FROM" or "JOIN")) continue;
            declarations.Add(index);
            var relation = ++index;
            if (relation >= tokens.Count || tokens[relation].Kind != Kind.Identifier) throw Reject();
            string table;
            if (tokens[relation].Text == "selected")
            {
                table = "users"; declarations.Add(relation);
            }
            else
            {
                if (relation + 2 >= tokens.Count || tokens[relation].Text != "dango"
                    || tokens[relation + 1].Text != "." || tokens[relation + 2].Kind != Kind.Identifier) throw Reject();
                table = tokens[relation + 2].Text;
                declarations.UnionWith([relation, relation + 1, relation + 2]);
                index += 2;
            }
            if (!metadata.ContainsKey(("dango", table))) throw Reject();
            var alias = tokens[relation].Text == "selected" ? "selected" : table;
            if (index + 1 < tokens.Count && tokens[index + 1].Kind == Kind.Identifier && !Keywords.Contains(tokens[index + 1].Text))
            {
                alias = tokens[++index].Text; declarations.Add(index);
            }
            if (!aliases.TryAdd(alias, table)) throw Reject();
            ordered.Add(alias + ":" + (tokens[relation].Text == "selected" ? "selected" : "dango." + table));
        }
        if (!ordered.SequenceEqual(Relations[scope], StringComparer.Ordinal)) throw Reject();
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (declarations.Contains(index)) continue;
            if (token.Kind != Kind.Identifier) continue;
            if (index + 1 < tokens.Count && tokens[index + 1].Text == ".")
            {
                if (index + 2 >= tokens.Count || tokens[index + 2].Kind != Kind.Identifier
                    || !aliases.TryGetValue(token.Text, out var table)) throw Reject();
                AddColumn(token.Text, table, tokens[index + 2].Text); index += 2;
            }
            else if (token.Text == "SELECT")
            {
                if (index != 0) throw Reject();
            }
            else if (token.Text == "count")
            {
                if (index != 1 || tokens[0].Text != "SELECT") throw Reject();
            }
            else if (token.Text is "json" or "numeric" or "uuid")
            {
                if (index == 0 || tokens[index - 1].Text != "::") throw Reject();
            }
            else if (token.Text is not ("ON" or "WHERE" or "AND" or "NOT" or "IS" or "NULL"))
            {
                if (aliases.Count != 1) throw Reject();
                var only = aliases.Single(); AddColumn(only.Key, only.Value, token.Text);
            }
        }
        return aliases;

        void AddColumn(string alias, string table, string column)
        {
            if (!metadata[("dango", table)].Contains(column)) throw Reject();
            columns.Add(table + "." + column);
            scopedColumns.Add(scope + ":" + alias + ":" + table + "." + column);
        }
    }

    private enum Kind { Identifier, Number, String, Unicode, QuotedIdentifier, Symbol }
    private sealed record Token(Kind Kind, string Text, string? Value = null);
    private static List<Token> Tokenize(string sql, bool allowQuotedIdentifiers)
    {
        var tokens = new List<Token>();
        for (var index = 0; index < sql.Length;)
        {
            var character = sql[index];
            if (char.IsWhiteSpace(character)) { index++; continue; }
            var start = index;
            if (character == '"')
            {
                if (!allowQuotedIdentifiers) throw Reject();
                index++;
                var valueStart = index;
                while (index < sql.Length && (char.IsAsciiLetterOrDigit(sql[index]) || sql[index] == '_')) index++;
                if (index == valueStart || index >= sql.Length || sql[index++] != '"') throw Reject();
                tokens.Add(new(Kind.QuotedIdentifier, sql[start..index], sql[valueStart..(index - 1)])); continue;
            }
            var unicode = character == 'U' && index + 2 < sql.Length && sql[index + 1] == '&' && sql[index + 2] == '\'';
            if (character == '\'' || unicode)
            {
                index += unicode ? 3 : 1;
                var value = new StringBuilder(); var closed = false;
                while (index < sql.Length)
                {
                    if (sql[index] == '\'')
                    {
                        index++;
                        if (!unicode && index < sql.Length && sql[index] == '\'') { value.Append('\''); index++; continue; }
                        closed = true; break;
                    }
                    if (unicode)
                    {
                        if (sql[index] != '\\' || index + 4 >= sql.Length
                            || !ushort.TryParse(sql.AsSpan(index + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codepoint)
                            || char.IsSurrogate((char)codepoint)) throw Reject();
                        value.Append((char)codepoint); index += 5;
                    }
                    else value.Append(sql[index++]);
                }
                if (!closed) throw Reject();
                tokens.Add(new(unicode ? Kind.Unicode : Kind.String, sql[start..index], value.ToString())); continue;
            }
            if (char.IsAsciiLetter(character) || character == '_')
            {
                while (++index < sql.Length && (char.IsAsciiLetterOrDigit(sql[index]) || sql[index] == '_')) { }
                tokens.Add(new(Kind.Identifier, sql[start..index])); continue;
            }
            if (char.IsAsciiDigit(character))
            {
                while (++index < sql.Length && char.IsAsciiDigit(sql[index])) { }
                tokens.Add(new(Kind.Number, sql[start..index])); continue;
            }
            if (index + 2 < sql.Length && sql.AsSpan(index, 3).SequenceEqual("->>")) index += 3;
            else if (index + 1 < sql.Length && sql.AsSpan(index, 2).SequenceEqual("::")) index += 2;
            else if (character is '(' or ')' or ',' or '.' or '=' or '*' or ';') index++;
            else throw Reject(); // Includes comments, quoted identifiers and extra operators.
            tokens.Add(new(Kind.Symbol, sql[start..index]));
        }
        return tokens;
    }

    private sealed class Reader(List<Token> tokens)
    {
        private int position;
        internal void Expect(params string[] values)
        {
            foreach (var value in values)
                if (position >= tokens.Count || !string.Equals(tokens[position++].Text, value, StringComparison.Ordinal)) throw Reject();
        }
        internal string String()
        {
            if (position >= tokens.Count || tokens[position].Kind != Kind.String) throw Reject();
            return tokens[position++].Value!;
        }
        internal void Quoted(string value)
        {
            if (position >= tokens.Count || tokens[position].Kind != Kind.QuotedIdentifier
                || tokens[position++].Value != value) throw Reject();
        }
        internal List<Token> Scope()
        {
            Expect("("); var start = position; var depth = 1;
            while (position < tokens.Count)
            {
                var token = tokens[position++];
                if (token.Kind == Kind.Symbol && token.Text == "(") depth++;
                else if (token.Kind == Kind.Symbol && token.Text == ")" && --depth == 0)
                    return tokens.GetRange(start, position - start - 1);
            }
            throw Reject();
        }
        internal void End() { if (position != tokens.Count) throw Reject(); }
    }

    private static InvalidDataException Reject() => new("accountsync_scoped_sql_review_rejected");
}
