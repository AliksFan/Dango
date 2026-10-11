using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[DoNotParallelize]
[TestCategory("OfflinePorchCrudC1")]
public sealed class PorchCrudC1BoundaryTests
{
    [TestMethod]
    public void OldGrantsCannotReachTheC1ResourceFactory()
    {
        Assert.IsFalse(PorchCrudC1TestOwner.IsApprovalGranted(key => key == "DANGO_BACKEND_ACCOUNTSYNC_B1C2_APPROVED" ? "1" : null));
        Assert.IsFalse(PorchCrudC1TestOwner.IsApprovalGranted(key => key == "DANGO_BACKEND_PORCHCRUD_C1_APPROVED" ? "1" : null));
        Assert.IsTrue(PorchCrudC1TestOwner.IsApprovalGranted(key => key == "DANGO_BACKEND_PORCHCRUD_C1R2_APPROVED" ? "1" : null));
        var loaded = false;
        Assert.ThrowsExactly<AssertInconclusiveException>(() => PorchCrudC1TestOwner.RequireWithFactory(false, () => { loaded = true; throw new Exception(); }));
        Assert.IsFalse(loaded);
    }
    [TestMethod]
    public void FixedBudgetSpendsTwentyOneAttemptsAndNeverRefundsAFailedRoomRequest()
    {
        var audit = new Audit(); var budget = new PorchCrudC1Budget(Guid.NewGuid(), audit, () => { });
        foreach (var item in PorchCrudC1Budget.Limits) for (var i = 0; i < item.Value; i++) budget.Reserve(item.Key);
        Assert.AreEqual(21, budget.TotalAttempts); Assert.AreEqual(21, audit.Rows.Count);
        CollectionAssert.AreEqual(Enumerable.Range(1, 21).Select(i => (long)i).ToArray(), audit.Rows.Select(r => r.Sequence).ToArray());
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(PorchCrudC1Operation.Create));
        Assert.ThrowsExactly<InvalidOperationException>(budget.CheckAlive); Assert.AreEqual(21, budget.TotalAttempts);
    }
    [TestMethod]
    public void DurableAuditFailureConsumesTheAttemptAndClosesTheOwner()
    {
        var budget = new PorchCrudC1Budget(Guid.NewGuid(), new Audit { Fail = true }, () => { });
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(PorchCrudC1Operation.Restore));
        Assert.AreEqual(1, budget.TotalAttempts); Assert.ThrowsExactly<InvalidOperationException>(budget.CountHttpRequest);
    }
    [TestMethod]
    public void RoutePolicyAllowsOnlyTheExactTargetPageAndNoConfigurationWrites()
    {
        var origin = new Uri("https://127.0.0.1:54321/"); var room = Guid.NewGuid(); var registration = Guid.NewGuid();
        Assert.AreEqual(PorchCrudC1Operation.Update, PorchCrudC1RequestPolicy.Classify(origin, new(origin, $"api/v1/rooms/{room:D}"), "PATCH", registration, room));
        Assert.IsNull(PorchCrudC1RequestPolicy.Classify(origin, new(origin, "api/v1/me/rooms?state=active&limit=4"), "GET", registration, room));
        foreach (var path in new[] { $"api/v1/rooms/{Guid.NewGuid():D}", "api/v1/me/rooms?state=active&limit=50",
            "api/v1/me/rooms?state=active&limit=4&after=" + Guid.NewGuid(), "api/v1/me/pet/configuration", "api/v1/rooms?extra=1" })
            Assert.ThrowsExactly<InvalidOperationException>(() => PorchCrudC1RequestPolicy.Classify(origin, new(origin, path), "PATCH", registration, room));
        Assert.ThrowsExactly<InvalidOperationException>(() => PorchCrudC1RequestPolicy.Classify(origin, new Uri("https://example.invalid/api/v1/me"), "GET", registration, room));
    }
    [TestMethod]
    public void RoomEvidenceRejectsSuccessWithoutReadbackAndRejectsForeignSuccess()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var room = Guid.NewGuid(); var rows = Sample(a, b, room);
        PorchCrudC1EvidenceContract.VerifyRequests(rows, a, b, room, 1000);
        var foreign = rows.FindIndex(r => r.Operation == "Update" && r.Actor == "b");
        var changed = rows.ToArray(); changed[foreign] = changed[foreign] with { Status = 200, RoomVersion = 2 };
        Assert.ThrowsExactly<InvalidOperationException>(() => PorchCrudC1EvidenceContract.VerifyRequests(changed, a, b, room, 1000));
        changed = rows.ToArray(); var readback = rows.FindIndex(r => r.Method == "GET" && r.RoomVersion == 4); changed[readback] = changed[readback] with { RoomVersion = 3 };
        Assert.ThrowsExactly<InvalidOperationException>(() => PorchCrudC1EvidenceContract.VerifyRequests(changed, a, b, room, 1000));
        changed = rows.ToArray(); changed[10] = changed[10] with { SendBefore = changed[9].SendAfter + 599 };
        Assert.ThrowsExactly<InvalidOperationException>(() => PorchCrudC1EvidenceContract.VerifyRequests(changed, a, b, room, 1000));
    }
    private static List<PorchCrudC1Request> Sample(Guid a, Guid b, Guid room)
    {
        var rows = new List<PorchCrudC1Request>();
        void Add(string actor, string method, string path, string? operation, int status, long? version = null, long? expected = null)
        {
            var n = rows.Count + 1; rows.Add(new(n, actor == "a" ? 3 : 4, actor == "a" ? "desktop" : "outsider", actor, method, path,
                actor == "a" ? a : b, operation is "Create" or "Update" or "Dissolve" or "Restore" ? Guid.NewGuid() : null,
                expected, n * 1000, n * 1000 + 1, status, version, operation, "RanToCompletion"));
        }
        foreach (var actor in new[] { "a", "b" }) foreach (var op in new[] { "Start", "Complete", "Login", "Profile", "Logout" }) Add(actor, "POST", "/api/v1/auth/test-oracle", op, 200);
        for (var i = 0; i < 8; i++) Add("a", "GET", "/api/v1/me/pet", null, 200);
        var detail = $"/api/v1/rooms/{room:D}";
        Add("a", "POST", "/api/v1/rooms", "Create", 201, 1); Add("a", "GET", detail, null, 200, 1); Add("b", "GET", detail, null, 404);
        Add("b", "PATCH", detail, "Update", 404, expected: 1); Add("a", "PATCH", detail, "Update", 200, 2, 1); Add("a", "GET", detail, null, 200, 2);
        Add("b", "POST", detail + "/dissolve", "Dissolve", 404, expected: 2); Add("a", "POST", detail + "/dissolve", "Dissolve", 200, 3, 2); Add("a", "GET", detail, null, 200, 3);
        Add("b", "POST", detail + "/restore", "Restore", 404, expected: 3); Add("a", "POST", detail + "/restore", "Restore", 200, 4, 3); Add("a", "GET", detail, null, 200, 4); Add("a", "GET", detail, null, 200, 4);
        return rows;
    }
    private sealed class Audit : IAccountSyncAttemptAudit
    {
        internal bool Fail; internal List<AccountSyncAttemptRecord> Rows { get; } = [];
        public void AppendAndFlush(AccountSyncAttemptRecord record) { if (Fail) throw new IOException(); Rows.Add(record); }
    }
}
