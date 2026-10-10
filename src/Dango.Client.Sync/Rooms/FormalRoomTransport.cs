using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Dango.Protocol;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dango.Client.Sync.Rooms;

internal sealed record FormalRoomBinding(Uri Origin, CookieContainer Cookies, Guid UserId, Guid PetId,
    Action<CancellationToken> Check, CancellationToken Lifetime,
    Func<HttpRequestMessage, Func<CancellationToken, Task<HttpResponseMessage>>, CancellationToken, Task<HttpResponseMessage>> Send,
    Func<Task>? AuthenticationLost = null);

internal interface IFormalRoomWire : IAsyncDisposable
{
    event Action<JsonElement>? Notice;
    event Action? Closed;
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task<JsonElement> InvokeAsync(string method, object?[] arguments, CancellationToken ct);
}

public sealed class FormalRoomTransport : IFormalRoomConnection
{
    private readonly FormalRoomBinding binding;
    private readonly IFormalRoomWire wire;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationTokenRegistration ownerLifetime;
    private int state; // 0 new, 1 starting, 2 connected, 3 closed. A connection is used once.
    private int disposed;
    public event Action<RoomChangeNotice>? RoomChanged;
    public event Action? Closed;

    internal FormalRoomTransport(FormalRoomBinding binding, Func<HttpMessageHandler, HttpMessageHandler>? factory, HttpTransportType? transports)
        : this(binding, new SignalRFormalRoomWire(binding, factory, transports)) { }
    internal FormalRoomTransport(FormalRoomBinding binding, IFormalRoomWire wire)
    {
        this.binding = binding; this.wire = wire;
        wire.Notice += OnNotice; wire.Closed += OnClosed;
        ownerLifetime = binding.Lifetime.Register(() => { lifetime.Cancel(); _ = QuietStopAsync(); });
    }
    public async Task StartAsync(CancellationToken ct)
    {
        Check(ct, connected: false);
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0) throw RoomAccessValidation.Reject("room_connection_used");
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, binding.Lifetime);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestLifetime.Token, lifetime.Token);
        try
        {
            await wire.StartAsync(linked.Token); Check(linked.Token, connected: false);
            if (Interlocked.CompareExchange(ref state, 2, 1) != 1) throw RoomAccessValidation.Unknown();
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref state, 3); await QuietStopAsync();
            var mapped = Map(error, requestLifetime.Token); await RejectAuthenticationAsync(mapped); throw mapped;
        }
    }
    public async Task StopAsync(CancellationToken ct)
    {
        Interlocked.Exchange(ref state, 3); lifetime.Cancel();
        try { await wire.StopAsync(ct); }
        catch (Exception error) { throw Map(error, ct); }
    }
    public Task<RoomAccessResult> AttachAsync(RoomLeaseRequest request, CancellationToken ct) => InvokeAsync("Attach", [request],
        () => RoomAccessValidation.Lease(request), value => RoomAccessValidation.Result(value, binding.PetId, "attached", expectedLease: request.LeaseId), ct);
    public Task<RoomAccessResult> HeartbeatAsync(RoomLeaseRequest request, CancellationToken ct) => InvokeAsync("Heartbeat", [request],
        () => RoomAccessValidation.Lease(request), value => RoomAccessValidation.Result(value, binding.PetId, "heartbeat", expectedLease: request.LeaseId), ct);
    public Task<PersonalRoomSnapshot> PersonalAsync(CancellationToken ct) => InvokeAsync("PersonalSnapshot", [], () => { },
        value => RoomAccessValidation.Personal(value, binding.UserId, binding.PetId), ct);
    public Task<SharedRoomSnapshot> SnapshotAsync(Guid roomId, RoomLeaseRequest request, CancellationToken ct) => InvokeAsync("Snapshot", [roomId, request],
        () => { RoomAccessValidation.Id(roomId); RoomAccessValidation.Lease(request); }, value => RoomAccessValidation.Snapshot(value, roomId), ct);
    public Task<RoomAccessResult> ControlAsync(RoomControlRequest request, CancellationToken ct) => InvokeAsync("TakeControl", [request],
        () => RoomAccessValidation.Control(request), value => RoomAccessValidation.Result(value, binding.PetId, "control", request.ExpectedRoomId, request.LeaseId,
            request.ExpectedAssignmentVersion, request.ExpectedControlGeneration), ct);
    public Task<RoomAccessResult> LeaveAsync(RoomControlRequest request, CancellationToken ct) => InvokeAsync("Leave", [request],
        () => RoomAccessValidation.Control(request), value => RoomAccessValidation.Result(value, binding.PetId, "left",
            expectedAssignmentVersion: request.ExpectedAssignmentVersion, expectedControlGeneration: request.ExpectedControlGeneration), ct);
    public Task<RoomAccessResult> EndLeaseAsync(RoomLeaseEndRequest request, CancellationToken ct) => InvokeAsync("EndLease", [request],
        () => RoomAccessValidation.End(request), value => RoomAccessValidation.Result(value, binding.PetId, "lease_ended"), ct);
    private async Task<T> InvokeAsync<T>(string method, object?[] arguments, Action validate, Func<JsonElement, T> parse, CancellationToken ct)
    {
        Check(ct); validate();
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, binding.Lifetime);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestLifetime.Token, lifetime.Token);
        try { var value = await wire.InvokeAsync(method, arguments, linked.Token); Check(linked.Token); return parse(value); }
        catch (Exception error) { var mapped = Map(error, requestLifetime.Token, method); await RejectAuthenticationAsync(mapped); throw mapped; }
    }
    private async Task RejectAuthenticationAsync(Exception error)
    {
        if (error is not RoomAccessClientException { Code: "authentication_required" }) return;
        try { if (binding.AuthenticationLost is { } invalidate) await invalidate(); }
        finally { Interlocked.Exchange(ref state, 3); lifetime.Cancel(); await QuietStopAsync(); }
    }
    private void Check(CancellationToken ct, bool connected = true)
    {
        ct.ThrowIfCancellationRequested(); binding.Lifetime.ThrowIfCancellationRequested(); lifetime.Token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this); binding.Check(ct);
        if (connected && Volatile.Read(ref state) != 2) throw RoomAccessValidation.Unknown();
    }
    private bool Current()
    {
        try { Check(CancellationToken.None); return true; }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or RoomAccessClientException) { return false; }
    }
    private void OnNotice(JsonElement json)
    {
        if (!Current()) return;
        RoomChangeNotice notice;
        try { notice = RoomAccessValidation.Notice(json); }
        catch (RoomAccessClientException) { OnClosed(); _ = QuietStopAsync(); return; }
        if (Current()) RoomChanged?.Invoke(notice);
    }
    private void OnClosed()
    {
        if (Interlocked.CompareExchange(ref state, 3, 1) == 1) { lifetime.Cancel(); return; }
        if (!Current()) return;
        if (Interlocked.Exchange(ref state, 3) == 2) { lifetime.Cancel(); Closed?.Invoke(); }
    }
    private async Task QuietStopAsync()
    { try { await wire.StopAsync(CancellationToken.None); } catch (Exception error) when (error is not OutOfMemoryException) { } }
    private static Exception Map(Exception error, CancellationToken ct, string? method = null) => error switch
    {
        RoomAccessClientException => error,
        OperationCanceledException when ct.IsCancellationRequested => new OperationCanceledException(ct),
        HubException hub => method is null ? RoomAccessValidation.HubError(hub.Message) : RoomAccessValidation.HubInvocationError(hub.Message, method),
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => new RoomAccessClientException(RoomAccessFailure.Authentication, "authentication_required"),
        JsonException or InvalidDataException => RoomAccessValidation.Invalid(),
        ObjectDisposedException when ct.IsCancellationRequested => new OperationCanceledException(ct),
        OutOfMemoryException => error,
        _ => RoomAccessValidation.Unknown()
    };
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Interlocked.Exchange(ref state, 3); lifetime.Cancel(); ownerLifetime.Dispose();
        wire.Notice -= OnNotice; wire.Closed -= OnClosed;
        try { await wire.DisposeAsync(); } finally { lifetime.Dispose(); }
    }
}

