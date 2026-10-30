using System.Collections.ObjectModel;
using System.Windows;
using Dango.Desktop.Accessories.Editing;
using Point = System.Windows.Point;

namespace Dango.Desktop.Accessories.Rig;

public sealed class AccessorySkeletonSnapshot
{
    public long Tick { get; }
    public AccessoryRigPose Pose { get; }
    public AccessorySkeletonConfiguration Configuration { get; }
    /// <summary>World positions, with exactly coincident entries for a connected root and target.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Point>> Groups { get; }
    /// <summary>World root and rotation of the group's reference frame relative to image rest coordinates.</summary>
    public IReadOnlyDictionary<string, AccessoryRigPose> GroupPoses { get; }

    internal AccessorySkeletonSnapshot(long tick, AccessoryRigPose pose, AccessorySkeletonConfiguration configuration,
        IDictionary<string, IReadOnlyList<Point>> groups, IDictionary<string, AccessoryRigPose> groupPoses)
    {
        Tick = tick; Pose = pose; Configuration = configuration;
        Groups = new ReadOnlyDictionary<string, IReadOnlyList<Point>>(groups);
        GroupPoses = new ReadOnlyDictionary<string, AccessoryRigPose>(groupPoses);
    }
}

/// <summary>
/// A coupled position-based particle graph, advanced exclusively by successful frames from its host owner.
/// Attachment identities share one particle. Length and angular gradients distribute corrections to both
/// groups by inverse mass; reference ordering is an editing constraint, never a one-way physics traversal.
/// This solver adds no timer, native world, collision body, or feedback to the host's body.
/// </summary>
public sealed class AccessorySkeletonSolver
{
    private const int ConstraintIterations = 96;
    private AccessorySkeletonConfiguration configuration = null!;
    private Particle[] particles = [];
    private Group[] groups = [];
    private Bone[] bones = [];
    private Bone[] closureBones = [];
    private Point[] constraintPositions = [];
    private Joint[] joints = [];
    private Dictionary<string, int> nodeParticles = new(StringComparer.Ordinal);
    private AccessoryRigPose pose;
    private double totalLength;
    private long tick = -1;
    private bool initialized;
    private bool paused;
    private bool allRigid;

    public AccessorySkeletonSolver(AccessorySkeletonConfiguration configuration, int width, int height) =>
        Configure(configuration, width, height);

    /// <summary>Configuration replacement is atomic and clears motion and the owner-tick epoch.</summary>
    public void Reset(AccessorySkeletonConfiguration replacement, int width, int height, AccessoryRigPose root)
    {
        root.Validate();
        Configure(replacement, width, height);
        SetRest(root);
    }

    public void Pause()
    {
        paused = true;
        foreach (var particle in particles) particle.ClearMotion();
    }

    /// <summary>Discard hidden motion and elapsed time, and rebase at the current P pose.</summary>
    public void Resume(long ownerTick, AccessoryRigPose root)
    {
        root.Validate();
        if (ownerTick < 0) throw new ArgumentOutOfRangeException(nameof(ownerTick));
        SetRest(root); tick = ownerTick; paused = false;
    }

    public bool Advance(long ownerTick, double seconds, AccessoryRigPose root)
    {
        root.Validate();
        if (ownerTick < 0 || !double.IsFinite(seconds)) throw new ArgumentException("Invalid owner frame.");
        if (seconds > 0 && seconds < AccessoryRigLimits.MinimumElapsedSeconds)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Elapsed time is below the finite integration budget.");
        if (paused || ownerTick <= tick || seconds <= 0) return false;
        if (!initialized || seconds > AccessoryRigLimits.MaximumElapsedSeconds || root.Scale != pose.Scale ||
            (root.Root - pose.Root).Length > Math.Max(totalLength, 1) * root.Scale * 2)
        {
            SetRest(root); tick = ownerTick; return true;
        }
        var before = pose;
        var count = Math.Min(AccessoryRigLimits.MaximumSubsteps, (int)Math.Ceiling(seconds / AccessoryRigLimits.MaximumSubstepSeconds));
        var dt = seconds / count;
        for (var step = 1; step <= count; step++)
        {
            var fraction = (double)step / count;
            var next = new AccessoryRigPose(before.Root + (root.Root - before.Root) * fraction,
                before.Angle + Wrap(root.Angle - before.Angle) * fraction, root.Scale);
            Integrate(dt, next);
        }
        pose = root; tick = ownerTick;
        return true;
    }

