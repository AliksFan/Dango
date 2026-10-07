using System.Windows;
using Dango.Desktop.Physics.Presentation.Attachments;
using Point = System.Windows.Point;

namespace Dango.Desktop.Accessories;

/// <summary>An immutable, display-only mesh. One solver step can be consumed by any number of hosts.</summary>
public sealed record AccessoryMeshFrame(
    long StepIndex, IReadOnlyList<Point> Vertices, IReadOnlyList<Point> TextureCoordinates,
    IReadOnlyList<int> TriangleIndices, Rect Bounds, Point ImagePivot,
    Vector ConnectionOffset, double RotationDegrees)
{
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

/// <summary>
/// A single display-only attachment, with independent connection, angular and material states.
/// The owner supplies the actual body anchor after a successful body step; rendering never advances it.
/// Uniform material parameters drive a continuous bend/strain field sampled into a texture mesh.
/// No node in this mesh is a collision object and only the image pivot is connected to the body.
/// </summary>
public sealed class AccessoryMotionSolver
{
    public const int GridDivisions = 8;
    private const double GravityAcceleration = 300;
    private const double MaximumAcceleration = 3000;
    private static readonly IReadOnlyList<Point> Uvs = CreateUvs();
    private static readonly IReadOnlyList<int> Triangles = CreateTriangles();
    private IReadOnlyList<Point> materialUvs = Uvs;
    private IReadOnlyList<int> materialTriangles = Triangles;
    private double meshPivotX = double.NaN, meshPivotY = double.NaN;
    private SpringState connectionX, connectionY, bendX, bendY, strainX, strainY;
    private Point? previousAnchor;
    private Vector velocity, acceleration;
    private bool velocityReady, paused;
    private double angle, angularVelocity;
    private long stepIndex = -1;
    public AccessoryMeshFrame? Current { get; private set; }

    public bool Paused
    {
        get => paused;
        set
        {
            if (paused == value) return;
            paused = value;
            ClearSignals();
        }
    }

    public void Reset()
    {
        ClearSignals(); connectionX = connectionY = bendX = bendY = strainX = strainY = default;
        angle = angularVelocity = 0; stepIndex = -1; Current = null;
    }

    public AccessoryMeshFrame Update(long nextStepIndex, double seconds, Point anchorPoint,
        double displayWidth, double displayHeight, AccessoryItem item)
    {
        Validate(anchorPoint, displayWidth, displayHeight, item);
        if (nextStepIndex < 0 || !double.IsFinite(seconds) || seconds is <= 0 or > 0.1)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if ((paused || nextStepIndex <= stepIndex) && Current is not null) return Current;
        if (paused) return Refresh(anchorPoint, displayWidth, displayHeight, item);
        Observe(anchorPoint, seconds);
        stepIndex = nextStepIndex;
        var length = Math.Sqrt(displayWidth * displayWidth + displayHeight * displayHeight);
        var softness = item.Softness / 100;
        var gravity = item.GravityInfluence / 100;
        var strength = item.ConnectionStrength / 100;
        var inertialForce = new Vector(-acceleration.X, -acceleration.Y + gravity * GravityAcceleration);

        if (strength >= 1) connectionX = connectionY = default;
        else
        {
            var frequency = 0.75 + strength * 4.25;
            var stiffness = Math.Pow(2 * Math.PI * frequency, 2);
            var limit = length * 0.18;
            connectionX = DampedSpring.AdvanceBounded(connectionX, inertialForce.X / stiffness,
                frequency, 0.8, seconds, -limit, limit);
            connectionY = DampedSpring.AdvanceBounded(connectionY, inertialForce.Y / stiffness,
                frequency, 0.8, seconds, -limit, limit);
        }

        if (softness == 0) bendX = bendY = strainX = strainY = default;
        else
        {
            // The same softness controls all material points. Their spatially varying response
            // comes from distance to the one pivot, not painted per-region stiffness or extra anchors.
            var force = inertialForce / length;
            var targetBend = force * (0.018 * softness);
            if (targetBend.Length > 0.10) targetBend *= 0.10 / targetBend.Length;
            var frequency = 4.5 - softness * 2;
            bendX = DampedSpring.AdvanceBounded(bendX, targetBend.X, frequency, 0.8, seconds, -0.10, 0.10);
            bendY = DampedSpring.AdvanceBounded(bendY, targetBend.Y, frequency, 0.8, seconds, -0.10, 0.10);
            strainX = DampedSpring.AdvanceBounded(strainX, Math.Clamp(-Math.Abs(force.X) * 0.006 * softness, -0.08, 0.08),
                frequency, 0.8, seconds, -0.08, 0.08);
            strainY = DampedSpring.AdvanceBounded(strainY, Math.Clamp(force.Y * 0.009 * softness, -0.08, 0.08),
                frequency, 0.8, seconds, -0.08, 0.08);
        }
        AdvanceAngle(item, inertialForce, displayWidth, displayHeight, seconds);
        return Refresh(anchorPoint, displayWidth, displayHeight, item);
    }

    /// <summary>Refreshes configuration or placement without integrating or changing the body step.</summary>
    public AccessoryMeshFrame Refresh(Point anchorPoint, double displayWidth, double displayHeight, AccessoryItem item)
    {
        Validate(anchorPoint, displayWidth, displayHeight, item);
        PrepareMaterialMesh(item.ImagePivotX, item.ImagePivotY);
        if (item.LockRotation) angle = angularVelocity = 0;
        if (item.ConnectionStrength >= 100) connectionX = connectionY = default;
        if (item.Softness == 0) bendX = bendY = strainX = strainY = default;
        var rotation = item.LockRotation ? 0 : angle;
        if (item.LimitSwing) rotation = Math.Clamp(rotation, -item.MaxSwingDegrees * Math.PI / 180, item.MaxSwingDegrees * Math.PI / 180);
        var radians = item.BaseAngleDegrees * Math.PI / 180 + rotation;
        var cosine = Math.Cos(radians); var sine = Math.Sin(radians);
        var connection = item.ConnectionStrength >= 100 ? new Vector() : new Vector(connectionX.Position, connectionY.Position);
        var pivot = anchorPoint + connection;
        var length = Math.Sqrt(displayWidth * displayWidth + displayHeight * displayHeight);
        var materialBend = item.Softness == 0 ? new Vector() : new Vector(bendX.Position, bendY.Position);
        // ||J-I|| < .37 for every point and legal pivot, so the material map cannot fold.
        if (materialBend.Length > 0.14) materialBend *= 0.14 / materialBend.Length;
        var sx = item.Softness == 0 ? 0 : strainX.Position;
        var sy = item.Softness == 0 ? 0 : strainY.Position;
        var vertices = new Point[materialUvs.Count];
        for (var i = 0; i < vertices.Length; i++)
        {
            var uv = materialUvs[i];
            var dx = (uv.X - item.ImagePivotX) * displayWidth;
            var dy = (uv.Y - item.ImagePivotY) * displayHeight;
            var radial = (dx * dx + dy * dy) / length;
            // Bend is in scene coordinates: gravity stays downward even when the image rotates.
            var localX = dx * (1 + sx); var localY = dy * (1 + sy);
            vertices[i] = new(pivot.X + localX * cosine - localY * sine + materialBend.X * radial,
                pivot.Y + localX * sine + localY * cosine + materialBend.Y * radial);
        }
        var left = vertices.Min(p => p.X); var top = vertices.Min(p => p.Y);
        var bounds = new Rect(left, top, vertices.Max(p => p.X) - left, vertices.Max(p => p.Y) - top);
        return Current = new(stepIndex, Array.AsReadOnly(vertices), materialUvs, materialTriangles, bounds, pivot,
            connection, item.BaseAngleDegrees + rotation * 180 / Math.PI);
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

    private void PrepareMaterialMesh(double pivotX, double pivotY)
    {
        if (pivotX == meshPivotX && pivotY == meshPivotY) return;
        meshPivotX = pivotX; meshPivotY = pivotY;
        var regular = Enumerable.Range(0, GridDivisions + 1).Select(i => i / (double)GridDivisions).ToArray();
        var xs = regular.Append(pivotX).Distinct().Order().ToArray();
        var ys = regular.Append(pivotY).Distinct().Order().ToArray();
        if (xs.Length == regular.Length && ys.Length == regular.Length)
        { materialUvs = Uvs; materialTriangles = Triangles; return; }
        // Include the exact arbitrary pivot as a texture vertex; otherwise linear interpolation of
        // a curved deformation field would subtly move a pivot lying inside a regular grid cell.
        materialUvs = Array.AsReadOnly(ys.SelectMany(y => xs.Select(x => new Point(x, y))).ToArray());
        var indices = new List<int>();
        for (var y = 0; y < ys.Length - 1; y++)
            for (var x = 0; x < xs.Length - 1; x++)
            {
                var a = y * xs.Length + x; var b = a + 1; var c = a + xs.Length; var d = c + 1;
                indices.AddRange([a, b, d, a, d, c]);
            }
        materialTriangles = Array.AsReadOnly(indices.ToArray());
    }

    private static void Validate(Point anchor, double width, double height, AccessoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y) || Math.Abs(anchor.X) > 1e7 || Math.Abs(anchor.Y) > 1e7 ||
            !double.IsFinite(width) || !double.IsFinite(height) || width is <= 0 or > 2048 || height is <= 0 or > 2048 ||
            !double.IsFinite(item.ImagePivotX) || !double.IsFinite(item.ImagePivotY) || item.ImagePivotX is < 0 or > 1 || item.ImagePivotY is < 0 or > 1 ||
            !double.IsFinite(item.Softness) || item.Softness is < 0 or > 100 || !double.IsFinite(item.ConnectionStrength) || item.ConnectionStrength is < 0 or > 100 ||
            !double.IsFinite(item.GravityInfluence) || item.GravityInfluence is < 0 or > 100 || !double.IsFinite(item.RotationDamping) || item.RotationDamping is < 0 or > 100 ||
            !double.IsFinite(item.ReturnStrength) || item.ReturnStrength is < 0 or > 100 || !double.IsFinite(item.MaxSwingDegrees) || item.MaxSwingDegrees is < 0 or > 180 ||
            !double.IsFinite(item.BaseAngleDegrees) || item.BaseAngleDegrees is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(item));
    }

    private static IReadOnlyList<Point> CreateUvs() => Array.AsReadOnly(Enumerable.Range(0, (GridDivisions + 1) * (GridDivisions + 1))
        .Select(i => new Point(i % (GridDivisions + 1) / (double)GridDivisions, i / (GridDivisions + 1) / (double)GridDivisions)).ToArray());
    private static IReadOnlyList<int> CreateTriangles()
    {
        var indices = new List<int>();
        for (var y = 0; y < GridDivisions; y++)
            for (var x = 0; x < GridDivisions; x++)
            {
                var a = y * (GridDivisions + 1) + x; var b = a + 1; var c = a + GridDivisions + 1; var d = c + 1;
                indices.AddRange([a, b, d, a, d, c]);
            }
        return Array.AsReadOnly(indices.ToArray());
    }
}
