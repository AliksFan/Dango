namespace Dango.Desktop.Accessories.Editing;

/// <summary>A stable editing identity and rest position in output-image pixels.</summary>
public sealed record AccessorySkeletonNode(string Id, double X, double Y);

/// <param name="Bend">Maximum relative angle deviation, as a fraction of 162 degrees.</param>
/// <param name="Damping">Velocity decay rate in inverse seconds.</param>
/// <param name="Restoration">Rest-angle and rest-length recovery frequency in radians per second.</param>
/// <param name="Gravity">0..100 control value; 100 maps to 4 image heights per second squared, downward in world space.</param>
/// <param name="Elasticity">Symmetric permitted fractional length change; zero fixes bone length.</param>
public sealed record AccessorySkeletonParameters(double Bend = .7, double Damping = 6,
    double Restoration = 5, double Gravity = 0, double Elasticity = 0)
{
    public void Validate()
    {
        if (!InRange(Bend, 1) || !InRange(Damping, 30) || !InRange(Restoration, 30) ||
            !InRange(Gravity, 100) || !InRange(Elasticity, .5))
            throw new ArgumentException("Skeleton parameters exceed their finite supported ranges.");
    }

    private static bool InRange(double value, double maximum) => double.IsFinite(value) && value >= 0 && value <= maximum;
}

/// <summary>Nodes[0] is the group's one root. TargetId references another group's node, never P.</summary>
public sealed record AccessorySkeletonGroup(string Id, string Name, string? TargetId,
    IReadOnlyList<AccessorySkeletonNode> Nodes, AccessorySkeletonParameters Parameters)
{
    public bool Equals(AccessorySkeletonGroup? other) => other is not null && Id == other.Id &&
        Name == other.Name && TargetId == other.TargetId && Parameters == other.Parameters && Nodes.SequenceEqual(other.Nodes);

    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Id); hash.Add(Name); hash.Add(TargetId); hash.Add(Parameters);
        foreach (var node in Nodes) hash.Add(node);
        return hash.ToHashCode();
    }
}

public sealed record AccessorySkeletonLayer(string Id, string? GroupId);

/// <summary>Independent skeleton schema; it does not reinterpret or migrate legacy rig schemas.</summary>
public sealed record AccessorySkeletonConfiguration(AccessorySkeletonNode Pivot,
    IReadOnlyList<AccessorySkeletonGroup> Groups, IReadOnlyList<AccessorySkeletonLayer> Layers, int Version = 1)
{
    public const int MaximumGroups = 12;
    /// <summary>Total editing nodes, including P and each group's distinct root identity.</summary>
    public const int MaximumNodes = 96;
    public const int SupportedVersion = 1;

    public bool Equals(AccessorySkeletonConfiguration? other) => other is not null && Version == other.Version &&
        Pivot == other.Pivot && Groups.SequenceEqual(other.Groups) && Layers.SequenceEqual(other.Layers);

    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Version); hash.Add(Pivot);
        foreach (var group in Groups) hash.Add(group);
        foreach (var layer in Layers) hash.Add(layer);
        return hash.ToHashCode();
    }

    public void Validate(int width, int height)
    {
        if (width is < 1 or > 4096 || height is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be between 1 and 4096 pixels.");
        if (Version != SupportedVersion || Pivot is null || Pivot.Id != "P" || Groups is null || Layers is null || Groups.Count > MaximumGroups)
            throw new ArgumentException("Unsupported or incomplete skeleton configuration.");

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var groups = new Dictionary<string, AccessorySkeletonGroup>(StringComparer.Ordinal);
        var owners = new Dictionary<string, AccessorySkeletonGroup>(StringComparer.Ordinal);
        CheckNode(Pivot);
        var count = 1;
        foreach (var group in Groups)
        {
            if (group is null || !ValidId(group.Id) || string.IsNullOrWhiteSpace(group.Name) || group.Name.Length > 128 ||
                group.Name.Any(char.IsControl) || group.Name.Contains('<') || group.Name.Contains('>') ||
                group.Nodes is null || group.Nodes.Count == 0 || group.Parameters is null || !groups.TryAdd(group.Id, group))
                throw new ArgumentException("Each uniquely identified group must have exactly one root and valid parameters.");
            count += group.Nodes.Count;
            if (count > MaximumNodes) throw new ArgumentException("The total skeleton node budget was exceeded.");
            group.Parameters.Validate();
            for (var i = 0; i < group.Nodes.Count; i++)
            {
                var node = group.Nodes[i];
                CheckNode(node);
                owners.Add(node.Id, group);
                if (i > 0 && DistanceSquared(node, group.Nodes[i - 1]) <= 1e-12)
                    throw new ArgumentException("Consecutive skeleton nodes must have nonzero bone length.");
            }
        }

        foreach (var group in Groups)
        {
            if (group.TargetId is null) continue;
            if (!owners.TryGetValue(group.TargetId, out var targetOwner) || ReferenceEquals(group, targetOwner))
                throw new ArgumentException("A root target must be an existing node of another group.");
            var target = targetOwner.Nodes.First(n => n.Id == group.TargetId);
            if (DistanceSquared(group.Nodes[0], target) > 1e-12)
                throw new ArgumentException("An attached root and its target must have the same rest position.");
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in Groups) Visit(group);
        var layerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in Layers)
            if (layer is null || !ValidId(layer.Id) || !layerIds.Add(layer.Id) ||
                (layer.GroupId is not null && !groups.ContainsKey(layer.GroupId)))
                throw new ArgumentException("Layers require unique identities and existing groups, or no group.");

        void CheckNode(AccessorySkeletonNode node)
        {
            if (node is null || !ValidId(node.Id) || !nodeIds.Add(node.Id) ||
                !double.IsFinite(node.X) || !double.IsFinite(node.Y) || node.X < 0 || node.X > width || node.Y < 0 || node.Y > height)
                throw new ArgumentException("Skeleton nodes require unique identities and finite output-image coordinates.");
        }

        void Visit(AccessorySkeletonGroup group)
        {
            if (done.Contains(group.Id)) return;
            if (!visiting.Add(group.Id)) throw new ArgumentException("Root attachment references must be acyclic.");
            if (group.TargetId is not null) Visit(owners[group.TargetId]);
            visiting.Remove(group.Id);
            done.Add(group.Id);
        }
    }

    /// <summary>Deep collection copy for ownership transfer. Record 'with' alone does not isolate mutable input lists.</summary>
    public AccessorySkeletonConfiguration Snapshot() => this with
    {
        Groups = Array.AsReadOnly(Groups.Select(group => group with
        {
            Nodes = Array.AsReadOnly(group.Nodes.ToArray())
        }).ToArray()),
        Layers = Array.AsReadOnly(Layers.ToArray())
    };

    private static double DistanceSquared(AccessorySkeletonNode a, AccessorySkeletonNode b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
    private static bool ValidId(string? id) => id is { Length: > 0 and <= 64 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
