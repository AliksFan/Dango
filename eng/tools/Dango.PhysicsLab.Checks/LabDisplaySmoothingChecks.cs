using Dango.Physics;
using Dango.PhysicsLab;

namespace Dango.PhysicsLab.Checks;

public static class LabDisplaySmoothingChecks
{
    public static int Run()
    {
        var passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAILED: " + name);
            passed++;
        }

        var smoother = new LabDisplaySmoothing();
        var original = GroundBody(1, 0);
        var originalContour = original.Contour.ToArray();
        var rawCoreBits = BitConverter.SingleToInt32Bits(original.CoreY);
        var output = smoother.Update(original, 1d / 120, true);
        Check(ReferenceEquals(output, original), "first frame passes through unchanged");

        const int samples = 960;
        const double dt = 1d / 120;
        var rawHeight = new double[samples];
        var shownHeight = new double[samples];
        var bottomAnchored = true;
        var worldFootFixed = true;
        DangoBodyState? last = null;
        for (var i = 0; i < samples; i++)
        {
            var offset = .35 * Math.Sin(2 * Math.PI * 4 * i * dt);
            var state = GroundBody(1, offset);
            last = smoother.Update(state, dt, true);
            var rawExtent = LabGroundExperiments.CurveExtent(state.Contour, 1);
            var shownExtent = LabGroundExperiments.CurveExtent(last.Contour, 1);
            var rawWorldBottom = state.CoreY + rawExtent.Minimum;
            var shownWorldBottom = last.CoreY + shownExtent.Minimum;
            worldFootFixed &= Math.Abs(rawWorldBottom) <= 1e-6;
            bottomAnchored &= Math.Abs(rawWorldBottom - shownWorldBottom) <= 2e-6;
            rawHeight[i] = rawExtent.Maximum - rawExtent.Minimum;
            shownHeight[i] = shownExtent.Maximum - shownExtent.Minimum;
        }
        Check(worldFootFixed, "4Hz shape fixture keeps its raw world-space foot fixed");
        Check(bottomAnchored, "world curve bottom matches raw on every frame");
        var rawRms = Rms(rawHeight);
        var shownRms = Rms(shownHeight);
        Check(shownRms <= rawRms * .70, "4Hz real curve-height RMS is reduced by at least 30 percent");

        var stable = GroundBody(1, .2);
        for (var i = 0; i < 300; i++) last = smoother.Update(stable, dt, true);
        Check(last is not null && Math.Abs(last.CoreY - stable.CoreY) < 1e-5,
            "stable core height converges to raw shape");
        Check(last is not null && last.Contour.Where((_, index) => index % 2 == 1)
                .Zip(stable.Contour.Where((_, index) => index % 2 == 1), (shown, raw) => Math.Abs(shown - raw))
                .Max() < 1e-5,
            "stable contour heights converge to raw shape");
        Check(last is not null && last.Contour.Where((_, index) => index % 2 == 0)
                .SequenceEqual(stable.Contour.Where((_, index) => index % 2 == 0)) &&
              Math.Abs(LabGroundExperiments.CurveExtent(last.Contour, 0).Maximum -
                       LabGroundExperiments.CurveExtent(stable.Contour, 0).Maximum) < 1e-12,
            "X coordinates and width remain unchanged");
        Check(last is not null && Math.Abs((last.CoreY + LabGroundExperiments.CurveExtent(last.Contour, 1).Minimum) -
                                           (stable.CoreY + LabGroundExperiments.CurveExtent(stable.Contour, 1).Minimum)) < 2e-6,
            "stable display preserves the raw world-space bottom");

        foreach (var (label, enabled, bypass, grounded) in new[]
                 { ("disabled", false, false, true), ("bypass", true, true, true), ("airborne", true, false, false) })
        {
            var changed = GroundBody(1, -.5f) with { IsGrounded = grounded };
            var pass = smoother.Update(changed, dt, enabled, bypass);
            Check(ReferenceEquals(pass, changed), label + " immediately returns raw and resets history");
            var next = GroundBody(1, 0);
            Check(ReferenceEquals(smoother.Update(next, dt, true), next), label + " clears history for next grounded frame");
        }

        smoother.Update(GroundBody(1, .4), dt, true);
        var newEntity = GroundBody(2, -.3);
        Check(ReferenceEquals(smoother.Update(newEntity, dt, true), newEntity), "entity change resets history");
        var changedPoints = new DangoBodyState(newEntity.EntityId,
            new float[] { -1, 0, 0, 1, 1, 0, 0, -1, -.5f, -.5f },
            newEntity.CoreX, newEntity.CoreY, newEntity.SignedArea, newEntity.CentroidX, newEntity.CentroidY,
            newEntity.MaximumSpeed, newEntity.GroundContactCount, true, newEntity.SelectedDragNode, 5);
        Check(ReferenceEquals(smoother.Update(changedPoints, dt, true), changedPoints), "point-count change resets history");

        smoother.Update(GroundBody(2, .4), dt, true);
        var invalidDt = GroundBody(2, -.3);
        Check(ReferenceEquals(smoother.Update(invalidDt, 0, true), invalidDt), "invalid frame clears history and passes raw");
        var afterInvalid = GroundBody(2, 0);
        Check(ReferenceEquals(smoother.Update(afterInvalid, dt, true), afterInvalid), "invalid frame cannot leak old history");
        smoother.Update(GroundBody(2, .4), dt, true);
        smoother.Reset();
        var afterReset = GroundBody(2, -.3);
        Check(ReferenceEquals(smoother.Update(afterReset, dt, true), afterReset), "explicit reset clears history");

        Check(original.Contour.SequenceEqual(originalContour) &&
              BitConverter.SingleToInt32Bits(original.CoreY) == rawCoreBits,
            "raw state and contour values remain unchanged");
        return passed;
    }

    private static DangoBodyState GroundBody(int entity, double offset)
    {
        var coreY = (float)(.5 + offset);
        var footRelativeToCore = -coreY;
        var topRelativeToCore = (float)(1 + offset) - coreY;
        return new(new DangoEntityId(entity), new float[] { -1, footRelativeToCore, 0, topRelativeToCore,
            1, footRelativeToCore, 0, footRelativeToCore }, 0, coreY, 1, 0, 0, 0, 1, true, -1, 4);
    }

    private static double Rms(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        return Math.Sqrt(values.Average(value => (value - mean) * (value - mean)));
    }
}
