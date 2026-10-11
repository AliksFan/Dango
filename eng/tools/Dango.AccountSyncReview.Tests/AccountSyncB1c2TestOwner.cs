using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Dango.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Dango.AccountSyncReview.Tests;

internal sealed class AccountSyncB1c2TestOwner : IDisposable
{
    internal const string ApprovalVariable = "DANGO_BACKEND_ACCOUNTSYNC_B1C2_APPROVED";
    internal const string Stage = "AccountSyncB1c2";
    internal const string RunName = "run20261011accountsyncb1c2";
    private static readonly object initializationGate = new();
    private static AccountSyncB1c2TestOwner? instance;
    private static bool initializationAttempted;
    private static readonly string[] RequiredHistory =
    [
        "20261008074701_InitialAccountRoomFoundation", "20261008085521_AccountLifecycle",
        "20261009031410_RoomManagement", "20261009055558_RoomAccess", "20261009062701_RoomAccessIntegrity",
        "20261009124814_AccessoryAccountBackup", "20261010055529_AccountProfile"
    ];

    private readonly DurableAudit audit;
    private readonly Action verifyParent;
    private readonly object evidenceGate = new();
    private int disposed;
    private readonly AccountSyncB1cLogoutIntentBudget logoutIntentBudget;
    internal Guid OwnerId { get; }
    internal string RunDirectory { get; }
    internal string ConnectionString { get; }
    internal string DataDirectory { get; }
    internal string KeyDirectory { get; }
    internal Guid RecoveryEpoch { get; }
    internal int DatabasePort { get; }
    internal AccountSyncB1cAttemptBudget Budget { get; }
    internal int LogoutIntents => logoutIntentBudget.Used;

    private AccountSyncB1c2TestOwner(Guid ownerId, string directory, string connection, string dataDirectory,
        string keys, Guid epoch, int port, DurableAudit audit, Action verifyParent)
    {
        OwnerId = ownerId; RunDirectory = directory; ConnectionString = connection;
        DataDirectory = dataDirectory; KeyDirectory = keys; RecoveryEpoch = epoch; DatabasePort = port;
        this.audit = audit;
        this.verifyParent = verifyParent;
        Budget = new(ownerId, audit, CheckAlive);
        logoutIntentBudget = new(ownerId, Budget.CheckAlive, Budget.Abort, AppendLogoutIntent);
    }

    internal static AccountSyncB1c2TestOwner Require() =>
        RequireWithFactory(IsApprovalGranted(Environment.GetEnvironmentVariable), LoadApproved);

    internal static bool IsApprovalGranted(Func<string, string?> readEnvironment) =>
        readEnvironment(ApprovalVariable) == "1";

    internal static AccountSyncB1c2TestOwner RequireWithFactory(bool approved, Func<AccountSyncB1c2TestOwner> loadApproved)
    {
        // The only operation preceding this check is reading the independent grant.
        // No old B1/B5/Profile/B1a grant authorizes this stage or even its resource reads.
        if (!approved)
            Assert.Inconclusive("AccountSyncB1c2 resource access and fictional writes need their own exact approval.");
        lock (initializationGate)
        {
            if (instance is not null) { instance.CheckAlive(); return instance; }
            if (initializationAttempted) throw Reject("initialization_restart_rejected");
            initializationAttempted = true;
            try { return instance = loadApproved(); }
            catch { throw Reject("owner_initialization_failed"); }
        }
    }

    internal void Reserve(AccountSyncOperation operation) => Budget.Reserve(operation);
    internal void CountHttpRequest() => Budget.CountHttpRequest();
    internal void CheckAlive()
    {
        if (Volatile.Read(ref disposed) != 0) throw Reject("owner_disposed");
        verifyParent();
        RequireRegularPath(RunDirectory);
        if (!Directory.Exists(RunDirectory)) throw Reject("run_directory_required");
    }
    internal void Abort() => Budget.Abort();

    internal void RecordLogoutIntent(string caseName, int clientId, Guid userId, Guid petId, Guid sessionId)
        => logoutIntentBudget.Record(caseName, clientId, userId, petId, sessionId);

    private void AppendLogoutIntent(AccountSyncB1cLogoutIntent record)
    {
        lock (evidenceGate)
        {
            var path = Path.Combine(RunDirectory, "logout-intents.jsonl");
            try
            {
                AccountSyncB1cLogoutIntentLedger.AppendExisting(path, record);
            }
            catch { Budget.Abort(); throw Reject("logout_intent_audit_failed"); }
        }
    }

