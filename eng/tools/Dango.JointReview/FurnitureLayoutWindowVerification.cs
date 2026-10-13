using System.Windows;
using System.Windows.Controls;
using Dango.Core.Furniture;

namespace Dango.JointReview;

/// <summary>Pure projection and WPF ownership gates; no OS event injection or native world is created.</summary>
internal static class FurnitureLayoutWindowVerification
{
    internal static int Run()
    {
        var checks = 0;
        void Check(bool valid, string description)
        { if (!valid) throw new InvalidOperationException(description); checks++; }
        static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 1e-9;
        var full = new FurnitureLayoutProjection(900, 600, 0, 0);
        var origin = full.ToScreen(0, 0);
        var unit = full.ToScreen(1, 1);
        Check(Near(unit.X - origin.X, 80) && Near(origin.Y - unit.Y, 60), "团子绘制比例不再为已接受的80/60。");
        Check(Near(full.VisualScale, 1), "满尺寸团子视觉缩放回归。");
        var rectangle = full.PlatformRect(LocalFurnitureSceneGeometry.DefaultAnchor);
        Check(Near(rectangle.Width, 272) && Near(rectangle.Height, 21), "平台默认尺寸绘制错误。");
        Check(Near(rectangle.Left, 314) && Near(rectangle.Top, 434.3), "家具锚点未按top-center转换。");
        foreach (var dimensions in new[] { (Width: 820d, Height: 580d), (Width: 620d, Height: 310d), (Width: 420d, Height: 200d) })
        {
            var projection = new FurnitureLayoutProjection(dimensions.Width, dimensions.Height, 83, -47);
            foreach (var world in new[] { new Point(-5, 0), new Point(0, 1.375), new Point(7, 5) })
            {
                var restored = projection.ToWorld(projection.ToScreen(world.X, world.Y));
                Check(Near(restored.X, world.X) && Near(restored.Y, world.Y), "缩放或相机后的场景逆变换不一致。");
            }
            Check(Near(projection.HorizontalScale / projection.VerticalScale, 4d / 3), "窗口缩小改变了团子的形状比例。");
        }
        foreach (var dimensions in new[] { (Width: 580d, Height: 400d), (Width: 894d, Height: 600d) })
        {
            var sized = new FurnitureLayoutProjection(dimensions.Width, dimensions.Height, 0, 0);
            var initial = sized with { PanX = FurnitureLayoutProjection.InitialCameraOffsetX * sized.HorizontalScale };
            var spawn = initial.ToScreen(LocalFurnitureSceneGeometry.SpawnX, LocalFurnitureSceneGeometry.SpawnY);
            var platform = initial.PlatformRect(LocalFurnitureSceneGeometry.DefaultAnchor);
            Check(spawn.X - 80 * initial.VisualScale >= 0 && spawn.X + 80 * initial.VisualScale <= dimensions.Width,
                "默认或最小窗口初始相机裁切本人团子。");
            Check(platform.Left >= 0 && platform.Right <= dimensions.Width, "最小窗口初始相机裁切默认平台。");
        }
        var panned = full with { PanX = 71, PanY = -39 };
        var anchor = new FurnitureAnchor(1, 2);
        var originalPoint = full.ToScreen(anchor.X, anchor.Y);
        var shiftedPoint = panned.ToScreen(anchor.X, anchor.Y);
        Check(Near(shiftedPoint.X - originalPoint.X, 71) && Near(shiftedPoint.Y - originalPoint.Y, -39), "右键相机不只改变视图。");
        var unshifted = panned.ToWorld(shiftedPoint);
        Check(Near(unshifted.X, anchor.X) && Near(unshifted.Y, anchor.Y), "相机平移改变了实际家具坐标。");
        var limited = FurnitureLayoutProjection.LimitGloveTarget(new(0, 0), new(3, 4), .05);
        Check(Near(limited.X, .24) && Near(limited.Y, .32), "快速手套目标没有被限至8p/s。");
        var near = FurnitureLayoutProjection.LimitGloveTarget(new(0, 0), new(.1, .1), .05);
        Check(near == new Point(.1, .1), "小幅手套移动被不必要地抬高力度。");
        var stalled = FurnitureLayoutProjection.LimitGloveTarget(new(0, 0), new(3, 4), 5);
        Check(Near(stalled.X, .48) && Near(stalled.Y, .64), "长调度间隔绕过手套移动限制。");
        Check(FurnitureLayoutProjection.LimitGloveTarget(new(0, 0), new(3, 4), 0) == new Point(0, 0), "零时间手套移动改变了目标。");
        var clamped = FurnitureLayoutProjection.ClampGloveTarget(new(100, -100));
        Check(Near(clamped.X, 7.2) && Near(clamped.Y, .08), "手套目标没有保持房间边界余量。");

        var owner = new Window(); var foreign = new Window();
        var panel = new StackPanel(); var scene = new Canvas { Focusable = true }; var button = new Button();
        panel.Children.Add(scene); panel.Children.Add(button); owner.Content = panel;
        var foreignScene = new Canvas(); foreign.Content = foreignScene;
        try
        {
            Check(FurnitureLayoutInputPolicy.HasSceneFocus(owner, scene, scene), "本窗口场景输入被拒绝。");
            Check(!FurnitureLayoutInputPolicy.HasSceneFocus(owner, scene, button), "按钮Space/Enter穿透为场景输入。");
            Check(!FurnitureLayoutInputPolicy.HasSceneFocus(owner, scene, null), "失焦场景仍收到持续输入。");
            Check(!FurnitureLayoutInputPolicy.HasSceneFocus(owner, scene, foreignScene), "其他窗口焦点穿透当前场景。");
            Check(!FurnitureLayoutInputPolicy.HasSceneFocus(foreign, scene, scene), "错误sourceWindow可以路由本场景输入。");
        }
        finally { owner.Close(); foreign.Close(); }

        var cleanupWindow = new FurnitureLayoutWindow("unused-physics-module.dll", "unused-furniture-data");
        try { cleanupWindow.VerifyInputCleanup(); checks += 3; }
        finally { cleanupWindow.Close(); }
        return checks;
    }
}
