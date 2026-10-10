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
    internal FurniturePlatformInstance? FurnitureApplied { get; private set; }
    internal FurniturePlatformInstance? FurniturePreview { get; private set; }
    internal bool FurniturePreviewAllowed { get; private set; }
    internal bool FurnitureSelected { get; private set; }
    internal void SetFurnitureSelected(bool selected)
    {
        if (selected == FurnitureSelected) return;
        FurnitureSelected = selected;
        InvalidateVisual();
    }

    internal void SetFurnitureFrame(FurniturePlatformInstance? applied, FurniturePlatformInstance? preview, bool canConfirm)
    {
        if (FurnitureApplied == applied && FurniturePreview == preview && FurniturePreviewAllowed == canConfirm) return;
        FurnitureApplied = applied;
        FurniturePreview = preview;
        FurniturePreviewAllowed = canConfirm;
        InvalidateVisual();
    }

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

    internal bool FurnitureAt(Point point) => FurnitureApplied is { } item && FurnitureRectangle(item.Anchor).Contains(point);

    internal RoomCameraSnapshot CaptureFurnitureCamera() => Camera.CaptureState();
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

    private void DrawFurniture(DrawingContext context)
    {
        if (!HasHealthyFrame || DesktopPetPresentationEnabled) return;
        if (FurnitureApplied is { } item)
        {
            var rectangle = FurnitureRectangle(item.Anchor);
            context.DrawImage(OfficialWoodPlatformVisual.CurrentImage, rectangle);
            if (FurnitureSelected)
            {
                var selection = SystemParameters.HighContrast ? SystemColors.HighlightBrush : new SolidColorBrush(Color.FromRgb(136, 94, 62));
                rectangle.Inflate(3, 3);
                context.DrawRoundedRectangle(null, new Pen(selection, 1.5), rectangle, 3, 3);
            }
        }
        if (FurniturePreview is { } preview)
        {
            var ink = SystemParameters.HighContrast ? SystemColors.HighlightBrush :
                new SolidColorBrush(FurniturePreviewAllowed ? Color.FromRgb(136, 94, 62) : Color.FromRgb(162, 61, 50));
            var pen = new Pen(ink, 1.5) { DashStyle = DashStyles.Dash };
            context.DrawRoundedRectangle(null, pen, FurnitureRectangle(preview.Anchor), 2, 2);
        }
    }
}