internal sealed class SignalRFormalRoomWire : IFormalRoomWire
{
    private readonly HubConnection hub;
    private readonly IDisposable notice;
    public event Action<JsonElement>? Notice;
    public event Action? Closed;
    internal SignalRFormalRoomWire(FormalRoomBinding binding, Func<HttpMessageHandler, HttpMessageHandler>? factory, HttpTransportType? transports)
    {
        if (transports is not null && transports != HttpTransportType.LongPolling && transports != HttpTransportType.WebSockets
            && transports != (HttpTransportType.WebSockets | HttpTransportType.LongPolling)) throw new ArgumentOutOfRangeException(nameof(transports));
        var builder = new HubConnectionBuilder().WithUrl(new Uri(binding.Origin, "api/v1/room-hub"), options =>
        {
            options.Cookies = binding.Cookies;
            options.Transports = transports ?? (HttpTransportType.WebSockets | HttpTransportType.LongPolling);
            options.SkipNegotiation = false; options.UseStatefulReconnect = false;
            options.TransportMaxBufferSize = options.ApplicationMaxBufferSize = RoomAccessValidation.MaximumBytes * 2;
            options.Headers["Origin"] = binding.Origin.GetLeftPart(UriPartial.Authority);
            options.HttpMessageHandlerFactory = handler => WrapHandler(binding, handler, factory);
            options.WebSocketFactory = async (context, ct) =>
            {
                FormalHubHttpHandler.RequireUri(binding.Origin, context.Uri, websocket: true); binding.Check(ct);
                var socket = new ClientWebSocket();
                socket.Options.Cookies = binding.Cookies;
                socket.Options.SetRequestHeader("Origin", binding.Origin.GetLeftPart(UriPartial.Authority));
                var invoker = new HttpMessageInvoker(WrapHandler(binding, new HttpClientHandler(), factory));
                try
                {
                    await socket.ConnectAsync(context.Uri, invoker, ct); binding.Check(ct);
                    return new OwnedWebSocket(socket, invoker);
                }
                catch { socket.Dispose(); invoker.Dispose(); throw; }
            };
        });
        builder.Services.AddSingleton<IHubProtocol>(new BoundedFormalHubProtocol());
        hub = builder.Build();
        notice = hub.On<JsonElement>("RoomChanged", value => Notice?.Invoke(value));
        hub.Closed += OnClosedAsync;
    }
    private static HttpMessageHandler WrapHandler(FormalRoomBinding binding, HttpMessageHandler handler, Func<HttpMessageHandler, HttpMessageHandler>? factory)
    {
        if (handler is HttpClientHandler normal) { normal.AllowAutoRedirect = false; normal.CookieContainer = binding.Cookies; normal.UseCookies = true; }
        return new FormalHubHttpHandler(binding, factory?.Invoke(handler) ?? handler);
    }
    private Task OnClosedAsync(Exception? error) { Closed?.Invoke(); return Task.CompletedTask; }
    public Task StartAsync(CancellationToken ct) => hub.StartAsync(ct);
    public Task StopAsync(CancellationToken ct) => hub.StopAsync(ct);
    public Task<JsonElement> InvokeAsync(string method, object?[] arguments, CancellationToken ct) => hub.InvokeCoreAsync<JsonElement>(method, arguments, ct);
    public async ValueTask DisposeAsync()
    { notice.Dispose(); hub.Closed -= OnClosedAsync; await hub.DisposeAsync(); }
    private sealed class OwnedWebSocket(ClientWebSocket socket, HttpMessageInvoker invoker) : WebSocket
    {
        private readonly FormalFrameCounter frames = new();
        public override WebSocketCloseStatus? CloseStatus => socket.CloseStatus;
        public override string? CloseStatusDescription => socket.CloseStatusDescription;
        public override WebSocketState State => socket.State;
        public override string? SubProtocol => socket.SubProtocol;
        public override void Abort() => socket.Abort();
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => socket.CloseAsync(status, description, ct);
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => socket.CloseOutputAsync(status, description, ct);
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        { var result = await socket.ReceiveAsync(buffer, ct); frames.Add(buffer.AsSpan(0, result.Count)); return result; }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct) => socket.SendAsync(buffer, type, end, ct);
        public override void Dispose() { socket.Dispose(); invoker.Dispose(); }
    }
}

