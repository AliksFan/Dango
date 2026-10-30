using System.Windows;
using Dango.Desktop.Accessories.Editing;
using Point = System.Windows.Point;

namespace Dango.Desktop.Accessories.Rig;

public readonly record struct AccessorySkeletonWeight(int FirstBone, int SecondBone, double FirstWeight)
{
    public double SecondWeight => 1 - FirstWeight;
}

/// <summary>
/// Bounded polyline skin for one complete-canvas render piece. Nearest two clamped segment distances
/// determine weights; unclamped axial coordinates and signed normal distances preserve image rest shape.
/// Crossings, hairpins and short nonzero segments are accepted, with deterministic ties by bone index.
/// </summary>
public sealed class AccessorySkeletonSkin
{
    public const int GridDivisions = 15;
    public const int MaximumRenderPieces = 16;
    public const int MaximumVerticesPerPiece = 256;
    public const int MaximumTotalVertices = MaximumRenderPieces * MaximumVerticesPerPiece;
    public const int MaximumTotalTriangles = MaximumRenderPieces * GridDivisions * GridDivisions * 2;
    private readonly AccessorySkeletonConfiguration configuration;
    private readonly AccessorySkeletonGroup? group;
    private readonly double aspect;
    private readonly int height;
    private readonly Binding[] bindings;
    public string? GroupId { get; }
    public IReadOnlyList<Point> TextureCoordinates { get; }
    public IReadOnlyList<int> TriangleIndices { get; }
    public IReadOnlyList<AccessorySkeletonWeight> Weights { get; }

