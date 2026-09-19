using System.IO;
using System.Text.Json;
using Dango.Desktop.Physics.Native;

namespace Dango.JointReview;

internal static class CushionVerification
{
    // Ratio is computed in world coordinates; pixel distance is diagnostic only.
    internal static (double Gap, double SideBlend, double Ratio) Contact(Box2DSelfSoftBodyNativeFrame[] frames)
    {
        var xs = frames.Select(f => InteractionVerification.Curve(f, 0)).ToArray();
        var ys = frames.Select(f => InteractionVerification.Curve(f, 1)).ToArray();
        double best = double.NegativeInfinity, blend = 0, pixelScale = 1, width = 1;
        for (int r = 0; r < 2; r++) for (int i = 0; i < 128; i++)
        {
            int j = (i + 1) % 128;
            double ex = (xs[r][j] - xs[r][i]) / 80, ey = (ys[r][j] - ys[r][i]) / 60;
            double length = Math.Sqrt(ex * ex + ey * ey);
            if (length < 1e-10) continue;
            double nx = ey / length, ny = -ex / length;
            var p0 = Enumerable.Range(0, 128).Select(n => nx * xs[0][n] / 80 + ny * ys[0][n] / 60).ToArray();
            var p1 = Enumerable.Range(0, 128).Select(n => nx * xs[1][n] / 80 + ny * ys[1][n] / 60).ToArray();
            double gap = Math.Max(p1.Min() - p0.Max(), p0.Min() - p1.Max());
            // Select in world space, exactly as the native SAT; convert only the result.
            if (gap > best)
            {
                best = gap;
                blend = Math.Clamp((Math.Abs(nx) - .75) / .2, 0, 1);
                pixelScale = Math.Sqrt(nx * nx / 6400 + ny * ny / 3600);
                width = Math.Min(p0.Max() - p0.Min(), p1.Max() - p1.Min());
            }
        }
        return (best / pixelScale, blend, best / width);
    }

    internal static double Width(Box2DSelfSoftBodyNativeFrame f)
    {
        var x = InteractionVerification.Curve(f, 0); return x.Max() - x.Min();
    }
    internal static double Height(Box2DSelfSoftBodyNativeFrame f)
    {
        var y = InteractionVerification.Curve(f, 1); return y.Max() - y.Min();
    }

