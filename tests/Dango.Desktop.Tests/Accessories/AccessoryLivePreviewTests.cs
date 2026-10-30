using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryLivePreviewTests
{
    [TestMethod]
    public void ChangedSequenceIsMonotonicStorageFreeAndResetByANewOpenIdentity()
    {
        using var fixture = new Fixture(); fixture.OpenReady();
        var notifications = 0; fixture.Session.ContentChanged += () => notifications++;
        fixture.Send("Changed", new { editSequence = 0 });
        fixture.Send("Changed", new { editSequence = 0 });
        fixture.Send("Changed", new { editSequence = 3 });
        fixture.Send("Changed", new { editSequence = 2 });
        Assert.AreEqual(2, notifications); Assert.AreSame(fixture.Original, fixture.Session.Parameters);
        Assert.IsNull(fixture.Session.Result);
        var old = fixture.Message("Changed", new { editSequence = 100 });
        fixture.Session.Open();
        fixture.Session.Receive(AccessoryEditorBridge.EntryPoint, old);
        fixture.Ready(); fixture.Send("Changed", new { editSequence = 0 });
        Assert.AreEqual(3, notifications, "A new bridge identity starts a fresh sequence epoch.");
        fixture.Send("Changed", new { editSequence = 9_007_199_254_740_991L });
        Assert.AreEqual(4, notifications);
    }

    [TestMethod]
    public void ChangedRejectsBadSafeIntegersAndContentPayloadsWithoutAcceptingAnyDraftData()
    {
        object[] payloads = [new { editSequence = -1 }, new { editSequence = 1.5 }, new { editSequence = "1" },
            new { editSequence = 9_007_199_254_740_992L }, new { }, new { editSequence = 1, project = new { } }];
        foreach (var payload in payloads)
        {
            using var fixture = new Fixture(); fixture.OpenReady();
            var changes = 0; fixture.Session.ContentChanged += () => changes++;
            fixture.Send("Changed", payload);
            Assert.AreEqual(AccessoryUnifiedEditorState.Failed, fixture.Session.State);
            Assert.AreEqual(0, changes); Assert.AreSame(fixture.Original, fixture.Session.Parameters);
            Assert.IsNull(fixture.Session.Result);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ChangedRejectsAnOlderExportAtItsHeaderOrCompletionAndRequiresAFreshSnapshot(bool changedAfterHeader)
    {
        using var fixture = new Fixture(); fixture.OpenReady();
        var changed = 0; fixture.Session.ContentChanged += () => changed++;
        Assert.IsTrue(fixture.Session.RequestSnapshot(true));
        byte[]? oldBytes = changedAfterHeader ? fixture.Header("Confirm") : null;
        fixture.Send("Changed", new { editSequence = 1 });
        Assert.AreEqual(AccessoryUnifiedEditorState.Busy, fixture.Session.State, "Changed alone cannot cancel an export that may already have captured the current edit.");
        oldBytes ??= fixture.Header("Confirm");
        fixture.Chunk(oldBytes);
        Assert.AreEqual(1, changed); Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
        Assert.IsNull(fixture.Session.Result); Assert.AreSame(fixture.Original, fixture.Session.Parameters);
        Assert.IsTrue(fixture.Session.RequestSnapshot(true));
        fixture.Export("Confirm", "Fresh synthetic snapshot", 1);
        Assert.IsNotNull(fixture.Session.Result); Assert.AreEqual("Fresh synthetic snapshot", fixture.Session.Result.Item.Name);
        Assert.AreEqual(AccessoryUnifiedEditorState.Closed, fixture.Session.State);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CurrentSnapshotHeaderFiltersDelayedSameSequenceChangedWithoutCancellingConfirmation(bool changedBeforeHeader)
    {
        using var fixture = new Fixture(); fixture.OpenReady();
        var changes = 0; fixture.Session.ContentChanged += () => changes++;
        fixture.Session.RequestSnapshot(true);
        if (changedBeforeHeader) fixture.Send("Changed", new { editSequence = 7 });
        var bytes = fixture.Header("Confirm", "Current synthetic capture", 7);
        fixture.Send("Changed", new { editSequence = 7 });
        fixture.Send("Changed", new { editSequence = 6 });
        Assert.AreEqual(AccessoryUnifiedEditorState.Busy, fixture.Session.State);
        fixture.Chunk(bytes);
        Assert.AreEqual(changedBeforeHeader ? 1 : 0, changes);
        Assert.IsNotNull(fixture.Session.Result); Assert.AreEqual("Current synthetic capture", fixture.Session.Result.Item.Name);
    }

    [TestMethod]
    public void SnapshotHeaderWithoutEditSequenceCannotUseTheProtocolTwoTransferPath()
    {
        using var fixture = new Fixture(); fixture.OpenReady(); fixture.Session.RequestSnapshot(false);
        fixture.Send("Preview", new { bytes = 1, sha256 = new string('a', 64) });
        Assert.AreEqual(AccessoryUnifiedEditorState.Failed, fixture.Session.State); Assert.IsNull(fixture.Session.Result);
    }

    [STATestMethod]
    public void UnshownWindowInvalidatesAndCoalescesPreviewButDoesNotRetryErrorsOrRunAfterClose()
    {
        var previews = new List<AccessoryEditResult>(); var invalidations = 0;
        using var fixture = new Fixture(previews.Add, () => invalidations++); fixture.OpenReady();
        var window = fixture.Window!;
        Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle); Assert.IsFalse(window.IsVisible);
        fixture.Send("Changed", new { editSequence = 0 }); Pump();
        Assert.AreEqual(0, fixture.RequestCount("Preview")); Assert.AreEqual(0, invalidations);
        fixture.Send("ViewCommand", new { command = "preview-show", x = 0, y = 0 });
        Assert.IsTrue(fixture.Session.RequestSnapshot(false)); fixture.Export("Preview");
        Assert.AreEqual(1, previews.Count);
        fixture.Send("Changed", new { editSequence = 1 });
        fixture.Send("Changed", new { editSequence = 2 });
        fixture.Send("Changed", new { editSequence = 1 });
        Assert.AreEqual(2, invalidations, "Duplicate/backward notifications cannot invalidate or enqueue again.");
        Assert.AreEqual(1, fixture.RequestCount("Preview"));
        Pump(); Assert.AreEqual(2, fixture.RequestCount("Preview"));
        fixture.Send("Error", new { message = "Synthetic editable input error" });
        Pump(); Pump();
        Assert.AreEqual(2, fixture.RequestCount("Preview"), "Ready/Error must not retry one consumed update request forever.");
        fixture.Send("Changed", new { editSequence = 3 });
        window.Close(); Pump();
        Assert.AreEqual(2, fixture.RequestCount("Preview")); Assert.AreEqual(AccessoryUnifiedEditorState.Closed, fixture.Session.State);
        Assert.AreEqual(1, previews.Count);
    }

    [STATestMethod]
    public void UnshownWindowRejectsBusyOldConfirmationAndRefreshesTheActivePreviewAfterReady()
    {
        var previews = new List<AccessoryEditResult>(); var invalidations = 0;
        using var fixture = new Fixture(previews.Add, () => invalidations++); fixture.OpenReady();
        fixture.Send("ViewCommand", new { command = "preview-show", x = 0, y = 0 });
        fixture.Session.RequestSnapshot(false); fixture.Export("Preview", "Initial synthetic preview");
        fixture.Session.RequestSnapshot(true); var oldBytes = fixture.Header("Confirm");
        fixture.Send("Changed", new { editSequence = 4 });
        Assert.AreEqual(AccessoryUnifiedEditorState.Busy, fixture.Session.State);
        Assert.AreEqual(1, invalidations); fixture.Chunk(oldBytes);
        Assert.IsNull(fixture.Session.Result); Assert.AreEqual(1, previews.Count);
        Pump(); Assert.AreEqual(2, fixture.RequestCount("Preview"));
        fixture.Export("Preview", "Updated synthetic preview", 4);
        Assert.AreEqual(2, previews.Count); Assert.AreEqual("Updated synthetic preview", previews[^1].Item.Name);
        Assert.IsNull(fixture.Session.Result); Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
        Assert.AreEqual(nint.Zero, new WindowInteropHelper(fixture.Window!).Handle);
    }

    [STATestMethod]
    public void UnshownWindowDoesNotInvalidateANewPreviewForItsDelayedSameSequenceNotification()
    {
        var previews = new List<AccessoryEditResult>(); var invalidations = 0;
        using var fixture = new Fixture(previews.Add, () => invalidations++); fixture.OpenReady();
        fixture.Session.RequestSnapshot(false); fixture.Export("Preview", "Latest synthetic preview", 12);
        fixture.Send("Changed", new { editSequence = 12 }); Pump();
        Assert.AreEqual(1, previews.Count); Assert.AreEqual(0, invalidations); Assert.AreEqual(1, fixture.RequestCount("Preview"));
        Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly AccessoryItem Original;
        internal readonly AccessoryUnifiedEditorSession Session;
        internal readonly AccessoryUnifiedEditorWindow? Window;
        private readonly List<string> outgoing = [];
        private readonly JsonObject document = AccessorySkeletonProjectContractTests.UpgradeFixture();
        internal Fixture(Action<AccessoryEditResult>? preview = null, Action? invalidated = null)
        {
            var source = AccessoryEditorSourceAsset.FromPng(Convert.FromBase64String(document["png"]!["pngBase64"]!.GetValue<string>()));
            Original = new("synthetic-live-preview", "Synthetic original", source.AssetId, 2, 2) { IsPendingCompletion = true };
            var draft = new AccessoryEditDraft("synthetic-outer", 1, Original.Id, 7, Original);
            if (preview is null) Session = new(draft, source, null);
            else
            {
                Window = new(draft, source, null, preview, invalidated);
                Session = (AccessoryUnifiedEditorSession)typeof(AccessoryUnifiedEditorWindow).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!;
                // An unshown window never starts WebView2. Replace only transport I/O with an
                // in-memory peer, while testing the actual session and Dispatcher scheduling.
                var send = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), Window,
                    typeof(AccessoryUnifiedEditorWindow).GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic)!);
                Session.Outgoing -= send;
            }
            Session.Outgoing += outgoing.Add;
        }
        internal void OpenReady() { Session.Open(); Ready(); }
        internal void Ready() => Send("Ready", new { });
        internal int RequestCount(string type) => outgoing.Count(json => JsonNode.Parse(json)!["type"]!.GetValue<string>() == type);
        internal string Message(string type, object payload)
        {
            var envelope = JsonNode.Parse(outgoing.Last(json => JsonNode.Parse(json)!["type"]!.GetValue<string>() == "Open"))!.AsObject();
            envelope["type"] = type; envelope["payload"] = JsonSerializer.SerializeToNode(payload); return envelope.ToJsonString();
        }
        internal void Send(string type, object payload) => Session.Receive(AccessoryEditorBridge.EntryPoint, Message(type, payload));
        internal byte[] Header(string type, string? name = null, long editSequence = 0)
        {
            if (name is not null) document["project"]!["name"] = name;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { project = document["project"], png = document["png"] });
            Send(type, new { bytes = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), editSequence }); return bytes;
        }
        internal void Chunk(byte[] bytes) => Send("Chunk", new { index = 0, base64 = Convert.ToBase64String(bytes) });
        internal void Export(string type, string? name = null, long editSequence = 0) => Chunk(Header(type, name, editSequence));
        public void Dispose() { Window?.Close(); Session.Dispose(); }
    }
}
