using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryUnifiedEditorSessionTests
{
    [STATestMethod]
    public void ClipboardBuildsPngBitmapAndEditableFormatsInOneDataObjectWithoutTouchingSystemClipboard()
    {
        var png = Convert.FromBase64String(AccessorySkeletonProjectContractTests.UpgradeFixture()["png"]!["pngBase64"]!.GetValue<string>());
        var data = AccessoryEditorClipboard.CreateDataObject("fictional-fragment"u8.ToArray(), png);
        Assert.IsTrue(data.GetDataPresent("PNG", false)); Assert.IsTrue(data.GetDataPresent(System.Windows.DataFormats.Bitmap));
        Assert.IsTrue(data.GetDataPresent(AccessoryEditorClipboard.FragmentFormat, false));
        CollectionAssert.AreEqual(png, ((System.IO.MemoryStream)data.GetData("PNG", false)!).ToArray());
        CollectionAssert.AreEqual("fictional-fragment"u8.ToArray(), ((System.IO.MemoryStream)data.GetData(AccessoryEditorClipboard.FragmentFormat, false)!).ToArray());
    }
    [TestMethod]
    public void ExplicitClipboardCopyAndPasteUseOnlyInjectedPortAndPreserveDraft()
    {
        var clipboard = new MemoryClipboard();
        using var fixture = new Fixture(clipboard: clipboard); fixture.Session.Open(); fixture.Ready();
        Assert.AreEqual(0, clipboard.Reads); Assert.AreEqual(0, clipboard.Writes);
        var bytes = System.Text.Encoding.UTF8.GetBytes(AccessorySkeletonProjectContractTests.UpgradeFixture().ToJsonString());
        fixture.Send("ClipboardCopy", new { bytes = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
        fixture.Send("ClipboardChunk", new { index = 0, base64 = Convert.ToBase64String(bytes) });
        Assert.AreEqual(1, clipboard.Writes); CollectionAssert.AreEqual(bytes, clipboard.Fragment);
        Assert.IsTrue(clipboard.Png!.Length > 0); Assert.IsNull(fixture.Session.Result);
        Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
        fixture.Send("ClipboardPaste", new { }); Assert.AreEqual(1, clipboard.Reads);
        var response = JsonNode.Parse(fixture.Outgoing.Last(json => JsonNode.Parse(json)!["type"]!.GetValue<string>() == "ClipboardResult"))!;
        Assert.IsTrue(response["payload"]!["ok"]!.GetValue<bool>());
        Assert.AreEqual(bytes.Length, response["payload"]!["bytes"]!.GetValue<int>());
        Assert.AreEqual(fixture.Original, fixture.Session.Parameters);
    }

    [TestMethod]
    public void ClipboardActionIdsRejectReplayAndForeignChunksWithoutSystemAccess()
    {
        var clipboard = new MemoryClipboard();
        using var fixture = new Fixture(clipboard: clipboard); fixture.Session.Open(); fixture.Ready();
        fixture.Send("ClipboardPaste", new { clipboardId = 10 });
        fixture.Send("ClipboardPaste", new { clipboardId = 10 });
        Assert.AreEqual(1, clipboard.Reads, "Replayed actions must not reread the system clipboard.");
        var bytes = System.Text.Encoding.UTF8.GetBytes(AccessorySkeletonProjectContractTests.UpgradeFixture().ToJsonString());
        fixture.Send("ClipboardCopy", new { clipboardId = 11, bytes = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
        fixture.Send("ClipboardChunk", new { clipboardId = 10, index = 0, base64 = Convert.ToBase64String(bytes) });
        Assert.AreEqual(0, clipboard.Writes);
        Assert.AreEqual(AccessoryUnifiedEditorState.Busy, fixture.Session.State);
        fixture.Send("ClipboardChunk", new { clipboardId = 11, index = 0, base64 = Convert.ToBase64String(bytes) });
        Assert.AreEqual(1, clipboard.Writes);
        Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
    }

    [TestMethod]
    public void ClipboardRejectsInvalidFragmentsAndWrongOriginWithoutSystemAccess()
    {
        var clipboard = new MemoryClipboard { Fragment = "{}"u8.ToArray() };
        using var fixture = new Fixture(clipboard: clipboard); fixture.Session.Open(); fixture.Ready();
        fixture.Session.Receive("https://invalid.example/index.html", fixture.Message("ClipboardPaste", new { }));
        Assert.AreEqual(0, clipboard.Reads);
        fixture.Send("ClipboardPaste", new { });
        Assert.AreEqual(1, clipboard.Reads); Assert.AreEqual(0, clipboard.Writes);
        Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
        Assert.IsFalse(JsonNode.Parse(fixture.Outgoing[^1])!["payload"]!["ok"]!.GetValue<bool>());
        var invalid = "{}"u8.ToArray();
        fixture.Send("ClipboardCopy", new { bytes = invalid.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(invalid)) });
        fixture.Send("ClipboardChunk", new { index = 0, base64 = Convert.ToBase64String(invalid) });
        Assert.AreEqual(0, clipboard.Writes); Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
    }

    [TestMethod]
    public void ClipboardAcceptsEmptyGroupWithoutRelaxingSavedProjectLayerMinimum()
    {
        var clipboard = new MemoryClipboard();
        using var fixture = new Fixture(clipboard: clipboard); fixture.Session.Open(); fixture.Ready();
        var snapshot = AccessorySkeletonProjectContractTests.UpgradeFixture();
        snapshot["project"]!["layers"] = new JsonArray(); snapshot["project"]!["assets"] = new JsonArray();
        snapshot["project"]!["renderPieces"] = new JsonArray(); snapshot["project"]!["skeleton"]!["layers"] = new JsonArray();
        snapshot["project"]!["skeleton"]!["groups"] = JsonSerializer.SerializeToNode(new[] { new { id = "empty-group", name = "空组", targetId = (string?)null,
            nodes = new[] { new { id = "empty-root", x = .5, y = .5 } }, parameters = new { bend = .7, damping = 6, restoration = 5, gravity = 0, elasticity = 0 } } });
        var bytes = System.Text.Encoding.UTF8.GetBytes(snapshot.ToJsonString());
        fixture.Send("ClipboardCopy", new { bytes = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
        fixture.Send("ClipboardChunk", new { index = 0, base64 = Convert.ToBase64String(bytes) });
        Assert.AreEqual(1, clipboard.Writes); CollectionAssert.AreEqual(bytes, clipboard.Fragment);
        Assert.IsNull(fixture.Session.Result); Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
    }

    private sealed class MemoryClipboard : IAccessoryEditorClipboard
    {
        internal byte[]? Fragment; internal byte[]? Png; internal int Reads; internal int Writes;
        public void Write(byte[] fragment, byte[] png) { Writes++; Fragment = fragment; Png = png; }
        public byte[]? ReadFragment() { Reads++; return Fragment; }
    }
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
        Assert.IsNotNull(document["project"]); Assert.IsNull(document["geometry"]);
        Assert.AreEqual(2, document["project"]!["schemaVersion"]!.GetValue<int>());
        Assert.AreEqual("虚构配饰", document["project"]!["name"]!.GetValue<string>());
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
            DeformationMode = AccessoryDeformationMode.Rigid });
        Assert.IsTrue(fixture.Session.RequestSnapshot(true));
        fixture.Export("Confirm", new { pivotX = .25, pivotY = .25,
            nodes = new[] { new { x = .25, y = .25 }, new { x = .75, y = .75 } }, requiresRebind = false });
        var result = fixture.Session.Result!;
        Assert.IsNotNull(result); Assert.AreEqual(AccessoryUnifiedEditorState.Closed, fixture.Session.State);
        Assert.AreEqual(73d, result.Item.GravityInfluence); Assert.AreEqual(180d, result.Item.SizePercent);
        Assert.AreEqual(.25, result.Item.ImagePivotX); Assert.IsNull(result.Item.Rig);
        Assert.AreEqual(AccessoryDeformationMode.Skeleton, result.Item.DeformationMode);
        Assert.IsNotNull(result.Item.Skeleton); Assert.AreEqual("虚构编辑名称", result.Item.Name);
        Assert.AreEqual(fixture.Original.Id, result.Item.Id);
        Assert.AreEqual(result.Project.ProjectId, result.Item.ProjectId);
        Assert.AreEqual(7L, result.Item.EditRevision);
        Assert.IsNull(fixture.Original.ProjectId); Assert.AreEqual(0d, fixture.Original.GravityInfluence);
    }

    [TestMethod]
    public void LegacyGeometryCannotMutateTheNativeDraftOrProduceProtocolTwoMessages()
    {
        using var fixture = new Fixture(); fixture.Session.Open(); fixture.Ready();
        fixture.Send("Preview", new { geometry = new { pivotX = -.1, pivotY = .5, nodes = Array.Empty<object>(), requiresRebind = true } });
        Assert.AreEqual(.5, fixture.Session.Parameters.ImagePivotX);
        Assert.ThrowsExactly<NotSupportedException>(() => fixture.Session.SetGeometry(new(.2, .3, [], false)));
        Assert.IsTrue(fixture.Session.RequestSnapshot(true));
        fixture.Session.Cancel(); Assert.IsNull(fixture.Session.Result);
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
        var confirm = fixture.Message("Confirm", new { bytes = 1, sha256 = new string('a', 64), editSequence = 0 });
        fixture.Session.Dispose(); fixture.Session.Receive(AccessoryEditorBridge.EntryPoint, confirm);
        Assert.IsNull(fixture.Session.Result); Assert.AreEqual(AccessoryUnifiedEditorState.Closed, fixture.Session.State);
    }

    [TestMethod]
    public void WrongIdentityOrUnsolicitedExportCannotReplaceDraftAndCorruptionFailsClosed()
    {
        using var fixture = new Fixture(); fixture.Session.Open(); fixture.Ready();
        fixture.Send("Confirm", new { bytes = 1, sha256 = new string('a', 64), editSequence = 0 });
        Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
        var update = JsonNode.Parse(fixture.Message("Preview", new { geometry = new
        { pivotX = .9, pivotY = .5, nodes = Array.Empty<object>(), requiresRebind = false } }))!;
        update["sessionId"] = "stale";
        fixture.Session.Receive(AccessoryEditorBridge.EntryPoint, update.ToJsonString());
        Assert.AreEqual(.5, fixture.Session.Parameters.ImagePivotX);
        fixture.Session.RequestSnapshot(true);
        fixture.Send("Confirm", new { bytes = 1, sha256 = new string('a', 64), editSequence = 0 });
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
        fixture.Send("Confirm", new { bytes = 100, sha256 = new string('a', 64), editSequence = 0 });
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
    public void ProtocolOneEnvelopeCannotCompleteTheCurrentSession()
    {
        using var fixture = new Fixture(); fixture.Session.Open();
        var stale = JsonNode.Parse(fixture.Message("Ready", new { }))!; stale["protocol"] = 1;
        fixture.Session.Receive(AccessoryEditorBridge.EntryPoint, stale.ToJsonString());
        Assert.AreEqual(AccessoryUnifiedEditorState.Loading, fixture.Session.State);
        fixture.Ready(); Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly List<string> Outgoing = [];
        internal readonly AccessoryItem Original;
        internal readonly AccessoryUnifiedEditorSession Session;
        private long clipboardSequence;
        private readonly JsonObject fixture = AccessorySkeletonProjectContractTests.UpgradeFixture();
        internal Fixture(TimeProvider? clock = null, IAccessoryEditorClipboard? clipboard = null)
        {
            var source = AccessoryEditorSourceAsset.FromPng(Convert.FromBase64String(fixture["png"]!["pngBase64"]!.GetValue<string>()));
            Original = new("fictional-modal-item", "虚构配饰", source.AssetId, 2, 2) { IsPendingCompletion = true };
            Session = new(new("fictional-outer-session", 3, Original.Id, 7, Original), source, null, clock, clipboard);
            Session.Outgoing += Outgoing.Add;
        }
        internal string Message(string type, object payload)
        {
            // A retry starts a new browser identity; use its latest Open declaration.
            var envelope = JsonNode.Parse(Outgoing.Last(json => JsonNode.Parse(json)!["type"]!.GetValue<string>() == "Open"))!.AsObject();
            var content = JsonSerializer.SerializeToNode(payload)!.AsObject();
            if (type is "ClipboardCopy" or "ClipboardPaste") content["clipboardId"] ??= ++clipboardSequence;
            if (type == "ClipboardChunk") content["clipboardId"] ??= clipboardSequence;
            envelope["type"] = type; envelope["payload"] = content; return envelope.ToJsonString();
        }
        internal void Send(string type, object payload) => Session.Receive(AccessoryEditorBridge.EntryPoint, Message(type, payload));
        internal void Ready() => Send("Ready", new { });
        internal void Export(string type, object geometry, Action? afterHeader = null)
        {
            var g = JsonSerializer.SerializeToElement(geometry);
            fixture["project"]!["skeleton"]!["pivot"]!["x"] = g.GetProperty("pivotX").GetDouble() * 2;
            fixture["project"]!["skeleton"]!["pivot"]!["y"] = g.GetProperty("pivotY").GetDouble() * 2;
            var content = JsonSerializer.SerializeToUtf8Bytes(new { project = fixture["project"], png = fixture["png"] });
            Send(type, new { bytes = content.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(content)), editSequence = 0 });
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
