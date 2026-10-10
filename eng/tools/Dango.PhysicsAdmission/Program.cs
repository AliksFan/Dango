using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dango.Physics;

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: Dango.PhysicsAdmission <isolated-native-module>");
    return 2;
}

// This process is an isolated P1 probe. It has no HTTP, account, database or UI host.
var modulePath = Path.GetFullPath(args[0]);
var library = NativeLibrary.Load(modulePath);
try
{
    var allocated = Marshal.GetDelegateForFunctionPointer<AllocatedBytes>(
        NativeLibrary.GetExport(library, "dango_physics_get_allocated_bytes"));
    var before = allocated();
    var timer = Stopwatch.StartNew();
    var results = new List<ProbeResult>();
    using (var worker = new PhysicsProbeWorker(modulePath))
    {
        foreach (var scenario in new[] { ProbeScenario.Push, ProbeScenario.Stack, ProbeScenario.Airborne })
            results.Add(worker.Submit(new((int)scenario + 1, scenario, 1_200)));
        results.Add(worker.Submit(new(4, ProbeScenario.Lifecycles, 64)));
    }
    var cancellationPassed = VerifyCancellation(modulePath);
    var after = allocated();
    var success = results.All(result => result.Error is null && result.NativeError == 0 &&
        result.OwnerThreadId != Environment.CurrentManagedThreadId) && after == before &&
        results.Single(result => result.Scenario == ProbeScenario.Push).ContactSteps > 0 &&
        results.Single(result => result.Scenario == ProbeScenario.Stack).SupportedSteps > 0 && cancellationPassed;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Gate = "isolated-headless-smoke",
        Success = success,
        RealTimeOrManualAcceptance = false,
        FullCheckpointAdmitted = false,
        MainThreadId = Environment.CurrentManagedThreadId,
        DurationMilliseconds = timer.Elapsed.TotalMilliseconds,
        NativeBytesBefore = before,
        NativeBytesAfter = after,
        ConcurrentCancellationPassed = cancellationPassed,
        Results = results,
    }, new JsonSerializerOptions { WriteIndented = true }));
    return success ? 0 : 1;
}
finally
{
    NativeLibrary.Free(library);
}

static bool VerifyCancellation(string modulePath)
{
    using var worker = new PhysicsProbeWorker(modulePath);
    var waiting = Task.Run(() =>
    {
        try { worker.Submit(new(5, ProbeScenario.Lifecycles, 10_000)); return false; }
        catch (OperationCanceledException) { return true; }
    });
    if (!worker.WaitForStarted(TimeSpan.FromSeconds(3)))
    {
        worker.Dispose();
        if (!waiting.Wait(TimeSpan.FromSeconds(3))) throw new InvalidOperationException("Unreleased probe caller.");
        return false;
    }
    var ending = Task.Run(worker.Dispose);
    var ownerWithinBudget = ending.Wait(TimeSpan.FromSeconds(3));
    var callerWithinBudget = waiting.Wait(TimeSpan.FromSeconds(3));
    // A timing failure remains a failed gate, but still wait for and verify resource cleanup.
    if (!ending.Wait(TimeSpan.FromSeconds(10)) || !waiting.Wait(TimeSpan.FromSeconds(3)))
        throw new InvalidOperationException("Probe shutdown was not confirmed.");
    return ownerWithinBudget && callerWithinBudget && waiting.Result;
}

enum ProbeScenario { Push, Stack, Airborne, Lifecycles }
readonly record struct ProbeRequest(int RequestId, ProbeScenario Scenario, int Steps);
sealed record ProbeResult(int RequestId, ProbeScenario Scenario, int OwnerThreadId,
    ulong Tick, int BodyCount, int ContactSteps, int SupportedSteps, int AppliedJumps,
    int NativeError, bool StaleSupportCleared, string? Error);

