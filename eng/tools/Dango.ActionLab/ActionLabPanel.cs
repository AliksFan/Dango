using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Dango.Desktop.AppShell.ControlCenter;
using System.Windows.Documents;
using System.Windows.Media;
using Dango.Desktop.Rendering.Actions;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Resources;
using ShapePath = System.Windows.Shapes.Path;

namespace Dango.ActionLab;

internal sealed class ActionLabPanel : Grid, IDisposable
{
    internal static readonly (DangoActionKind Kind, string Name, string Hint)[] Actions =
    [
        (DangoActionKind.Shake, "抖身", "短促、小幅颤动，逐渐收住"),
        (DangoActionKind.Sway, "摇摆", "较慢地左右晃动，再收稳"),
        (DangoActionKind.Stretch, "舒展", "略微收拢，向上伸展，再放松"),
        (DangoActionKind.Lower, "伏低", "慢慢压低，保持片刻，再放松"),
        (DangoActionKind.Peek, "歪身探看", "向所选侧轻倾，视线停留后回正"),
        (DangoActionKind.Lean, "轻靠", "模拟所选侧抚摸：向接触侧轻靠，再慢慢收回"),
        (DangoActionKind.Recoil, "短后缩", "轻微收拢并短退，停住观察"),
        (DangoActionKind.ChargedHop, "蓄力轻跳", "压低准备、真实起跳、落地收稳"),
        (DangoActionKind.SlowBlink, "慢眨", "慢慢合眼，稍停，再睁开"),
        (DangoActionKind.Squint, "眯眼", "轻轻眯眼，停留后放松"),
        (DangoActionKind.LookAway, "注视后移开", "先注视所选侧，再把视线移开"),
        (DangoActionKind.LookBack, "回头看", "先看向所选侧，再回看另一侧"),
        (DangoActionKind.Approach, "挪近后停", "沿所选方向挪近一点，然后停住"),
        (DangoActionKind.Retreat, "短退后观察", "向所选方向的反侧短退，停住观察"),
    ];

