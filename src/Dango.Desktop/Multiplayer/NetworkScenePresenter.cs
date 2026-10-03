using Dango.Core.Coordinates;
using Dango.Protocol;

namespace Dango.Desktop.Multiplayer;

/// <summary>Replica snapshots enter here; only immutable drawing frames leave.</summary>
public sealed class NetworkScenePresenter(string nativeLibraryPath) : IDisposable
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, NetworkPetPresentation> pets = new(StringComparer.Ordinal);
    private string? epoch;
    private bool paused;
    private bool disposed;
    public IReadOnlyList<NetworkPetFrame> Sample(RoomSnapshot snapshot, long serverTimeMs,
        LogicalRect viewport, TimeSpan frameElapsed, bool reducedMotion = false)
    {
        VerifyAccess();
        if (paused) throw new InvalidOperationException("Network scene is paused.");
        if (!RoomProtocol.IsSnapshot(snapshot)) throw new ArgumentException("Invalid snapshot.", nameof(snapshot));
        if (epoch != snapshot.RoomEpoch) { Clear(); epoch = snapshot.RoomEpoch; }
        foreach (var removed in pets.Keys.Except(snapshot.Pets.Select(p => p.PetId)).ToArray())
        { pets[removed].Dispose(); pets.Remove(removed); }
        var frames = new List<NetworkPetFrame>(2);
        foreach (var pet in snapshot.Pets.OrderBy(p => p.PetId, StringComparer.Ordinal))
        {
            if (!pets.TryGetValue(pet.PetId, out var presentation))
                pets.Add(pet.PetId, presentation = new(pet.PetId, nativeLibraryPath));
            frames.Add(presentation.Sample(pet, serverTimeMs, viewport, frameElapsed, reducedMotion));
        }
        return frames.AsReadOnly();
    }
    public void SetPaused(bool value)
    {
        VerifyAccess(); paused = value;
        foreach (var pet in pets.Values) pet.SetPaused(value);
    }
    private void Clear() { foreach (var pet in pets.Values) pet.Dispose(); pets.Clear(); }
    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Network scene is UI-thread owned.");
    }
    public void Dispose() { if (disposed) return; VerifyAccess(); Clear(); disposed = true; }
}
