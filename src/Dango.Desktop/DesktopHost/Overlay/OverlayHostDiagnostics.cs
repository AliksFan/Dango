using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace Dango.Desktop.DesktopHost.Overlay;

internal static class OverlayHostDiagnostics
{
    public static string CreateSummary(
        Window window,
        OverlayInteractionController interactionController,
        bool isInteractionEnabled)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(interactionController);

        var dpi = VisualTreeHelper.GetDpi(window);
        return string.Join(
            Environment.NewLine,
            "Dango P1-002 透明覆盖层诊断",
            $"运行时：.NET {Environment.Version}",
            $"系统：{RuntimeInformation.OSDescription}",
            $"进程架构：{RuntimeInformation.ProcessArchitecture}",
            $"窗口逻辑尺寸：{window.ActualWidth:F0} × {window.ActualHeight:F0}",
            $"DPI 比例：{dpi.DpiScaleX:F2} × {dpi.DpiScaleY:F2}",
            $"桌面显示：{window.IsVisible}",
            $"允许交互：{isInteractionEnabled}",
            $"拖动中：{interactionController.IsDragging}",
            $"最后取消原因：{interactionController.LastCancellationReason?.ToString() ?? "无"}");
    }
}