    private readonly string module;
    private readonly Canvas world = new() { Width = 520, Height = 340, ClipToBounds = true };
    private readonly DeformableDangoVisual body = new() { ShowHat = false, IsHitTestVisible = false };
    private readonly DangoHatVisual hat = new() { IsHitTestVisible = false };
    private readonly TextBlock phase = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 44 };
    private readonly TextBlock diagnostics = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed };
    private readonly Dictionary<DangoActionKind, Button> buttons = [];
    private readonly Dictionary<(DangoActionKind Kind, string CandidateId), Button> candidateButtons = [];
    internal ScrollViewer CandidateScroll { get; private set; } = null!;
    internal Button RestartButton { get; private set; } = null!;
    internal Button StopButton => stop;
    internal FrameworkElement PreviewWorld => world;
    private readonly RadioButton left = new() { Content = "左侧", GroupName = "direction", Margin = new(0, 0, 18, 0) };
    private readonly RadioButton right = new() { Content = "右侧", GroupName = "direction", IsChecked = true };
    private readonly Button stop;
    internal ActionLabSession Session { get; private set; }
    internal int Direction => left.IsChecked == true ? -1 : 1;
    internal IReadOnlyDictionary<DangoActionKind, Button> ActionButtons => buttons;
    internal IReadOnlyDictionary<(DangoActionKind Kind, string CandidateId), Button> CandidateButtons => candidateButtons;
    internal IEnumerable<Button> AllButtons => Descendants(this).OfType<Button>();

    internal ActionLabPanel(string module)
    {
        this.module = module;
        Resources.MergedDictionaries.Add(DesktopTheme.CreatePalette(SystemParameters.HighContrast));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Dango.Desktop;component/Resources/DesktopButtons.xaml", UriKind.Relative) });
        SetResourceReference(BackgroundProperty, "HomeBackground");
        TextElement.SetFontFamily(this, new FontFamily("Segoe UI, Microsoft YaHei"));
        TextElement.SetFontSize(this, 14);
        SetResourceReference(TextElement.ForegroundProperty, "HomeInk");
        Margin = new(24);
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new() { Width = new(404) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });

        var title = new StackPanel { Margin = new(0, 0, 0, 18) };
        title.Children.Add(new TextBlock { Text = "动作实验", FontSize = 20, Margin = new(0, 0, 0, 6) });
        title.Children.Add(Muted("单只团子 · 按钮触发 · 待审美审核"));
        Children.Add(title);

        var scene = new Border { Margin = new(0, 0, 24, 0), BorderThickness = new(1), CornerRadius = new(7), Padding = new(12) };
        scene.SetResourceReference(Border.BorderBrushProperty, "HomeLine");
        var view = new Viewbox { Child = world, Stretch = Stretch.Uniform };
        scene.Child = view;
        var floor = new System.Windows.Shapes.Line { X1 = ActionLabSession.Left, X2 = ActionLabSession.Right, Y1 = ActionLabSession.Floor, Y2 = ActionLabSession.Floor, StrokeThickness = .7 };
        floor.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "HomeLine");
        world.Children.Add(floor); world.Children.Add(body); world.Children.Add(hat);
        SetRow(scene, 1); Children.Add(scene);

        var controls = new Grid();
        controls.RowDefinitions.Add(new() { Height = GridLength.Auto });
        controls.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        controls.RowDefinitions.Add(new() { Height = GridLength.Auto });
        controls.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var directions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 18) };
        directions.Children.Add(new TextBlock { Text = "动作方向", Margin = new(0, 0, 18, 0) });
        directions.Children.Add(left); directions.Children.Add(right);
        controls.Children.Add(directions);
        var actionRows = new StackPanel();
        actionRows.Children.Add(Muted("身体"));
        actionRows.Children.Add(ActionRows(Actions.Take(8)));
        actionRows.Children.Add(Muted("眼神与短移动"));
        actionRows.Children.Add(ActionRows(Actions.Skip(8)));
        CandidateScroll = new SettingsScrollViewer { Content = actionRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        CandidateScroll.Resources.Add(typeof(ScrollBar), CreateThinScrollBarStyle());
        SetRow(CandidateScroll, 1); controls.Children.Add(CandidateScroll);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 12, 0, 12) };
        stop = MakeButton("停止动作", "停止驱动力，让身体自然收稳", "M5,5 L15,5 15,15 5,15 Z");
        stop.Click += (_, _) => StopAction();
        tools.Children.Add(stop);
        var restart = MakeButton("重新开始", "只重建本实验的内存场景", "M5,8 A6,6 0 1 1 5,14 M5,3 L5,8 10,8");
        RestartButton = restart;
        restart.Margin = new(8, 0, 0, 0);
        restart.Click += (_, _) => Restart(); tools.Children.Add(restart);
        SetRow(tools, 2); controls.Children.Add(tools);
        var debug = new CheckBox { Content = "诊断", Margin = new(0, 0, 0, 8) };
        debug.Checked += (_, _) => { diagnostics.Visibility = Visibility.Visible; Render(); };
        debug.Unchecked += (_, _) => diagnostics.Visibility = Visibility.Collapsed;
        var diagnosticArea = new StackPanel();
        diagnosticArea.Children.Add(debug); diagnosticArea.Children.Add(diagnostics);
        SetRow(diagnosticArea, 3); controls.Children.Add(diagnosticArea);
        SetColumn(controls, 1); SetRowSpan(controls, 3); Children.Add(controls);

        var footer = new StackPanel { Margin = new(0, 18, 0, 0) };
        footer.Children.Add(phase);
        footer.Children.Add(Muted("轻靠模拟所选侧抚摸；动作结束后可再次选择。"));
        SetRow(footer, 2); Children.Add(footer);
        Session = new(module);
        SystemParameters.StaticPropertyChanged += SystemSettingChanged;
        Render();
    }

    private StackPanel ActionRows(IEnumerable<(DangoActionKind Kind, string Name, string Hint)> actions)
    {
        var rows = new StackPanel { Margin = new(0, 6, 0, 12) };
        foreach (var (kind, name, hint) in actions)
        {
            var row = new Grid { Margin = new(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new() { Width = new(100) });
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, ToolTip = hint });
            var choices = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var candidate in ActionLabCandidates.For(kind))
            {
                var button = MakeButton(candidate.Label, hint + " · " + candidate.Hint, "M6,4 L15,10 6,16 Z");
                button.Margin = new(0, 0, 8, 0); button.Padding = new(8);
                AutomationProperties.SetName(button, name + " · " + candidate.Label);
                DesktopTheme.SetPreserveSelectedHover(button, true);
                button.Click += (_, _) => { if (button.IsEnabled) { Session.Trigger(kind, Direction, candidate.Id); Render(); } };
                candidateButtons.Add((kind, candidate.Id), button);
                if (candidate.Id == "current") buttons.Add(kind, button);
                choices.Children.Add(button);
            }
            SetColumn(choices, 1); row.Children.Add(choices); rows.Children.Add(row);
        }
        return rows;
    }

    // Reuse the existing furniture editor's thin-scrollbar template; all colors
    // resolve through the shared desktop palette. No production view is created.
    private static Style CreateThinScrollBarStyle() => (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
          <Setter Property="Width" Value="16"/><Setter Property="MinWidth" Value="16"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
            <Grid Background="Transparent"><Border Width="6" Background="{DynamicResource HomeSoft}" CornerRadius="3"/>
              <Track x:Name="PART_Track" Orientation="Vertical" IsDirectionReversed="True">
                <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
                <Track.Thumb><Thumb MinHeight="28"><Thumb.Template><ControlTemplate TargetType="Thumb">
                  <Grid Background="Transparent"><Border x:Name="Grip" Width="6" CornerRadius="3" Background="{DynamicResource HomeMuted}"/></Grid>
                  <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Grip" Property="Background" Value="{DynamicResource HomeAccent}"/></Trigger>
                    <Trigger Property="IsDragging" Value="True"><Setter TargetName="Grip" Property="Background" Value="{DynamicResource HomeAccent}"/></Trigger>
                  </ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              </Track>
            </Grid>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);

    private Button MakeButton(string label, string tooltip, string geometry)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new ShapePath { Data = Geometry.Parse(geometry), Width = 20, Height = 20, StrokeThickness = 1.2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground")
        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = label, Margin = new(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, ToolTip = tooltip, Style = (Style)FindResource("DesktopButton") };
        ToolTipService.SetInitialShowDelay(button, 250);
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetHelpText(button, tooltip);
        return button;
    }

    private TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
        return block;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private void SystemSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Dispatcher.Invoke(() => DesktopTheme.ApplyPalette(Resources, SystemParameters.HighContrast));
    }

    internal void Step() { Session.Step(); Render(); }
    private void StopAction() { Session.Cancel(); Render(); }
    internal void Render()
    {
        if (Session.Runtime.LastFrame is { } frame)
            DangoHostFrameBinding.Apply(body, hat, frame, 0, appearance: Session.Runtime.DisplayHatAppearance,
                hatSize: Session.Runtime.DisplayHatSize, runtime: Session.Runtime);
        var active = Session.Controller.Active;
        foreach (var ((kind, candidateId), button) in candidateButtons)
        {
            button.IsEnabled = !active && !Session.Paused && Session.Error is null;
            var selectedCandidate = active && Session.Controller.Current == kind && Session.LastCandidateId == candidateId;
            button.Tag = selectedCandidate ? "Selected" : null;
            button.SetResourceReference(Button.BackgroundProperty, selectedCandidate ? "HomeSelected" : "HomeBackground");
            // Shared disabled chrome keeps the selected border visible during playback.
            button.SetResourceReference(Button.BorderBrushProperty, selectedCandidate ? "HomeAccent" : "HomeLine");
        }
        left.IsEnabled = right.IsEnabled = !active && !Session.Paused && Session.Error is null;
        stop.IsEnabled = active && Session.Error is null && !Session.Paused;
        var selected = Session.LastAction is { } current ? Actions.First(a => a.Kind == current).Name + " · " + ActionLabCandidates.For(current).First(c => c.Id == Session.LastCandidateId).Label + " · " : "";
        phase.Text = selected + PhaseLabel(Session.Phase);
        if (Session.Runtime.LastBeltRoomFrame is { } native)
            diagnostics.Text = $"步进 {native.Tick} · 接地 {(native.Stats[14] != 0 ? "是" : "否")}\n离地 {native.Stats[12]:F4} m · 故障 {native.Fault}\n动作 {Session.Controller.Elapsed:F2} s";
    }

    private static string PhaseLabel(string phase) => phase switch
    {
        "enter" => "轻轻进入", "exit" or "release" => "缓慢收回", "hold" => "停留",
        "press" or "compress" or "charge" => "压低准备", "extend" => "向上舒展",
        "recoil" => "轻轻后缩", "observe" or "observe-stable" => "停住观察",
        "blink" => "慢眨", "look-first" => "注视", "look-across" => "移开视线", "return" => "视线回正",
        "move" => "轻轻挪动", "brake" => "准备停住", "launch" or "await-liftoff" => "起跳",
        "airborne" => "腾空", "settle" or "settle-fast" => "慢慢收稳", _ => phase,
    };

    internal void Restart()
    {
        // Construct first; preserve the old scene and its error if allocation fails.
        try
        {
            var next = new ActionLabSession(module);
            Session.Dispose(); Session = next;
            Render();
        }
        catch (InvalidOperationException error) { phase.Text = "重新开始失败：" + error.Message; }
    }

    internal void TriggerForVerification(DangoActionKind kind, int direction, string candidateId = "current")
    {
        var button = candidateButtons[(kind, candidateId)];
        if (!button.IsEnabled || !left.IsEnabled || !right.IsEnabled)
            throw new InvalidOperationException("Verification cannot click a disabled action or change a disabled direction.");
        left.IsChecked = direction < 0; right.IsChecked = direction > 0;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    public void Dispose()
    {
        SystemParameters.StaticPropertyChanged -= SystemSettingChanged;
        Session.Dispose();
    }
}
