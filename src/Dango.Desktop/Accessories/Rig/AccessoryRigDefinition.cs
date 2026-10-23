using System.Windows;
using Point = System.Windows.Point;

namespace Dango.Desktop.Accessories.Rig;

/// <summary>E0 candidate limits. Units are normalized image coordinates and seconds; no runtime owner is added.</summary>
public static class AccessoryRigLimits
{
    public const int MaximumNodes = 17;
    public const int GridSegments = 31;
    public const int MaximumMeshVertices = 33 * 33;
    public const int MaximumMeshTriangles = 32 * 32 * 2;
    public const int MaximumSubsteps = 8;
    public const double MinimumElapsedSeconds = 0.000001;
    public const double MaximumSubstepSeconds = 1d / 120;
    public const double MaximumElapsedSeconds = MaximumSubsteps * MaximumSubstepSeconds;
    public const double MinimumBoneLength = 0.005;
    public const double MaximumSpeedInChainLengths = 8;
    public const double MaximumBendRadians = Math.PI * 0.9;
    public const int BindingAlgorithmVersion = 1;
}

/// <summary>Immutable single-chain rest geometry. Conservative invalid bindings require centerline adjustment.</summary>
public sealed class AccessoryRigDefinition
{
    public IReadOnlyList<Point> RestNodes { get; }
    /// <summary>Rest positions in image-height units, preserving actual rectangular image aspect.</summary>
    public IReadOnlyList<Point> LocalNodes { get; }
    public double ImageAspectRatio { get; }
    public IReadOnlyList<double> BoneLengths { get; }
    public double TotalLength { get; }

    public AccessoryRigDefinition(IEnumerable<Point> nodes, double imageAspectRatio = 1)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var points = nodes.Take(AccessoryRigLimits.MaximumNodes + 1).ToArray();
        if (points.Length is < 2 or > AccessoryRigLimits.MaximumNodes)
            throw new ArgumentException("A rig needs 2 through 17 nodes.", nameof(nodes));
        if (points.Any(p => !Finite(p) || p.X < 0 || p.X > 1 || p.Y < 0 || p.Y > 1))
            throw new ArgumentException("Rig nodes must be finite normalized image coordinates.", nameof(nodes));
        if (!double.IsFinite(imageAspectRatio) || imageAspectRatio < 1d / 4096 || imageAspectRatio > 4096)
            throw new ArgumentException("Image aspect must fit the supported 1..4096 output dimensions.", nameof(imageAspectRatio));
        ImageAspectRatio = imageAspectRatio;
        var normalized = points;
        points = normalized.Select(ToLocal).ToArray();
        var lengths = new double[points.Length - 1];
        for (var i = 0; i < lengths.Length; i++)
        {
            var direction = points[i + 1] - points[i];
            lengths[i] = direction.Length;
            if (lengths[i] < AccessoryRigLimits.MinimumBoneLength)
                throw new ArgumentException("A bone is zero length or below the minimum length.", nameof(nodes));
            if (i > 0)
            {
                var previous = points[i] - points[i - 1];
                if (Vector.Multiply(previous, direction) / (previous.Length * direction.Length) < -0.85)
                    throw new ArgumentException("An adjacent hairpin has ambiguous skinning; adjust the centerline.", nameof(nodes));
            }
            for (var j = 0; j < i - 1; j++)
            {
                var a = points[j]; var b = points[j + 1];
                var c = points[i]; var d = points[i + 1];
                if (Cross(b - a, c - a) * Cross(b - a, d - a) <= 0 &&
                    Cross(d - c, a - c) * Cross(d - c, b - c) <= 0 &&
                    Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) <= Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) &&
                    Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) <= Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)))
                    throw new ArgumentException("A crossing or closed centerline cannot be bound automatically.", nameof(nodes));
            }
        }
        RestNodes = Array.AsReadOnly(normalized);
        LocalNodes = Array.AsReadOnly(points);
        BoneLengths = Array.AsReadOnly(lengths);
        TotalLength = lengths.Sum();
    }

    internal static bool Finite(Point p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
    internal Point ToLocal(Point uv) => new(uv.X * ImageAspectRatio, uv.Y);
    internal static double Cross(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;
    internal static Vector Rotate(Vector v, double angle) => new(v.X * Math.Cos(angle) - v.Y * Math.Sin(angle), v.X * Math.Sin(angle) + v.Y * Math.Cos(angle));
}

/// <param name="Softness">0..1; zero fixes the exact rest curve, one allows up to 162 degrees relative bending per joint.</param>
/// <param name="Damping">Velocity decay rate, 0..30 per second.</param>
/// <param name="Restoration">Spring angular frequency, 0..30 radians per second, toward the posed rest curve.</param>
/// <param name="Gravity">Downward acceleration, 0..4 normalized image units per second squared.</param>
public sealed record AccessoryRigParameters(double Softness = 0.7, double Damping = 6, double Restoration = 5, double Gravity = 1)
{
    internal void Validate()
    {
        if (!double.IsFinite(Softness) || Softness < 0 || Softness > 1 ||
            !double.IsFinite(Damping) || Damping < 0 || Damping > 30 ||
            !double.IsFinite(Restoration) || Restoration < 0 || Restoration > 30 ||
            !double.IsFinite(Gravity) || Gravity < 0 || Gravity > 4)
            throw new ArgumentException("Rig parameters exceed their finite candidate ranges.");
    }
}

/// <summary>Root translation, radians and uniform positive scale of image-height units.</summary>
public readonly record struct AccessoryRigPose(Point Root, double Angle = 0, double Scale = 1)
{
    internal void Validate()
    {
        if (!AccessoryRigDefinition.Finite(Root) || Math.Abs(Root.X) > 1_000_000 || Math.Abs(Root.Y) > 1_000_000 ||
            !double.IsFinite(Angle) || Math.Abs(Angle) > 1_000_000 || !double.IsFinite(Scale) || Scale < 0.001 || Scale > 10_000)
            throw new ArgumentException("Invalid or excessive root pose.");
    }
    internal Point Transform(Point point, Point restRoot) => Root + AccessoryRigDefinition.Rotate((point - restRoot) * Scale, Angle);
}
