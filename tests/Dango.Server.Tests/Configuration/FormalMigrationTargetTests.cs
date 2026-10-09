using Dango.Server.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Configuration;

[TestClass]
public sealed class FormalMigrationTargetTests
{
    private static string Directory => Path.Combine(Path.GetTempPath(), "dango-fictional-migration", "pgdata");

    [TestMethod]
    public void ExactDeclaredTargetCanBeMigrated()
    {
        Assert.IsTrue(Matches("dango_fictional", Directory, 180005, "127.0.0.1", "fictional_owner"));
    }

    [TestMethod]
    [DataRow("database")]
    [DataRow("directory")]
    [DataRow("version17")]
    [DataRow("version19")]
    [DataRow("role")]
    [DataRow("any_listener")]
    [DataRow("additional_listener")]
    [DataRow("ipv6_listener")]
    public void WrongTargetIsRejectedBeforeMigration(string changedFact)
    {
        var database = changedFact == "database" ? "dango_other" : "dango_fictional";
        var directory = changedFact == "directory" ? Directory + "-other" : Directory;
        var version = changedFact switch { "version17" => 170009, "version19" => 190000, _ => 180005 };
        var role = changedFact == "role" ? "fictional_other_role" : "fictional_owner";
        var listen = changedFact switch
        {
            "any_listener" => "*",
            "additional_listener" => "127.0.0.1,0.0.0.0",
            "ipv6_listener" => "::1",
            _ => "127.0.0.1"
        };
        Assert.IsFalse(Matches(database, directory, version, listen, role));
    }

    private static bool Matches(string database, string directory, int version, string listen, string role) =>
        FormalEntry.MigrationTargetMatches("dango_fictional", Directory, "fictional_owner", database, directory,
            version, listen, role);
}
