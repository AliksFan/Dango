using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Desktop.Accessories.Rig;

namespace Dango.Desktop.Accessories.Editing;

internal enum AccessoryUnifiedEditorState { Loading, Ready, Busy, Failed, Closed }

/// <summary>Modal-owned, storage-free transaction. Only complete validated snapshots escape this boundary.</summary>
internal sealed class AccessoryUnifiedEditorSession : IDisposable
{
    private readonly AccessoryEditDraft identity;
    private readonly TimeProvider clock;
    private readonly AccessoryBrowserProjectAdapter adapter = new();
    private AccessoryEditorProject? project;
    private readonly AccessoryEditorSourceAsset? source;
    private AccessoryEditorBridge? bridge;
    private long? started;
    private string? operation;
    private bool receiving;
    private bool opened;
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

    internal AccessoryUnifiedEditorSession(AccessoryEditDraft identity, AccessoryEditorSourceAsset? source,
        AccessoryEditorProject? project, TimeProvider? clock = null)
    {
        if ((source is null) == (project is null)) throw new ArgumentException("Provide exactly one source or editable project.");
        if (identity.ItemId != identity.Original.Id || identity.Revision < 0) throw new ArgumentException("Invalid modal identity.");
        this.identity = identity; this.source = source; this.project = project;
        this.clock = clock ?? TimeProvider.System;
        Parameters = identity.Original;
        RigSoftness = Parameters.Rig?.Softness ?? .7; RigDamping = Parameters.Rig?.Damping ?? 6;
        RigRestoration = Parameters.Rig?.Restoration ?? 5;
        Geometry = new(Parameters.ImagePivotX, Parameters.ImagePivotY, Parameters.Rig?.Nodes ?? [], false);
        lastCompleteGeometry = Geometry;
    }

