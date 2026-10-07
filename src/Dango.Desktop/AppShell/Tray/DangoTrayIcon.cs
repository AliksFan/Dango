using System.Drawing;
using Forms = System.Windows.Forms;
using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.AppShell.Tray;

internal sealed record LocalRoomTrayActions(Func<bool> IsSelected, Func<bool> IsVisible,
    Action Restore, Action Hide, Action Return, Action<LocalRoomPresentation> Present, Action<RoomViewPreset> View, Action Recover,
    Func<bool>? IsDesktopPet = null);

internal sealed class DangoTrayIcon : IDisposable
{
    private readonly ApplicationCommandController commandController;
    private readonly Action showControlCenter;
    private readonly Action showDiagnostics;
    private readonly Action requestExit;
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ToolStripMenuItem visibilityItem;
    private readonly LocalRoomTrayActions? localRoom;

    public DangoTrayIcon(
        ApplicationCommandController commandController,
        Action showControlCenter,
        Action showDiagnostics,
        Action requestExit, LocalRoomTrayActions? localRoom = null)
    {
        this.commandController = commandController ?? throw new ArgumentNullException(nameof(commandController));
        this.showControlCenter = showControlCenter ?? throw new ArgumentNullException(nameof(showControlCenter));
        this.showDiagnostics = showDiagnostics ?? throw new ArgumentNullException(nameof(showDiagnostics));
        this.requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));
        this.localRoom = localRoom;

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
        if (localRoom is not null)
        {
            var roomMenu = new Forms.ToolStripMenuItem("普通房 · 本地单人");
            void Add(Forms.ToolStripMenuItem parent, string text, Action action)
            { var item = new Forms.ToolStripMenuItem(text); item.Click += (_, _) => action(); parent.DropDownItems.Add(item); }
            Add(roomMenu, "恢复房间", localRoom.Restore);
            Add(roomMenu, "隐藏房间", localRoom.Hide);
            Add(roomMenu, "恢复团子并重试", localRoom.Recover);
            Add(roomMenu, "房门 · 返回门廊", localRoom.Return);
            var presentation = new Forms.ToolStripMenuItem("显示方式");
            Add(presentation, "窗口", () => localRoom.Present(LocalRoomPresentation.Window));
            Add(presentation, "壁纸", () => localRoom.Present(LocalRoomPresentation.Wallpaper));
            roomMenu.DropDownItems.Add(presentation);
            var view = new Forms.ToolStripMenuItem("视角");
            Add(view, "标准", () => localRoom.View(RoomViewPreset.Standard));
            Add(view, "近景", () => localRoom.View(RoomViewPreset.Near));
            Add(view, "全景", () => localRoom.View(RoomViewPreset.Whole));
            roomMenu.DropDownItems.Add(view);
            menu.Opening += (_, _) =>
            {
                var desktopPet = localRoom.IsDesktopPet?.Invoke() == true && localRoom.IsSelected();
                roomMenu.Text = desktopPet ? "桌宠房" : "普通房 · 本地单人";
                presentation.Visible = view.Visible = !desktopPet;
            };
            menu.Items.Add(roomMenu);
        }
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
        notifyIcon.DoubleClick += (_, _) => { if (this.localRoom?.IsSelected() == true) this.localRoom.Restore(); else this.showControlCenter(); };
        RefreshVisibilityText();
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip?.Dispose();
        notifyIcon.Dispose();
    }

    public void ShowSettingsWarning(string message) => notifyIcon.ShowBalloonTip(
        5000, "Dango 设置未保存", message, Forms.ToolTipIcon.Warning);
    public void ShowRoomWarning(string message) => notifyIcon.ShowBalloonTip(5000, "Dango 房间", message, Forms.ToolTipIcon.Warning);

    private void ToggleVisibility()
    {
        if (localRoom?.IsSelected() == true)
        { if (localRoom.IsVisible()) localRoom.Hide(); else localRoom.Restore(); RefreshVisibilityText(); return; }
        var snapshot = commandController.GetSnapshot();
        if (!snapshot.IsVisible && !commandController.CanShowDesktop)
        {
            showControlCenter();
            return;
        }
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
        if (localRoom?.IsSelected() == true)
        {
            var name = localRoom.IsDesktopPet?.Invoke() == true ? "桌宠房" : "普通房";
            visibilityItem.Text = (localRoom.IsVisible() ? "隐藏" : "恢复") + name;
            visibilityItem.Enabled = true;
            return;
        }
        var isVisible = commandController.GetSnapshot().IsVisible;
        visibilityItem.Text = !commandController.CanShowDesktop ? "进入房间后显示桌面团子"
            : isVisible ? "隐藏桌面团子" : "显示桌面团子";
        visibilityItem.Enabled = commandController.CanShowDesktop;
    }
}
