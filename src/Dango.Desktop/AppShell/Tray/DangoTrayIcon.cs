using System.Drawing;
using Forms = System.Windows.Forms;

namespace Dango.Desktop.AppShell.Tray;

internal sealed class DangoTrayIcon : IDisposable
{
    private readonly ApplicationCommandController commandController;
    private readonly Action showControlCenter;
    private readonly Action showDiagnostics;
    private readonly Action requestExit;
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ToolStripMenuItem visibilityItem;

    public DangoTrayIcon(
        ApplicationCommandController commandController,
        Action showControlCenter,
        Action showDiagnostics,
        Action requestExit)
    {
        this.commandController = commandController ?? throw new ArgumentNullException(nameof(commandController));
        this.showControlCenter = showControlCenter ?? throw new ArgumentNullException(nameof(showControlCenter));
        this.showDiagnostics = showDiagnostics ?? throw new ArgumentNullException(nameof(showDiagnostics));
        this.requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));

        var openItem = new Forms.ToolStripMenuItem("打开 Dango 控制中心");
        openItem.Click += (_, _) => this.showControlCenter();

        visibilityItem = new Forms.ToolStripMenuItem();
        visibilityItem.Click += (_, _) => ToggleVisibility();

        var diagnosticsItem = new Forms.ToolStripMenuItem("打开诊断");
        diagnosticsItem.Click += (_, _) => this.showDiagnostics();

        var exitItem = new Forms.ToolStripMenuItem("退出 Dango");
        exitItem.Click += (_, _) => this.requestExit();

        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => RefreshVisibilityText();
        menu.Items.Add(openItem);
        menu.Items.Add(visibilityItem);
        menu.Items.Add(diagnosticsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = SystemIcons.Application,
            Text = "Dango 控制中心",
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => this.showControlCenter();
        RefreshVisibilityText();
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip?.Dispose();
        notifyIcon.Dispose();
    }

    private void ToggleVisibility()
    {
        var snapshot = commandController.GetSnapshot();
        var result = commandController.SetVisibility(!snapshot.IsVisible);
        if (result.State == CommandApplicationState.Failed)
        {
            notifyIcon.ShowBalloonTip(
                timeout: 5000,
                tipTitle: "Dango 未能切换显示状态",
                tipText: result.Error ?? "桌面宿主未确认操作。",
                tipIcon: Forms.ToolTipIcon.Warning);
        }

        RefreshVisibilityText();
    }

    private void RefreshVisibilityText()
    {
        var isVisible = commandController.GetSnapshot().IsVisible;
        visibilityItem.Text = isVisible ? "隐藏桌面团子" : "显示桌面团子";
    }
}
