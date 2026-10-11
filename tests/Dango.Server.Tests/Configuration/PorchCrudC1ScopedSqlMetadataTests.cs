using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dango.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Configuration;

[TestClass]
[TestCategory("OfflinePorchCrudC1Sql")]
public sealed class PorchCrudC1ScopedSqlMetadataTests
{
    [TestMethod]
    public void ActualGetterSqlResolvesAllTablesAndColumnsAgainstModelAndMigrationSnapshot()
    {
        var sql = ReadSql(); using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var columns = Check(sql, db.Model); var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!;
        CollectionAssert.AreEqual(columns.Order().ToArray(), Check(sql, snapshot.Model).Order().ToArray());
        foreach (var expected in new[] { "rooms.admission_generation", "audit_logs.request_hash", "domain_outbox.target_user_ids", "auth_sessions.security_stamp" }) Assert.Contains(expected, columns);
        Assert.AreEqual(12, columns.Select(c => c.Split('.')[0]).Distinct().Count());
        Assert.AreEqual(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
    [TestMethod]
    public void QueryUsesReadonlyAsciiTransportAndRejectsUnknownPhysicalColumn()
    {
        var sql = ReadSql(); using var db = new DangoDbContext(DangoDatabaseOptions.Create()); _ = Check(sql, db.Model);
        Assert.IsTrue(sql.Contains("BEGIN READ ONLY;", StringComparison.Ordinal) && sql.EndsWith("COMMIT;", StringComparison.Ordinal));
        Assert.IsTrue(sql.All(c => c <= 127)); Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        CollectionAssert.AreEqual(Encoding.GetEncoding(936).GetBytes(sql), Encoding.UTF8.GetBytes(sql));
        Assert.ThrowsExactly<InvalidDataException>(() => Check(sql.Replace("o.target_user_ids", "o.target_user_id", StringComparison.Ordinal), db.Model));
        Assert.ThrowsExactly<InvalidDataException>(() => Check(sql.Replace("dango.audit_logs", "dango.audit_log", StringComparison.Ordinal), db.Model));
    }
    private static HashSet<string> Check(string sql, IModel model)
    {
        // Dots inside data literals (including .invalid addresses) are not column references.
        sql = Regex.Replace(sql, @"'(?:''|[^'])*'", "''");
        var aliases = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var tables = model.GetEntityTypes().Where(e => e.GetSchema() == "dango").GroupBy(e => e.GetTableName()!)
            .ToDictionary(g => g.Key, g => g.SelectMany(e => e.GetProperties().Select(p => p.GetColumnName(StoreObjectIdentifier.Table(g.Key, "dango"))!)).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(sql, @"(?:FROM|JOIN) dango\.([a-z_]+) ([a-z]+)\b"))
        { var table = match.Groups[1].Value; var alias = match.Groups[2].Value; if (!tables.TryGetValue(table, out var names)) throw new InvalidDataException(); aliases[alias] = names; mapped[alias] = table; }
        aliases["e"] = ["actor", "user_id", "pet_id", "default_room_id", "session_id", "nickname"];
        aliases["w"] = ["request_id", "version", "operation"];
        var columns = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(sql, @"\b([a-z]+)\.([a-z_]+)\b"))
        {
            var alias = match.Groups[1].Value; var column = match.Groups[2].Value;
            if (alias == "dango") continue;
            if (!aliases.TryGetValue(alias, out var names) || !names.Contains(column)) throw new InvalidDataException("Unknown SQL column: " + alias + "." + column);
            if (mapped.TryGetValue(alias, out var table)) columns.Add(table + "." + column);
        }
        return columns;
    }
    private static string ReadSql()
    {
        DirectoryInfo? root = new(Directory.GetCurrentDirectory()); while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root!.FullName, "artifacts/porch-crud-c1-r2-candidate/scoped-sql-review.json")));
        return doc.RootElement.GetProperty("Sql").GetString()!;
    }
}
