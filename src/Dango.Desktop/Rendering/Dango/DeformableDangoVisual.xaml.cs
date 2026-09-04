using System.Windows;
using System.Windows.Media;
using Dango.Desktop.Physics.Presentation;

namespace Dango.Desktop.Rendering.Visuals;

public partial class DeformableDangoVisual : System.Windows.Controls.UserControl, IPhysicsPresentationSink
{
    private static readonly StreamGeometry FallbackGeometry = CreateFallbackGeometry();

    public DeformableDangoVisual()
    {
        InitializeComponent();
        FallbackBodyPath.Data = FallbackGeometry;
    }

    public DangoContourFailure LastContourFailure { get; private set; }

    public bool ShowDebugGeometry { get; set; }

    public bool ShowAccessories
    {
        get => LeftEyeLayer.Visibility == Visibility.Visible;
        set => LeftEyeLayer.Visibility = RightEyeLayer.Visibility = HatLayer.Visibility =
            value ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ApplyFrame(PhysicsPresentationFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Visibility = frame.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (!frame.IsVisible)
        {
            return;
        }

        var geometry = DangoStreamGeometryFactory.TryCreate(frame.Contour, Width, Height);
        var useFallback = frame.FallbackReason != PhysicsPresentationFallbackReason.None || !geometry.Succeeded;
        LastContourFailure = geometry.Failure;
        BodyPath.Visibility = useFallback ? Visibility.Collapsed : Visibility.Visible;
        FallbackBodyPath.Visibility = useFallback ? Visibility.Visible : Visibility.Collapsed;
        if (!useFallback)
        {
            BodyPath.Data = geometry.Geometry;
        }
        DrawDebug(frame.Contour, useFallback);

        LeftEyeLayer.RenderTransform = AttachmentTransform(
            frame.LeftEye,
            new PresentationPoint(-PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05),
            false);
        RightEyeLayer.RenderTransform = AttachmentTransform(
            frame.RightEye,
            new PresentationPoint(PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05),
            false);
        HatLayer.RenderTransform = AttachmentTransform(frame.Hat, new PresentationPoint(0, 0.82), true);
    }

    private void DrawDebug(IReadOnlyList<PresentationPoint> contour, bool useFallback)
    {
        DebugLayer.Children.Clear();
        if (!ShowDebugGeometry || useFallback) return;
        var points = contour.Select(point => new System.Windows.Point((point.X + 1) * Width / 2, (1 - point.Y) * Height / 2)).ToArray();
        for (var index = 0; index < points.Length; index++)
        {
            var next = points[(index + 1) % points.Length];
            DebugLayer.Children.Add(new System.Windows.Shapes.Line { X1 = points[index].X, Y1 = points[index].Y, X2 = next.X, Y2 = next.Y, Stroke = System.Windows.Media.Brushes.SaddleBrown, StrokeThickness = 0.7, Opacity = 0.55 });
            DebugLayer.Children.Add(new System.Windows.Shapes.Line { X1 = Width / 2, Y1 = Height / 2, X2 = points[index].X, Y2 = points[index].Y, Stroke = System.Windows.Media.Brushes.Peru, StrokeThickness = 0.45, Opacity = 0.35 });
            var node = new System.Windows.Shapes.Ellipse { Width = 3.5, Height = 3.5, Fill = System.Windows.Media.Brushes.DarkRed };
            System.Windows.Controls.Canvas.SetLeft(node, points[index].X - 1.75);
            System.Windows.Controls.Canvas.SetTop(node, points[index].Y - 1.75);
            DebugLayer.Children.Add(node);
        }
    }

    private static Transform AttachmentTransform(
        PresentationAttachment attachment,
        PresentationPoint baseline,
        bool allowRotation)
    {
        var transforms = new TransformGroup();
        if (allowRotation)
        {
            transforms.Children.Add(new RotateTransform(
                Math.Clamp(attachment.RotationRadians, -0.35, 0.35) * 180 / Math.PI));
        }
        transforms.Children.Add(new TranslateTransform(
            Math.Clamp(attachment.Position.X - baseline.X, -0.2, 0.2) * 72,
            -Math.Clamp(attachment.Position.Y - baseline.Y, -0.2, 0.2) * 58));
        return transforms;
    }

    private static StreamGeometry CreateFallbackGeometry()
    {
        var contour = Enumerable.Range(0, 12)
            .Select(index => (Math.PI * 2 * index) / 12)
            .Select(angle => new PresentationPoint(Math.Cos(angle) * 0.72, Math.Sin(angle) * 0.82))
            .ToArray();
        return DangoStreamGeometryFactory.TryCreate(contour, 160, 120).Geometry!;
    }
}
