using System.Windows;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Accessories.Rig;
using Dango.Desktop.Accessories.Editing;
using Point = System.Windows.Point;

namespace Dango.Desktop.Accessories;

/// <summary>An immutable, display-only mesh. One solver step can be consumed by any number of hosts.</summary>
public sealed record AccessoryMeshFrame(
    long StepIndex, IReadOnlyList<Point> Vertices, IReadOnlyList<Point> TextureCoordinates,
    IReadOnlyList<int> TriangleIndices, Rect Bounds, Point ImagePivot,
    Vector ConnectionOffset, double RotationDegrees)
{
    public bool AllowFolding { get; init; }

    public AccessoryMeshFrame Translated(Vector offset)
    {
        var bounds = Bounds; bounds.Offset(offset);
        return this with
        {
            Vertices = Array.AsReadOnly(Vertices.Select(p => p + offset).ToArray()),
            Bounds = bounds,
            ImagePivot = ImagePivot + offset,
        };
    }
}

/// <summary>Display-only root connection, overall rotation, and one shared skeleton per equipped item.
/// Successful owner frames advance motion; paints only project immutable current geometry.</summary>
public sealed class AccessoryMotionSolver
{
    public const int GridDivisions = 8;
    private const double GravityAcceleration = 300;
    private const double MaximumAcceleration = 3000;
    private SpringState connectionX, connectionY;
    private Point? previousAnchor;
    private Vector velocity, acceleration;
    private bool velocityReady, paused, needsRebase = true;
    private double angle, angularVelocity;
    private long stepIndex = -1;
    private AccessoryItem? activeItem;
    private AccessoryItem? displayedItem;
    private double activeWidth, activeHeight;
    private double displayedWidth, displayedHeight;
    private AccessoryRigSolver? rig;
    private AccessoryRigSkin? skin;
    private AccessorySkeletonSolver? skeleton;
    private Dictionary<string, AccessorySkeletonSkin> skeletonSkins = new(StringComparer.Ordinal);
    public AccessoryMeshFrame? Current { get; private set; }
    public IReadOnlyList<AccessoryMeshFrame> CurrentPieces { get; private set; } = [];

    public bool Paused
    {
        get => paused;
        set
        {
            if (paused == value) return;
            paused = value; needsRebase = true;
            ClearSignals();
            if (value) { rig?.Pause(); skeleton?.Pause(); }
        }
    }

    public void Reset()
    {
        ClearDynamics(); activeItem = null; rig = null; skin = null; skeleton = null; skeletonSkins.Clear();
        stepIndex = -1; Current = null; CurrentPieces = []; displayedItem = null; needsRebase = true;
    }

