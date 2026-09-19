using System.Windows;
using System.Windows.Media;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Physics;
using Canvas = System.Windows.Controls.Canvas;
using Color = System.Windows.Media.Color;

namespace Dango.Desktop.Rendering.Visuals;

internal static class DangoHostFrameBinding
{
    private static readonly SolidColorBrush[] Colors =
    [
        CreateBrush(255, 232, 194), CreateBrush(201, 226, 255), CreateBrush(221, 204, 248),
    ];

    public static LogicalRect Bounds(StackedPhysicsPresentationFrame frame, int index) =>
        frame.AuthoritativeBounds[index].Translate(frame.RenderOffsets[index]);

    public static DangoEntityId? HitTest(StackedPhysicsPresentationFrame frame, LogicalPoint pointer)
    {
        // Same back-to-front order as the scene canvas; hats never intercept input.
        for (var i = frame.EntityIds.Count - 1; i >= 0; i--)
            if (Bounds(frame, i).Contains(pointer)) return frame.EntityIds[i];
        return null;
    }

    public static void Apply(DeformableDangoVisual body, DangoHatVisual hat,
        StackedPhysicsPresentationFrame frame, int index, LogicalVector translation = default,
        HatAppearance appearance = HatAppearance.Sprout, double hatSize = 1)
    {
        var active = index < frame.Bodies.Count;
        body.Visibility = hat.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        body.Tag = active && index < frame.EntityIds.Count ? frame.EntityIds[index] : null;
        if (!active) return;
        body.Appearance = hat.Appearance = appearance;
        body.HatSize = hat.HatSize = hatSize;
        var bounds = Bounds(frame, index).Translate(translation);
        Canvas.SetLeft(body, bounds.X); Canvas.SetTop(body, bounds.Y);
        Canvas.SetLeft(hat, bounds.X); Canvas.SetTop(hat, bounds.Y);
        if (index < frame.ColorSlots.Count) body.BodyFill = Colors[frame.ColorSlots[index]];
        body.ApplyFrame(frame.Bodies[index]);
        hat.ApplyFrame(frame.Bodies[index]);
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
