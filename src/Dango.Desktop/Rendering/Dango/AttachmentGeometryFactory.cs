using System.Windows.Media;
using Dango.Desktop.Physics.Presentation.Attachments;
using Color = System.Windows.Media.Color;

namespace Dango.Desktop.Rendering.Visuals;

/// <summary>One calibrated leaf asset and transform, for both attachment render paths.</summary>
public static class AttachmentGeometryFactory
{
    public static Transform ScaleHatTransform(Transform placement, double size, bool legacy)
    {
        if (!double.IsFinite(size) || size is < 0.5 or > 2) throw new ArgumentOutOfRangeException(nameof(size));
        if (size == 1) return placement;
        var result = new TransformGroup();
        // Legacy Canvas origin is (80,15); candidate transforms use absolute material coordinates.
        result.Children.Add(new ScaleTransform(size, size, legacy ? 0 : 80, legacy ? 10 : 25));
        result.Children.Add(placement); result.Freeze(); return result;
    }

    public static bool ApplyHatFrame(System.Windows.Controls.Canvas layer, HatMotionFrame? hat, double size = 1)
    {
        HatDeformationGeometry.Apply(layer, hat?.Follow.Bend ?? 0, hat?.Follow.Stretch ?? 0);
        layer.RenderTransformOrigin = hat is null ? new(0.5, 0.125) : new(0, 0);
        layer.Opacity = hat is not null && hat.Pose is null ? 0 : 1;
        if (hat is null) return false;
        layer.RenderTransform = hat.Pose is null ? Transform.Identity : ScaleHatTransform(CreateStaticHat(hat.Pose).Transform, size, false);
        return true;
    }
    public static Geometry CreateEye(EyePresentationFrame eye)
    {
        ArgumentNullException.ThrowIfNull(eye);
        if (!double.IsFinite(eye.Center.X) || !double.IsFinite(eye.Center.Y) || Math.Abs(eye.Center.X) > 1 || Math.Abs(eye.Center.Y) > 1 ||
            !double.IsFinite(eye.Width) || eye.Width is <= 0 or > 0.075 || !double.IsFinite(eye.Height) || eye.Height is <= 0 or > 0.075 ||
            !double.IsFinite(eye.Angle) || Math.Abs(eye.Angle) > Math.PI / 18 + 1e-10 || !double.IsFinite(eye.Openness) || eye.Openness is < 0 or > 1 ||
            !double.IsFinite(eye.Strength) || eye.Strength is < 0 or > 1 || !Enum.IsDefined(eye.Shape))
            throw new ArgumentException("Eye frame is invalid.", nameof(eye));
        if (!eye.Visible) return Geometry.Empty;
        Geometry result;
        if (eye.Openness == 1 && (eye.Shape == Dango.Core.Animation.EyeShape.Round ||
            eye.Shape == Dango.Core.Animation.EyeShape.Soft && eye.Strength == 0))
        {
            var center = new System.Windows.Point(80 + eye.Center.X * 160, 66 - eye.Center.Y * 160);
            result = new EllipseGeometry(center, eye.Width * 80, eye.Height * 80)
            { Transform = new RotateTransform(-eye.Angle * 180 / Math.PI, center.X, center.Y) };
        }
        else
        {
            var artwork = EyeArtwork.Create(eye);
            Geometry Paths(IReadOnlyList<IReadOnlyList<AttachmentPoint>> paths)
            {
                var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
                using (var context = geometry.Open())
                    foreach (var path in paths)
                    {
                        var points = path.Select(p => new System.Windows.Point(80 + p.X * 160, 66 - p.Y * 160)).ToArray();
                        context.BeginFigure(points[0], true, true);
                        context.PolyLineTo(points.Skip(1).ToArray(), true, false);
                    }
                geometry.Freeze(); return geometry;
            }
            var fills = Paths(artwork.Fills);
            // Exclusion clips the hole to the ink; a closing ring cannot create stray outside ink.
            result = artwork.Holes.Count == 0 ? fills : new CombinedGeometry(GeometryCombineMode.Exclude, fills, Paths(artwork.Holes));
        }
        result.Freeze(); return result;
    }

    private static readonly string[] HatPaths =
    [
        "M77,25 C77,20 78,16 80,12 C83,16 84,21 83,26 Z",
        "M80,15 C71,15 64,11 62,4 C70,2 77,5 80,15 Z",
        "M80,15 C89,15 96,11 98,4 C90,2 83,5 80,15 Z",
    ];

    public static DrawingGroup CreateStaticHat(HatContactPose pose)
    {
        ArgumentNullException.ThrowIfNull(pose);
        if (!double.IsFinite(pose.Angle) || Math.Abs(pose.Angle) > 0.35 || !double.IsFinite(pose.Center.X) || !double.IsFinite(pose.Center.Y) ||
            Math.Abs(pose.Center.X) > 1 || Math.Abs(pose.Center.Y) > 1 || Math.Abs(pose.ContactWidth - HatTrackSolver.BaseWidth) > 1e-12 ||
            !double.IsFinite(pose.MaximumGap) || pose.MaximumGap is < 0 or > 0.005 ||
            !double.IsFinite(pose.MaximumPenetration) || pose.MaximumPenetration is < 0 or > 0.007)
            throw new ArgumentException("Hat contact pose is not safe to render.", nameof(pose));
        return CreateDrawing(pose.Center, pose.Angle);
    }

    public static DrawingGroup CreateStaticHat(HatVisualPose pose)
    {
        ArgumentNullException.ThrowIfNull(pose);
        if (!double.IsFinite(pose.Angle) || Math.Abs(pose.Angle) > 0.35 || !double.IsFinite(pose.Center.X) || !double.IsFinite(pose.Center.Y) ||
            Math.Abs(pose.Center.X) > 1 || Math.Abs(pose.Center.Y) > 1 || !double.IsFinite(pose.S))
            throw new ArgumentException("Hat visual pose is not safe to render.", nameof(pose));
        // The intentional legacy inset is not a surface penetration failure.
        return CreateDrawing(pose.Center, pose.Angle);
    }

    private static DrawingGroup CreateDrawing(AttachmentPoint center, double angle)
    {
        var drawing = new DrawingGroup();
        for (var i = 0; i < HatPaths.Length; i++)
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(i == 0 ? Color.FromRgb(0x67, 0xB8, 0x4E) : Color.FromRgb(0x78, 0xC8, 0x5A)), null, Geometry.Parse(HatPaths[i])));
        var transform = new TransformGroup();
        transform.Children.Add(new TranslateTransform(-80, -25));
        transform.Children.Add(new RotateTransform(-angle * 180 / Math.PI));
        transform.Children.Add(new TranslateTransform(80 + center.X * 160, 66 - center.Y * 160));
        drawing.Transform = transform;
        drawing.Freeze();
        return drawing;
    }
}