    public AccessoryMeshFrame Update(long nextStepIndex, double seconds, Point anchorPoint,
        double displayWidth, double displayHeight, AccessoryItem item, AccessoryEditorProject? project = null)
    {
        Validate(anchorPoint, displayWidth, displayHeight, item);
        ValidateProject(item, project);
        if (nextStepIndex < 0 || !double.IsFinite(seconds) || seconds < AccessoryRigLimits.MinimumElapsedSeconds || seconds > .1)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (paused || nextStepIndex <= stepIndex)
            return Current is not null && SameConfiguration(displayedItem, item) && displayedWidth == displayWidth && displayedHeight == displayHeight
                ? Current : Refresh(anchorPoint, displayWidth, displayHeight, item, project);

        var changed = !Matches(item, displayWidth, displayHeight);
        var teleport = previousAnchor is { } prior &&
            (anchorPoint - prior).Length > 2 * Math.Sqrt(displayWidth * displayWidth + displayHeight * displayHeight);
        var rebase = changed || needsRebase || teleport || seconds > AccessoryRigLimits.MaximumElapsedSeconds;
        if (rebase)
        {
            ClearDynamics();
            if (changed)
            {
                activeItem = item with { Skeleton = item.Skeleton?.Snapshot() }; activeWidth = displayWidth; activeHeight = displayHeight;
                skeleton = null; skeletonSkins.Clear();
                if (item.DeformationMode == AccessoryDeformationMode.SingleChain)
                {
                    var definition = Definition(item, displayWidth, displayHeight);
                    skin = new(definition); rig = new(definition, item.Rig!.CreateParameters(item.GravityInfluence));
                }
                else { rig = null; skin = null; }
                if (item.DeformationMode == AccessoryDeformationMode.Skeleton)
                {
                    skeleton = new(item.Skeleton!, item.PixelWidth, item.PixelHeight);
                    skeletonSkins = CreateSkins(item, project!);
                }
            }
            needsRebase = false;
        }
        Observe(anchorPoint, seconds);
        stepIndex = nextStepIndex;
        if (!rebase)
        {
            var length = Math.Sqrt(displayWidth * displayWidth + displayHeight * displayHeight);
            var strength = item.ConnectionStrength / 100;
            var looseExtension = SmoothStep((.3 - strength) / .3);
            // Root translation and whole-image angular torque act on their own degrees of freedom.
            // Local bones receive gravity only through RigParameters, never this root force again.
            var force = new Vector(-acceleration.X, -acceleration.Y + item.GravityInfluence / 100 * GravityAcceleration);
            if (strength >= 1) connectionX = connectionY = default;
            else
            {
                var frequency = .75 + strength * 4.25 - .57 * looseExtension;
                var stiffness = Math.Pow(2 * Math.PI * frequency, 2);
                var limit = length * item.MaxConnectionTravelPercent / 100;
                connectionX = DampedSpring.AdvanceBounded(connectionX, force.X / stiffness, frequency, .8, seconds, -limit, limit);
                connectionY = DampedSpring.AdvanceBounded(connectionY, force.Y / stiffness, frequency, .8, seconds, -limit, limit);
                LimitConnectionTravel(limit);
            }
            AdvanceAngle(item, force, displayWidth, displayHeight, seconds);
        }
        var pose = RootPose(anchorPoint, displayWidth, displayHeight, item, false);
        if (rig is not null)
        {
            if (rebase) rig.Resume(nextStepIndex, pose);
            else rig.Advance(nextStepIndex, seconds, pose);
        }
        if (skeleton is not null)
        {
            if (rebase) skeleton.Resume(nextStepIndex, pose);
            else skeleton.Advance(nextStepIndex, seconds, pose);
        }
        return Refresh(anchorPoint, displayWidth, displayHeight, item, project);
    }

