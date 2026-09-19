using Dango.Core.Animation;

namespace Dango.Desktop.Physics.Presentation.Attachments;

/// <summary>Authored eye paths. Nominal coordinates are 12 DIP; openness changes paths, not their height scale.</summary>
public sealed record EyeArtwork(IReadOnlyList<IReadOnlyList<AttachmentPoint>> Fills, IReadOnlyList<IReadOnlyList<AttachmentPoint>> Holes)
{
    private static AttachmentPoint P(double x, double y) => new(x, y);
    private static readonly AttachmentPoint[] Open = Circle(6);
    private static readonly AttachmentPoint[] Closed = Curve(
        P(-5.75, 1), P(-5.75, 0.2), P(-2, -1.25), P(0, -1.25),
        P(2, -1.25), P(5.75, 0.2), P(5.75, 1),
        P(5.75, 1.8), P(2, 0.25), P(0, 0.25),
        P(-2, 0.25), P(-5.75, 1.8), P(-5.75, 1)).Select(p => p + P(0, 3.5)).ToArray();
    // Mirror the resting curve vertically, preserving winding and the first contour point.
    private static readonly AttachmentPoint[] SleepingClosed = Enumerable.Range(0, 128)
        .Select(i => { var p = Closed[(128 - i) % 128]; return P(p.X, 7 - p.Y); }).ToArray();
    // Meet in the lower half of the open eye; avoid lifting the lid during the final closure.
    private static readonly AttachmentPoint[] Nearly = Four(P(-5, 1.7), P(-2, -0.3), P(2, -0.3), P(5, 1.7), P(3.6, 4.3), P(-3.6, 4.3), P(-5, 1.7)).Select(p => p + P(0, 1.5)).ToArray();
    private static readonly AttachmentPoint[] Half = Four(P(-6, 0), P(-3, -0.7), P(3, -0.7), P(6, 0),
        P(6, 3.314), P(3.314, 6), P(0, 6), P(-3.314, 6), P(-6, 3.314), P(-6, 0));
    private static readonly AttachmentPoint[] Light = Four(P(-5.2, -3), P(-2, -4), P(2, -4), P(5.2, -3),
        P(7, 0), P(5, 6), P(0, 6), P(-5, 6), P(-7, 0), P(-5.2, -3));
    private static readonly AttachmentPoint[] Angry = Four(P(-5, -3), P(-2, -2.1), P(2, -0.9), P(5, 0), P(3, 5), P(-4, 5), P(-5, -3));

    public static EyeArtwork Create(EyePresentationFrame eye)
    {
        var fills = new List<AttachmentPoint[]>(); var holes = new List<AttachmentPoint[]>();
        var openness = eye.Openness;
        switch (eye.Shape)
        {
            case EyeShape.Sleeping:
                fills.Add(Mix(SleepingClosed, Normal(openness), Math.Min(1, openness / .2)));
                break;
            case EyeShape.NaturalClosed: fills.Add(Closed); break;
            case EyeShape.Tight:
                var sign = eye.IsRight ? -1 : 1;
                fills.Add(Stroke([P(-4 * sign, -4), P(4 * sign, 0)]));
                fills.Add(Stroke([P(4 * sign, 0), P(-4 * sign, 4)]));
                break;
            case EyeShape.Happy:
                fills.Add(Stroke(Enumerable.Range(0, 65).Select(i => Cubic(P(-5, 2), P(-2, -5), P(2, -5), P(5, 2), i / 64d)).ToArray()));
                break;
            case EyeShape.Deadpan: fills.Add(Stroke([P(-4, 0), P(4, 0)])); break;
            case EyeShape.Angry:
                // Mirror the authored lid, but keep the same natural closing curve.
                var angry = eye.IsRight ? Enumerable.Range(0, 128).Select(i =>
                { var p = Angry[(64 - i + 128) % 128]; return P(-p.X, p.Y); }).ToArray() : Angry;
                fills.Add(Mix(Closed, angry, openness)); break;
            case EyeShape.Surprised:
                fills.Add(Mix(Closed, Circle(5.75), openness));
                if (openness > 0) holes.Add(Circle(4.25 * Math.Pow(openness, 3)).Select(p => p + P(0, -0.5 * (1 - openness))).ToArray());
                break;
            default:
                var phase = eye.Shape == EyeShape.Soft ? openness * (1 - 0.3 * eye.Strength) : eye.Shape == EyeShape.Sleepy ? openness * 0.5 : openness;
                var path = Normal(phase);
                if (eye.Shape == EyeShape.Puzzled)
                    path = path.Select(p => eye.IsRight ? p * (2d / 3) + P(0, -2) : p).ToArray();
                fills.Add(path); break;
        }
        IReadOnlyList<IReadOnlyList<AttachmentPoint>> Map(List<AttachmentPoint[]> paths) => Array.AsReadOnly(paths.Select(path =>
            (IReadOnlyList<AttachmentPoint>)Array.AsReadOnly(path.Select(p =>
            {
                var x = p.X / 12 * eye.Width; var y = -p.Y / 12 * eye.Height;
                return eye.Center + P(x * Math.Cos(eye.Angle) - y * Math.Sin(eye.Angle), x * Math.Sin(eye.Angle) + y * Math.Cos(eye.Angle));
            }).ToArray())).ToArray());
        return new(Map(fills), Map(holes));
    }

