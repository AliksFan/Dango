using System.Windows.Media;
using Dango.Desktop.Physics.Presentation;

namespace Dango.Desktop.Rendering.Visuals;

public partial class DangoHatVisual : System.Windows.Controls.UserControl
{
    private HatAppearance appearance;
    public double HatSize { get; set; } = 1;
    public HatAppearance Appearance
    {
        get => appearance;
        set { if (appearance == value) return; HatAppearanceGeometry.Apply(HatLayer, value); appearance = value; }
    }
    public DangoHatVisual()
    {
        InitializeComponent();
    }

    public void ApplyFrame(PhysicsPresentationFrame frame) => ApplyFrame(frame, null);

    public void ApplyFrame(PhysicsPresentationFrame frame, GaitPresentationFrame? gait)
    {
        ArgumentNullException.ThrowIfNull(frame);
        GaitLayer.RenderTransform = DeformableDangoVisual.CreateGaitTransform(frame, gait);
        Visibility = frame.IsVisible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (!AttachmentGeometryFactory.ApplyHatFrame(HatLayer,
            frame.FallbackReason == PhysicsPresentationFallbackReason.None ? frame.Attachments?.Hat : null, HatSize))
            HatLayer.RenderTransform = AttachmentGeometryFactory.ScaleHatTransform(AttachmentTransform(frame.Hat), HatSize, true);
    }

    private static Transform AttachmentTransform(PresentationAttachment attachment)
    {
        var transforms = new TransformGroup();
        transforms.Children.Add(new RotateTransform(
            Math.Clamp(attachment.RotationRadians, -0.35, 0.35) * 180 / Math.PI));
        transforms.Children.Add(new TranslateTransform(
            Math.Clamp(attachment.Position.X, -0.2, 0.2) * 72,
            -Math.Clamp(attachment.Position.Y - 0.82, -0.2, 0.2) * 72));
        return transforms;
    }
}
