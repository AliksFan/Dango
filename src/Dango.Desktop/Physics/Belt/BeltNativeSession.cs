using System.Diagnostics;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Physics.Belt;

internal interface IBeltBodySession : IDisposable
{
    void Reconfigure(DangoBodyControls body);
    void SetBounds(double left, double right);
    void SetSolidWalls(bool enabled) { if (enabled) throw new NotSupportedException("This session cannot enable preview side walls."); }
    BeltNativeStep Step(DangoBeltInput input);
    (DangoBeltFrame Frame, double[] State) CaptureState() => throw new NotSupportedException();
}

internal sealed record BeltNativeStep(DangoBeltFrame? Frame, double[]? State, float[]? Guard,
    double[]? Speed, double[]? Radial, double[]? Rear, double[]? Ledger, double[]? PreState,
    double[]? SolverFailure, Exception? Error, double StepMilliseconds, double ReadMilliseconds);

internal sealed class BeltNativeSession : IBeltBodySession
{
    private readonly DangoBeltWorld world;
    private int attempt;
    internal BeltNativeSession(string module, DangoBodyControls body)
    {
        world = new(module, DangoBodyMaterial.Configure(body));
        try { world.SetBounds(-256, 256); }
        catch { world.Dispose(); throw; }
    }

    public void Reconfigure(DangoBodyControls body) => world.Reconfigure(DangoBodyMaterial.Configure(body));
    public void SetBounds(double left, double right) => world.SetBounds(left, right);
    public void SetSolidWalls(bool enabled) => world.SetSolidWalls(enabled);
    public (DangoBeltFrame Frame, double[] State) CaptureState() => (world.Read(), world.ReadDebugState());
    public BeltNativeStep Step(DangoBeltInput input)
    {
        double[]? before = null, state = null, speed = null, radial = null, rear = null, ledger = null, solver = null;
        DangoBeltFrame? frame = null; float[]? guard = null; Exception? error = null;
        double stepMs = 0; var reads = Stopwatch.StartNew();
        try
        {
            before = world.ReadDebugState();
            reads.Stop(); var started = Stopwatch.GetTimestamp();
            try { attempt++; world.Step(DangoBeltWorld.FixedDt, input); }
            finally { stepMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds; reads.Start(); }
            frame = world.Read(); state = world.ReadDebugState(); guard = world.ReadShapeGuard();
            speed = world.ReadSpeedLimit(); radial = world.ReadRadialDrive(); rear = world.ReadRearDrive(); ledger = world.ReadForceLedger();
            BeltNativeValidation.Validate(world, frame, attempt, input, world.Configuration.MaximumSpeed, guard, speed, radial, rear, ledger);
        }
        catch (Exception caught)
        {
            error = caught;
            T? Evidence<T>(Func<T> read) where T : class
            {
                try { return read(); }
                catch (Exception diagnosticError) { error = new AggregateException(error!, diagnosticError); return null; }
            }
            guard = Evidence(world.ReadShapeGuard); speed = Evidence(world.ReadSpeedLimit);
            radial = Evidence(world.ReadRadialDrive); rear = Evidence(world.ReadRearDrive);
            ledger = Evidence(world.ReadForceLedger); solver = Evidence(world.ReadSolverDiagnostics);
        }
        reads.Stop();
        return new(frame, state, guard, speed, radial, rear, ledger, before, solver, error, stepMs, reads.Elapsed.TotalMilliseconds);
    }
    public void Dispose() => world.Dispose();
}
