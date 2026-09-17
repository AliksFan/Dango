using Dango.Physics;

if (args.Length is < 1 or > 2 || (args.Length == 2 && (!int.TryParse(args[1], out var value) || value is < 1 or > 3)))
{
    Console.Error.WriteLine("usage: Dango.Physics.Sample <dango_physics_module.dll> [1|2|3; default=3]");
    return 2;
}

try
{
    var capacity = args.Length == 2 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 3;
    using var world = DangoPhysicsWorld.Create(args[0], capacity);
    var ids = Enumerable.Range(0, capacity).Select(i => world.Add((i - 1) * 2.8, 0)).ToList();
    DangoPhysicsFrame Step()
    {
        var frame = world.Step(TimeSpan.FromSeconds(1d / 60), ids.Select(id => new DangoPhysicsInput(id)).ToArray());
        if (!frame.Succeeded) throw new InvalidOperationException($"Physics stopped: {frame.ErrorCode}; last safe frame retained. Explicit reset or exit required.");
        return frame;
    }
    for (int i = 0; i < 120; i++) Step();
    var removed = ids[^1]; world.Remove(removed); ids.RemoveAt(ids.Count - 1); Step();
    var replacement = world.Add((capacity - 2) * 2.8, 0); ids.Add(replacement);
    world.Reset();
    for (int i = 0; i < 120; i++) Step();
    var final = Step();
    Console.WriteLine($"capacity={world.Capacity}; bodies={final.Bodies.Count}; removed={removed}; replacement={replacement}; reset=ok; error={final.ErrorCode}");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
