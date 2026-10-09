using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Rooms.Access;

public sealed record RoomConnection(string Id, Guid UserId, Guid SessionId, Guid? LeaseId = null,
    Guid? InstanceId = null, Guid? RoomId = null);

public sealed class RoomConnectionRegistry(TimeProvider clock)
{
    public const int MaximumConnections = 128;
    public const int MaximumPerUser = 8;
    public const int MaximumCallsPerWindow = 30;
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = [];

    public bool Add(string id, Guid userId, Guid sessionId, Action? abort = null)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || userId == Guid.Empty || sessionId == Guid.Empty
                || entries.ContainsKey(id) || entries.Count >= MaximumConnections
                || entries.Values.Count(e => e.Connection.UserId == userId) >= MaximumPerUser) return false;
            entries.Add(id, new(new(id, userId, sessionId), clock.GetUtcNow(), abort));
            return true;
        }
    }

    public bool AllowCall(string id)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(id, out var entry)) return false;
            var now = clock.GetUtcNow();
            if (now >= entry.Window.AddSeconds(10)) { entry.Window = now; entry.Calls = 0; }
            return ++entry.Calls <= MaximumCallsPerWindow;
        }
    }

    public void Bind(string id, Guid? leaseId, Guid? instanceId, Guid? roomId)
    {
        lock (gate)
            if (entries.TryGetValue(id, out var entry))
                entry.Connection = entry.Connection with { LeaseId = leaseId, InstanceId = instanceId, RoomId = roomId };
    }

    public void Remove(string id) { lock (gate) entries.Remove(id); }
    public void UnbindLease(string id, Guid leaseId, Guid instanceId)
    {
        lock (gate)
            if (entries.TryGetValue(id, out var entry) && entry.Connection.LeaseId == leaseId && entry.Connection.InstanceId == instanceId)
                entry.Connection = entry.Connection with { LeaseId = null, InstanceId = null, RoomId = null };
    }
    public void RemoveIfCurrent(RoomConnection connection)
    {
        Action? abort = null;
        lock (gate)
            if (entries.TryGetValue(connection.Id, out var entry) && entry.Connection == connection)
            { entries.Remove(connection.Id); abort = entry.Abort; }
        abort?.Invoke();
    }
    public RoomConnection[] All() { lock (gate) return entries.Values.Select(e => e.Connection).ToArray(); }
    public bool IsCurrent(RoomConnection connection)
    { lock (gate) return entries.TryGetValue(connection.Id, out var entry) && entry.Connection == connection; }
    public RoomConnection[] Candidates(Guid roomId, IReadOnlyCollection<Guid> targets)
    {
        lock (gate) return entries.Values.Select(e => e.Connection)
            .Where(c => c.RoomId == roomId || targets.Contains(c.UserId)).OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
    }

    private sealed class Entry(RoomConnection connection, DateTimeOffset window, Action? abort)
    {
        public RoomConnection Connection { get; set; } = connection;
        public DateTimeOffset Window { get; set; } = window;
        public int Calls { get; set; }
        public Action? Abort { get; } = abort;
    }
}

public sealed class RoomConnectionAdmission(DangoDbContext db, AccountEnvironment environment, TimeProvider clock,
    RoomConnectionRegistry registry)
{
    public async Task PruneAsync(CancellationToken ct)
    {
        var connections = registry.All();
        var ids = connections.Select(c => c.SessionId).Distinct().ToArray();
        if (ids.Length == 0) return;
        var sessions = await (from session in db.AuthSessions.AsNoTracking()
                              join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                              where ids.Contains(session.Id)
                              select new { User = user, Session = session }).Take(RoomConnectionRegistry.MaximumConnections).ToArrayAsync(ct);
        var now = clock.GetUtcNow();
        var valid = sessions.Where(s => AccountSessionPolicy.IsValid(s.User, s.Session, environment.RecoveryEpoch, now))
            .Select(s => (s.User.Id, s.Session.Id)).ToHashSet();
        foreach (var connection in connections)
            if (!valid.Contains((connection.UserId, connection.SessionId))) registry.RemoveIfCurrent(connection);
    }
}
