using System.Windows;
using Dango.Core.Coordinates;

namespace Dango.Desktop.AppShell.Rooms;

// One fixed, invertible projection is shared by the wallpaper, pet and pointer.
internal readonly record struct DesktopPetRoomMapping(RoomProjection Projection, LogicalRect SafeSceneBounds)
{
    internal static DesktopPetRoomMapping Create(double width, double height, Thickness insets, double groundY)
    {
        var safe = new LogicalRect(insets.Left + 16, insets.Top + 16,
            width - insets.Left - insets.Right - 32, height - insets.Top - insets.Bottom - 32);
        if (!double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(groundY) ||
            !double.IsFinite(safe.X) || !double.IsFinite(safe.Y) || !double.IsFinite(safe.Width) || !double.IsFinite(safe.Height) ||
            safe.Width <= 0 || safe.Height <= 88)
            throw new InvalidOperationException("主屏安全区域不足以呈现桌宠房间。");
        var scale = safe.Width / (LocalRoomSceneView.WorldWidth - 2 * LocalRoomSceneView.SideBoundaryInset);
        return new(new(scale, safe.X - LocalRoomSceneView.SideBoundaryInset * scale,
            safe.Bottom - 48 - groundY * scale), safe);
    }

    internal LogicalRect SafeWorldBounds
    {
        get
        {
            var topLeft = Projection.ScreenToWorld(new(SafeSceneBounds.X, SafeSceneBounds.Y));
            return new(topLeft.X, topLeft.Y, SafeSceneBounds.Width / Projection.Scale, SafeSceneBounds.Height / Projection.Scale);
        }
    }

    internal LogicalRect Surface => new(0, SafeWorldBounds.Y, LocalRoomSceneView.WorldWidth,
        LocalRoomSceneView.WorldHeight - SafeWorldBounds.Y);
}
