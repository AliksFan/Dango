using System.IO;
using System.Text.Json;
using Dango.Desktop.Physics.Native;

namespace Dango.JointReview;

internal static class ContactInteractionVerification
{
    private static double Width(Box2DSelfSoftBodyNativeFrame f) { var x = InteractionVerification.Curve(f, 0); return x.Max() - x.Min(); }
    internal static bool Run(string path, string output, bool broken = false)
    {
        var notes = new List<object>();
        int failures = 0;
        // Native leak accounting is process-wide: compare sequential worlds.
        var expected = new List<Box2DSelfSoftBodyNativeFrame>();
        using (var single = new CollisionSession(path, 1))
        {
            for (int i = 0; i < 660; i++)
            {
                var controls = new float[12]; controls[2] = i == 600 ? 1 : 0;
                expected.Add(single.Step(controls)[0]);
            }
            if (single.Error != 0) failures++;
        }
        using (var repeated = new CollisionSession(path, 1))
        {
            double maxError = 0;
            for (int i = 0; i < 660; i++)
            {
                var b = new float[12];
                b[2] = i == 600 || i is >= 610 and < 650 ? 1 : 0;
                var first = expected[i]; var second = repeated.Step(b)[0];
                maxError = Math.Max(maxError, first.Points.Zip(second.Points, (x, y) => Math.Abs((double)x - y))
                    .Append(Math.Abs(first.CoreY - second.CoreY)).Max());
            }
            bool passed = maxError <= 1e-5 && repeated.Error == 0;
            notes.Add(new { Test = "airborne-repeat-jump-rejected", MaxError = maxError, Passed = passed });
            if (!passed) failures++;
        }
        using (var session = new CollisionSession(path))
        {
            Box2DSelfSoftBodyNativeFrame[]? previous = null;
            bool landed = false, cleared = false;
            int lastContacts = 0;
            var trace = new List<object>();
            for (int i = 0; i < 1020; i++)
            {
                var controls = new float[12];
                if (i is >= 600 and < 850 && previous is not null)
                {
                    controls[0] = (float)Math.Clamp((previous[1].CoreX - previous[0].CoreX) * 1.5, -1, 1);
                    controls[2] = i == 700 ? 1 : 0;
                }
                var frame = session.Step(controls);
                if (i > 700 && InteractionVerification.Bottom(frame[0]) > InteractionVerification.Top(frame[1]) + 3) cleared = true;
                if (cleared && previous is not null && frame[0].CoreY < previous[0].CoreY &&
                    session.ContactSteps > lastContacts && frame[0].CoreY > frame[1].CoreY + .5 &&
                    Math.Abs(frame[0].CoreX - frame[1].CoreX) < .5)
                {
                    if (!landed) CollisionWindow.SaveSnapshot(path, frame, Path.Combine(output, "head-landing.png"));
                    landed = true;
                }
                if (i is >= 700 and < 850 && i % 5 == 0) trace.Add(new { Frame = i, X = frame[0].CoreX, Y = frame[0].CoreY, X2 = frame[1].CoreX, Y2 = frame[1].CoreY, session.ContactSteps });
                previous = frame; lastContacts = session.ContactSteps;
                if (session.Error != 0) break;
            }
            notes.Add(new { Test = "jump-onto-head", Cleared = cleared, Landed = landed, session.Error, Trace = trace });
            if (!landed || session.Error != 0) failures++;
        }
        var widths = new double[2];
        for (int variant = 0; variant < 2; variant++)
        {
            using var session = new CollisionSession(path);
            session.SetInteractionTestFlags(variant == 0 || broken ? 2 : 0);
            double widthSum = 0, finalGap = 0, initialWidth = 0, finalWidth = 0, centerSum = 0; int samples = 0, releaseContacts = 0;
            for (int i = 0; i < 1800; i++)
            {
                var controls = new float[12];
                if (i is >= 600 and < 900) { controls[0] = 1; controls[6] = -1; }
                if (i is >= 900 and < 960) { controls[0] = -1; controls[6] = 1; }
                var frame = session.Step(controls);
                if (i == 599) initialWidth = frame.Average(Width);
                if (i is >= 780 and < 900)
                {
                    widthSum += frame.Average(Width);
                    centerSum += Math.Sqrt(Math.Pow((frame[1].CoreX - frame[0].CoreX) * 80d, 2) + Math.Pow((frame[1].CoreY - frame[0].CoreY) * 60d, 2));
                    samples++;
                }
                if (i == 899) CollisionWindow.SaveSnapshot(path, frame, Path.Combine(output, variant == 0 ? "stiff-contact.png" : "soft-contact.png"));
                if (i == 1200) releaseContacts = session.ContactSteps;
                if (i == 1799) { finalGap = CollisionVerification.VisibleGap(frame); finalWidth = frame.Average(Width); }
                if (session.Error != 0) break;
            }
            widths[variant] = samples > 0 ? centerSum / samples : 0;
            bool released = finalGap > 20 && session.ContactSteps == releaseContacts;
            bool recovered = Math.Abs(finalWidth - initialWidth) < 3;
            notes.Add(new { Test = variant == 0 ? "stiff-control" : "soft-contact-release", MeanCoreSeparationPixels = widths[variant], MeanPressedWidth = samples > 0 ? widthSum / samples : 0, InitialWidth = initialWidth, FinalWidth = finalWidth, FinalGap = finalGap, Released = released, Recovered = recovered, session.Error });
            if (!released || !recovered || session.Error != 0) failures++;
        }
        bool softer = widths[0] - widths[1] >= 2;
        if (!softer) failures++;
        using var report = new FileStream(Path.Combine(output, "contact-interaction.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(report, new { Softer = softer, AdditionalCoreApproachPixels = widths[0] - widths[1], Failures = failures, Tests = notes }, new JsonSerializerOptions { WriteIndented = true });
        return failures == 0;
    }
}
