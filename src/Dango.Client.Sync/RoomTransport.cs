using Dango.Protocol;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Dango.Client.Sync;

/// <summary>Transport-only seam. Test decorators may delay/drop delivery without changing the reducer or server.</summary>
public interface IRoomTransport : IAsyncDisposable
{
    event Action<RoomEvent>? RoomEventReceived;
    event Action<SessionSuperseded>? SessionSupersededReceived;
    event Action? Closed;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task<T> InvokeAsync<T>(string method, object? argument, CancellationToken cancellationToken);
}

public interface IRoomTransportFactory
{
    IRoomTransport Create(Uri endpoint);
}

public sealed class SignalRRoomTransportFactory : IRoomTransportFactory
{
    public IRoomTransport Create(Uri endpoint) => new SignalRRoomTransport(endpoint);
}

internal sealed class SignalRRoomTransport : IRoomTransport
{
    private readonly HubConnection _hub;
    public event Action<RoomEvent>? RoomEventReceived;
    public event Action<SessionSuperseded>? SessionSupersededReceived;
    public event Action? Closed;
    public SignalRRoomTransport(Uri endpoint)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(endpoint, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions = RoomProtocol.CreateJsonOptions())
            .Build();
        // Reconnection is owned by RoomConnection so every new transport has a fresh callback fence.
        _hub.On<RoomEvent>("RoomEvent", value => RoomEventReceived?.Invoke(value));
        _hub.On<SessionSuperseded>("SessionSuperseded", value => SessionSupersededReceived?.Invoke(value));
        _hub.Closed += _ => { Closed?.Invoke(); return Task.CompletedTask; };
    }
    public Task StartAsync(CancellationToken cancellationToken) => _hub.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => _hub.StopAsync(cancellationToken);
    public Task<T> InvokeAsync<T>(string method, object? argument, CancellationToken cancellationToken) =>
        _hub.InvokeCoreAsync<T>(method, argument is null ? [] : [argument], cancellationToken);
    public ValueTask DisposeAsync() => _hub.DisposeAsync();
}
