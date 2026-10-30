using System.Windows;
using Point = System.Windows.Point;

namespace Dango.Desktop.Accessories.Rig;

public sealed class AccessoryRigSnapshot
{
    public long Tick { get; }
    public AccessoryRigPose Pose { get; }
    public IReadOnlyList<Point> Nodes { get; }
    public AccessoryRigDefinition Definition { get; }
    internal AccessoryRigSnapshot(long tick, AccessoryRigPose pose, IReadOnlyList<Point> nodes, AccessoryRigDefinition definition)
    {
        Tick = tick; Pose = pose; Nodes = nodes; Definition = definition;
    }
}

/// <summary>Managed inertial chain candidate. Its caller supplies only successful owner frames.
/// Forward length/bend projection is bounded O(nodes * substeps), with no native body or timer.</summary>
public sealed class AccessoryRigSolver
{
    private AccessoryRigDefinition definition;
    private AccessoryRigParameters parameters;
    private Point[] positions;
    private readonly Point[] previous = new Point[AccessoryRigLimits.MaximumNodes];
    private readonly Vector[] velocity = new Vector[AccessoryRigLimits.MaximumNodes];
    private AccessoryRigPose pose;
    private long tick = -1;
    private bool initialized;
    private bool paused;

    public AccessoryRigSolver(AccessoryRigDefinition definition, AccessoryRigParameters? parameters = null)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        this.parameters = parameters ?? new(); this.parameters.Validate();
        positions = new Point[definition.LocalNodes.Count];
    }

    /// <summary>Replacing configuration/topology clears all motion and the owner tick epoch.</summary>
    public void Reset(AccessoryRigDefinition replacement, AccessoryRigParameters configuration, AccessoryRigPose root)
    {
        ArgumentNullException.ThrowIfNull(replacement); ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate(); root.Validate();
        definition = replacement; parameters = configuration;
        positions = new Point[definition.LocalNodes.Count];
        tick = -1; initialized = false; paused = false;
        SetRest(root);
    }

    public void Pause() { paused = true; Array.Clear(velocity); }

    /// <summary>Resume explicitly rebases at the current root; hidden motion and hidden elapsed time are discarded.</summary>
    public void Resume(long ownerTick, AccessoryRigPose root)
    {
        root.Validate();
        if (ownerTick < 0) throw new ArgumentOutOfRangeException(nameof(ownerTick));
        SetRest(root); tick = ownerTick; paused = false;
    }

    /// <summary>False means duplicate/backward tick, pause, or nonpositive elapsed: no state was advanced.</summary>
    public bool Advance(long ownerTick, double elapsedSeconds, AccessoryRigPose root)
    {
        root.Validate();
        if (ownerTick < 0 || !double.IsFinite(elapsedSeconds)) throw new ArgumentException("Invalid owner frame.");
        if (elapsedSeconds > 0 && elapsedSeconds < AccessoryRigLimits.MinimumElapsedSeconds)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Elapsed time is below the finite integration budget.");
        if (paused || ownerTick <= tick || elapsedSeconds <= 0) return false;
        if (!initialized || elapsedSeconds > AccessoryRigLimits.MaximumElapsedSeconds ||
            (root.Root - pose.Root).Length > definition.TotalLength * root.Scale * 2 || root.Scale != pose.Scale)
        {
            SetRest(root); tick = ownerTick; return true;
        }
        var priorPose = pose;
        var count = Math.Min(AccessoryRigLimits.MaximumSubsteps, (int)Math.Ceiling(elapsedSeconds / AccessoryRigLimits.MaximumSubstepSeconds));
        var dt = elapsedSeconds / count;
        for (var step = 1; step <= count; step++)
        {
            var fraction = (double)step / count;
            var interpolated = new AccessoryRigPose(priorPose.Root + (root.Root - priorPose.Root) * fraction,
                priorPose.Angle + Wrap(root.Angle - priorPose.Angle) * fraction, root.Scale);
            Integrate(dt, interpolated);
        }
        pose = root; tick = ownerTick; return true;
    }

    /// <summary>Immutable copy, with no elapsed time or mutation. Hosts may reuse it for any number of paints.</summary>
    public AccessoryRigSnapshot Refresh()
    {
        if (!initialized) throw new InvalidOperationException("Advance or reset the rig before reading it.");
        return new(tick, pose, Array.AsReadOnly((Point[])positions.Clone()), definition);
    }

    private void SetRest(AccessoryRigPose root)
    {
        for (var i = 0; i < positions.Length; i++) positions[i] = root.Transform(definition.LocalNodes[i], definition.LocalNodes[0]);
        Array.Clear(velocity); pose = root; initialized = true;
    }

    private void Integrate(double dt, AccessoryRigPose root)
    {
        if (parameters.Softness == 0) { SetRest(root); return; }
        Array.Copy(positions, previous, positions.Length);
        positions[0] = root.Root;
        var decay = Math.Exp(-parameters.Damping * dt);
        var maxSpeed = definition.TotalLength * root.Scale * AccessoryRigLimits.MaximumSpeedInChainLengths;
        var parentAngle = root.Angle;
        var previousRestAngle = 0d;
        for (var i = 1; i < positions.Length; i++)
        {
            var target = root.Transform(definition.LocalNodes[i], definition.LocalNodes[0]);
            var acceleration = (target - positions[i]) * (parameters.Restoration * parameters.Restoration) + new Vector(0, parameters.Gravity * root.Scale);
            var v = velocity[i] * decay + acceleration * dt;
            if (v.Length > maxSpeed) v *= maxSpeed / v.Length;
            var predicted = positions[i] + v * dt;
            var rest = definition.LocalNodes[i] - definition.LocalNodes[i - 1];
            var restAngle = Math.Atan2(rest.Y, rest.X);
            var expected = i == 1 ? root.Angle + restAngle : parentAngle + Wrap(restAngle - previousRestAngle);
            var delta = predicted - positions[i - 1];
            var angle = delta.LengthSquared < 1e-20 ? expected : Math.Atan2(delta.Y, delta.X);
            var limit = parameters.Softness * AccessoryRigLimits.MaximumBendRadians;
            angle = expected + Math.Clamp(Wrap(angle - expected), -limit, limit);
            positions[i] = positions[i - 1] + new Vector(Math.Cos(angle), Math.Sin(angle)) * (definition.BoneLengths[i - 1] * root.Scale);
            velocity[i] = (positions[i] - previous[i]) / dt;
            if (velocity[i].Length > maxSpeed) velocity[i] *= maxSpeed / velocity[i].Length;
            parentAngle = angle; previousRestAngle = restAngle;
        }
    }

    private static double Wrap(double angle) => Math.IEEERemainder(angle, Math.Tau);
}
