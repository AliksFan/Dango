using System.Windows;
using Point = System.Windows.Point;

namespace Dango.Desktop.Accessories.Rig;

public readonly record struct AccessoryRigWeight(int FirstBone, int SecondBone, double FirstWeight)
{
    public double SecondWeight => 1 - FirstWeight;
}

/// <summary>Immutable bounded topology, UVs and neighboring segment weights in aspect-correct image-height units.
/// Folded/self-intersecting centerlines are conservatively rejected instead of silently generating a bad binding.</summary>
public sealed class AccessoryRigSkin
{
    private readonly AccessoryRigDefinition definition;
    public IReadOnlyList<Point> TextureCoordinates { get; }
    public IReadOnlyList<int> TriangleIndices { get; }
    public IReadOnlyList<AccessoryRigWeight> Weights { get; }
    public int PivotVertexIndex { get; }

    public AccessoryRigSkin(AccessoryRigDefinition definition)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        // Detect opposing overlapping limbs analytically, including bisectors missed by the finite grid.
        for (var i = 0; i < definition.BoneLengths.Count; i++)
        for (var j = i + 2; j < definition.BoneLengths.Count; j++)
        {
            var a = definition.LocalNodes[i]; var direction = definition.LocalNodes[i + 1] - a;
            var other = definition.LocalNodes[j + 1] - definition.LocalNodes[j];
            var dot = Vector.Multiply(direction, other) / (direction.Length * other.Length);
            var first = Vector.Multiply(definition.LocalNodes[j] - a, direction) / direction.LengthSquared;
            var second = Vector.Multiply(definition.LocalNodes[j + 1] - a, direction) / direction.LengthSquared;
            if (dot < -0.85 && Math.Max(0, Math.Min(first, second)) < Math.Min(1, Math.Max(first, second)))
                throw new ArgumentException("Opposing overlapping limbs have ambiguous skinning; adjust the centerline.");
        }
        var pivot = definition.RestNodes[0];
        var xs = Axis(pivot.X); var ys = Axis(pivot.Y);
        var vertices = new Point[xs.Length * ys.Length];
        var weights = new AccessoryRigWeight[vertices.Length];
        for (var y = 0; y < ys.Length; y++)
        for (var x = 0; x < xs.Length; x++)
        {
            var index = y * xs.Length + x;
            vertices[index] = new(xs[x], ys[y]);
            weights[index] = Bind(vertices[index]);
            if (vertices[index] == pivot) PivotVertexIndex = index;
        }
        var triangles = new int[(xs.Length - 1) * (ys.Length - 1) * 6];
        var next = 0;
        for (var y = 0; y < ys.Length - 1; y++)
        for (var x = 0; x < xs.Length - 1; x++)
        {
            var a = y * xs.Length + x;
            triangles[next++] = a; triangles[next++] = a + 1; triangles[next++] = a + xs.Length;
            triangles[next++] = a + 1; triangles[next++] = a + xs.Length + 1; triangles[next++] = a + xs.Length;
        }
        TextureCoordinates = Array.AsReadOnly(vertices);
        TriangleIndices = Array.AsReadOnly(triangles);
        Weights = Array.AsReadOnly(weights);
    }

    public AccessoryMeshFrame Deform(AccessoryRigSnapshot frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.Pose.Validate();
        if (!ReferenceEquals(frame.Definition, definition) || frame.Nodes.Count != definition.LocalNodes.Count || frame.Nodes.Any(p => !AccessoryRigDefinition.Finite(p)))
            throw new ArgumentException("Snapshot does not belong to this skin's finite rest topology.", nameof(frame));
        var rotations = new double[definition.BoneLengths.Count];
        for (var i = 0; i < rotations.Length; i++)
        {
            var rest = definition.LocalNodes[i + 1] - definition.LocalNodes[i];
            var current = frame.Nodes[i + 1] - frame.Nodes[i];
            rotations[i] = Math.Atan2(current.Y, current.X) - Math.Atan2(rest.Y, rest.X);
        }
        var vertices = new Point[TextureCoordinates.Count];
        var bounds = Rect.Empty;
        var restPose = Enumerable.Range(0, frame.Nodes.Count).All(i => frame.Nodes[i] == frame.Pose.Transform(definition.LocalNodes[i], definition.LocalNodes[0]));
        var identity = restPose && frame.Pose.Root == definition.LocalNodes[0] && frame.Pose.Angle == 0 && frame.Pose.Scale == 1;
        for (var i = 0; i < vertices.Length; i++)
        {
            var weight = Weights[i];
            var uv = TextureCoordinates[i];
            var a = Transform(weight.FirstBone, uv);
            var b = Transform(weight.SecondBone, uv);
            vertices[i] = i == PivotVertexIndex ? frame.Pose.Root : identity ? definition.ToLocal(uv) : restPose
                ? frame.Pose.Transform(definition.ToLocal(uv), definition.LocalNodes[0]) : a + (b - a) * weight.SecondWeight;
            bounds.Union(vertices[i]);
        }
        return new(frame.Tick, Array.AsReadOnly(vertices), TextureCoordinates, TriangleIndices, bounds,
            frame.Pose.Root, new Vector(), frame.Pose.Angle * 180 / Math.PI) { AllowFolding = true };

        Point Transform(int bone, Point uv) => frame.Nodes[bone] + AccessoryRigDefinition.Rotate((definition.ToLocal(uv) - definition.LocalNodes[bone]) * frame.Pose.Scale, rotations[bone]);
    }

    private AccessoryRigWeight Bind(Point point)
    {
        point = definition.ToLocal(point);
        Span<double> distances = stackalloc double[AccessoryRigLimits.MaximumNodes - 1];
        var best = 0; var bestDistance = double.PositiveInfinity; var bestT = 0d;
        for (var i = 0; i < definition.BoneLengths.Count; i++)
        {
            var start = definition.LocalNodes[i]; var delta = definition.LocalNodes[i + 1] - start;
            var t = Math.Clamp(Vector.Multiply(point - start, delta) / delta.LengthSquared, 0, 1);
            var distance = (point - (start + delta * t)).Length;
            distances[i] = distance;
            if (distance < bestDistance) { best = i; bestT = t; bestDistance = distance; }
        }
        for (var i = 0; i < definition.BoneLengths.Count; i++)
            if (Math.Abs(i - best) > 1 && Math.Abs(distances[i] - bestDistance) < 1e-8)
                throw new ArgumentException("Nonadjacent bones produce ambiguous image binding; adjust the centerline.");
        if (bestT < 0.5 && best > 0) return new(best - 1, best, 0.5 - bestT);
        if (bestT > 0.5 && best < definition.BoneLengths.Count - 1) return new(best, best + 1, 1.5 - bestT);
        return new(best, best, 1);
    }

    private static double[] Axis(double pivot) => Enumerable.Range(0, AccessoryRigLimits.GridSegments + 1)
        .Select(i => (double)i / AccessoryRigLimits.GridSegments)
        .Select(value => Math.Abs(value - pivot) < 1e-8 ? pivot : value).Append(pivot).Distinct().Order().ToArray();
}
