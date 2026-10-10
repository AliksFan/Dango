using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Dango.BackendManualReview;

public sealed record VerifiedManualReviewCandidate(string SourceRoot, string CandidateRoot, string ExecutablePath,
    string ExecutableSha256, string NativeModulePath, string NativeModuleSha256);
public sealed record ManualReviewDesktopLaunch(string ExecutablePath, string WorkingDirectory, string Argument, Uri Origin);
public sealed record ManualReviewDesktopReceipt(int ProcessId, DateTimeOffset CreationTimeUtc, string ExecutablePath, Uri Origin, int LaunchNumber);
public interface IManualReviewProcess : IDisposable
{
    int ProcessId { get; }
    DateTimeOffset CreationTimeUtc { get; }
    string ExecutablePath { get; }
    bool HasExited { get; }
    Task WaitForExitAsync(CancellationToken ct);
}
public interface IManualReviewDesktopResources
{
    void VerifyCandidate(VerifiedManualReviewCandidate candidate);
    bool LocalRoomReviewExists();
    IManualReviewProcess Start(ManualReviewDesktopLaunch launch);
}
public sealed class OwnedDesktopProcess(IManualReviewProcess process, ManualReviewDesktopReceipt receipt)
{
    public IManualReviewProcess Process { get; } = process;
    public ManualReviewDesktopReceipt Receipt { get; } = receipt;
    internal SemaphoreSlim ExitGate { get; } = new(1, 1);
}
public sealed class ManualReviewDesktopProcess(IManualReviewDesktopResources? resources = null)
{
    public const string LocalRoomReviewMutexName = @"Local\Dango.Phase1.App.LocalRoomReview.Mutex";
    private readonly IManualReviewDesktopResources backend = resources ?? new WindowsDesktopResources();
    private readonly HashSet<OwnedDesktopProcess> ownedProcesses = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<OwnedDesktopProcess> completedProcesses = new(ReferenceEqualityComparer.Instance);
    private readonly object gate = new();
    private Uri? fixedOrigin;
    private VerifiedManualReviewCandidate? fixedCandidate;
    private int launches;

    // Retains ownership even when observing the new process fails after Start.
    public IReadOnlyList<OwnedDesktopProcess> SnapshotOwnedProcesses()
    {
        lock (gate) return ownedProcesses.ToArray();
    }

