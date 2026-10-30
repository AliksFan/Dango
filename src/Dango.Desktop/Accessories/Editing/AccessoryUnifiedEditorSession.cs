using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dango.Desktop.Accessories.Rig;

namespace Dango.Desktop.Accessories.Editing;

internal enum AccessoryUnifiedEditorState { Loading, Ready, Busy, Failed, Closed }

/// <summary>Modal-owned, storage-free transaction. Only complete validated snapshots escape this boundary.</summary>
internal sealed class AccessoryUnifiedEditorSession : IDisposable
{
    private readonly AccessoryEditDraft identity;
    private readonly TimeProvider clock;
    private readonly IAccessoryEditorClipboard clipboard;
    private readonly AccessoryBrowserProjectAdapter adapter = new();
    private AccessoryEditorProject? project;
    private AccessoryEditorBridge? bridge;
    private long? started;
    private string? operation;
    private bool receiving;
    private bool opened;
    private long editSequence = -1;
    private long clipboardSequence;
    private long? receivingEditSequence;
    private AccessoryEditorGeometry lastCompleteGeometry;
    internal AccessoryUnifiedEditorState State { get; private set; } = AccessoryUnifiedEditorState.Loading;
    internal AccessoryItem Parameters { get; private set; }
    internal AccessoryEditorGeometry Geometry { get; private set; }
    internal double RigSoftness { get; private set; }
    internal double RigDamping { get; private set; }
    internal double RigRestoration { get; private set; }
    internal string? ErrorMessage { get; private set; }
    internal AccessoryEditResult? Result { get; private set; }
    internal bool RestoredCheckpoint { get; private set; }
    internal event Action<string>? Outgoing;
    internal event Action? Changed;
    internal event Action<AccessoryEditResult>? PreviewReady;
    internal event Action? ContentChanged;
    internal event Action<string, double, double>? ViewCommandReceived;
    internal event Action? WearChanged;
    internal event Action<AccessorySimulationViewport>? SimulationViewportReceived;

    internal void PostSimulationState(string tool, string? error = null, bool updating = false)
    {
        if (bridge is null || State is not (AccessoryUnifiedEditorState.Ready or AccessoryUnifiedEditorState.Busy)) return;
        if (tool is not ("default" or "pick" or "pan" or "zoom")) throw new ArgumentException("Invalid simulation tool.", nameof(tool));
        Send("SimulationState", new { tool, error = error ?? "", updating });
    }

    internal void RequestWearEdit(double anchorX, double anchorY)
    {
        if (bridge is null || State is not (AccessoryUnifiedEditorState.Ready or AccessoryUnifiedEditorState.Busy)) return;
        if (!double.IsFinite(anchorX) || !double.IsFinite(anchorY) || anchorX is < 0 or > 1 || anchorY is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(anchorX));
        // The browser applies this through its ordinary history and returns WearChanged.
        Send("WearEdit", AccessoryWearParameters.ToPayload(Parameters with { BodyAnchorX = anchorX, BodyAnchorY = anchorY }));
    }
    internal AccessoryUnifiedEditorSession(AccessoryEditDraft identity, AccessoryEditorSourceAsset? source,
        AccessoryEditorProject? project, TimeProvider? clock = null, IAccessoryEditorClipboard? clipboard = null)
    {
        if ((source is null) == (project is null)) throw new ArgumentException("Provide exactly one source or editable project.");
        if (identity.ItemId != identity.Original.Id || identity.Revision < 0) throw new ArgumentException("Invalid modal identity.");
        this.identity = identity;
        this.project = project ?? new AccessoryEditorProject(source!.PixelWidth, source.PixelHeight, identity.Revision, source.AssetId,
            [new("layer-1", "图片", source.AssetId, 0, 0, source.PixelWidth, source.PixelHeight)], [source],
            new(new("P", identity.Original.ImagePivotX * source.PixelWidth, identity.Original.ImagePivotY * source.PixelHeight), [],
                [new("layer-1", null)]), identity.Original.Name);
        this.clock = clock ?? TimeProvider.System;
        this.clipboard = clipboard ?? new AccessoryEditorClipboard();
        Parameters = identity.Original;
        RigSoftness = Parameters.Rig?.Softness ?? .7; RigDamping = Parameters.Rig?.Damping ?? 6;
        RigRestoration = Parameters.Rig?.Restoration ?? 5;
        Geometry = new(Parameters.ImagePivotX, Parameters.ImagePivotY, Parameters.Rig?.Nodes ?? [], false);
        lastCompleteGeometry = Geometry;
    }

