using System.Diagnostics;

namespace Dango.PhysicsLab;

internal sealed record LabBeltCapabilities(bool ShapeGuardAvailable, bool ShapeGuardEnabled,
    bool SpeedSaturationAvailable, bool SpeedSaturationEnabled, bool RadialDriveAvailable, bool RadialDriveEnabled,
    bool RearDriveAvailable, bool ForceLedgerAvailable, bool WhaleDriveEnabled, bool OriginalGroundJumpEnabled,
    bool DebugStateAvailable, bool SolverDiagnosticsAvailable)
{
    internal static LabBeltCapabilities From(LabBeltWorld world) => new(world.ShapeGuardAvailable, world.ShapeGuardEnabled,
        world.SpeedSaturationAvailable, world.SpeedSaturationEnabled, world.RadialDriveAvailable, world.RadialDriveEnabled,
        world.RearDriveAvailable, world.ForceLedgerAvailable, world.WhaleDriveEnabled, world.OriginalGroundJumpEnabled,
        world.DebugStateAvailable, world.SolverDiagnosticsAvailable);
}

// Every array is allocated by the native read for this tick; producers never mutate it after publication.
internal sealed record LabBeltStepSnapshot(long Epoch, int Tick, LabInputSnapshot Input, LabBeltInput ActualInput,
    LabBeltFrame? Frame, float[]? Guard, double[]? Speed, double[]? Radial, double[]? Rear, double[]? Ledger,
    float[]? Diagnostics, double[]? PreState, double[]? SolverFailure, Exception? Error,
    double NativeStepMilliseconds, double NativeReadMilliseconds, long PublishedTimestamp)
{
    internal double FixedDt => LabWindow.FixedDt;
}

internal static class LabBeltStepPipeline
{
    internal static LabBeltStepSnapshot Step(LabBeltWorld belt, long epoch, int tick, LabInputSnapshot input,
        LabBeltInput actualInput, double maximumSpeed, bool calibrated)
    {
        double[]? pre = null; LabBeltFrame? frame = null; float[]? guard = null;
        double[]? speed = null, radial = null, rear = null, ledger = null, solver = null;
        float[]? diagnostics = null; Exception? error = null;
        var reads = Stopwatch.StartNew(); double nativeMs = 0;
        try
        {
            pre = belt.DebugStateAvailable ? belt.ReadDebugState() : null;
            reads.Stop(); var native = Stopwatch.StartNew();
            try { belt.Step(LabWindow.FixedDt, actualInput); }
            finally { native.Stop(); nativeMs = native.Elapsed.TotalMilliseconds; reads.Start(); }
            frame = belt.Read();
            guard = belt.ShapeGuardEnabled ? belt.ReadShapeGuard() : null;
            speed = belt.SpeedSaturationEnabled ? belt.ReadSpeedLimit() : null;
            radial = belt.RadialDriveAvailable ? belt.ReadRadialDrive() : null;
            rear = belt.RearDriveAvailable ? belt.ReadRearDrive() : null;
            ledger = belt.ForceLedgerAvailable ? belt.ReadForceLedger() : null;
            diagnostics = calibrated ? belt.ReadDiagnostics() : null;
            Validate(belt, frame, tick, actualInput, maximumSpeed, guard, speed, radial, rear, ledger);
        }
        catch (Exception caught)
        {
            error = caught;
            T? Evidence<T>(Func<T> read) where T : class
            {
                try { return read(); }
                catch (Exception diagnosticError) { error = new AggregateException(error!, diagnosticError); return null; }
            }
            if (belt.ShapeGuardAvailable) guard = Evidence(belt.ReadShapeGuard);
            if (belt.SpeedSaturationAvailable) speed = Evidence(belt.ReadSpeedLimit);
            if (belt.RadialDriveAvailable) radial = Evidence(belt.ReadRadialDrive);
            if (belt.RearDriveAvailable) rear = Evidence(belt.ReadRearDrive);
            if (belt.ForceLedgerAvailable) ledger = Evidence(belt.ReadForceLedger);
            if (belt.SolverDiagnosticsAvailable) solver = Evidence(belt.ReadSolverDiagnostics);
        }
        reads.Stop();
        return new(epoch, tick, input, actualInput, frame, guard, speed, radial, rear, ledger, diagnostics,
            pre, solver, error, nativeMs, reads.Elapsed.TotalMilliseconds, Stopwatch.GetTimestamp());
    }

