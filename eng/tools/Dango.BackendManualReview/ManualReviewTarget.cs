using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Dango.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Dango.BackendManualReview;

public sealed class ManualReviewRejected(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed record ManualReviewTarget(string SourceRoot, string StateDirectory, string DataDirectory,
    string ManualDirectory, string CandidateRoot, string BinaryDirectory, int DatabasePort,
    Guid RecoveryEpoch, string KeyDirectory)
{
    public const string ExistingRun = "run20261008b0";
    public const string ManualRun = "run20261010b5cmanualr2";
    public const string Database = "dango_backend_b0";
    public const string DatabaseRole = "dango_b0_owner";
    public const string ProtectedNativeHash = "6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A";
    public const string ReviewNativeHash = "BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507";

    public static void RequireApproval(bool approved)
    { if (!approved) throw new ManualReviewRejected("manual_review_explicit_approval_required"); }

    public static string ParseArguments(string[] args)
    {
        if (args.Length != 3 || args[0] != "--execute-approved-plan" || args[1] != "--postgres-bin")
            throw new ManualReviewRejected("manual_review_arguments_rejected");
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL", "18", "bin");
        if (!SamePath(args[2], expected)) throw new ManualReviewRejected("manual_review_binary_target_rejected");
        return expected;
    }

    public static ManualReviewTarget Load(bool approved, string sourceRoot, string binaryDirectory)
    {
        RequireApproval(approved);
        if (!OperatingSystem.IsWindows()) throw new ManualReviewRejected("manual_review_windows_required");
        var repository = Path.GetFullPath(sourceRoot);
        RequireRegularTree(repository);
        var stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango", "BackendDevelopment", ExistingRun);
        var dataDirectory = Path.Combine(stateDirectory, "pgdata");
        var manualDirectory = Path.Combine(Path.GetDirectoryName(stateDirectory)!, ManualRun);
        var candidate = Path.Combine(repository, "artifacts", "local-room", "backend-b5c-review-r1-20261010", "bin", "Dango.Desktop", "release");
        foreach (var path in new[] { stateDirectory, dataDirectory, manualDirectory, candidate, binaryDirectory }) RequireRegularTree(path);
        if (Directory.Exists(manualDirectory) || File.Exists(manualDirectory)) throw new ManualReviewRejected("manual_review_directory_already_exists");
        if (File.Exists(Path.Combine(dataDirectory, "postmaster.pid"))) throw new ManualReviewRejected("manual_review_existing_postmaster_rejected");
        foreach (var path in new[] { "state.json", "credential.dpapi", "b1test-state.json" })
            RequireFile(Path.Combine(stateDirectory, path), 65536);
        using var state = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(stateDirectory, "state.json")));
        var port = ValidateState(state.RootElement, dataDirectory);
        using var epoch = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(stateDirectory, "b1test-state.json")));
        if (epoch.RootElement.GetProperty("Version").GetInt32() != 1
            || !epoch.RootElement.GetProperty("RecoveryEpoch").TryGetGuid(out var recoveryEpoch) || recoveryEpoch == Guid.Empty)
            throw new ManualReviewRejected("manual_review_epoch_rejected");
        var keyDirectory = Path.Combine(stateDirectory, "b1tests-keys");
        RequireRegularTree(keyDirectory);
        if (!Directory.Exists(keyDirectory)) throw new ManualReviewRejected("manual_review_existing_keys_required");
        foreach (var executable in new[] { "postgres.exe", "pg_ctl.exe" }) RequireFile(Path.Combine(binaryDirectory, executable), long.MaxValue);
        RequireFile(Path.Combine(candidate, "Dango.Desktop.exe"), long.MaxValue);
        RequireHash(Path.Combine(repository, "src", "Dango.Desktop", "runtimes", "win-x64", "native", "dango_box2d.dll"), ProtectedNativeHash);
        RequireHash(Path.Combine(candidate, "dango_physics_module.dll"), ReviewNativeHash);
        return new(repository, stateDirectory, dataDirectory, manualDirectory, candidate, binaryDirectory, port, recoveryEpoch, keyDirectory);
    }

    public static int ValidateState(JsonElement state, string expectedDataDirectory)
    {
        try { return ValidateStateShape(state, expectedDataDirectory); }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        { throw new ManualReviewRejected("manual_review_state_rejected"); }
    }

    private static int ValidateStateShape(JsonElement state, string expectedDataDirectory)
    {
        if (state.ValueKind != JsonValueKind.Object || state.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != state.EnumerateObject().Count()
            || state.GetProperty("Version").GetInt32() != 1 || state.GetProperty("RunName").GetString() != ExistingRun
            || state.GetProperty("Database").GetString() != Database || state.GetProperty("Owner").GetString() != DatabaseRole
            || !SamePath(state.GetProperty("DataDirectory").GetString()!, expectedDataDirectory)
            || !state.GetProperty("Port").TryGetInt32(out var port) || port is < 1 or > 65535)
            throw new ManualReviewRejected("manual_review_state_rejected");
        return port;
    }

    public string ReadConnection(bool approved)
    {
        RequireApproval(approved);
        var encrypted = File.ReadAllBytes(Path.Combine(StateDirectory, "credential.dpapi"));
        var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1", Port = DatabasePort, Database = Database, Username = DatabaseRole,
                Password = Encoding.UTF8.GetString(plain), Timeout = 10, CommandTimeout = 15, Pooling = true,
                IncludeErrorDetail = false
            };
            return builder.ConnectionString;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public async Task ValidateDatabaseAsync(string connection, CancellationToken ct = default)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT current_database(),current_user,current_setting('server_version_num'),current_setting('data_directory'),current_setting('listen_addresses')", db);
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct) || reader.GetString(0) != Database || reader.GetString(1) != DatabaseRole
                || !int.TryParse(reader.GetString(2), out var version) || version is < 180000 or >= 190000
                || !SamePath(reader.GetString(3), DataDirectory) || reader.GetString(4) != "127.0.0.1")
                throw new ManualReviewRejected("manual_review_actual_database_rejected");
        }
        await using var context = CreateValidationContext(connection);
        if ((await context.Database.GetPendingMigrationsAsync(ct)).Any())
            throw new ManualReviewRejected("manual_review_migration_required");
    }

    public static DangoDbContext CreateValidationContext(string connection) => new(DangoDatabaseOptions.Create(connection));

    public void CreateOwnedDirectory(bool approved, Guid ownerId)
    {
        RequireApproval(approved);
        RequireRegularTree(ManualDirectory);
        if (ownerId == Guid.Empty || Directory.Exists(ManualDirectory) || File.Exists(ManualDirectory))
            throw new ManualReviewRejected("manual_review_fresh_directory_required");
        var directory = new DirectoryInfo(ManualDirectory);
        // DirectoryInfo.Create accepts an existing directory. Win32 creation is exclusive,
        // so a check/create race cannot give this run ownership of somebody else's folder.
        if (!CreateDirectoryW(ManualDirectory, IntPtr.Zero))
            throw new ManualReviewRejected("manual_review_fresh_directory_required");
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new ManualReviewRejected("manual_review_user_sid_required");
        var acl = new DirectorySecurity();
        acl.SetOwner(sid); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(acl);
        using var marker = new FileStream(Path.Combine(ManualDirectory, "owner-receipt.json"), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        JsonSerializer.Serialize(marker, new
        { Version = 1, RunName = ManualRun, OwnerId = ownerId, Directory = ManualDirectory });
        marker.Flush(true);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);

    public static void RequireRegularTree(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ManualReviewRejected("manual_review_reparse_target_rejected");
    }
    public static bool SamePath(string actual, string expected) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), StringComparison.OrdinalIgnoreCase);
    public static void RequireFile(string path, long maximumLength)
    {
        RequireRegularTree(path);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 1 || file.Length > maximumLength) throw new ManualReviewRejected("manual_review_file_rejected");
    }
    public static void RequireHash(string path, string hash)
    {
        RequireFile(path, long.MaxValue);
        using var stream = File.OpenRead(path);
        if (Convert.ToHexString(SHA256.HashData(stream)) != hash) throw new ManualReviewRejected("manual_review_candidate_hash_rejected");
    }
}
