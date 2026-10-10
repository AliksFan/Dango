using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Server.Accounts;
using Dango.Server.Configuration;
using Dango.Server.Email;
using Dango.Server.Hosting;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Access;
using Dango.Server.Rooms.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Dango.BackendManualReview;

// This process owns the entire manual-host lifetime. It cannot restart a host and reset its
// in-memory write budget. Persisted receipts contain identifiers and counts, never secrets.
public sealed class ManualReviewRuntime
{
    private readonly ManualReviewTarget target;
    private readonly ManualReviewPostgres postgres;
    private readonly ManualReviewTls tls = new();
    private readonly ManualReviewCredentials credentials = new();
    private readonly ManualReviewDesktopProcess desktop = new();
    private readonly Guid ownerId = Guid.NewGuid();
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object receiptGate = new();
    private ManualReviewTlsMaterial? material;
    private OwnedCredentialSlots? slots;
    private WebApplication? host;
    private Task? hostDisposal;
    private string? connectionString;
    private ManualReviewDatabaseEvidence? databaseEvidence;
    private string? databaseEvidenceFailure;
    private bool directoryOwned;
    private bool ready;
    private bool stopped;
    private bool confirmationDone;
    private string? confirmationProof;
    private bool credentialsCleared;
    private bool certificateDisposed;
    private string startupPhase = "not_started";
    private string? startupFailure;

    public ManualReviewRuntime(ManualReviewTarget target)
    { this.target = target; postgres = new(target); }

    public string Email { get; private set; } = string.Empty;
    public Guid InvitationId { get; private set; }
    public string InvitationProof { get; private set; } = string.Empty;
    public Uri? Origin => slots?.Origin;
    public ManualReviewBudget? Budget { get; private set; }
    public bool Ready => ready && !stopped;
    public string? EvidenceFailure => databaseEvidenceFailure;

