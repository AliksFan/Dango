using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryEditorViewTransportTests
{
    [STATestMethod]
    public void RuntimeFailureReleasesPreviewAndLateSnapshotCannotRestartHiddenExporting()
    {
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8); bitmap.Freeze();
        var source = AccessoryEditorSourceAsset.FromBitmap(bitmap);
        var item = new AccessoryItem("lifecycle-fictional", "虚构生命周期", source.AssetId, 2, 2);
        var draft = new AccessoryEditDraft("lifecycle-outer", 1, item.Id, 1, item);
        var window = new AccessoryUnifiedEditorWindow(draft, source, null, _ => { });
        using var document = System.Text.Json.JsonDocument.Parse(AccessorySkeletonProjectContractTests.UpgradeFixture().ToJsonString());
        var result = new AccessoryBrowserProjectAdapter().ReadResult(document.RootElement.GetProperty("project"), document.RootElement.GetProperty("png"), draft, item);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        void Command(string command) => typeof(AccessoryUnifiedEditorWindow).GetMethod("OnViewCommand", flags)!.Invoke(window, [command, 0d, 0d]);
        bool Active() => (bool)typeof(AccessoryUnifiedEditorWindow).GetField("previewActive", flags)!.GetValue(window)!;
        try
        {
            var layout = (System.Windows.FrameworkElement)window.Content;
            layout.Measure(new(800, 640)); layout.Arrange(new(0, 0, 800, 640)); layout.UpdateLayout();
            Command("preview-show");
            var cancelButton = (System.Windows.Controls.Button)typeof(AccessoryUnifiedEditorWindow).GetField("cancelButton", flags)!.GetValue(window)!;
            Assert.IsFalse(cancelButton.IsCancel, "Escape in the simulation cannot trigger outer modal cancellation.");
            typeof(AccessoryUnifiedEditorWindow).GetMethod("OnSimulationViewport", flags)!.Invoke(window, [new AccessorySimulationViewport(8, 80, 480, 400, true)]);
            var browser = (AccessoryEditorBrowserHost)typeof(AccessoryUnifiedEditorWindow).GetField("browser", flags)!.GetValue(window)!;
            Assert.IsFalse(browser.SimulationCutout.IsEmpty);
            Command("preview-hide"); Assert.IsFalse(Active());
            Assert.IsTrue(cancelButton.IsCancel);
            typeof(AccessoryUnifiedEditorWindow).GetMethod("OnPreview", flags)!.Invoke(window, [result]);
            Assert.IsFalse(Active(), "An export completing after hide must not re-enable hidden exports.");
            Command("preview-show");
            var session = (AccessoryUnifiedEditorSession)typeof(AccessoryUnifiedEditorWindow).GetField("session", flags)!.GetValue(window)!;
            session.Fail("虚构运行时故障");
            Assert.IsFalse(Active()); Assert.IsTrue(browser.SimulationCutout.IsEmpty);
            var simulation = (AccessorySimulationView)typeof(AccessoryUnifiedEditorWindow).GetField("simulation", flags)!.GetValue(window)!;
            Assert.IsFalse(simulation.IsActive);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ViewCommandsUseSessionIdentityAndDoNotChangeProjectOrDraft()
    {
        using var fixture = new Fixture();
        var commands = new List<string>();
        fixture.Session.ViewCommandReceived += (command, _, _) => commands.Add(command);
        fixture.Send("ViewCommand", new { command = "preview-show", x = 0, y = 0 }, "https://other.invalid");
        fixture.Send("ViewCommand", new { command = "preview-show", x = 0, y = 0 }, foreignIdentity: true);
        fixture.Send("ViewCommand", new { command = "file-open", x = 0, y = 0 });
        fixture.Send("ViewCommand", new { command = "drag-start", x = 10, y = 0 });
        fixture.Send("ViewCommand", new { command = "simulation-refresh", x = 1, y = 0 });
        fixture.Send("ViewCommand", new { command = "simulation-refresh", x = 0, y = 0 }, foreignIdentity: true);
        CollectionAssert.AreEqual(Array.Empty<string>(), commands);
        foreach (var command in new[] { "window-drag", "preview-show", "preview-hide", "simulation-refresh" })
            fixture.Send("ViewCommand", new { command, x = 0, y = 0 });
        CollectionAssert.AreEqual(new[] { "window-drag", "preview-show", "preview-hide", "simulation-refresh" }, commands);
        Assert.AreEqual(fixture.Original, fixture.Session.Parameters);
        Assert.IsNull(fixture.Session.Result);
        Assert.AreEqual(AccessoryUnifiedEditorState.Ready, fixture.Session.State);
    }

    [STATestMethod]
    public void SimulationStateHasNoImagePayloadAndStopsAfterSessionClose()
    {
        using var fixture = new Fixture();
        fixture.Session.PostSimulationState("pick", updating: true);
        var frame = JsonNode.Parse(fixture.Outgoing[^1])!;
        Assert.AreEqual("SimulationState", frame["type"]!.GetValue<string>());
        Assert.AreEqual("pick", frame["payload"]!["tool"]!.GetValue<string>());
        Assert.IsNull(frame["payload"]!["pngBase64"]);
        Assert.IsTrue(frame["payload"]!["updating"]!.GetValue<bool>());
        var count = fixture.Outgoing.Count;
        fixture.Session.Dispose(); fixture.Session.PostSimulationState("default");
        Assert.AreEqual(count, fixture.Outgoing.Count);
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly AccessoryUnifiedEditorSession Session;
        internal readonly AccessoryItem Original;
        internal readonly List<string> Outgoing = [];
        private readonly JsonObject identity;

        internal Fixture()
        {
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8); bitmap.Freeze();
            var source = AccessoryEditorSourceAsset.FromBitmap(bitmap);
            Original = new("view-fictional-item", "虚构预览配饰", source.AssetId, 2, 2);
            Session = new(new("view-fictional-outer", 1, Original.Id, 1, Original), source, null);
            Session.Outgoing += Outgoing.Add; Session.Open();
            identity = (JsonObject)JsonNode.Parse(Outgoing[0])!;
            Send("Ready", new { });
        }

        internal void Send(string type, object payload, string origin = AccessoryEditorBridge.EntryPoint, bool foreignIdentity = false)
        {
            var message = (JsonObject)identity.DeepClone(); message["type"] = type;
            message["payload"] = System.Text.Json.JsonSerializer.SerializeToNode(payload);
            if (foreignIdentity) message["sessionId"] = new string('f', 32);
            Session.Receive(origin, message.ToJsonString());
        }
        public void Dispose() => Session.Dispose();
    }
}
