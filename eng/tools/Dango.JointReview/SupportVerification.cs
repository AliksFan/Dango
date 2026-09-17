using System.IO;
using System.Text.Json;
using Dango.Desktop.Physics.Native;

namespace Dango.JointReview;

internal static class SupportVerification
{
    internal static void Run(string path, string output, bool broken)
    {
        Directory.CreateDirectory(output);
        var notes = new List<object>(); int failures = 0;
        bool appearance = CollisionWindow.VerifyAppearance(path);
        if (!appearance) failures++;
        notes.Add(new { Test = "retained-colors-and-hat-layer", Passed = appearance });
        var airReference = new List<Box2DSelfSoftBodyNativeFrame>();
        for (int variant = 0; variant < 2; variant++)
        {
            using var s = new CollisionSession(path, 1);
            double maxError = 0;
            for (int i = 0; i < 660; i++)
            {
                var c = new float[12]; c[2] = i == 600 || variant == 1 && i is >= 610 and < 640 ? 1 : 0;
                var f = s.Step(c)[0];
                if (variant == 0) airReference.Add(f);
                else maxError = Math.Max(maxError, f.Points.Zip(airReference[i].Points, (x, y) => Math.Abs((double)x - y))
                    .Append(Math.Abs(f.CoreY - airReference[i].CoreY)).Max());
                if (s.Error != 0) break;
            }
            bool passed = maxError <= 1e-5 && s.Error == 0;
            if (!passed) failures++;
            if (variant == 1) notes.Add(new { Test = "airborne-repeat-rejected", MaximumError = maxError, Passed = passed });
        }
        using (var s = new CollisionSession(path))
        {
            Box2DSelfSoftBodyNativeFrame[]? previous = null;
            bool landed = false, cleared = false; int lastContacts = 0;
            var trace = new List<object>();
            for (int i = 0; i < 1020; i++)
            {
                var c = new float[12];
                if (i is >= 600 and < 850 && previous is not null)
                {
                    c[0] = (float)Math.Clamp((previous[1].CoreX - previous[0].CoreX) * 1.5, -1, 1);
                    c[2] = i == 675 ? 1 : 0;
                }
                var f = s.Step(c);
                if (i is >= 695 and < 790 && i % 5 == 0) trace.Add(new { Frame = i, X = f[0].CoreX, Y = f[0].CoreY, X2 = f[1].CoreX, Y2 = f[1].CoreY, s.ContactSteps });
                if (i > 675 && InteractionVerification.Bottom(f[0]) > InteractionVerification.Top(f[1]) + 3) cleared = true;
                if (cleared && previous is not null && f[0].CoreY < previous[0].CoreY && s.ContactSteps > lastContacts &&
                    f[0].CoreY > f[1].CoreY + .5 && Math.Abs(f[0].CoreX - f[1].CoreX) < .5)
                {
                    if (!landed) CollisionWindow.SaveSnapshot(path, f, Path.Combine(output, "lower-head-landing.png"));
                    landed = true;
                }
                previous = f; lastContacts = s.ContactSteps;
                if (s.Error != 0) break;
            }
            bool passed = landed && s.Error == 0;
            notes.Add(new { Test = "lower-jump-onto-head", Cleared = cleared, Landed = landed, s.Error, Passed = passed, Trace = trace });
            if (!passed) failures++;
        }
        double groundRise = 0;
        using (var s = new CollisionSession(path))
        {
            double initial = 0, top = 0, apex = double.NegativeInfinity;
            Box2DSelfSoftBodyNativeFrame[]? highest = null;
            for (int i = 0; i < 840; i++)
            {
                var c = new float[12]; c[2] = i == 600 ? 1 : 0;
                var f = s.Step(c);
                if (i == 599) { initial = f[0].CoreY * 60d; top = InteractionVerification.Top(f[1]); }
                if (i >= 600)
                {
                    groundRise = Math.Max(groundRise, f[0].CoreY * 60d - initial);
                    if (InteractionVerification.Bottom(f[0]) > apex) { apex = InteractionVerification.Bottom(f[0]); highest = f; }
                }
                if (s.Error != 0) break;
            }
            bool passed = s.Error == 0 && apex - top is >= 3 and <= 20;
            notes.Add(new { Test = "lower-ground-jump", RisePixels = groundRise, ClearancePixels = apex - top, s.Error, Passed = passed });
            if (!passed) failures++;
            if (highest is not null) CollisionWindow.SaveSnapshot(path, highest, Path.Combine(output, "lower-jump.png"));
        }
        foreach (int direction in new[] { 1, -1 })
        {
            using var s = new CollisionSession(path);
            if (broken) s.DisableDampingForTest();
            int active = direction == 1 ? 0 : 1, passive = 1 - active;
            double initial = 0, lastStart = 0, lastEnd = 0, maximum = 0, activeStart = 0, activeEnd = 0;
            for (int i = 0; i < 1800; i++)
            {
                var c = new float[12]; if (i is >= 600 and < 1500) c[active * 6] = direction;
                if (i is >= 1550 and < 1650) c[passive * 6] = direction;
                var f = s.Step(c);
                if (i == 599) initial = f[passive].CoreX * 80d;
                if (i is >= 600 and < 1500) maximum = Math.Max(maximum, Math.Abs(f[passive].CoreX * 80d - initial));
                if (i == 1200) lastStart = f[passive].CoreX * 80d;
                if (i == 1499) { lastEnd = f[passive].CoreX * 80d; CollisionWindow.SaveSnapshot(path, f, Path.Combine(output, $"push-{direction}.png")); }
                if (i == 1549) activeStart = f[passive].CoreX * 80d;
                if (i == 1649) activeEnd = f[passive].CoreX * 80d;
                if (s.Error != 0) break;
            }
            bool passed = s.Error == 0 && maximum <= 8 && Math.Abs(lastEnd - lastStart) <= .8 && Math.Abs(activeEnd - activeStart) > 15;
            notes.Add(new { Test = "idle-push", Direction = direction, MaximumDisplacementPixels = maximum, LateDriftPixels = Math.Abs(lastEnd - lastStart), ActiveMovementPixels = Math.Abs(activeEnd - activeStart), s.Error, Passed = passed });
            if (!passed) failures++;
        }
        // Same natural falling stack, with/without a jump request on first supported frame.
        var reference = new List<Box2DSelfSoftBodyNativeFrame[]>(); int trigger = -1;
        using (var s = new CollisionSession(path, 2, 1))
        {
            for (int i = 0; i < 360; i++)
            {
                var f = s.Step(new float[12]); reference.Add(f);
                if (trigger < 0 && i >= 30 && s.ContactSteps > 0 && f[1].CoreY > f[0].CoreY + .5 && CollisionVerification.VisibleGap(f) < .8) trigger = i + 1;
                if (s.Error != 0) break;
            }
        }
        using (var s = new CollisionSession(path, 2, 1))
        {
            if (broken) s.SetInteractionTestFlags(8);
            double initial = 0, rise = 0, recoil = 0, departure = 0; Box2DSelfSoftBodyNativeFrame[]? highest = null;
            for (int i = 0; i < reference.Count; i++)
            {
                var c = new float[12]; c[8] = i == trigger ? 1 : 0;
                var f = s.Step(c);
                if (i == trigger - 1) initial = f[1].CoreY * 60d;
                if (trigger >= 0 && i >= trigger)
                {
                    if (f[1].CoreY * 60d - initial > rise) { rise = f[1].CoreY * 60d - initial; highest = f; }
                    if (i < trigger + 20) recoil = Math.Max(recoil, (reference[i][0].CoreY - f[0].CoreY) * 60d);
                    if (i < trigger + 60) departure = Math.Max(departure, CollisionVerification.VisibleGap(f));
                }
                if (s.Error != 0) break;
            }
            var ratio = rise / groundRise;
            bool passed = trigger >= 0 && s.Error == 0 && ratio is > .35 and < .8 && departure > 10 && recoil > .5;
            notes.Add(new { Test = "supported-jump", TriggerFrame = trigger, RisePixels = rise, HeightRatio = ratio, DownwardReactionPixels = recoil, SeparationPixels = departure, s.Error, Passed = passed });
            if (!passed) failures++;
            if (highest is not null) CollisionWindow.SaveSnapshot(path, highest, Path.Combine(output, "supported-jump.png"));
        }
        using var report = new FileStream(Path.Combine(output, "support.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(report, new { Failures = failures, Tests = notes }, new JsonSerializerOptions { WriteIndented = true });
        if (failures != 0) throw new InvalidDataException("Support acceptance failed; see support.json");
    }
}