    private static AttachmentPoint[] Normal(double openness)
    {
        var keys = new[] { 0d, 0.2, 0.5, 0.75, 1 };
        var paths = new[] { Closed, Nearly, Half, Light, Open };
        for (var i = 1; i < keys.Length; i++)
            if (openness <= keys[i])
            {
                var t = (openness - keys[i - 1]) / (keys[i] - keys[i - 1]);
                return Mix(paths[i - 1], paths[i], t * t * (3 - 2 * t));
            }
        return Open;
    }
    private static AttachmentPoint[] Mix(AttachmentPoint[] a, AttachmentPoint[] b, double t) => a.Select((p, i) => p * (1 - t) + b[i] * t).ToArray();
    private static AttachmentPoint Cubic(AttachmentPoint a, AttachmentPoint b, AttachmentPoint c, AttachmentPoint d, double t) =>
        a * Math.Pow(1 - t, 3) + b * (3 * t * Math.Pow(1 - t, 2)) + c * (3 * t * t * (1 - t)) + d * (t * t * t);
    private static AttachmentPoint[] Curve(params AttachmentPoint[] points) => Enumerable.Range(0, 128).Select(i =>
    {
        var segment = i / 32; var t = (i % 32) / 32d;
        return Cubic(points[segment * 3], points[segment * 3 + 1], points[segment * 3 + 2], points[segment * 3 + 3], t);
    }).ToArray();
    // Split authored two/three-segment paths into four matching sections; preserve sharp lid endpoints.
    private static AttachmentPoint[] Four(params AttachmentPoint[] p)
    {
        var result = new List<AttachmentPoint> { p[0] };
        for (var s = 0; s < (p.Length - 1) / 3; s++)
        {
            var a = p[s * 3]; var b = p[s * 3 + 1]; var c = p[s * 3 + 2]; var d = p[s * 3 + 3];
            if (p.Length == 7 || s == 0)
            {
                var ab = (a + b) * 0.5; var bc = (b + c) * 0.5; var cd = (c + d) * 0.5;
                var abc = (ab + bc) * 0.5; var bcd = (bc + cd) * 0.5; var middle = (abc + bcd) * 0.5;
                result.AddRange([ab, abc, middle, bcd, cd, d]);
            }
            else result.AddRange([b, c, d]);
        }
        return Curve(result.ToArray());
    }
    private static AttachmentPoint[] Circle(double radius) => Enumerable.Range(0, 128).Select(i =>
        P(radius * Math.Cos(Math.PI + i * Math.PI / 64), radius * Math.Sin(Math.PI + i * Math.PI / 64))).ToArray();
    private static AttachmentPoint[] Stroke(AttachmentPoint[] line)
    {
        const double radius = 0.75;
        var tangents = line.Select((_, i) => line[Math.Min(i + 1, line.Length - 1)] - line[Math.Max(0, i - 1)]).ToArray();
        var normals = tangents.Select(t => P(-t.Y, t.X) * (radius / t.Length)).ToArray();
        var result = line.Select((p, i) => p - normals[i]).ToList();
        void Cap(AttachmentPoint center, double start)
        {
            for (var i = 1; i <= 32; i++) result.Add(center + P(Math.Cos(start + Math.PI * i / 32), Math.Sin(start + Math.PI * i / 32)) * radius);
        }
        Cap(line[^1], Math.Atan2(tangents[^1].Y, tangents[^1].X) - Math.PI / 2);
        for (var i = line.Length - 2; i >= 0; i--) result.Add(line[i] + normals[i]);
        Cap(line[0], Math.Atan2(tangents[0].Y, tangents[0].X) + Math.PI / 2);
        return result.ToArray();
    }
}
