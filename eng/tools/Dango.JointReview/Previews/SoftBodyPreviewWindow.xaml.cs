using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Microsoft.Win32;

namespace Dango.JointReview.Previews;

public partial class SoftBodyPreviewWindow : Window
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer frameTimer;
    private readonly DispatcherTimer rebuildTimer;
    private readonly Dictionary<string, (Slider Slider, System.Windows.Controls.TextBox Text)> editors = [];
    private readonly Dictionary<string, System.Windows.Controls.ComboBox> choiceEditors = [];
    private PhysicsPresentationController? controller;
    private SoftBodyTuningParameters parameters = new();
    private long resetAt;
    private bool buildingControls;
    internal bool ResourcesReleased => !frameTimer.IsEnabled && !rebuildTimer.IsEnabled && controller is null;

    internal void VerifyNativeFrame() => OnTick(null, EventArgs.Empty);

    public SoftBodyPreviewWindow()
    {
        IsolatedPreviews.RequireLegacyNative();
        InitializeComponent();
        BuildParameterEditors();
        rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
        rebuildTimer.Tick += (_, _) => { rebuildTimer.Stop(); RebuildWorld(); };
        frameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(parameters.FixedStepSeconds) };
        frameTimer.Tick += OnTick;
        RebuildWorld();
        Closed += OnClosed;
        try { VerifyNativeFrame(); frameTimer.Start(); }
        catch { OnClosed(null, EventArgs.Empty); throw; }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (PauseToggle.IsChecked == true || controller is null) return;
        var timeScale = SlowMotionToggle.IsChecked == true ? 0.25 : 1;
        var step = TimeSpan.FromSeconds(parameters.FixedStepSeconds * timeScale);
        var input = new PhysicsPresentationInput(
            "tuning-dango", new LogicalRect(200, 240, 160, 120), DangoActionState.Idle,
            resetAt, 17, "ground-contact", new LogicalVector(0, -1), 0, 1, false, true, []);
        var frame = controller.Tick(input, step);
        UpdateMetrics(frame);
    }

    private void RebuildWorld()
    {
        rebuildTimer?.Stop();
        controller?.Dispose();
        controller = new PhysicsPresentationController(
            Box2DPhysicsPresentationAdapter.CreateTuning(Box2DNativeLoader.DefaultLibraryPath, parameters), PreviewDango);
        frameTimer.Interval = TimeSpan.FromSeconds(parameters.FixedStepSeconds);
        resetAt = clock.ElapsedMilliseconds;
        StateText.Text = "AirCircle";
    }

    private void UpdateMetrics(PhysicsPresentationFrame frame)
    {
        if (frame.FallbackReason != PhysicsPresentationFallbackReason.None || frame.Contour.Count == 0)
        {
            throw new InvalidOperationException($"旧模块 soft-body 预览初始化或推进失败：{frame.FallbackReason} / native {frame.Diagnostics.NativeErrorCode}");
        }
        var width = frame.Contour.Max(x => x.X) - frame.Contour.Min(x => x.X);
        var height = frame.Contour.Max(x => x.Y) - frame.Contour.Min(x => x.Y);
        var elapsed = (clock.ElapsedMilliseconds - resetAt) / 1000d;
        StateText.Text = elapsed switch
        {
            < 0.12 => "AirCircle",
            < 0.55 => "Falling",
            < 0.78 => "ContactCompression",
            < 1.05 => "TinyRebound",
            _ => "GroundedRest",
        };
        var metrics = frame.Diagnostics.SoftBody!;
        MetricsText.Text = string.Create(CultureInfo.InvariantCulture,
            $"面积 {metrics.SignedArea:F3}　宽高比 {(width / height):F3}　最低点 {frame.Contour.Min(x => x.Y):F3}　最大速度 {metrics.MaximumNodeSpeed:F3}　质心 ({metrics.Centroid.X:F3}, {metrics.Centroid.Y:F3})　节点 {frame.Contour.Count}");
    }

    private void BuildParameterEditors()
    {
        buildingControls = true;
        AddGroup(ShapeParameters,
        [
            ("宽度", "RestWidth", 1.1, 1.9, "增大：落地后更宽。"),
            ("高度", "RestHeight", 0.9, 1.7, "增大：落地常态更高、更接近圆形。"),
            ("顶部圆度", "TopRoundness", 0, 1, "增大：头顶更饱满；减小：顶部更收。"),
            ("下半部鼓度", "LowerBulge", 0, 0.4, "增大：身体下半部向两侧鼓出。"),
            ("底部平坦度", "BottomFlatness", 0, 1, "增大：底边更平；减小：底部更圆。"),
            ("常态压缩", "GroundCompression", 0, 0.35, "增大：落地常态更扁。")
        ]);
        AddGroup(LandingParameters,
        [
            ("下落高度", "DropHeight", 0.05, 1.5, "增大：下落距离更长。"),
            ("撞击速度", "ImpactSpeed", 0.1, 4, "增大：触地更重、瞬间形变更明显。"),
            ("瞬间压缩", "MaximumCompression", 0.02, 0.4, "增大：触地瞬间压得更扁。"),
            ("瞬间外扩", "LateralExpansion", 0, 0.4, "增大：触地瞬间向两侧摊开。"),
            ("回弹", "ReboundStrength", 0, 0.4, "增大：触地后的回弹更明显。"),
            ("稳定速度", "SettleTime", 0.2, 3, "增大：从撞击形状回到常态更慢。")
        ]);
        AddGroup(MaterialParameters,
        [
            ("内部弹性", "RadialElasticity", 1, 20, "增大：更快恢复整体体积，手感更硬。"),
            ("外圈弹性", "ContourElasticity", 1, 25, "增大：轮廓更整齐，局部波动更少。"),
            ("整体阻尼", "OverallDamping", 0, 8, "增大：晃动更快停止；过大会显得迟钝。")
        ]);
        AddGroup(ContactParameters,
        [
            ("接触软硬", "ContactSoftness", 1, 60, "增大：地面接触更硬；减小：更软但可能下陷。")
        ]);
        AddChoice(QualityParameters, "轮廓节点", nameof(SoftBodyTuningParameters.NodeCount), [12, 16], "16 更圆滑，12 更省性能。");
        AddChoice(QualityParameters, "物理子步", nameof(SoftBodyTuningParameters.SubStepCount), [2, 4], "4 更稳定，2 更省性能；不会产生中间值。");
        buildingControls = false;
    }

    private void AddGroup(System.Windows.Controls.Panel panel, IEnumerable<(string Label, string Property, double Minimum, double Maximum, string Description)> definitions)
    {
        foreach (var definition in definitions)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 8, 7) };
            row.RowDefinitions.Add(new RowDefinition());
            row.RowDefinitions.Add(new RowDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            var label = new TextBlock { Text = definition.Label, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = definition.Minimum, Maximum = definition.Maximum, Margin = new Thickness(6, 0, 8, 0), Tag = definition.Property };
            var text = new System.Windows.Controls.TextBox { VerticalContentAlignment = VerticalAlignment.Center, Tag = definition.Property };
            var description = new TextBlock { Text = definition.Description, Foreground = System.Windows.Media.Brushes.DimGray, FontSize = 11, Margin = new Thickness(6, 1, 0, 0) };
            Grid.SetColumn(slider, 1); Grid.SetColumn(text, 2); Grid.SetRow(description, 1); Grid.SetColumn(description, 1); Grid.SetColumnSpan(description, 2);
            row.Children.Add(label); row.Children.Add(slider); row.Children.Add(text); row.Children.Add(description); panel.Children.Add(row);
            editors[definition.Property] = (slider, text);
            slider.ValueChanged += OnSliderChanged;
            text.LostKeyboardFocus += OnTextCommitted;
        }
        RefreshEditors();
    }

    private void AddChoice(System.Windows.Controls.Panel panel, string label, string propertyName, int[] choices, string description)
    {
        var row = new StackPanel { Margin = new Thickness(0, 3, 8, 8) };
        var line = new DockPanel();
        line.Children.Add(new TextBlock { Text = label, Width = 112, VerticalAlignment = VerticalAlignment.Center });
        var combo = new System.Windows.Controls.ComboBox { ItemsSource = choices, SelectedValue = (int)typeof(SoftBodyTuningParameters).GetProperty(propertyName)!.GetValue(parameters)!, Tag = propertyName, MinWidth = 90 };
        combo.SelectionChanged += (_, _) =>
        {
            if (!buildingControls && combo.SelectedValue is int value) ApplyParameter(propertyName, value);
        };
        line.Children.Add(combo);
        row.Children.Add(line);
        row.Children.Add(new TextBlock { Text = description, Foreground = System.Windows.Media.Brushes.DimGray, FontSize = 11, Margin = new Thickness(112, 1, 0, 0) });
        panel.Children.Add(row);
        choiceEditors[propertyName] = combo;
    }

    private void RefreshEditors()
    {
        buildingControls = true;
        foreach (var (name, editor) in editors)
        {
            var value = Convert.ToDouble(typeof(SoftBodyTuningParameters).GetProperty(name)!.GetValue(parameters), CultureInfo.InvariantCulture);
            editor.Slider.Value = value;
            editor.Text.Text = value.ToString(name is "NodeCount" or "SubStepCount" ? "F0" : "0.####", CultureInfo.InvariantCulture);
        }
        foreach (var (name, editor) in choiceEditors)
        {
            editor.SelectedValue = (int)typeof(SoftBodyTuningParameters).GetProperty(name)!.GetValue(parameters)!;
        }
        buildingControls = false;
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (buildingControls) return;
        var slider = (Slider)sender;
        SetParameter((string)slider.Tag, slider.Value);
    }

    private void OnTextCommitted(object sender, RoutedEventArgs e)
    {
        var box = (System.Windows.Controls.TextBox)sender;
        if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) SetParameter((string)box.Tag, value);
        else RefreshEditors();
    }

    private void SetParameter(string name, double value)
    {
        ApplyParameter(name, value);
    }

    private void ApplyParameter(string name, double value)
    {
        try
        {
            parameters = parameters.WithNumericValue(name, value);
            RefreshEditors();
            rebuildTimer.Stop(); rebuildTimer.Start();
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            MetricsText.Text = $"参数未应用：{name} — {exception.Message}";
            RefreshEditors();
        }
    }

    private void OnResetDrop(object sender, RoutedEventArgs e) => RebuildWorld();
    private void OnResetDefaults(object sender, RoutedEventArgs e) { parameters = new(); RefreshEditors(); RebuildWorld(); }
    private void OnDisplayOptionChanged(object sender, RoutedEventArgs e)
    {
        ComparePanel.Visibility = CompareToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PreviewDango.ShowDebugGeometry = DebugToggle.IsChecked == true;
        PreviewDango.ShowAccessories = ContourOnlyToggle.IsChecked != true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Dango 参数 (*.json)|*.json", FileName = "dango-landing-tuning.json" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, parameters.ToJson());
    }

    private void OnLoad(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Dango 参数 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { parameters = SoftBodyTuningParameters.FromJson(File.ReadAllText(dialog.FileName)); RefreshEditors(); RebuildWorld(); }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or ArgumentOutOfRangeException)
        { System.Windows.MessageBox.Show(this, exception.Message, "参数文件无效", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        frameTimer.Stop(); rebuildTimer.Stop(); controller?.Dispose(); controller = null;
    }
}
