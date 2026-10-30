using System.Windows;
using System.Windows.Media;
using Dango.Desktop.Physics.Presentation;

namespace Dango.Desktop.Rendering.Visuals;

public partial class DeformableDangoVisual : System.Windows.Controls.UserControl, IPhysicsPresentationSink
{
    private static readonly StreamGeometry FallbackGeometry = CreateFallbackGeometry();
    private readonly UIElement oldLeftEye;
    private readonly UIElement oldRightEye;
    private readonly System.Windows.Shapes.Path compiledLeftEye = new() { IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Path compiledRightEye = new() { IsHitTestVisible = false };
    private bool candidateEyes;
    private readonly System.Windows.Controls.TextBlock[] dozeLetters;
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
        dozeLetters = Enumerable.Range(0, 3).Select(i => new System.Windows.Controls.TextBlock
        {
            Text = "z",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x72, 0x67, 0x83)),
            IsHitTestVisible = false,
        }).ToArray();
        foreach (var letter in dozeLetters) DozeLayer.Children.Add(letter);
    }

    public DangoContourFailure LastContourFailure { get; private set; }

    internal double? RenderedBodyBottom
    {
        get
        {
            if (Visibility != Visibility.Visible || BodyPath.Visibility != Visibility.Visible ||
                BodyPath.Data is null || BodyPath.Data.Bounds.IsEmpty) return null;
            var bottom = GaitBodyLayer.RenderTransform.TransformBounds(BodyPath.Data.Bounds).Bottom;
            return double.IsFinite(bottom) ? bottom : null;
        }
    }

    // The default WPF template can hit the union of transformed descendant bounds,
    // even when those drawing layers ignore input. Keep the existing root rectangle
    // as the sole input surface; this does not clip the hat or move the drag threshold.
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) =>
        new Rect(RenderSize).Contains(parameters.HitPoint) ? new PointHitTestResult(this, parameters.HitPoint) : null;

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

    private System.Windows.Media.Brush eyeFill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2B, 0x20, 0x1E));
    public System.Windows.Media.Brush EyeFill
    {
        get => eyeFill;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            eyeFill = value;
            ((System.Windows.Shapes.Shape)oldLeftEye).Fill = value;
            ((System.Windows.Shapes.Shape)oldRightEye).Fill = value;
            foreach (var shape in LeftEyeLayer.Children.OfType<System.Windows.Shapes.Shape>().Concat(RightEyeLayer.Children.OfType<System.Windows.Shapes.Shape>()))
                shape.Fill = value;
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

    public void ApplyFrame(PhysicsPresentationFrame frame) => ApplyFrame(frame, null);

    public void ApplyFrame(PhysicsPresentationFrame frame, GaitPresentationFrame? gait)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var transform = CreateGaitTransform(frame, gait);
        GaitBodyLayer.RenderTransform = GaitAttachmentLayer.RenderTransform = transform;
        Visibility = frame.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        DrawDoze(frame);
        if (!frame.IsVisible)
        {
            return;
        }

        var geometry = frame.Diagnostics.Backend == PhysicsPresentationBackend.ContinuousBelt
            ? DangoStreamGeometryFactory.TryCreateBelt(frame.Contour, Width, Height)
            : DangoStreamGeometryFactory.TryCreate(frame.Contour, Width, Height);
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
            DrawEye(LeftEyeLayer, compiledLeftEye, frame.Attachments.Face.Left);
            DrawEye(RightEyeLayer, compiledRightEye, frame.Attachments.Face.Right);
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

    internal static Transform CreateGaitTransform(PhysicsPresentationFrame frame, GaitPresentationFrame? gait)
    {
        if (gait is null || !gait.Matches(frame, gait.EntityId) || !frame.IsVisible ||
            frame.FallbackReason != PhysicsPresentationFallbackReason.None) return Transform.Identity;
        var transform = new ScaleTransform(gait.ScaleX, gait.ScaleY, gait.CenterX, gait.BottomY);
        transform.Freeze();
        return transform;
    }

    private void DrawDoze(PhysicsPresentationFrame frame)
    {
        var visible = frame.IsVisible && ShowAccessories && frame.FallbackReason == PhysicsPresentationFallbackReason.None &&
            frame.DozeSeconds is >= 0 && frame.Attachments?.Face is { Left.Openness: <= .03, Right.Openness: <= .03 };
        DozeLayer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        for (var i = 0; i < dozeLetters.Length; i++)
        {
            var elapsed = frame.DozeSeconds!.Value - i * 1.0;
            var phase = Math.Max(0, elapsed) % 3 / 3;
            var letter = dozeLetters[i];
            letter.FontSize = 12 + phase * 10;
            letter.Opacity = elapsed < 0 ? 0 : .8 * (1 - phase) * Math.Min(1, phase * 30);
            System.Windows.Controls.Canvas.SetLeft(letter, 112 + phase * 30);
            System.Windows.Controls.Canvas.SetTop(letter, 32 - phase * 30);
        }
    }

    private void DrawEye(System.Windows.Controls.Canvas layer, System.Windows.Shapes.Path path,
        Dango.Desktop.Physics.Presentation.Attachments.EyePresentationFrame eye)
    {
        layer.RenderTransform = Transform.Identity;
        if (layer.Children.Count != 1 || !ReferenceEquals(layer.Children[0], path))
        {
            layer.Children.Clear();
            layer.Children.Add(path);
        }
        path.Data = AttachmentGeometryFactory.CreateEye(eye);
        path.Fill = EyeFill;
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