    /// <summary>Pure geometry projection: does not change tick, velocities, root state or rig state.
    /// A changed configuration is shown at rest until the next successful owner update rebases it.</summary>
    public AccessoryMeshFrame Refresh(Point anchorPoint, double displayWidth, double displayHeight, AccessoryItem item,
        AccessoryEditorProject? project = null)
    {
        Validate(anchorPoint, displayWidth, displayHeight, item);
        ValidateProject(item, project);
        var same = Matches(item, displayWidth, displayHeight);
        var pose = RootPose(anchorPoint, displayWidth, displayHeight, item, !same);
        if (item.DeformationMode == AccessoryDeformationMode.Skeleton)
        {
            var snapshot = same && skeleton is not null ? ProjectSnapshot(skeleton.Refresh(), pose) : RestSnapshot(item.Skeleton!, item.PixelHeight, pose);
            var skins = same && skeleton is not null ? skeletonSkins : CreateSkins(item, project!);
            var pieces = project!.RenderPieces.Select(piece => skins[piece.GroupId ?? string.Empty].Deform(snapshot)
                with { ConnectionOffset = pose.Root - anchorPoint }).ToArray();
            if (pieces.Sum(piece => piece.Vertices.Count) > AccessorySkeletonSkin.MaximumTotalVertices ||
                pieces.Sum(piece => piece.TriangleIndices.Count / 3) > AccessorySkeletonSkin.MaximumTotalTriangles)
                throw new InvalidOperationException("Skeleton render pieces exceeded their total mesh budget.");
            CurrentPieces = Array.AsReadOnly(pieces);
            displayedItem = item; displayedWidth = displayWidth; displayedHeight = displayHeight;
            return Current = pieces[0];
        }
        AccessoryMeshFrame frame;
        if (item.DeformationMode == AccessoryDeformationMode.SingleChain)
        {
            if (same && rig is not null && skin is not null)
            {
                var snapshot = rig.Refresh();
                var prior = snapshot.Pose;
                var nodes = snapshot.Nodes.Select(node => pose.Root + AccessoryRigDefinition.Rotate(
                    (node - prior.Root) * (pose.Scale / prior.Scale), pose.Angle - prior.Angle)).ToArray();
                frame = skin.Deform(new(stepIndex, pose, Array.AsReadOnly(nodes), snapshot.Definition));
            }
            else
            {
                var definition = Definition(item, displayWidth, displayHeight);
                var nodes = definition.LocalNodes.Select(node => pose.Transform(node, definition.LocalNodes[0])).ToArray();
                frame = new AccessoryRigSkin(definition).Deform(new(stepIndex, pose, Array.AsReadOnly(nodes), definition));
            }
        }
        else
        {
            var xs = Axis(item.ImagePivotX); var ys = Axis(item.ImagePivotY);
            var uvs = ys.SelectMany(y => xs.Select(x => new Point(x, y))).ToArray();
            var vertices = uvs.Select(uv => pose.Root + AccessoryRigDefinition.Rotate(
                new Vector((uv.X - item.ImagePivotX) * displayWidth, (uv.Y - item.ImagePivotY) * displayHeight), pose.Angle)).ToArray();
            var triangles = new List<int>();
            for (var y = 0; y < ys.Length - 1; y++)
            for (var x = 0; x < xs.Length - 1; x++)
            {
                var a = y * xs.Length + x; var b = a + 1; var c = a + xs.Length; var d = c + 1;
                triangles.AddRange([a, b, d, a, d, c]);
            }
            var bounds = Rect.Empty;
            foreach (var vertex in vertices) bounds.Union(vertex);
            frame = new(stepIndex, Array.AsReadOnly(vertices), Array.AsReadOnly(uvs), triangles.AsReadOnly(), bounds,
                pose.Root, default, pose.Angle * 180 / Math.PI);
        }
        displayedItem = item; displayedWidth = displayWidth; displayedHeight = displayHeight;
        Current = frame with { ConnectionOffset = pose.Root - anchorPoint };
        CurrentPieces = Array.AsReadOnly(new[] { Current });
        return Current;
    }

    /// <summary>Advance one item once, then return all its pieces in final drawing order.</summary>
    public IReadOnlyList<AccessoryMeshFrame> UpdatePieces(long nextStepIndex, double seconds, Point anchorPoint,
        double displayWidth, double displayHeight, AccessoryItem item, AccessoryEditorProject? project = null)
    {
        Update(nextStepIndex, seconds, anchorPoint, displayWidth, displayHeight, item, project);
        return CurrentPieces;
    }

    /// <summary>Reproject all pieces without observing movement or consuming owner time.</summary>
    public IReadOnlyList<AccessoryMeshFrame> RefreshPieces(Point anchorPoint, double displayWidth, double displayHeight,
        AccessoryItem item, AccessoryEditorProject? project = null)
    {
        Refresh(anchorPoint, displayWidth, displayHeight, item, project);
        return CurrentPieces;
    }

    /// <summary>Immutable physics snapshot at the last successful owner frame; display projections do not replace it.</summary>
    public AccessorySkeletonSnapshot? ReadSkeletonSnapshot() => skeleton?.Refresh();

