using System.IO;
using Dango.Desktop.Accessories.Rig;
using Point = System.Windows.Point;

namespace Dango.Desktop.Accessories.Editing;

public sealed record AccessoryRigNode(double X, double Y);

/// <summary>Only rest geometry and parameters are saved; derived skin and motion are rebuilt.</summary>
public sealed record AccessoryRigConfiguration
{
    public IReadOnlyList<AccessoryRigNode> Nodes { get; }
    public double Softness { get; }
    public double Damping { get; }
    public double Restoration { get; }
    public int BindingVersion { get; }

    public AccessoryRigConfiguration(IReadOnlyList<AccessoryRigNode> nodes, double softness = .7,
        double damping = 6, double restoration = 5, int bindingVersion = AccessoryRigLimits.BindingAlgorithmVersion)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        Nodes = Array.AsReadOnly(nodes.Take(AccessoryRigLimits.MaximumNodes + 1).ToArray());
        Softness = softness; Damping = damping; Restoration = restoration; BindingVersion = bindingVersion;
    }

    public AccessoryRigDefinition CreateDefinition(int width, int height) =>
        new(Nodes.Select(n => new Point(n.X, n.Y)), (double)width / height);

    public AccessoryRigParameters CreateParameters(double gravityInfluence) =>
        new(Softness, Damping, Restoration, gravityInfluence / 100 * 4);

    public void Validate(int width, int height, double pivotX, double pivotY)
    {
        if (BindingVersion != AccessoryRigLimits.BindingAlgorithmVersion || Nodes.Any(n => n is null) ||
            Nodes.Count == 0 || Nodes[0].X != pivotX || Nodes[0].Y != pivotY)
            throw new InvalidDataException("骨架根部必须等于图片支点，且绑定版本有效。");
        // Valid geometry can still produce ambiguous skin weights (for example a close U turn).
        try
        {
            _ = new AccessoryRigSkin(CreateDefinition(width, height));
            CreateParameters(0).Validate();
        }
        catch (ArgumentException error) { throw new InvalidDataException("骨架或蒙皮绑定无效，请调整中心线。", error); }
    }

    public bool Equals(AccessoryRigConfiguration? other) => other is not null &&
        Softness == other.Softness && Damping == other.Damping && Restoration == other.Restoration &&
        BindingVersion == other.BindingVersion && Nodes.SequenceEqual(other.Nodes);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Softness); hash.Add(Damping); hash.Add(Restoration); hash.Add(BindingVersion);
        foreach (var node in Nodes) hash.Add(node);
        return hash.ToHashCode();
    }
}
