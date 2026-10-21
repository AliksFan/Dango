using Dango.Physics;
namespace Dango.Desktop.Physics.Belt;
internal static class BeltNativeValidation
{
    internal static void Validate(DangoBeltWorld belt, DangoBeltFrame frame, int tick, DangoBeltInput input,
        double maximumSpeed, float[]? guardDiagnostics, double[]? speedLimitDiagnostics,
        double[]? radialDriveDiagnostics, double[]? rearDriveDiagnostics, double[]? forceLedger)
    {
        int Count(int kind) => forceLedger is not { } rows ? 0 : Enumerable.Range(0, (int)rows[4]).Count(i => rows[8 + 6 * i + 4] == kind);
        var s = frame.Stats;
        if (forceLedger is { } forces && (forces.Length != 1024 || forces[0] != 1 || forces[1] != 1024 ||
            forces[2] != tick || forces[4] < 0 || forces[4] > 97 || forces[6] != 6 || forces[7] != 64))
            throw new InvalidOperationException("真实施力明细格式或同帧诊断失败");
        if (forceLedger is { } verticalLedger)
            ValidateVerticalLedger(verticalLedger, input.CancelForces || input.Stretch > 0 && input.Downward > 0 || frame.JumpAccepted ? 0 :
                (float)belt.Configuration.DownwardForce * (s[17] - (float)input.Downward));
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
            ValidateRearLedger(rear, main, rearLedger, tick, belt.RadialDriveEnabled,
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
    internal static void ValidateVerticalLedger(double[] ledger, double upperForce)
    {
        if (ledger.Length != 1024 || ledger[4] != Math.Truncate(ledger[4]) || ledger[4] < 0 || ledger[4] > 97 || !double.IsFinite(upperForce))
            throw new InvalidOperationException("W/S force ledger schema or budget is invalid.");
        double upper = 0, lower = 0, upperY = 0, lowerY = 0;
        var upperBins = new HashSet<int>(); var lowerBins = new HashSet<int>();
        var tolerance = 1e-6 * (1 + Math.Abs(upperForce));
        for (var i = 0; i < (int)ledger[4]; i++)
        {
            var row = 8 + 6 * i;
            if (ledger[row + 4] != 2) continue;
            var fy = ledger[row + 3]; var part = ledger[row + 5];
            if (Enumerable.Range(row, 6).Any(index => !double.IsFinite(ledger[index])) || ledger[row + 2] != 0 ||
                fy == 0 || upperForce == 0 || part != Math.Truncate(part) || part < 0 || part >= 16)
                throw new InvalidOperationException("W/S contains an invalid, horizontal or ungated load.");
            if (Math.Sign(fy) == Math.Sign(upperForce))
            {
                if (!upperBins.Add((int)part)) throw new InvalidOperationException("W/S repeats an upper material bin.");
                upper += fy; upperY += ledger[row + 1] * fy;
            }
            else
            {
                if (!lowerBins.Add((int)part)) throw new InvalidOperationException("W/S repeats a lower material bin.");
                lower += fy; lowerY += ledger[row + 1] * fy;
            }
        }
        if (Math.Abs(upper - upperForce) > tolerance || Math.Abs(lower + upperForce) > tolerance || Math.Abs(upper + lower) > tolerance ||
            upperForce != 0 && (upperBins.Count == 0 || lowerBins.Count == 0 || upperY / upper <= lowerY / lower))
            throw new InvalidOperationException("W/S must apply equal opposite totals to the upper and lower world halves.");
    }

    internal static void ValidateRearLedger(double[] rear, double[] main, double[] ledger, int tick, bool enabled,
        double movement, double radius, double statsFx, double statsFy)
    {
        const double tolerance = 1e-9;
        if (main.Length != 16 || main.Any(x => !double.IsFinite(x)) || main[0] != 3 || main[1] != (enabled ? 1 : 0) ||
            rear.Length != 16 || rear.Any(x => !double.IsFinite(x)) || rear[0] != 1 || rear[1] != (enabled ? 1 : 0) ||
            rear[2] != (enabled ? tick : 0) || main[2] != (enabled ? tick : 0) || ledger[2] != tick || rear[4] != Math.Truncate(rear[4]) || rear[4] < 0 || rear[4] > 16 ||
            rear[5] < 0 || rear[6] < 0 || rear[10] >= tolerance || rear[7] != 0 || !(radius > 0))
            throw new InvalidOperationException("Rear drive schema, frame, point count, horizontal force or budget error contract failed.");
        var expected = enabled ? Math.Abs(main[8]) / 3 : 0;
        if (Math.Abs(rear[5] - expected) >= tolerance || Math.Abs(rear[6] - rear[5]) >= tolerance ||
            Math.Abs(rear[8] + rear[6]) >= tolerance || Math.Abs(rear[11] - main[8]) >= tolerance)
            throw new InvalidOperationException("Rear downforce must be abs(main eight-force netFy)/3.");
        if ((!enabled || movement == 0 || main[3] == 0) && (rear[3] != 0 || rear[5] != 0))
            throw new InvalidOperationException("Rear force survived release or main-drive gating.");
        if (rear[5] == 0 && (Enumerable.Range(4, 5).Any(i => rear[i] != 0) || rear[11] != 0 || rear[14] != 0 || rear[15] != 0))
            throw new InvalidOperationException("Zero rear budget retained final-substep force fields.");
        if (rear[5] > 0 && (rear[4] < 1 || rear[3] <= 0 || main[4] != 8 || rear[14] != Math.Sign(movement)))
            throw new InvalidOperationException("Rear downforce lacks active eight-force source or correct movement sign.");
        double rearFx = 0, rearFy = 0, controlFx = 0, controlFy = 0, absoluteControl = 0;
        var rearCount = 0; var endpointIndices = new HashSet<int>();
        for (var i = 0; i < (int)ledger[4]; i++)
        {
            var row = 8 + 6 * i; var kind = (int)ledger[row + 4]; var fx = ledger[row + 2]; var fy = ledger[row + 3];
            if (kind is 1 or 2 or 5 or 6) { controlFx += fx; controlFy += fy; absoluteControl += Math.Abs(fx) + Math.Abs(fy); }
            if (kind != 6) continue;
            rearCount++; rearFx += fx; rearFy += fy;
            var materialIndex = ledger[row + 5] * 16 / (2 * Math.PI);
            var index = (int)Math.Round(materialIndex);
            if (rear[4] < 1 || fx != 0 || fy >= 0 || Math.Abs(fy + rear[5] / rear[4]) >= tolerance ||
                rear[14] * (ledger[row] - rear[15]) >= -1e-9 * radius ||
                index < 0 || index >= 16 || Math.Abs(materialIndex - index) >= tolerance || !endpointIndices.Add(index))
                throw new InvalidOperationException("Rear force is not an equal downward load on unique rear-half skeleton endpoints.");
        }
        if (rearCount != rear[4] || Math.Abs(rearFx - rear[7]) >= tolerance || Math.Abs(rearFy - rear[8]) >= tolerance ||
            (((int)ledger[5] & 32) != 0) != (rearCount > 0))
            throw new InvalidOperationException("Rear ledger count, mask or net force disagrees with diagnostics.");
        var statsTolerance = 1e-6 * (1 + absoluteControl);
        if (Math.Abs(controlFx - statsFx) > statsTolerance || Math.Abs(controlFy - statsFy) > statsTolerance)
            throw new InvalidOperationException("Stats control force must total main eight forces, rear load and W/S, excluding drag.");
    }

}


