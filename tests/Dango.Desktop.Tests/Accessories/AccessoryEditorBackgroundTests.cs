using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryEditorBackgroundTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    public void HiddenNativeControllerLoadsThePackagedOfflineEditorAndReopensItsPixels()
    {
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { VerifyHiddenController(); }
        finally { SynchronizationContext.SetSynchronizationContext(oldContext); }
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HiddenBrowserRoundTripPreservesNativeImageBrushTextAndEmptyLayers(bool equalLayerOrders)
    {
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            using var window = new HwndSource(new HwndSourceParameters("Dango synthetic project interchange check")
            { Width = 800, Height = 600, WindowStyle = unchecked((int)0x80000000) });
            Assert.IsFalse(IsWindowVisible(window.Handle));
            using var runtime = new AccessoryEditorRuntime();
            var profile = Path.Combine(TestRepository.Root, "artifacts", "accessory-editor-e2-interchange", Guid.NewGuid().ToString("N"));
            Assert.IsTrue(Pump(runtime.InitializeAsync(window.Handle, Path.Combine(AppContext.BaseDirectory, "AccessoryEditor"), profile)), runtime.ErrorMessage);
            var pixels = new byte[64 * 48 * 4];
            for (var y = 10; y < 38; y++) for (var x = 10; x < 54; x++)
            {
                var index = (y * 64 + x) * 4;
                pixels[index] = 32; pixels[index + 1] = 90; pixels[index + 2] = 150; pixels[index + 3] = 255;
            }
            var source = AccessoryEditorSourceAsset.FromBitmap(BitmapSource.Create(64, 48, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4));
            var image = AccessoryImageAsset.FromRenderedPng(source.CopyPngBytes());
            var brush = new AccessoryBrushContent(4, false, new IReadOnlyList<AccessoryBrushPoint>[]
                { new AccessoryBrushPoint[] { new(0, 0, 4), new(12, 10, 4) } });
            var text = new AccessoryTextContent(new IReadOnlyList<AccessoryTextSpan>[]
                { new AccessoryTextSpan[] { new("Hi", new AccessoryTextStyle(Size: 12, FillColor: "#ffffff")) } });
            var project = new AccessoryEditorProject(64, 48, 0, image.AssetId,
                [new("native-image", "图片", source.AssetId, 0, 0, 64, 48),
                 new("native-brush", "笔画", null, 5, 5, 12, 10, Type: "brush", Color: "#ab3412", Brush: brush),
                 new("native-text", "文字", null, 18, 15, 20, 15, Type: "text", Text: text),
                 new("native-empty", "空层", null, 0, 0, 0, 0, Type: null)], [source]);
            var item = new AccessoryItem("fictional-interchange", "虚构编辑工程", image.AssetId, 64, 48);
            var draft = new AccessoryEditDraft("fictional-session", 1, item.Id, 1, item);
            var adapter = new AccessoryBrowserProjectAdapter();
            var open = adapter.CreateOpenProject(project);
            if (equalLayerOrders)
            {
                var tied = JsonNode.Parse(open.GetRawText())!;
                foreach (var layer in tied["layers"]!.AsArray()) layer!["order"] = 1;
                open = JsonSerializer.SerializeToElement(tied);
            }
            var first = Exchange(open);
            var result = adapter.ReadResult(first.GetProperty("project"), first.GetProperty("png"), draft, item);
            Assert.AreEqual(2, result.Project.Layers.Single(layer => layer.Type == "brush").Brush!.Strokes.Single().Count);
            Assert.AreEqual("Hi", result.Project.Layers.Single(layer => layer.Type == "text").Text!.Lines.Single().Single().Text);
            var second = Exchange(adapter.CreateOpenProject(result.Project));
            Assert.AreEqual(first.GetProperty("png").GetProperty("pngBase64").GetString(), second.GetProperty("png").GetProperty("pngBase64").GetString(), "Native save/reopen must preserve composited browser pixels.");
            var expectedIds = new[] { "native-image", "native-brush", "native-text", "native-empty" };
            if (equalLayerOrders) Array.Reverse(expectedIds);
            CollectionAssert.AreEqual(expectedIds, result.Project.Layers.Select(layer => layer.Id).ToArray());
            Assert.IsFalse(IsWindowVisible(window.Handle));
            TestContext.WriteLine("Hidden native/browser four-layer interchange passed; Runtime " + runtime.RuntimeVersion);

            JsonElement Exchange(JsonElement open)
            {
                Pump(runtime.Core!.ExecuteScriptAsync("window.dangoInterop={done:false};(async()=>{try{await DangoMiniPaint.load(" + open.GetRawText() + ");window.dangoInterop={done:true,project:DangoMiniPaint.exportProject(),png:DangoMiniPaint.exportPng()};}catch(e){window.dangoInterop={done:true,error:e.name+': '+e.message};}})();"));
                for (var i = 0; i < 100; i++)
                {
                    var raw = Pump(runtime.Core.ExecuteScriptAsync("JSON.stringify(window.dangoInterop)"));
                    using var status = JsonDocument.Parse(JsonSerializer.Deserialize<string>(raw)!);
                    if (status.RootElement.GetProperty("done").GetBoolean())
                    {
                        Assert.IsFalse(status.RootElement.TryGetProperty("error", out var error), error.ValueKind == JsonValueKind.Undefined ? "" : error.GetString());
                        return status.RootElement.Clone();
                    }
                    Pump(Task.Delay(50));
                }
                throw new TimeoutException("Hidden editor interchange did not complete.");
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(oldContext); }
    }

    private void VerifyHiddenController()
    {
        // No WS_VISIBLE, ShowWindow, focus, activation or global input. This verifies scripting only.
        using var window = new HwndSource(new HwndSourceParameters("Dango synthetic editor check")
        { Width = 800, Height = 600, WindowStyle = unchecked((int)0x80000000) });
        Assert.IsFalse(IsWindowVisible(window.Handle));
        var userData = Path.Combine(TestRepository.Root, "artifacts", "accessory-editor-background", Guid.NewGuid().ToString("N"));
        using var runtime = new AccessoryEditorRuntime();
        var initialized = Pump(runtime.InitializeAsync(window.Handle, Path.Combine(AppContext.BaseDirectory, "AccessoryEditor"), userData));
        Assert.IsTrue(initialized, runtime.ErrorMessage);
        Assert.AreEqual(AccessoryEditorRuntimeState.Ready, runtime.State);
        TestContext.WriteLine("Runtime: " + runtime.RuntimeVersion);
        var core = runtime.Core!;
        var capabilities = Pump(core.ExecuteScriptAsync("JSON.stringify({load:typeof window.DangoMiniPaint?.load,project:typeof window.DangoMiniPaint?.exportProject,png:typeof window.DangoMiniPaint?.exportPng})"));
        StringAssert.Contains(capabilities, "function");
        const string check = """
            window.dangoCheck = {done:false};
            (async () => {
              try {
                const c=document.createElement('canvas');c.width=12;c.height=8;
                const x=c.getContext('2d');x.fillStyle='#885e3e';x.fillRect(2,2,6,4);
                await DangoMiniPaint.load({width:12,height:8,pngBase64:c.toDataURL('image/png').split(',')[1]});
                const png=DangoMiniPaint.exportPng().pngBase64, project=DangoMiniPaint.exportProject();
                await DangoMiniPaint.load(project);
                window.dangoCheck={done:true,equal:png===DangoMiniPaint.exportPng().pngBase64,layers:project.layers.length,
                  safe:!JSON.stringify(project).includes('https:')&&!JSON.stringify(project).includes('file:'),
                  width:project.width,height:project.height};
              } catch(e) { window.dangoCheck={done:true,error:e.name+': '+e.message}; }
            })();
            """;
        Pump(core.ExecuteScriptAsync(check));
        string result = "";
        for (var i = 0; i < 100; i++)
        {
            result = Pump(core.ExecuteScriptAsync("JSON.stringify(window.dangoCheck)"));
            var decoded = JsonSerializer.Deserialize<string>(result)!;
            using var status = JsonDocument.Parse(decoded);
            if (status.RootElement.GetProperty("done").GetBoolean()) break;
            Pump(Task.Delay(50));
        }
        TestContext.WriteLine(result);
        using var final = JsonDocument.Parse(JsonSerializer.Deserialize<string>(result)!);
        var value = final.RootElement;
        Assert.IsFalse(value.TryGetProperty("error", out _), result);
        Assert.IsTrue(value.GetProperty("done").GetBoolean(), result);
        Assert.IsTrue(value.GetProperty("equal").GetBoolean(), result);
        Assert.IsTrue(value.GetProperty("safe").GetBoolean(), result);
        Assert.AreEqual(12, value.GetProperty("width").GetInt32());
        Assert.AreEqual(8, value.GetProperty("height").GetInt32());
        Assert.IsFalse(IsWindowVisible(window.Handle));
        Pump(core.ExecuteScriptAsync("location.href='https://blocked-editor-check.invalid/';"));
        Pump(Task.Delay(100));
        Assert.AreEqual(AccessoryEditorBridge.EntryPoint, core.Source, "External navigation must be cancelled before issuing a request.");
        // Crash only this isolated page's renderer; do not kill a shared or user-owned process.
        var crash = core.CallDevToolsProtocolMethodAsync("Page.crash", "{}");
        try { Pump(crash.WaitAsync(TimeSpan.FromSeconds(3))); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException) { }
        for (var i = 0; i < 50 && runtime.State != AccessoryEditorRuntimeState.Failed; i++) Pump(Task.Delay(50));
        Assert.AreEqual(AccessoryEditorRuntimeState.Failed, runtime.State);
        StringAssert.Contains(runtime.ErrorMessage, "草稿仍保留");
        Assert.IsTrue(Pump(runtime.InitializeAsync(window.Handle, Path.Combine(AppContext.BaseDirectory, "AccessoryEditor"), userData)), runtime.ErrorMessage);
        Assert.IsFalse(IsWindowVisible(window.Handle));
        runtime.Dispose();
        Assert.AreEqual(AccessoryEditorRuntimeState.Closed, runtime.State);
    }

    [STATestMethod]
    public void HiddenUnifiedProtocolReturnsOneCompleteRigDraftAndOnlyOuterApplyPersistsIt()
    {
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-editor-e2-protocol", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var pixels = new byte[320 * 240 * 4]; new Random(42).NextBytes(pixels);
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = i < 320 * 4 ? (byte)0 : (byte)255;
            var source = AccessoryEditorSourceAsset.FromBitmap(BitmapSource.Create(320, 240, 96, 96, PixelFormats.Bgra32, null, pixels, 320 * 4));
            var sourcePath = Path.Combine(root, "fictional-source.png"); File.WriteAllBytes(sourcePath, source.CopyPngBytes());
            var settings = Path.Combine(root, "settings");
            var outer = new AccessorySettingsSession(settings);
            var item = outer.ImportSource(sourcePath); var ticket = outer.BeginEdit(item.Id);
            using var modal = new AccessoryUnifiedEditorSession(ticket, outer.GetPendingSource(item.AssetId), null);
            using var window = new HwndSource(new HwndSourceParameters("Dango synthetic complete editor protocol")
            { Width = 800, Height = 600, WindowStyle = unchecked((int)0x80000000) });
            using var runtime = new AccessoryEditorRuntime();
            Assert.IsTrue(Pump(runtime.InitializeAsync(window.Handle, Path.Combine(AppContext.BaseDirectory, "AccessoryEditor"), Path.Combine(root, "profile"))), runtime.ErrorMessage);
            var trace = new List<string>();
            runtime.MessageReceived += (origin, json) =>
            {
                using var received = JsonDocument.Parse(json);
                var message = received.RootElement;
                trace.Add(message.GetProperty("type").GetString() + ":" + string.Join(",", message.GetProperty("payload").EnumerateObject().Select(p => p.Name)));
                modal.Receive(origin, json);
            };
            var outgoingChunks = 0; var previews = 0;
            modal.Outgoing += json =>
            {
                using var message = JsonDocument.Parse(json);
                if (message.RootElement.GetProperty("type").GetString() == "Chunk") outgoingChunks++;
                Assert.IsTrue(runtime.Post(json));
            };
            modal.PreviewReady += _ => previews++;
            modal.Open(); Wait(() => modal.State != AccessoryUnifiedEditorState.Loading);
            Assert.AreEqual(AccessoryUnifiedEditorState.Ready, modal.State, "Open: " + modal.ErrorMessage + " [" + string.Join(";", trace) + "]");
            Assert.IsTrue(outgoingChunks > 1, "The fixture must exercise multi-chunk Open.");
            modal.SetGeometry(new(.5, .25, Array.AsReadOnly(new AccessoryRigNode[] { new(.5, .25), new(.5, .85) }), false));
            modal.UpdateParameters(modal.Parameters with { DeformationMode = AccessoryDeformationMode.SingleChain, GravityInfluence = 42 });
            Assert.IsTrue(modal.RequestSnapshot(confirm: false)); Wait(() => modal.State != AccessoryUnifiedEditorState.Busy);
            Assert.AreEqual(AccessoryUnifiedEditorState.Ready, modal.State, "Preview: " + modal.ErrorMessage + " [" + string.Join(";", trace) + "]"); Assert.AreEqual(1, previews);
            Assert.IsTrue(outer.DraftConfiguration.Items.Single(i => i.Id == item.Id).IsPendingCompletion);
            Assert.IsFalse(Directory.Exists(settings), "A modal preview cannot create persistent assets.");
            Assert.IsTrue(modal.RequestSnapshot(confirm: true)); Wait(() => modal.State != AccessoryUnifiedEditorState.Busy);
            Assert.AreEqual(AccessoryUnifiedEditorState.Closed, modal.State, modal.ErrorMessage);
            var result = modal.Result; Assert.IsNotNull(result);
            Assert.AreEqual(42d, result.Item.GravityInfluence); Assert.AreEqual(2, result.Item.Rig!.Nodes.Count);
            Assert.IsTrue(outer.TryCompleteEdit(ticket, result)); Assert.IsFalse(Directory.Exists(settings));
            Assert.IsTrue(outer.TrySave(), outer.StatusMessage);
            var reopened = new AccessorySettingsSession(settings);
            Assert.AreEqual(result.Item, reopened.SavedConfiguration.Items.Single(i => i.Id == item.Id));
            Assert.AreEqual(result.Project.ProjectId, reopened.TryGetProject(item.Id)!.ProjectId);
            Assert.IsFalse(IsWindowVisible(window.Handle));
            TestContext.WriteLine("Hidden packaged editor: chunked Open/Preview/Confirm, native rig validation, outer Apply/reopen passed.");
            void Wait(Func<bool> condition)
            {
                var until = DateTime.UtcNow.AddSeconds(15);
                while (!condition() && DateTime.UtcNow < until) Pump(Task.Delay(10));
                Assert.IsTrue(condition(), modal.ErrorMessage ?? "Hidden modal protocol timed out.");
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(oldContext); }
    }

    private static T Pump<T>(Task<T> task)
    {
        Pump((Task)task); return task.GetAwaiter().GetResult();
    }
    private static void Pump(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
