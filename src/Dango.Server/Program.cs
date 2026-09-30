using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Realtime;
using Dango.Server.Rooms;
using Microsoft.AspNetCore.Http.Connections;

namespace Dango.Server;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Handles are not credentials. Bootstrap secrets exist only in inherited anonymous pipes and memory.
        if (args.Length != 4 || args[0] != "--bootstrap-pipe" || args[2] != "--control-pipe") return 2;
        try
        {
            using var bootstrap = new AnonymousPipeClientStream(PipeDirection.Out, args[1]);
            using var control = new AnonymousPipeClientStream(PipeDirection.In, args[3]);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(server =>
            {
                server.Listen(IPAddress.Loopback, 0);
                server.Limits.MaxConcurrentConnections = 32;
                server.Limits.MaxRequestBodySize = 4096;
            });
            builder.Services.AddSignalR(options =>
            {
                options.MaximumReceiveMessageSize = 4096;
                options.MaximumParallelInvocationsPerClient = 1;
                options.EnableDetailedErrors = false;
                options.StreamBufferCapacity = 1;
            }).AddJsonProtocol(options => options.PayloadSerializerOptions = RoomProtocol.CreateJsonOptions());
            builder.Services.AddSingleton<ConnectionAdmission>();
            builder.Services.AddSingleton<IRoomEventSink, SignalRRoomEventSink>();
            builder.Services.AddSingleton(provider => new RoomRuntime(sink: provider.GetRequiredService<IRoomEventSink>()));
            await using var app = builder.Build();
            app.MapHub<RoomHub>("/room", options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.ApplicationMaxBufferSize = 4096;
                options.TransportMaxBufferSize = 4096;
            });
            await app.StartAsync();
            var endpoint = app.Urls.Single();
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Host != "127.0.0.1" || uri.Scheme != "http")
                throw new InvalidOperationException("loopback_required");
            var room = app.Services.GetRequiredService<RoomRuntime>();
            await using (var writer = new StreamWriter(bootstrap, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
                await writer.WriteLineAsync(JsonSerializer.Serialize(new LocalBootstrap(endpoint, RoomProtocol.RoomId, room.Epoch, room.JoinTickets), RoomProtocol.Json));
            using var input = new StreamReader(control, Encoding.UTF8, leaveOpen: true);
            // Parent EOF is also a normal shutdown. Nothing survives process exit and no database exists.
            while (await input.ReadLineAsync(app.Lifetime.ApplicationStopping) is { } command)
                if (command == "STOP") break;
            await app.StopAsync();
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return 3; }
    }
}
