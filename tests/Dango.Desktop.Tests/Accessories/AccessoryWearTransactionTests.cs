using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryWearTransactionTests
{
    [TestMethod]
    public void ReadyInitializesOnlyWearingSubsetAndAnchorRequestWaitsForHistoryReply()
    {
        using var f = new Fixture();
        var initial = JsonNode.Parse(f.Outgoing[^1])!;
        Assert.AreEqual("WearState", initial["type"]!.GetValue<string>());
        Assert.AreEqual(13, initial["payload"]!.AsObject().Count);
        Assert.IsNull(initial["payload"]!["isEquipped"]);
        var changes = 0; var contentChanges = 0;
        f.Session.WearChanged += () => changes++; f.Session.ContentChanged += () => contentChanges++;
        f.Session.RequestWearEdit(.7, .8);
        var request = JsonNode.Parse(f.Outgoing[^1])!;
        Assert.AreEqual("WearEdit", request["type"]!.GetValue<string>());
        Assert.AreEqual(f.Original, f.Session.Parameters);
        f.Send("WearChanged", request["payload"]!);
        Assert.AreEqual(.7, f.Session.Parameters.BodyAnchorX); Assert.AreEqual(.8, f.Session.Parameters.BodyAnchorY);
        Assert.AreEqual(1, changes); Assert.AreEqual(0, contentChanges);
        Assert.AreEqual(f.Original, f.Session.Parameters with { BodyAnchorX = f.Original.BodyAnchorX, BodyAnchorY = f.Original.BodyAnchorY });
        f.Send("WearChanged", request["payload"]!); Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void InvalidFieldsRangesTypesAndForeignIdentitiesCannotMutateModalOrWriteFiles()
    {
        foreach (var field in new[] { "bodyAnchorX", "bodyAnchorY", "sizePercent", "baseAngleDegrees", "connectionStrength",
            "maxConnectionTravelPercent", "rotationDamping", "returnStrength", "maxSwingDegrees", "gravityInfluence",
            "layer", "lockRotation", "limitSwing", "isEquipped", "order", "assetId" })
        {
            using var f = new Fixture(); var payload = f.Wear();
            payload[field] = field is "layer" ? JsonValue.Create("front") : JsonValue.Create(10000);
            f.Send("WearChanged", payload);
            Assert.AreEqual(f.Original, f.Session.Parameters, field); Assert.IsNull(f.Session.Result);
        }
        using var foreign = new Fixture(); var wear = foreign.Wear(); wear["sizePercent"] = 220;
        var message = foreign.Message("WearChanged", wear); message["sessionId"] = new string('f', 32);
        foreign.Session.Receive(AccessoryEditorBridge.EntryPoint, message.ToJsonString());
        message = foreign.Message("WearChanged", wear); message["requestId"] = new string('f', 32);
        foreign.Session.Receive(AccessoryEditorBridge.EntryPoint, message.ToJsonString());
        message = foreign.Message("WearChanged", wear); message["revision"] = 999;
        foreign.Session.Receive(AccessoryEditorBridge.EntryPoint, message.ToJsonString());
        Assert.AreEqual(foreign.Original, foreign.Session.Parameters);
    }

    [TestMethod]
    public void BusySnapshotKeepsLatestWearAndConfirmApplyCancelRemainSeparate()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-wear-transaction", Guid.NewGuid().ToString("N"));
        var outer = new AccessorySettingsSession(root);
        var chosen = outer.DraftConfiguration.Items[0];
        outer.Stage(new(outer.DraftConfiguration.Items.Select(i => i.Id == chosen.Id ? i with { IsEquipped = false, Order = 9 } : i)));
        var before = outer.DraftConfiguration; var saved = outer.SavedConfiguration;
        var ticket = outer.BeginEdit(chosen.Id);
        using (var f = new Fixture(ticket))
        {
            f.Session.RequestSnapshot(true);
            var bytes = f.ExportHeader("Confirm");
            var wear = f.Wear(); wear["sizePercent"] = 210; wear["layer"] = "Behind"; wear["bodyAnchorY"] = .8;
            f.Send("WearChanged", wear);
            f.Send("Chunk", new { index = 0, base64 = Convert.ToBase64String(bytes) });
            Assert.IsNotNull(f.Session.Result); Assert.AreEqual(210d, f.Session.Result.Item.SizePercent);
            Assert.IsFalse(f.Session.Result.Item.IsEquipped); Assert.AreEqual(9, f.Session.Result.Item.Order);
            Assert.AreEqual(before, outer.DraftConfiguration); Assert.IsFalse(Directory.Exists(root));
            Assert.IsTrue(outer.TryCompleteEdit(ticket, f.Session.Result));
        }
        Assert.AreEqual(saved, outer.SavedConfiguration); Assert.IsFalse(Directory.Exists(root));
        CollectionAssert.AreEqual(before.Items.Skip(1).ToArray(), outer.DraftConfiguration.Items.Skip(1).ToArray());
        outer.CancelPreview(); Assert.AreEqual(saved, outer.DraftConfiguration); Assert.IsFalse(Directory.Exists(root));
        ticket = outer.BeginEdit(chosen.Id);
        using (var f = new Fixture(ticket))
        {
            var wear = f.Wear(); wear["gravityInfluence"] = 82; f.Send("WearChanged", wear);
            f.Session.Cancel(); outer.CancelEdit(ticket);
            Assert.IsNull(f.Session.Result); Assert.AreEqual(saved, outer.DraftConfiguration); Assert.IsFalse(Directory.Exists(root));
        }
        ticket = outer.BeginEdit(chosen.Id);
        using (var f = new Fixture(ticket))
        {
            var wear = f.Wear(); wear["gravityInfluence"] = 82; f.Send("WearChanged", wear);
            f.Session.RequestSnapshot(true); var bytes = f.ExportHeader("Confirm");
            f.Send("Chunk", new { index = 0, base64 = Convert.ToBase64String(bytes) });
            Assert.IsTrue(outer.TryCompleteEdit(ticket, f.Session.Result!));
        }
        Assert.IsFalse(Directory.Exists(root)); Assert.IsTrue(outer.TrySave());
        var reloaded = new AccessorySettingsSession(root);
        Assert.AreEqual(82d, reloaded.SavedConfiguration.Items.Single(i => i.Id == chosen.Id).GravityInfluence);
    }

    [TestMethod]
    public void ViewportAndCommandsAcceptOnlyBoundedCurrentSessionMessages()
    {
        using var f = new Fixture(); var viewports = new List<AccessorySimulationViewport>(); var commands = new List<double>();
        f.Session.SimulationViewportReceived += viewports.Add;
        f.Session.ViewCommandReceived += (_, x, _) => commands.Add(x);
        f.Send("SimulationViewport", new { x = 10, y = 20, width = 800, height = 600, visible = true });
        Assert.AreEqual(new AccessorySimulationViewport(10, 20, 800, 600, true), viewports.Single());
        foreach (var x in new[] { -1d, .5, 4, 8192 }) f.Send("ViewCommand", new { command = "simulation-tool", x, y = 0 });
        f.Send("ViewCommand", new { command = "left", x = 0, y = 0 });
        f.Send("ViewCommand", new { command = "window-drag", x = 1, y = 0 });
        Assert.AreEqual(0, commands.Count);
        for (var x = 0; x < 4; x++) f.Send("ViewCommand", new { command = "simulation-tool", x, y = 0 });
        CollectionAssert.AreEqual(new[] { 0d, 1d, 2d, 3d }, commands);
        f.Send("SimulationViewport", new { x = 0, y = 0, width = 8193, height = 10, visible = true });
        Assert.AreEqual(1, viewports.Count); Assert.AreEqual(f.Original, f.Session.Parameters);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly AccessoryUnifiedEditorSession Session;
        internal readonly AccessoryItem Original;
        internal readonly List<string> Outgoing = [];
        private readonly JsonObject snapshot = AccessorySkeletonProjectContractTests.UpgradeFixture();
        internal Fixture(AccessoryEditDraft? ticket = null)
        {
            var source = AccessoryEditorSourceAsset.FromPng(Convert.FromBase64String(snapshot["png"]!["pngBase64"]!.GetValue<string>()));
            Original = ticket?.Original ?? new AccessoryItem("wear-fictional", "虚构佩戴", source.AssetId, 2, 2) { IsEquipped = false, Order = 4 };
            Session = new(ticket ?? new("fictional-outer", 1, Original.Id, 1, Original), source, null);
            Session.Outgoing += Outgoing.Add; Session.Open(); Send("Ready", new { });
        }
        internal JsonObject Wear() => JsonSerializer.SerializeToNode(AccessoryWearParameters.ToPayload(Session.Parameters))!.AsObject();
        internal JsonObject Message(string type, object payload)
        {
            var envelope = JsonNode.Parse(Outgoing[0])!.AsObject(); envelope["type"] = type;
            envelope["payload"] = JsonSerializer.SerializeToNode(payload); return envelope;
        }
        internal void Send(string type, object payload) => Session.Receive(AccessoryEditorBridge.EntryPoint, Message(type, payload).ToJsonString());
        internal byte[] ExportHeader(string type)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
            Send(type, new { bytes = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), editSequence = 0 });
            return bytes;
        }
        public void Dispose() => Session.Dispose();
    }
}
