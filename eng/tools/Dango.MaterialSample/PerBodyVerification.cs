using System.IO;
using System.Windows;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.MaterialSample;

// Per-body shapes in the multi-body module (dango_physics_world_add_configured).
// 1. Uniform shapes: the new build must reproduce the previous build bit for bit, through both
//    the old add and the configured add with the world configuration.
// 2. Mixed shapes: the generic rest rules (design 搂7.6) hold, and rest stays close to base.
internal static partial class RestVerification
{
    private const int PerBodyFrames = 2400;

    private static (string Name, Func<int, int, DangoPhysicsInput, DangoPhysicsInput> Script, int Bodies)[] PerBodyScripts() =>
    [
        .. Interactions.Select(x => (x.Item1, x.Item2, 2)),
        ("jump onto", (f, i, x) => i == 1 && f >= 600 && f < 660 ? x with { Horizontal = -1, JumpPressed = f == 600 } : x, 2),
        ("stack, top walks off", (f, i, x) => i == 2 && f is >= 1800 and < 1890 ? x with { Horizontal = 1 } : x, 3),
    ];

    private static DangoSelfPhysicsParameters Shape(double size, double softness, double build) =>
        new LandingBuildControls(size, softness, build).Configure(LandingBuildTable.SizeDampingPreview);

    private static (double X, double Y)[] SpacedSpawns(DangoSelfPhysicsParameters[] shapes)
    {
        var r = shapes.Select(s => s.RadialRestLength + s.NodeRadius).ToArray();
        return shapes.Length == 2
            ? [(-(r[0] + .06), 0), (r[1] + .06, 0)]
            : [(-(r[0] + .06), 0), (r[1] + .06, 0), (0, 2 * Math.Max(r[0], r[1]) + r[2] - .2)];
    }