    public OwnedDesktopProcess Launch(bool authorizationVerified, VerifiedManualReviewCandidate candidate, Uri origin)
    {
        ManualReviewCredentials.RequireAuthorization(authorizationVerified);
        ManualReviewCredentials.RequireManualOrigin(origin);
        ValidateCandidatePaths(candidate);
        lock (gate)
        {
            if (launches >= 4) throw new InvalidOperationException("manual_desktop_launch_budget_exhausted");
            if (fixedOrigin is not null && fixedOrigin != origin) throw new InvalidOperationException("manual_desktop_origin_changed");
            if (fixedCandidate is not null && fixedCandidate != candidate) throw new InvalidOperationException("manual_desktop_candidate_changed");
            if (ownedProcesses.Any(process => !completedProcesses.Contains(process)))
                throw new InvalidOperationException("manual_desktop_owned_exit_unconfirmed");
            backend.VerifyCandidate(candidate);
            if (backend.LocalRoomReviewExists()) throw new InvalidOperationException("manual_desktop_review_already_running");
            fixedOrigin ??= origin; fixedCandidate ??= candidate;
            launches++;
            var process = backend.Start(new(candidate.ExecutablePath, candidate.CandidateRoot, "--local-room-review", fixedOrigin));
            var receipt = new ManualReviewDesktopReceipt(process.ProcessId, process.CreationTimeUtc, process.ExecutablePath, fixedOrigin, launches);
            var owned = new OwnedDesktopProcess(process, receipt);
            ownedProcesses.Add(owned);
            if (receipt.ProcessId <= 0 || !receipt.ExecutablePath.Equals(candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("manual_desktop_start_identity_mismatch");
            return owned;
        }
    }

    public Task<bool> WaitForOwnedExitAsync(bool authorizationVerified, OwnedDesktopProcess owned, TimeSpan timeout, CancellationToken ct = default)
    {
        ManualReviewCredentials.RequireAuthorization(authorizationVerified);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5)) throw new InvalidDataException("manual_desktop_timeout_invalid");
        lock (gate)
        {
            if (!ownedProcesses.Contains(owned)) throw new InvalidOperationException("manual_desktop_ownership_unproven");
            if (completedProcesses.Contains(owned)) return Task.FromResult(true);
            if (owned.Process.ProcessId != owned.Receipt.ProcessId || owned.Process.CreationTimeUtc != owned.Receipt.CreationTimeUtc
                || !owned.Process.ExecutablePath.Equals(owned.Receipt.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("manual_desktop_process_identity_changed");
        }
        return WaitCoreAsync(owned, timeout, ct);
    }

    private async Task<bool> WaitCoreAsync(OwnedDesktopProcess owned, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var entered = false;
        try
        {
            await owned.ExitGate.WaitAsync(deadline.Token); entered = true;
            lock (gate) if (completedProcesses.Contains(owned)) return true;
            await owned.Process.WaitForExitAsync(deadline.Token);
            if (!owned.Process.HasExited) throw new InvalidOperationException("manual_desktop_exit_unproven");
            lock (gate) if (completedProcesses.Add(owned)) owned.Process.Dispose();
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
        finally { if (entered) owned.ExitGate.Release(); }
    }

    public static void ValidateCandidatePaths(VerifiedManualReviewCandidate candidate)
    {
        var source = FullPath(candidate.SourceRoot); var root = FullPath(candidate.CandidateRoot);
        var expectedExe = Path.Combine(root, "Dango.Desktop.exe");
        var expectedNative = Path.Combine(root, "dango_physics_module.dll");
        var candidateParent = Path.Combine(source, "artifacts", "local-room") + Path.DirectorySeparatorChar;
        if (!root.StartsWith(candidateParent, StringComparison.OrdinalIgnoreCase)
            || !FullPath(candidate.ExecutablePath).Equals(expectedExe, StringComparison.OrdinalIgnoreCase)
            || !FullPath(candidate.NativeModulePath).Equals(expectedNative, StringComparison.OrdinalIgnoreCase)
            || !ValidHash(candidate.ExecutableSha256) || !ValidHash(candidate.NativeModuleSha256))
            throw new InvalidDataException("manual_desktop_candidate_invalid");
    }

    private static bool ValidHash(string hash) => hash.Length == 64 && hash.All(Uri.IsHexDigit);
    private static string FullPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(component => component is "." or ".." || component.EndsWith('.') || component.EndsWith(' ')))
            throw new InvalidDataException("manual_desktop_path_invalid");
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
    }

    private sealed class WindowsDesktopResources : IManualReviewDesktopResources
    {
        public void VerifyCandidate(VerifiedManualReviewCandidate candidate)
        {
            ValidateCandidatePaths(candidate);
            foreach (var path in new[] { candidate.ExecutablePath, candidate.NativeModulePath })
            {
                for (var cursor = path; cursor is not null; cursor = Path.GetDirectoryName(cursor))
                    if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("manual_desktop_reparse_point");
            }
            using var exe = File.OpenRead(candidate.ExecutablePath); using var native = File.OpenRead(candidate.NativeModulePath);
            if (!Convert.ToHexString(SHA256.HashData(exe)).Equals(candidate.ExecutableSha256, StringComparison.OrdinalIgnoreCase)
                || !Convert.ToHexString(SHA256.HashData(native)).Equals(candidate.NativeModuleSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("manual_desktop_candidate_hash_mismatch");
        }
        public bool LocalRoomReviewExists()
        {
            try { using var existing = Mutex.OpenExisting(LocalRoomReviewMutexName); return true; }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { throw new InvalidOperationException("manual_desktop_mutex_unverifiable"); }
        }
        public IManualReviewProcess Start(ManualReviewDesktopLaunch launch)
        {
            var info = new ProcessStartInfo(launch.ExecutablePath)
            {
                WorkingDirectory = launch.WorkingDirectory, UseShellExecute = false, CreateNoWindow = false,
            };
            info.ArgumentList.Add(launch.Argument);
            info.Environment["DANGO_SERVER_ORIGIN"] = launch.Origin.GetLeftPart(UriPartial.Authority);
            var process = Process.Start(info) ?? throw new IOException("manual_desktop_start_failed");
            return new WindowsOwnedProcess(process, launch.ExecutablePath);
        }
    }

    private sealed class WindowsOwnedProcess(Process process, string launchedPath) : IManualReviewProcess
    {
        public int ProcessId => process.Id;
        public DateTimeOffset CreationTimeUtc => new(process.StartTime.ToUniversalTime());
        public string ExecutablePath => process.HasExited ? launchedPath : process.MainModule?.FileName ?? throw new IOException("manual_desktop_path_unavailable");
        public bool HasExited => process.HasExited;
        public Task WaitForExitAsync(CancellationToken ct) => process.WaitForExitAsync(ct);
        public void Dispose() => process.Dispose();
    }
}
