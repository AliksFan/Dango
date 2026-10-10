using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.BackendManualReview.Tests;

[TestClass]
public sealed class ManualReviewTargetTests
{
    private static readonly string Data = Path.Combine(Path.GetTempPath(), "DangoManualReviewStaticOnly", "pgdata");

    [TestMethod]
    public void MissingApprovalRejectsBeforeAnyTargetAccess() =>
        Assert.ThrowsExactly<ManualReviewRejected>(() => ManualReviewTarget.Load(false, "invalid\0path", "invalid\0path"));

    [TestMethod]
    [DataRow("Database", "unapproved")]
    [DataRow("Owner", "unapproved")]
    [DataRow("RunName", "unapproved")]
    [DataRow("DataDirectory", "other")]
    [DataRow("Version", "2")]
    [DataRow("Port", "0")]
    [DataRow("Port", "65536")]
    public void ExactStateCannotSelectOtherResources(string field, string value)
    {
        var fields = State();
        fields[field] = field is "Port" or "Version" ? int.Parse(value) : value;
        Assert.ThrowsExactly<ManualReviewRejected>(() => ManualReviewTarget.ValidateState(JsonSerializer.SerializeToElement(fields), Data));
    }

    [TestMethod]
    public void ApprovedStateUsesOnlyRecordedPort() => Assert.AreEqual(54321,
        ManualReviewTarget.ValidateState(JsonSerializer.SerializeToElement(State()), Data));

    [TestMethod]
    public void MissingStateFieldRejectsWithStableCode()
    {
        var fields = State(); fields.Remove("Port");
        var error = Assert.ThrowsExactly<ManualReviewRejected>(() => ManualReviewTarget.ValidateState(JsonSerializer.SerializeToElement(fields), Data));
        Assert.AreEqual("manual_review_state_rejected", error.Code);
    }

    [TestMethod]
    public void FailedControlOrPreexistingProcessCannotGrantStopAuthority()
    {
        var start = DateTimeOffset.UtcNow;
        Assert.IsFalse(ManualReviewPostgres.StartWasOwned(1, start, start.AddSeconds(5), start.AddSeconds(1)));
        Assert.IsFalse(ManualReviewPostgres.StartWasOwned(0, start, start.AddSeconds(5), start.AddTicks(-1)));
        Assert.IsFalse(ManualReviewPostgres.StartWasOwned(0, start, start.AddSeconds(5), start.AddSeconds(6)));
        Assert.IsTrue(ManualReviewPostgres.StartWasOwned(0, start, start.AddSeconds(5), start.AddSeconds(1)));
    }

    [TestMethod]
    public void UnobservedStartedToolCannotClaimExitFromAnEmptyPid()
    {
        var capability = new ManualReviewToolProcess(0, DateTimeOffset.MinValue, "pg_ctl.exe", Guid.NewGuid(), false);
        Assert.IsFalse(capability.HasExited());
        Assert.IsFalse((capability with { Pid = Environment.ProcessId }).HasExited());
    }

    [TestMethod]
    [DataRow("prefix")]
    [DataRow("duplicate")]
    [DataRow("host")]
    [DataRow("port")]
    [DataRow("executable")]
    public void ProcessProofCannotMatchByDirectorySubstringOrPidAlone(string fault)
    {
        var executable = Path.Combine(Path.GetTempPath(), "pg18", "postgres.exe");
        var arguments = new[] { executable, "-D", Data, "-h", "127.0.0.1", "-p", "54321" };
        if (fault == "prefix") arguments[2] += "-other";
        if (fault == "duplicate") { arguments[3] = "-D"; arguments[4] = Data; }
        if (fault == "host") arguments[4] = "0.0.0.0";
        if (fault == "port") arguments[6] = "54322";
        if (fault == "executable") arguments[0] += ".other";
        Assert.IsFalse(ManualReviewPostgres.MatchArguments(arguments, executable, Data, 54321));
    }

    [TestMethod]
    public void ExactProcessArgumentsAreAccepted()
    {
        var executable = Path.Combine(Path.GetTempPath(), "pg18", "postgres.exe");
        Assert.IsTrue(ManualReviewPostgres.MatchArguments([executable, "-h", "127.0.0.1", "-p", "54321", "-D", Data], executable, Data, 54321));
    }

    private static Dictionary<string, object> State() => new()
    {
        ["Version"] = 1, ["RunName"] = ManualReviewTarget.ExistingRun, ["Database"] = ManualReviewTarget.Database,
        ["Owner"] = ManualReviewTarget.DatabaseRole, ["DataDirectory"] = Data, ["Port"] = 54321
    };
}
