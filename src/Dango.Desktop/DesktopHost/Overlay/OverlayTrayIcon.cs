using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Dango.Desktop.DesktopHost.Overlay;

internal sealed class OverlayTrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ToolStripMenuItem visibilityItem;
    private readonly Window window;
    private readonly OverlayInteractionController interactionController;

    public OverlayTrayIcon(Window window, OverlayInteractionController interactionController, Action exit)
    {
        this.window = window;
        this.interactionController = interactionController;

        visibilityItem = new Forms.ToolStripMenuItem("隐藏覆盖层");
        visibilityItem.Click += (_, _) => ToggleVisibility();

        var diagnosticsItem = new Forms.ToolStripMenuItem("诊断摘要");
        diagnosticsItem.Click += (_, _) => ShowDiagnostics();

        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => exit();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(visibilityItem);
        menu.Items.Add(diagnosticsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = SystemIcons.Application,
            Text = "Dango P1-002 覆盖层",
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => ToggleVisibility();
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip?.Dispose();
        notifyIcon.Dispose();
    }

    private void ToggleVisibility()
    {
        if (window.IsVisible)
        {
            window.Hide();
            visibilityItem.Text = "显示覆盖层";
            return;
        }

        window.Show();
        visibilityItem.Text = "隐藏覆盖层";
    }

    private void ShowDiagnostics()
    {
        var summary = OverlayHostDiagnostics.CreateSummary(window, interactionController);
        System.Windows.MessageBox.Show(
            summary,
            "Dango 本地诊断",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }
}
