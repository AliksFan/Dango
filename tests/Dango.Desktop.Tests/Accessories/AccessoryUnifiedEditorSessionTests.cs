using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryUnifiedEditorSessionTests
{
    [TestMethod]
    public void OpenIsChunkedAndHasNoHumanEditingDeadlineButEachOperationHasOne()
    {
        var clock = new TestClock();
        using var fixture = new Fixture(clock);
        fixture.Session.Open();
        var header = JsonNode.Parse(fixture.Outgoing[0])!;
        Assert.AreEqual("Open", header["type"]!.GetValue<string>());
        var raw = fixture.Outgoing.Skip(1).SelectMany(json => Convert.FromBase64String(
            JsonNode.Parse(json)!["payload"]!["base64"]!.GetValue<string>())).ToArray();
        Assert.AreEqual(raw.Length, header["payload"]!["bytes"]!.GetValue<int>());
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(raw)), header["payload"]!["sha256"]!.GetValue<string>());
        var document = JsonNode.Parse(raw)!;
        Assert.IsNotNull(document["project"]); Assert.IsNotNull(document["geometry"]);
        fixture.Ready(); clock.Advance(TimeSpan.FromHours(4)); fixture.Session.CheckTimeout();
        Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
        Assert.IsTrue(fixture.Session.RequestSnapshot(true));
        clock.Advance(TimeSpan.FromSeconds(61)); fixture.Session.CheckTimeout();
        Assert.AreEqual(AccessoryUnifiedEditorState.Failed, fixture.Session.State);
        Assert.IsNull(fixture.Session.Result);
    }

    [TestMethod]
    public void CompleteConfirmationIsAtomicAndUsesNativeParametersAndCurrentGeometry()
    {
        using var fixture = new Fixture();
        fixture.Session.Open(); fixture.Ready();
        fixture.Session.UpdateParameters(fixture.Original with { GravityInfluence = 73, SizePercent = 180,
            DeformationMode = AccessoryDeformationMode.SingleChain });
        fixture.Session.UpdateRigParameters(.2, 11, 9);
        Assert.IsTrue(fixture.Session.RequestSnapshot(true));
        fixture.Export("Confirm", new { pivotX = .25, pivotY = .25,
            nodes = new[] { new { x = .25, y = .25 }, new { x = .75, y = .75 } }, requiresRebind = false });
        var result = fixture.Session.Result!;
        Assert.IsNotNull(result); Assert.AreEqual(AccessoryUnifiedEditorState.Closed, fixture.Session.State);
        Assert.AreEqual(73d, result.Item.GravityInfluence); Assert.AreEqual(180d, result.Item.SizePercent);
        Assert.AreEqual(.25, result.Item.ImagePivotX); Assert.AreEqual(.2, result.Item.Rig!.Softness);
        Assert.AreEqual(11d, result.Item.Rig.Damping); Assert.AreEqual(9d, result.Item.Rig.Restoration);
        Assert.AreEqual(fixture.Original.Id, result.Item.Id);
        Assert.AreEqual(result.Project.ProjectId, result.Item.ProjectId);
        Assert.AreEqual(7L, result.Item.EditRevision);
        Assert.IsNull(fixture.Original.ProjectId); Assert.AreEqual(0d, fixture.Original.GravityInfluence);
    }

    [TestMethod]
    public void GeometryNotificationsRemainDraftsAndRebindingPreventsConfirmation()
    {
        using var fixture = new Fixture(); fixture.Session.Open(); fixture.Ready();
        fixture.Send("Preview", new { geometry = new { pivotX = -.1, pivotY = .5, nodes = Array.Empty<object>(), requiresRebind = true } });
        Assert.AreEqual(-.1, fixture.Session.Parameters.ImagePivotX);
        Assert.IsFalse(fixture.Session.RequestSnapshot(true)); Assert.IsNull(fixture.Session.Result);
        fixture.Session.SetGeometry(new(.2, .3, [], false));
        var outgoing = JsonNode.Parse(fixture.Outgoing.Last())!;
        Assert.AreEqual("SetGeometry", outgoing["type"]!.GetValue<string>());
        Assert.AreEqual(.2, outgoing["payload"]!["pivotX"]!.GetValue<double>());
        Assert.IsTrue(fixture.Session.RequestSnapshot(true));
        fixture.Session.Dispose();
        Assert.IsNull(fixture.Session.Result); Assert.AreEqual(.5, fixture.Original.ImagePivotX);
    }

    [TestMethod]
    public void RetryChangesIdentityAndRejectsOldResultsAndCancellationRejectsLateCallbacks()
    {
        using var fixture = new Fixture(); fixture.Session.Open(); fixture.Ready();
        var oldReady = fixture.Message("Ready", new { });
        fixture.Session.UpdateParameters(fixture.Original with { GravityInfluence = 39 });
        fixture.Session.Fail("synthetic renderer failure"); fixture.Session.Open();
        fixture.Session.Receive(AccessoryEditorBridge.EntryPoint, oldReady);
        Assert.AreEqual(AccessoryUnifiedEditorState.Loading, fixture.Session.State);
        fixture.Ready(); Assert.AreEqual(39d, fixture.Session.Parameters.GravityInfluence);
        var confirm = fixture.Message("Confirm", new { bytes = 1, sha256 = new string('a', 64) });
        fixture.Session.Dispose(); fixture.Session.Receive(AccessoryEditorBridge.EntryPoint, confirm);
        Assert.IsNull(fixture.Session.Result); Assert.AreEqual(AccessoryUnifiedEditorState.Closed, fixture.Session.State);
    }

    [TestMethod]
    public void WrongIdentityOrUnsolicitedExportCannotReplaceDraftAndCorruptionFailsClosed()
    {
        using var fixture = new Fixture(); fixture.Session.Open(); fixture.Ready();
        fixture.Send("Confirm", new { bytes = 1, sha256 = new string('a', 64) });
        Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
        var update = JsonNode.Parse(fixture.Message("Preview", new { geometry = new
        { pivotX = .9, pivotY = .5, nodes = Array.Empty<object>(), requiresRebind = false } }))!;
        update["sessionId"] = "stale";
        fixture.Session.Receive(AccessoryEditorBridge.EntryPoint, update.ToJsonString());
        Assert.AreEqual(.5, fixture.Session.Parameters.ImagePivotX);
        fixture.Session.RequestSnapshot(true);
        fixture.Send("Confirm", new { bytes = 1, sha256 = new string('a', 64) });
        fixture.Send("Chunk", new { index = 0, base64 = "YQ==" });
        Assert.AreEqual(AccessoryUnifiedEditorState.Failed, fixture.Session.State); Assert.IsNull(fixture.Session.Result);
    }

    [TestMethod]
    public void PreviewReturnsACompleteValidatedSnapshotAndCancelStillReturnsNoConfirmation()
    {
        using var fixture = new Fixture(); fixture.Session.Open(); fixture.Ready();
        AccessoryEditResult? preview = null; fixture.Session.PreviewReady += result => preview = result;
        fixture.Session.RequestSnapshot(false);
        fixture.Export("Preview", new { pivotX = .5, pivotY = .5, nodes = Array.Empty<object>(), requiresRebind = false });
        Assert.IsNotNull(preview); Assert.IsNull(fixture.Session.Result);
        Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
        fixture.Session.UpdateParameters(fixture.Session.Parameters with { GravityInfluence = 21 });
        fixture.Session.Fail("synthetic render exit"); fixture.Session.Open(); fixture.Ready();
        Assert.AreEqual(21d, fixture.Session.Parameters.GravityInfluence);
        fixture.Session.Dispose(); Assert.IsNull(fixture.Session.Result);
    }

    [TestMethod]
    public void RetryRestoresMatchingProjectGeometryButKeepsNativeMotionParameters()
    {
        using var fixture = new Fixture(); fixture.Session.Open(); fixture.Ready();
        fixture.Session.RequestSnapshot(false);
        fixture.Export("Preview", new { pivotX = .25, pivotY = .25, nodes = Array.Empty<object>(), requiresRebind = false });
        fixture.Send("Preview", new { geometry = new { pivotX = -.5, pivotY = .25, nodes = Array.Empty<object>(), requiresRebind = true } });
        fixture.Session.UpdateParameters(fixture.Session.Parameters with { BodyAnchorY = .8 });
        fixture.Session.Fail("synthetic process exit"); fixture.Session.Open(); fixture.Ready();
        Assert.AreEqual(.25, fixture.Session.Geometry.PivotX);
        Assert.AreEqual(.25, fixture.Session.Geometry.PivotY);
        Assert.AreEqual(.8, fixture.Session.Parameters.BodyAnchorY);
        Assert.IsTrue(fixture.Session.RestoredCheckpoint);
    }

    [TestMethod]
    public void IncompleteWebOperationCanBeCorrectedWithoutDiscardingTheCurrentCanvas()
    {
        using var fixture = new Fixture(); fixture.Session.Open(); fixture.Ready();
        fixture.Session.RequestSnapshot(true);
        fixture.Send("Error", new { message = "fixed synthetic failure" });
        Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
        Assert.IsNotNull(fixture.Session.ErrorMessage); Assert.IsNull(fixture.Session.Result);
        Assert.IsTrue(fixture.Session.RequestSnapshot(true));
        fixture.Export("Confirm", new { pivotX = .5, pivotY = .5, nodes = Array.Empty<object>(), requiresRebind = false });
        Assert.IsNotNull(fixture.Session.Result);
    }

    [TestMethod]
    public void ErrorAfterTransferDeclarationRequiresFreshBridgeBeforeAnotherConfirmation()
    {
        var clock = new TestClock();
        using var fixture = new Fixture(clock); fixture.Session.Open(); fixture.Ready();
        fixture.Session.RequestSnapshot(true);
        fixture.Send("Confirm", new { bytes = 100, sha256 = new string('a', 64) });
        fixture.Send("Error", new { message = "fixed synthetic transfer failure" });
        Assert.AreEqual(AccessoryUnifiedEditorState.Failed, fixture.Session.State);
        Assert.IsFalse(fixture.Session.RequestSnapshot(true)); Assert.IsNull(fixture.Session.Result);
        fixture.Session.Open(); fixture.Ready();
        clock.Advance(TimeSpan.FromHours(1)); fixture.Session.CheckTimeout();
        Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
        Assert.IsTrue(fixture.Session.RequestSnapshot(true));
        fixture.Export("Confirm", new { pivotX = .5, pivotY = .5, nodes = Array.Empty<object>(), requiresRebind = false });
        Assert.IsNotNull(fixture.Session.Result);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DelayedGeometryNotificationCannotBecomeTheRequestedSnapshotHeader(bool confirm)
    {
        using var fixture = new Fixture(); fixture.Session.Open(); fixture.Ready();
        fixture.Session.SetGeometry(new(.25, .25, [], false));
        AccessoryEditResult? preview = null; fixture.Session.PreviewReady += result => preview = result;
        Assert.IsTrue(fixture.Session.RequestSnapshot(confirm));
        fixture.Send("Preview", new { geometry = new { pivotX = .6, pivotY = .6, nodes = Array.Empty<object>(), requiresRebind = false } });
        Assert.AreEqual(AccessoryUnifiedEditorState.Busy, fixture.Session.State);
        Assert.AreEqual(.25, fixture.Session.Geometry.PivotX);
        Assert.AreEqual(.25, fixture.Session.Parameters.ImagePivotY);
        fixture.Export(confirm ? "Confirm" : "Preview", new { pivotX = .25, pivotY = .25, nodes = Array.Empty<object>(), requiresRebind = false },
            () => fixture.Send("Preview", new { geometry = new { pivotX = .6, pivotY = .6, nodes = Array.Empty<object>(), requiresRebind = false } }));
        var result = confirm ? fixture.Session.Result : preview;
        Assert.IsNotNull(result); Assert.AreEqual(.25, result.Item.ImagePivotX);
        Assert.AreEqual(confirm ? AccessoryUnifiedEditorState.Closed : AccessoryUnifiedEditorState.Ready, fixture.Session.State);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DelayedGeometryStillRejectsUnexpectedFieldsWhileSnapshotIsBusy(bool confirm)
    {
        foreach (var extraInGeometry in new[] { false, true })
        {
            using var fixture = new Fixture(); fixture.Session.Open(); fixture.Ready(); fixture.Session.RequestSnapshot(confirm);
            var geometry = new JsonObject { ["pivotX"] = .25, ["pivotY"] = .25, ["nodes"] = new JsonArray(), ["requiresRebind"] = false };
            var payload = new JsonObject { ["geometry"] = geometry };
            if (extraInGeometry) geometry["unexpected"] = 1;
            else payload["bytes"] = 1;
            fixture.Send("Preview", payload);
            Assert.AreEqual(AccessoryUnifiedEditorState.Failed, fixture.Session.State);
            Assert.IsNull(fixture.Session.Result);
        }
    }

    [TestMethod]
    public void DelayedGeometryDoesNotExtendTheActiveOperationDeadline()
    {
        var clock = new TestClock();
        using var fixture = new Fixture(clock); fixture.Session.Open(); fixture.Ready(); fixture.Session.RequestSnapshot(false);
        clock.Advance(TimeSpan.FromSeconds(59));
        fixture.Send("Preview", new { geometry = new { pivotX = .5, pivotY = .5, nodes = Array.Empty<object>(), requiresRebind = false } });
        Assert.AreEqual(AccessoryUnifiedEditorState.Busy, fixture.Session.State);
        clock.Advance(TimeSpan.FromSeconds(2)); fixture.Session.CheckTimeout();
        Assert.AreEqual(AccessoryUnifiedEditorState.Failed, fixture.Session.State); Assert.IsNull(fixture.Session.Result);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly List<string> Outgoing = [];
        internal readonly AccessoryItem Original;
        internal readonly AccessoryUnifiedEditorSession Session;
        private readonly JsonObject fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(TestRepository.Root,
            "tests", "Dango.Desktop.Tests", "Accessories", "Fixtures", "AccessoryBrowserProjectV1.json")))!.AsObject();
        internal Fixture(TimeProvider? clock = null)
        {
            var source = AccessoryEditorSourceAsset.FromPng(Convert.FromBase64String(fixture["png"]!["pngBase64"]!.GetValue<string>()));
            Original = new("fictional-modal-item", "虚构配饰", source.AssetId, 2, 2) { IsPendingCompletion = true };
            Session = new(new("fictional-outer-session", 3, Original.Id, 7, Original), source, null, clock);
            Session.Outgoing += Outgoing.Add;
        }
        internal string Message(string type, object payload)
        {
            // A retry starts a new browser identity; use its latest Open declaration.
            var envelope = JsonNode.Parse(Outgoing.Last(json => JsonNode.Parse(json)!["type"]!.GetValue<string>() == "Open"))!.AsObject();
            envelope["type"] = type; envelope["payload"] = JsonSerializer.SerializeToNode(payload); return envelope.ToJsonString();
        }
        internal void Send(string type, object payload) => Session.Receive(AccessoryEditorBridge.EntryPoint, Message(type, payload));
        internal void Ready() => Send("Ready", new { });
        internal void Export(string type, object geometry, Action? afterHeader = null)
        {
            var content = JsonSerializer.SerializeToUtf8Bytes(new { project = fixture["project"], png = fixture["png"], geometry });
            Send(type, new { bytes = content.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) });
            afterHeader?.Invoke();
            Send("Chunk", new { index = 0, base64 = Convert.ToBase64String(content) });
        }
        public void Dispose() => Session.Dispose();
    }
    private sealed class TestClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        internal void Advance(TimeSpan duration) => ticks += duration.Ticks;
    }
}
