using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Physics.Presentation;

namespace Dango.SemanticPresentationReview;

internal sealed class ReviewWindow : Window
{
    private readonly ReviewInputs inputs;
    private readonly string output;
    private readonly bool automatic;
    private readonly DockPanel root = new();
    private readonly TextBlock status = new() { Margin = new Thickness(16, 8, 16, 12), TextWrapping = TextWrapping.Wrap };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<object> completed = [];
    private readonly List<string> failures = [];
    private readonly string[] scenarios = ["push", "stack", "withdraw"];
    private ReviewSurface? surface;
    private int scenarioIndex = 1;
    private int renderCallbacks, activeCallbacks, suspendedCallbacks;
    private double previousSeconds, accumulator, visibleSeconds, focusedSeconds, droppedReplaySeconds;
    private bool paused, closing;
    private string? failure;
    internal int ExitCode { get; private set; }

    internal ReviewWindow(ReviewInputs inputs, string output, bool automatic)
    {
        this.inputs = inputs; this.output = output; this.automatic = automatic;
        Title = "Dango · 双人语义呈现验证（隔离）";
        Width = 1200; Height = 760; MinWidth = 960; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = root; ChangeScenario(automatic ? 0 : 1);
        foreach (var key in surface!.Resources.Keys) Resources[key] = surface.Resources[key];
        root.SetResourceReference(Panel.BackgroundProperty, "HomeBackground");
        root.SetResourceReference(TextBlock.ForegroundProperty, "HomeInk");
        TextBlock.SetFontFamily(root, new FontFamily("Segoe UI, Microsoft YaHei")); TextBlock.SetFontSize(root, 14);
        var tools = new WrapPanel { Margin = new Thickness(16, 12, 16, 0) };
        for (var index = 0; index < scenarios.Length; index++)
        {
            var selected = index;
            tools.Children.Add(ActionButton(new[] { "推挤", "堆叠", "撤支撑" }[index], () => ChangeScenario(selected)));
        }
        tools.Children.Add(ActionButton("暂停／继续", () => { paused = !paused; previousSeconds = clock.Elapsed.TotalSeconds; }));
        tools.Children.Add(ActionButton("重放", () => ChangeScenario(scenarioIndex)));
        DockPanel.SetDock(tools, Dock.Top); root.Children.Insert(0, tools);
        var scope = new TextBlock { Text = "两个独立呈现世界 · 同一份低频物理语义 · 未联网", Margin = new Thickness(16, 8, 16, 8) };
        scope.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted"); DockPanel.SetDock(scope, Dock.Top); root.Children.Insert(1, scope);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Insert(2, status);
        Loaded += (_, _) => { previousSeconds = clock.Elapsed.TotalSeconds; CompositionTarget.Rendering += OnRendering; };
        Closed += (_, _) =>
        {
            closing = true; CompositionTarget.Rendering -= OnRendering; surface?.Dispose(); surface = null;
            WriteLive(); Application.Current.Shutdown(ExitCode);
        };
    }

    private Button ActionButton(string label, Action action)
    {
        var glyph = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(label.StartsWith("暂停", StringComparison.Ordinal) ? "M 2,1 L 2,13 M 10,1 L 10,13" :
                label == "重放" ? "M 2,5 A 6,6 0 1 1 2,11 M 2,1 L 2,5 L 6,5" : "M 2,1 L 10,7 L 2,13"),
            StrokeThickness = 1.2, Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0),
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "HomeAccent");
        var content = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(glyph); content.Children.Add(new TextBlock { Text = label });
        var button = new Button { Content = content, MinHeight = 40, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0),
            ToolTip = label, IsEnabled = !automatic };
        button.SetResourceReference(Control.BackgroundProperty, "HomeSoft"); button.SetResourceReference(Control.BorderBrushProperty, "HomeLine");
        button.SetResourceReference(Control.ForegroundProperty, "HomeInk"); AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action(); return button;
    }

    private void ChangeScenario(int index)
    {
        if (surface is not null) { root.Children.Remove(surface); surface.Dispose(); }
        scenarioIndex = index; surface = new(inputs, scenarios[index]); root.Children.Add(surface);
        paused = false; failure = null; accumulator = 0; previousSeconds = clock.Elapsed.TotalSeconds;
        status.Text = "运行中";
    }

    private void OnRendering(object? sender, EventArgs args)
    {
        if (closing || surface is null) return;
        renderCallbacks++;
        var now = clock.Elapsed.TotalSeconds; var delta = Math.Max(0, now - previousSeconds); previousSeconds = now;
        if (automatic && now > 120) { Stop("window-timeout"); return; }
        if (!IsVisible || WindowState == WindowState.Minimized) { suspendedCallbacks++; return; }
        if (paused || failure is not null) return;
        activeCallbacks++; visibleSeconds += delta; if (IsActive) focusedSeconds += delta;
        try
        {
            accumulator += delta;
            // A preview may slow its replay under UI load; it never advances hidden time or claims server timing/FPS.
            var due = Math.Min(4, (int)(accumulator / SemanticPresentationSampler.FixedStep.TotalSeconds));
            accumulator -= due * SemanticPresentationSampler.FixedStep.TotalSeconds;
            if (accumulator > .25) { droppedReplaySeconds += accumulator; accumulator = 0; }
            var target = Math.Min(600, surface.Left.Steps + due);
            surface.Left.AdvanceTo(target); surface.Right.AdvanceTo(target); surface.Redraw();
            status.Text = $"{new[] { "推挤", "堆叠", "撤支撑" }[scenarioIndex]} · {target}/600";
            if (target < 600) return;
            completed.Add(new { scenario = scenarios[scenarioIndex], surface.CommonTimeChecks, surface.MaximumCommonCenterError,
                left = surface.Left.Evidence(), right = surface.Right.Evidence(), renderCallbacks, activeCallbacks, visibleSeconds, focusedSeconds });
            WriteLive();
            if (automatic)
            {
                if (scenarioIndex == 2) { ExitCode = 0; Close(); }
                else ChangeScenario(scenarioIndex + 1);
            }
            else { paused = true; status.Text += " · 完成，可重放或切换场景"; }
        }
        catch (Exception error) { Stop((error as GateFailure)?.Oracle ?? error.ToString()); }
    }

    private void Stop(string error)
    {
        failure = error; failures.Add(error); ExitCode = 1; paused = true; status.Text = "验证停止：" + error; WriteLive();
        if (automatic) Close();
    }

    private void WriteLive() => Program.Write(Path.Combine(output, "live.json"), new
    {
        Version = 1, Pid = Environment.ProcessId, Automatic = automatic, Pass = failures.Count == 0 && (!automatic || completed.Count == 3),
        failure, failures, closing, renderCallbacks, activeCallbacks, suspendedCallbacks, visibleSeconds, focusedSeconds, droppedReplaySeconds, completed,
        requiredActiveSeconds = 600, continuousObservationPassed = false, manualAcceptance = "pending",
        scope = "Real visible isolated WPF replay. Callback counts are not compositor FPS or network/multiplayer evidence.",
    });
}
