using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Desktop.Resources;

namespace Dango.ActionLab;

/// <summary>Keep a reviewable error and retry path even if native startup fails.</summary>
internal sealed class ActionLabHost : ContentControl, IDisposable
{
    private readonly string module;
    internal ActionLabPanel? Panel { get; private set; }
    internal string? Error { get; private set; }
    internal Button? RetryButton { get; private set; }

    internal ActionLabHost(string module)
    {
        this.module = module;
        Resources.MergedDictionaries.Add(DesktopTheme.CreatePalette(SystemParameters.HighContrast));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Dango.Desktop;component/Resources/DesktopButtons.xaml", UriKind.Relative) });
        SetResourceReference(BackgroundProperty, "HomeBackground");
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        TryStart();
    }

    private void TryStart()
    {
        try
        {
            var next = new ActionLabPanel(module);
            var old = Panel;
            Content = next; Panel = next; old?.Dispose();
            Error = null; RetryButton = null;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.IO.IOException)
        {
            Error = error.Message;
            var stack = new StackPanel { Margin = new(32), MaxWidth = 560, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = "实验场景未能启动", FontSize = 20, Margin = new(0, 0, 0, 16) });
            stack.Children.Add(new TextBlock { Text = Error, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) });
            RetryButton = new Button { Content = "重新尝试", Style = (Style)FindResource("DesktopButton"), HorizontalAlignment = HorizontalAlignment.Left };
            RetryButton.Click += (_, _) => TryStart();
            stack.Children.Add(RetryButton);
            stack.SetResourceReference(TextBlock.ForegroundProperty, "HomeInk");
            stack.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI, Microsoft YaHei"));
            Content = stack;
        }
    }

    public void Dispose() { Panel?.Dispose(); Panel = null; }
}