internal sealed class FormalHubHttpHandler(FormalRoomBinding binding, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly FormalFrameCounter frames = new();
    internal static void RequireUri(Uri origin, Uri? uri, bool websocket = false)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || uri.IdnHost != origin.IdnHost || uri.Port != origin.Port || (websocket ? uri.Scheme != "wss" : uri.Scheme != "https")
            || uri.AbsolutePath != "/api/v1/room-hub" && (websocket || uri.AbsolutePath != "/api/v1/room-hub/negotiate")) throw RoomAccessValidation.Invalid();
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        RequireUri(binding.Origin, request.RequestUri);
        request.Headers.Remove("Origin"); request.Headers.Add("Origin", binding.Origin.GetLeftPart(UriPartial.Authority));
        var response = await binding.Send(request, token => base.SendAsync(request, token), ct);
        try
        {
            if ((int)response.StatusCode is >= 300 and < 400) throw RoomAccessValidation.Invalid();
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new RoomAccessClientException(RoomAccessFailure.Authentication, "authentication_required");
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout) throw RoomAccessValidation.Unknown();
            if (!response.IsSuccessStatusCode)
            {
                var errorBytes = await SmallBodyAsync(response.Content, ct);
                using var errorJson = JsonDocument.Parse(errorBytes, new JsonDocumentOptions { MaxDepth = 8 });
                var error = errorJson.RootElement;
                if (error.ValueKind != JsonValueKind.Object || BoundedFormalHubProtocol.Duplicates(error)
                    || !error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String) throw RoomAccessValidation.Unknown();
                throw RoomAccessValidation.HubError(code.GetString());
            }
            if (request.RequestUri!.AbsolutePath.EndsWith("/negotiate", StringComparison.Ordinal) && response.IsSuccessStatusCode)
            {
                var payload = await SmallBodyAsync(response.Content, ct);
                using var json = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
                ValidateNegotiation(json.RootElement);
                var replacement = new ByteArrayContent(payload);
                foreach (var header in response.Content.Headers) replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
                response.Content.Dispose(); response.Content = replacement;
            }
            else if (request.Method == HttpMethod.Get && response.IsSuccessStatusCode)
                response.Content = new BoundedHubContent(response.Content, frames);
            return response;
        }
        catch { response.Dispose(); throw; }
    }
    private static async Task<byte[]> SmallBodyAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > 16384 || content.Headers.ContentType?.MediaType != "application/json") throw RoomAccessValidation.Invalid();
        await using var stream = await content.ReadAsStreamAsync(ct); using var bytes = new MemoryStream(); var buffer = new byte[4096];
        while (true)
        { var read = await stream.ReadAsync(buffer, ct); if (read == 0) break; if (bytes.Length + read > 16384) throw RoomAccessValidation.Invalid(); bytes.Write(buffer, 0, read); }
        return bytes.ToArray();
    }
    internal static void ValidateNegotiation(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || BoundedFormalHubProtocol.Duplicates(value)
            || value.EnumerateObject().Any(p => p.Name is not ("negotiateVersion" or "connectionId" or "connectionToken" or "availableTransports"))) throw RoomAccessValidation.Invalid();
        if (!value.TryGetProperty("connectionId", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() is not { Length: > 0 and <= 256 }
            || !value.TryGetProperty("negotiateVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1
            || !value.TryGetProperty("connectionToken", out var token) || token.ValueKind != JsonValueKind.String || token.GetString() is not { Length: > 0 and <= 256 }
            || !value.TryGetProperty("availableTransports", out var transports) || transports.ValueKind != JsonValueKind.Array || transports.GetArrayLength() is < 1 or > 3)
            throw RoomAccessValidation.Invalid();
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (var transport in transports.EnumerateArray())
        {
            if (transport.ValueKind != JsonValueKind.Object || transport.EnumerateObject().Count() != 2
                || !transport.TryGetProperty("transport", out var name) || name.ValueKind != JsonValueKind.String
                || name.GetString() is not ("WebSockets" or "LongPolling" or "ServerSentEvents") || !names.Add(name.GetString()!)
                || !transport.TryGetProperty("transferFormats", out var formats) || formats.ValueKind != JsonValueKind.Array || formats.GetArrayLength() is < 1 or > 2)
                throw RoomAccessValidation.Invalid();
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (var format in formats.EnumerateArray())
                if (format.ValueKind != JsonValueKind.String || format.GetString() is not ("Text" or "Binary") || !seen.Add(format.GetString()!)) throw RoomAccessValidation.Invalid();
        }
    }
}

