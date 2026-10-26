using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly RadioButton left = new() { Content = "左侧", GroupName = "direction", Margin = new(0, 0, 18, 0) };
    private readonly RadioButton right = new() { Content = "右侧", GroupName = "direction", IsChecked = true };
    private readonly Button stop;
    internal ActionLabSession Session { get; private set; }
    internal int Direction => left.IsChecked == true ? -1 : 1;
    internal IReadOnlyDictionary<DangoActionKind, Button> ActionButtons => buttons;
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
        ColumnDefinitions.Add(new() { Width = new(312) });
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

        var controls = new StackPanel();
        var directions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 18) };
        directions.Children.Add(new TextBlock { Text = "动作方向", Margin = new(0, 0, 18, 0) });
        directions.Children.Add(left); directions.Children.Add(right);
        controls.Children.Add(directions);
        controls.Children.Add(Muted("身体"));
        controls.Children.Add(ActionGrid(Actions.Take(8)));
        controls.Children.Add(Muted("眼神与短移动"));
        controls.Children.Add(ActionGrid(Actions.Skip(8)));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 12, 0, 12) };
        stop = MakeButton("停止动作", "停止驱动力，让身体自然收稳", "M5,5 L15,5 15,15 5,15 Z");
        stop.Click += (_, _) => StopAction();
        tools.Children.Add(stop);
        var restart = MakeButton("重新开始", "只重建本实验的内存场景", "M5,8 A6,6 0 1 1 5,14 M5,3 L5,8 10,8");
        restart.Margin = new(8, 0, 0, 0);
        restart.Click += (_, _) => Restart(); tools.Children.Add(restart);
        controls.Children.Add(tools);
        var debug = new CheckBox { Content = "诊断", Margin = new(0, 0, 0, 8) };
        debug.Checked += (_, _) => { diagnostics.Visibility = Visibility.Visible; Render(); };
        debug.Unchecked += (_, _) => diagnostics.Visibility = Visibility.Collapsed;
        controls.Children.Add(debug); controls.Children.Add(diagnostics);
        SetColumn(controls, 1); SetRowSpan(controls, 3); Children.Add(controls);

        var footer = new StackPanel { Margin = new(0, 18, 0, 0) };
        footer.Children.Add(phase);
        footer.Children.Add(Muted("轻靠模拟所选侧抚摸；动作结束后可再次选择。"));
        SetRow(footer, 2); Children.Add(footer);
        Session = new(module);
        SystemParameters.StaticPropertyChanged += SystemSettingChanged;
        Render();
    }

    private UniformGrid ActionGrid(IEnumerable<(DangoActionKind Kind, string Name, string Hint)> actions)
    {
        var grid = new UniformGrid { Columns = 2, Margin = new(-4, 6, -4, 12) };
        foreach (var (kind, name, hint) in actions)
        {
            var button = MakeButton(name, hint, "M6,4 L15,10 6,16 Z");
            button.Margin = new(4); button.Padding = new(8);
            button.Click += (_, _) => { Session.Trigger(kind, Direction); Render(); };
            buttons.Add(kind, button); grid.Children.Add(button);
        }
        return grid;
    }

    private Button MakeButton(string label, string tooltip, string geometry)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new ShapePath { Data = Geometry.Parse(geometry), Width = 20, Height = 20, StrokeThickness = 1.2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "HomeInk");
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
        foreach (var (kind, button) in buttons)
        {
            button.IsEnabled = !active && !Session.Paused && Session.Error is null;
            button.Tag = Session.Controller.Current == kind ? "Selected" : null;
            button.SetResourceReference(Button.BackgroundProperty, Session.Controller.Current == kind ? "HomeSelected" : "HomeBackground");
        }
        left.IsEnabled = right.IsEnabled = !active;
        stop.IsEnabled = active && Session.Error is null && !Session.Paused;
        var selected = Session.LastAction is { } current ? Actions.First(a => a.Kind == current).Name + " · " : "";
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

    internal void TriggerForVerification(DangoActionKind kind, int direction)
    {
        left.IsChecked = direction < 0; right.IsChecked = direction > 0;
        buttons[kind].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    public void Dispose()
    {
        SystemParameters.StaticPropertyChanged -= SystemSettingChanged;
        Session.Dispose();
    }
}
