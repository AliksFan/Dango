using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dango.Server.Accounts;
using Dango.Server.Hosting;

namespace Dango.BackendManualReview;

public sealed record ManualReviewIdentity(Guid UserId, Guid PetId, Guid DefaultRoomId);
public enum ManualReviewOperation { RegistrationStart, RegistrationComplete, Login, RoomCreate, Enter, CodeReset, Configuration, Other }

// A failed response or disconnected request cannot prove that no write committed. Reservations
// therefore remain consumed for this short-lived, approved manual host's entire lifetime.
public sealed class ManualReviewBudget
{
    private readonly object sync = new();
    private readonly byte[] hashKey = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<string, Reservation> reservations = new(StringComparer.Ordinal);
    private readonly Dictionary<ManualReviewOperation, int> counts = new();
    private readonly HashSet<Guid> rooms = [];
    private readonly HashSet<Guid> sessions = [];
    private readonly Dictionary<Guid, Lease> leases = [];
    private readonly Dictionary<string, Guid> codes = new(StringComparer.Ordinal);
    private readonly Func<CancellationToken, Task<ManualReviewIdentity>>? resolveIdentity;
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private ManualReviewIdentity? identity;
    private Guid? pendingRegistrationId;
    private string? confirmationHash;

    public ManualReviewBudget(string email, Guid invitationId,
        Func<CancellationToken, Task<ManualReviewIdentity>>? resolveIdentity = null)
    {
        var validated = AccountInput.Email(email);
        if (!validated.Email.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) || invitationId == Guid.Empty)
            throw new InvalidOperationException("manual_review_target_invalid");
        Email = validated.Email;
        InvitationId = invitationId;
        this.resolveIdentity = resolveIdentity;
    }

    public string Email { get; }
    public Guid InvitationId { get; }
    public Guid? PendingRegistrationId { get { lock (sync) return pendingRegistrationId; } }
    public ManualReviewIdentity? Identity { get { lock (sync) return identity; } }

    public void BindIdentity(ManualReviewIdentity value)
    {
        if (value.UserId == Guid.Empty || value.PetId == Guid.Empty || value.DefaultRoomId == Guid.Empty) throw Reject();
        lock (sync)
        {
            if (identity is not null && identity != value) throw Reject();
            identity = value;
            rooms.Add(value.DefaultRoomId);
        }
    }

    public void AllowConfirmationProof(string proof)
    {
        if (string.IsNullOrWhiteSpace(proof) || proof.Length > 4096) throw Reject();
        var digest = HashText(proof);
        lock (sync)
        {
            if (pendingRegistrationId is null || confirmationHash is not null && confirmationHash != digest) throw Reject();
            confirmationHash = digest;
        }
    }

    public void Reserve(ManualReviewOperation operation, string replayKey, ReadOnlySpan<byte> payload)
    {
        if (!Enum.IsDefined(operation) || string.IsNullOrEmpty(replayKey) || replayKey.Length > 256) throw Reject();
        var digest = Hash(payload);
        lock (sync)
        {
            if (reservations.TryGetValue(replayKey, out var prior))
            {
                if (prior.Operation != operation || prior.Hash != digest) throw new InvalidOperationException("manual_review_request_conflict");
                return;
            }
            var limit = operation switch
            {
                ManualReviewOperation.RegistrationStart or ManualReviewOperation.RegistrationComplete => 1,
                ManualReviewOperation.Login => 3,
                ManualReviewOperation.RoomCreate or ManualReviewOperation.CodeReset or ManualReviewOperation.Configuration => 2,
                ManualReviewOperation.Enter => 4,
                ManualReviewOperation.Other => 64,
                _ => 0
            };
            var used = counts.GetValueOrDefault(operation);
            if (used >= limit) throw new InvalidOperationException("manual_review_budget_exhausted");
            reservations.Add(replayKey, new(operation, digest));
            counts[operation] = used + 1;
        }
    }

    public IReadOnlyDictionary<ManualReviewOperation, int> Snapshot()
    {
        lock (sync) return Enum.GetValues<ManualReviewOperation>().ToDictionary(operation => operation, operation => counts.GetValueOrDefault(operation));
    }
    internal async Task<IDisposable> LockMutationAsync(CancellationToken ct)
    {
        await mutationGate.WaitAsync(ct);
        return new GateRelease(mutationGate);
    }
    internal bool IsEmail(string? email) => string.Equals(email, Email, StringComparison.OrdinalIgnoreCase);
    internal bool IsRoom(Guid roomId) { lock (sync) return rooms.Contains(roomId); }
    internal bool IsRegistration(Guid id) { lock (sync) return pendingRegistrationId == id; }
    internal bool IsLease(Guid leaseId, Guid instanceId, Guid? roomId = null)
    {
        lock (sync) return leases.TryGetValue(leaseId, out var lease) && lease.InstanceId == instanceId
            && (roomId is null || lease.RoomId == roomId);
    }
    internal bool IsCode(string? code)
    {
        if (code is null || code.Length > 4096) return false;
        lock (sync) return codes.ContainsKey(HashText(code));
    }
    internal bool IsConfirmation(string? proof)
    {
        if (proof is null || proof.Length > 4096) return false;
        lock (sync) return pendingRegistrationId is not null && confirmationHash is not null
            && confirmationHash == HashText(proof);
    }
    internal bool Owns(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal.FindFirstValue(FormalHost.SessionClaim), out var sessionId)) return false;
        lock (sync) return identity?.UserId == userId && sessions.Contains(sessionId);
    }
    internal void CaptureRegistration(Guid id)
    {
        if (id == Guid.Empty) throw Reject();
        lock (sync)
        {
            if (pendingRegistrationId is not null && pendingRegistrationId != id) throw Reject();
            pendingRegistrationId = id;
        }
    }
    internal async Task CaptureIdentityAsync(ManualReviewIdentity value, CancellationToken ct)
    {
        if (resolveIdentity is not null && await resolveIdentity(ct) != value) throw Reject();
        BindIdentity(value);
    }
    internal void CaptureSession(Guid userId, Guid sessionId)
    {
        lock (sync)
        {
            if (identity?.UserId != userId || sessionId == Guid.Empty || !sessions.Contains(sessionId) && sessions.Count >= 3) throw Reject();
            sessions.Add(sessionId);
        }
    }
    internal void CaptureRoom(Guid roomId)
    {
        lock (sync)
        {
            if (identity is null || roomId == Guid.Empty || !rooms.Contains(roomId) && rooms.Count >= 3) throw Reject();
            rooms.Add(roomId);
        }
    }
    internal void CaptureLease(Guid petId, Guid roomId, Guid leaseId, Guid instanceId)
    {
        lock (sync)
        {
            if (identity?.PetId != petId || !rooms.Contains(roomId) || leaseId == Guid.Empty || instanceId == Guid.Empty) throw Reject();
            var value = new Lease(roomId, instanceId);
            if (leases.TryGetValue(leaseId, out var prior) && prior != value || !leases.ContainsKey(leaseId) && leases.Count >= 4) throw Reject();
            leases[leaseId] = value;
        }
    }
    internal void CaptureCode(Guid roomId, string code)
    {
        lock (sync)
        {
            if (!rooms.Contains(roomId) || string.IsNullOrEmpty(code) || code.Length > 4096) throw Reject();
            var digest = HashText(code);
            if (codes.TryGetValue(digest, out var prior) && prior != roomId || !codes.ContainsKey(digest) && codes.Count >= 2) throw Reject();
            codes[digest] = roomId;
        }
    }

    private string Hash(ReadOnlySpan<byte> data) => Convert.ToHexString(HMACSHA256.HashData(hashKey, data));
    private string HashText(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try { return Hash(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    internal static InvalidOperationException Reject() => new("manual_review_scope_rejected");
    private sealed record Reservation(ManualReviewOperation Operation, string Hash);
    private sealed record Lease(Guid RoomId, Guid InstanceId);
    private sealed class GateRelease(SemaphoreSlim gate) : IDisposable
    {
        private int released;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) gate.Release(); }
    }
}
