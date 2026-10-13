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
    private const int RerestDeadline = 1800;   // 30 s after the last input (user-approved, design §7)
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
        var only = Environment.GetEnvironmentVariable("DANGO_REST_ONLY");
        void Case(string label, Action run)
        {
            if (!string.IsNullOrEmpty(only) && !only.Split('|').Any(label.Contains)) return;
            Console.WriteLine("running " + label); run(); Save();
        }
        foreach (var size in new[] { 80d, 100, 120 })
            foreach (var softness in new[] { 0d, 50, 100 })
                foreach (var build in new[] { 0d, 50, 100 })
                    Case($"single {size}/{softness}/{build}", () => Single(check, restCandidate, baseCandidate, size, softness, build));
        Case("platform", () => Platform(check, restModule, baseModule));
        Case("ball", () => Ball(check, restModule, baseModule));
        Case("push", () => Push(check, restModule, baseModule));
        Case("jump onto", () => JumpOnto(check, restModule, baseModule));
        Case("three-body stack", () => Stack(check, restModule, baseModule));
        foreach (var (label, script) in Interactions)
            Case("interaction " + label, () => Compare(check, "interaction " + label, restModule, baseModule, [(0.0, 0), (1.5, 0)],
                InteractionFrames, script, [.. Enumerable.Range(0, InteractionFrames)], (c, s, r) =>
                    c.Require(r[599][0] && r[599][1], $"interaction {label}: precondition not met (not resting before frame 600)")));
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
        internal List<DangoBodyState[]> History { get; } = [];
        internal List<bool[]> Inputs { get; } = [];
        // Frames whose step began right after a world mutation (a wake event, design §7.6).
        internal List<bool> Mutations { get; } = [];
        private bool mutated;
        // Native rest state after each frame (null when the module has no rest export). Read
        // directly so a rest that is woken before the next frame is still seen.
        internal List<bool[]>? NativeRest { get; }
        private readonly NativeRestReader? restState;

        internal Sim(string library, DangoSelfPhysicsParameters config, int capacity, params (double X, double Y)[] spawns)
        {
            World = DangoPhysicsWorld.Create(library, capacity: capacity, configuration: config);
            foreach (var (x, y) in spawns) Ids.Add(World.Add(x, y));
            restState = NativeRestReader.TryCreate(library, World);
            if (restState is not null) NativeRest = [];
        }

        internal DangoBodyState[] Advance(Func<int, DangoEntityId, DangoPhysicsInput>? input = null)
        {
            var inputs = Ids.Select((id, i) => input?.Invoke(i, id) ?? new DangoPhysicsInput(id)).ToArray();
            var frame = World.Step(Step, inputs);
            if (!frame.Succeeded) throw new InvalidOperationException($"native error {frame.ErrorCode} at frame {Frame}");
            Previous = Last; Last = [.. Ids.Select(id => frame.Bodies.Single(b => b.EntityId == id))];
            History.Add(Last);
            // Per-body control input. A world mutation is a wake event, not an input: what follows is
            // judged by motion, matching the native rest counter (design §7.6).
            Inputs.Add([.. inputs.Select(x => x.Horizontal != 0 || x.Downward != 0 || x.JumpPressed || x.DragActive)]);
            Mutations.Add(mutated); mutated = false;
            if (restState is not null) NativeRest!.Add(restState.Read(Ids));
            Frame++;
            return Last;
        }

        internal void Mutate(Action<DangoPhysicsWorld> change) { change(World); mutated = true; }

        internal bool Resting(int i) => Previous.Length > 0 && Same(Previous[i], Last[i]);

        public void Dispose() => World.Dispose();
    }

    // Generic rest rules (design §7.6), identical for every scenario. Rest is observed as a
    // bit-identical published frame; thresholds are the native ones with margins.
    private const int Window = 30;              // DANGO_REST_FRAMES
    private const double EntryDrift = .003;     // native d_rest .002 plus representation margin
    private const double QuietDrift = .001;     // half of d_rest: clearly quiet
    private const double QuietSpeed = .025;     // half of v_rest
    private const double GroupReach = .10;      // below the native .15 margin

    private static double PointShift(DangoBodyState a, DangoBodyState b)
    {
        double worst = 0;
        for (var k = 0; k < a.Contour.Count; k++)
            worst = Math.Max(worst, Math.Abs(a.Contour[k] + (k % 2 == 0 ? a.CoreX : a.CoreY) - b.Contour[k] - (k % 2 == 0 ? b.CoreX : b.CoreY)));
        return Math.Max(worst, Math.Max(Math.Abs(a.CoreX - b.CoreX), Math.Abs(a.CoreY - b.CoreY)));
    }

    private static (double L, double B, double R, double T) Box(DangoBodyState s)
    {
        double l = double.MaxValue, b = double.MaxValue, r = double.MinValue, t = double.MinValue;
        for (var k = 0; k < s.Contour.Count; k += 2)
        {
            double x = s.Contour[k] + s.CoreX, y = s.Contour[k + 1] + s.CoreY;
            l = Math.Min(l, x); r = Math.Max(r, x); b = Math.Min(b, y); t = Math.Max(t, y);
        }
        return (l, b, r, t);
    }

    private static bool Near(DangoBodyState a, DangoBodyState b)
    {
        var p = Box(a); var q = Box(b);
        return p.L - GroupReach <= q.R && q.L - GroupReach <= p.R && p.B - GroupReach <= q.T && q.B - GroupReach <= p.T;
    }

    private static object Consistency(Check check, string name, List<DangoBodyState[]> h, List<bool[]> input, List<bool> mutation, List<bool[]>? native)
    {
        int n = h[0].Length, falseRest = 0, missedRest = 0, splitGroup = 0, entries = 0, frozenMoved = 0;
        string? example = null;
        // rest[f]: resting at the end of frame f-1, so frame f is a frozen copy. With the native state
        // this is native[f-1]; without it, it is inferred from bit-identical frames.
        var rest = h.Select((frame, f) => frame.Select((s, i) => f > 0 && (native is null ? Same(s, h[f - 1][i]) : native[f - 1][i])).ToArray()).ToList();
        // A rest that holds into the next frame must publish that frame bit-identical.
        if (native is not null)
            for (var f = 1; f < h.Count; f++)
                for (var i = 0; i < n; i++)
                    if (native[f - 1][i] && native[f][i] && !Same(h[f][i], h[f - 1][i]))
                    { frozenMoved++; example ??= $"body {i} resting but frame {f} changed"; }
        var firstEntry = new int[n]; Array.Fill(firstEntry, -1);
        for (var f = 1; f < h.Count; f++)
            for (var i = 0; i < n; i++)
            {
                if (!rest[f][i] || rest[f - 1][i]) continue;
                // Rest entered at the end of frame f-1: the window before it must be quiet and input-free.
                entries++; if (firstEntry[i] < 0) firstEntry[i] = f;
                var start = Math.Max(0, f - 1 - Window);
                var moved = Enumerable.Range(start, f - start).Max(k => PointShift(h[k][i], h[start][i]));
                // Input in the window from this body, or from a body still touching it at entry, forbids
                // rest; a body that touched and left no longer belongs to its group (design §7.6).
                var driver = Enumerable.Range(start + 1, f - 1 - start)
                    .SelectMany(k => Enumerable.Range(0, n).Where(j => input[k][j] && (j == i || Near(h[f - 1][i], h[f - 1][j]))).Select(j => (k, j)))
                    .Select(x => ((int K, int J)?)x).LastOrDefault();
                if (moved >= EntryDrift || driver is not null)
                {
                    falseRest++;
                    example ??= $"body {i} rested at frame {f} after moving {moved:F4}" + (driver is { } d
                        ? $"; body {d.J} had input at frame {d.K}" : "");
                }
            }
        for (var f = 2 * Window; f < h.Count; f++)
        {
            var from = f - 2 * Window;
            // The native quiet count restarts at a wake event, so a quiet second cannot span one.
            if (Enumerable.Range(from + 1, 2 * Window).Any(k => input[k].Any(x => x) || mutation[k])) continue;
            var quiet = Enumerable.Range(0, n).All(i => Enumerable.Range(from, 2 * Window + 1)
                .All(k => PointShift(h[k][i], h[from][i]) < QuietDrift && h[k][i].MaximumSpeed < QuietSpeed));
            if (quiet && !rest[f].All(x => x)) { missedRest++; example ??= $"world quiet for 1 s but not resting at frame {f}"; }
        }
        for (var f = 1; f < h.Count; f++)
            for (var i = 0; i < n; i++) for (var j = i + 1; j < n; j++)
                if (rest[f][i] != rest[f][j] && Near(h[f][i], h[f][j]))
                { splitGroup++; example ??= $"bodies {i},{j} touching but rest differs at frame {f}"; }
        check.Require(falseRest == 0, $"{name}: rested while moving or under input {falseRest} times ({example})");
        check.Require(missedRest == 0, $"{name}: stayed awake while clearly still for {missedRest} frames ({example})");
        check.Require(frozenMoved == 0, $"{name}: resting body changed on {frozenMoved} frames ({example})");
        check.Require(splitGroup == 0, $"{name}: touching bodies disagreed on rest for {splitGroup} frames ({example})");
        return new { nativeState = native is not null, entries, firstEntryFrames = firstEntry, falseRest, missedRest, splitGroup, frozenMoved };
    }

    private static void Single(Check check, string restLib, string baseLib, double size, double softness, double build)
    {
        var name = $"single {size}/{softness}/{build}";
        var phase = "landing";
        Sim? active = null;
        try
        {
            var config = new LandingBuildControls(size, softness, build).Configure(LandingBuildTable.SizeDampingPreview);
            var bodySize = config.RadialRestLength / DangoPhysicsDefaults.Current.RadialRestLength;
            using var rest = new Sim(restLib, config, 1, (0, 0));
            active = rest;
            using var baseline = new Sim(baseLib, config, 1, (0, 0));
            int entry = -1, divergedBeforeRest = -1;
            for (var f = 0; f < RerestDeadline && entry < 0; f++)
            {
                rest.Advance(); baseline.Advance();
                if (rest.Resting(0)) entry = rest.Frame;
                else if (divergedBeforeRest < 0 && !Same(rest.Last[0], baseline.Last[0])) divergedBeforeRest = rest.Frame;
            }
            check.Require(divergedBeforeRest < 0, $"{name}: awake trajectory differs from base at frame {divergedBeforeRest}");
            if (entry < 0) { check.Results.Add(new { name, entry, rules = Consistency(check, name, rest.History, rest.Inputs, rest.Mutations, rest.NativeRest) }); return; }
            phase = "rest hold";
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
            phase = "translate wake";
            rest.Mutate(w => w.TranslateHorizontal(0));
            var before = rest.Last[0]; double wakeJump = 0;
            for (var f = 0; f < 10; f++) { rest.Advance(); wakeJump = Math.Max(wakeJump, VisibleChange(rest.Last[0], before, bodySize)); }
            check.Require(wakeJump <= WakeJumpPixels, $"{name}: wake jump {wakeJump:F3} px");
            phase = "after translate";
            var rerest1 = Settle(rest);

            // Autonomous wander from rest: ShortWanderController ramps ±0.35 over 0.2 s and
            // abandons a drive that covers under 2 px (80 px/unit) within 0.5 s.
            phase = "wander";
            var wanderStart = rest.Last[0].CoreX;
            for (var f = 0; f < 30; f++)
                rest.Advance((_, id) => new DangoPhysicsInput(id, Horizontal: .35 * Math.Min(1, (f + 1) / 60d / .2)));
            var wanderPixels = (rest.Last[0].CoreX - wanderStart) * 80;
            // 120/100/0 is a user-accepted known impact (design §7.4): record, do not gate.
            if (!(size == 120 && softness == 100 && build == 0))
                check.Require(wanderPixels >= 2, $"{name}: wander-style input moved only {wanderPixels:F3} px in 0.5 s");
            phase = "after wander";
            var rerestWander = Settle(rest);

            phase = "walk";
            var x0 = rest.Last[0].CoreX;
            for (var f = 0; f < 60; f++) rest.Advance((_, id) => new DangoPhysicsInput(id, Horizontal: 1));
            var walked = rest.Last[0].CoreX - x0;
            check.Require(walked > .05, $"{name}: horizontal wake moved only {walked:F4}");
            phase = "after walk";
            var rerest2 = Settle(rest);

            phase = "jump";
            var y0 = rest.Last[0].CoreY; double peak = y0;
            for (var f = 0; f < 45; f++)
            {
                rest.Advance((_, id) => new DangoPhysicsInput(id, JumpPressed: f == 0));
                peak = Math.Max(peak, rest.Last[0].CoreY);
            }
            var jumpRise = peak - y0;
            check.Require(jumpRise > .1, $"{name}: jump wake rose only {jumpRise:F4}");
            phase = "after jump";
            var rerest3 = Settle(rest);

            // The base build lifts the core only ~0.01 in 0.5 s with this drag, so require a
            // visible response rather than an absolute rise.
            phase = "drag";
            var start = rest.Last[0]; double dragResponse = 0;
            for (var f = 0; f < 30; f++)
            {
                rest.Advance((_, id) => new DangoPhysicsInput(id, DragActive: true, DragTargetX: start.CoreX, DragTargetY: start.CoreY + .6));
                dragResponse = Math.Max(dragResponse, VisibleChange(rest.Last[0], start, bodySize));
            }
            check.Require(dragResponse > 1, $"{name}: drag wake changed only {dragResponse:F3} px");
            phase = "after drag";
            var rerest4 = Settle(rest);
            var rules = Consistency(check, name, rest.History, rest.Inputs, rest.Mutations, rest.NativeRest);
            check.Results.Add(new { name, entry, moved, baseDrift60Px = baseDrift60, wakeJumpPx = wakeJump, wanderPixels, walked, dragResponsePx = dragResponse,
                jumpRise, rerestFrames = new[] { rerest1, rerestWander, rerest2, rerest3, rerest4 }, rules });
        }
        catch (Exception error)
        {
            var resting = active is { } a && a.Previous.Length > 0 ? a.Resting(0).ToString() : "?";
            check.Failures.Add($"{name}: {error.Message} (phase {phase}, previous frame resting {resting})");
        }
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
            Settle(sim); sim.Mutate(w => w.TranslateHorizontal(0)); Settle(sim);
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
        foreach (var half in new[] { .6, .65, .7, .75, .8 })
            foreach (var y in new[] { 1.4, 2.0 })
            {
                try
                {
                    using var sim = new Sim(library, DangoPhysicsDefaults.Current, 3, (-half, 0), (half, 0), (0, y));
                    for (var f = 0; f < 900; f++) sim.Advance();
                    var a = sim.Last[0]; var b = sim.Last[1]; var c = sim.Last[2];
                    Console.WriteLine($"half {half} top y {y} -> A=({a.CoreX:F3},{a.CoreY:F3}) B=({b.CoreX:F3},{b.CoreY:F3}) C=({c.CoreX:F3},{c.CoreY:F3}) stacked={c.CoreY > Math.Max(a.CoreY, b.CoreY) + .4}");
                }
                catch (Exception e) { Console.WriteLine($"half {half} y {y} error {e.Message}"); }
            }
        return 0;
    }

    // Diagnostic: replay the Single timeline. waits = "auto" settles with rest detection and
    // prints the waits; otherwise "entry,afterTranslate,afterWander" replays fixed idle counts.
    internal static int DiagnoseReplay(string library, double size, double softness, double build, string waits)
    {
        var config = new LandingBuildControls(size, softness, build).Configure(LandingBuildTable.SizeDampingPreview);
        using var sim = new Sim(library, config, 1, (0, 0));
        var fixedWaits = waits == "auto" ? null : waits.Split(',').Select(int.Parse).ToArray();
        int Wait(int index, int limit)
        {
            if (fixedWaits is not null) { for (var f = 0; f < fixedWaits[index]; f++) sim.Advance(); return fixedWaits[index]; }
            var w = Settle(sim, limit); return w;
        }
        try
        {
            var entry = Wait(0, RestDeadline);
            for (var f = 0; f < RestHold; f++) sim.Advance();
            sim.Mutate(w => w.TranslateHorizontal(0));
            for (var f = 0; f < 10; f++) sim.Advance();
            var w1 = Wait(1, RerestDeadline);
            for (var f = 0; f < 30; f++)
                sim.Advance((_, id) => new DangoPhysicsInput(id, Horizontal: .35 * Math.Min(1, (f + 1) / 60d / .2)));
            var w2 = Wait(2, RerestDeadline);
            for (var f = 0; f < 60; f++) sim.Advance((_, id) => new DangoPhysicsInput(id, Horizontal: 1));
            Console.WriteLine($"waits {entry},{w1},{w2}; walk ended at frame {sim.Frame}");
            for (var f = 0; f < RerestDeadline; f++) sim.Advance();
            Console.WriteLine($"no error through frame {sim.Frame}");
        }
        catch (Exception e) { Console.WriteLine($"error: {e.Message}"); }
        return 0;
    }

    // Diagnostic: three-body stack after the top body walks; per-body motion per 0.5 s.
    internal static int DiagnoseStack(string library)
    {
        using var sim = new Sim(library, DangoPhysicsDefaults.Current, 3, (-.7, 0), (.7, 0), (0, 2.0));
        for (var f = 0; f < 1890; f++) sim.Advance((i, id) => i == 2 && f >= 1800 ? new DangoPhysicsInput(id, Horizontal: 1) : new DangoPhysicsInput(id));
        var window = sim.Last;
        for (var f = 1; f <= 1800; f++)
        {
            sim.Advance();
            if (f % 30 != 0) continue;
            var line = $"t={f / 60.0,5:F1}s";
            for (var i = 0; i < 3; i++)
            {
                var s = sim.Last[i]; var w = window[i]; double node = 0;
                for (var k = 0; k < s.Contour.Count; k++) node = Math.Max(node, Math.Abs(s.Contour[k] + (k % 2 == 0 ? s.CoreX : s.CoreY) - w.Contour[k] - (k % 2 == 0 ? w.CoreX : w.CoreY)));
                line += $" | {"ABC"[i]} move={node * Pixels,6:F3}px v={s.MaximumSpeed:F4} rest={(sim.Resting(i) ? 1 : 0)}";
            }
            Console.WriteLine(line);
            window = sim.Last;
        }
        return 0;
    }

    private static int Settle(Sim sim, int limit = RerestDeadline)
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
            sim.Mutate(w => w.SetStaticPlatform(new DangoStaticPlatformDefinition(0, -.1, 3, .3)));
            var entry = Settle(sim);
            // Scenario precondition: removal must happen while resting to exercise the wake.
            check.Require(entry > 0, $"{name}: precondition not met (never rested on the platform)");
            var y0 = sim.Last[0].CoreY;
            sim.Mutate(w => w.ClearStaticPlatform());
            for (var f = 0; f < 120; f++) sim.Advance();
            var fell = y0 - sim.Last[0].CoreY;
            check.Require(fell > .2, $"{name}: body fell only {fell:F4} after removal");
            for (var f = 0; f < RerestDeadline; f++) sim.Advance();
            check.Results.Add(new { name, entry, fell, rules = Consistency(check, name, sim.History, sim.Inputs, sim.Mutations, sim.NativeRest) });
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
                entry = Settle(sim);
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
            check.Require(entry > 0, $"{name}: precondition not met (never rested before the ball)");
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
            (List<DangoBodyState[]> States, List<bool[]> Resting, List<bool[]> Inputs, List<bool> Mutations, List<bool[]>? Native) Play(string library)
            {
                using var sim = new Sim(library, config, spawns.Length, spawns);
                var rest = new List<bool[]>();
                for (var f = 0; f < frames; f++)
                {
                    sim.Advance((i, id) => script(f, i, new DangoPhysicsInput(id)));
                    rest.Add([.. Enumerable.Range(0, spawns.Length).Select(sim.Resting)]);
                }
                return (sim.History, rest, sim.Inputs, sim.Mutations, sim.NativeRest);
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
            var rules = Consistency(check, name, a.States, a.Inputs, a.Mutations, a.Native);
            check.Results.Add(new { name, worstCoreDifference = worst, framesWithRest = restedFrames, rules,
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
                c.Require(r[599][0] && r[599][1], "push resting body: precondition not met (not resting before the push)");
                // Idle friction keeps A in place in both builds; require contact and that A woke.
                c.Require(s[779][1].CoreX < .5, "push resting body: B did not reach A");
                c.Require(Enumerable.Range(600, 400).Any(f => !Same(s[f][0], s[f - 1][0])), "push resting body: A never woke on contact");
            });

    private static void JumpOnto(Check check, string restLib, string baseLib) =>
        Compare(check, "jump onto resting body", restLib, baseLib, [(0.0, 0), (1.5, 0)], 660 + RerestDeadline,
            (f, i, input) => i == 1 && f >= 600 && f < 660 ? input with { Horizontal = -1, JumpPressed = f == 600 } : input,
            [599, 620, 660, 760, 1000, 659 + RerestDeadline], (c, s, r) =>
            {
                c.Require(r[599][0] && r[599][1], "jump onto resting body: precondition not met (not resting before the jump)");
                c.Require(Enumerable.Range(601, 159).Any(f => !Same(s[f][0], s[f - 1][0])), "jump onto resting body: A never woke");
            });

    // Two-body actions around a resting pair (A at 0, B at 1.5, both resting by frame 599). Every
    // frame is compared with the base module, so a sleep-induced change in any action shows up.
    private const int InteractionFrames = 2400;
    private static readonly (string, Func<int, int, DangoPhysicsInput, DangoPhysicsInput>)[] Interactions =
    [
        ("jump away and walk back", (f, i, x) => i != 1 ? x : f switch
        {
            >= 600 and < 660 => x with { Horizontal = 1, JumpPressed = f == 600 },
            >= 900 and < 1020 => x with { Horizontal = -1 },
            _ => x,
        }),
        ("jump over", (f, i, x) => i == 1 && f is >= 600 and < 760 ? x with { Horizontal = -1, JumpPressed = f is 600 or 640 } : x),
        ("push and retreat", (f, i, x) => i != 1 ? x : f switch
        {
            >= 600 and < 700 => x with { Horizontal = -1 },
            >= 700 and < 760 => x with { Horizontal = 1 },
            _ => x,
        }),
        ("drag onto", (f, i, x) => i == 1 && f is >= 600 and < 720 ? x with { DragActive = true, DragTargetX = 0, DragTargetY = 1.6 } : x),
        ("hold down beside", (f, i, x) => i == 1 && f is >= 600 and < 1200 ? x with { Downward = 1 } : x),
        ("wander into neighbour", (f, i, x) => i == 0 && f is >= 600 and < 690 ? x with { Horizontal = .35 * Math.Min(1, (f - 599) / 12d) } : x),
        ("walk toward each other", (f, i, x) => f is >= 600 and < 660 ? x with { Horizontal = i == 0 ? 1 : -1 } : x),
    ];

    // Two bodies side by side with a third resting on top (user-chosen stable stack).
    private static void Stack(Check check, string restLib, string baseLib)
    {
        const int walk = 1800, end = walk + 90 + RerestDeadline;
        Compare(check, "three-body stack", restLib, baseLib, [(-.7, 0), (.7, 0), (0.0, 2.0)], end,
            (f, i, input) => i == 2 && f is >= walk and < walk + 90 ? input with { Horizontal = 1 } : input,
            [600, walk - 1, walk + 2, walk + 90, walk + 600, end - 1], (c, s, r) =>
            {
                c.Require(s[walk - 1][2].CoreY > Math.Max(s[walk - 1][0].CoreY, s[walk - 1][1].CoreY) + .4, "stack: top body is not on the lower two");
                c.Require(r[walk - 1].All(x => x), "stack: precondition not met (not resting when the top body starts walking)");
                c.Require(!Same(s[walk + 1][0], s[walk][0]) && !Same(s[walk + 1][1], s[walk][1]) ||
                    !Same(s[walk + 2][0], s[walk + 1][0]) && !Same(s[walk + 2][1], s[walk + 1][1]),
                    "stack: lower bodies did not wake with the top body");
            });
    }
}
