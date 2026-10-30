using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dango.Desktop.Accessories.Editing;

/// <summary>Native-owned identity and bounded transport. Payloads still require domain validation.</summary>
internal sealed class AccessoryEditorBridge : IDisposable
{
    internal const int ProtocolVersion = 2;
    internal const int MaximumMessageBytes = 256 * 1024;
    internal const int MaximumChunkBytes = 128 * 1024;
    internal const int MaximumTransferBytes = 64 * 1024 * 1024;
    internal static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(60);
    internal const string Origin = "https://editor.dango.invalid";
    internal const string EntryPoint = Origin + "/index.html";
    private static readonly HashSet<string> MessageTypes = ["Ready", "Preview", "Confirm", "Cancel", "Error", "Chunk", "Changed", "ClipboardCopy", "ClipboardPaste", "ClipboardChunk", "ViewCommand", "WearChanged", "SimulationViewport"];
    private readonly TimeProvider clock;
    private long? operationStarted;
    private bool closed;
    private int nextChunk;
    private int transferLength;
    private string? transferHash;
    private MemoryStream? transfer;

    internal AccessoryEditorBridge(long revision, TimeProvider? clock = null)
    {
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        Revision = revision;
        this.clock = clock ?? TimeProvider.System;
        operationStarted = this.clock.GetTimestamp();
    }

    internal string SessionId { get; } = Guid.NewGuid().ToString("N");
    internal string RequestId { get; } = Guid.NewGuid().ToString("N");
    internal long Revision { get; }
    internal bool IsClosed => closed;

    internal bool TryRead(string source, string json, out AccessoryEditorMessage? message)
    {
        message = null;
        if (closed || source != EntryPoint || json.Length > MaximumMessageBytes ||
            Encoding.UTF8.GetByteCount(json) > MaximumMessageBytes) return false;
        if (HasTimedOut) { Dispose(); return false; }
        try
        {
            using var document = JsonDocument.Parse(json, new() { MaxDepth = 24 });
            var fields = ExactObject(document.RootElement,
                ["protocol", "sessionId", "requestId", "revision", "type", "payload"]);
            if (!fields["protocol"].TryGetInt32(out var protocol) || protocol != ProtocolVersion ||
                fields["sessionId"].GetString() != SessionId || fields["requestId"].GetString() != RequestId ||
                !fields["revision"].TryGetInt64(out var revision) || revision != Revision ||
                fields["type"].GetString() is not { } type || !MessageTypes.Contains(type) ||
                fields["payload"].ValueKind != JsonValueKind.Object) return false;
            message = new(type, fields["payload"].Clone());
            if (type == "Ready" && transfer is null) operationStarted = null;
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or InvalidDataException)
        { return false; }
    }

    internal string Serialize(string type, object payload) => JsonSerializer.Serialize(new
    { protocol = ProtocolVersion, sessionId = SessionId, requestId = RequestId, revision = Revision, type, payload });

    // A transfer is declared by native domain validation, never by an unchecked webpage allocation.
    internal void BeginTransfer(int bytes, string sha256)
    {
        if (closed) throw new ObjectDisposedException(nameof(AccessoryEditorBridge));
        if (transfer is not null || bytes is < 1 or > MaximumTransferBytes ||
            sha256.Length != 64 || sha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException("编辑器传输声明无效。");
        transferLength = bytes; transferHash = sha256; nextChunk = 0;
        operationStarted = clock.GetTimestamp();
        // Grow only as accepted chunks arrive; do not preallocate the declared maximum.
        transfer = new MemoryStream();
    }

    internal byte[]? AppendChunk(int index, string base64)
    {
        if (closed || HasTimedOut || transfer is null ||
            index != nextChunk || base64.Length > (MaximumChunkBytes + 2) / 3 * 4)
            return RejectTransfer();
        var buffer = new byte[MaximumChunkBytes];
        if (!Convert.TryFromBase64String(base64, buffer, out var count) || count == 0 ||
            transfer.Length + count > transferLength) return RejectTransfer();
        transfer.Write(buffer, 0, count); nextChunk++;
        if (transfer.Length < transferLength) return null;
        var bytes = transfer.ToArray();
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != transferHash) return RejectTransfer();
        transfer.Dispose(); transfer = null; transferHash = null; operationStarted = null;
        return bytes;
    }

    private byte[]? RejectTransfer()
    {
        Dispose();
        throw new InvalidDataException("编辑器传输已失效；请重试，草稿仍保留。");
    }

    private bool HasTimedOut => operationStarted is { } timestamp && clock.GetElapsedTime(timestamp) > OperationTimeout;

    private void CancelTransfer()
    {
        transfer?.Dispose(); transfer = null; transferHash = null;
        transferLength = 0; nextChunk = 0; operationStarted = null;
    }

    internal static Dictionary<string, JsonElement> ExactObject(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("编辑消息不是对象。");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Contains(property.Name) || !fields.TryAdd(property.Name, property.Value))
                throw new InvalidDataException("编辑消息字段无效。");
        if (fields.Count != names.Length) throw new InvalidDataException("编辑消息字段缺失。");
        return fields;
    }

    public void Dispose()
    {
        closed = true;
        CancelTransfer();
    }
}

internal sealed record AccessoryEditorMessage(string Type, JsonElement Payload);