    private static Dictionary<string, AccessorySkeletonSkin> CreateSkins(AccessoryItem item, AccessoryEditorProject project) =>
        project.RenderPieces.Select(piece => piece.GroupId ?? string.Empty).Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, id => new AccessorySkeletonSkin(item.Skeleton!, item.PixelWidth, item.PixelHeight,
                id.Length == 0 ? null : id), StringComparer.Ordinal);

    private AccessorySkeletonSnapshot RestSnapshot(AccessorySkeletonConfiguration configuration, int pixelHeight, AccessoryRigPose pose)
    {
        var groups = new Dictionary<string, IReadOnlyList<Point>>(StringComparer.Ordinal);
        var roots = new Dictionary<string, AccessoryRigPose>(StringComparer.Ordinal);
        foreach (var group in configuration.Groups)
        {
            var nodes = group.Nodes.Select(node => pose.Root + AccessoryRigDefinition.Rotate(new Vector(
                (node.X - configuration.Pivot.X) / pixelHeight, (node.Y - configuration.Pivot.Y) / pixelHeight) * pose.Scale, pose.Angle)).ToArray();
            groups.Add(group.Id, Array.AsReadOnly(nodes)); roots.Add(group.Id, new(nodes[0], pose.Angle, pose.Scale));
        }
        return new(stepIndex, pose, configuration.Snapshot(), groups, roots);
    }

    private static AccessorySkeletonSnapshot ProjectSnapshot(AccessorySkeletonSnapshot snapshot, AccessoryRigPose pose)
    {
        var scale = pose.Scale / snapshot.Pose.Scale; var angle = pose.Angle - snapshot.Pose.Angle;
        Point Project(Point point) => pose.Root + AccessoryRigDefinition.Rotate((point - snapshot.Pose.Root) * scale, angle);
        return new(snapshot.Tick, pose, snapshot.Configuration,
            snapshot.Groups.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<Point>)Array.AsReadOnly(pair.Value.Select(Project).ToArray()), StringComparer.Ordinal),
            snapshot.GroupPoses.ToDictionary(pair => pair.Key,
                pair => new AccessoryRigPose(Project(pair.Value.Root), pair.Value.Angle + angle, pair.Value.Scale * scale), StringComparer.Ordinal));
    }

    private static void ValidateProject(AccessoryItem item, AccessoryEditorProject? project)
    {
        if (item.DeformationMode != AccessoryDeformationMode.Skeleton) return;
        if (project is null) throw new InvalidOperationException("分组骨架工程不可用，无法独立呈现绘制片。");
        if (project.ProjectId != item.ProjectId || project.Version != item.ProjectVersion || project.Revision != item.EditRevision ||
            project.FinalAssetId != item.AssetId || project.PixelWidth != item.PixelWidth || project.PixelHeight != item.PixelHeight ||
            !AccessoryConfiguration.SkeletonEquals(project.Skeleton, item.Skeleton) ||
            Math.Abs(item.ImagePivotX - project.Skeleton.Pivot.X / item.PixelWidth) > 1e-10 ||
            Math.Abs(item.ImagePivotY - project.Skeleton.Pivot.Y / item.PixelHeight) > 1e-10 ||
            project.RenderPieces.Count is < 1 or > AccessorySkeletonSkin.MaximumRenderPieces)
            throw new ArgumentException("分组骨架工程与当前配饰修订或绘制片不一致。", nameof(project));
    }

    private bool Matches(AccessoryItem item, double width, double height) =>
        SameConfiguration(activeItem, item) && activeWidth == width && activeHeight == height;

    private static bool SameConfiguration(AccessoryItem? a, AccessoryItem b) => a is not null &&
        a.AssetId == b.AssetId && a.PixelWidth == b.PixelWidth && a.PixelHeight == b.PixelHeight &&
        a.DeformationMode == b.DeformationMode && Equals(a.Rig, b.Rig) &&
        a.ProjectId == b.ProjectId && a.ProjectVersion == b.ProjectVersion && a.EditRevision == b.EditRevision &&
        AccessoryConfiguration.SkeletonEquals(a.Skeleton, b.Skeleton) &&
        a.ImagePivotX == b.ImagePivotX && a.ImagePivotY == b.ImagePivotY &&
        a.BodyAnchorX == b.BodyAnchorX && a.BodyAnchorY == b.BodyAnchorY && a.BaseAngleDegrees == b.BaseAngleDegrees &&
        a.ConnectionStrength == b.ConnectionStrength && a.MaxConnectionTravelPercent == b.MaxConnectionTravelPercent &&
        a.LockRotation == b.LockRotation && a.RotationDamping == b.RotationDamping && a.ReturnStrength == b.ReturnStrength &&
        a.LimitSwing == b.LimitSwing && a.MaxSwingDegrees == b.MaxSwingDegrees && a.GravityInfluence == b.GravityInfluence;

    private static AccessoryRigDefinition Definition(AccessoryItem item, double width, double height) =>
        new(item.Rig!.Nodes.Select(n => new Point(n.X, n.Y)), width / height);

    private AccessoryRigPose RootPose(Point anchor, double width, double height, AccessoryItem item, bool rest)
    {
        var rotation = rest || item.LockRotation ? 0 : angle;
        if (item.LimitSwing) rotation = Math.Clamp(rotation, -item.MaxSwingDegrees * Math.PI / 180, item.MaxSwingDegrees * Math.PI / 180);
        var connection = rest || item.ConnectionStrength >= 100 ? new Vector() : new Vector(connectionX.Position, connectionY.Position);
        var limit = Math.Sqrt(width * width + height * height) * item.MaxConnectionTravelPercent / 100;
        if (connection.Length > limit) connection *= limit / connection.Length;
        return new(anchor + connection, item.BaseAngleDegrees * Math.PI / 180 + rotation, height);
    }

    private void ClearDynamics()
    {
        ClearSignals(); connectionX = connectionY = default; angle = angularVelocity = 0;
    }

    private void LimitConnectionTravel(double limit)
    {
        if (limit == 0) { connectionX = connectionY = default; return; }
        var position = new Vector(connectionX.Position, connectionY.Position);
        if (position.Length < limit) return;
        var normal = position / position.Length;
        var speed = new Vector(connectionX.Velocity, connectionY.Velocity);
        var outward = Vector.Multiply(normal, speed);
        if (outward > 0) speed -= normal * outward;
        position = normal * limit;
        connectionX = new(position.X, speed.X); connectionY = new(position.Y, speed.Y);
    }
    private void Observe(Point anchor, double seconds)
    {
        if (previousAnchor is null) { previousAnchor = anchor; return; }
        var nextVelocity = (anchor - previousAnchor.Value) / seconds;
        var nextAcceleration = velocityReady ? (nextVelocity - velocity) / seconds : new Vector();
        nextAcceleration.X = Math.Clamp(nextAcceleration.X, -MaximumAcceleration, MaximumAcceleration);
        nextAcceleration.Y = Math.Clamp(nextAcceleration.Y, -MaximumAcceleration, MaximumAcceleration);
        var blend = 1 - Math.Exp(-seconds / 0.055);
        acceleration += (nextAcceleration - acceleration) * blend;
        velocity = nextVelocity; velocityReady = true; previousAnchor = anchor;
    }

    private void AdvanceAngle(AccessoryItem item, Vector force, double width, double height, double seconds)
    {
        if (item.LockRotation) { angle = angularVelocity = 0; return; }
        var count = Math.Max(1, (int)Math.Ceiling(seconds * 240));
        var dt = seconds / count;
        var returnRatio = item.ReturnStrength / 100;
        var stiffness = 120 * returnRatio;
        var drag = item.RotationDamping / 100 * 24;
        var center = new Vector((0.5 - item.ImagePivotX) * width, (0.5 - item.ImagePivotY) * height);
        var inertia = (width * width + height * height) / 12 + center.LengthSquared;
        var limit = item.MaxSwingDegrees * Math.PI / 180;
        for (var i = 0; i < count; i++)
        {
            var radians = item.BaseAngleDegrees * Math.PI / 180 + angle;
            var arm = new Vector(center.X * Math.Cos(radians) - center.Y * Math.Sin(radians),
                center.X * Math.Sin(radians) + center.Y * Math.Cos(radians));
            var torque = Vector.CrossProduct(arm, force) / Math.Max(1, inertia);
            angularVelocity = Math.Clamp(angularVelocity + (torque - stiffness * angle - drag * angularVelocity) * dt, -8, 8);
            angle += angularVelocity * dt;
            if (item.LimitSwing && Math.Abs(angle) > limit)
            { angle = Math.Clamp(angle, -limit, limit); if (angle * angularVelocity >= 0) angularVelocity = 0; }
            else if (!item.LimitSwing) angle = Math.IEEERemainder(angle, Math.PI * 2);
        }
    }

    private void ClearSignals()
    { previousAnchor = null; velocity = acceleration = new(); velocityReady = false; }


    private static double SmoothStep(double value)
    {
        var t = Math.Clamp(value, 0, 1); return t * t * (3 - 2 * t);
    }

    private static double[] Axis(double pivot) => Enumerable.Range(0, GridDivisions + 1)
        .Select(i => i / (double)GridDivisions).Append(pivot).Distinct().Order().ToArray();

    private static void Validate(Point anchor, double width, double height, AccessoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y) || Math.Abs(anchor.X) > 1e6 || Math.Abs(anchor.Y) > 1e6 ||
            !double.IsFinite(width) || !double.IsFinite(height) || width is < .001 or > 2048 || height is < .001 or > 2048 ||
            !double.IsFinite(item.ImagePivotX) || !double.IsFinite(item.ImagePivotY) || item.ImagePivotX is < 0 or > 1 || item.ImagePivotY is < 0 or > 1 ||
            !Enum.IsDefined(item.DeformationMode) ||
            !double.IsFinite(item.ConnectionStrength) || item.ConnectionStrength is < 0 or > 100 ||
            !double.IsFinite(item.MaxConnectionTravelPercent) || item.MaxConnectionTravelPercent is < 0 or > 200 ||
            !double.IsFinite(item.GravityInfluence) || item.GravityInfluence is < 0 or > 100 || !double.IsFinite(item.RotationDamping) || item.RotationDamping is < 0 or > 100 ||
            !double.IsFinite(item.ReturnStrength) || item.ReturnStrength is < 0 or > 100 || !double.IsFinite(item.MaxSwingDegrees) || item.MaxSwingDegrees is < 0 or > 180 ||
            !double.IsFinite(item.BaseAngleDegrees) || item.BaseAngleDegrees is < -180 or > 180 ||
            item.DeformationMode == AccessoryDeformationMode.SingleChain && item.Rig is null)
            throw new ArgumentOutOfRangeException(nameof(item));
        if (item.DeformationMode == AccessoryDeformationMode.SingleChain)
        {
            if (item.Rig!.Nodes.Count is < 2 or > AccessoryRigLimits.MaximumNodes || item.Rig.Nodes.Any(node => node is null) ||
                item.Rig.Nodes[0].X != item.ImagePivotX || item.Rig.Nodes[0].Y != item.ImagePivotY ||
                item.Rig.BindingVersion != AccessoryRigLimits.BindingAlgorithmVersion)
                throw new ArgumentException("Rig root must match the image pivot.", nameof(item));
            item.Rig.CreateParameters(item.GravityInfluence).Validate();
        }
        if (item.DeformationMode == AccessoryDeformationMode.Skeleton)
        {
            if (item.Skeleton is null) throw new ArgumentException("Skeleton configuration is missing.", nameof(item));
            item.Skeleton.Validate(item.PixelWidth, item.PixelHeight);
        }
    }
}
