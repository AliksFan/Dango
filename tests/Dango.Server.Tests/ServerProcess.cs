using Dango.Tests.Support;
using System.Diagnostics;
using Dango.Protocol;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests;

internal sealed class ServerProcess : IAsyncDisposable
{
    private readonly LocalServerProcess child;
    public LocalBootstrap Bootstrap => child.Bootstrap;

    private ServerProcess(LocalServerProcess child) => this.child = child;

    public static async Task<ServerProcess> StartAsync(TestContext context)
    {
        var binary = typeof(Program).Assembly.Location;
        Assert.IsTrue(File.Exists(Path.ChangeExtension(binary, ".runtimeconfig.json")));
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(binary);
        return new(await LocalServerProcess.StartAsync(start, binary, message => context.WriteLine(message)));
    }

    public HubConnection CreateConnection() => new HubConnectionBuilder().WithUrl(Bootstrap.Endpoint + "/room", options =>
    {
        options.Transports = HttpTransportType.WebSockets;
        options.SkipNegotiation = true; // There is no transport fallback in this evidence.
    }).AddJsonProtocol(options => options.PayloadSerializerOptions = RoomProtocol.CreateJsonOptions())
        .ConfigureLogging(logging => logging.ClearProviders()).Build();

    public async Task<JoinRoomResponse> JoinAsync(HubConnection connection, int index) => await connection.InvokeAsync<JoinRoomResponse>("JoinRoom",
        new JoinRoomRequest(RoomProtocol.Version, RoomProtocol.RoomId, $"instance-{index}", Bootstrap.JoinTickets[index]));

    public ValueTask DisposeAsync() => child.DisposeAsync();
}
