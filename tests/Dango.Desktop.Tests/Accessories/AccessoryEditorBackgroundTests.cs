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
                var priorGroups = tied["skeleton"]!["layers"]!.AsArray()
                    .ToDictionary(layer => layer!["id"]!.GetValue<string>(), layer => layer!["groupId"]?.DeepClone(), StringComparer.Ordinal);
                var paintOrder = tied["layers"]!.AsArray().Select((layer, index) =>
                    (Id: layer!["id"]!.GetValue<int>().ToString(System.Globalization.CultureInfo.InvariantCulture),
                     Order: layer!["order"]!.GetValue<int>(), Index: index))
                    .OrderByDescending(layer => layer.Order).ThenBy(layer => layer.Index).Reverse().ToArray();
                var orderedSkeletonLayers = new JsonArray();
                foreach (var layer in paintOrder)
                {
                    var skeletonLayer = new JsonObject { ["id"] = layer.Id };
                    skeletonLayer["groupId"] = priorGroups[layer.Id]?.DeepClone();
                    orderedSkeletonLayers.Add(skeletonLayer);
                }
                tied["skeleton"]!["layers"] = orderedSkeletonLayers;
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
                const probe=new Image();window.dangoDecodeProbe={loaded:false,decoded:false,visibility:document.visibilityState};
                probe.onload=()=>window.dangoDecodeProbe.loaded=true;
                probe.src=c.toDataURL('image/png');probe.decode().then(()=>window.dangoDecodeProbe.decoded=true);
                await DangoMiniPaint.load({width:12,height:8,pngBase64:c.toDataURL('image/png').split(',')[1]});
                const png=DangoMiniPaint.exportPng().pngBase64, project=DangoMiniPaint.exportProject();
                await DangoMiniPaint.load(project);
                let corruptRejected=false;
                try {await DangoMiniPaint.load({width:12,height:8,pngBase64:btoa(atob(png).slice(0,33))});}
                catch {corruptRejected=true;}
                window.dangoCheck={done:true,equal:png===DangoMiniPaint.exportPng().pngBase64,layers:project.layers.length,
                  corruptRejected,
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
        Assert.IsTrue(value.GetProperty("done").GetBoolean(), result + " decode=" + Pump(core.ExecuteScriptAsync("JSON.stringify(window.dangoDecodeProbe)")));
        Assert.IsTrue(value.GetProperty("equal").GetBoolean(), result);
        Assert.IsTrue(value.GetProperty("corruptRejected").GetBoolean(), "A valid PNG header with missing image data must reject without replacing the prior project.");
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
    public void HiddenUnifiedProtocolReturnsOneCompleteSkeletonDraftAndOnlyOuterApplyPersistsIt()
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
            var imported = outer.ImportSource(sourcePath);
            outer.Stage(new(outer.DraftConfiguration.Items.Select(i => i.Id == imported.Id ? i with { GravityInfluence = 42 } : i)));
            var item = outer.DraftConfiguration.Items.Single(i => i.Id == imported.Id); var ticket = outer.BeginEdit(item.Id);
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
            Pump(runtime.Core!.ExecuteScriptAsync("""
                window.dangoSkeletonSetup={done:false};(async()=>{try{
                  const api=DangoMiniPaint,s=api.getSkeleton();s.pivot={id:'P',x:160,y:60};
                  s.groups=[{id:'group-main',name:'骨架',targetId:null,nodes:[{id:'R',x:160,y:60},{id:'J',x:160,y:204}]}];
                  s.layers=s.layers.map(l=>({...l,groupId:'group-main'}));await api.setSkeleton(s);
                  window.dangoSkeletonSetup={done:true};
                }catch(e){window.dangoSkeletonSetup={done:true,error:e.message};}})();
                """));
            Wait(() => Pump(runtime.Core.ExecuteScriptAsync("Boolean(window.dangoSkeletonSetup?.done)")) == "true");
            Assert.AreEqual("false", Pump(runtime.Core.ExecuteScriptAsync("Boolean(window.dangoSkeletonSetup.error)")));
            Assert.IsTrue(modal.RequestSnapshot(confirm: false)); Wait(() => modal.State != AccessoryUnifiedEditorState.Busy);
            Assert.AreEqual(AccessoryUnifiedEditorState.Ready, modal.State, "Preview: " + modal.ErrorMessage + " [" + string.Join(";", trace) + "]"); Assert.AreEqual(1, previews);
            Assert.IsTrue(outer.DraftConfiguration.Items.Single(i => i.Id == item.Id).IsPendingCompletion);
            Assert.IsFalse(Directory.Exists(settings), "A modal preview cannot create persistent assets.");
            Assert.IsTrue(modal.RequestSnapshot(confirm: true)); Wait(() => modal.State != AccessoryUnifiedEditorState.Busy);
            Assert.AreEqual(AccessoryUnifiedEditorState.Closed, modal.State, modal.ErrorMessage);
            var result = modal.Result; Assert.IsNotNull(result);
            Assert.AreEqual(42d, result.Item.GravityInfluence); Assert.AreEqual(2, result.Item.Skeleton!.Groups.Single().Nodes.Count);
            Assert.IsTrue(outer.TryCompleteEdit(ticket, result)); Assert.IsFalse(Directory.Exists(settings));
            Assert.IsTrue(outer.TrySave(), outer.StatusMessage);
            var reopened = new AccessorySettingsSession(settings);
            Assert.AreEqual(result.Item, reopened.SavedConfiguration.Items.Single(i => i.Id == item.Id));
            Assert.AreEqual(result.Project.ProjectId, reopened.TryGetProject(item.Id)!.ProjectId);
            Assert.IsFalse(IsWindowVisible(window.Handle));
            TestContext.WriteLine("Hidden packaged editor: chunked Open/Preview/Confirm, native skeleton validation, outer Apply/reopen passed.");
            void Wait(Func<bool> condition)
            {
                var until = DateTime.UtcNow.AddSeconds(15);
                while (!condition() && DateTime.UtcNow < until) Pump(Task.Delay(10));
                Assert.IsTrue(condition(), modal.ErrorMessage ?? "Hidden modal protocol timed out.");
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(oldContext); }
    }

    [STATestMethod]
    public void HiddenPackagedBrowserRoundTripsConnectedSkeletonThroughNativeSessionAndBridge()
    {
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            var artifactRoot = Path.Combine(TestRepository.Root, "artifacts", "accessory-editor-background-connected", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(artifactRoot);
            var settingsRoot = Path.Combine(artifactRoot, "settings");
            var outer = new AccessorySettingsSession(settingsRoot);
            var savedConfiguration = outer.SavedConfiguration;
            var pixels = new byte[64 * 48 * 4];
            for (var y = 5; y < 43; y++) for (var x = 7; x < 57; x++)
            {
                var index = (y * 64 + x) * 4;
                pixels[index] = (byte)(x * 3); pixels[index + 1] = (byte)(y * 4); pixels[index + 2] = 90; pixels[index + 3] = 255;
            }
            var source = AccessoryEditorSourceAsset.FromBitmap(BitmapSource.Create(64, 48, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4));
            var project = new AccessoryEditorProject(64, 48, 9, source.AssetId,
                [new("native-layer", "虚构图片", source.AssetId, 0, 0, 64, 48)], [source], name: "虚构共享骨架工程");
            var item = new AccessoryItem("fictional-connected-skeleton", "虚构配饰", source.AssetId, 64, 48)
            {
                IsEquipped = true, Layer = AccessoryLayer.Behind, Order = 4, SizePercent = 137,
                BaseAngleDegrees = 17, BodyAnchorX = .23, BodyAnchorY = .71, ConnectionStrength = 63,
                MaxConnectionTravelPercent = 82, RotationDamping = 47, LockRotation = false,
                ReturnStrength = 58, MaxSwingDegrees = 73, LimitSwing = false, GravityInfluence = 31,
            };
            var draft = new AccessoryEditDraft("fictional-hidden-bridge-session", 1, item.Id, project.Revision, item);
            using var modal = new AccessoryUnifiedEditorSession(draft, null, project);
            using var window = new HwndSource(new HwndSourceParameters("Dango hidden connected skeleton bridge check")
            { Width = 800, Height = 600, WindowStyle = unchecked((int)0x80000000) });
            AssertHidden();
            using var runtime = new AccessoryEditorRuntime();
            Assert.IsTrue(Pump(runtime.InitializeAsync(window.Handle, Path.Combine(AppContext.BaseDirectory, "AccessoryEditor"), Path.Combine(artifactRoot, "profile"))), runtime.ErrorMessage);
            AssertHidden();

            var trace = new List<string>();
            var headers = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var outgoingChunks = 0;
            AccessoryEditResult? preview = null;
            var contentChanges = 0;
            runtime.MessageReceived += (origin, json) =>
            {
                AssertHidden();
                using var received = JsonDocument.Parse(json);
                var message = received.RootElement;
                var type = message.GetProperty("type").GetString()!;
                trace.Add(type);
                if (type is "Preview" or "Confirm") headers[type] = message.GetProperty("payload").Clone();
                modal.Receive(origin, json);
            };
            modal.Outgoing += json =>
            {
                AssertHidden();
                using var message = JsonDocument.Parse(json);
                if (message.RootElement.GetProperty("type").GetString() == "Chunk") outgoingChunks++;
                Assert.IsTrue(runtime.Post(json), "Native session message must reach the packaged editor.");
            };
            modal.PreviewReady += result => preview = result;
            modal.ContentChanged += () => contentChanges++;
            modal.Open();
            Wait(() => modal.State != AccessoryUnifiedEditorState.Loading, "Open");
            Assert.AreEqual(AccessoryUnifiedEditorState.Ready, modal.State, modal.ErrorMessage + " [" + string.Join(",", trace) + "]");
            Assert.IsTrue(outgoingChunks > 0, "The actual packaged bridge must carry the project through Open chunks.");
            AssertHidden();

            RunBrowserEdit("dangoConnectedSetup", """
                const api=window.DangoMiniPaint,base=api.getSkeleton();
                const groups=[
                  {id:'group-a',name:'虚构骨架 A',targetId:null,nodes:[{id:'aR',x:12,y:10},{id:'aJ',x:24,y:20}],parameters:{bend:.7,damping:6,restoration:5,gravity:0,elasticity:0}},
                  {id:'group-b',name:'虚构骨架 B',targetId:'aJ',nodes:[{id:'bR',x:24,y:20},{id:'bJ',x:38,y:31}],parameters:{bend:.65,damping:8,restoration:4,gravity:12,elasticity:.5}}
                ];
                const skeleton={...base,groups,layers:base.layers.map(layer=>({...layer,groupId:layer.id==='1'?'group-a':null}))};
                await api.setSkeleton(skeleton);
                return {skeleton:api.getSkeleton(),editSequence:api.getEditSequence()};
                """);
            var setup = LastBrowserEdit("dangoConnectedSetup");
            var setupSkeleton = AccessoryEditorProject.ReadSkeleton(setup.GetProperty("skeleton"), 64, 48);
            Assert.AreEqual(2, setupSkeleton.Groups.Count);
            Assert.AreEqual("aJ", setupSkeleton.Groups.Single(group => group.Id == "group-b").TargetId);
            Assert.AreEqual(new AccessorySkeletonParameters(), setupSkeleton.Groups.Single(group => group.Id == "group-a").Parameters);
            Assert.AreEqual(.5, setupSkeleton.Groups.Single(group => group.Id == "group-b").Parameters.Elasticity);
            Assert.AreEqual("group-a", setupSkeleton.Layers.Single(layer => layer.Id == "1").GroupId);
            Wait(() => contentChanges >= 1, "Changed after skeleton setup");
            AssertHidden();

            Assert.IsTrue(modal.RequestSnapshot(confirm: false));
            Wait(() => modal.State != AccessoryUnifiedEditorState.Busy, "Preview");
            Assert.AreEqual(AccessoryUnifiedEditorState.Ready, modal.State, modal.ErrorMessage + " [" + string.Join(",", trace) + "]");
            Assert.IsNotNull(preview);
            Assert.IsTrue(headers.TryGetValue("Preview", out var previewHeader));
            Assert.IsTrue(previewHeader.GetProperty("editSequence").GetInt64() >= setup.GetProperty("editSequence").GetInt64());
            Assert.AreEqual(2, preview!.Project.Skeleton.Groups.Count);
            Assert.AreEqual("aJ", preview.Project.Skeleton.Groups.Single(group => group.Id == "group-b").TargetId);
            Assert.AreEqual("group-a", preview.Project.Skeleton.Layers.Single(layer => layer.Id == "native-layer").GroupId);
            Assert.AreEqual(1, preview.Project.RenderPieces.Count);
            Assert.AreEqual("group-a", preview.Project.RenderPieces[0].GroupId);
            Assert.IsTrue(preview.Project.Assets.ContainsKey(preview.Project.RenderPieces[0].AssetId));
            Assert.AreEqual(64, preview.Project.Assets[preview.Project.RenderPieces[0].AssetId].PixelWidth);
            Assert.AreEqual(48, preview.Project.Assets[preview.Project.RenderPieces[0].AssetId].PixelHeight);
            Assert.AreEqual(savedConfiguration, outer.DraftConfiguration, "Preview cannot alter the outer native settings draft.");
            Assert.IsFalse(Directory.Exists(settingsRoot), "Bridge preview must not create a persistent settings store.");
            AssertHidden();

            RunBrowserEdit("dangoConnectedNudge", """
                const api=window.DangoMiniPaint,current=api.getSkeleton(),groups=current.groups.map(group=>({...group,nodes:group.nodes.map(node=>({...node}))}));
                const parent=groups.find(group=>group.id==='group-a'),child=groups.find(group=>group.id==='group-b');
                parent.nodes[1].x+=1;parent.nodes[1].y+=2;child.nodes[0].x=parent.nodes[1].x;child.nodes[0].y=parent.nodes[1].y;
                await api.setSkeleton({...current,groups});
                return {skeleton:api.getSkeleton(),editSequence:api.getEditSequence()};
                """);
            var nudged = LastBrowserEdit("dangoConnectedNudge");
            Wait(() => contentChanges >= 2, "Changed after shared-point nudge");
            Assert.IsTrue(modal.RequestSnapshot(confirm: true));
            Wait(() => modal.State != AccessoryUnifiedEditorState.Busy, "Confirm");
            Assert.AreEqual(AccessoryUnifiedEditorState.Closed, modal.State, modal.ErrorMessage + " [" + string.Join(",", trace) + "]");
            Assert.IsNotNull(modal.Result);
            Assert.IsTrue(headers.TryGetValue("Confirm", out var confirmHeader));
            Assert.IsTrue(confirmHeader.GetProperty("editSequence").GetInt64() >= nudged.GetProperty("editSequence").GetInt64());
            var result = modal.Result!;
            var savedParent = result.Project.Skeleton.Groups.Single(group => group.Id == "group-a");
            var savedChild = result.Project.Skeleton.Groups.Single(group => group.Id == "group-b");
            Assert.AreEqual(25d, savedParent.Nodes[1].X); Assert.AreEqual(22d, savedParent.Nodes[1].Y);
            Assert.AreEqual(savedParent.Nodes[1].X, savedChild.Nodes[0].X); Assert.AreEqual(savedParent.Nodes[1].Y, savedChild.Nodes[0].Y);
            Assert.AreEqual("aJ", savedChild.TargetId);
            Assert.AreEqual(.5, savedChild.Parameters.Elasticity);
            Assert.AreEqual(item.IsEquipped, result.Item.IsEquipped); Assert.AreEqual(item.Layer, result.Item.Layer);
            Assert.AreEqual(item.Order, result.Item.Order); Assert.AreEqual(item.SizePercent, result.Item.SizePercent);
            Assert.AreEqual(item.BaseAngleDegrees, result.Item.BaseAngleDegrees);
            Assert.AreEqual(item.BodyAnchorX, result.Item.BodyAnchorX); Assert.AreEqual(item.BodyAnchorY, result.Item.BodyAnchorY);
            Assert.AreEqual(item.ConnectionStrength, result.Item.ConnectionStrength);
            Assert.AreEqual(item.MaxConnectionTravelPercent, result.Item.MaxConnectionTravelPercent);
            Assert.AreEqual(item.RotationDamping, result.Item.RotationDamping); Assert.AreEqual(item.LockRotation, result.Item.LockRotation);
            Assert.AreEqual(item.ReturnStrength, result.Item.ReturnStrength); Assert.AreEqual(item.MaxSwingDegrees, result.Item.MaxSwingDegrees);
            Assert.AreEqual(item.LimitSwing, result.Item.LimitSwing); Assert.AreEqual(item.GravityInfluence, result.Item.GravityInfluence);
            Assert.AreEqual(savedConfiguration, outer.DraftConfiguration, "Confirm returns a result; only outer Apply can persist or stage it.");
            Assert.IsFalse(Directory.Exists(settingsRoot), "Confirm must not write persistent native settings.");
            Assert.IsTrue(contentChanges >= 1, "The real page should deliver its coalesced Changed revision through native bridge.");
            AssertHidden();
            TestContext.WriteLine("Hidden packaged bridge roundtrip passed: Open/Ready, API-edited shared roots, Preview/Confirm editSequence headers, native render piece validation, and no outer persistence. Runtime " + runtime.RuntimeVersion);

            void AssertHidden() => Assert.IsFalse(IsWindowVisible(window.Handle), "The background WebView host must remain non-visible.");

            void Wait(Func<bool> condition, string operation)
            {
                var until = DateTime.UtcNow.AddSeconds(20);
                while (!condition() && DateTime.UtcNow < until)
                {
                    AssertHidden();
                    Pump(Task.Delay(10));
                }
                AssertHidden();
                Assert.IsTrue(condition(), $"Hidden bridge {operation} timed out: {modal.ErrorMessage} [{string.Join(",", trace)}]");
            }

            void RunBrowserEdit(string stateName, string body)
            {
                var encodedName = JsonSerializer.Serialize(stateName);
                var script = "window[" + encodedName + "]={done:false};(async()=>{try{window[" + encodedName + "].value=await (async()=>{" + body +
                    "})();}catch(error){window[" + encodedName + "].error=error.name+': '+error.message;}finally{window[" + encodedName + "].done=true;}})();";
                Pump(runtime.Core!.ExecuteScriptAsync(script));
                _ = LastBrowserEdit(stateName);
            }

            JsonElement LastBrowserEdit(string stateName)
            {
                var until = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < until)
                {
                    AssertHidden();
                    var raw = Pump(runtime.Core!.ExecuteScriptAsync($"JSON.stringify(window[{JsonSerializer.Serialize(stateName)}])"));
                    using var status = JsonDocument.Parse(JsonSerializer.Deserialize<string>(raw)!);
                    var value = status.RootElement;
                    if (value.GetProperty("done").GetBoolean())
                    {
                        Assert.IsFalse(value.TryGetProperty("error", out var error), error.ValueKind == JsonValueKind.Undefined ? "" : error.GetString());
                        return value.GetProperty("value").Clone();
                    }
                    Pump(Task.Delay(10));
                }
                throw new TimeoutException($"Hidden browser edit {stateName} did not complete.");
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(oldContext); }
    }

    [STATestMethod]
    [DataRow("erase")]
    [DataRow("magic_erase")]
    [DataRow("fill")]
    public void HiddenPackagedNativeOpenSupportsRasterToolsWhenOriginalDimensionsAreNull(string tool)
    {
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            using var window = new HwndSource(new HwndSourceParameters("Dango fictional raster regression")
            { Width = 1120, Height = 760, WindowStyle = unchecked((int)0x80000000) });
            Assert.IsFalse(IsWindowVisible(window.Handle));
            using var runtime = new AccessoryEditorRuntime();
            var profile = Path.Combine(TestRepository.Root, "artifacts", "accessory-native-raster", Guid.NewGuid().ToString("N"));
            Assert.IsTrue(Pump(runtime.InitializeAsync(window.Handle, Path.Combine(AppContext.BaseDirectory, "AccessoryEditor"), profile)), runtime.ErrorMessage);
            var pixels = new byte[64 * 48 * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            { pixels[i] = 107; pixels[i + 1] = 157; pixels[i + 2] = 194; pixels[i + 3] = 255; }
            var source = AccessoryEditorSourceAsset.FromBitmap(BitmapSource.Create(64, 48, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4));
            var nativeOpen = new AccessoryBrowserProjectAdapter().CreateOpenProject(source);
            Assert.AreEqual(JsonValueKind.Null, nativeOpen.GetProperty("layers")[0].GetProperty("width_original").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, nativeOpen.GetProperty("layers")[0].GetProperty("height_original").ValueKind);
            var script = "window.rasterRegression={done:false};(async()=>{try{const api=DangoMiniPaint;await api.load(" + nativeOpen.GetRawText() + ");const tool=" + JsonSerializer.Serialize(tool) + ";" + """
                async function settle(){for(let i=0;i<100;i++){try{api.exportProject();return;}catch{}await new Promise(r=>setTimeout(r,10));}throw Error('raster action did not settle');}
                const before=api.exportPng().pngBase64;document.getElementById(tool).click();await settle();
                const p=new DOMPoint(16,24).matrixTransform(document.getElementById('geometry_overlay').getScreenCTM()),canvas=document.getElementById('canvas_minipaint');
                for(const type of ['mousemove','mousedown','mouseup'])canvas.dispatchEvent(new MouseEvent(type,{bubbles:true,clientX:p.x,clientY:p.y,button:0,buttons:type==='mousedown'?1:0}));
                await settle();
                if(tool==='magic_erase'){canvas.dispatchEvent(new KeyboardEvent('keydown',{key:'Delete',bubbles:true}));await settle();}
                const after=await api.exportSnapshot(),image=new Image();await new Promise((resolve,reject)=>{image.onload=resolve;image.onerror=reject;image.src='data:image/png;base64,'+after.png.pngBase64;});
                const c=document.createElement('canvas');c.width=64;c.height=48;const x=c.getContext('2d');x.drawImage(image,0,0);const pixel=[...x.getImageData(16,24,1,1).data];
                document.getElementById('undo_button').click();await settle();const undo=api.exportPng().pngBase64===before;
                await api.load(after.project);window.rasterRegression.value={pixel,undo,reopen:api.exportPng().pngBase64===after.png.pngBase64,
                  width:after.project.layers[0].width_original,height:after.project.layers[0].height_original,status:document.getElementById('editor_status')?.textContent||''};
                }catch(e){window.rasterRegression.error=e.name+': '+e.message;}finally{window.rasterRegression.done=true;}})();
                """;
            Pump(runtime.Core!.ExecuteScriptAsync(script));
            var until = DateTime.UtcNow.AddSeconds(20); JsonElement result = default;
            while (DateTime.UtcNow < until)
            {
                Assert.IsFalse(IsWindowVisible(window.Handle));
                var raw = Pump(runtime.Core.ExecuteScriptAsync("JSON.stringify(window.rasterRegression)"));
                using var status = JsonDocument.Parse(JsonSerializer.Deserialize<string>(raw)!);
                if (status.RootElement.GetProperty("done").GetBoolean()) { result = status.RootElement.Clone(); break; }
                Pump(Task.Delay(20));
            }
            Assert.AreNotEqual(JsonValueKind.Undefined, result.ValueKind, "Hidden raster regression timed out.");
            Assert.IsFalse(result.TryGetProperty("error", out var error), error.ValueKind == JsonValueKind.Undefined ? "" : error.GetString());
            var value = result.GetProperty("value");
            Assert.AreEqual(64, value.GetProperty("width").GetInt32()); Assert.AreEqual(48, value.GetProperty("height").GetInt32());
            var actual = value.GetProperty("pixel").EnumerateArray().Select(p => p.GetInt32()).ToArray();
            if (tool == "fill") CollectionAssert.AreEqual(new[] { 136, 94, 62, 255 }, actual);
            else Assert.AreEqual(0, actual[3], "Raster removal must change real PNG pixels.");
            Assert.IsTrue(value.GetProperty("undo").GetBoolean()); Assert.IsTrue(value.GetProperty("reopen").GetBoolean());
            Assert.IsFalse(value.GetProperty("status").GetString()!.Contains("操作未完成", StringComparison.Ordinal));
            Assert.IsFalse(IsWindowVisible(window.Handle));
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