sealed class PhysicsProbeWorker : IDisposable
{
    private static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60);
    private readonly BlockingCollection<ProbeRequest> requests = new(64);
    private readonly BlockingCollection<ProbeResult> replies = new(8);
    private readonly CancellationTokenSource stopping = new();
    private readonly Thread owner;
    private readonly object submissionGate = new();
    private readonly ManualResetEventSlim started = new(false);
    private readonly string modulePath;
    private int disposed;

    public PhysicsProbeWorker(string modulePath)
    {
        this.modulePath = modulePath;
        owner = new Thread(Run) { IsBackground = true, Name = "Dango P1 physics owner" };
        owner.Start();
    }

    public ProbeResult Submit(ProbeRequest request)
    {
        lock (submissionGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (!Enum.IsDefined(request.Scenario) || request.Steps is < 1 or > 10_000)
                throw new ArgumentOutOfRangeException(nameof(request));
            requests.Add(request, stopping.Token);
            var reply = replies.Take(stopping.Token);
            if (reply.RequestId != request.RequestId) throw new InvalidOperationException("Probe reply mismatch.");
            return reply;
        }
    }

    public bool WaitForStarted(TimeSpan timeout) => started.Wait(timeout);

    private void Run()
    {
        try
        {
            foreach (var request in requests.GetConsumingEnumerable(stopping.Token))
            {
                ProbeResult reply;
                try { reply = Execute(request); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    reply = new(request.RequestId, request.Scenario, Environment.CurrentManagedThreadId,
                        0, 0, 0, 0, 0, -1, false, exception.GetType().Name);
                }
                replies.Add(reply, stopping.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private ProbeResult Execute(ProbeRequest request)
    {
        // All native Create/Enable/Add/Step/Read/Remove/Dispose occur on this one thread.
        using var world = DangoPhysicsWorld.Create(modulePath, capacity: 2);
        world.EnableAuthority();
        started.Set();
        if (request.Scenario == ProbeScenario.Lifecycles)
        {
            for (var iteration = 0; iteration < request.Steps; iteration++)
            {
                stopping.Token.ThrowIfCancellationRequested();
                using var other = DangoPhysicsWorld.Create(modulePath, capacity: 2);
                other.EnableAuthority();
                var id = other.Add(0, 0);
                for (var step = 0; step < 20; step++) other.Step(FixedStep, [new(id)]);
                if (other.ReadAuthorityFrame().Bodies.Count != 1)
                    throw new InvalidOperationException("Lifecycle body count.");
            }
            return new(request.RequestId, request.Scenario, Environment.CurrentManagedThreadId,
                world.ReadAuthorityFrame().Tick, 0, 0, 0, 0, 0, true, null);
        }

        var stacked = request.Scenario == ProbeScenario.Stack;
        var airborne = request.Scenario == ProbeScenario.Airborne;
        var first = world.Add(stacked ? 0 : -1.4, airborne ? 3 : 0);
        var second = world.Add(stacked ? 0 : 1.4, stacked ? 1.65 : airborne ? 4 : 0);
        var contacts = 0;
        var supported = 0;
        var jumps = 0;
        var removed = false;
        var staleSupportCleared = !stacked;
        DangoAuthorityFrame? latest = null;
        for (var step = 0; step < request.Steps; step++)
        {
            stopping.Token.ThrowIfCancellationRequested();
            if (stacked && step == 1_000)
            {
                world.Remove(first);
                removed = true;
                var mutation = world.ReadAuthorityFrame();
                staleSupportCleared = mutation.Bodies.Count == 1 &&
                    mutation.Bodies.All(body => body.SupportEntityId is null);
                if (!staleSupportCleared) throw new InvalidOperationException("Removed support survives.");
            }
            var moving = request.Scenario == ProbeScenario.Push && step is >= 120 and < 600;
            var inputs = new List<DangoPhysicsInput>(2);
            if (!removed) inputs.Add(new(first, Horizontal: moving ? 1 : 0));
            inputs.Add(new(second, Horizontal: moving ? -1 : 0, JumpPressed: step == 300));
            var raw = world.Step(FixedStep, inputs);
            latest = world.ReadAuthorityFrame();
            if (!raw.Succeeded || latest.ErrorCode != 0 || latest.Tick != (ulong)step + 1)
                throw new InvalidOperationException("Unhealthy or incoherent common tick.");
            contacts = raw.ContactStepCount;
            supported += latest.Bodies.Count(body => body.SupportEntityId is not null);
            jumps += latest.Bodies.Count(body => body.JumpResult is
                DangoAuthorityJumpResult.AppliedFloor or DangoAuthorityJumpResult.AppliedSupport);
            if (latest.Bodies.Any(body => !float.IsFinite(body.CoreX) || !float.IsFinite(body.CoreY)))
                throw new InvalidOperationException("Nonfinite authority center.");
        }
        return new(request.RequestId, request.Scenario, Environment.CurrentManagedThreadId,
            latest!.Tick, latest.Bodies.Count, contacts, supported, jumps,
            latest.ErrorCode, staleSupportCleared, null);
    }

    public void Dispose()
    {
        // Cancellation must not wait for Submit's lock: its reply wait needs this signal to exit.
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stopping.Cancel();
        requests.CompleteAdding();
        if (!owner.Join(TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("Physics owner did not release its worlds.");
        lock (submissionGate)
        {
            requests.Dispose();
            replies.Dispose();
            started.Dispose();
            stopping.Dispose();
        }
    }
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int AllocatedBytes();
