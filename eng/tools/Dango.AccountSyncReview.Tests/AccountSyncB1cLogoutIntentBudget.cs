using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dango.AccountSyncReview.Tests;

internal sealed record AccountSyncB1cLogoutIntent(int Version, Guid OwnerId, int Sequence, string Case,
    int ClientId, Guid UserId, Guid PetId, Guid SessionId, string Intent, bool Forwarded, DateTimeOffset AtUtc);

// The runner reserves this ordinary empty file before any resource access. Never
// create, replace, truncate or restart it; validate and append through one open handle.
internal static class AccountSyncB1cLogoutIntentLedger
{
    private const int MaximumEntryBytes = 4096;
    private static readonly JsonSerializerOptions Json = new()
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 8
    };

    internal static void AppendExisting(string path, AccountSyncB1cLogoutIntent record)
    {
        try
        {
            Validate(record);
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw Reject();
            for (DirectoryInfo? part = new(Path.GetFullPath(path)); part is not null; part = part.Parent)
                if (part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0) throw Reject();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Reject();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(record, Json);
            if (bytes.Length is <= 0 or > MaximumEntryBytes) throw Reject();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read,
                4096, FileOptions.WriteThrough);
            if (record.Sequence == 1)
            {
                if (stream.Length != 0) throw Reject();
            }
            else
            {
                if (stream.Length is <= 1 or > MaximumEntryBytes + 1) throw Reject();
                var previousBytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(previousBytes);
                if (stream.ReadByte() != -1 || previousBytes[^1] != 10 || previousBytes.AsSpan(0, previousBytes.Length - 1).Contains((byte)10)) throw Reject();
                var previous = JsonSerializer.Deserialize<AccountSyncB1cLogoutIntent>(previousBytes.AsSpan(0, previousBytes.Length - 1), Json) ?? throw Reject();
                Validate(previous);
                if (previous.Sequence != 1 || previous.OwnerId != record.OwnerId || previous.UserId != record.UserId
                    || previous.PetId != record.PetId || previous.ClientId == record.ClientId || previous.SessionId == record.SessionId) throw Reject();
            }
            stream.Seek(0, SeekOrigin.End);
            stream.Write(bytes); stream.WriteByte(10); stream.Flush(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { throw Reject(); }
    }

    private static void Validate(AccountSyncB1cLogoutIntent record)
    {
        if (record.Version != 1 || record.Sequence is < 1 or > 2 || record.OwnerId == Guid.Empty || record.ClientId <= 0
            || record.UserId == Guid.Empty || record.PetId == Guid.Empty || record.SessionId == Guid.Empty
            || record.Case != (record.Sequence == 1 ? "late-get" : "late-patch") || record.Intent != "uia-logout"
            || record.Forwarded || record.AtUtc.Offset != TimeSpan.Zero) throw Reject();
    }
    private static InvalidOperationException Reject() => new("accountsync_b1c_logout_intent_ledger_rejected");
}

// Independent of the formal request budget: these UI intentions never crossed the server boundary.
internal sealed class AccountSyncB1cLogoutIntentBudget
{
    private readonly object gate = new();
    private readonly Guid ownerId;
    private readonly Action checkAlive, abortOwner;
    private readonly Action<AccountSyncB1cLogoutIntent> appendAndFlush;
    private readonly TimeProvider clock;
    private int used;
    private bool closed;

    internal AccountSyncB1cLogoutIntentBudget(Guid ownerId, Action checkAlive, Action abortOwner,
        Action<AccountSyncB1cLogoutIntent> appendAndFlush, TimeProvider? clock = null)
    {
        if (ownerId == Guid.Empty) throw Reject();
        this.ownerId = ownerId; this.checkAlive = checkAlive ?? throw new ArgumentNullException(nameof(checkAlive));
        this.abortOwner = abortOwner ?? throw new ArgumentNullException(nameof(abortOwner));
        this.appendAndFlush = appendAndFlush ?? throw new ArgumentNullException(nameof(appendAndFlush));
        this.clock = clock ?? TimeProvider.System;
    }

    internal int Used { get { lock (gate) return used; } }
    internal void Record(string caseName, int clientId, Guid userId, Guid petId, Guid sessionId)
    {
        lock (gate)
        {
            if (closed) throw Reject();
            try
            {
                checkAlive();
                if (used >= 2 || caseName != (used == 0 ? "late-get" : "late-patch")
                    || clientId <= 0 || userId == Guid.Empty || petId == Guid.Empty || sessionId == Guid.Empty) throw Reject();
                var sequence = ++used; // A failed append consumes the intention; it never refunds.
                appendAndFlush(new(1, ownerId, sequence, caseName, clientId, userId, petId, sessionId,
                    "uia-logout", false, clock.GetUtcNow()));
            }
            catch { closed = true; abortOwner(); throw Reject(); }
        }
    }
    private static InvalidOperationException Reject() => new("accountsync_b1c_logout_intent_rejected");
}