    internal static int PerBody(string oldDefault, string newDefault, string oldRest, string newRest, string output)
    {
        var check = new Check();
        var world = DangoPhysicsDefaults.Current;
        void Save() => File.WriteAllText(output, JsonSerializer.Serialize(
            new { passed = check.Failures.Count == 0, failures = check.Failures, results = check.Results }, new JsonSerializerOptions { WriteIndented = true }));

        List<DangoBodyState[]> Play(string library, DangoSelfPhysicsParameters[]? shapes, (double, double)[] spawns,
            Func<int, int, DangoPhysicsInput, DangoPhysicsInput> script, out Sim sim)
        {
            sim = new Sim(library, world, spawns.Length, shapes, spawns);
            var s = sim;
            for (var f = 0; f < PerBodyFrames; f++) s.Advance((i, id) => script(f, i, new DangoPhysicsInput(id)));
            return sim.History;
        }

        int Diverged(List<DangoBodyState[]> a, List<DangoBodyState[]> b)
        {
            for (var f = 0; f < a.Count; f++)
                for (var i = 0; i < a[f].Length; i++) if (!Same(a[f][i], b[f][i])) return f;
            return -1;
        }

        // 1. Bit identity with uniform shapes.
        foreach (var (name, script, count) in PerBodyScripts())
        {
            Console.WriteLine("identity " + name);
            (double, double)[] spawns = count == 2 ? [(0.0, 0), (1.5, 0)] : [(-.7, 0), (.7, 0), (0, 2.0)];
            var uniform = Enumerable.Repeat(world, count).ToArray();
            foreach (var (label, before, after) in new[] { ("default", oldDefault, newDefault), ("rest", oldRest, newRest) })
            {
                using var a = new Sim(before, world, count, spawns); using var b = new Sim(after, world, count, spawns);
                using var c = new Sim(after, world, count, uniform, spawns);
                for (var f = 0; f < PerBodyFrames; f++)
                {
                    a.Advance((i, id) => script(f, i, new DangoPhysicsInput(id)));
                    b.Advance((i, id) => script(f, i, new DangoPhysicsInput(id)));
                    c.Advance((i, id) => script(f, i, new DangoPhysicsInput(id)));
                }
                var plain = Diverged(a.History, b.History); var configured = Diverged(a.History, c.History);
                check.Require(plain < 0, $"identity {label} {name}: new build differs from old at frame {plain}");
                check.Require(configured < 0, $"identity {label} {name}: configured add differs from old at frame {configured}");
                check.Results.Add(new { kind = "identity", module = label, name, plainDiverged = plain, configuredDiverged = configured });
            }
            Save();
        }

        // 2. Mixed shapes.
        var sets = new (string Name, DangoSelfPhysicsParameters[] Shapes)[]
        {
            ("80/0/0 + 120/100/100", [Shape(80, 0, 0), Shape(120, 100, 100)]),
            ("120/0/0 + 80/100/100", [Shape(120, 0, 0), Shape(80, 100, 100)]),
            ("120/100/0 + 80/0/100", [Shape(120, 100, 0), Shape(80, 0, 100)]),
            ("stack big below, small on top", [Shape(120, 0, 0), Shape(120, 0, 50), Shape(80, 100, 100)]),
            ("stack small below, big on top", [Shape(80, 50, 50), Shape(80, 0, 0), Shape(120, 100, 0)]),
        };
        foreach (var (setName, shapes) in sets)
            foreach (var (name, script, count) in PerBodyScripts().Where(s => s.Bodies == shapes.Length))
            {
                var label = $"mixed {setName} 路 {name}";
                Console.WriteLine(label);
                try
                {
                    var spawns = SpacedSpawns(shapes);
                    var restStates = Play(newRest, shapes, spawns, script, out var rest);
                    var baseStates = Play(newDefault, shapes, spawns, script, out var baseline);
                    using (rest) using (baseline)
                    {
                        double worst = 0; var worstFrame = 0;
                        for (var f = 0; f < PerBodyFrames; f++)
                            for (var i = 0; i < count; i++)
                            {
                                var d = Math.Max(Math.Abs(restStates[f][i].CoreX - baseStates[f][i].CoreX), Math.Abs(restStates[f][i].CoreY - baseStates[f][i].CoreY));
                                if (d > worst) { worst = d; worstFrame = f; }
                            }
                        var restedBefore = rest.NativeRest is { } n && n[599].All(x => x);
                        check.Require(worst <= MultiTolerance, $"{label}: rest vs base core difference {worst:F4} at frame {worstFrame}");
                        var rules = Consistency(check, label, rest.History, rest.Inputs, rest.Mutations, rest.NativeRest);
                        check.Results.Add(new { kind = "mixed", name = label, restedBeforeFrame600 = restedBefore, worstCoreDifference = worst, worstFrame, rules });
                    }
                }
                catch (Exception error) { check.Failures.Add($"{label}: {error.Message}"); }
                Save();
            }

        Save();
        Console.WriteLine($"per-body verification: {check.Results.Count} results, {check.Failures.Count} failures");
        foreach (var failure in check.Failures) Console.WriteLine("FAIL " + failure);
        return check.Failures.Count == 0 ? 0 : 1;
    }
}

