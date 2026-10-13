using System.IO;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.MaterialSample;

// Idle rest sleep acceptance (docs/planning/2026-10-13-idle-rest-sleep-design.md §6).
// Rest is observed from outside: a resting body publishes bit-identical frames.
internal static class RestVerification
{
    private const double Pixels = 105;
    private static readonly TimeSpan Step = MaterialSession.FixedStep;
    private const int RestDeadline = 720;      // frames from spawn (12 s)
    private const int RestHold = 3600;         // 60 s bit-identical
    private const double WakeJumpPixels = .5;
    private const double MultiTolerance = .05; // world units, about 5 px

    private sealed class Check
    {
        internal List<object> Results { get; } = [];
        internal List<string> Failures { get; } = [];
        internal void Require(bool condition, string message) { if (!condition) Failures.Add(message); }
    }

    internal static int Run(string restCandidate, string baseCandidate, string restModule, string baseModule, string output)
    {
        var check = new Check();
        void Save()
        {
            var report = new { passed = check.Failures.Count == 0, failures = check.Failures, results = check.Results };
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        void Case(string label, Action run) { Console.WriteLine("running " + label); run(); Save(); }
        foreach (var size in new[] { 80d, 100, 120 })
            foreach (var softness in new[] { 0d, 50, 100 })
                foreach (var build in new[] { 0d, 50, 100 })
                    Case($"single {size}/{softness}/{build}", () => Single(check, restCandidate, baseCandidate, size, softness, build));
        Case("platform", () => Platform(check, restModule, baseModule));
        Case("ball", () => Ball(check, restModule, baseModule));
        Case("push", () => Push(check, restModule, baseModule));
        Case("jump onto", () => JumpOnto(check, restModule, baseModule));
        Case("drop beside", () => Stack(check, restModule, baseModule));
        check.Results.Add(new { name = "stacked group rest", status = "unverified: a stable stack cannot be built with the current physics" });
        Console.WriteLine($"rest verification: {check.Results.Count} cases, {check.Failures.Count} failures");
        foreach (var failure in check.Failures) Console.WriteLine("FAIL " + failure);
        return check.Failures.Count == 0 ? 0 : 1;
    }

    private static float[] Bits(DangoBodyState s) => [.. s.Contour, s.CoreX, s.CoreY];

    private static bool Same(DangoBodyState a, DangoBodyState b)
    {
        var x = Bits(a); var y = Bits(b);
        for (var i = 0; i < x.Length; i++)
            if (BitConverter.SingleToInt32Bits(x[i]) != BitConverter.SingleToInt32Bits(y[i])) return false;
        return true;
    }

    private static (double Center, double Width, double Height) Visible(DangoBodyState s, double bodySize)
    {
        var b = SampleGeometry.Create(s, false, Pixels, bodySize).Bounds;
        return (s.CoreX * Pixels + (b.Left + b.Right) / 2, b.Width, b.Height);
    }

    private static double VisibleChange(DangoBodyState a, DangoBodyState b, double bodySize)
    {
        var p = Visible(a, bodySize); var q = Visible(b, bodySize);
        return new[] { Math.Abs(p.Center - q.Center), Math.Abs(p.Width - q.Width), Math.Abs(p.Height - q.Height),
            Math.Abs(a.CoreY - b.CoreY) * Pixels }.Max();
    }

    private sealed class Sim : IDisposable
    {
        internal DangoPhysicsWorld World { get; }
        internal List<DangoEntityId> Ids { get; } = [];
        internal int Frame { get; private set; }
        internal DangoBodyState[] Last { get; private set; } = [];
        internal DangoBodyState[] Previous { get; private set; } = [];

        internal Sim(string library, DangoSelfPhysicsParameters config, int capacity, params (double X, double Y)[] spawns)
        {
            World = DangoPhysicsWorld.Create(library, capacity: capacity, configuration: config);
            foreach (var (x, y) in spawns) Ids.Add(World.Add(x, y));
        }

        internal DangoBodyState[] Advance(Func<int, DangoEntityId, DangoPhysicsInput>? input = null)
        {
            var frame = World.Step(Step, Ids.Select((id, i) => input?.Invoke(i, id) ?? new DangoPhysicsInput(id)).ToArray());
            if (!frame.Succeeded) throw new InvalidOperationException($"native error {frame.ErrorCode} at frame {Frame}");
            Previous = Last; Last = [.. Ids.Select(id => frame.Bodies.Single(b => b.EntityId == id))];
            Frame++;
            return Last;
        }

        internal bool Resting(int i) => Previous.Length > 0 && Same(Previous[i], Last[i]);

        public void Dispose() => World.Dispose();
    }

    private static void Single(Check check, string restLib, string baseLib, double size, double softness, double build)
    {
        var name = $"single {size}/{softness}/{build}";
        try
        {
            var config = new LandingBuildControls(size, softness, build).Configure(LandingBuildTable.SizeDampingPreview);
            var bodySize = config.RadialRestLength / DangoPhysicsDefaults.Current.RadialRestLength;
            using var rest = new Sim(restLib, config, 1, (0, 0));
            using var baseline = new Sim(baseLib, config, 1, (0, 0));
            int entry = -1, divergedBeforeRest = -1;
            for (var f = 0; f < RestDeadline && entry < 0; f++)
            {
                rest.Advance(); baseline.Advance();
                if (rest.Resting(0)) entry = rest.Frame;
                else if (divergedBeforeRest < 0 && !Same(rest.Last[0], baseline.Last[0])) divergedBeforeRest = rest.Frame;
            }
            check.Require(entry > 0, $"{name}: no rest within {RestDeadline} frames");
            check.Require(divergedBeforeRest < 0, $"{name}: awake trajectory differs from base at frame {divergedBeforeRest}");
            if (entry < 0) { check.Results.Add(new { name, entry }); return; }
            var frozen = rest.Last[0];
            var moved = 0;
            for (var f = 0; f < RestHold; f++)
            {
                rest.Advance(); baseline.Advance();
                if (!Same(rest.Last[0], frozen)) moved++;
            }
            check.Require(moved == 0, $"{name}: {moved} frames changed during the 60 s rest hold");
            var baseDrift60 = VisibleChange(baseline.Last[0], frozen, bodySize);

            // Wake by zero-translation world change: physics resumes with no input.
            rest.World.TranslateHorizontal(0);
            var before = rest.Last[0]; double wakeJump = 0;
            for (var f = 0; f < 10; f++) { rest.Advance(); wakeJump = Math.Max(wakeJump, VisibleChange(rest.Last[0], before, bodySize)); }
            check.Require(wakeJump <= WakeJumpPixels, $"{name}: wake jump {wakeJump:F3} px");
            var rerest1 = Settle(rest);

            // Autonomous wander from rest: ShortWanderController ramps ±0.35 over 0.2 s and
            // abandons a drive that covers under 2 px (80 px/unit) within 0.5 s.
            var wanderStart = rest.Last[0].CoreX;
            for (var f = 0; f < 30; f++)
                rest.Advance((_, id) => new DangoPhysicsInput(id, Horizontal: .35 * Math.Min(1, (f + 1) / 60d / .2)));
            var wanderPixels = (rest.Last[0].CoreX - wanderStart) * 80;
            check.Require(wanderPixels >= 2, $"{name}: wander-style input moved only {wanderPixels:F3} px in 0.5 s");
            var rerestWander = Settle(rest);
            check.Require(rerestWander > 0, $"{name}: no rest again after wander-style wake");

            var x0 = rest.Last[0].CoreX;
            for (var f = 0; f < 60; f++) rest.Advance((_, id) => new DangoPhysicsInput(id, Horizontal: 1));
            var walked = rest.Last[0].CoreX - x0;
            check.Require(walked > .05, $"{name}: horizontal wake moved only {walked:F4}");
            var rerest2 = Settle(rest);

            var y0 = rest.Last[0].CoreY; double peak = y0;
            for (var f = 0; f < 45; f++)
            {
                rest.Advance((_, id) => new DangoPhysicsInput(id, JumpPressed: f == 0));
                peak = Math.Max(peak, rest.Last[0].CoreY);
            }
            var jumpRise = peak - y0;
            check.Require(jumpRise > .1, $"{name}: jump wake rose only {jumpRise:F4}");
            var rerest3 = Settle(rest);

            // The base build lifts the core only ~0.01 in 0.5 s with this drag, so require a
            // visible response rather than an absolute rise.
            var start = rest.Last[0]; double dragResponse = 0;
            for (var f = 0; f < 30; f++)
            {
                rest.Advance((_, id) => new DangoPhysicsInput(id, DragActive: true, DragTargetX: start.CoreX, DragTargetY: start.CoreY + .6));
                dragResponse = Math.Max(dragResponse, VisibleChange(rest.Last[0], start, bodySize));
            }
            check.Require(dragResponse > 1, $"{name}: drag wake changed only {dragResponse:F3} px");
            var rerest4 = Settle(rest);
            foreach (var (label, value) in new[] { ("translate", rerest1), ("walk", rerest2), ("jump", rerest3), ("drag", rerest4) })
                check.Require(value > 0, $"{name}: no rest again after {label} wake");
            check.Results.Add(new { name, entry, moved, baseDrift60Px = baseDrift60, wakeJumpPx = wakeJump, wanderPixels, walked, dragResponsePx = dragResponse,
                jumpRise, rerest = new[] { rerest1, rerest2, rerest3, rerest4 } });
        }
        catch (Exception error) { check.Failures.Add($"{name}: {error.Message}"); }
    }

    // Diagnostic: after a walk, report per-0.5 s motion of the published frame.
    internal static int DiagnoseAfterWalk(string library, double size, double softness, double build, int walkFrames, int dragFrames)
    {
        var config = new LandingBuildControls(size, softness, build).Configure(LandingBuildTable.SizeDampingPreview);
        using var sim = new Sim(library, config, 1, (0, 0));
        for (var f = 0; f < 300; f++) sim.Advance();
        var settled = sim.Last[0];
        if (walkFrames < 0)
        {
            // Reproduce the acceptance order: rest, wake by zero translation, then wander-style input.
            Settle(sim); sim.World.TranslateHorizontal(0); Settle(sim);
            Console.WriteLine($"rested again at frame {sim.Frame}");
            var wanderStart = sim.Last[0].CoreX;
            for (var f = 0; f < -walkFrames; f++)
                sim.Advance((_, id) => new DangoPhysicsInput(id, Horizontal: .35 * Math.Min(1, (f + 1) / 60d / .2)));
            Console.WriteLine($"wander moved {(sim.Last[0].CoreX - wanderStart) * 80:F3} px in {-walkFrames} frames");
        }
        for (var f = 0; f < walkFrames; f++) sim.Advance((_, id) => new DangoPhysicsInput(id, Horizontal: 1));
        for (var f = 0; f < dragFrames; f++)
            sim.Advance((_, id) => new DangoPhysicsInput(id, DragActive: true, DragTargetX: settled.CoreX, DragTargetY: settled.CoreY + .6));
        if (dragFrames > 0) Console.WriteLine($"drag rise {sim.Last[0].CoreY - settled.CoreY:F4} (core {settled.CoreY:F4} -> {sim.Last[0].CoreY:F4})");
        var window = sim.Last[0];
        for (var f = 1; f <= 1500; f++)
        {
            sim.Advance();
            if (f % 30 != 0) continue;
            var s = sim.Last[0];
            double node = 0;
            for (var i = 0; i < s.Contour.Count; i++) node = Math.Max(node, Math.Abs(s.Contour[i] + (i % 2 == 0 ? s.CoreX : s.CoreY) - window.Contour[i] - (i % 2 == 0 ? window.CoreX : window.CoreY)));
            Console.WriteLine($"t={f / 60.0,5:F1}s maxSpeed={s.MaximumSpeed:F5} coreDx={(s.CoreX - window.CoreX) * Pixels,8:F4}px coreDy={(s.CoreY - window.CoreY) * Pixels,8:F4}px maxNodeMove={node * Pixels,8:F4}px rest={sim.Resting(0)} grounded={s.IsGrounded}");
            window = s;
        }
        return 0;
    }

    // Diagnostic: which spawn heights/offsets produce a stable two-body stack.
    internal static int ScanStack(string library)
    {
        foreach (var y in new[] { 1.2, 1.4, 1.6, 2.0 })
            foreach (var x in new[] { 0.0, .05, .15 })
            {
                try
                {
                    using var sim = new Sim(library, DangoPhysicsDefaults.Current, 2, (0, 0), (x, y));
                    for (var f = 0; f < 600; f++) sim.Advance();
                    var a = sim.Last[0]; var b = sim.Last[1];
                    Console.WriteLine($"spawn ({x},{y}) -> A=({a.CoreX:F3},{a.CoreY:F3}) B=({b.CoreX:F3},{b.CoreY:F3}) stacked={b.CoreY > a.CoreY + .5}");
                }
                catch (Exception e) { Console.WriteLine($"spawn ({x},{y}) error {e.Message}"); }
            }
        return 0;
    }

    private static int Settle(Sim sim, int limit = RestDeadline)
    {
        for (var f = 1; f <= limit; f++) { sim.Advance(); if (sim.Resting(0)) return f; }
        return -1;
    }

    private static void Platform(Check check, string restLib, string baseLib)
    {
        const string name = "platform removal";
        try
        {
            var config = DangoPhysicsDefaults.Current;
            using var sim = new Sim(restLib, config, 1, (0, 1.2));
            sim.World.SetStaticPlatform(new DangoStaticPlatformDefinition(0, -.1, 3, .3));
            var entry = Settle(sim, 900);
            check.Require(entry > 0, $"{name}: no rest on the platform");
            var y0 = sim.Last[0].CoreY;
            sim.World.ClearStaticPlatform();
            for (var f = 0; f < 120; f++) sim.Advance();
            var fell = y0 - sim.Last[0].CoreY;
            check.Require(fell > .2, $"{name}: body fell only {fell:F4} after removal");
            check.Results.Add(new { name, entry, fell });
        }
        catch (Exception error) { check.Failures.Add($"{name}: {error.Message}"); }
    }

    private static void Ball(Check check, string restLib, string baseLib)
    {
        const string name = "ball drop onto resting body";
        try
        {
            var config = DangoPhysicsDefaults.Current;
            double Impact(string library, out int entry, out bool wokeBeforeContact)
            {
                using var sim = new Sim(library, config, 1, (0, 0));
                entry = Settle(sim, 900);
                var rested = sim.Last[0];
                sim.World.AddRoundBall(new DangoRoundBallDefinition(rested.CoreX * 4 / 3, 3.2));
                double lowest = rested.CoreY; wokeBeforeContact = false;
                for (var f = 0; f < 240; f++)
                {
                    var frame = sim.World.StepWithRoundBall(Step, [new DangoPhysicsInput(sim.Ids[0])]);
                    if (!frame.Physics.Succeeded) throw new InvalidOperationException($"native error {frame.Physics.ErrorCode}");
                    var body = frame.Physics.Bodies[0];
                    if (!wokeBeforeContact && !Same(body, rested) && !frame.DangoContact.Touching) wokeBeforeContact = true;
                    lowest = Math.Min(lowest, body.CoreY);
                }
                return rested.CoreY - lowest;
            }
            var restDip = Impact(restLib, out var entry, out var woke);
            var baseDip = Impact(baseLib, out _, out _);
            check.Require(entry > 0, $"{name}: no rest before the ball");
            check.Require(woke, $"{name}: body did not wake before ball contact");
            check.Require(Math.Abs(restDip - baseDip) <= MultiTolerance, $"{name}: impact dip {restDip:F4} vs base {baseDip:F4}");
            check.Results.Add(new { name, entry, restDip, baseDip });
        }
        catch (Exception error) { check.Failures.Add($"{name}: {error.Message}"); }
    }

    // Runs the same multi-body script on both modules and compares core positions.
    private static void Compare(Check check, string name, string restLib, string baseLib, (double, double)[] spawns,
        int frames, Func<int, int, DangoPhysicsInput, DangoPhysicsInput> script, int[] probes,
        Action<Check, List<DangoBodyState[]>, List<bool[]>>? extra = null)
    {
        try
        {
            var config = DangoPhysicsDefaults.Current;
            (List<DangoBodyState[]> States, List<bool[]> Resting) Play(string library)
            {
                using var sim = new Sim(library, config, spawns.Length, spawns);
                var states = new List<DangoBodyState[]>(); var rest = new List<bool[]>();
                for (var f = 0; f < frames; f++)
                {
                    sim.Advance((i, id) => script(f, i, new DangoPhysicsInput(id)));
                    states.Add(sim.Last);
                    rest.Add([.. Enumerable.Range(0, spawns.Length).Select(sim.Resting)]);
                }
                return (states, rest);
            }
            var a = Play(restLib); var b = Play(baseLib);
            double worst = 0;
            foreach (var p in probes)
                for (var i = 0; i < spawns.Length; i++)
                    worst = Math.Max(worst, Math.Max(Math.Abs(a.States[p][i].CoreX - b.States[p][i].CoreX),
                        Math.Abs(a.States[p][i].CoreY - b.States[p][i].CoreY)));
            check.Require(worst <= MultiTolerance, $"{name}: rest vs base core difference {worst:F4}");
            var restedFrames = a.Resting.Count(r => r.Any(x => x));
            extra?.Invoke(check, a.States, a.Resting);
            check.Results.Add(new { name, worstCoreDifference = worst, framesWithRest = restedFrames,
                probes = probes.Select(p => new { frame = p, rest = a.States[p].Select(s => new[] { s.CoreX, s.CoreY }),
                    @base = b.States[p].Select(s => new[] { s.CoreX, s.CoreY }) }) });
        }
        catch (Exception error) { check.Failures.Add($"{name}: {error.Message}"); }
    }

    private static void Push(Check check, string restLib, string baseLib) =>
        Compare(check, "push resting body", restLib, baseLib, [(-1.0, 0), (1.1, 0)], 1200,
            (f, i, input) => i == 1 && f is >= 600 and < 780 ? input with { Horizontal = -1 } : input,
            [599, 650, 700, 779, 900, 1199], (c, s, r) =>
            {
                c.Require(r[599][0] && r[599][1], "push resting body: both bodies should rest before the push");
                // Idle friction keeps A in place in both builds; require contact and that A woke.
                c.Require(s[779][1].CoreX < .5, "push resting body: B did not reach A");
                c.Require(Enumerable.Range(600, 400).Any(f => !Same(s[f][0], s[f - 1][0])), "push resting body: A never woke on contact");
            });

    private static void JumpOnto(Check check, string restLib, string baseLib) =>
        Compare(check, "jump onto resting body", restLib, baseLib, [(0.0, 0), (1.5, 0)], 1300,
            (f, i, input) => i == 1 && f >= 600 && f < 660 ? input with { Horizontal = -1, JumpPressed = f == 600 } : input,
            [599, 620, 660, 760, 1000, 1299], (c, s, r) =>
            {
                c.Require(r[599][0] && r[599][1], "jump onto resting body: both should rest first");
                c.Require(Enumerable.Range(601, 159).Any(f => !Same(s[f][0], s[f - 1][0])), "jump onto resting body: A never woke");
                c.Require(r[1299][0] && r[1299][1], "jump onto resting body: bodies did not rest again");
            });

    // A body dropped onto another always slides off the round top in both builds, so a
    // resting stack cannot be built; stacked rest stays unverified. This only compares a drop.
    private static void Stack(Check check, string restLib, string baseLib) =>
        Compare(check, "drop beside and walk away", restLib, baseLib, [(0.0, 0), (0.0, 2.0)], 2100,
            (f, i, input) => i == 1 && f is >= 1200 and < 1320 ? input with { Horizontal = 1 } : input,
            [300, 1199, 1230, 1320, 1600, 2099], (c, s, r) =>
            {
                c.Require(r[1199][0] && r[1199][1], "drop beside: both bodies should rest");
                c.Require(r[2099][0] && r[2099][1], "drop beside: bodies did not rest again after walking away");
            });
}
