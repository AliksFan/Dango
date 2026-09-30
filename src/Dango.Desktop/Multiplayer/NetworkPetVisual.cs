using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Multiplayer;

/// <summary>Reuses the accepted body/eyes/hat control. The ground-pivot transform also applies in fallback.</summary>
public sealed class NetworkPetVisual : Canvas
{
    private readonly DeformableDangoVisual visual = new() { IsHitTestVisible = false, HatSize = 1.35 };
    public NetworkPetVisual()
    {
        Width = 160; Height = 120; ClipToBounds = false; IsHitTestVisible = false;
        Children.Add(visual);
    }
    public long RenderedFrameCount { get; private set; }
    public NetworkPetFrame? CurrentFrame { get; private set; }
    public void ApplyFrame(NetworkPetFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Dispatcher.VerifyAccess();
        visual.ApplyFrame(frame.Presentation);
        var transform = new ScaleTransform(frame.ScaleX, frame.ScaleY, 80, frame.BottomY);
        transform.Freeze(); visual.RenderTransform = transform;
        CurrentFrame = frame; RenderedFrameCount++;
    }
    internal FrameworkElement BodyVisual => visual;
}