    private static void Validate(LabBeltWorld belt, LabBeltFrame frame, int tick, LabBeltInput input,
        double maximumSpeed, float[]? guardDiagnostics, double[]? speedLimitDiagnostics,
        double[]? radialDriveDiagnostics, double[]? rearDriveDiagnostics, double[]? forceLedger)
    {
        int Count(int kind) => forceLedger is not { } rows ? 0 : Enumerable.Range(0, (int)rows[4]).Count(i => rows[8 + 6 * i + 4] == kind);
        var s = frame.Stats;
        if (forceLedger is { } forces && (forces.Length != 1024 || forces[0] != 1 || forces[1] != 1024 ||
            forces[2] != tick || forces[4] < 0 || forces[4] > 97 || forces[6] != 6 || forces[7] != 64))
            throw new InvalidOperationException("真实施力明细格式或同帧诊断失败");
        if (belt.RadialDriveEnabled && (radialDriveDiagnostics is not { } radial || radial.Any(x => !double.IsFinite(x)) || radial[0] != 3 || radial[1] != 1 || radial[2] != tick ||
            (radial[4] != 0 && radial[4] != 8) || radial[10] >= 1e-9 || radial[11] >= 1e-9))
            throw new InvalidOperationException("8点推动同帧、点数或参考合力诊断失败");
        if (belt.RadialDriveEnabled && radialDriveDiagnostics is { } drive && forceLedger is { } actualForces)
        {
            double fx = 0, fy = 0, magnitude = 0;
            for (var i = 0; i < (int)actualForces[4]; i++)
            {
                var row = 8 + 6 * i;
                if (actualForces[row + 4] != 5) continue;
                var x = actualForces[row + 2]; var y = actualForces[row + 3];
                fx += x; fy += y; magnitude += Math.Sqrt(x * x + y * y);
            }
            if (Count(5) != drive[4] || Math.Abs(fx - drive[7]) >= 1e-9 || Math.Abs(fy - drive[8]) >= 1e-9 ||
                Math.Abs(fx - drive[12]) >= 1e-9 || Math.Abs(fy - (2d / 3) * drive[15]) >= 1e-9 || Math.Abs(magnitude - drive[6]) >= 1e-9 ||
                drive[5] == 0 && (Enumerable.Range(4, 5).Any(i => drive[i] != 0) || drive[12] != 0 || drive[15] != 0))
                throw new InvalidOperationException("8点实际施力与水平保持、竖向2/3参考不一致");
        }
        if (belt.RadialDriveEnabled && (rearDriveDiagnostics is null || radialDriveDiagnostics is null || forceLedger is null))
            throw new InvalidOperationException("8点推动缺少同帧后压诊断或施力明细");
        if (rearDriveDiagnostics is { } rear && radialDriveDiagnostics is { } main && forceLedger is { } rearLedger)
            LabBeltGuardExperiments.ValidateRearLedger(rear, main, rearLedger, tick, belt.RadialDriveEnabled,
                input.Horizontal, s[62], s[18], s[19]);
        if (belt.WhaleDriveEnabled || Count(3) != 0 || (belt.RadialDriveEnabled && Count(1) != 0))
            throw new InvalidOperationException("当前实验出现旧whale塑形力或错误水平推动来源");
        if (speedLimitDiagnostics is { } cap && (cap[1] != 1 || cap[2] != tick || cap[6] > maximumSpeed))
            throw new InvalidOperationException("限速同帧或安全速度上限诊断失败");
        if (guardDiagnostics is { } g && (g[0] != 1 || g[1] != 1 || g[2] != tick || g[3] != 0 ||
            g[13] != 1 || g[11] < g[20] || g[12] < g[19]))
            throw new InvalidOperationException("形状保护同帧、核心在内或硬边界诊断失败");
        if (s[36] != 0 || s[31] > .0001 || s[39] != 0) throw new InvalidOperationException("闭合带本帧安全诊断失败");
    }
}
