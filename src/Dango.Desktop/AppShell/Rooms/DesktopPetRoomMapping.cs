using System.Windows;
using Dango.Core.Coordinates;

namespace Dango.Desktop.AppShell.Rooms;

// One fixed, invertible projection is shared by the wallpaper, pet and pointer.
internal readonly record struct DesktopPetRoomMapping(RoomProjection Projection, LogicalRect SafeSceneBounds)
{
    internal static DesktopPetRoomMapping Create(double width, double height, Thickness insets, double groundY)
    {
        var safe = new LogicalRect(insets.Left, insets.Top,
            width - insets.Left - insets.Right, height - insets.Top - insets.Bottom);
        if (!double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(groundY) ||
            !double.IsFinite(safe.X) || !double.IsFinite(safe.Y) || !double.IsFinite(safe.Width) || !double.IsFinite(safe.Height) ||
            safe.Width <= 32 || safe.Height <= 120)
            throw new InvalidOperationException("主屏安全区域不足以呈现桌宠房间。");
        // Keep the requested multiplier relative to the previous default, while
        // removing its extra screen inset. Zoom the shared world around its centre.
        var scale = (safe.Width - 32) / (LocalRoomSceneView.WorldWidth - 2 * LocalRoomSceneView.SideBoundaryInset) * 1.4;
        return new(new(scale, safe.X + safe.Width / 2 - LocalRoomSceneView.WorldWidth / 2 * scale,
            safe.Bottom - groundY * scale), safe);
    }

    internal LogicalRect SafeWorldBounds
    {
        get
        {
            var topLeft = Projection.ScreenToWorld(new(SafeSceneBounds.X, SafeSceneBounds.Y));
            return new(topLeft.X, topLeft.Y, SafeSceneBounds.Width / Projection.Scale, SafeSceneBounds.Height / Projection.Scale);
        }
    }

    internal LogicalRect Surface => SafeWorldBounds;
}