    internal void Open()
    {
        if (State == AccessoryUnifiedEditorState.Closed) return;
        if (opened) { Geometry = lastCompleteGeometry; RestoredCheckpoint = true; }
        opened = true;
        bridge?.Dispose(); bridge = new(identity.Revision, clock);
        receiving = false; receivingEditSequence = null; editSequence = -1; clipboardSequence = 0; operation = "Open"; started = clock.GetTimestamp();
        State = AccessoryUnifiedEditorState.Loading; ErrorMessage = null; Changed?.Invoke();
        try
        {
            var content = JsonSerializer.SerializeToUtf8Bytes(new
            { project = adapter.CreateOpenProject(project!, Parameters.Name, OpenSkeleton()) });
            if (content.Length > AccessoryEditorBridge.MaximumTransferBytes) throw new InvalidDataException("编辑工程超过传输上限。");
            Send("Open", new { bytes = content.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) });
            for (var offset = 0; offset < content.Length && State == AccessoryUnifiedEditorState.Loading; offset += AccessoryEditorBridge.MaximumChunkBytes)
                Send("Chunk", new { index = offset / AccessoryEditorBridge.MaximumChunkBytes,
                    base64 = Convert.ToBase64String(content, offset, Math.Min(AccessoryEditorBridge.MaximumChunkBytes, content.Length - offset)) });
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or JsonException)
        { Fail("编辑工程无法加载。草稿仍保留，可重试或取消。"); }
    }

    private AccessorySkeletonConfiguration OpenSkeleton()
    {
        var skeleton = Parameters.Skeleton ?? project!.Skeleton;
        if (Parameters.Skeleton is null)
            skeleton = skeleton with { Pivot = skeleton.Pivot with
            { X = Parameters.ImagePivotX * project!.PixelWidth, Y = Parameters.ImagePivotY * project.PixelHeight } };
        skeleton.Validate(project!.PixelWidth, project.PixelHeight);
        return skeleton;
    }

    internal bool RequestSnapshot(bool confirm)
    {
        if (State != AccessoryUnifiedEditorState.Ready) return false;

        operation = confirm ? "Confirm" : "Preview"; started = clock.GetTimestamp();
        State = AccessoryUnifiedEditorState.Busy; ErrorMessage = null; Changed?.Invoke();
        Send(operation, new { }); return true;
    }

    internal void UpdateParameters(AccessoryItem item)
    {
        if (State != AccessoryUnifiedEditorState.Ready) throw new InvalidOperationException("Editor is not ready.");
        if (item.Id != identity.ItemId) throw new ArgumentException("Item identity cannot change.");
        Parameters = item; Changed?.Invoke();
    }

    // Compatibility-only native API. Protocol 2 geometry is owned by complete project snapshots.
    internal void SetGeometry(AccessoryEditorGeometry geometry) =>
        throw new NotSupportedException("Protocol 2 geometry must be edited in the complete browser project.");

    internal void UpdateRigParameters(double softness, double damping, double restoration) =>
        throw new NotSupportedException("Protocol 2 group parameters belong to the complete browser project.");

    internal void Receive(string origin, string json)
    {
        if (State is AccessoryUnifiedEditorState.Closed or AccessoryUnifiedEditorState.Failed || bridge is null) return;
        CheckTimeout();
        if (State == AccessoryUnifiedEditorState.Failed || !bridge.TryRead(origin, json, out var message)) return;
        try
        {
            switch (message!.Type)
            {
                case "ViewCommand" when State is AccessoryUnifiedEditorState.Ready or AccessoryUnifiedEditorState.Busy:
                    var view = AccessoryEditorBridge.ExactObject(message.Payload, ["command", "x", "y"]);
                    var command = Text(view["command"]);
                    if (command is not ("window-drag" or "preview-show" or "preview-hide" or "simulation-tool" or "simulation-focus" or "simulation-refresh")) break;
                    var x = view["x"].GetDouble(); var y = view["y"].GetDouble();
                    if (!double.IsFinite(x) || !double.IsFinite(y)) break;
                    if (command == "simulation-tool")
                    {
                        if (x is < 0 or > 3 || x != Math.Truncate(x) || y != 0) break;
                    }
                    else if (command == "simulation-focus")
                    {
                        if (x is not (0 or 1) || y != 0) break;
                    }
                    else if (x != 0 || y != 0) break;
                    ViewCommandReceived?.Invoke(command, x, y); break;
                case "WearChanged" when State is AccessoryUnifiedEditorState.Ready or AccessoryUnifiedEditorState.Busy:
                    var wear = AccessoryWearParameters.Read(message.Payload, Parameters);
                    if (wear == Parameters) break;
                    Parameters = wear; WearChanged?.Invoke(); Changed?.Invoke(); break;
                case "SimulationViewport" when State is AccessoryUnifiedEditorState.Ready or AccessoryUnifiedEditorState.Busy:
                    SimulationViewportReceived?.Invoke(AccessorySimulationViewport.Read(message.Payload)); break;
                case "ClipboardCopy" when State == AccessoryUnifiedEditorState.Ready:
                    var copyHeader = AccessoryEditorBridge.ExactObject(message.Payload, ["clipboardId", "bytes", "sha256"]);
                    var copySequence = ReadEditSequence(copyHeader["clipboardId"]);
                    if (copySequence <= clipboardSequence) break;
                    clipboardSequence = copySequence;
                    bridge.BeginTransfer(copyHeader["bytes"].GetInt32(), Text(copyHeader["sha256"]));
                    operation = "ClipboardCopy"; receiving = true; started = clock.GetTimestamp();
                    State = AccessoryUnifiedEditorState.Busy; Changed?.Invoke(); break;
                case "ClipboardChunk" when State == AccessoryUnifiedEditorState.Busy && operation == "ClipboardCopy" && receiving:
                    var copyChunk = AccessoryEditorBridge.ExactObject(message.Payload, ["clipboardId", "index", "base64"]);
                    if (ReadEditSequence(copyChunk["clipboardId"]) != clipboardSequence) break;
                    var fragment = bridge.AppendChunk(copyChunk["index"].GetInt32(), Text(copyChunk["base64"]));
                    if (fragment is not null)
                    {
                        try
                        {
                            var png = ValidateClipboardFragment(fragment);
                            clipboard.Write(fragment, png);
                            Send("ClipboardResult", new { clipboardId = clipboardSequence, ok = true, bytes = 0, sha256 = "" });
                        }
                        catch (Exception error) when (error is InvalidDataException or System.Runtime.InteropServices.ExternalException or IOException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or NullReferenceException)
                        { Send("ClipboardResult", new { clipboardId = clipboardSequence, ok = false, bytes = 0, sha256 = "" }); }
                        CompleteOperation();
                    }
                    break;
                case "ClipboardPaste" when State == AccessoryUnifiedEditorState.Ready:
                    var pasteHeader = AccessoryEditorBridge.ExactObject(message.Payload, ["clipboardId"]);
                    var pasteSequence = ReadEditSequence(pasteHeader["clipboardId"]);
                    if (pasteSequence <= clipboardSequence) break;
                    clipboardSequence = pasteSequence;
                    try
                    {
                        var copied = clipboard.ReadFragment();
                        if (copied is not null) _ = ValidateClipboardFragment(copied);
                        Send("ClipboardResult", new { clipboardId = clipboardSequence, ok = true, bytes = copied?.Length ?? 0,
                            sha256 = copied is null ? "" : Convert.ToHexStringLower(SHA256.HashData(copied)) });
                        if (copied is not null)
                            for (var offset = 0; offset < copied.Length; offset += AccessoryEditorBridge.MaximumChunkBytes)
                                Send("ClipboardChunk", new { clipboardId = clipboardSequence, index = offset / AccessoryEditorBridge.MaximumChunkBytes,
                                    base64 = Convert.ToBase64String(copied, offset, Math.Min(AccessoryEditorBridge.MaximumChunkBytes, copied.Length - offset)) });
                    }
                    catch (Exception error) when (error is InvalidDataException or System.Runtime.InteropServices.ExternalException or IOException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or NullReferenceException)
                    { Send("ClipboardResult", new { clipboardId = clipboardSequence, ok = false, bytes = 0, sha256 = "" }); }
                    break;
                case "Ready" when State == AccessoryUnifiedEditorState.Loading:
                    _ = AccessoryEditorBridge.ExactObject(message.Payload, []);
                    CompleteOperation(); Send("WearState", AccessoryWearParameters.ToPayload(Parameters)); break;
                case "Changed" when State is AccessoryUnifiedEditorState.Ready or AccessoryUnifiedEditorState.Busy:
                    var change = AccessoryEditorBridge.ExactObject(message.Payload, ["editSequence"]);
                    var sequence = ReadEditSequence(change["editSequence"]);
                    if (sequence <= editSequence) break;
                    editSequence = sequence;
                    // A delayed, rate-limited notification does not cancel a Busy export: its
                    // header identifies the revision actually captured, which may already be current.
                    ContentChanged?.Invoke(); Changed?.Invoke(); break;
                case "Cancel": Dispose(); Changed?.Invoke(); break;
                case "Error":
                    // Do not display untrusted browser text, paths or image-derived content.
                    var failure = AccessoryEditorBridge.ExactObject(message.Payload, ["message"]);
                    if (Text(failure["message"]).Length > 512) throw new InvalidDataException("Oversized browser error.");
                    if (receiving) Fail("编辑结果传输中断。可重试或取消；外层草稿未更改。");
                    else if (State == AccessoryUnifiedEditorState.Loading) Fail("图片工程加载失败。可重试或取消；外层草稿未更改。");
                    else
                    {
                        CompleteOperation();
                        ErrorMessage = "图片编辑操作未完成。请结束画布操作或修正图片后再次确认；外层草稿未更改。";
                        Changed?.Invoke();
                    }
                    break;
                case "Preview" or "Confirm" when State == AccessoryUnifiedEditorState.Busy && message.Type == operation && !receiving:
                    var header = AccessoryEditorBridge.ExactObject(message.Payload, ["bytes", "sha256", "editSequence"]);
                    var capturedSequence = ReadEditSequence(header["editSequence"]);
                    if (capturedSequence < editSequence) { CompleteOperation(); break; }
                    editSequence = capturedSequence;
                    bridge.BeginTransfer(header["bytes"].GetInt32(), Text(header["sha256"]));
                    receivingEditSequence = capturedSequence; receiving = true; break;
                case "Chunk" when State == AccessoryUnifiedEditorState.Busy && receiving:
                    var chunk = AccessoryEditorBridge.ExactObject(message.Payload, ["index", "base64"]);
                    var bytes = bridge.AppendChunk(chunk["index"].GetInt32(), Text(chunk["base64"]));
                    if (bytes is not null)
                    {
                        if (receivingEditSequence < editSequence) { CompleteOperation(); break; }
                        try { AcceptSnapshot(bytes); }
                        catch (InvalidDataException)
                        {
                            CompleteOperation();
                            ErrorMessage = "图片或中心线未通过校验。请调整后再次确认；外层草稿未更改。";
                            Changed?.Invoke();
                        }
                    }
                    break;
            }
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or InvalidOperationException or
            ArgumentException or FormatException or OverflowException)
        { Fail("编辑结果无效。请修正图片或中心线后重试；草稿仍保留，也可取消。"); }
    }

    private void AcceptSnapshot(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 });
        var fields = AccessoryEditorBridge.ExactObject(document.RootElement, ["project", "png"]);
        var previousGeometry = Geometry; var previousParameters = Parameters;
        try
        {
            var result = adapter.ReadResult(fields["project"], fields["png"], identity, Parameters);
            project = result.Project; Parameters = result.Item;
            Geometry = new(Parameters.ImagePivotX, Parameters.ImagePivotY, [], false); lastCompleteGeometry = Geometry;
            if (operation == "Confirm") { Result = result; Dispose(); Changed?.Invoke(); }
            else { CompleteOperation(); PreviewReady?.Invoke(result); }
        }
        catch { Geometry = previousGeometry; Parameters = previousParameters; throw; }
    }

    private byte[] ValidateClipboardFragment(byte[] bytes)
    {
        if (bytes.Length is < 1 or > AccessoryEditorBridge.MaximumTransferBytes) throw new InvalidDataException("Clipboard budget exceeded.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 });
        var fields = AccessoryEditorBridge.ExactObject(document.RootElement, ["project", "png"]);
        // Reuse the strict project/image validator without changing this session's
        // draft, adapter aliases, result or save/preview state.
        var clipboardProject = fields["project"];
        if (clipboardProject.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array && layers.GetArrayLength() == 0)
        {
            // A clipboard-only empty group has no image layers. Validate it using
            // a temporary empty layer; never relax the saved project's minimum.
            var node = JsonNode.Parse(clipboardProject.GetRawText())!.AsObject();
            var skeleton = node["skeleton"]!.AsObject();
            if (skeleton["groups"]!.AsArray().Count != 1 || skeleton["layers"]!.AsArray().Count != 0 ||
                node["assets"]!.AsArray().Count != 0 || node["renderPieces"]!.AsArray().Count != 0)
                throw new InvalidDataException("Invalid empty clipboard group.");
            node["layers"] = JsonSerializer.SerializeToNode(new[] { new { id = 1, name = "空层", type = (string?)null,
                x = 0, y = 0, width = 0, height = 0, visible = true, is_vector = false, opacity = 100, order = 1,
                composition = "source-over", rotate = 0, data = (object?)null, @params = new { }, status = (object?)null,
                color = "#000000", render_function = (object?)null } });
            skeleton["layers"] = JsonSerializer.SerializeToNode(new[] { new { id = "1", groupId = (string?)null } });
            clipboardProject = JsonSerializer.SerializeToElement(node);
        }
        var result = new AccessoryBrowserProjectAdapter().ReadResult(clipboardProject, fields["png"], identity, Parameters);
        return result.Image.CopyPngBytes();
    }

    private void CompleteOperation()
    {
        started = null; operation = null; receiving = false; receivingEditSequence = null;
        State = AccessoryUnifiedEditorState.Ready; ErrorMessage = null; Changed?.Invoke();
    }
    internal void CheckTimeout()
    {
        if (started is { } timestamp && clock.GetElapsedTime(timestamp) > AccessoryEditorBridge.OperationTimeout)
            Fail("编辑操作超时。草稿仍保留，可重试或取消。");
    }
    internal void Fail(string message)
    {
        if (State == AccessoryUnifiedEditorState.Closed) return;
        started = null; receiving = false; receivingEditSequence = null; bridge?.Dispose();
        State = AccessoryUnifiedEditorState.Failed; ErrorMessage = message; Changed?.Invoke();
    }
    private void Send(string type, object payload) => Outgoing?.Invoke(bridge!.Serialize(type, payload));
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String && value.GetString() is { } text
        ? text : throw new InvalidDataException("Invalid transport text.");
    private static long ReadEditSequence(JsonElement value) => value.TryGetInt64(out var sequence) && sequence is >= 0 and <= 9_007_199_254_740_991
        ? sequence : throw new InvalidDataException("Invalid browser edit sequence.");
    internal void Cancel()
    {
        if (State is not (AccessoryUnifiedEditorState.Closed or AccessoryUnifiedEditorState.Failed) && bridge is not null)
            Send("Cancel", new { });
        Dispose();
    }
    public void Dispose()
    {
        started = null; receiving = false; receivingEditSequence = null; bridge?.Dispose(); State = AccessoryUnifiedEditorState.Closed;
    }
}

