using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Tests.Support;

// Owns only the private local-test protocol and this fixture's child process.
// Callers keep their own binary selection and business/transport assertions.
internal sealed class LocalServerProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly AnonymousPipeServerStream control;
    private readonly Task<string> stdout;
    private readonly Task<string> stderr;
    private readonly Action<string> log;
    public LocalBootstrap Bootstrap { get; }
    public int Pid => process.Id;

    private LocalServerProcess(Process process, AnonymousPipeServerStream control, LocalBootstrap bootstrap,
        Task<string> stdout, Task<string> stderr, Action<string> log)
    {
        this.process = process;
        this.control = control;
        this.stdout = stdout;
        this.stderr = stderr;
        this.log = log;
        Bootstrap = bootstrap;
    }

    public static async Task<LocalServerProcess> StartAsync(ProcessStartInfo start, string binary, Action<string> log)
    {
        using var bootstrapPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var control = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        Process? process = null;
        try
        {
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            foreach (var value in new[] { "--bootstrap-pipe", bootstrapPipe.GetClientHandleAsString(), "--control-pipe", control.GetClientHandleAsString() })
                start.ArgumentList.Add(value);
            process = Process.Start(start) ?? throw new AssertFailedException("Server start failed.");
            bootstrapPipe.DisposeLocalCopyOfClientHandle();
            control.DisposeLocalCopyOfClientHandle();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var reader = new StreamReader(bootstrapPipe, Encoding.UTF8, leaveOpen: true);
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.IsNotNull(line, "Server did not provide private bootstrap.");
            LocalBootstrap? bootstrap;
            try { bootstrap = JsonSerializer.Deserialize<LocalBootstrap>(line, RoomProtocol.Json); }
            catch (JsonException) { throw new AssertFailedException("Server provided invalid private bootstrap JSON."); }
            Assert.IsNotNull(bootstrap, "Server provided empty private bootstrap.");
            // Never include the private payload, tickets, or raw child output in test diagnostics.
            Assert.IsTrue(Uri.TryCreate(bootstrap.Endpoint, UriKind.Absolute, out var endpoint), "Invalid local server endpoint.");
            Assert.IsTrue(endpoint.Scheme == "http" && endpoint.Host == "127.0.0.1" &&
                endpoint.UserInfo.Length == 0 && endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0,
                "Server must expose an unauthenticated loopback endpoint without private URL fields.");
            log($"Real Kestrel PID={process.Id}; endpoint={endpoint.GetLeftPart(UriPartial.Authority)}; binarySHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(binary)))}");
            return new(process, control, bootstrap, stdout, stderr, log);
        }
        catch
        {
            control.Dispose(); // EOF requests shutdown even when bootstrap fails.
            if (process is not null)
            {
                try { await EnsureExitedAsync(process); }
                finally { process.Dispose(); }
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!process.HasExited)
            {
                await control.WriteAsync("STOP\n"u8.ToArray()).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                await control.FlushAsync().WaitAsync(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            Assert.AreEqual(0, process.ExitCode, "Server failed normal shutdown.");
            var output = await stdout;
            var error = await stderr;
            foreach (var ticket in Bootstrap.JoinTickets)
            {
                Assert.IsFalse(output.Contains(ticket, StringComparison.Ordinal), "Credential appeared in stdout.");
                Assert.IsFalse(error.Contains(ticket, StringComparison.Ordinal), "Credential appeared in stderr.");
            }
            log($"Server PID={process.Id} exited normally; stdoutChars={output.Length}; stderrChars={error.Length}; child no longer running.");
        }
        finally
        {
            control.Dispose();
            try { await EnsureExitedAsync(process); }
            finally { process.Dispose(); }
        }
    }

    private static async Task EnsureExitedAsync(Process process)
    {
        if (process.HasExited) return;
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException)
        {
            // Reap this exact fixture-owned child if STOP/EOF cannot complete.
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