    // Only evidence is writable after business abort or owner loss, so finally blocks
    // can preserve cleanup failures. This never authorizes resource creation or calls.
    internal void WriteEvidence(string name, object value)
    {
        if (name is not ("journey-evidence.json" or "configuration-sync-evidence.json" or "fault-evidence.json" or "scoped-inputs-evidence.json"))
            throw Reject("evidence_name_rejected");
        lock (evidenceGate)
        {
            var path = Path.Combine(RunDirectory, name);
            try
            {
                RequireRegularPath(path);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
                var maximum = name == "fault-evidence.json" ? 1048576 : 131072;
                if (bytes.Length <= 0 || bytes.Length > maximum) throw Reject("evidence_bound_rejected");
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.WriteThrough);
                stream.Write(bytes); stream.Flush(true);
            }
            catch { Budget.Abort(); throw Reject("evidence_write_failed"); }
        }
    }

    private static AccountSyncB1c2TestOwner LoadApproved()
    {
        if (!OperatingSystem.IsWindows()) throw Reject("windows_required");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Dango", "BackendDevelopment", RunName);
        if (!SamePath(RequiredEnvironment("DANGO_BACKEND_ACCOUNTSYNC_B1C2_RUN_DIRECTORY"), directory))
            throw Reject("run_directory_rejected");
        RequireRegularPath(directory);
        var info = new DirectoryInfo(directory);
        if (!info.Exists) throw Reject("run_directory_required");
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw Reject("owner_sid_required");
        var access = info.GetAccessControl();
        if (!access.AreAccessRulesProtected || !sid.Equals(access.GetOwner(typeof(SecurityIdentifier))))
            throw Reject("run_acl_rejected");
        foreach (FileSystemAccessRule rule in access.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(sid)
                && (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Modify | FileSystemRights.FullControl)) != 0)
                throw Reject("run_acl_rejected");

        if (!Guid.TryParseExact(RequiredEnvironment("DANGO_BACKEND_ACCOUNTSYNC_B1C2_OWNER_ID"), "D", out var ownerId) || ownerId == Guid.Empty
            || !int.TryParse(RequiredEnvironment("DANGO_BACKEND_ACCOUNTSYNC_B1C2_OWNER_PID"), out var ownerPid) || ownerPid <= 0)
            throw Reject("owner_identity_rejected");
        var manifestPath = Path.Combine(directory, "owner-manifest.json");
        var manifestBytes = ReadBoundedFile(manifestPath, 65536);
        var expectedDigest = RequiredEnvironment("DANGO_BACKEND_ACCOUNTSYNC_B1C2_OWNER_MANIFEST_SHA256");
        if (expectedDigest.Length != 64 || expectedDigest.Any(character => character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))
            || Convert.ToHexString(SHA256.HashData(manifestBytes)) != expectedDigest)
            throw Reject("owner_manifest_digest_rejected");
        using var manifest = JsonDocument.Parse(manifestBytes, new() { MaxDepth = 12, AllowDuplicateProperties = false });
        var facts = manifest.RootElement;
        if (facts.GetProperty("Version").GetInt32() != 1 || facts.GetProperty("Stage").GetString() != Stage
            || facts.GetProperty("RunName").GetString() != "run20261008b0"
            || facts.GetProperty("OwnerId").GetGuid() != ownerId || facts.GetProperty("OwnerPid").GetInt32() != ownerPid
            || facts.GetProperty("ExpectedTests").GetInt32() != 1
            || !SamePath(facts.GetProperty("RunDirectory").GetString()!, directory))
            throw Reject("owner_manifest_rejected");
        var limits = facts.GetProperty("Budgets");
        if (limits.ValueKind != JsonValueKind.Object || limits.EnumerateObject().Count() != AccountSyncB1cAttemptBudget.Limits.Count
            || AccountSyncB1cAttemptBudget.Limits.Any(item => limits.GetProperty(item.Key.ToString()).GetInt32() != item.Value))
            throw Reject("owner_manifest_budget_rejected");
        var created = facts.GetProperty("OwnerCreatedAt").GetDateTimeOffset();
        var executable = Path.GetFullPath(facts.GetProperty("OwnerExecutable").GetString()!);
        void VerifyParent()
        {
            using var parent = Process.GetProcessById(ownerPid);
            if (parent.HasExited || parent.StartTime.ToUniversalTime() != created.UtcDateTime
                || !SamePath(parent.MainModule!.FileName, executable)) throw Reject("parent_identity_rejected");
        }
        VerifyParent();

        var stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Dango", "BackendDevelopment", "run20261008b0");
        var dataDirectory = Path.Combine(stateDirectory, "pgdata");
        var keys = Path.Combine(stateDirectory, "b1tests-keys");
        RequireRegularPath(dataDirectory); RequireRegularPath(keys);
        if (!Directory.Exists(dataDirectory) || !Directory.Exists(keys)
            || !SamePath(RequiredEnvironment("DANGO_BACKEND_TEST_DATA_DIRECTORY"), dataDirectory)
            || !SamePath(RequiredEnvironment("DANGO_BACKEND_B1_KEY_DIRECTORY"), keys)) throw Reject("original_target_rejected");
        using var state = JsonDocument.Parse(ReadBoundedFile(Path.Combine(stateDirectory, "state.json"), 8192),
            new() { MaxDepth = 8, AllowDuplicateProperties = false });
        var target = state.RootElement;
        var port = target.GetProperty("Port").GetInt32();
        if (target.GetProperty("Version").GetInt32() != 1 || target.GetProperty("RunName").GetString() != "run20261008b0"
            || target.GetProperty("Database").GetString() != "dango_backend_b0"
            || target.GetProperty("Owner").GetString() != "dango_b0_owner"
            || !SamePath(target.GetProperty("DataDirectory").GetString()!, dataDirectory) || port != 63237)
            throw Reject("original_state_rejected");
        if (!Guid.TryParse(RequiredEnvironment("DANGO_BACKEND_B1_RECOVERY_EPOCH"), out var epoch) || epoch == Guid.Empty)
            throw Reject("epoch_rejected");
        using var epochState = JsonDocument.Parse(ReadBoundedFile(Path.Combine(stateDirectory, "b1test-state.json"), 8192),
            new() { MaxDepth = 8, AllowDuplicateProperties = false });
        if (epochState.RootElement.GetProperty("Version").GetInt32() != 1
            || epochState.RootElement.GetProperty("RecoveryEpoch").GetGuid() != epoch) throw Reject("epoch_rejected");
        var connection = new NpgsqlConnectionStringBuilder(RequiredEnvironment("DANGO_BACKEND_TEST_CONNECTION"))
        { IncludeErrorDetail = false, LogParameters = false, Timeout = 5, CommandTimeout = 15, Timezone = "UTC" };
        if (connection.Host != "127.0.0.1" || connection.Port != port || connection.Database != "dango_backend_b0"
            || connection.Username != "dango_b0_owner" || string.IsNullOrEmpty(connection.Password)) throw Reject("connection_target_rejected");
        VerifyDatabase(connection.ConnectionString, dataDirectory);
        VerifyParent();
        var attemptPath = Path.Combine(directory, "attempts.jsonl");
        foreach (var name in new[] { "attempts.jsonl", "resources.jsonl" })
        {
            var path = Path.Combine(directory, name); RequireRegularPath(path);
            var file = new FileInfo(path);
            if (!file.Exists || file.Length != 0) throw Reject("ledger_restart_rejected");
        }
        var audit = new DurableAudit(attemptPath);
        return new(ownerId, directory, connection.ConnectionString, dataDirectory, keys, epoch, port, audit, VerifyParent);
    }

    private static void VerifyDatabase(string connection, string dataDirectory)
    {
        using var db = new NpgsqlConnection(connection); db.Open();
        using var command = new NpgsqlCommand("SELECT current_database(),current_user,current_setting('server_version_num')::integer,current_setting('data_directory'),current_setting('listen_addresses')", db);
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(0) != "dango_backend_b0" || reader.GetString(1) != "dango_b0_owner"
                || reader.GetInt32(2) is < 180000 or >= 190000 || !SamePath(reader.GetString(3), dataDirectory)
                || reader.GetString(4) != "127.0.0.1") throw Reject("actual_target_rejected");
        }
        using var context = new DangoDbContext(DangoDatabaseOptions.Create(connection));
        if (!context.Database.GetAppliedMigrations().SequenceEqual(RequiredHistory, StringComparer.Ordinal)
            || context.Database.GetPendingMigrations().Any()) throw Reject("seven_history_required");
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw Reject("environment_required");

    private static byte[] ReadBoundedFile(string path, long maximum)
    {
        RequireRegularPath(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 || info.Length > maximum) throw Reject("bounded_file_rejected");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximum) throw Reject("bounded_file_rejected");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw Reject("bounded_file_rejected");
        return bytes;
    }

    private static void RequireRegularPath(string path)
    {
        for (DirectoryInfo? part = new(Path.GetFullPath(path)); part is not null; part = part.Parent)
            if (part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0) throw Reject("reparse_rejected");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Reject("reparse_rejected");
    }

    private static bool SamePath(string actual, string expected) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), StringComparison.OrdinalIgnoreCase);
    private static InvalidOperationException Reject(string suffix) => new("accountsync_" + suffix);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Budget.Abort(); audit.Dispose();
    }

    private sealed class DurableAudit(string path) : IAccountSyncAttemptAudit, IDisposable
    {
        private readonly FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        public void AppendAndFlush(AccountSyncAttemptRecord record)
        {
            RequireRegularPath(path);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
            stream.Seek(0, SeekOrigin.End); stream.Write(bytes); stream.WriteByte((byte)'\n'); stream.Flush(true);
        }
        public void Dispose() => stream.Dispose();
    }
}
