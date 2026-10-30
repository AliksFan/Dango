using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Desktop.Accessories;
using Dango.Desktop.Rendering.Visuals;
using Canvas = System.Windows.Controls.Canvas;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    // This layer owns only rest geometry. It never shares or advances a worn item's solver.
    private readonly Canvas accessoryAnchorGhost = new()
    {
        Width = 160, Height = 120, IsHitTestVisible = false,
        Opacity = .45, Visibility = Visibility.Collapsed,
    };
    private string? accessoryAnchorPickItemId;
    internal Canvas AccessoryAnchorGhost => accessoryAnchorGhost;

    private void InitializeAccessoryAnchorGhost()
    {
        PetVisualArea.Children.Add(accessoryAnchorGhost);
        accessoryAnchorGhost.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        accessoryAnchorGhost.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        PetPreview.MouseMove += (_, e) => UpdateAccessoryAnchorGhost(e.GetPosition(PetPreview));
        PetPreview.MouseLeave += (_, _) => accessoryAnchorGhost.Visibility = Visibility.Collapsed;
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelAccessoryAnchorPick(); };
        Unloaded += (_, _) => CancelAccessoryAnchorPick();
        accessoryPanel.PreviewChanged += _ => CancelAccessoryAnchorPick();
    }

    internal bool BeginAccessoryAnchorPick()
    {
        CancelAccessoryAnchorPick();
        if (!IsAccessoryPreview || accessoryPanel.SelectedItem is not { } item) return false;
        try
        {
            var project = accessoryPanel.ResolvePreviewProject(item);
            var longest = 64 * item.SizePercent / 100;
            var ratio = item.PixelWidth / (double)item.PixelHeight;
            var width = ratio >= 1 ? longest : longest * ratio;
            var height = ratio >= 1 ? longest / ratio : longest;
            var frames = new AccessoryMotionSolver().RefreshPieces(default, width, height, item, project);
            for (var i = 0; i < frames.Count; i++)
            {
                var texture = item.DeformationMode == AccessoryDeformationMode.Skeleton
                    ? project!.Assets[project.RenderPieces[i].AssetId].Bitmap
                    : AccessoryItem.IsBuiltInAsset(item.AssetId) ? BuiltinAccessoryImages.Get(item.AssetId)
                    : accessoryPanel.ResolvePreviewImage(item.AssetId);
                if (texture is null) throw new InvalidDataException("配饰图片不可用。");
                var visual = new AccessoryMeshVisual();
                visual.SetTexture(texture); visual.ApplyFrame(frames[i]);
                accessoryAnchorGhost.Children.Add(visual);
            }
            ClearLiveDirectionalInput();
            accessoryAnchorPickItemId = item.Id;
            pickingAccessoryAnchor = true;
            PetPreview.Cursor = Cursors.Cross;
            PetPreview.ToolTip = "点击团子选择附着位置 · Esc 取消";
            CompositionTarget.Rendering += PresentAccessoryAnchorGhost;
            if (PetPreview.IsMouseOver) UpdateAccessoryAnchorGhost(Mouse.GetPosition(PetPreview));
            return true;
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or ArithmeticException)
        {
            CancelAccessoryAnchorPick();
            PetPreview.ToolTip = "配饰预览不可用，请重新选择配饰";
            return false;
        }
    }

    private void PresentAccessoryAnchorGhost(object? sender, EventArgs e)
    {
        if (!IsAccessoryPreview || !IsVisible || accessoryPanel.SelectedItem?.Id != accessoryAnchorPickItemId)
        { CancelAccessoryAnchorPick(); return; }
        if (PetPreview.IsMouseOver) UpdateAccessoryAnchorGhost(Mouse.GetPosition(PetPreview));
        else accessoryAnchorGhost.Visibility = Visibility.Collapsed;
    }

    internal bool UpdateAccessoryAnchorGhost(Point previewPoint)
    {
        accessoryAnchorGhost.Visibility = Visibility.Collapsed;
        if (!pickingAccessoryAnchor) return false;
        if (!IsAccessoryPreview || accessoryPanel.SelectedItem?.Id != accessoryAnchorPickItemId)
        { CancelAccessoryAnchorPick(); return false; }
        if (!TryResolveAccessoryAnchor(previewPoint, out _, out var transform)) return false;
        accessoryAnchorGhost.RenderTransform = transform;
        accessoryAnchorGhost.Visibility = Visibility.Visible;
        return true;
    }

    private bool TryResolveAccessoryAnchor(Point previewPoint, out Point anchor, out Transform transform)
    {
        anchor = default; transform = Transform.Identity;
        if (!double.IsFinite(previewPoint.X) || !double.IsFinite(previewPoint.Y) ||
            previewPoint.X < 0 || previewPoint.Y < 0 || previewPoint.X > PetPreview.ActualWidth || previewPoint.Y > PetPreview.ActualHeight)
            return false;
        try
        {
            FrameworkElement source;
            Dango.Desktop.Physics.Presentation.PhysicsPresentationFrame body;
            Dango.Desktop.Physics.Presentation.GaitPresentationFrame? gait;
            if (beltPresenter is not null)
            {
                source = beltPresenter.View;
                if (!beltPresenter.TryPickBodyAnchor(PetPreview.TranslatePoint(previewPoint, source), out anchor) ||
                    beltPresenter.Inspect().Body is not { } displayedBody) return false;
                body = displayedBody; gait = beltPresenter.GaitFrame;
            }
            else
            {
                if (BoundRuntime?.LastFrame is not { Bodies.Count: > 0 } frame ||
                    LiveDangoVisual.Visibility != Visibility.Visible || LiveDangoVisual.LastContourFailure != DangoContourFailure.None)
                    return false;
                source = LiveDangoVisual; body = frame.Bodies[0]; gait = frame.Gait;
                anchor = AccessoryBodyMapper.Unmap(body, gait, PetPreview.TranslatePoint(previewPoint, source));
            }
            var rawAnchor = AccessoryBodyMapper.Map(body, null, anchor.X, anchor.Y);
            transform = ProjectAccessoryAnchorGhost(rawAnchor, DeformableDangoVisual.CreateGaitTransform(body, gait),
                source.TransformToVisual(PetVisualArea));
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException)
        { return false; }
    }

    internal static MatrixTransform ProjectAccessoryAnchorGhost(Point rawAnchor, Transform gait, GeneralTransform sourceToOverlay)
    {
        Point Project(Point point) => sourceToOverlay.Transform(gait.Transform(point + (Vector)rawAnchor));
        var origin = Project(default); var x = Project(new(1, 0)) - origin; var y = Project(new(0, 1)) - origin;
        return new(new Matrix(x.X, x.Y, y.X, y.Y, origin.X, origin.Y));
    }

    internal bool CommitAccessoryAnchorPick(Point previewPoint)
    {
        if (!pickingAccessoryAnchor || !IsAccessoryPreview || accessoryPanel.SelectedItem?.Id != accessoryAnchorPickItemId)
        { CancelAccessoryAnchorPick(); return false; }
        if (!TryResolveAccessoryAnchor(previewPoint, out var anchor, out _))
        { accessoryAnchorGhost.Visibility = Visibility.Collapsed; PetPreview.ToolTip = "等待有效身体画面后重试"; return false; }
        CancelAccessoryAnchorPick();
        accessoryPanel.SetBodyAnchor(anchor);
        return true;
    }

    internal void CancelAccessoryAnchorPick()
    {
        CompositionTarget.Rendering -= PresentAccessoryAnchorGhost;
        pickingAccessoryAnchor = false; accessoryAnchorPickItemId = null;
        accessoryAnchorGhost.Visibility = Visibility.Collapsed;
        accessoryAnchorGhost.Children.Clear();
        PetPreview.Cursor = Cursors.Arrow;
        PetPreview.ToolTip = "同一只实时物理团子";
    }

    private bool TryCancelAccessoryAnchorPick(KeyEventArgs e)
    {
        if (!pickingAccessoryAnchor || e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None) return false;
        CancelAccessoryAnchorPick(); e.Handled = true; return true;
    }
}
