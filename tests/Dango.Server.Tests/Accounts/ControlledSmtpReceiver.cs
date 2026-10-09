using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace Dango.Server.Tests.Accounts;

// This receiver accepts fictional recipients only. Message proofs stay in memory.
internal sealed class ControlledSmtpReceiver : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentBag<Task> connections = [];
    private readonly Channel<string> messages = Channel.CreateBounded<string>(128);
    private readonly Task accepting;
    private int attempts;

    public ControlledSmtpReceiver()
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        accepting = AcceptAsync();
    }

    public int Port { get; }
    public bool RejectTransiently { get; set; }
    public int Attempts => Volatile.Read(ref attempts);

    public async Task<string> ReceiveAsync(CancellationToken ct = default)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(15));
        return await messages.Reader.ReadAsync(bounded.Token);
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stopping.Token);
                connections.Add(HandleAsync(client));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (SocketException) when (stopping.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                using var input = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
                await using var output = new StreamWriter(stream, Encoding.ASCII, 4096, true) { NewLine = "\r\n", AutoFlush = true };
                await output.WriteLineAsync("220 localhost Dango fictional SMTP receiver");
                var fictionalRecipient = false;
                while (!stopping.IsCancellationRequested)
                {
                    var line = await input.ReadLineAsync(stopping.Token);
                    if (line is null) break;
                    var verb = line.Split(' ', 2)[0].ToUpperInvariant();
                    switch (verb)
                    {
                        case "EHLO":
                            await output.WriteLineAsync("250-localhost");
                            await output.WriteLineAsync("250 SIZE 65536");
                            break;
                        case "HELO":
                        case "MAIL":
                        case "NOOP":
                            await output.WriteLineAsync("250 OK");
                            break;
                        case "RCPT":
                            fictionalRecipient = line.TrimEnd().EndsWith(".invalid>", StringComparison.OrdinalIgnoreCase);
                            await output.WriteLineAsync(fictionalRecipient ? "250 OK" : "550 fictional recipients only");
                            break;
                        case "RSET":
                            fictionalRecipient = false;
                            await output.WriteLineAsync("250 OK");
                            break;
                        case "DATA":
                            if (!fictionalRecipient) { await output.WriteLineAsync("503 recipient required"); break; }
                            await output.WriteLineAsync("354 End with a dot");
                            var body = new StringBuilder();
                            while (true)
                            {
                                var data = await input.ReadLineAsync(stopping.Token);
                                if (data is null) return;
                                if (data == ".") break;
                                if (data.StartsWith("..", StringComparison.Ordinal)) data = data[1..];
                                body.Append(data).Append("\r\n");
                                if (body.Length > 65536) { await output.WriteLineAsync("552 message too large"); return; }
                            }
                            Interlocked.Increment(ref attempts);
                            if (RejectTransiently) await output.WriteLineAsync("451 controlled transient failure");
                            else if (messages.Writer.TryWrite(body.ToString())) await output.WriteLineAsync("250 accepted by controlled receiver");
                            else await output.WriteLineAsync("452 controlled queue full");
                            fictionalRecipient = false;
                            break;
                        case "QUIT":
                            await output.WriteLineAsync("221 closing");
                            return;
                        default:
                            await output.WriteLineAsync("502 command unavailable");
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (IOException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        await accepting;
        await Task.WhenAll(connections.ToArray());
        messages.Writer.TryComplete();
        stopping.Dispose();
    }
}