    public async Task StartAsync()
    {
        await operations.WaitAsync();
        try
        {
            if (directoryOwned || stopped) throw new ManualReviewRejected("manual_review_restart_rejected");
            startupPhase = "postgres_preflight";
            await postgres.CheckStoppedAsync(true);
            // Reject another review-mode Desktop before creating any resources.
            desktop.AssertNoReviewModeProcess();
            target.CreateOwnedDirectory(true, ownerId); directoryOwned = true;
            startupPhase = "tls_prepare";
            material = tls.Prepare(true, new(target.ManualDirectory, ownerId, true, true), DateTimeOffset.UtcNow);
            startupPhase = "tls_trust";
            WriteReceipt("tls_prepared");
            tls.Trust(true, material);
            startupPhase = "origin_reserve";
            for (var attempt = 0; attempt < 16 && slots is null; attempt++)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                if (port == target.DatabasePort) continue;
                try { slots = credentials.ReserveUnusedOrigin(true, new Uri($"https://127.0.0.1:{port}/")); }
                catch (InvalidOperationException ex) when (ex.Message == "manual_credential_slot_already_exists") { }
            }
            if (slots is null) throw new ManualReviewRejected("manual_review_unused_origin_unavailable");
            WriteReceipt("preparing");
            startupPhase = "postgres_start";
            await postgres.StartAsync(true);
            WriteReceipt("postgres_started");
            var connection = target.ReadConnection(true);
            connectionString = connection;
            startupPhase = "database_target_and_history";
            await target.ValidateDatabaseAsync(connection);
            if (!FormalServerOptions.TryCreate(ManualReviewTarget.Database, connection, target.KeyDirectory,
                slots.Origin.AbsoluteUri, target.SourceRoot, out var options, out _))
                throw new ManualReviewRejected("manual_review_formal_configuration_rejected");
            Email = $"b5c-manual-{Guid.NewGuid():N}@review.invalid";
            var randomProof = RandomNumberGenerator.GetBytes(32);
            try { InvitationProof = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(randomProof); }
            finally { CryptographicOperations.ZeroMemory(randomProof); }
            startupPhase = "formal_host_build";
            host = FormalHost.Create(options!, new(target.RecoveryEpoch), material.Certificate,
                services =>
                {
                    services.AddSingleton(_ => Budget ?? throw new ManualReviewRejected("manual_review_budget_unbound"));
                    services.AddSingleton<ManualReviewHubFilter>();
                    services.AddSignalR().AddHubOptions<FormalRoomHub>(hub => hub.AddFilter<ManualReviewHubFilter>());
                }, enableEmailWorker: false, enableRoomExpiryWorker: false, enableRoomLiveWorkers: false,
                configureRequestBoundary: app => app.UseMiddleware<ManualReviewHttpGuard>());
            if (host.Services.GetServices<IHostedService>().Any(worker => worker is EmailWorker or RoomExpiryWorker or RoomLiveWorker or RoomOutboxWorker))
                throw new ManualReviewRejected("manual_review_background_worker_rejected");
            startupPhase = "invitation_issue";
            using (var scope = host.Services.CreateScope())
                InvitationId = await scope.ServiceProvider.GetRequiredService<InvitationService>()
                    .IssueAsync(Email, InvitationProof, DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None);
            Budget = new(Email, InvitationId, ResolveIdentityAsync);
            WriteReceipt("invitation_issued");
            startupPhase = "https_start";
            await host.StartAsync();
            ready = true;
            startupPhase = "ready";
            WriteReceipt("ready");
        }
        catch (Exception error)
        {
            startupFailure = Program.SafeError(error);
            try { WriteReceipt("start_failed"); } catch { }
            throw;
        }
        finally { operations.Release(); }
    }

    public async Task<int> LaunchDesktopAsync()
    {
        await operations.WaitAsync();
        try
        {
            RequireReady();
            foreach (var previous in desktop.SnapshotOwnedProcesses())
                if (!await desktop.WaitForOwnedExitAsync(true, previous, TimeSpan.FromSeconds(1)))
                    throw new ManualReviewRejected("manual_review_exit_previous_desktop_first");
            var executable = Path.Combine(target.CandidateRoot, "Dango.Desktop.exe");
            var native = Path.Combine(target.CandidateRoot, "dango_physics_module.dll");
            ManualReviewTarget.RequireHash(native, ManualReviewTarget.ReviewNativeHash);
            var process = desktop.Launch(true, new(target.SourceRoot, target.CandidateRoot, executable,
                HashFile(executable), native, ManualReviewTarget.ReviewNativeHash), slots!.Origin);
            WriteReceipt("ready");
            return process.Receipt.ProcessId;
        }
        finally { operations.Release(); }
    }

    private async Task<ManualReviewIdentity> ResolveIdentityAsync(CancellationToken ct)
    {
        var budget = Budget ?? throw new ManualReviewRejected("manual_review_budget_unbound");
        var id = budget.PendingRegistrationId ?? throw new ManualReviewRejected("manual_review_pending_registration_required");
        using var scope = host!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DangoDbContext>();
        var pending = await db.PendingRegistrations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, ct);
        if (pending is null || pending.InvitationId != InvitationId || !string.Equals(pending.Email, Email, StringComparison.OrdinalIgnoreCase)
            || pending.NormalizedEmail != Email.ToUpperInvariant()
            || pending.RecoveryEpoch != target.RecoveryEpoch || pending.State != RegistrationState.Completed
            || pending.UserId is not { } user || pending.PetId is not { } pet || pending.RoomId is not { } room)
            throw new ManualReviewRejected("manual_review_completed_identity_rejected");
        if (!await db.Pets.AsNoTracking().AnyAsync(row => row.Id == pet && row.UserId == user, ct)
            || !await db.Rooms.AsNoTracking().AnyAsync(row => row.Id == room && row.OwnerId == user && row.Kind == PersistentRoomKind.DesktopPet, ct))
            throw new ManualReviewRejected("manual_review_completed_identity_rejected");
        return new(user, pet, room);
    }

    public async Task ConfirmThisEmailAsync()
    {
        await operations.WaitAsync();
        try
        {
            RequireReady();
            if (confirmationDone) return;
            var id = Budget!.PendingRegistrationId ?? throw new ManualReviewRejected("manual_review_start_registration_first");
            using (var scope = host!.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DangoDbContext>();
                var pending = await db.PendingRegistrations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id);
                ValidatePendingSubject(pending, id, Email, InvitationId, target.RecoveryEpoch);
                // A lost HTTP response is unknown. Reconcile this exact subject before retrying;
                // never produce a new random proof or a new registration for that case.
                if (confirmationProof is not null && pending!.State is RegistrationState.Verified or RegistrationState.Completed)
                { confirmationDone = true; WriteReceipt("ready_email_confirmed"); return; }
                var challenges = await db.EmailChallenges.AsNoTracking().Where(row => row.PendingRegistrationId == id
                    && row.Purpose == EmailChallengePurpose.Registration && row.ConsumedAt == null && row.RevokedAt == null)
                    .Take(2).ToListAsync();
                if (pending is null || challenges.Count != 1) throw new ManualReviewRejected("manual_review_confirmation_subject_rejected");
                ValidateConfirmation(pending, challenges[0], id, Email, InvitationId, target.RecoveryEpoch, DateTimeOffset.UtcNow);
                confirmationProof ??= scope.ServiceProvider.GetRequiredService<IEmailProofProtector>().Protect(challenges[0]);
            }
            Budget.AllowConfirmationProof(confirmationProof);
            // Use the same normal system trust, Origin and fresh antiforgery token as a real client.
            using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = slots!.Origin, Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("Origin", slots.Origin.GetLeftPart(UriPartial.Authority));
            using var protection = await client.GetAsync("api/v1/auth/request-protection");
            if (!protection.IsSuccessStatusCode) throw new ManualReviewRejected("manual_review_confirmation_protection_failed");
            using var json = JsonDocument.Parse(await protection.Content.ReadAsByteArrayAsync());
            if (!json.RootElement.TryGetProperty("requestToken", out var token) || token.ValueKind != JsonValueKind.String)
                throw new ManualReviewRejected("manual_review_confirmation_protection_failed");
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/email-verifications/confirm")
            { Content = JsonContent.Create(new EmailProofRequest(confirmationProof)) };
            request.Headers.Add("X-CSRF-TOKEN", token.GetString());
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) throw new ManualReviewRejected("manual_review_confirmation_http_failed");
            confirmationDone = true;
            WriteReceipt("ready_email_confirmed");
        }
        finally { operations.Release(); }
    }

    public static void ValidateConfirmation(PendingRegistration pending, EmailChallenge challenge, Guid pendingId,
        string email, Guid invitationId, Guid epoch, DateTimeOffset now)
    {
        ValidatePendingSubject(pending, pendingId, email, invitationId, epoch);
        if (pending.State != RegistrationState.Pending || pending.ExpiresAt <= now
            || challenge.Id == Guid.Empty || challenge.PendingRegistrationId != pendingId || challenge.UserId is not null
            || challenge.Purpose != EmailChallengePurpose.Registration || challenge.NormalizedEmail != pending.NormalizedEmail
            || challenge.RecoveryEpoch != epoch || challenge.Generation != pending.Generation || challenge.Generation <= 0
            || challenge.SecurityStamp is not null || challenge.CreatedAt > now || challenge.ExpiresAt <= now
            || challenge.ExpiresAt > pending.ExpiresAt || challenge.ConsumedAt is not null || challenge.RevokedAt is not null)
            throw new ManualReviewRejected("manual_review_confirmation_subject_rejected");
    }

    private static void ValidatePendingSubject(PendingRegistration? pending, Guid pendingId, string email, Guid invitationId, Guid epoch)
    {
        if (pending is null || pendingId == Guid.Empty || invitationId == Guid.Empty || epoch == Guid.Empty || pending.Id != pendingId
            || pending.InvitationId != invitationId || !string.Equals(pending.Email, email, StringComparison.OrdinalIgnoreCase) || pending.NormalizedEmail != email.ToUpperInvariant()
            || pending.RecoveryEpoch != epoch)
            throw new ManualReviewRejected("manual_review_confirmation_subject_rejected");
    }

    // A live Desktop prevents teardown: ask the user to use its tray Exit, then try again.
    // Cleanup steps are independent; failure in one does not skip other owned resources.
    public async Task<IReadOnlyList<string>> TryFinishAsync()
    {
        await operations.WaitAsync();
        try
        {
            if (stopped) return [];
            var problems = new List<string>();
            foreach (var process in desktop.SnapshotOwnedProcesses())
            {
                try { if (!await desktop.WaitForOwnedExitAsync(true, process, TimeSpan.FromSeconds(2))) problems.Add("manual_review_desktop_still_running"); }
                catch { problems.Add("manual_review_desktop_identity_unproven"); }
            }
            if (problems.Count != 0) { WriteReceipt("desktop_exit_pending", problems); return problems; }
            ready = false;
            if (host is not null)
            {
                if (hostDisposal is null)
                {
                    try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await host.StopAsync(timeout.Token).WaitAsync(timeout.Token); }
                    catch { problems.Add("manual_review_host_stop_unconfirmed"); }
                }
                try { hostDisposal ??= host.DisposeAsync().AsTask(); await hostDisposal.WaitAsync(TimeSpan.FromSeconds(15)); host = null; }
                catch { problems.Add("manual_review_host_dispose_unconfirmed"); }
            }
            if (host is null && connectionString is not null && InvitationId != Guid.Empty && postgres.StartedPid != 0
                && ManualReviewPostgres.ProcessPresent(postgres.StartedPid))
            {
                try { databaseEvidence = await ReadScopedDatabaseEvidenceAsync(); databaseEvidenceFailure = null; }
                catch { databaseEvidenceFailure = "manual_review_scoped_database_evidence_unconfirmed"; }
            }
            // The native server key no longer has a consumer once this host is disposed.
            // Remove it independently of PostgreSQL, credential and evidence outcomes.
            if (host is null && material is not null && !certificateDisposed)
            {
                try { material.Dispose(); certificateDisposed = true; }
                catch { problems.Add("manual_review_server_key_dispose_unconfirmed"); }
            }
            NpgsqlConnection.ClearAllPools();
            try { if (!await postgres.StopOnlyOwnedAsync(true)) problems.Add("manual_review_postgres_stop_unconfirmed"); }
            catch { problems.Add("manual_review_postgres_stop_unconfirmed"); }
            if (slots is not null && !credentialsCleared)
            {
                try { credentials.ClearOnlyOwned(true, slots); credentialsCleared = true; }
                catch { problems.Add("manual_review_credential_cleanup_unconfirmed"); }
            }
            if (material is not null && tls.CanRemoveOwnedTrust(material.Receipt))
            {
                try { tls.RemoveOwnedTrust(true, material.Receipt); }
                catch { problems.Add("manual_review_tls_cleanup_unconfirmed"); }
            }
            if (postgres.ToolProcesses.Any(process => !process.HasExited())) problems.Add("manual_review_tool_subprocess_still_running");
            if (Origin is { } origin && ManualReviewPostgres.ListenerPresent(origin.Port)) problems.Add("manual_review_https_listener_still_present");
            stopped = problems.Count == 0;
            WriteReceipt(stopped ? "stopped" : "cleanup_incomplete", problems);
            if (stopped) { InvitationProof = string.Empty; confirmationProof = null; connectionString = null; }
            return problems;
        }
        finally { operations.Release(); }
    }

    public void RefreshReceipt() { if (directoryOwned && Ready) WriteReceipt("ready"); }
    private void RequireReady() { if (!Ready) throw new ManualReviewRejected("manual_review_not_ready"); }
    private static string HashFile(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private void WriteReceipt(string status, IReadOnlyList<string>? problems = null)
    {
        if (!directoryOwned) return;
        lock (receiptGate) WriteReceiptCore(status, problems);
    }
    private void WriteReceiptCore(string status, IReadOnlyList<string>? problems)
    {
        ManualReviewTarget.RequireRegularTree(target.ManualDirectory);
        ManualReviewTarget.RequireRegularTree(Path.Combine(target.ManualDirectory, "runtime-receipt.json"));
        var receipt = new
        {
            Version = 1, RunName = ManualReviewTarget.ManualRun, OwnerId = ownerId, Status = status, UpdatedAt = DateTimeOffset.UtcNow,
            StartupPhase = startupPhase, StartupFailure = startupFailure,
            HostPid = Environment.ProcessId, Origin = slots?.Origin.AbsoluteUri, InvitationId,
            PendingRegistrationId = Budget?.PendingRegistrationId, Identity = Budget?.Identity,
            Counts = Budget?.Snapshot().ToDictionary(entry => entry.Key.ToString(), entry => entry.Value),
            PostgresPid = postgres.StartedPid, PostgresStartedAt = postgres.StartedAt, PostgresPort = target.DatabasePort,
            Certificate = material?.Receipt, CredentialSlots = slots is null ? null : new { slots.SessionTarget, slots.RegistrationTarget },
            DesktopProcesses = desktop.SnapshotOwnedProcesses().Select(process => process.Receipt).ToArray(),
            ToolProcesses = postgres.ToolProcesses, Problems = problems ?? [], DatabaseEvidence = databaseEvidence,
            EvidenceFailure = databaseEvidenceFailure, ResourceCleanupComplete = stopped, ServerKeyDisposed = certificateDisposed
        };
        var temporary = Path.Combine(target.ManualDirectory, $"runtime-receipt-{Guid.NewGuid():N}.tmp");
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(output, receipt); output.Flush(true); }
        File.Move(temporary, Path.Combine(target.ManualDirectory, "runtime-receipt.json"), overwrite: true);
    }

    private async Task<ManualReviewDatabaseEvidence> ReadScopedDatabaseEvidenceAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        await using var db = new DangoDbContext(DangoDatabaseOptions.Create(connectionString!));
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var invitations = await db.TestInvitations.AsNoTracking().Where(row => row.Id == InvitationId
            && row.NormalizedEmail == Email.ToUpperInvariant() && row.RecoveryEpoch == target.RecoveryEpoch).CountAsync(ct);
        var pendingRows = await db.PendingRegistrations.AsNoTracking().Where(row => row.InvitationId == InvitationId
            && row.NormalizedEmail == Email.ToUpperInvariant() && row.RecoveryEpoch == target.RecoveryEpoch).Take(2).ToListAsync(ct);
        if (invitations != 1 || pendingRows.Count > 1) throw new ManualReviewRejected("manual_review_scoped_database_bounds_rejected");
        var pendingId = pendingRows.SingleOrDefault()?.Id;
        var challenges = await db.EmailChallenges.AsNoTracking().Where(row => pendingId != null && row.PendingRegistrationId == pendingId)
            .Select(row => row.Id).ToListAsync(ct);
        var jobs = await db.EmailJobs.AsNoTracking().Where(row => challenges.Contains(row.ChallengeId))
            .Select(row => new { row.Attempts, row.State }).ToListAsync(ct);
        if (challenges.Count > 1 || jobs.Count != challenges.Count || jobs.Any(row => row.Attempts != 0 || row.State != EmailJobState.Pending))
            throw new ManualReviewRejected("manual_review_scoped_email_bounds_rejected");
        var pending = pendingRows.SingleOrDefault();
        var users = 0; var pets = 0; var rooms = 0; var sessions = 0; var leases = 0; var codes = 0; var audit = 0; var events = 0;
        if (pending?.State == RegistrationState.Completed && pending.UserId is { } user && pending.PetId is { } pet)
        {
            users = await db.Users.AsNoTracking().Where(row => row.Id == user && row.NormalizedEmail == Email.ToUpperInvariant()).CountAsync(ct);
            pets = await db.Pets.AsNoTracking().Where(row => row.Id == pet && row.UserId == user).CountAsync(ct);
            var roomIds = await db.Rooms.AsNoTracking().Where(row => row.OwnerId == user).Select(row => row.Id).ToListAsync(ct);
            rooms = roomIds.Count;
            sessions = await db.AuthSessions.AsNoTracking().Where(row => row.UserId == user).CountAsync(ct);
            leases = await db.RoomDeviceLeases.AsNoTracking().Where(row => row.UserId == user && row.PetId == pet).CountAsync(ct);
            codes = await db.PublicRoomCredentials.AsNoTracking().Where(row => roomIds.Contains(row.RoomId)).CountAsync(ct);
            audit = await db.RoomOperationAudits.AsNoTracking().Where(row => roomIds.Contains(row.RoomId)).CountAsync(ct);
            events = await db.RoomDomainEvents.AsNoTracking().Where(row => roomIds.Contains(row.RoomId)).CountAsync(ct);
            if (await db.RoomDomainEvents.AsNoTracking().AnyAsync(row => roomIds.Contains(row.RoomId) && row.PublishedAt != null, ct))
                throw new ManualReviewRejected("manual_review_unexpected_event_dispatch");
            if (users != 1 || pets != 1 || rooms is < 1 or > 3 || sessions > 3 || leases > 4 || codes > 2)
                throw new ManualReviewRejected("manual_review_scoped_database_bounds_rejected");
        }
        await transaction.CommitAsync(ct);
        return new(invitations, pendingRows.Count, challenges.Count, jobs.Count, 0, users, pets, rooms, sessions, leases, codes, audit, events);
    }
}

public sealed record ManualReviewDatabaseEvidence(int Invitations, int Registrations, int Challenges, int EmailJobs,
    int EmailAttempts, int Users, int Pets, int Rooms, int Sessions, int HistoricalLeases, int Codes, int Audit, int Events);
