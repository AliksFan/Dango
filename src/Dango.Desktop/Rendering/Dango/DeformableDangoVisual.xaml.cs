using System.Windows;
using System.Windows.Media;
using Dango.Desktop.Physics.Presentation;

namespace Dango.Desktop.Rendering.Visuals;

public partial class DeformableDangoVisual : System.Windows.Controls.UserControl, IPhysicsPresentationSink
{
    private static readonly StreamGeometry FallbackGeometry = CreateFallbackGeometry();
    private readonly UIElement oldLeftEye;
    private readonly UIElement oldRightEye;
    private bool candidateEyes;
    private HatAppearance appearance;
    public double HatSize { get; set; } = 1;
    public HatAppearance Appearance
    {
        get => appearance;
        set { if (appearance == value) return; HatAppearanceGeometry.Apply(HatLayer, value); appearance = value; }
    }

    public DeformableDangoVisual()
    {
        InitializeComponent();
        oldLeftEye = LeftEyeLayer.Children[0];
        oldRightEye = RightEyeLayer.Children[0];
        FallbackBodyPath.Data = FallbackGeometry;
    }

    public DangoContourFailure LastContourFailure { get; private set; }

    public bool ShowDebugGeometry { get; set; }

    public System.Windows.Media.Brush BodyFill
    {
        get => BodyPath.Fill;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            BodyPath.Fill = value;
            FallbackBodyPath.Fill = value;
        }
    }

    public bool ShowAccessories
    {
        get => LeftEyeLayer.Visibility == Visibility.Visible;
        set => LeftEyeLayer.Visibility = RightEyeLayer.Visibility = HatLayer.Visibility =
            value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool ShowHat
    {
        get => HatLayer.Visibility == Visibility.Visible;
        set => HatLayer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
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
        var candidateHat = AttachmentGeometryFactory.ApplyHatFrame(HatLayer, useFallback ? null : frame.Attachments?.Hat, HatSize);

        if (frame.Attachments is not null && !useFallback)
        {
            DrawEye(LeftEyeLayer, frame.Attachments.Face.Left);
            DrawEye(RightEyeLayer, frame.Attachments.Face.Right);
            candidateEyes = true;
            if (!candidateHat) HatLayer.RenderTransform = AttachmentGeometryFactory.ScaleHatTransform(AttachmentTransform(frame.Hat, new PresentationPoint(0, 0.82), true), HatSize, true);
            return;
        }
        if (candidateEyes)
        {
            LeftEyeLayer.Children.Clear(); LeftEyeLayer.Children.Add(oldLeftEye);
            RightEyeLayer.Children.Clear(); RightEyeLayer.Children.Add(oldRightEye);
            candidateEyes = false;
        }

        LeftEyeLayer.RenderTransform = AttachmentTransform(
            frame.LeftEye,
            new PresentationPoint(-PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05),
            false);
        RightEyeLayer.RenderTransform = AttachmentTransform(
            frame.RightEye,
            new PresentationPoint(PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05),
            false);
        HatLayer.RenderTransform = AttachmentGeometryFactory.ScaleHatTransform(AttachmentTransform(frame.Hat, new PresentationPoint(0, 0.82), true), HatSize, true);
    }

    private static void DrawEye(System.Windows.Controls.Canvas layer, Dango.Desktop.Physics.Presentation.Attachments.EyePresentationFrame eye)
    {
        layer.RenderTransform = Transform.Identity;
        layer.Children.Clear();
        layer.Children.Add(new System.Windows.Shapes.Path
        {
            Data = AttachmentGeometryFactory.CreateEye(eye),
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2B, 0x20, 0x1E)),
            IsHitTestVisible = false,
        });
    }

    private void DrawDebug(IReadOnlyList<PresentationPoint> contour, bool useFallback)
    {
        DebugLayer.Children.Clear();
        if (!ShowDebugGeometry || useFallback) return;
        var points = contour.Select(point => DangoStreamGeometryFactory.Map(point, Width, Height)).ToArray();
        var corePoint = DangoStreamGeometryFactory.Map(new PresentationPoint(0, 0), Width, Height);
        var core = new System.Windows.Shapes.Ellipse
        {
            Width = 5,
            Height = 5,
            Fill = System.Windows.Media.Brushes.DarkOrange,
        };
        System.Windows.Controls.Canvas.SetLeft(core, corePoint.X - 2.5);
        System.Windows.Controls.Canvas.SetTop(core, corePoint.Y - 2.5);
        DebugLayer.Children.Add(core);
        for (var index = 0; index < points.Length; index++)
        {
            DebugLayer.Children.Add(CreateRadialLine(corePoint, points[index]));
            DebugLayer.Children.Add(CreateContourLine(points[index], points[(index + 1) % points.Length]));
        }
        for (var index = 0; index < points.Length; index++)
        {
            DebugLayer.Children.Add(CreateNode(points[index]));
        }
    }

    private static System.Windows.Shapes.Line CreateRadialLine(System.Windows.Point start, System.Windows.Point end) =>
        new()
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 255, 140, 0)),
            StrokeThickness = 0.8,
        };

    private static System.Windows.Shapes.Line CreateContourLine(System.Windows.Point start, System.Windows.Point end) =>
        new()
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(190, 164, 32, 64)),
            StrokeThickness = 1.2,
        };

    private static System.Windows.Shapes.Ellipse CreateNode(System.Windows.Point point)
    {
        var node = new System.Windows.Shapes.Ellipse
        {
            Width = 3.5,
            Height = 3.5,
            Fill = System.Windows.Media.Brushes.DarkRed,
        };
        System.Windows.Controls.Canvas.SetLeft(node, point.X - 1.75);
        System.Windows.Controls.Canvas.SetTop(node, point.Y - 1.75);
        return node;
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
