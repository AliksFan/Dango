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

// Reads the actual PowerShell getter's artifact. No database or shell is invoked by these tests.
[TestClass]
[DoNotParallelize]
[TestCategory("OfflineAccountSyncB1bScopedSql")]
public sealed class AccountSyncB1bScopedSqlMetadataTests
{
    private const string Literal = @"U&'\5F02\5E38\540C\6B65\56E2\5B50'";
    private static readonly Guid User = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Pet = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Room = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Owner = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Epoch = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Wrong = Guid.Parse("99999999-9999-4999-8999-999999999999");

    [TestMethod]
    public void Actual_getter_matches_model_snapshot_and_all_five_fictional_identities()
    {
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var sql = ReadSql(); var report = Check(sql, db.Model);
        Assert.AreEqual(8, report.Keys.Count);
        Assert.AreEqual(8, report.Scopes.Count);
        Assert.AreEqual(60, report.Columns.Count);
        Assert.AreEqual(72, report.ScopedColumns.Count);
        Assert.AreEqual(10, report.Columns.Select(column => column.Split('.')[0]).Distinct().Count());
        Assert.AreEqual(User, report.UserId); Assert.AreEqual(Pet, report.PetId); Assert.AreEqual(Room, report.RoomId);
        Assert.AreEqual(Owner, report.SourceOwnerId); Assert.AreEqual(Epoch, report.RecoveryEpoch);
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.IsNotNull(snapshot);
        var second = Check(sql, snapshot.Model);
        CollectionAssert.AreEqual(report.Columns.Order(StringComparer.Ordinal).ToArray(), second.Columns.Order(StringComparer.Ordinal).ToArray());
        Assert.IsNull(db.Database.GetConnectionString());
        Assert.AreEqual(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [TestMethod]
    public void Actual_unicode_nickname_is_ascii_in_CP936_and_UTF8()
    {
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var sql = ReadSql(); var report = Check(sql, db.Model);
        Assert.AreEqual("异常同步团子", report.Nickname);
        CollectionAssert.AreEqual(new[] { 0x5F02, 0x5E38, 0x540C, 0x6B65, 0x56E2, 0x5B50 },
            report.Nickname.EnumerateRunes().Select(rune => rune.Value).ToArray());
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        CollectionAssert.AreEqual(Encoding.GetEncoding(936).GetBytes(sql), new UTF8Encoding(false, true).GetBytes(sql));
    }

    [TestMethod]
    public void Actual_recovery_eye_size_is_accepted_by_the_formal_appearance_contract()
    {
        var matches = Regex.Matches(ReadSql(), @"\(p\.configuration::json->>'eyeSizePercent'\)::numeric=(\d+)");
        Assert.AreEqual(1, matches.Count, "The actual getter must bind one final eye size.");
        var size = double.Parse(matches[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var configuration = JsonSerializer.SerializeToElement(new
        {
            appearanceSchemaVersion = AppearanceContract.SchemaVersion, bodyColorHex = "#AABBCC", eyeColorHex = "#DDEEFF",
            eyeSizePercent = size, eyeOvalityPercent = 0d, eyeHeightControlPercent = 51d, eyeSpacingControlPercent = 24.5d
        });
        var accepted = AppearanceContract.ValidateAndClone(configuration);
        Assert.AreEqual(size, accepted.GetProperty("eyeSizePercent").GetDouble());
    }

    [TestMethod]
    [DataRow("user")]
    [DataRow("pet")]
    [DataRow("room")]
    [DataRow("owner")]
    [DataRow("registration-epoch")]
    [DataRow("session-epoch")]
    [DataRow("missing-pet")]
    [DataRow("missing-room")]
    [DataRow("raw-chinese")]
    [DataRow("previous-nickname")]
    [DataRow("profile-version")]
    [DataRow("configuration-version")]
    [DataRow("configuration-value")]
    [DataRow("unknown-column")]
    [DataRow("wrong-table")]
    [DataRow("wrong-scope")]
    [DataRow("missing-readonly")]
    [DataRow("extra-write")]
    [DataRow("truncated")]
    public void Mutations_of_the_actual_generated_SQL_fail_before_any_database_access(string fault)
    {
        var sql = ReadSql();
        var mutation = fault switch
        {
            "user" => Replace(sql, User.ToString("D"), Wrong.ToString("D")),
            "pet" => Replace(sql, Pet.ToString("D"), Wrong.ToString("D")),
            "room" => Replace(sql, Room.ToString("D"), Wrong.ToString("D")),
            "owner" => Replace(sql, Owner.ToString("N"), Wrong.ToString("N")),
            "registration-epoch" => Replace(sql, "pr.recovery_epoch='" + Epoch.ToString("D") + "'", "pr.recovery_epoch='" + Wrong.ToString("D") + "'"),
            "session-epoch" => Replace(sql, "s.recovery_epoch='" + Epoch.ToString("D") + "'", "s.recovery_epoch='" + Wrong.ToString("D") + "'"),
            "missing-pet" => Replace(sql, " AND p.id='" + Pet.ToString("D") + "'::uuid", ""),
            "missing-room" => Replace(sql, " AND r.id='" + Room.ToString("D") + "'::uuid", ""),
            "raw-chinese" => Replace(sql, Literal, "'异常同步团子'"),
            "previous-nickname" => Replace(sql, Literal, AccountSyncScopedSqlReview.NicknameLiteral),
            "profile-version" => Replace(sql, "profile_version=2", "profile_version=3"),
            "configuration-version" => Replace(sql, "p.configuration_version=7", "p.configuration_version=8"),
            "configuration-value" => Replace(sql, "::numeric=139", "::numeric=138"),
            "unknown-column" => Replace(sql, "s.control_generation", "s.control_generations"),
            "wrong-table" => Replace(sql, "dango.pet_room_states s", "dango.pet_room_state s"),
            "wrong-scope" => Replace(sql, "p.user_id=u.id", "p.user_id=s.id"),
            "missing-readonly" => Replace(sql, "BEGIN READ ONLY;", "BEGIN;"),
            "extra-write" => sql + " DELETE FROM dango.users;",
            "truncated" => sql[..^5],
            _ => throw new AssertFailedException("Unknown mutation")
        };
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        Assert.ThrowsExactly<InvalidDataException>(() => Check(mutation, db.Model));
        Assert.AreEqual(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    private static AccountSyncScopedSqlReport Check(string sql, IModel model)
    {
        foreach (var anchor in new[] { Literal, "profile_version=2", "p.configuration_version=7", "::numeric=139" })
            if (Count(sql, anchor) != 1) throw new InvalidDataException("B1b fixed SQL value rejected.");
        return AccountSyncScopedSqlReview.CheckB1b(sql, model, User, Pet, Room, Owner, Epoch);
    }

    private static string Replace(string sql, string before, string after)
    {
        Assert.IsTrue(sql.Contains(before, StringComparison.Ordinal), "Actual getter is missing the mutation's target.");
        return sql.Replace(before, after, StringComparison.Ordinal);
    }
    private static int Count(string value, string part) => (value.Length - value.Replace(part, "", StringComparison.Ordinal).Length) / part.Length;

    private static string ReadSql()
    {
        var configured = Environment.GetEnvironmentVariable("DANGO_SQL_B1B_REVIEW_PATH");
        if (string.IsNullOrWhiteSpace(configured)) Assert.Inconclusive("Set DANGO_SQL_B1B_REVIEW_PATH to the actual getter artifact.");
        var path = Path.GetFullPath(configured!);
        DirectoryInfo? root = new(Directory.GetCurrentDirectory());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root);
        var artifactRoot = Path.Combine(root.FullName, "artifacts"); var relative = Path.GetRelativePath(artifactRoot, path);
        Assert.IsFalse(Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.AreEqual(".json", Path.GetExtension(path));
        for (FileSystemInfo? part = new FileInfo(path); part is not null; part = part is FileInfo file ? file.Directory : ((DirectoryInfo)part).Parent)
        {
            Assert.IsFalse(part.Attributes.HasFlag(FileAttributes.ReparsePoint));
            if (string.Equals(part.FullName, artifactRoot, StringComparison.OrdinalIgnoreCase)) break;
        }
        Assert.IsTrue(new FileInfo(path).Length is > 0 and <= 131072);
        var bytes = File.ReadAllBytes(path); Assert.IsTrue(bytes.Length is > 0 and <= 131072);
        using var document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes), new() { MaxDepth = 8, AllowDuplicateProperties = false });
        return document.RootElement.GetProperty("B1bScopedSql").GetString() ?? throw new AssertFailedException("Actual SQL is missing.");
    }
}
