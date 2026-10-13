using System.Reflection;
using System.Runtime.InteropServices;
using Dango.Physics;

namespace Dango.MaterialSample;

// Test/review-only reader for dango_physics_world_get_rest_state. The world handle is not
// public API, so it is read by reflection from the same loaded module.
internal sealed class NativeRestReader
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RestStateDelegate(nint handle, int[] entityIds, int[] resting, int capacity, out int count);

    private readonly RestStateDelegate read;
    private readonly DangoPhysicsWorld world;

    private NativeRestReader(RestStateDelegate read, DangoPhysicsWorld world) { this.read = read; this.world = world; }

    // Null when the module was built without DANGO_REST_SLEEP.
    internal static NativeRestReader? TryCreate(string library, DangoPhysicsWorld world) =>
        NativeLibrary.TryGetExport(NativeLibrary.Load(library), "dango_physics_world_get_rest_state", out var address)
            ? new(Marshal.GetDelegateForFunctionPointer<RestStateDelegate>(address), world) : null;

    internal bool[] Read(IReadOnlyList<DangoEntityId> ids)
    {
        var handle = (nint)typeof(DangoPhysicsWorld).GetField("world", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
        int[] entities = new int[world.Capacity], resting = new int[world.Capacity];
        var error = read(handle, entities, resting, world.Capacity, out var count);
        if (error != 0) throw new InvalidOperationException($"rest state error {error}");
        return [.. ids.Select(id => resting[Array.IndexOf(entities, id.Value, 0, count)] != 0)];
    }
}
