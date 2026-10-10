using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Dango.Server.Tests.Accounts;

internal enum ProfileAttemptOperation
{
    Invite, Start, Confirm, Complete, Login, DirectProfileUpdate, HttpProfilePatch, SessionRevoke, HttpLogout
}

internal sealed record ProfileAttemptRecord(int Version, Guid OwnerId, long Sequence, string CaseId,
    string Operation, int Used, DateTimeOffset AtUtc);

internal interface IProfileAttemptAudit
{
    void AppendAndFlush(ProfileAttemptRecord record);
}

// A fresh test process cannot resume this owner or refund calls. The runner creates the directory,
// zero-length ledger and immutable manifest before resource access; this process only appends.
internal sealed class ProfileAttemptBudget(Guid ownerId, IProfileAttemptAudit audit, Action assertOwnerAlive)
{
    internal static readonly IReadOnlyDictionary<ProfileAttemptOperation, int> Limits = new ReadOnlyDictionary<ProfileAttemptOperation, int>(new Dictionary<ProfileAttemptOperation, int>
    {
        [ProfileAttemptOperation.Invite] = 9, [ProfileAttemptOperation.Start] = 9,
        [ProfileAttemptOperation.Confirm] = 9, [ProfileAttemptOperation.Complete] = 9,
        [ProfileAttemptOperation.Login] = 10, [ProfileAttemptOperation.DirectProfileUpdate] = 11,
        [ProfileAttemptOperation.HttpProfilePatch] = 8, [ProfileAttemptOperation.SessionRevoke] = 1,
        [ProfileAttemptOperation.HttpLogout] = 1
    });
    private readonly object gate = new();
    private readonly Dictionary<ProfileAttemptOperation, int> used = [];
    private readonly HashSet<string> begun = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<ProfileAttemptOperation, int>> perCase = new(StringComparer.Ordinal);
    private long sequence;
    private int httpRequests;
    private bool aborted;

    internal static IReadOnlyDictionary<ProfileAttemptOperation, int> CaseLimits(string caseId)
    {
        var (graphs, logins, updates, patches, revokes, logouts) = caseId switch
        {
            "concurrent_profile" => (1, 1, 2, 0, 0, 0),
            "duplicate_names" => (2, 2, 4, 0, 0, 0),
            "rollback_after_profile" or "rollback_before_commit" => (1, 1, 1, 0, 0, 0),
            "cross_session" => (2, 2, 2, 0, 1, 0),
            "expiry" => (1, 1, 1, 0, 0, 0),
            "https" => (1, 2, 0, 8, 0, 1),
            _ => throw Reject()
        };
        return new Dictionary<ProfileAttemptOperation, int>
        {
            [ProfileAttemptOperation.Invite] = graphs, [ProfileAttemptOperation.Start] = graphs,
            [ProfileAttemptOperation.Confirm] = graphs, [ProfileAttemptOperation.Complete] = graphs,
            [ProfileAttemptOperation.Login] = logins, [ProfileAttemptOperation.DirectProfileUpdate] = updates,
            [ProfileAttemptOperation.HttpProfilePatch] = patches, [ProfileAttemptOperation.SessionRevoke] = revokes,
            [ProfileAttemptOperation.HttpLogout] = logouts
        };
    }

    internal void BeginCase(string caseId)
    {
        lock (gate)
        {
            Check();
            try { _ = CaseLimits(caseId); }
            catch { aborted = true; throw; }
            if (!begun.Add(caseId)) { aborted = true; throw Reject(); }
            perCase.Add(caseId, []);
        }
    }

    internal void Reserve(string caseId, ProfileAttemptOperation operation)
    {
        lock (gate)
        {
            Check();
            if (!Enum.IsDefined(operation) || !perCase.TryGetValue(caseId, out var local)) { aborted = true; throw Reject(); }
            var count = used.GetValueOrDefault(operation);
            var localCount = local.GetValueOrDefault(operation);
            if (count >= Limits[operation] || localCount >= CaseLimits(caseId)[operation])
            { aborted = true; throw new InvalidOperationException("profile_attempt_budget_exhausted"); }
            // Consume before append; even a failed durable write has no refund or executable continuation.
            used[operation] = count + 1; local[operation] = localCount + 1;
            try { audit.AppendAndFlush(new(1, ownerId, ++sequence, caseId, operation.ToString(), count + 1, DateTimeOffset.UtcNow)); }
            catch { aborted = true; throw; }
        }
    }

