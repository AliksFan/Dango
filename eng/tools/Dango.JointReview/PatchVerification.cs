using System.IO;
using System.Text.Json;
using Dango.Desktop.Physics.Native;

namespace Dango.JointReview;

internal static class PatchVerification
{
    internal static (double Length, double Gap) Patch(Box2DSelfSoftBodyNativeFrame[] f)
    {
        var xs = f.Select(v => InteractionVerification.Curve(v, 0)).ToArray();
        var ys = f.Select(v => InteractionVerification.Curve(v, 1)).ToArray();
        double best = 0, bestGap = double.PositiveInfinity;
        double center = Math.Atan2((f[1].CoreY - f[0].CoreY) * 60, (f[1].CoreX - f[0].CoreX) * 80);
        // Fit a shared supporting line, not the tangent at an arbitrarily tied closest sample.
        for (int angle = -240; angle <= 240; angle++)
        {
            double theta = center + angle * Math.PI / 720, nx = Math.Cos(theta), ny = Math.Sin(theta), tx = -ny, ty = nx;
            var n = Enumerable.Range(0, 2).Select(r => Enumerable.Range(0, 128).Select(i => nx * xs[r][i] + ny * ys[r][i]).ToArray()).ToArray();
            double edge0 = n[0].Max(), edge1 = n[1].Min();
            if (edge1 - edge0 is < -.5 or > 1) continue;
            var spans = Enumerable.Range(0, 2).Select(r => Enumerable.Range(0, 128)
                .Where(i => Math.Abs(n[r][i] - (r == 0 ? edge0 : edge1)) <= .25)
                .Select(i => tx * xs[r][i] + ty * ys[r][i]).ToArray()).ToArray();
            double shared = Math.Max(0, Math.Min(spans[0].Max(), spans[1].Max()) - Math.Max(spans[0].Min(), spans[1].Min()));
            if (shared > best) { best = shared; bestGap = edge1 - edge0; }
        }
        return (best, double.IsFinite(bestGap) ? bestGap : 999);
    }

    internal static void Run(string path, string output, bool broken = false)
    {
        Directory.CreateDirectory(output); var notes = new List<object>(); int failures = 0;
        foreach (int mode in new[] { 0, 1 })
        {
            using var session = new CollisionSession(path); double length = 0, gap = 0; int samples = 0;
            if (broken) session.SetInteractionTestFlags(48);
            double[] initialWidth = new double[2]; double recovery = 999, releaseGap = 0; int lateContacts = 0;
            Box2DSelfSoftBodyNativeFrame[]? pressed = null;
            for (int i = 0; i < 1500; i++)
            {
                var c = new float[12];
                if (i is >= 600 and < 1000) { c[0] = 1; c[6] = mode == 1 ? -1 : 0; }
                if (i is >= 1000 and < 1120) c[0] = -1;
                var f = session.Step(c);
                double Width(Box2DSelfSoftBodyNativeFrame v) { var x = InteractionVerification.Curve(v, 0); return x.Max() - x.Min(); }
                if (i == 599) initialWidth = f.Select(Width).ToArray();
                if (i == 1300) lateContacts = session.ContactSteps;
                if (i == 1499) { recovery = f.Select((v, r) => Math.Abs(Width(v) - initialWidth[r])).Max(); releaseGap = CollisionVerification.VisibleGap(f); }
                if (i is >= 880 and < 1000 && i % 10 == 0) { var p = Patch(f); length += p.Length; gap = Math.Max(gap, p.Gap); samples++; }
                if (i == 999) { pressed = f; CollisionWindow.SaveSnapshot(path, f, Path.Combine(output, $"patch-{mode}.png")); }
                if (session.Error != 0) break;
            }
            bool passed = session.Error == 0 && samples > 0 && length / samples >= 20 && gap <= 1 && recovery <= 3 && releaseGap > 20 && lateContacts == session.ContactSteps;
            notes.Add(new { Test = "side-contact-patch", Mode = mode, MeanSharedFlatLengthPixels = samples > 0 ? length / samples : 0, MaximumGapPixels = gap, RecoveryWidthErrorPixels = recovery, ReleaseGapPixels = releaseGap, session.Error, Passed = passed, Pressed = pressed });
            if (!passed) failures++;
        }
        foreach (int layout in new[] { 3, 4 })
        {
            var old = new List<Box2DSelfSoftBodyNativeFrame[]>(); int trigger = -1;
            using (var reference = new CollisionSession(path, 2, layout))
            {
                reference.SetInteractionTestFlags(32);
                for (int i = 0; i < 180; i++)
                {
                    var c = new float[12]; c[8] = i == trigger ? 1 : 0;
                    var f = reference.Step(c); old.Add(f);
                    if (trigger < 0 && i >= 30 && reference.ContactSteps > 0 && f[1].CoreY > f[0].CoreY + .4 && CollisionVerification.VisibleGap(f) < .8) trigger = i + 1;
                    if (reference.Error != 0) break;
                }
            }
            using var candidate = new CollisionSession(path, 2, layout);
            if (broken) candidate.SetInteractionTestFlags(48);
            double bestAsymmetry = double.NegativeInfinity;
            Box2DSelfSoftBodyNativeFrame[]? strongest = null;
            for (int i = 0; i < old.Count; i++)
            {
                var c = new float[12]; c[8] = i == trigger ? 1 : 0;
                var f = candidate.Step(c);
                if (trigger > 0 && i >= trigger && i < trigger + 12)
                {
                    var before = old[trigger - 1][0];
                    double Height(Box2DSelfSoftBodyNativeFrame frame, int n) => frame.CoreY * 60d + frame.Points[2 * n + 1] * Math.Min(1, .98 / frame.Points.Max(v => Math.Abs(v))) * 58;
                    var left = Enumerable.Range(0, 16).Where(n => before.Points[2 * n] < -.1 && before.Points[2 * n + 1] > .1).ToArray();
                    var right = Enumerable.Range(0, 16).Where(n => before.Points[2 * n] > .1 && before.Points[2 * n + 1] > .1).ToArray();
                    double l = left.Average(n => Height(old[i][0], n) - Height(f[0], n));
                    double r = right.Average(n => Height(old[i][0], n) - Height(f[0], n));
                    double asymmetric = layout == 3 ? l - r : r - l;
                    if (asymmetric > bestAsymmetry) { bestAsymmetry = asymmetric; strongest = f; }
                }
                if (candidate.Error != 0) break;
            }
            bool passed = trigger > 0 && candidate.Error == 0 && bestAsymmetry >= .4;
            notes.Add(new { Test = "localized-support-pressure", Layout = layout, TriggerFrame = trigger, ExtraNearSideCompressionPixels = bestAsymmetry, candidate.Error, Passed = passed });
            if (!passed) failures++;
            if (strongest is not null) CollisionWindow.SaveSnapshot(path, strongest, Path.Combine(output, $"pressure-{layout}.png"));
        }
        using var report = new FileStream(Path.Combine(output, "patch.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(report, new { Failures = failures, Tests = notes }, new JsonSerializerOptions { WriteIndented = true });
        if (failures > 0) throw new InvalidDataException("Contact patch acceptance failed; see patch.json");
    }
}
