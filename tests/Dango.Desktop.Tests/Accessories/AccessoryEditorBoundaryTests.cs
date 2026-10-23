using System.Security.Cryptography;
using System.Text.Json;
using Dango.Desktop.Accessories.Editing;
using Microsoft.Web.WebView2.Core;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryEditorBoundaryTests
{
    [TestMethod]
    public void BridgeRejectsWrongOriginIdentityRevisionDuplicateFieldsAndOversizedInput()
    {
        using var bridge = new AccessoryEditorBridge(7);
        var valid = bridge.Serialize("Preview", new { });
        Assert.IsTrue(bridge.TryRead(AccessoryEditorBridge.EntryPoint, valid, out var message));
        Assert.AreEqual("Preview", message!.Type);
        foreach (var source in new[] { "file:///index.html", "https://elsewhere.invalid/index.html", AccessoryEditorBridge.EntryPoint + "?x=1" })
            Assert.IsFalse(bridge.TryRead(source, valid, out _));
        foreach (var invalid in new[]
        {
            valid.Replace(bridge.SessionId, Guid.NewGuid().ToString("N")),
            valid.Replace(bridge.RequestId, Guid.NewGuid().ToString("N")),
            valid.Replace("\"revision\":7", "\"revision\":8"),
            valid.Replace("\"protocol\":1", "\"protocol\":2"),
            valid.Replace("\"protocol\":1", "\"protocol\":1,\"protocol\":1"),
            valid.Replace("\"protocol\":1", "\"protocol\":\"1\""),
            valid.Replace("\"Preview\"", "\"ArbitraryFile\""),
            valid.Replace("\"payload\":{}", "\"payload\":null"),
            new string('x', AccessoryEditorBridge.MaximumMessageBytes + 1),
        }) Assert.IsFalse(bridge.TryRead(AccessoryEditorBridge.EntryPoint, invalid, out _));
    }

    [TestMethod]
    public void ReadyDoesNotPutATimeLimitOnHumanEditingButInitializationAndTransfersExpire()
    {
        var clock = new TestClock();
        using var bridge = new AccessoryEditorBridge(0, clock);
        Assert.IsTrue(bridge.TryRead(AccessoryEditorBridge.EntryPoint, bridge.Serialize("Ready", new { }), out _));
        clock.Advance(TimeSpan.FromHours(1));
        Assert.IsTrue(bridge.TryRead(AccessoryEditorBridge.EntryPoint, bridge.Serialize("Preview", new { }), out _));
        bridge.BeginTransfer(1, new string('a', 64));
        clock.Advance(AccessoryEditorBridge.OperationTimeout + TimeSpan.FromSeconds(1));
        Assert.ThrowsExactly<InvalidDataException>(() => bridge.AppendChunk(0, "YQ=="));
        Assert.IsTrue(bridge.IsClosed);
        using var neverReady = new AccessoryEditorBridge(0, clock);
        clock.Advance(AccessoryEditorBridge.OperationTimeout + TimeSpan.FromSeconds(1));
        Assert.IsFalse(neverReady.TryRead(AccessoryEditorBridge.EntryPoint, neverReady.Serialize("Ready", new { }), out _));
        Assert.IsTrue(neverReady.IsClosed);
    }

    [TestMethod]
    public void TransfersRequireOrderedBoundedChunksAndContentHashAndCloseInvalidatesLateResults()
    {
        var content = new byte[AccessoryEditorBridge.MaximumChunkBytes + 19];
        new Random(73).NextBytes(content);
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        using var bridge = new AccessoryEditorBridge(4);
        bridge.BeginTransfer(content.Length, hash);
        Assert.IsNull(bridge.AppendChunk(0, Convert.ToBase64String(content, 0, AccessoryEditorBridge.MaximumChunkBytes)));
        CollectionAssert.AreEqual(content, bridge.AppendChunk(1, Convert.ToBase64String(content, AccessoryEditorBridge.MaximumChunkBytes, 19))!);
        bridge.Dispose();
        Assert.IsFalse(bridge.TryRead(AccessoryEditorBridge.EntryPoint, bridge.Serialize("Confirm", new { }), out _));
        using var reordered = new AccessoryEditorBridge(4);
        reordered.BeginTransfer(1, hash);
        Assert.ThrowsExactly<InvalidDataException>(() => reordered.AppendChunk(1, "YQ=="));
        Assert.IsTrue(reordered.IsClosed);
        using var corrupt = new AccessoryEditorBridge(4);
        corrupt.BeginTransfer(1, hash);
        Assert.ThrowsExactly<InvalidDataException>(() => corrupt.AppendChunk(0, "YQ=="));
        using var oversized = new AccessoryEditorBridge(4);
        Assert.ThrowsExactly<InvalidDataException>(() => oversized.BeginTransfer(AccessoryEditorBridge.MaximumTransferBytes + 1, hash));
    }

    [TestMethod]
    public void BrowserResourcesCannotEscapeTheVerifiedLocalInventory()
    {
        var files = new HashSet<string> { "index.html", "bundle.js", "theme.css" };
        Assert.IsTrue(AccessoryEditorRuntime.IsAllowedResource(AccessoryEditorBridge.EntryPoint, CoreWebView2WebResourceContext.Document, files));
        Assert.IsTrue(AccessoryEditorRuntime.IsAllowedResource(AccessoryEditorBridge.Origin + "/bundle.js", CoreWebView2WebResourceContext.Script, files));
        foreach (var uri in new[]
        {
            "https://external.invalid/image.png", "file:///C:/private.png", "https://editor.dango.invalid:1234/bundle.js",
            "https://user@editor.dango.invalid/bundle.js", AccessoryEditorBridge.Origin + "/bundle.js?url=other",
            AccessoryEditorBridge.Origin + "/private.png", AccessoryEditorBridge.Origin + "/%2e%2e/private.png",
            "blob:https://external.invalid/private", "data:text/html,hello",
        }) Assert.IsFalse(AccessoryEditorRuntime.IsAllowedResource(uri, CoreWebView2WebResourceContext.Image, files), uri);
        Assert.IsTrue(AccessoryEditorRuntime.IsAllowedResource("data:image/png;base64,YQ==", CoreWebView2WebResourceContext.Image, files));
        Assert.IsFalse(AccessoryEditorRuntime.IsAllowedResource("data:image/png;base64,YQ==", CoreWebView2WebResourceContext.Script, files));
    }

    [STATestMethod]
    public void MissingOrOldRuntimeReturnsRecoverableFailureAndCanRetryWithoutASystemInstall()
    {
        var probes = 0;
        using var runtime = new AccessoryEditorRuntime(() => { probes++; throw new WebView2RuntimeNotFoundException(); });
        for (var i = 0; i < 2; i++)
        {
            Assert.IsFalse(runtime.InitializeAsync(0, "unused", "unused").GetAwaiter().GetResult());
            Assert.AreEqual(AccessoryEditorRuntimeState.Failed, runtime.State);
            StringAssert.Contains(runtime.ErrorMessage, "草稿仍保留");
        }
        Assert.AreEqual(2, probes);
        using var old = new AccessoryEditorRuntime(() => "100.0.0.0");
        Assert.IsFalse(old.InitializeAsync(0, "unused", "unused").GetAwaiter().GetResult());
        runtime.Dispose();
        Assert.AreEqual(AccessoryEditorRuntimeState.Closed, runtime.State);
        Assert.ThrowsExactly<ObjectDisposedException>(() => runtime.InitializeAsync(0, "unused", "unused").GetAwaiter().GetResult());
    }

    [TestMethod]
    public void LocalAssetsRejectCorruptionTraversalAndWrongSourceCommit()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-editor-boundary", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "licenses"));
        var files = new[] { "index.html", "bundle.js", "theme.css", "licenses/MIT-minipaint.txt" };
        foreach (var file in files) File.WriteAllText(Path.Combine(root, file), "synthetic resource");
        void Manifest(string commit = AccessoryEditorAssets.SourceCommit, bool traverse = false) =>
            File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new
            {
                sourceCommit = commit, version = AccessoryEditorAssets.BundleVersion,
                files = files.Select((file, i) => new
                {
                    path = traverse && i == 0 ? "../index.html" : file,
                    sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, file)))),
                    bytes = new FileInfo(Path.Combine(root, file)).Length,
                }),
            }));
        Manifest(); Assert.AreEqual(4, AccessoryEditorAssets.Verify(root).Count);
        File.AppendAllText(Path.Combine(root, "bundle.js"), "corruption");
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorAssets.Verify(root));
        Manifest("unrecognized"); Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorAssets.Verify(root));
        Manifest(traverse: true); Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorAssets.Verify(root));
    }

    private sealed class TestClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        internal void Advance(TimeSpan duration) => ticks += duration.Ticks;
    }
}
