using System.Runtime.CompilerServices;
using System.Windows;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Canvas = System.Windows.Controls.Canvas;

namespace Dango.Desktop.Accessories;

/// <summary>Read-only host layers. No host owns accessory motion or a second physical clock.</summary>
internal static class AccessoryHostBinding
{
    private sealed class Layers
    {
        public readonly Canvas Behind = new() { Width = 160, Height = 120, IsHitTestVisible = false, ClipToBounds = false };
        public readonly Canvas Front = new() { Width = 160, Height = 120, IsHitTestVisible = false, ClipToBounds = false };
        public readonly Dictionary<string, AccessoryMeshVisual> Visuals = [];
    }
    private static readonly ConditionalWeakTable<DeformableDangoVisual, Layers> Hosts = new();

    public static void Apply(DeformableDangoVisual body, DangoHatVisual hat,
        StackedPhysicsPresentationFrame frame, int index, LogicalVector translation, StackedDangoSceneRuntime? runtime)
    {
        if (runtime is null || body.Parent is not Canvas parent) return;
        var enabled = runtime.AccessoriesEnabled;
        if (!enabled)
        {
            if (Hosts.TryGetValue(body, out var previous))
            {
                parent.Children.Remove(previous.Behind); parent.Children.Remove(previous.Front);
                Hosts.Remove(body);
            }
            Canvas.SetZIndex(body, 0); Canvas.SetZIndex(hat, 0);
            runtime.ReportAccessoryRenderError(null);
            return;
        }
        var layers = Hosts.GetValue(body, _ =>
        {
            var value = new Layers(); parent.Children.Add(value.Behind); parent.Children.Add(value.Front); return value;
        });
        var active = index < frame.Bodies.Count;
        Canvas.SetZIndex(body, enabled ? index * 4 + 1 : 0);
        Canvas.SetZIndex(hat, enabled ? index * 4 + 2 : 0);
        Canvas.SetZIndex(layers.Behind, index * 4);
        Canvas.SetZIndex(layers.Front, index * 4 + 3);
        if (!active) { layers.Behind.Visibility = layers.Front.Visibility = Visibility.Collapsed; return; }
        var customized = runtime.AppearanceEntity is null ? index == 0 :
            index < frame.EntityIds.Count && frame.EntityIds[index] == runtime.AppearanceEntity;
        if (enabled && customized) hat.Visibility = Visibility.Collapsed;
        var outputs = customized && enabled ? runtime.GetAccessoryFrames(frame, index) : [];
        if (customized) runtime.ReportAccessoryRenderError(null);
        var ids = outputs.Select(output => output.Item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in layers.Visuals.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            var visual = layers.Visuals[id];
            if (visual.Parent is Canvas owner) owner.Children.Remove(visual);
            layers.Visuals.Remove(id);
        }
        var bounds = DangoHostFrameBinding.Bounds(frame, index);
        var requests = new List<(AccessoryMeshVisual Visual, AccessoryMeshFrame Frame)>();
        var names = new Dictionary<AccessoryMeshVisual, string>();
        var behind = new List<AccessoryMeshVisual>();
        var front = new List<AccessoryMeshVisual>();
        foreach (var output in outputs)
        {
            if (!layers.Visuals.TryGetValue(output.Item.Id, out var visual))
                layers.Visuals[output.Item.Id] = visual = new() { Width = 160, Height = 120, IsHitTestVisible = false };
            var target = output.Item.Layer == AccessoryLayer.Behind ? layers.Behind : layers.Front;
            if (!ReferenceEquals(visual.Parent, target))
            {
                if (visual.Parent is Canvas owner) owner.Children.Remove(visual);
                target.Children.Add(visual);
            }
            Canvas.SetZIndex(visual, output.Item.Order);
            (output.Item.Layer == AccessoryLayer.Behind ? behind : front).Add(visual);
            names[visual] = output.Item.Name;
            try
            {
                visual.SetTexture(output.Texture);
                requests.Add((visual, output.Mesh.Translated(new(-bounds.X, -bounds.Y))));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException or System.Runtime.InteropServices.ExternalException)
            {
                visual.ClearFrame();
                runtime.ReportAccessoryRenderError($"配饰“{output.Item.Name}”画面暂不可用（{error.GetType().Name}）；可重试或取消。");
            }
        }
        Reorder(layers.Behind, behind);
        Reorder(layers.Front, front);
        foreach (var (visual, error) in AccessoryMeshVisual.ApplyBatch(requests).Errors)
            runtime.ReportAccessoryRenderError($"配饰“{names[visual]}”画面暂不可用（{error.GetType().Name}）；可重试或取消。");
        layers.Behind.Visibility = layers.Behind.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        layers.Front.Visibility = layers.Front.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Position(body, bounds.X + translation.X, bounds.Y + translation.Y);
    }

    private static void Reorder(Canvas layer, IReadOnlyList<AccessoryMeshVisual> expected)
    {
        if (layer.Children.Count == expected.Count &&
            expected.Select((visual, index) => ReferenceEquals(layer.Children[index], visual)).All(matches => matches)) return;
        foreach (var visual in expected) layer.Children.Remove(visual);
        foreach (var visual in expected) layer.Children.Add(visual);
    }

    public static void Position(DeformableDangoVisual body, double left, double top)
    {
        if (!Hosts.TryGetValue(body, out var layers)) return;
        Canvas.SetLeft(layers.Behind, left); Canvas.SetTop(layers.Behind, top);
        Canvas.SetLeft(layers.Front, left); Canvas.SetTop(layers.Front, top);
    }

    public static void Hide(DeformableDangoVisual body)
    {
        if (Hosts.TryGetValue(body, out var layers)) layers.Behind.Visibility = layers.Front.Visibility = Visibility.Collapsed;
    }

    public static LogicalRect DisplayBounds(StackedPhysicsPresentationFrame frame, int index, StackedDangoSceneRuntime runtime)
    {
        var body = DangoHostFrameBinding.Bounds(frame, index);
        var result = new Rect(body.X, body.Y, body.Width, body.Height);
        foreach (var output in runtime.GetAccessoryFrames(frame, index)) result.Union(output.Mesh.Bounds);
        return new(result.X, result.Y, result.Width, result.Height);
    }
}