    internal void Open()
    {
        if (State == AccessoryUnifiedEditorState.Closed) return;
        if (opened) { Geometry = lastCompleteGeometry; ApplyGeometry(); RestoredCheckpoint = true; }
        opened = true;
        bridge?.Dispose(); bridge = new(identity.Revision, clock);
        receiving = false; operation = "Open"; started = clock.GetTimestamp();
        State = AccessoryUnifiedEditorState.Loading; ErrorMessage = null; Changed?.Invoke();
        try
        {
            var content = JsonSerializer.SerializeToUtf8Bytes(new
            { project = project is null ? adapter.CreateOpenProject(source!) : adapter.CreateOpenProject(project), geometry = Geometry.ToPayload() });
            if (content.Length > AccessoryEditorBridge.MaximumTransferBytes) throw new InvalidDataException("编辑工程超过传输上限。");
            Send("Open", new { bytes = content.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) });
            for (var offset = 0; offset < content.Length && State == AccessoryUnifiedEditorState.Loading; offset += AccessoryEditorBridge.MaximumChunkBytes)
                Send("Chunk", new { index = offset / AccessoryEditorBridge.MaximumChunkBytes,
                    base64 = Convert.ToBase64String(content, offset, Math.Min(AccessoryEditorBridge.MaximumChunkBytes, content.Length - offset)) });
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or JsonException)
        { Fail("编辑工程无法加载。草稿仍保留，可重试或取消。"); }
    }

    internal bool RequestSnapshot(bool confirm)
    {
        if (State != AccessoryUnifiedEditorState.Ready) return false;
        if (Geometry.RequiresRebind) { ErrorMessage = "请先重新定位连接点或调整中心线。"; Changed?.Invoke(); return false; }
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

    internal void SetGeometry(AccessoryEditorGeometry geometry)
    {
        if (State != AccessoryUnifiedEditorState.Ready) return;
        Geometry = geometry; ApplyGeometry();
        Send("SetGeometry", geometry.ToPayload()); Changed?.Invoke();
    }

    internal void UpdateRigParameters(double softness, double damping, double restoration)
    {
        if (State != AccessoryUnifiedEditorState.Ready) return;
        new AccessoryRigParameters(softness, damping, restoration).Validate();
        RigSoftness = softness; RigDamping = damping; RigRestoration = restoration;
        ApplyGeometry(); Changed?.Invoke();
    }

    internal void Receive(string origin, string json)
    {
        if (State is AccessoryUnifiedEditorState.Closed or AccessoryUnifiedEditorState.Failed || bridge is null) return;
        CheckTimeout();
        if (State == AccessoryUnifiedEditorState.Failed || !bridge.TryRead(origin, json, out var message)) return;
        try
        {
            switch (message!.Type)
            {
                case "Ready" when State == AccessoryUnifiedEditorState.Loading:
                    _ = AccessoryEditorBridge.ExactObject(message.Payload, []);
                    CompleteOperation(); break;
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
                case "Preview" when State == AccessoryUnifiedEditorState.Ready ||
                    State == AccessoryUnifiedEditorState.Busy && message.Payload.TryGetProperty("geometry", out _):
                    var update = AccessoryEditorBridge.ExactObject(message.Payload, ["geometry"]);
                    var geometryUpdate = AccessoryEditorGeometry.Read(update["geometry"]);
                    // SetGeometry acknowledgements can arrive after a snapshot request. Validate their shape,
                    // but let the requested complete snapshot own its geometry and transfer while busy.
                    if (State == AccessoryUnifiedEditorState.Ready)
                    { Geometry = geometryUpdate; ApplyGeometry(); Changed?.Invoke(); }
                    break;
                case "Preview" or "Confirm" when State == AccessoryUnifiedEditorState.Busy && message.Type == operation && !receiving:
                    var header = AccessoryEditorBridge.ExactObject(message.Payload, ["bytes", "sha256"]);
                    bridge.BeginTransfer(header["bytes"].GetInt32(), Text(header["sha256"]));
                    receiving = true; break;
                case "Chunk" when State == AccessoryUnifiedEditorState.Busy && receiving:
                    var chunk = AccessoryEditorBridge.ExactObject(message.Payload, ["index", "base64"]);
                    var bytes = bridge.AppendChunk(chunk["index"].GetInt32(), Text(chunk["base64"]));
                    if (bytes is not null)
                    {
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
        var fields = AccessoryEditorBridge.ExactObject(document.RootElement, ["project", "png", "geometry"]);
        var geometry = AccessoryEditorGeometry.Read(fields["geometry"]);
        if (geometry.RequiresRebind) throw new InvalidDataException("Rebind required.");
        var previousGeometry = Geometry; var previousParameters = Parameters;
        try
        {
            Geometry = geometry; ApplyGeometry();
            var result = adapter.ReadResult(fields["project"], fields["png"], identity, Parameters);
            project = result.Project; Parameters = result.Item; lastCompleteGeometry = Geometry;
            if (operation == "Confirm") { Result = result; Dispose(); Changed?.Invoke(); }
            else { CompleteOperation(); PreviewReady?.Invoke(result); }
        }
        catch { Geometry = previousGeometry; Parameters = previousParameters; throw; }
    }

    private void ApplyGeometry()
    {
        Parameters = Parameters with { ImagePivotX = Geometry.PivotX, ImagePivotY = Geometry.PivotY,
            Rig = Geometry.Nodes.Count == 0 ? null : new(Geometry.Nodes, RigSoftness, RigDamping, RigRestoration) };
    }

    private void CompleteOperation()
    {
        started = null; operation = null; receiving = false;
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
        started = null; receiving = false; bridge?.Dispose();
        State = AccessoryUnifiedEditorState.Failed; ErrorMessage = message; Changed?.Invoke();
    }
    private void Send(string type, object payload) => Outgoing?.Invoke(bridge!.Serialize(type, payload));
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String && value.GetString() is { } text
        ? text : throw new InvalidDataException("Invalid transport text.");
    internal void Cancel()
    {
        if (State is not (AccessoryUnifiedEditorState.Closed or AccessoryUnifiedEditorState.Failed) && bridge is not null)
            Send("Cancel", new { });
        Dispose();
    }
    public void Dispose()
    {
        started = null; receiving = false; bridge?.Dispose(); State = AccessoryUnifiedEditorState.Closed;
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
