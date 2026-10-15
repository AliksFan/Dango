using Dango.Physics;

namespace Dango.PhysicsLab;

/// <summary>Low-pass display candidate for grounded contour and core height.</summary>
public sealed class LabDisplaySmoothing
{
    public const double TimeConstantSeconds = .060;
    private int? entityId;
    private int pointCount;
    private double[]? relativeY;
    private double coreRelativeY;

    /// <summary>
    /// Smooths grounded display heights only. Call once for each successful physics frame;
    /// rendering should read the returned state without advancing this helper.
    /// </summary>
    public DangoBodyState Update(DangoBodyState raw, double dt, bool enabled, bool bypass = false)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (!enabled || bypass || !raw.IsGrounded || !Valid(raw, dt))
        {
            Reset();
            return raw;
        }

        var count = raw.Contour.Count / 2;
        if (relativeY is null || entityId != raw.EntityId.Value || pointCount != count)
        {
            Reset();
            entityId = raw.EntityId.Value;
            pointCount = count;
            relativeY = new double[count];
            var initialBottom = LabGroundExperiments.CurveExtent(raw.Contour, 1).Minimum;
            for (var i = 0; i < count; i++) relativeY[i] = raw.Contour[2 * i + 1] - initialBottom;
            coreRelativeY = -initialBottom;
            return raw;
        }

        var contourBottom = LabGroundExperiments.CurveExtent(raw.Contour, 1).Minimum;
        var rawWorldBottom = raw.CoreY + contourBottom;
        var alpha = 1 - Math.Exp(-dt / TimeConstantSeconds);
        var aboveFoot = new float[raw.Contour.Count];
        for (var i = 0; i < count; i++)
        {
            var rawRelative = raw.Contour[2 * i + 1] - contourBottom;
            relativeY[i] += alpha * (rawRelative - relativeY[i]);
            aboveFoot[2 * i] = raw.Contour[2 * i];
            aboveFoot[2 * i + 1] = (float)relativeY[i];
        }

        var rawCoreRelative = -contourBottom;
        coreRelativeY += alpha * (rawCoreRelative - coreRelativeY);
        var filteredContourBottom = LabGroundExperiments.CurveExtent(aboveFoot, 1).Minimum;
        var display = new float[raw.Contour.Count];
        for (var i = 0; i < count; i++)
        {
            display[2 * i] = raw.Contour[2 * i];
            display[2 * i + 1] = (float)(relativeY[i] - coreRelativeY);
        }

        return raw with
        {
            Contour = display,
            CoreY = (float)(rawWorldBottom + coreRelativeY - filteredContourBottom)
        };
    }

    public void Reset()
    {
        entityId = null;
        pointCount = 0;
        relativeY = null;
        coreRelativeY = 0;
    }

    private static bool Valid(DangoBodyState body, double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0 || body.PointCount <= 0 || body.Contour.Count != body.PointCount * 2)
            return false;
        if (!float.IsFinite(body.CoreX) || !float.IsFinite(body.CoreY)) return false;
        foreach (var value in body.Contour) if (!float.IsFinite(value)) return false;
        var minimum = LabGroundExperiments.CurveExtent(body.Contour, 1).Minimum;
        return double.IsFinite(minimum) && float.IsFinite((float)(body.CoreY + minimum));
    }
}
