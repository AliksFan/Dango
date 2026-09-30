using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dango.Protocol;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests;

internal sealed class ServerProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly AnonymousPipeServerStream control;
    private readonly StreamWriter writer;
    private readonly Task<string> stdout;
    private readonly Task<string> stderr;
    private readonly TestContext context;
    public LocalBootstrap Bootstrap { get; }

    private ServerProcess(Process process, AnonymousPipeServerStream control, LocalBootstrap bootstrap,
        Task<string> stdout, Task<string> stderr, TestContext context)
    {
        this.process = process;
        this.control = control;
        this.stdout = stdout;
        this.stderr = stderr;
        this.context = context;
        writer = new(control, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        Bootstrap = bootstrap;
    }

    public static async Task<ServerProcess> StartAsync(TestContext context)
    {
        var binary = typeof(Program).Assembly.Location;
        Assert.IsTrue(File.Exists(Path.ChangeExtension(binary, ".runtimeconfig.json")));
        using var bootstrapPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var control = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(binary);
        start.ArgumentList.Add("--bootstrap-pipe");
        start.ArgumentList.Add(bootstrapPipe.GetClientHandleAsString());
        start.ArgumentList.Add("--control-pipe");
        start.ArgumentList.Add(control.GetClientHandleAsString());
        var process = Process.Start(start) ?? throw new AssertFailedException("server start failed");
        bootstrapPipe.DisposeLocalCopyOfClientHandle();
        control.DisposeLocalCopyOfClientHandle();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var reader = new StreamReader(bootstrapPipe, Encoding.UTF8, leaveOpen: true);
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.IsNotNull(line, "Server did not provide private bootstrap.");
            var bootstrap = JsonSerializer.Deserialize<LocalBootstrap>(line, RoomProtocol.Json)!;
            var endpoint = new Uri(bootstrap.Endpoint);
            Assert.AreEqual("127.0.0.1", endpoint.Host);
            context.WriteLine($"Real Kestrel PID={process.Id}; endpoint={bootstrap.Endpoint}; transport=WebSockets-only; assemblySHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(binary)))}");
            return new(process, control, bootstrap, stdout, stderr, context);
        }
        catch
        {
            control.Dispose(); // EOF asks this child to shut down normally.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            process.Dispose();
            throw;
        }
    }

    public HubConnection CreateConnection() => new HubConnectionBuilder().WithUrl(Bootstrap.Endpoint + "/room", options =>
    {
        options.Transports = HttpTransportType.WebSockets;
        options.SkipNegotiation = true; // There is no transport fallback in this evidence.
    }).AddJsonProtocol(options => options.PayloadSerializerOptions = RoomProtocol.CreateJsonOptions())
        .ConfigureLogging(logging => logging.ClearProviders()).Build();

    public async Task<JoinRoomResponse> JoinAsync(HubConnection connection, int index) => await connection.InvokeAsync<JoinRoomResponse>("JoinRoom",
        new JoinRoomRequest(RoomProtocol.Version, RoomProtocol.RoomId, $"instance-{index}", Bootstrap.JoinTickets[index]));

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!process.HasExited) { await writer.WriteLineAsync("STOP"); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            Assert.AreEqual(0, process.ExitCode, "Server failed normal shutdown.");
            var output = await stdout;
            var error = await stderr;
            foreach (var ticket in Bootstrap.JoinTickets)
            {
                Assert.IsFalse(output.Contains(ticket, StringComparison.Ordinal), "Credential appeared in stdout.");
                Assert.IsFalse(error.Contains(ticket, StringComparison.Ordinal), "Credential appeared in stderr.");
            }
            context.WriteLine($"Server PID={process.Id} exited normally; stdoutChars={output.Length}; stderrChars={error.Length}; child no longer running.");
        }
        finally { await writer.DisposeAsync(); control.Dispose(); process.Dispose(); }
    }
}
