using System.Windows;
using System.Windows.Media;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using SystemColors = System.Windows.SystemColors;

namespace Dango.Desktop.AppShell.Rooms;

internal sealed partial class LocalRoomSceneView
{
    internal IReadOnlyList<OrdinaryRoomFurnitureRenderItem> FurnitureItems { get; private set; } = [];
    internal FurnitureItemInstance? FurniturePreviewItem { get; private set; }
    internal Guid? FurnitureSelectedInstanceId { get; private set; }
    internal bool FurniturePreviewAllowed { get; private set; }
    // Explicit single-platform conveniences for the older isolated verifier.
    internal FurniturePlatformInstance? FurnitureApplied => FurnitureItems.Count == 1 && FurnitureItems[0].Kind == FurnitureItemKind.StaticPlatform
        ? new(FurnitureItems[0].InstanceId, FurnitureItems[0].Anchor) : null;
    internal FurniturePlatformInstance? FurniturePreview => FurniturePreviewItem is { Kind: FurnitureItemKind.StaticPlatform } item
        ? new(item.InstanceId, item.Anchor) : null;
    internal bool FurnitureSelected => FurnitureSelectedInstanceId is not null;

    internal void SetFurnitureSelected(Guid? selectedInstanceId)
    {
        if (selectedInstanceId == FurnitureSelectedInstanceId) return;
        FurnitureSelectedInstanceId = selectedInstanceId;
        InvalidateVisual();
    }

    internal void SetFurnitureSelected(bool selected) => SetFurnitureSelected(selected && FurnitureItems.Count == 1 ? FurnitureItems[0].InstanceId : null);

    internal void SetFurnitureFrame(IReadOnlyList<OrdinaryRoomFurnitureRenderItem> applied, FurnitureItemInstance? preview, bool canConfirm)
    {
        if (FurnitureItems.SequenceEqual(applied) && FurniturePreviewItem == preview && FurniturePreviewAllowed == canConfirm) return;
        FurnitureItems = applied.ToArray();
        FurniturePreviewItem = preview;
        FurniturePreviewAllowed = canConfirm;
        InvalidateVisual();
    }

    internal void SetFurnitureFrame(FurniturePlatformInstance? applied, FurniturePlatformInstance? preview, bool canConfirm) =>
        SetFurnitureFrame(applied is null ? [] : [new(applied.InstanceId, FurnitureItemKind.StaticPlatform, applied.Anchor, 0)],
            preview is null ? null : new(preview.InstanceId, FurnitureItemKind.StaticPlatform, preview.Anchor), canConfirm);

    // Furniture and body use the same neutral render alignment. The stored
    // height is relative to the real floor, never the visually lifted floor.
    internal Point AnchorToScreen(FurnitureAnchor anchor)
    {
        var point = Projection.WorldToScreen(new(anchor.X, groundY + cameraBodyAlignmentY - anchor.Y));
        return new(point.X, point.Y);
    }

    internal FurnitureAnchor ScreenToFurnitureAnchor(Point screen)
    {
        var point = Projection.ScreenToWorld(new(screen.X, screen.Y));
        return new(point.X, groundY + cameraBodyAlignmentY - point.Y);
    }

    internal Guid? HitTestFurniture(Point point)
    {
        // Reverse paint order resolves the frontmost collider without replacing
        // its instance identity with a preset or a collection index.
        for (var index = FurnitureItems.Count - 1; index >= 0; index--)
        {
            var item = FurnitureItems[index];
            if (FurnitureHitTest(item.Kind, item.Anchor, point)) return item.InstanceId;
        }
        return null;
    }
    internal bool FurnitureAt(Point point) => HitTestFurniture(point) is not null;
    internal bool IsFurniturePreviewHit(Point point) => FurniturePreviewItem is { } preview && FurnitureHitTest(preview.Kind, preview.Anchor, point);

