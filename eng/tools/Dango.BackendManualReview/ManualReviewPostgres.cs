using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Dango.BackendManualReview;

public sealed class ManualReviewPostgres(ManualReviewTarget target)
{
    private readonly List<ManualReviewToolProcess> subprocesses = [];
    private readonly object processGate = new();
    public int StartedPid { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    private bool identityProven;
    public IReadOnlyList<ManualReviewToolProcess> ToolProcesses { get { lock (processGate) return subprocesses.ToArray(); } }
    public static bool StartWasOwned(int controlExitCode, DateTimeOffset attemptedAt, DateTimeOffset returnedAt, DateTimeOffset processStartedAt)
        => controlExitCode == 0 && processStartedAt >= attemptedAt && processStartedAt <= returnedAt;

    public static bool ListenerPresent(int port) => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == port);

    public async Task CheckStoppedAsync(bool approved)
    {
        ManualReviewTarget.RequireApproval(approved);
        var version = await RunAsync(Path.Combine(target.BinaryDirectory, "postgres.exe"), ["--version"]);
        if (version.ExitCode != 0 || !version.Output.StartsWith("postgres (PostgreSQL) 18.", StringComparison.Ordinal))
            throw new ManualReviewRejected("manual_review_postgres18_required");
        var status = await ControlAsync(["status", "-D", target.DataDirectory]);
        if (status.ExitCode != 3 || ListenerPresent(target.DatabasePort) || File.Exists(Path.Combine(target.DataDirectory, "postmaster.pid")))
            throw new ManualReviewRejected("manual_review_existing_postgres_rejected");
    }

    public async Task StartAsync(bool approved)
    {
        ManualReviewTarget.RequireApproval(approved);
        await CheckStoppedAsync(approved);
        var log = Path.Combine(target.ManualDirectory, "postgresql.log");
        if (File.Exists(log)) throw new ManualReviewRejected("manual_review_existing_log_rejected");
        var attemptedAt = DateTimeOffset.UtcNow;
        var result = await ControlAsync(["start", "-D", target.DataDirectory, "-l", log, "-o",
            $"-h 127.0.0.1 -p {target.DatabasePort.ToString(CultureInfo.InvariantCulture)}", "-w", "-t", "30"]);
        var returnedAt = DateTimeOffset.UtcNow;
        var pidFile = Path.Combine(target.DataDirectory, "postmaster.pid");
        if (File.Exists(pidFile))
        {
            ManualReviewTarget.RequireFile(pidFile, 4096);
            if (int.TryParse(File.ReadLines(pidFile).First(), out var pid) && pid > 0)
            {
                StartedPid = pid;
                using var process = Process.GetProcessById(pid);
                StartedAt = new DateTimeOffset(process.StartTime.ToUniversalTime());
                identityProven = StartWasOwned(result.ExitCode, attemptedAt, returnedAt, StartedAt.Value)
                    && await MatchesOwnedAsync();
            }
        }
        if (result.ExitCode != 0 || !identityProven) throw new ManualReviewRejected("manual_review_postgres_start_not_proven");
    }

    public async Task<bool> StopOnlyOwnedAsync(bool approved)
    {
        ManualReviewTarget.RequireApproval(approved);
        if (StartedPid == 0) return !ListenerPresent(target.DatabasePort) && !File.Exists(Path.Combine(target.DataDirectory, "postmaster.pid"));
        if (ProcessPresent(StartedPid))
        {
            if (!identityProven || !await MatchesOwnedAsync()) return false;
            var result = await ControlAsync(["stop", "-D", target.DataDirectory, "-m", "fast", "-w", "-t", "30"]);
            if (result.ExitCode != 0) return false;
        }
        return !ProcessPresent(StartedPid) && !ListenerPresent(target.DatabasePort)
            && !File.Exists(Path.Combine(target.DataDirectory, "postmaster.pid"));
    }