    internal void CountHttpRequest(string caseId)
    {
        lock (gate)
        {
            Check();
            if (caseId != "https" || !perCase.ContainsKey(caseId) || httpRequests >= 22)
            { aborted = true; throw Reject(); }
            httpRequests++;
        }
    }

    internal void EndCase(string caseId, bool passed)
    {
        lock (gate)
        {
            if (!passed) { aborted = true; return; }
            Check();
            if (!perCase.TryGetValue(caseId, out var local)
                || CaseLimits(caseId).Any(item => local.GetValueOrDefault(item.Key) != item.Value)
                || caseId == "https" && httpRequests != 22)
            { aborted = true; throw new InvalidOperationException("profile_case_attempts_incomplete"); }
        }
    }

    internal IReadOnlyDictionary<ProfileAttemptOperation, int> Snapshot()
    { lock (gate) return Limits.Keys.ToDictionary(operation => operation, operation => used.GetValueOrDefault(operation)); }

    internal void VerifyAlive() { lock (gate) Check(); }
    internal void Abort() { lock (gate) aborted = true; }

    private void Check()
    {
        if (aborted) throw Reject();
        try { assertOwnerAlive(); }
        catch { aborted = true; throw; }
    }
    private static InvalidOperationException Reject() => new("profile_owner_or_case_rejected");
}

internal sealed class ProfileTestOwner
{
    private const string Stage = "AccountProfileA";
    private static readonly object initialization = new();
    private static ProfileTestOwner? current;
    private static bool initializationAttempted;
    private readonly object caseGate = new();
    private string? activeCase;
    private readonly ProfileAttemptBudget budget;
    private readonly Guid ownerId;
    private readonly ProfileResourceBudget resources;

    private ProfileTestOwner(Guid ownerId, ProfileAttemptBudget budget, ProfileResourceBudget resources)
    { this.ownerId = ownerId; this.budget = budget; this.resources = resources; }

    internal static ProfileTestOwner Require()
    {
        // This precedes all directory, process, connection, key, port and certificate inspection.
        if (Environment.GetEnvironmentVariable("DANGO_BACKEND_PROFILE_TEST_APPROVED") != "1")
            throw new InvalidOperationException("profile_independent_approval_required");
        lock (initialization)
        {
            if (current is not null) return current;
            if (initializationAttempted) throw new InvalidOperationException("profile_owner_initialization_restart_rejected");
            initializationAttempted = true;
            return current = LoadFresh();
        }
    }

    internal Guid OwnerId => ownerId;
    internal void BeginCase(string caseId)
    {
        lock (caseGate)
        {
            if (activeCase is not null) { budget.Abort(); throw new InvalidOperationException("profile_parallel_cases_rejected"); }
            budget.BeginCase(caseId); activeCase = caseId;
        }
    }
    internal void EndCase(bool passed)
    {
        lock (caseGate)
        {
            var caseId = activeCase; activeCase = null;
            if (caseId is not null) budget.EndCase(caseId, passed);
        }
    }
    internal void Reserve(ProfileAttemptOperation operation) => budget.Reserve(CaseId(), operation);
    internal void CountHttpRequest() => budget.CountHttpRequest(CaseId());
    internal Task<ProfileFixtureScope<T>> CreateFixtureAsync<T>(bool startHttp, Func<Task<T>> create) where T : IAsyncDisposable =>
        ProfileFixtureScope<T>.CreateAsync(resources, CaseId(), startHttp, create);
    private string CaseId() { lock (caseGate) return activeCase ?? throw new InvalidOperationException("profile_case_required"); }

