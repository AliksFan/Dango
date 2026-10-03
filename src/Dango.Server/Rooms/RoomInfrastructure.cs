using System.Diagnostics;
using Dango.Protocol;

namespace Dango.Server.Rooms;

public interface IRoomClock { long Milliseconds { get; } }
public sealed class MonotonicRoomClock : IRoomClock
{
    private readonly Stopwatch watch = Stopwatch.StartNew();
    public long Milliseconds => watch.ElapsedMilliseconds;
}

public sealed record RoomOptions
{
    public int RoomQueueCapacity { get; init; } = 128;
    public int PerIdentityPendingCapacity { get; init; } = 64;
    public int OutboundCapacity { get; init; } = 256;
    public int ResultCapacity { get; init; } = 2048;
    public long ResultRetentionMs { get; init; } = 120_000;
    public double RatePerSecond { get; init; } = 10;
    public int RateBurst { get; init; } = 20;
    public long MaximumSequenceLag { get; init; } = 256;
    public const double Speed = .12;
    public const int MovementDurationMs = 800;
    public const int WaveDurationMs = 1200;

    public void Validate()
    {
        if (RoomQueueCapacity < 1 || PerIdentityPendingCapacity < 1 || OutboundCapacity < 1 ||
            ResultCapacity < 1 || ResultRetentionMs < 1 || !double.IsFinite(RatePerSecond) ||
            RatePerSecond <= 0 || RateBurst < 1 || MaximumSequenceLag < 0)
            throw new ArgumentOutOfRangeException(nameof(RoomOptions));
    }
}

public sealed class RoomRejectedException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed record RoomRecipient(string ConnectionId, long Generation);
public interface IRoomEventSink
{
    Task SendAsync(RoomEvent roomEvent, IReadOnlyList<RoomRecipient> recipients, CancellationToken cancellationToken);
    Task SupersedeAsync(RoomRecipient oldRecipient, CancellationToken cancellationToken);
}

public sealed class NullRoomEventSink : IRoomEventSink
{
    public Task SendAsync(RoomEvent roomEvent, IReadOnlyList<RoomRecipient> recipients, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SupersedeAsync(RoomRecipient oldRecipient, CancellationToken cancellationToken) => Task.CompletedTask;
}