// This applies before SignalR's separate handshake parser as well as its Hub message parser.
internal sealed class FormalFrameCounter
{
    private int length;
    internal void Add(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        { if (value == 0x1e) length = 0; else if (++length > RoomAccessValidation.MaximumBytes) throw new InvalidDataException("formal_frame_invalid"); }
    }
}

internal sealed class BoundedHubContent : HttpContent
{
    private readonly HttpContent inner;
    private readonly FormalFrameCounter counter;
    internal BoundedHubContent(HttpContent inner, FormalFrameCounter counter)
    {
        this.inner = inner; this.counter = counter;
        foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
    }
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    { await using var read = await CreateContentReadStreamAsync(); await read.CopyToAsync(stream); }
    protected override async Task<Stream> CreateContentReadStreamAsync() => new BoundedReadStream(await inner.ReadAsStreamAsync(), counter);
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    private sealed class BoundedReadStream(Stream inner, FormalFrameCounter counter) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { var read = inner.Read(buffer, offset, count); counter.Add(buffer.AsSpan(offset, read)); return read; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { var read = await inner.ReadAsync(buffer, ct); counter.Add(buffer.Span[..read]); return read; }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal sealed class BoundedFormalHubProtocol : IHubProtocol
{
    private readonly JsonHubProtocol inner = new(Options.Create(new JsonHubProtocolOptions { PayloadSerializerOptions = RoomAccessValidation.Json }));
    public string Name => inner.Name;
    public int Version => inner.Version;
    public TransferFormat TransferFormat => inner.TransferFormat;
    public bool IsVersionSupported(int version) => inner.IsVersionSupported(version);
    public bool TryParseMessage(ref ReadOnlySequence<byte> input, IInvocationBinder binder, [NotNullWhen(true)] out HubMessage? message)
    {
        var separator = input.PositionOf((byte)0x1e);
        if (separator is null)
        { if (input.Length > RoomAccessValidation.MaximumBytes) throw new InvalidDataException("formal_frame_invalid"); message = null; return false; }
        var frame = input.Slice(0, separator.Value);
        if (frame.Length > RoomAccessValidation.MaximumBytes) throw new InvalidDataException("formal_frame_invalid");
        try
        {
            using var document = JsonDocument.Parse(frame, new JsonDocumentOptions { MaxDepth = 16 });
            ValidateFrame(document.RootElement);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or RoomAccessClientException)
        { throw new InvalidDataException("formal_frame_invalid"); }
        return inner.TryParseMessage(ref input, binder, out message);
    }
    internal static bool Duplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject()) if (!names.Add(p.Name) || Duplicates(p.Value)) return true;
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) if (Duplicates(item)) return true;
        return false;
    }
    internal static void ValidateFrame(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || Duplicates(value) || !value.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.Number || !type.TryGetInt32(out var number)) throw RoomAccessValidation.Invalid();
        string[] allowed = number switch { 1 => ["type", "target", "arguments"], 3 => ["type", "invocationId", "result", "error"], 6 => ["type"], 7 => ["type", "error", "allowReconnect"], _ => throw RoomAccessValidation.Invalid() };
        if (value.EnumerateObject().Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal))) throw RoomAccessValidation.Invalid();
        if (number == 1 && (!value.TryGetProperty("target", out var target) || target.GetString() != "RoomChanged"
            || !value.TryGetProperty("arguments", out var args) || args.ValueKind != JsonValueKind.Array || args.GetArrayLength() != 1)) throw RoomAccessValidation.Invalid();
        if (number == 3 && (!value.TryGetProperty("invocationId", out var id) || id.GetString() is not { Length: > 0 and <= 64 }
            || value.TryGetProperty("result", out _) && value.TryGetProperty("error", out _))) throw RoomAccessValidation.Invalid();
    }
    public void WriteMessage(HubMessage message, IBufferWriter<byte> output) => inner.WriteMessage(message, output);
    public ReadOnlyMemory<byte> GetMessageBytes(HubMessage message) => inner.GetMessageBytes(message);
}
