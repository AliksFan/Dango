using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using Dango.Protocol;

namespace Dango.MultiplayerSample;

internal static class LocalSession
{
    public static async Task<int> RunAsync(string directory, string serverPath, bool verify, bool fallbackB)
    {
        using var serverBootstrap = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var serverControl = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var info = new ProcessStartInfo(serverPath) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--bootstrap-pipe"); info.ArgumentList.Add(serverBootstrap.GetClientHandleAsString());
        info.ArgumentList.Add("--control-pipe"); info.ArgumentList.Add(serverControl.GetClientHandleAsString());
        using var server = Process.Start(info) ?? throw new InvalidOperationException("server_start_failed");
        serverBootstrap.DisposeLocalCopyOfClientHandle(); serverControl.DisposeLocalCopyOfClientHandle();
        using var stop = new StreamWriter(serverControl) { AutoFlush = true };
        using var boot = new StreamReader(serverBootstrap);
        ClientProcess? first = null, second = null;
        var code = 0;
        string? endpoint = null;
        try
        {
            var line = await boot.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            var bootstrap = JsonSerializer.Deserialize<LocalBootstrap>(line ?? "", RoomProtocol.Json)
                ?? throw new InvalidDataException("server_bootstrap_failed");
            var uri = new Uri(bootstrap.Endpoint);
            if (uri.Scheme != "http" || !uri.IsLoopback || bootstrap.JoinTickets.Count != 2)
                throw new InvalidDataException("invalid_local_endpoint");
            endpoint = bootstrap.Endpoint;
            first = await ClientProcess.StartAsync(directory, "A", bootstrap, false);
            second = await ClientProcess.StartAsync(directory, "B", bootstrap, fallbackB);
            SessionFiles.Write(directory, "manifest.json", new
            {
                parameterVersion = "p2a-local-1", endpoint, serverPid = server.Id,
                clientA = first.Process.Id, clientB = second.Process.Id, fallbackB,
                binaries = new[] { serverPath, Path.ChangeExtension(serverPath, ".dll"), typeof(RoomProtocol).Assembly.Location,
                    typeof(Program).Assembly.Location, typeof(Dango.Client.Sync.RoomConnection).Assembly.Location,
                    typeof(Dango.Desktop.Multiplayer.NetworkScenePresenter).Assembly.Location,
                    Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll") }.Select(SessionFiles.Fingerprint),
            });
            await WaitAsync(async () => (await first.ReadAsync()).State == "Live" && (await second.ReadAsync()).State == "Live", 30000);
            if (verify) await VerifyAsync(directory, first, second, fallbackB);
            else await Task.WhenAll(first.Process.WaitForExitAsync(), second.Process.WaitForExitAsync());
        }
        catch (Exception exception)
        {
            code = 10;
            SessionFiles.Write(directory, "verification-failure.json", new { code = "session_check_failed", kind = exception.GetType().Name });
        }
        finally
        {
            if (first is not null) await first.CloseAsync();
            if (second is not null) await second.CloseAsync();
            try
            {
                if (!server.HasExited) await stop.WriteLineAsync("STOP");
                await server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception) when (exception is IOException or TimeoutException) { code = 11; }
            var firstAlive = first is not null && !first.Process.HasExited;
            var secondAlive = second is not null && !second.Process.HasExited;
            var serverAlive = !server.HasExited;
            if ((!serverAlive && server.ExitCode != 0) || (first is not null && !firstAlive && first.Process.ExitCode != 0) ||
                (second is not null && !secondAlive && second.Process.ExitCode != 0)) code = 10;
            if (firstAlive || secondAlive || serverAlive) code = 11;
            SessionFiles.Write(directory, "teardown.json", new
            {
                code, endpoint, serverPid = server.Id, serverAlive,
                clientAPid = first?.Process.Id, clientAAlive = firstAlive,
                clientBPid = second?.Process.Id, clientBAlive = secondAlive,
                dataRetained = true,
            });
            first?.Dispose(); second?.Dispose();
        }
        return code;
    }

    private static async Task VerifyAsync(string directory, ClientProcess a, ClientProcess b, bool fallbackB)
    {
        await WaitAsync(async () => (await a.ReadAsync()).RenderedFrames >= 10 && (await b.ReadAsync()).RenderedFrames >= 10);
        await RecordPairAsync("initial");
        await a.SendAsync("move", "right");
        await Task.Delay(600);
        var beforeStop = await a.ReadAsync();
        await a.SendAsync("move", "stopped");
        await WaitAsync(async () =>
        {
            var value = await a.ReadAsync();
            return value.Snapshot!.LastSequence > beforeStop.Snapshot!.LastSequence &&
                value.Snapshot.Pets.Single(p => p.PetId == value.Snapshot.SelfPetId).Motion is null;
        }, 500);
        var stopped = await RecordPairAsync("move-stop");
        if (stopped.Snapshot!.Pets.Single(p => p.PetId == stopped.Snapshot.SelfPetId).AnchorPosition.X <= .30)
            throw new InvalidOperationException("movement_not_observed");
        await a.SendAsync("wave");
        await WaitAsync(async () =>
        {
            var left = await a.ReadAsync(); var right = await b.ReadAsync();
            return left.Snapshot?.Pets.Any(p => p.Action is not null) == true && right.Snapshot?.Pets.Any(p => p.Action is not null) == true;
        });
        await RecordPairAsync("wave");
        await a.SendAsync("capture"); await b.SendAsync("capture");
        await Task.Delay(1400);
        await RecordPairAsync("wave-ended");
        await b.SendAsync("disconnect");
        await a.SendAsync("move", "left"); await Task.Delay(400); await a.SendAsync("move", "stopped");
        await Task.Delay(900);
        var recovery = Stopwatch.StartNew();
        await b.SendAsync("reconnect");
        await WaitAsync(async () => (await b.ReadAsync()).State == "Live");
        await RecordPairAsync("reconnected");
        SessionFiles.Write(directory, "recovery.json", new { elapsedMs = recovery.Elapsed.TotalMilliseconds, measured = true });
        await a.SendAsync("move", "right"); await Task.Delay(200);
        await a.SendAsync("minimize"); await Task.Delay(1100);
        var hidden = await RecordPairAsync("hidden-stopped");
        if (hidden.Snapshot!.Pets.Any(p => p.Motion is not null)) throw new InvalidOperationException("hidden_kept_moving");
        await a.SendAsync("restore");
        await Task.Delay(1000);
        var finalA = await a.ReadAsync();
        var finalB = await b.ReadAsync();
        if (finalA.FallbackPets != 0 || finalB.FallbackPets != (fallbackB ? 2 : 0))
            throw new InvalidOperationException("native_fallback_mode_mismatch");
        if (finalA.Snapshot!.LastSequence != hidden.Snapshot.LastSequence || finalA.Snapshot.Pets.Any(p => p.Motion is not null))
            throw new InvalidOperationException("restore_replayed_old_input");
        SessionFiles.Write(directory, "verification.json", new
        {
            passed = true, actualWpfClients = 2, checks = new[] { "move-stop", "wave", "wave-ended", "reconnect", "hidden-stopped",
                fallbackB ? "native-fallback" : "native-native" },
            fallbackB, manualAcceptance = "pending", dualPc = "pending",
        });

        async Task<SampleReport> RecordPairAsync(string name)
        {
            SampleReport? first = null, second = null;
            await WaitAsync(async () =>
            {
                first = await a.ReadAsync(); second = await b.ReadAsync();
                return first.Snapshot is not null && second.Snapshot is not null && first.Snapshot.LastSequence == second.Snapshot.LastSequence;
            });
            var time = Math.Max(first!.SampledAtServerTimeMs, second!.SampledAtServerTimeMs);
            first = await a.ReadAsync(time); second = await b.ReadAsync(time);
            if (first.State != "Live" || second.State != "Live" || first.Snapshot!.LastSequence != second.Snapshot!.LastSequence ||
                first.Snapshot.RoomEpoch != second.Snapshot.RoomEpoch ||
                JsonSerializer.Serialize(first.Snapshot.Pets.OrderBy(p => p.PetId), RoomProtocol.Json) !=
                JsonSerializer.Serialize(second.Snapshot.Pets.OrderBy(p => p.PetId), RoomProtocol.Json))
                throw new InvalidOperationException("pair_diverged");
            SessionFiles.Write(directory, name + ".json", new { first, second, commonServerTimeMs = time, matched = true });
            return first;
        }
    }

    private static async Task WaitAsync(Func<Task<bool>> predicate, int timeoutMs = 10000)
    {
        var watch = Stopwatch.StartNew();
        while (!await predicate())
        {
            if (watch.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("check_timeout");
            await Task.Delay(100);
        }
    }

    private sealed class ClientProcess : IDisposable
    {
        public Process Process { get; }
        private readonly StreamWriter control;
        private readonly StreamReader reports;
        private ClientProcess(Process process, StreamWriter control, StreamReader reports)
        { Process = process; this.control = control; this.reports = reports; }

        public static async Task<ClientProcess> StartAsync(string directory, string role, LocalBootstrap bootstrap, bool fallback)
        {
            using var bootPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            var controlPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            var reportPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            var info = new ProcessStartInfo(Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe")) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var value in new[] { "--client", role, "--run-name", Path.GetFileName(directory),
                "--bootstrap-pipe", bootPipe.GetClientHandleAsString(), "--control-pipe", controlPipe.GetClientHandleAsString(),
                "--report-pipe", reportPipe.GetClientHandleAsString() }) info.ArgumentList.Add(value);
            if (fallback) info.ArgumentList.Add("--fallback");
            var process = Process.Start(info) ?? throw new InvalidOperationException("client_start_failed");
            try
            {
                bootPipe.DisposeLocalCopyOfClientHandle(); controlPipe.DisposeLocalCopyOfClientHandle(); reportPipe.DisposeLocalCopyOfClientHandle();
                using (var bootWriter = new StreamWriter(bootPipe) { AutoFlush = true })
                    await bootWriter.WriteLineAsync(JsonSerializer.Serialize(new LocalClientBootstrap(bootstrap.Endpoint, bootstrap.RoomId,
                        bootstrap.RoomEpoch, "sample-" + role, bootstrap.JoinTickets[role == "A" ? 0 : 1]), RoomProtocol.Json));
                return new(process, new StreamWriter(controlPipe) { AutoFlush = true }, new StreamReader(reportPipe));
            }
            catch
            {
                bootPipe.Dispose(); controlPipe.Dispose(); reportPipe.Dispose(); // EOF requests child shutdown.
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException) { }
                SessionFiles.Write(directory, "startup-failure-" + role + ".json", new { pid = process.Id, alive = !process.HasExited });
                process.Dispose(); throw;
            }
        }

        public Task<SampleReport> ReadAsync(long? serverTime = null) => RequestAsync(new(Guid.NewGuid().ToString("N"), "snapshot", ServerTimeMs: serverTime));
        public Task<SampleReport> SendAsync(string type, string? direction = null) => RequestAsync(new(Guid.NewGuid().ToString("N"), type, direction));

        private async Task<SampleReport> RequestAsync(SampleCommand command)
        {
            await control.WriteLineAsync(JsonSerializer.Serialize(command, RoomProtocol.Json));
            var line = await reports.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var result = JsonSerializer.Deserialize<SampleReport>(line ?? "", RoomProtocol.Json) ?? throw new IOException("report_missing");
            if (result.Id != command.Id || !result.Success) throw new InvalidOperationException("client_control_failed");
            return result;
        }

        public async Task CloseAsync()
        {
            if (Process.HasExited) return;
            try
            {
                await SendAsync("close");
                await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or InvalidOperationException) { }
        }

        public void Dispose() { control.Dispose(); reports.Dispose(); Process.Dispose(); }
    }
}