    public AccessorySkeletonSkin(AccessorySkeletonConfiguration configuration, int width, int height, string? groupId)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate(width, height);
        this.configuration = configuration.Snapshot(); this.height = height; aspect = width / (double)height;
        GroupId = groupId;
        group = groupId is null ? null : this.configuration.Groups.FirstOrDefault(candidate => candidate.Id == groupId)
            ?? throw new ArgumentException("The skin's group does not exist.", nameof(groupId));
        var uvs = new Point[MaximumVerticesPerPiece];
        var triangles = new int[GridDivisions * GridDivisions * 6];
        var weights = new AccessorySkeletonWeight[uvs.Length];
        bindings = new Binding[uvs.Length];
        for (var y = 0; y <= GridDivisions; y++)
        for (var x = 0; x <= GridDivisions; x++)
        {
            var index = y * (GridDivisions + 1) + x;
            uvs[index] = new(x / (double)GridDivisions, y / (double)GridDivisions);
            if (group is { Nodes.Count: > 1 }) bindings[index] = Bind(ToLocal(uvs[index]));
            weights[index] = bindings[index].Weight;
        }
        var next = 0;
        for (var y = 0; y < GridDivisions; y++)
        for (var x = 0; x < GridDivisions; x++)
        {
            var a = y * (GridDivisions + 1) + x; var b = a + 1;
            var c = a + GridDivisions + 1; var d = c + 1;
            triangles[next++] = a; triangles[next++] = b; triangles[next++] = d;
            triangles[next++] = a; triangles[next++] = d; triangles[next++] = c;
        }
        TextureCoordinates = Array.AsReadOnly(uvs); TriangleIndices = Array.AsReadOnly(triangles);
        Weights = group is { Nodes.Count: > 1 } ? Array.AsReadOnly(weights) : Array.Empty<AccessorySkeletonWeight>();
    }

    public AccessoryMeshFrame Deform(AccessorySkeletonSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Pose.Validate();
        if (snapshot.Configuration.Pivot != configuration.Pivot)
            throw new ArgumentException("The skeleton pivot no longer matches this binding.", nameof(snapshot));
        IReadOnlyList<Point>? nodes = null;
        AccessoryRigPose groupPose = snapshot.Pose;
        if (group is not null)
        {
            var definition = snapshot.Configuration.Groups.FirstOrDefault(candidate => candidate.Id == GroupId);
            if (definition is null || !definition.Nodes.SequenceEqual(group.Nodes) ||
                !snapshot.Groups.TryGetValue(group.Id, out nodes) || nodes.Count != group.Nodes.Count ||
                nodes.Any(point => !AccessoryRigDefinition.Finite(point)) || !snapshot.GroupPoses.TryGetValue(group.Id, out groupPose))
                throw new ArgumentException("The skeleton frame no longer matches this group's binding.", nameof(snapshot));
            groupPose.Validate();
        }
        var vertices = new Point[TextureCoordinates.Count]; var bounds = Rect.Empty;
        for (var i = 0; i < vertices.Length; i++)
        {
            var local = ToLocal(TextureCoordinates[i]);
            if (group is null)
                vertices[i] = snapshot.Pose.Root + AccessoryRigDefinition.Rotate((local - Pixel(configuration.Pivot)) * snapshot.Pose.Scale, snapshot.Pose.Angle);
            else if (group.Nodes.Count == 1)
                vertices[i] = groupPose.Root + AccessoryRigDefinition.Rotate((local - Pixel(group.Nodes[0])) * groupPose.Scale, groupPose.Angle);
            else
            {
                var binding = bindings[i];
                var first = Transform(binding.Weight.FirstBone, binding.First);
                var second = Transform(binding.Weight.SecondBone, binding.Second);
                vertices[i] = first + (second - first) * binding.Weight.SecondWeight;
            }
            if (!AccessoryRigDefinition.Finite(vertices[i])) throw new ArithmeticException("Skeleton skin produced a nonfinite vertex.");
            bounds.Union(vertices[i]);
        }
        return new(snapshot.Tick, Array.AsReadOnly(vertices), TextureCoordinates, TriangleIndices, bounds,
            snapshot.Pose.Root, default, snapshot.Pose.Angle * 180 / Math.PI) { AllowFolding = group is not null };

        Point Transform(int bone, Coordinates coordinates)
        {
            var delta = nodes![bone + 1] - nodes[bone]; var length = delta.Length;
            var normal = length > 1e-15 ? new Vector(-delta.Y / length, delta.X / length) :
                AccessoryRigDefinition.Rotate(new(0, 1), groupPose.Angle);
            return nodes[bone] + delta * coordinates.Axial + normal * (coordinates.Normal * snapshot.Pose.Scale);
        }
    }

    private Binding Bind(Point point)
    {
        var first = 0; var second = 0; var firstDistance = double.PositiveInfinity; var secondDistance = double.PositiveInfinity;
        for (var bone = 0; bone < group!.Nodes.Count - 1; bone++)
        {
            var start = Pixel(group.Nodes[bone]); var delta = Pixel(group.Nodes[bone + 1]) - start;
            var t = Math.Clamp(Vector.Multiply(point - start, delta) / delta.LengthSquared, 0, 1);
            var distance = (point - (start + delta * t)).Length;
            if (distance < firstDistance)
            { second = first; secondDistance = firstDistance; first = bone; firstDistance = distance; }
            else if (distance < secondDistance) { second = bone; secondDistance = distance; }
        }
        if (!double.IsFinite(secondDistance)) { second = first; secondDistance = firstDistance; }
        var denominator = firstDistance + secondDistance;
        var weight = denominator < 1e-14 ? .5 : secondDistance / denominator;
        return new(new(first, second, weight), Coordinate(first), Coordinate(second));

        Coordinates Coordinate(int bone)
        {
            var start = Pixel(group.Nodes[bone]); var delta = Pixel(group.Nodes[bone + 1]) - start;
            return new(Vector.Multiply(point - start, delta) / delta.LengthSquared,
                Vector.CrossProduct(delta / delta.Length, point - start));
        }
    }

    private Point Pixel(AccessorySkeletonNode node) => new(node.X / height, node.Y / height);
    private Point ToLocal(Point uv) => new(uv.X * aspect, uv.Y);
    private readonly record struct Coordinates(double Axial, double Normal);
    private readonly record struct Binding(AccessorySkeletonWeight Weight, Coordinates First, Coordinates Second);
}