    internal bool FurnitureHitTest(FurnitureItemKind kind, FurnitureAnchor anchor, Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return false;
        if (kind == FurnitureItemKind.StaticPlatform) return FurnitureRectangle(anchor).Contains(point);
        if (kind != FurnitureItemKind.RoundBall) return false;
        var center = AnchorToScreen(anchor);
        var radius = FurnitureLayoutContract.OrdinaryRoom.BallRadius * Projection.Scale;
        var delta = point - center;
        return delta.LengthSquared <= radius * radius;
    }

    internal RoomCameraSnapshot CaptureFurnitureCamera() => Camera.CaptureState();
    internal void SetFurnitureEditorOcclusion(double width)
    {
        if (DesktopPetPresentationEnabled || !Camera.SetRightPresentationOcclusion(width)) return;
        InputInvalidated?.Invoke();
        ClearWheel();
        UpdateProjection(0, advanceCamera: false);
        ViewChanged?.Invoke();
    }

    internal void RestoreFurnitureCamera(RoomCameraSnapshot snapshot)
    {
        Camera.RestoreState(snapshot);
        UpdateProjection(0, advanceCamera: false);
        ViewChanged?.Invoke();
    }

    private Rect FurnitureRectangle(FurnitureAnchor anchor)
    {
        var center = AnchorToScreen(anchor);
        var contract = FurnitureLayoutContract.OrdinaryRoom;
        return new(center.X - contract.PlatformWidth * Projection.Scale / 2, center.Y,
            contract.PlatformWidth * Projection.Scale, contract.PlatformHeight * Projection.Scale);
    }

    internal static double BallWpfAngleDegrees(double nativeAngleRadians) => -nativeAngleRadians * 180 / Math.PI;

    private void DrawFurniture(DrawingContext context)
    {
        if (!HasHealthyFrame || DesktopPetPresentationEnabled) return;
        foreach (var item in FurnitureItems)
        {
            if (item.Kind == FurnitureItemKind.StaticPlatform)
                context.DrawImage(OfficialWoodPlatformVisual.CurrentImage, FurnitureRectangle(item.Anchor));
            else if (item.Kind == FurnitureItemKind.RoundBall)
            {
                var center = AnchorToScreen(item.Anchor);
                var radius = FurnitureLayoutContract.OrdinaryRoom.BallRadius * Projection.Scale;
                context.PushTransform(new RotateTransform(BallWpfAngleDegrees(item.AngleRadians), center.X, center.Y));
                context.DrawImage(OfficialRoundBallVisual.CurrentImage, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
                context.Pop();
            }
            if (item.InstanceId == FurnitureSelectedInstanceId)
            {
                var selection = SystemParameters.HighContrast ? SystemColors.HighlightBrush : new SolidColorBrush(Color.FromRgb(136, 94, 62));
                DrawFurnitureOutline(context, item.Kind, item.Anchor, new Pen(selection, 1.5), 3);
            }
        }
        if (FurniturePreviewItem is { } preview)
        {
            var ink = SystemParameters.HighContrast ? SystemColors.HighlightBrush :
                new SolidColorBrush(FurniturePreviewAllowed ? Color.FromRgb(136, 94, 62) : Color.FromRgb(162, 61, 50));
            var pen = new Pen(ink, 1.5) { DashStyle = DashStyles.Dash };
            DrawFurnitureOutline(context, preview.Kind, preview.Anchor, pen, 0);
        }
    }

    private void DrawFurnitureOutline(DrawingContext context, FurnitureItemKind kind, FurnitureAnchor anchor, Pen pen, double margin)
    {
        if (kind == FurnitureItemKind.StaticPlatform)
        {
            var rectangle = FurnitureRectangle(anchor); rectangle.Inflate(margin, margin);
            context.DrawRoundedRectangle(null, pen, rectangle, 3, 3);
        }
        else if (kind == FurnitureItemKind.RoundBall)
        {
            var radius = FurnitureLayoutContract.OrdinaryRoom.BallRadius * Projection.Scale + margin;
            context.DrawEllipse(null, pen, AnchorToScreen(anchor), radius, radius);
        }
    }
}