internal static partial class RestVerification
{
    // Measures how much two bodies overlap in contact: native contact outline depth and its share
    // of the smaller width, node-circle (skeleton) depth, and drawn silhouette width vs outline width.
    internal static int MeasureOverlap(string library, string output)
    {
        var world = DangoPhysicsDefaults.Current;
        var results = new List<object>();
        var cases = new (string Name, DangoSelfPhysicsParameters[] Shapes, Func<int, int, DangoPhysicsInput, DangoPhysicsInput> Script, int[] Frames)[]
        {
            ("side by side, idle", [world, world], (f, i, x) => x, [599]),
            ("B pushes A", [world, world], (f, i, x) => i == 1 && f >= 600 ? x with { Horizontal = -1 } : x, [700, 800, 899]),
            ("stack, idle", [world, world, world], (f, i, x) => x, [1199]),
            ("mixed 80/0/0 + 120/100/100, B pushes A", [Shape(80, 0, 0), Shape(120, 100, 100)], (f, i, x) => i == 1 && f >= 600 ? x with { Horizontal = -1 } : x, [599, 800, 899]),
            ("mixed stack 120/0/0, 120/0/50, top 80/100/100", [Shape(120, 0, 0), Shape(120, 0, 50), Shape(80, 100, 100)], (f, i, x) => x, [1199]),
        };
        foreach (var (name, shapes, script, frames) in cases)
        {
            var spawns = SpacedSpawns(shapes);
            using var sim = new Sim(library, world, shapes.Length, shapes, spawns);
            for (var f = 0; f <= frames.Max(); f++) sim.Advance((i, id) => script(f, i, new DangoPhysicsInput(id)));
            foreach (var frame in frames)
            {
                var states = sim.History[frame];
                for (var a = 0; a < states.Length; a++)
                    for (var b = a + 1; b < states.Length; b++)
                    {
                        Point[] oa = ContactOutline.Build(states[a], shapes[a].RadialRestLength / .68), ob = ContactOutline.Build(states[b], shapes[b].RadialRestLength / .68);
                        var (gap, width) = ContactOutline.Separation(oa, ob);
                        if (gap > .05) continue;
                        var skeleton = ContactOutline.SkeletonOverlap(states[a], shapes[a].NodeRadius, states[b], shapes[b].NodeRadius);
                        double Silhouette(int i) => SampleGeometry.Create(states[i], false, 1, shapes[i].RadialRestLength / world.RadialRestLength).Bounds.Width;
                        double OutlineWidth(Point[] o) => o.Max(p => p.X) - o.Min(p => p.X);
                        // Outer extent of the node circles = the published points.
                        double SkeletonWidth(DangoBodyState s) => Enumerable.Range(0, 16).Max(n => s.Contour[2 * n]) - Enumerable.Range(0, 16).Min(n => s.Contour[2 * n]);
                        results.Add(new
                        {
                            name, frame, pair = $"{a + 1}-{b + 1}", outlineOverlap = -gap, outlineOverlapShare = -gap / width, skeletonOverlap = skeleton,
                            widths = new[] { (i: a, o: oa), (i: b, o: ob) }.Select(x => new { skeleton = SkeletonWidth(states[x.i]), outline = OutlineWidth(x.o), silhouette = Silhouette(x.i) }).ToArray(),
                        });
                        Console.WriteLine($"{name} f{frame} {a + 1}-{b + 1}: outline overlap {-gap:F4} ({-gap / width:P2}), skeleton overlap {skeleton:F4}");
                    }
            }
        }
        File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}

internal static partial class RestVerification
{
    // Side-by-side trace of one mixed-shape script on the rest and base modules.
    internal static int DiagnosePerBody(string restLib, string baseLib, int from, int to)
    {
        var world = DangoPhysicsDefaults.Current;
        DangoSelfPhysicsParameters[] shapes = [Shape(80, 0, 0), Shape(120, 100, 100)];
        var script = Interactions.Single(x => x.Item1 == "hold down beside").Item2;
        var spawns = SpacedSpawns(shapes);
        using var rest = new Sim(restLib, world, 2, shapes, spawns);
        using var baseline = new Sim(baseLib, world, 2, shapes, spawns);
        for (var f = 0; f <= to; f++)
        {
            rest.Advance((i, id) => script(f, i, new DangoPhysicsInput(id)));
            baseline.Advance((i, id) => script(f, i, new DangoPhysicsInput(id)));
            if (f < from || f % 5 != 0) continue;
            var r = rest.Last; var b = baseline.Last; var n = rest.NativeRest![f];
            var (gap, _) = ContactOutline.Separation(ContactOutline.Build(r[0], .8), ContactOutline.Build(r[1], 1.2));
            var (bgap, _) = ContactOutline.Separation(ContactOutline.Build(b[0], .8), ContactOutline.Build(b[1], 1.2));
            Console.WriteLine($"f{f} rest A({r[0].CoreX:F4},{r[0].CoreY:F4}) B({r[1].CoreX:F4},{r[1].CoreY:F4}) sleep {(n[0] ? 1 : 0)}{(n[1] ? 1 : 0)} gap {gap:F4} | base A({b[0].CoreX:F4},{b[0].CoreY:F4}) B({b[1].CoreX:F4},{b[1].CoreY:F4}) gap {bgap:F4}");
        }
        return 0;
    }
}

internal static partial class RestVerification
{
    // Control: the base module against itself with body A spawned 0.001 further left. If this
    // already exceeds the tolerance, the script amplifies tiny differences regardless of rest.
    internal static int PerBodyChaosControl(string restLib, string baseLib)
    {
        var world = DangoPhysicsDefaults.Current;
        var cases = new (DangoSelfPhysicsParameters[] Shapes, string Script)[]
        {
            ([Shape(80, 0, 0), Shape(120, 100, 100)], "hold down beside"),
            ([Shape(120, 0, 0), Shape(80, 100, 100)], "hold down beside"),
            ([Shape(120, 100, 0), Shape(80, 0, 100)], "jump away and walk back"),
            ([Shape(120, 100, 0), Shape(80, 0, 100)], "hold down beside"),
            ([Shape(80, 0, 0), Shape(120, 100, 100)], "push and retreat"),
        };
        foreach (var (shapes, name) in cases)
        {
            var script = Interactions.Single(x => x.Item1 == name).Item2;
            var spawns = SpacedSpawns(shapes);
            (double, double)[] shifted = [(spawns[0].X - .001, spawns[0].Y), spawns[1]];
            (double, double)[] nudged = [spawns[0], (spawns[1].X + 1e-5, spawns[1].Y)];
            double Worst(string a, (double, double)[] sa, string b, (double, double)[] sb)
            {
                using var x = new Sim(a, world, 2, shapes, sa); using var y = new Sim(b, world, 2, shapes, sb);
                double worst = 0;
                for (var f = 0; f < PerBodyFrames; f++)
                {
                    x.Advance((i, id) => script(f, i, new DangoPhysicsInput(id))); y.Advance((i, id) => script(f, i, new DangoPhysicsInput(id)));
                    // Body B only: A's own 0.001 shift is not a divergence.
                    worst = Math.Max(worst, Math.Max(Math.Abs(x.Last[1].CoreX - y.Last[1].CoreX), Math.Abs(x.Last[1].CoreY - y.Last[1].CoreY)));
                }
                return worst;
            }
            Console.WriteLine($"{name} [{string.Join(" + ", shapes.Select(s => $"{s.RadialRestLength / world.RadialRestLength * 100:0}"))}]: " +
                $"rest vs base B {Worst(restLib, spawns, baseLib, spawns):F4}, base vs base(A shifted 0.001) B {Worst(baseLib, spawns, baseLib, shifted):F4}, base vs base(B nudged 1e-5) B {Worst(baseLib, spawns, baseLib, nudged):F4}");
        }
        return 0;
    }
}

internal static partial class RestVerification
{
    // Lateral slide of the softest-fattest body under a held downward press, for several press
    // start frames: shows whether the slide direction depends on the idle micro-state at press time.
    internal static int PressSlide(string restLib, string baseLib)
    {
        var world = DangoPhysicsDefaults.Current;
        DangoSelfPhysicsParameters[] shapes = [Shape(80, 0, 0), Shape(120, 100, 100)];
        var spawns = SpacedSpawns(shapes);
        foreach (var (label, lib) in new[] { ("rest", restLib), ("base", baseLib) })
            foreach (var start in new[] { 600, 603, 610, 620, 650, 700 })
            {
                using var sim = new Sim(lib, world, 2, shapes, spawns);
                double x0 = 0, low = 0, high = 0;
                for (var f = 0; f < start + 600; f++)
                {
                    sim.Advance((i, id) => i == 1 && f >= start ? new DangoPhysicsInput(id, Downward: 1) : new DangoPhysicsInput(id));
                    if (f == start - 1) x0 = sim.Last[1].CoreX;
                    if (f >= start) { low = Math.Min(low, sim.Last[1].CoreX - x0); high = Math.Max(high, sim.Last[1].CoreX - x0); }
                }
                Console.WriteLine($"{label} press from {start}: B slide range [{low:F4}, {high:F4}], end {sim.Last[1].CoreX - x0:F4}");
            }
        return 0;
    }
}
