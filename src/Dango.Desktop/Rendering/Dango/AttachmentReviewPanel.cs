using System.Windows;
using System.Windows.Threading;
using Dango.Core.Animation;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Dango.Desktop.Rendering.Visuals;

internal sealed class AttachmentReviewPanel : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    public bool AllowClose { get; set; }

    public AttachmentReviewPanel(StackedDangoSceneRuntime runtime)
    {
        Title = "Dango 眼帽验收工具"; Width = 330; Height = 330;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(16) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "在控制中心切换窗口 / 桌面模式和团子数量。\n点击或拖动团子后，再为它切换表情。", TextWrapping = TextWrapping.Wrap });
        var hats = new ComboBox { ItemsSource = new[] { "叶芽", "小礼帽", "圆顶帽" }, SelectedIndex = (int)runtime.ReviewHatAppearance, Margin = new Thickness(0, 12, 0, 8) };
        hats.SelectionChanged += (_, _) => runtime.ReviewHatAppearance = (HatAppearance)hats.SelectedIndex;
        panel.Children.Add(hats);
        var eyes = new ComboBox { ItemsSource = new[] { "圆眼", "柔和", "自然闭眼", "紧闭", "开心", "困倦", "无语", "认真 / 生气", "惊讶", "疑惑" }, SelectedIndex = 0 };
        panel.Children.Add(eyes);
        var status = new TextBlock { Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
        void Submit(AttachmentIntent intent)
        {
            try
            {
                var result = runtime.SubmitAttachment(intent);
                status.Text = result.Accepted ? "动作已接纳" : $"动作未接纳：{result.Reason}";
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException) { status.Text = error.Message; }
        }
        void AddButton(string label, Action action)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 7, 0, 0), Padding = new Thickness(8, 4, 8, 4) };
            button.Click += (_, _) => action(); panel.Children.Add(button);
        }
        AddButton("应用表情", () => Submit(new SetEyes(EyeSelection.Both, 1, Enum.GetValues<EyeShape>()[eyes.SelectedIndex], 1)));
        AddButton("眨一下", () => Submit(Blink.Default(EyeSelection.Both)));
        AddButton("恢复自然表情", () => Submit(new Release(AttachmentChannel.All)));
        panel.Children.Add(status);
        timer.Tick += (_, _) => { if (runtime.AttachmentError is { } error) status.Text = error; };
        IsVisibleChanged += (_, _) => { if (IsVisible) timer.Start(); else timer.Stop(); };
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => timer.Stop();
    }
}
