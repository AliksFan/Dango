using System.Security.Cryptography;
using Dango.Protocol;
using Dango.Server.Rooms;

namespace Dango.Server.Sessions;

internal sealed class RoomSession(int index, RoomOptions options)
{
    public string ParticipantId { get; } = $"fictional-participant-{index + 1}";
    public string PetId { get; } = $"fictional-pet-{index + 1}";
    public string JoinTicket { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string ResumeToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string? ClientInstanceId { get; set; }
    public string? ConnectionId { get; set; }
    public long Generation { get; set; }
    public long NextOrdinal { get; set; } = 1;
    public double Tokens { get; set; } = options.RateBurst;
    public long TokenTime { get; set; }
    public SemaphoreSlim JoinGate { get; } = new(1, 1);
    public SemaphoreSlim PendingSlots { get; } = new(options.PerIdentityPendingCapacity, options.PerIdentityPendingCapacity);
    public Dictionary<string, CachedCommand> Results { get; } = new(StringComparer.Ordinal);
}

internal sealed record CachedCommand(string Fingerprint, long CreatedAt, CommandResult Result);
