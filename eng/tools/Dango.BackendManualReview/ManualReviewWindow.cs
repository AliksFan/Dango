using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dango.BackendManualReview;

internal sealed class ManualReviewWindow : Window
{
    private readonly ManualReviewRuntime runtime;
    private readonly TextBox email = Field();
    private readonly TextBox invitation = Field();
    private readonly TextBox proof = Field();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) };
    private readonly TextBlock budget = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 10) };
    private readonly Button launch = new() { Content = "启动／重新打开 Dango", Margin = new(0, 4, 8, 4), Padding = new(10, 7, 10, 7), IsEnabled = false };
    private readonly Button confirm = new() { Content = "确认本轮测试邮箱", Margin = new(0, 4, 8, 4), Padding = new(10, 7, 10, 7), IsEnabled = false };
    private readonly Button finish = new() { Content = "结束验收并清理本轮资源", Margin = new(0, 4, 8, 4), Padding = new(10, 7, 10, 7), IsEnabled = false };
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool busy;
    private bool mayClose;
    private bool confirmed;

    public ManualReviewWindow(ManualReviewRuntime runtime)
    {
        this.runtime = runtime;
        Title = "Dango B5C 人工验收助手"; Width = 720; Height = 790; MinWidth = 440; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 250));
        var body = new StackPanel { Margin = new(22) };
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text = "真实 Windows 交互验收", FontSize = 24, FontWeight = FontWeights.SemiBold });
        body.Children.Add(status);
        AddField(body, "本轮邮箱", email); AddField(body, "邀请 ID", invitation); AddField(body, "邀请凭证（仅此窗口临时显示）", proof);
        var buttons = new WrapPanel(); buttons.Children.Add(launch); buttons.Children.Add(confirm); buttons.Children.Add(finish); body.Children.Add(buttons);
        body.Children.Add(budget);
        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 14, Text =
                "操作顺序：\n" +
                "1. 在 Dango 的账号居中浮层选择注册，使用上方邮箱、邀请 ID 和凭证。密码由你设置，至少12个字符。提交预注册后点本助手‘确认本轮测试邮箱’，再回 Dango 刷新状态、完成注册、首次登录。此步骤没有真实发邮件。\n\n" +
                "2. 首次登录不记住。保留账号团子的身份，明确采用本机配置；修改并保存本地外观后，回账号重新读取两份配置，再次采用本机。配置写入总共最多2次。\n\n" +
                "3. 托盘‘退出 Dango’后点本助手重开，第二次登录仍不记住。创建私人／公共共同房各1间，验收改名、解散、已解散目录与恢复；公共码可重置2次并验证旧码拒绝。\n\n" +
                "4. Enter总共4次：默认桌宠房首次、租约到期后再入、私人房、公共房。关闭浮层20秒内重开应恢复原租约；关闭超过30秒应要求明确再入。离房先确认，取消应不改变归属。\n\n" +
                "5. 注销第二个会话，第三次登录记住。托盘退出后经本助手重开，应恢复同一会话；随后注销。完成后托盘退出 Dango，再点‘结束验收’。窗口X可能只是隐藏，不能代替进程退出。\n\n" +
                "登录最多3次尝试，失败也消耗预算；失败请先停下反馈，避免重复尝试。注册挑战约30分钟有效。宿主和本助手需要保持打开，不能重启重置预算。\n\n" +
                "请检查窄窗口、错误文字、焦点和关闭行为。此阶段只验收账号、配置、房间管理与服务连接；多人移动／动作画面尚未接入。"
        });
        launch.Click += async (_, _) => await PerformAsync(async () =>
        {
            var pid = await runtime.LaunchDesktopAsync(); status.Text = $"Dango 已启动（本轮 PID {pid}）。请在客户端完成上述验收。";
        });
        confirm.Click += async (_, _) => await PerformAsync(async () =>
        {
            await runtime.ConfirmThisEmailAsync(); confirmed = true;
            status.Text = "本轮测试邮箱已通过正式受保护 HTTP 确认。请回 Dango 刷新注册状态并完成注册。";
        });
        finish.Click += async (_, _) => await FinishAsync();
        Closing += OnClosing;
        Loaded += async (_, _) => await PrepareAsync();
        refresh.Tick += (_, _) => Refresh();
    }

    private async Task PrepareAsync()
    {
        busy = true; status.Text = "正在按批准计划核验并准备独立验收环境…";
        try
        {
            await Task.Run(runtime.StartAsync);
            email.Text = runtime.Email; invitation.Text = runtime.InvitationId.ToString("D"); proof.Text = runtime.InvitationProof;
            await Task.Run(runtime.LaunchDesktopAsync);
            status.Text = "环境已就绪，Dango 已打开。请先在账号浮层提交预注册，再用本助手确认本轮邮箱。";
            refresh.Start();
        }
        catch (Exception error)
        {
            var code = Program.SafeError(error);
            IReadOnlyList<string> cleanup;
            try { cleanup = await Task.Run(runtime.TryFinishAsync); }
            catch { cleanup = ["manual_review_cleanup_unconfirmed"]; }
            status.Text = $"准备停止：{code}。" + (cleanup.Count == 0 ? "已完成本轮可证明归属资源的收尾。" : "收尾待处理：" + string.Join("、", cleanup));
            mayClose = cleanup.Count == 0;
            finish.Content = mayClose ? "关闭助手" : "重试收尾";
        }
        finally { busy = false; UpdateButtons(); Refresh(); }
    }

    private async Task PerformAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; UpdateButtons();
        try { await action(); }
        catch (Exception error) { status.Text = "操作停止：" + Program.SafeError(error) + "。请先反馈，不要重复提交账号或房间变更。"; }
        finally { busy = false; UpdateButtons(); Refresh(); }
    }

    private async Task FinishAsync()
    {
        if (busy) return;
        if (mayClose) { Close(); return; }
        busy = true; refresh.Stop(); UpdateButtons(); status.Text = "正在核验客户端退出并收尾本轮资源…";
        try
        {
            var problems = await Task.Run(runtime.TryFinishAsync);
            if (problems.Count == 0)
            {
                mayClose = true; email.Clear(); invitation.Clear(); proof.Clear();
                status.Text = "已核验本轮客户端退出、宿主与 PostgreSQL 停止，并清理专用凭据及本轮信任。数据与受保护文件已保留。请向 Codex 反馈人工验收结果。";
                if (runtime.EvidenceFailure is { } evidenceFailure)
                    status.Text += " 本轮数据库证据未确认，人工验收仍未通过：" + evidenceFailure + "。可关闭助手后向 Codex 反馈。";
                finish.Content = "关闭助手";
            }
            else
            {
                status.Text = "收尾尚未完成：" + string.Join("、", problems) + "。若客户端仍运行，请先使用 Dango 托盘‘退出’，然后重试结束验收。";
                if (runtime.Ready) refresh.Start();
            }
        }
        catch (Exception error) { status.Text = "收尾未确认：" + Program.SafeError(error) + "。请保留助手并反馈。"; }
        finally { busy = false; UpdateButtons(); }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (mayClose) return;
        e.Cancel = true;
        status.Text = "请先从托盘退出 Dango，然后点击‘结束验收’，核验本轮资源收尾后再关闭助手。";
    }
    private void UpdateButtons()
    {
        launch.IsEnabled = !busy && runtime.Ready;
        confirm.IsEnabled = !busy && runtime.Ready && !confirmed;
        finish.IsEnabled = !busy;
    }
    private void Refresh()
    {
        if (runtime.Budget is not { } current) return;
        var counts = current.Snapshot();
        budget.Text = $"已用预算：登录尝试 {counts[ManualReviewOperation.Login]}/3；共同房 {counts[ManualReviewOperation.RoomCreate]}/2；Enter {counts[ManualReviewOperation.Enter]}/4；公共码重置 {counts[ManualReviewOperation.CodeReset]}/2；配置 {counts[ManualReviewOperation.Configuration]}/2。";
        if (current.Identity is not null) { invitation.Clear(); proof.Clear(); }
        try { runtime.RefreshReceipt(); }
        catch { status.Text = "运行回执写入未确认，请停止提交新变更并反馈。"; }
    }
    private static TextBox Field() => new() { IsReadOnly = true, FontSize = 13, Padding = new(7), Margin = new(0, 3, 0, 10), TextWrapping = TextWrapping.Wrap };
    private static void AddField(Panel body, string label, TextBox field)
    { body.Children.Add(new TextBlock { Text = label }); body.Children.Add(field); }
}