    internal static void Run(string path, string output, bool broken)
    {
        Directory.CreateDirectory(output); var notes = new List<object>(); int failures = 0;
        foreach (int mode in new[] { 0, 1 })
        {
            using var s = new CollisionSession(path);
            if (broken) s.SetInteractionTestFlags(64 | 128);
            double overlap = 0, maximum = 0, release = 0; int samples = 0, contacts = 0;
            for (int i = 0; i < 1800; i++)
            {
                var c = new float[12];
                if (i is >= 600 and < 1500) { c[0] = 1; c[6] = mode == 1 ? -1 : 0; }
                if (i is >= 1500 and < 1620) c[0] = -1;
                var f = s.Step(c);
                if (s.Error != 0) break;
                if (i % 10 == 0)
                {
                    double depth = Math.Max(0, -Contact(f).Ratio); maximum = Math.Max(maximum, depth);
                    if (i is >= 1200 and < 1500) { overlap += depth; samples++; }
                }
                if (i == 1499) CollisionWindow.SaveSnapshot(path, f, Path.Combine(output, $"side-{mode}.png"));
                if (i == 1700) contacts = s.ContactSteps;
                if (i == 1799) release = Contact(f).Gap;
            }
            bool passed = s.Error == 0 && samples == 30 && overlap / samples is >= .02 and <= .031 && maximum <= .031 && release > 20 && contacts == s.ContactSteps;
            notes.Add(new { Test = "bounded-side-overlap-ratio", Mode = mode, MeanOverlapRatio = overlap / Math.Max(1, samples), MaximumOverlapRatio = maximum, ReleaseGapPixels = release, s.Error, Passed = passed });
            if (!passed) failures++;
        }
        var reference = new List<Box2DSelfSoftBodyNativeFrame[]>();
        using (var s = new CollisionSession(path, 2, 1))
        {
            s.SetInteractionTestFlags(128);
            for (int i = 0; i < 600; i++)
            {
                reference.Add(s.Step(new float[12]));
                if (s.Error != 0) throw new InvalidDataException("Support reference failed");
            }
        }
        using (var s = new CollisionSession(path, 2, 1))
        {
            if (broken) s.SetInteractionTestFlags(128);
            double widthGain = 0, heightLoss = 0, supportDepth = 0, maximumDepth = 0; int samples = 0;
            Box2DSelfSoftBodyNativeFrame[]? snapshot = null;
            for (int i = 0; i < 600; i++)
            {
                var f = s.Step(new float[12]); var old = reference[i];
                if (s.Error != 0) break;
                var contact = Contact(f); var oldContact = Contact(old);
                maximumDepth = Math.Max(maximumDepth, -contact.Ratio);
                if (i is >= 45 and < 180 && f[1].CoreY > f[0].CoreY + .5 && old[1].CoreY > old[0].CoreY + .5 &&
                    i % 3 == 0 && contact.Gap < .8 && contact.Ratio >= -.031 && oldContact.Gap < .8 && oldContact.Ratio >= -.031)
                {
                    supportDepth += Math.Max(0, -contact.Ratio);
                    widthGain += Width(f[1]) - Width(old[1]);
                    heightLoss += Height(old[1]) - Height(f[1]); samples++;
                    snapshot = f;
                }
            }
            bool passed = s.Error == 0 && samples >= 10 && widthGain / samples >= 2 && heightLoss / samples >= 1;
            notes.Add(new { Test = "upper-support-flattening", Samples = samples, MeanWidthGainPixels = widthGain / Math.Max(1, samples), MeanHeightLossPixels = heightLoss / Math.Max(1, samples), s.Error, Passed = passed });
            if (!passed) failures++;
            bool overlapPassed = s.Error == 0 && samples >= 10 && supportDepth / samples is >= .02 and <= .031 && maximumDepth <= .031;
            notes.Add(new { Test = "top-overlap-ratio", MeanOverlapRatio = supportDepth / Math.Max(1, samples), MaximumOverlapRatio = maximumDepth, Passed = overlapPassed });
            if (!overlapPassed) failures++;
            if (snapshot is not null) CollisionWindow.SaveSnapshot(path, snapshot, Path.Combine(output, "supported-flat.png"));
        }
        double groundWidth = 0, groundHeight = 0;
        using (var ground = new CollisionSession(path, 1))
        {
            for (int i = 0; i < 600; i++)
            {
                var f = ground.Step(new float[12])[0];
                if (ground.Error != 0) throw new InvalidDataException("Ground reference failed");
                if (i == 599) { groundWidth = Width(f); groundHeight = Height(f); }
            }
        }
        using (var s = new CollisionSession(path, 2, 1))
        {
            Box2DSelfSoftBodyNativeFrame[]? last = null; double departure = 0;
            for (int i = 0; i < 900; i++)
            {
                var c = new float[12];
                if (i is >= 180 and < 300) c[6] = 1;
                if (i == 180) c[8] = 1;
                last = s.Step(c);
                if (s.Error != 0) break;
                if (i is >= 180 and < 300) departure = Math.Max(departure, Contact(last).Gap);
            }
            double widthError = last is null ? 999 : Math.Abs(Width(last[1]) - groundWidth);
            double heightError = last is null ? 999 : Math.Abs(Height(last[1]) - groundHeight);
            bool passed = s.Error == 0 && last is not null && last[1].Grounded && departure > 20 && widthError <= 3 && heightError <= 3;
            notes.Add(new { Test = "support-release-and-recovery", DeparturePixels = departure, WidthErrorPixels = widthError, HeightErrorPixels = heightError, s.Error, Passed = passed });
            if (!passed) failures++;
            if (last is not null && s.Error == 0) CollisionWindow.SaveSnapshot(path, last, Path.Combine(output, "recovered.png"));
        }
        using var report = new FileStream(Path.Combine(output, "cushion.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(report, new { Failures = failures, Tests = notes }, new JsonSerializerOptions { WriteIndented = true });
        if (failures > 0) throw new InvalidDataException("Cushion acceptance failed; see cushion.json");
    }
}
