using System.IO;
using System.Text.Json;
using Dango.Desktop.Physics.Native;

namespace Dango.JointReview;

internal static class InteractionVerification
{
    internal static double[] Curve(Box2DSelfSoftBodyNativeFrame f, int axis)
    {
        var scale = Math.Min(1, .98 / f.Points.Max(v => Math.Abs(v)));
        return Enumerable.Range(0, 128).Select(s =>
        {
            int i = s / 8; double t = s % 8 / 8d;
            double P(int n) => f.Points[(n % 16) * 2 + axis];
            return (axis == 0 ? f.CoreX * 80d : f.CoreY * 60d) + (axis == 0 ? 72 : 58) * scale *
                (.5 * (1 - t) * (1 - t) * P(i + 15) + (.5 + t - t * t) * P(i) + .5 * t * t * P(i + 1));
        }).ToArray();
    }
    internal static double Top(Box2DSelfSoftBodyNativeFrame f) => Curve(f, 1).Max();
    internal static double Bottom(Box2DSelfSoftBodyNativeFrame f) => Curve(f, 1).Min();

    internal static void Run(string path, string output, bool broken = false)
    {
        Directory.CreateDirectory(output);
        var appearance = CollisionWindow.VerifyAppearance(path);
        var extraPassed = ContactInteractionVerification.Run(path, output, broken);
        using var session = new CollisionSession(path);
        double highestBottom = double.NegativeInfinity, targetTop = 0;
        Box2DSelfSoftBodyNativeFrame[]? apex = null;
        for (int i = 0; i < 840; i++)
        {
            var controls = new float[12]; controls[2] = i == 600 ? 1 : 0;
            var frame = session.Step(controls);
            if (i == 599) targetTop = Top(frame[1]);
            if (i >= 600 && Bottom(frame[0]) > highestBottom) { highestBottom = Bottom(frame[0]); apex = frame; }
            if (session.Error != 0) break;
        }
        var clearsHead = highestBottom >= targetTop + 3;
        if (apex is not null) CollisionWindow.SaveSnapshot(path, apex, Path.Combine(output, "jump-apex.png"));
        using var report = new FileStream(Path.Combine(output, "interaction.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(report, new { Appearance = appearance, ClearsHead = clearsHead, ClearancePixels = highestBottom - targetTop, session.Error }, new JsonSerializerOptions { WriteIndented = true });
        if (!appearance || !clearsHead || !extraPassed || session.Error != 0) throw new InvalidDataException("Interaction acceptance failed; see interaction.json and contact-interaction.json");
    }
}