    public async Task<bool> MatchesOwnedAsync()
    {
        if (StartedPid <= 0 || StartedAt is null) return false;
        try
        {
            var pidFile = Path.Combine(target.DataDirectory, "postmaster.pid");
            ManualReviewTarget.RequireFile(pidFile, 4096);
            var lines = File.ReadAllLines(pidFile);
            if (lines.Length < 4 || lines[0] != StartedPid.ToString(CultureInfo.InvariantCulture)
                || !ManualReviewTarget.SamePath(lines[1], target.DataDirectory)
                || lines[3] != target.DatabasePort.ToString(CultureInfo.InvariantCulture)) return false;
            using var process = Process.GetProcessById(StartedPid);
            var executable = Path.Combine(target.BinaryDirectory, "postgres.exe");
            if (new DateTimeOffset(process.StartTime.ToUniversalTime()) != StartedAt
                || !ManualReviewTarget.SamePath(process.MainModule!.FileName, executable)) return false;
            var commandLine = await ReadCommandLineAsync(StartedPid);
            if (!MatchArguments(SplitCommandLine(commandLine), executable, target.DataDirectory, target.DatabasePort)) return false;
            using var final = Process.GetProcessById(StartedPid);
            return new DateTimeOffset(final.StartTime.ToUniversalTime()) == StartedAt;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
    }

    public static bool MatchArguments(string[] args, string executable, string dataDirectory, int port)
    {
        if (args.Length != 7 || !ManualReviewTarget.SamePath(args[0], executable)) return false;
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
            if (args[i] is not ("-D" or "-h" or "-p") || !values.TryAdd(args[i], args[i + 1])) return false;
        return values.TryGetValue("-D", out var directory) && ManualReviewTarget.SamePath(directory, dataDirectory)
            && values.GetValueOrDefault("-h") == "127.0.0.1" && values.GetValueOrDefault("-p") == port.ToString(CultureInfo.InvariantCulture);
    }

    public static bool ProcessPresent(int pid)
    {
        if (pid <= 0) return false;
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private Task<(int ExitCode, string Output)> ControlAsync(string[] arguments) => RunAsync(Path.Combine(target.BinaryDirectory, "pg_ctl.exe"), arguments);
    private async Task<(int ExitCode, string Output)> RunAsync(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        // Even pg_ctl can continue starting PostgreSQL after identity observation fails.
        // Retain a provisional capability before reading any fallible process properties.
        var capability = new ManualReviewToolProcess(0, DateTimeOffset.MinValue, executable, Guid.NewGuid(), false);
        int index;
        lock (processGate) { index = subprocesses.Count; subprocesses.Add(capability); }
        var pid = process.Id;
        lock (processGate) subprocesses[index] = capability with { Pid = pid };
        var createdAt = new DateTimeOffset(process.StartTime.ToUniversalTime());
        lock (processGate) subprocesses[index] = capability with { Pid = pid, StartedAt = createdAt, IdentityVerified = true };
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
        string text;
        try { text = await output.WaitAsync(TimeSpan.FromSeconds(1)); }
        catch (TimeoutException) { text = string.Empty; }
        // Drain errors without exposing PostgreSQL paths or configuration in UI/logs.
        try { _ = await error.WaitAsync(TimeSpan.FromSeconds(1)); }
        catch (TimeoutException) { }
        return (process.ExitCode, text.Trim());
    }

    private async Task<string> ReadCommandLineAsync(int pid)
    {
        var executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var script = "$OutputEncoding=[Text.UTF8Encoding]::new($false);[Console]::OutputEncoding=$OutputEncoding;"
            + "[pscustomobject]@{CommandLine=(Get-CimInstance Win32_Process -Filter 'ProcessId="
            + pid.ToString(CultureInfo.InvariantCulture) + "').CommandLine}|ConvertTo-Json -Compress";
        var result = await RunAsync(executable, ["-NoProfile", "-NonInteractive", "-Command", script]);
        if (result.ExitCode != 0 || result.Output.Length > 16384) throw new ManualReviewRejected("manual_review_process_proof_rejected");
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.GetProperty("CommandLine").GetString() ?? throw new ManualReviewRejected("manual_review_process_proof_missing");
    }

    private static string[] SplitCommandLine(string command)
    {
        var pointer = CommandLineToArgvW(command, out var count);
        if (pointer == IntPtr.Zero) throw new ManualReviewRejected("manual_review_process_arguments_rejected");
        try
        {
            var result = new string[count];
            for (var i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size))!;
            return result;
        }
        finally { _ = LocalFree(pointer); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argc);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);
}

public sealed record ManualReviewToolProcess(int Pid, DateTimeOffset StartedAt, string Executable, Guid? CapabilityId = null, bool IdentityVerified = true)
{
    public bool HasExited()
    {
        if (!IdentityVerified || Pid <= 0 || StartedAt == DateTimeOffset.MinValue) return false;
        try
        {
            using var process = Process.GetProcessById(Pid);
            return process.HasExited || new DateTimeOffset(process.StartTime.ToUniversalTime()) != StartedAt;
        }
        catch (ArgumentException) { return true; }
        catch { return false; }
    }
}