    private static ProfileTestOwner LoadFresh()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("profile_windows_owner_required");
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("DANGO_BACKEND_PROFILE_OWNER_ID"), out var ownerId) || ownerId == Guid.Empty
            || !int.TryParse(Environment.GetEnvironmentVariable("DANGO_BACKEND_PROFILE_OWNER_PID"), out var pid) || pid <= 0)
            throw new InvalidOperationException("profile_owner_environment_required");
        var supplied = Environment.GetEnvironmentVariable("DANGO_PROFILE_VERIFICATION_RUN_DIRECTORY");
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Dango", "BackendDevelopment", "run20261010profilea");
        if (string.IsNullOrEmpty(supplied) || !Path.GetFullPath(supplied).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("profile_run_directory_rejected");
        for (DirectoryInfo? directory = new(expected); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("profile_run_directory_rejected");
        var access = new DirectoryInfo(expected).GetAccessControl();
        if (!access.AreAccessRulesProtected) throw new InvalidOperationException("profile_run_acl_rejected");
        var allowed = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("profile_owner_sid_rejected");
        foreach (FileSystemAccessRule rule in access.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(allowed)
                && (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Modify | FileSystemRights.FullControl)) != 0)
                throw new InvalidOperationException("profile_run_acl_rejected");
        var manifestFile = new FileInfo(Path.Combine(expected, "owner-manifest.json"));
        if (!manifestFile.Exists || manifestFile.Length is <= 0 or > 65536 || (manifestFile.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("profile_manifest_rejected");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestFile.FullName));
        var facts = manifest.RootElement;
        if (facts.GetProperty("Version").GetInt32() != 1 || facts.GetProperty("Stage").GetString() != Stage
            || facts.GetProperty("RunName").GetString() != "run20261008b0" || facts.GetProperty("OwnerId").GetGuid() != ownerId
            || facts.GetProperty("OwnerPid").GetInt32() != pid || facts.GetProperty("ExpectedTests").GetInt32() != 7
            || !Path.GetFullPath(facts.GetProperty("RunDirectory").GetString()!).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("profile_manifest_rejected");
        var limits = facts.GetProperty("Budgets");
        if (limits.EnumerateObject().Count() != ProfileAttemptBudget.Limits.Count
            || ProfileAttemptBudget.Limits.Any(item => limits.GetProperty(item.Key.ToString()).GetInt32() != item.Value))
            throw new InvalidOperationException("profile_manifest_budget_rejected");
        var createdAt = facts.GetProperty("OwnerCreatedAt").GetDateTimeOffset();
        var executable = Path.GetFullPath(facts.GetProperty("OwnerExecutable").GetString()!);
        void AssertOwnerAlive()
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || process.StartTime.ToUniversalTime() != createdAt.UtcDateTime
                || !Path.GetFullPath(process.MainModule!.FileName).Equals(executable, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("profile_owner_lifetime_rejected");
        }
        AssertOwnerAlive();
        var ledgerFile = new FileInfo(Path.Combine(expected, "attempts.jsonl"));
        if (!ledgerFile.Exists || ledgerFile.Length != 0 || (ledgerFile.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("profile_attempt_ledger_restart_rejected");
        var resourceFile = new FileInfo(Path.Combine(expected, "resources.jsonl"));
        if (!resourceFile.Exists || resourceFile.Length != 0 || (resourceFile.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("profile_resource_ledger_restart_rejected");
        var ledger = new DurableAudit(ledgerFile.FullName, resourceFile.FullName);
        var budget = new ProfileAttemptBudget(ownerId, ledger, AssertOwnerAlive);
        return new(ownerId, budget, new(ownerId, ledger, budget.VerifyAlive, budget.Abort));
    }

    private sealed class DurableAudit : IProfileAttemptAudit, IProfileResourceAudit
    {
        private readonly FileStream attempts;
        private readonly FileStream resources;
        internal DurableAudit(string attemptPath, string resourcePath)
        {
            attempts = new(attemptPath, FileMode.Open, FileAccess.Write, FileShare.Read);
            try { resources = new(resourcePath, FileMode.Open, FileAccess.Write, FileShare.Read); }
            catch { attempts.Dispose(); throw; }
        }
        public void AppendAndFlush(ProfileAttemptRecord record) => Append(attempts, record);
        public void AppendAndFlush(ProfileResourceRecord record) => Append(resources, record);
        private static void Append<T>(FileStream stream, T record)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
            stream.Seek(0, SeekOrigin.End);
            stream.Write(bytes); stream.WriteByte((byte)'\n'); stream.Flush(true);
        }
    }
}

internal sealed record ProfileResourceRecord(int Version, Guid OwnerId, long Sequence, Guid FixtureId,
    string CaseId, string Phase, bool RequestedHttps, bool HttpsStarted, bool NativeKeyDisposeConfirmed, DateTimeOffset AtUtc);

internal interface IProfileResourceAudit
{
    void AppendAndFlush(ProfileResourceRecord record);
}

// Cleanup receipts remain writable after owner loss. Resource creation and business calls do not.
internal sealed class ProfileResourceBudget(Guid ownerId, IProfileResourceAudit audit, Action verifyAlive, Action abort)
{
    private readonly object gate = new();
    private readonly HashSet<string> cases = new(StringComparer.Ordinal);
    private long sequence;

    internal Guid Prepare(string caseId, bool startHttp)
    {
        lock (gate)
        {
            try { verifyAlive(); _ = ProfileAttemptBudget.CaseLimits(caseId); }
            catch { abort(); throw; }
            if (cases.Count >= 7 || !cases.Add(caseId) || startHttp != (caseId == "https"))
            { abort(); throw new InvalidOperationException("profile_fixture_budget_rejected"); }
            var id = Guid.NewGuid();
            Append(id, caseId, "preparing", false, false);
            return id;
        }
    }

    internal void VerifyAlive() => verifyAlive();
    internal void Record(Guid id, string caseId, string phase, bool httpsStarted, bool disposed)
    {
        lock (gate)
        {
            if (phase is "preparation_failed" or "dispose_failed") abort();
            Append(id, caseId, phase, httpsStarted, disposed);
        }
    }

    private void Append(Guid id, string caseId, string phase, bool httpsStarted, bool disposed)
    {
        try { audit.AppendAndFlush(new(1, ownerId, ++sequence, id, caseId, phase, caseId == "https", httpsStarted, disposed, DateTimeOffset.UtcNow)); }
        catch { abort(); throw; }
    }
}

internal sealed class ProfileFixtureScope<T> : IAsyncDisposable where T : IAsyncDisposable
{
    private readonly ProfileResourceBudget resources;
    private readonly Guid id;
    private readonly string caseId;
    private readonly bool httpsStarted;
    private int disposed;
    internal T Fixture { get; }

    private ProfileFixtureScope(T fixture, ProfileResourceBudget resources, Guid id, string caseId, bool httpsStarted)
    { Fixture = fixture; this.resources = resources; this.id = id; this.caseId = caseId; this.httpsStarted = httpsStarted; }

    internal static async Task<ProfileFixtureScope<T>> CreateAsync(ProfileResourceBudget resources, string caseId,
        bool startHttp, Func<Task<T>> create)
    {
        var id = resources.Prepare(caseId, startHttp);
        ProfileFixtureScope<T>? scope = null;
        try
        {
            resources.VerifyAlive();
            var fixture = await create();
            scope = new(fixture, resources, id, caseId, startHttp);
            resources.Record(id, caseId, "ready", startHttp, false);
            resources.VerifyAlive();
            return scope;
        }
        catch
        {
            if (scope is not null) await scope.DisposeAsync();
            else resources.Record(id, caseId, "preparation_failed", false, false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Exception? receiptFailure = null;
        try { resources.Record(id, caseId, "dispose_started", httpsStarted, false); }
        catch (Exception failure) { receiptFailure = failure; }
        try { await Fixture.DisposeAsync(); }
        catch
        {
            // A failed stop/dispose never receives a successful cleanup receipt.
            resources.Record(id, caseId, "dispose_failed", httpsStarted, false);
            throw;
        }
        resources.Record(id, caseId, "disposed", httpsStarted, true);
        if (receiptFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(receiptFailure).Throw();
    }
}