internal sealed record AccessoryEditorGeometry(double PivotX, double PivotY, IReadOnlyList<AccessoryRigNode> Nodes, bool RequiresRebind)
{
    internal object ToPayload() => new { pivotX = PivotX, pivotY = PivotY,
        nodes = Nodes.Select(n => new { x = n.X, y = n.Y }), requiresRebind = RequiresRebind };
    internal static AccessoryEditorGeometry Read(JsonElement element)
    {
        var f = AccessoryEditorBridge.ExactObject(element, ["pivotX", "pivotY", "nodes", "requiresRebind"]);
        static double Number(JsonElement v)
        {
            var n = v.GetDouble();
            if (!double.IsFinite(n) || Math.Abs(n) > 8192) throw new InvalidDataException("Invalid geometry coordinate.");
            return n;
        }
        var nodes = f["nodes"];
        if (nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() > AccessoryRigLimits.MaximumNodes)
            throw new InvalidDataException("Invalid chain budget.");
        var points = nodes.EnumerateArray().Select(node =>
        {
            var p = AccessoryEditorBridge.ExactObject(node, ["x", "y"]);
            return new AccessoryRigNode(Number(p["x"]), Number(p["y"]));
        }).ToArray();
        var result = new AccessoryEditorGeometry(Number(f["pivotX"]), Number(f["pivotY"]), Array.AsReadOnly(points), f["requiresRebind"].GetBoolean());
        if (!result.RequiresRebind && (result.PivotX is < 0 or > 1 || result.PivotY is < 0 or > 1 ||
            points.Any(p => p.X is < 0 or > 1 || p.Y is < 0 or > 1)) ||
            points.Length > 0 && (points[0].X != result.PivotX || points[0].Y != result.PivotY))
            throw new InvalidDataException("Invalid geometry root or bounds.");
        return result;
    }
}
