using System.Windows;
using System.Windows.Media;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Physics;
using Dango.Desktop.Accessories;
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
        HatAppearance appearance = HatAppearance.Sprout, double hatSize = 1, System.Windows.Media.Brush? bodyFillOverride = null, System.Windows.Media.Brush? eyeFillOverride = null, DangoEntityId? appearanceEntity = null,
        StackedDangoSceneRuntime? runtime = null)
    {
        var active = index < frame.Bodies.Count;
        var suppressLegacyHat = active && runtime is { AccessoriesEnabled: true } && body.Parent is Canvas &&
            (runtime.AppearanceEntity is null ? index == 0 :
                index < frame.EntityIds.Count && frame.EntityIds[index] == runtime.AppearanceEntity);
        body.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        hat.Visibility = active && !suppressLegacyHat ? Visibility.Visible : Visibility.Collapsed;
        body.Tag = active && index < frame.EntityIds.Count ? frame.EntityIds[index] : null;
        if (!active) { AccessoryHostBinding.Hide(body); return; }
        body.Appearance = appearance;
        body.HatSize = hatSize;
        if (!suppressLegacyHat)
        {
            hat.Appearance = appearance;
            hat.HatSize = hatSize;
        }
        var bounds = Bounds(frame, index).Translate(translation);
        ApplyBodyScale(body, bounds.Width / 160);
        ApplyBodyScale(hat, bounds.Width / 160);
        Canvas.SetLeft(body, bounds.X); Canvas.SetTop(body, bounds.Y);
        Canvas.SetLeft(hat, bounds.X); Canvas.SetTop(hat, bounds.Y);
        var customized = appearanceEntity is null ? index == 0 : index < frame.EntityIds.Count && frame.EntityIds[index] == appearanceEntity;
        if (customized && bodyFillOverride is not null) body.BodyFill = bodyFillOverride;
        else if (index < frame.ColorSlots.Count) body.BodyFill = Colors[frame.ColorSlots[index]];
        body.EyeFill = customized && eyeFillOverride is not null ? eyeFillOverride : DefaultEyeFill;
        var gait = frame.Gait is { } candidate && index < frame.EntityIds.Count &&
            candidate.Matches(frame.Bodies[index], frame.EntityIds[index]) ? candidate : null;
        body.ApplyFrame(frame.Bodies[index], gait);
        // Accessory layers replace only this Canvas host's customized legacy
        // visual. Runtime Hat/Gait state still advances and restores on this frame.
        if (!suppressLegacyHat) hat.ApplyFrame(frame.Bodies[index], gait);
        AccessoryHostBinding.Apply(body, hat, frame, index, translation, runtime);
    }

    private static void ApplyBodyScale(FrameworkElement element, double scale)
    {
        if (scale == 1)
        {
            element.RenderTransform = Transform.Identity;
            return;
        }
        if (element.RenderTransform is ScaleTransform current && current.ScaleX == scale && current.ScaleY == scale)
            return;
        var transform = new ScaleTransform(scale, scale);
        transform.Freeze();
        element.RenderTransform = transform;
    }

    private static readonly SolidColorBrush DefaultEyeFill = CreateBrush(0x2B, 0x20, 0x1E);

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
