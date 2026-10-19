using System.Diagnostics;
using Dango.Desktop.Physics.Belt;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Tests.Physics.Belt;

[TestClass]
[DoNotParallelize]
public sealed class BeltBodyNativeSleepTests
{
    private static byte[] Bytes(double[] values) => System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
    private static string Module
    {
        get
        {
            var module = Environment.GetEnvironmentVariable("DANGO_BELT_MODULE");
            if (string.IsNullOrWhiteSpace(module) || !File.Exists(module))
                Assert.Inconclusive("Set DANGO_BELT_MODULE to the isolated product belt native DLL.");
            return module!;
        }
    }

    [TestMethod]
    [DataRow(100d, 50d, 50d)]
    [DataRow(80d, 0d, 0d)]
    [DataRow(80d, 0d, 100d)]
    [DataRow(80d, 100d, 0d)]
    [DataRow(80d, 100d, 100d)]
    [DataRow(120d, 0d, 0d)]
    [DataRow(120d, 0d, 100d)]
    [DataRow(120d, 100d, 0d)]
    [DataRow(120d, 100d, 100d)]
    public void NaturalRestMotionDiagnosticDoesNotAlterSleepThresholds(double size, double softness, double build)
    {
        using var native = new BeltNativeSession(Module, new(size, softness, build));
        var frames = new Queue<BeltNativeStep>(); var reports = new List<object>();
        for (var i = 1; i <= 3600; i++)
        {
            var current = native.Step(default); Assert.IsNull(current.Error, current.Error?.ToString());
            frames.Enqueue(current); if (frames.Count > 121) frames.Dequeue();
            if (i is 1800 or 3600)
            {
                var rows = frames.ToArray();
                var v = Enumerable.Range(38, 36).Select(j => rows.Max(row => Math.Abs(row.State![j]))).ToArray();
                var vTick = Enumerable.Range(38, 36).Select(j => Enumerable.Range(1, rows.Length - 1).Max(k => Math.Abs(rows[k].State![j] - rows[k - 1].State![j]))).ToArray();
                var vEnvelope = Enumerable.Range(38, 36).Select(j => rows.Max(row => row.State![j]) - rows.Min(row => row.State![j])).ToArray();
                var qTick = Enumerable.Range(2, 36).Select(j => Enumerable.Range(1, rows.Length - 1).Max(k => Math.Abs(rows[k].State![j] - rows[k - 1].State![j]))).ToArray();
                var qEnvelope = Enumerable.Range(2, 36).Select(j => rows.Max(row => row.State![j]) - rows.Min(row => row.State![j])).ToArray();
                double tickCurve = 0, tickCore = 0;
                for (var k = 1; k < rows.Length; k++)
                {
                    tickCurve = Math.Max(tickCurve, rows[k].Frame!.Curve.Zip(rows[k - 1].Frame!.Curve).Max(p => Math.Abs(p.First - p.Second)));
                    tickCore = Math.Max(tickCore, Math.Max(Math.Abs(rows[k].Frame!.CoreX - rows[k - 1].Frame!.CoreX), Math.Abs(rows[k].Frame!.CoreY - rows[k - 1].Frame!.CoreY)));
                }
                var curveEnvelope = Enumerable.Range(0, 512).Max(j => rows.Max(row => row.Frame!.Curve[j]) - rows.Min(row => row.Frame!.Curve[j]));
                var coreEnvelope = Math.Max(rows.Max(row => row.Frame!.CoreX) - rows.Min(row => row.Frame!.CoreX), rows.Max(row => row.Frame!.CoreY) - rows.Min(row => row.Frame!.CoreY));
                var report = new { Size = size, Softness = softness, Build = build, Tick = i, MaxV = v.Max(), MaxTickV = vTick.Max(), MaxWindowV = vEnvelope.Max(), MaxTickQ = qTick.Max(), MaxWindowQ = qEnvelope.Max(), TickCurve = tickCurve, TickCore = tickCore, WindowCurve = curveEnvelope, WindowCore = coreEnvelope, AllVelocityBounds = v, AllTickVelocityBounds = vTick, AllWindowVelocityBounds = vEnvelope, AllTickQBounds = qTick, AllWindowQBounds = qEnvelope };
                reports.Add(report);
                Console.WriteLine($"REST MOTION body={size}/{softness}/{build} tick={i} maxV={v.Max():G9} tickV={vTick.Max():G9} window120V={vEnvelope.Max():G9} tickQ={qTick.Max():G9} tickCurve={tickCurve:G9} tickCore={tickCore:G9} window120Q={qEnvelope.Max():G9} window120Curve={curveEnvelope:G9} window120Core={coreEnvelope:G9}");
                var folder = Path.Combine(TestRepository.Root, "artifacts", "belt-runtime", "sleep-motion");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, $"natural-{size}-{softness}-{build}.json"), System.Text.Json.JsonSerializer.Serialize(reports, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }

    [TestMethod]
    public async Task ActualNativeNaturalRestSleepsFreezesFullNumericStateAndWakesWithOneJump()
    {
        await using var runtime = new BeltBodyRuntime(body => new BeltNativeSession(Module, body), true);
        runtime.SleepEnabled = true;
        await runtime.ConfigureAsync(DangoBodyControls.Default, 0, 0);
        BeltBodySnapshot? last = null;
        for (var i = 0; i < 3600 && !runtime.Metrics.Sleeping; i++)
        {
            Assert.IsTrue(await runtime.AdvanceAsync(), runtime.Metrics.Error?.ToString());
            Assert.IsTrue(runtime.TryDequeue(out last));
        }
        Assert.IsTrue(runtime.Metrics.Sleeping, $"No sleep at tick {last?.Tick}; maxV={last?.DebugState?[38..74].Max(Math.Abs)}");
        Console.WriteLine($"Natural default sleep tick={last!.Tick} maxV={last.DebugState![38..74].Max(Math.Abs):G9}");
        var before = await runtime.CaptureStateAsync(); var calls = runtime.Metrics.NativeStepCalls;
        await Task.Delay(100);
        var after = await runtime.CaptureStateAsync();
        CollectionAssert.AreEqual(Bytes(before.State), Bytes(after.State));
        CollectionAssert.AreEqual(before.Frame.Scaffold, after.Frame.Scaffold);
        CollectionAssert.AreEqual(before.Frame.Curve, after.Frame.Curve);
        CollectionAssert.AreEqual(before.Frame.Stats, after.Frame.Stats);
        Assert.AreEqual(calls, runtime.Metrics.NativeStepCalls);
        await using var reference = new BeltBodyRuntime(body => new BeltNativeSession(Module, body), true);
        await reference.ConfigureAsync(DangoBodyControls.Default, 0, 0);
        for (var i = 0; i < last.Tick; i++) { Assert.IsTrue(await reference.AdvanceAsync()); reference.TryDequeue(out _); }
        var same = await reference.CaptureStateAsync();
        CollectionAssert.AreEqual(Bytes(before.State), Bytes(same.State), "Unslept reference must start the next step from the identical complete native state.");
        runtime.PublishInput(new(JumpPressed: true, InputActive: true)); runtime.PublishInput(default);
        reference.PublishInput(new(JumpPressed: true, InputActive: true)); reference.PublishInput(default);
        Assert.IsTrue(await runtime.AdvanceAsync()); Assert.IsTrue(runtime.TryDequeue(out var awake));
        Assert.IsTrue(await reference.AdvanceAsync()); Assert.IsTrue(reference.TryDequeue(out var expected));
        CollectionAssert.AreEqual(Bytes(expected!.DebugState!), Bytes(awake!.DebugState!));
        CollectionAssert.AreEqual(expected.Frame!.Scaffold, awake.Frame!.Scaffold);
        CollectionAssert.AreEqual(expected.Frame.Curve, awake.Frame.Curve);
        CollectionAssert.AreEqual(expected.Frame.Stats, awake.Frame.Stats);
        Assert.IsTrue(awake!.ActualInput.JumpPressed); Assert.IsTrue(awake.Frame!.JumpAccepted);
        Assert.AreEqual(last.Tick + 1, awake.Tick); Assert.AreEqual(.0166666, awake.FixedDt);
        Assert.IsTrue(await runtime.AdvanceAsync()); Assert.IsTrue(runtime.TryDequeue(out var next));
        Assert.IsFalse(next!.ActualInput.JumpPressed);
    }

    [TestMethod]
    [DataRow(100d, 50d, 50d)]
    [DataRow(80d, 0d, 0d)]
    [DataRow(80d, 0d, 100d)]
    [DataRow(80d, 100d, 0d)]
    [DataRow(80d, 100d, 100d)]
    [DataRow(120d, 0d, 0d)]
    [DataRow(120d, 0d, 100d)]
    [DataRow(120d, 100d, 0d)]
    [DataRow(120d, 100d, 100d)]
    public async Task NativeCornersSleepOnlyAfterMeasuredStableFullState(double size, double softness, double build)
    {
        await using var runtime = new BeltBodyRuntime(body => new BeltNativeSession(Module, body), true);
        runtime.SleepEnabled = true;
        await runtime.ConfigureAsync(new(size, softness, build), 0, 0);
        var window = new Queue<BeltBodySnapshot>(); BeltBodySnapshot? current = null;
        for (var i = 0; i < 3600 && !runtime.Metrics.Sleeping; i++)
        {
            Assert.IsTrue(await runtime.AdvanceAsync(), runtime.Metrics.Error?.ToString());
            Assert.IsTrue(runtime.TryDequeue(out current));
            window.Enqueue(current!); if (window.Count > 121) window.Dequeue();
        }
        if (runtime.Metrics.Sleeping)
        {
            Assert.IsTrue(current!.IsSleeping);
            var state = await runtime.CaptureStateAsync(); var calls = runtime.Metrics.NativeStepCalls;
            await Task.Delay(30);
            var after = await runtime.CaptureStateAsync();
            CollectionAssert.AreEqual(Bytes(state.State), Bytes(after.State)); Assert.AreEqual(calls, runtime.Metrics.NativeStepCalls);
            Console.WriteLine($"CORNER {size}/{softness}/{build} SLEEP tick={current.Tick} residualV={current.DebugState![38..74].Max(Math.Abs):G9}");
        }
        else
        {
            var rows = window.ToArray();
            var qEnvelope = Enumerable.Range(2, 36).Max(j => rows.Max(row => row.DebugState![j]) - rows.Min(row => row.DebugState![j]));
            var vEnvelope = Enumerable.Range(38, 36).Max(j => rows.Max(row => row.DebugState![j]) - rows.Min(row => row.DebugState![j]));
            var curveEnvelope = Enumerable.Range(0, 512).Max(j => rows.Max(row => row.Frame!.Curve[j]) - rows.Min(row => row.Frame!.Curve[j]));
            // An unsettled corner is a correct refusal, never a reason to widen the envelope.
            Assert.IsTrue(qEnvelope > BeltSleepPolicy.MaximumWindowDelta || curveEnvelope > BeltSleepPolicy.MaximumWindowDelta ||
                vEnvelope > BeltSleepPolicy.MaximumVelocityWindowDelta || !current!.Frame!.Grounded || current.Frame.ActiveContacts == 0,
                "A fully stable final 120-tick state should already have slept.");
            Console.WriteLine($"CORNER {size}/{softness}/{build} CORRECT_REFUSAL tick={current!.Tick} qWindow={qEnvelope:G9} curveWindow={curveEnvelope:G9} vWindow={vEnvelope:G9}");
        }
    }

    [TestMethod]
    [DataRow("walk-stop")]
    [DataRow("drag-release")]
    [DataRow("jump-release")]
    [DataRow("hot-edit")]
    [DataRow("cancel")]
    public async Task NativeRestAfterActivitySleepsFreezesAndWakes(string activity)
    {
        await using var runtime = new BeltBodyRuntime(body => new BeltNativeSession(Module, body), true);
        runtime.SleepEnabled = true;
        await runtime.ConfigureAsync(DangoBodyControls.Default, 0, 0);
        BeltBodySnapshot? current = null;
        for (var i = 0; i < 300 && !runtime.Metrics.Sleeping; i++)
        { Assert.IsTrue(await runtime.AdvanceAsync(), runtime.Metrics.Error?.ToString()); runtime.TryDequeue(out current); }
        if (activity == "walk-stop")
        {
            runtime.PublishInput(new(Horizontal: 1, InputActive: true));
            for (var i = 0; i < 120; i++) { Assert.IsTrue(await runtime.AdvanceAsync(), runtime.Metrics.Error?.ToString()); runtime.TryDequeue(out current); }
            runtime.ClearInput();
        }
        else if (activity == "drag-release")
        {
            runtime.PublishInput(new(DragActive: true, DragTargetX: current!.Frame!.CoreX + .12, DragTargetY: current.Frame.CoreY + .1, InputActive: true));
            for (var i = 0; i < 90; i++) { Assert.IsTrue(await runtime.AdvanceAsync(), runtime.Metrics.Error?.ToString()); runtime.TryDequeue(out current); }
            runtime.ClearInput();
        }
        else if (activity == "jump-release")
        { runtime.PublishInput(new(JumpPressed: true, InputActive: true)); runtime.PublishInput(default); }
        else if (activity == "hot-edit")
            await runtime.ConfigureAsync(new(110, 60, 40), 1, 0);
        else
            await runtime.ConfigureAsync(DangoBodyControls.Default, 1, 1);

        for (var i = 0; i < 3600 && !runtime.Metrics.Sleeping; i++)
        { Assert.IsTrue(await runtime.AdvanceAsync(), runtime.Metrics.Error?.ToString()); runtime.TryDequeue(out current); }
        Console.WriteLine($"ACTIVITY {activity} sleeping={runtime.Metrics.Sleeping} tick={current?.Tick} calls={runtime.Metrics.NativeStepCalls}");
        Assert.IsTrue(runtime.Metrics.Sleeping, $"Activity {activity} did not establish the frozen 120-tick stable-state gate.");
        var before = await runtime.CaptureStateAsync(); var calls = runtime.Metrics.NativeStepCalls;
        await Task.Delay(50);
        var after = await runtime.CaptureStateAsync();
        CollectionAssert.AreEqual(Bytes(before.State), Bytes(after.State)); Assert.AreEqual(calls, runtime.Metrics.NativeStepCalls);
        runtime.PublishInput(new(InputActive: true)); Assert.IsTrue(await runtime.AdvanceAsync());
        Assert.IsFalse(runtime.Metrics.Sleeping);
    }

    [TestMethod]
    public async Task AutomaticOwnerCpuComparisonSameRestStateAndWallDuration()
    {
        var module = Module;
        var enabled = await Measure(true); var disabled = await Measure(false);
        Console.WriteLine($"SLEEP CPU AB window=3000ms enabledCPU={enabled.Cpu:F3}ms disabledCPU={disabled.Cpu:F3}ms enabledCalls={enabled.Calls} disabledCalls={disabled.Calls} enabledNative={enabled.Native:F3}ms disabledNative={disabled.Native:F3}ms enabledAllocated={enabled.Allocated} disabledAllocated={disabled.Allocated}");
        Assert.AreEqual(0L, enabled.Calls);
        Assert.IsTrue(disabled.Calls >= 120);
        // Process CPU includes test host noise; this reports measured CPU without promising power.
        Assert.IsTrue(enabled.Cpu <= disabled.Cpu * .5, "Actual process CPU must decrease at least 50% for this controlled candidate.");

        async Task<(double Cpu, long Calls, double Native, long Allocated)> Measure(bool sleepEnabled)
        {
            await using var runtime = new BeltBodyRuntime(module);
            runtime.SleepEnabled = true;
            await runtime.ConfigureAsync(DangoBodyControls.Default, 0, 0);
            Assert.IsTrue(await runtime.ResumeAsync());
            // Both arms warm using the real wall-clock owner and same frozen sleep decision.
            var warmup = Stopwatch.GetTimestamp();
            while (!runtime.Metrics.Sleeping && Stopwatch.GetElapsedTime(warmup).TotalSeconds < 20)
            {
                while (runtime.TryDequeue(out var frame)) Assert.IsNull(frame!.Error, frame.Error?.ToString());
                await Task.Delay(8);
            }
            Assert.IsTrue(runtime.Metrics.Sleeping);
            while (runtime.TryDequeue(out _)) { }
            runtime.SleepEnabled = sleepEnabled;
            var before = runtime.Metrics;
            using var process = Process.GetCurrentProcess(); var beforeCpu = process.TotalProcessorTime;
            var allocated = GC.GetTotalAllocatedBytes();
            var started = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 3000)
            {
                while (runtime.TryDequeue(out var frame)) Assert.IsNull(frame!.Error, frame.Error?.ToString());
                await Task.Delay(8);
            }
            process.Refresh();
            var after = runtime.Metrics;
            return ((process.TotalProcessorTime - beforeCpu).TotalMilliseconds, after.NativeStepCalls - before.NativeStepCalls,
                after.NativeStepMilliseconds - before.NativeStepMilliseconds, GC.GetTotalAllocatedBytes() - allocated);
        }
    }
}