    /// <summary>Queue world-space momentum for the next successful owner frame. Shared IDs address the same particle.</summary>
    public bool ApplyImpulse(string nodeId, Vector impulse)
    {
        var particle = MovableParticle(nodeId, impulse);
        if (particle is null) return false;
        particle.PendingImpulse += impulse;
        return true;
    }

    /// <summary>Queue a world-space displacement (e.g. a drag sample) without advancing outside the owner frame.</summary>
    public bool DisplaceNode(string nodeId, Vector displacement)
    {
        var particle = MovableParticle(nodeId, displacement);
        if (particle is null) return false;
        particle.PendingDisplacement += displacement;
        return true;
    }

    /// <summary>Deep immutable output copy. Repainting does not step, consume forces, or update the owner tick.</summary>
    public AccessorySkeletonSnapshot Refresh()
    {
        if (!initialized) throw new InvalidOperationException("Advance, resume or reset before reading the skeleton.");
        var output = new Dictionary<string, IReadOnlyList<Point>>(StringComparer.Ordinal);
        var roots = new Dictionary<string, AccessoryRigPose>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            output.Add(group.Id, Array.AsReadOnly(group.Particles.Select(index => particles[index].Position).ToArray()));
            var angle = group.Reference is { } reference
                ? EdgeAngle(reference.A, reference.B, pose.Angle + reference.RestAngle) - reference.RestAngle
                : pose.Angle;
            roots.Add(group.Id, new(particles[group.Particles[0]].Position, angle, pose.Scale));
        }
        return new(tick, pose, configuration, output, roots);
    }

    private Particle? MovableParticle(string nodeId, Vector delta)
    {
        if (!initialized) throw new InvalidOperationException("Initialize the skeleton before applying input.");
        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y) || Math.Abs(delta.X) > 1_000_000 || Math.Abs(delta.Y) > 1_000_000)
            throw new ArgumentException("Input must be a finite bounded world vector.");
        if (!nodeParticles.TryGetValue(nodeId, out var index)) throw new ArgumentException("Unknown skeleton node.", nameof(nodeId));
        return paused || particles[index].Pinned ? null : particles[index];
    }

    private void Configure(AccessorySkeletonConfiguration input, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate(width, height);
        var snapshot = input.Snapshot();
        var ordered = snapshot.Groups.OrderBy(group => group.Id, StringComparer.Ordinal).ToArray();
        var nodes = ordered.SelectMany(group => group.Nodes).ToArray();
        var indices = nodes.Select((node, index) => (node.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);
        var parents = Enumerable.Range(0, nodes.Length).ToArray();
        foreach (var group in ordered)
            if (group.TargetId is not null) parents[Find(indices[group.Nodes[0].Id])] = Find(indices[group.TargetId]);

        var built = new List<Particle>();
        var representatives = new Dictionary<int, int>();
        var mapping = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < nodes.Length; i++)
        {
            var representative = Find(i);
            if (!representatives.TryGetValue(representative, out var particleIndex))
            {
                particleIndex = built.Count; representatives.Add(representative, particleIndex);
                built.Add(new(new((nodes[representative].X - snapshot.Pivot.X) / height,
                    (nodes[representative].Y - snapshot.Pivot.Y) / height)));
            }
            mapping.Add(nodes[i].Id, particleIndex);
        }

        var groupMap = ordered.ToDictionary(group => group.Id,
            group => new Group(group.Id, group.Nodes.Select(node => mapping[node.Id]).ToArray()), StringComparer.Ordinal);
        var owners = ordered.SelectMany(group => group.Nodes.Select((node, index) => (node.Id, group, index)))
            .ToDictionary(pair => pair.Id, pair => (pair.group, pair.index), StringComparer.Ordinal);
        var builtBones = new List<Bone>();
        var builtJoints = new List<Joint>();
        foreach (var definition in ordered)
        {
            var group = groupMap[definition.Id];
            group.Reference = Reference(definition);
            if (definition.TargetId is null) built[group.Particles[0]].Pinned = true;
            for (var i = 0; i < definition.Nodes.Count; i++)
            {
                // Each incident half-segment contributes mass. A root-only group has a small finite point mass.
                var mass = definition.Nodes.Count == 1 ? .005 :
                    ((i > 0 ? Length(definition, i) : 0) + (i + 1 < definition.Nodes.Count ? Length(definition, i + 1) : 0)) / 2;
                var particle = built[group.Particles[i]];
                particle.Mass += mass;
                particle.Gravity += mass * definition.Parameters.Gravity;
                particle.Damping += mass * definition.Parameters.Damping;
                if (i == 0) continue;
                builtBones.Add(new(group.Particles[i - 1], group.Particles[i], Length(definition, i), definition.Parameters));
                var current = Edge(definition, i);
                var reference = i == 1 ? group.Reference : Edge(definition, i - 1);
                builtJoints.Add(new(current.A, current.B, reference, current.RestAngle - (reference?.RestAngle ?? 0), definition.Parameters));
            }
        }
        foreach (var particle in built)
        {
            // Same control mapping as the legacy rig: 100 -> 4 image heights/s².
            // Average contributions first, then convert once for the one shared degree of freedom.
            particle.Gravity = particle.Gravity / particle.Mass * (4d / 100);
            particle.Damping /= particle.Mass;
            particle.InverseMass = particle.Pinned ? 0 : 1 / particle.Mass;
        }
        configuration = snapshot; particles = built.ToArray(); groups = groupMap.Values.ToArray();
        nodeParticles = mapping; bones = builtBones.ToArray(); joints = builtJoints.ToArray();
        // Acyclic single-source root references make the shared-particle graph a forest.
        // This order is for exact geometric closure only, not the physical impulse/constraint solve.
        var children = Enumerable.Range(0, particles.Length).Select(_ => new List<Bone>()).ToArray();
        foreach (var bone in bones) children[bone.A].Add(bone);
        var pending = new Queue<int>(Enumerable.Range(0, particles.Length).Where(index => particles[index].Pinned));
        var closure = new List<Bone>();
        while (pending.TryDequeue(out var parent))
            foreach (var bone in children[parent]) { closure.Add(bone); pending.Enqueue(bone.B); }
        closureBones = closure.ToArray(); constraintPositions = new Point[particles.Length];
        totalLength = bones.Sum(bone => bone.Length);
        allRigid = ordered.All(group => group.Parameters.Bend == 0 && group.Parameters.Elasticity == 0);
        initialized = false; paused = false; tick = -1;

        int Find(int index)
        {
            while (parents[index] != index) { parents[index] = parents[parents[index]]; index = parents[index]; }
            return index;
        }
        double Length(AccessorySkeletonGroup group, int end)
        {
            var a = group.Nodes[end - 1]; var b = group.Nodes[end];
            return new Vector((b.X - a.X) / height, (b.Y - a.Y) / height).Length;
        }
        ReferenceEdge Edge(AccessorySkeletonGroup group, int end)
        {
            var a = group.Nodes[end - 1]; var b = group.Nodes[end];
            return new(mapping[a.Id], mapping[b.Id], Math.Atan2(b.Y - a.Y, b.X - a.X));
        }
        ReferenceEdge? Reference(AccessorySkeletonGroup group)
        {
            if (group.TargetId is null) return null;
            var target = owners[group.TargetId];
            if (target.index > 0) return Edge(target.group, target.index);
            // A root has its reference-frame orientation, independent of bending its first bone.
            // Root-to-root attachment inherits that same frame, including through root-only groups.
            return Reference(target.group);
        }
    }

    private void SetRest(AccessoryRigPose root)
    {
        foreach (var particle in particles)
        {
            particle.Position = Transform(particle.Rest, root);
            particle.ClearMotion();
        }
        initialized = true; pose = root;
    }

    private void Integrate(double dt, AccessoryRigPose root)
    {
        // With every angular and length degree fixed, the forest has no internal motion.
        // Elasticity must also be zero: zero bend alone must still permit axial deformation.
        if (allRigid) { SetRest(root); return; }
        var maxSpeed = Math.Max(totalLength, .1) * root.Scale * AccessoryRigLimits.MaximumSpeedInChainLengths;
        foreach (var particle in particles)
        {
            particle.Previous = particle.Position;
            if (particle.Pinned) { particle.Position = Transform(particle.Rest, root); continue; }
            var velocity = particle.Velocity * Math.Exp(-particle.Damping * dt) + particle.PendingImpulse * particle.InverseMass +
                new Vector(0, particle.Gravity * root.Scale * dt);
            particle.PendingImpulse = default;
            particle.Position += particle.PendingDisplacement;
            particle.PendingDisplacement = default;
            if (velocity.Length > maxSpeed) velocity *= maxSpeed / velocity.Length;
            particle.Position += velocity * dt;
        }
        // Apply the recovery spring once. Reapplying soft targets between hard projections
        // would continually reintroduce length error and prevent the constraints from settling.
        foreach (var joint in joints) ProjectAngle(joint, dt, root, recover: true);
        foreach (var bone in bones) ProjectLength(bone, dt, root, recover: true);
        for (var iteration = 0; iteration < ConstraintIterations; iteration++)
        {
            // Alternating traversal removes the parent-first directional bias of a single forward projection.
            for (var k = 0; k < joints.Length; k++) ProjectAngle(joints[(iteration & 1) == 0 ? k : joints.Length - 1 - k], dt, root, recover: false);
            for (var k = 0; k < bones.Length; k++) ProjectLength(bones[(iteration & 1) == 0 ? k : bones.Length - 1 - k], dt, root, recover: false);
        }
        CloseLengths(root);
        foreach (var particle in particles)
        {
            particle.Velocity = particle.Pinned ? default : (particle.Position - particle.Previous) / dt;
            if (particle.Velocity.Length > maxSpeed) particle.Velocity *= maxSpeed / particle.Velocity.Length;
        }
    }

    private void CloseLengths(AccessoryRigPose root)
    {
        // Finite PBD iterations leave a small stretch residual, especially on long chains.
        // Translate each entire downstream subtree along its incoming edge to close that residual.
        // Reading all directions from one pre-closure copy preserves every edge angle exactly,
        // including cross-group angular constraints. The preceding solve remains fully bidirectional.
        for (var i = 0; i < particles.Length; i++) constraintPositions[i] = particles[i].Position;
        foreach (var bone in closureBones)
        {
            var delta = constraintPositions[bone.B] - constraintPositions[bone.A];
            var length = delta.Length;
            var rest = bone.Length * root.Scale;
            var bounded = Math.Clamp(length, rest * (1 - bone.Parameters.Elasticity), rest * (1 + bone.Parameters.Elasticity));
            var direction = length > 1e-15 ? delta / length :
                AccessoryRigDefinition.Rotate(particles[bone.B].Rest - particles[bone.A].Rest, root.Angle) / bone.Length;
            particles[bone.B].Position = particles[bone.A].Position + direction * bounded;
        }
    }

    private void ProjectLength(Bone bone, double dt, AccessoryRigPose root, bool recover)
    {
        var a = particles[bone.A]; var b = particles[bone.B];
        var delta = b.Position - a.Position;
        var length = delta.Length;
        var rest = bone.Length * root.Scale;
        var target = Math.Clamp(length, rest * (1 - bone.Parameters.Elasticity), rest * (1 + bone.Parameters.Elasticity));
        var error = recover ? (length - rest) * Recovery(bone.Parameters.Restoration, dt) : length - target;
        var inverseMass = a.InverseMass + b.InverseMass;
        if (inverseMass == 0 || error == 0) return;
        var direction = length > 1e-15 ? delta / length :
            AccessoryRigDefinition.Rotate(b.Rest - a.Rest, root.Angle) / bone.Length;
        var correction = direction * (error / inverseMass);
        a.Position += correction * a.InverseMass;
        b.Position -= correction * b.InverseMass;
    }

    private void ProjectAngle(Joint joint, double dt, AccessoryRigPose root, bool recover)
    {
        var current = particles[joint.B].Position - particles[joint.A].Position;
        if (current.LengthSquared < 1e-24) return;
        var referenceAngle = joint.Reference is { } reference
            ? EdgeAngle(reference.A, reference.B, root.Angle + reference.RestAngle) : root.Angle;
        var delta = Wrap(Math.Atan2(current.Y, current.X) - referenceAngle - joint.RestAngle);
        var limit = joint.Parameters.Bend * AccessoryRigLimits.MaximumBendRadians;
        var error = recover ? delta * Recovery(joint.Parameters.Restoration, dt) : delta - Math.Clamp(delta, -limit, limit);
        if (error == 0) return;

        Span<int> ids = stackalloc int[4];
        Span<Vector> gradients = stackalloc Vector[4];
        var count = 0;
        Add(joint.A, -Perpendicular(current) / current.LengthSquared, ids, gradients, ref count);
        Add(joint.B, Perpendicular(current) / current.LengthSquared, ids, gradients, ref count);
        if (joint.Reference is { } edge)
        {
            var vector = particles[edge.B].Position - particles[edge.A].Position;
            if (vector.LengthSquared < 1e-24) return;
            Add(edge.A, Perpendicular(vector) / vector.LengthSquared, ids, gradients, ref count);
            Add(edge.B, -Perpendicular(vector) / vector.LengthSquared, ids, gradients, ref count);
        }
        var denominator = 0d;
        for (var i = 0; i < count; i++) denominator += particles[ids[i]].InverseMass * gradients[i].LengthSquared;
        if (denominator < 1e-24) return;
        // Bound one nonlinear angular correction; repeated coupled iterations settle the hard limit.
        var multiplier = Math.Clamp(error, -.25, .25) / denominator;
        for (var i = 0; i < count; i++) particles[ids[i]].Position -= gradients[i] * (multiplier * particles[ids[i]].InverseMass);
    }

    private static void Add(int id, Vector gradient, Span<int> ids, Span<Vector> gradients, ref int count)
    {
        for (var i = 0; i < count; i++)
            if (ids[i] == id) { gradients[i] += gradient; return; }
        ids[count] = id; gradients[count] = gradient; count++;
    }

    private double EdgeAngle(int a, int b, double fallback)
    {
        var vector = particles[b].Position - particles[a].Position;
        return vector.LengthSquared < 1e-24 ? fallback : Math.Atan2(vector.Y, vector.X);
    }
    private static double Recovery(double frequency, double dt) => 1 - Math.Exp(-frequency * frequency * dt * dt);
    private static Vector Perpendicular(Vector value) => new(-value.Y, value.X);
    private static double Wrap(double angle) => Math.IEEERemainder(angle, Math.Tau);
    private static Point Transform(Point rest, AccessoryRigPose root) => root.Root + AccessoryRigDefinition.Rotate((Vector)rest * root.Scale, root.Angle);

    private sealed class Particle(Point rest)
    {
        internal readonly Point Rest = rest;
        internal Point Position;
        internal Point Previous;
        internal Vector Velocity;
        internal Vector PendingImpulse;
        internal Vector PendingDisplacement;
        internal double Mass;
        internal double InverseMass;
        internal double Gravity;
        internal double Damping;
        internal bool Pinned;
        internal void ClearMotion() { Velocity = default; PendingImpulse = default; PendingDisplacement = default; }
    }
    private sealed class Group(string id, int[] particles)
    {
        internal readonly string Id = id;
        internal readonly int[] Particles = particles;
        internal ReferenceEdge? Reference;
    }
    private readonly record struct ReferenceEdge(int A, int B, double RestAngle);
    private readonly record struct Bone(int A, int B, double Length, AccessorySkeletonParameters Parameters);
    private readonly record struct Joint(int A, int B, ReferenceEdge? Reference, double RestAngle, AccessorySkeletonParameters Parameters);
}
