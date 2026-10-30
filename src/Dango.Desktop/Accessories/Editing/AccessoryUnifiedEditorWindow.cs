using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Shell;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Desktop.AppShell;
using Dango.Desktop.Rendering.Visuals;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace Dango.Desktop.Accessories.Editing;

/// <summary>The native modal owns browser lifetime and complete project transactions; editor controls live in miniPaint.</summary>
internal sealed class AccessoryUnifiedEditorWindow : Window
{
    private readonly AccessoryUnifiedEditorSession session;
    private readonly AccessoryEditorBrowserHost browser = new();
    private readonly Grid footer = new() { Margin = new Thickness(0, 6, 0, 0), MinHeight = 32 };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button retry;
    private readonly Button confirm;
    private readonly Button cancelButton;
    private readonly AccessorySimulationView simulation;
    private readonly Grid browserStage = new();
    private bool dynamicVisible, browserInputReserved, nativeMenuOpen;
    private readonly DispatcherTimer simulationFocusTimer = new(DispatcherPriority.Input)
    { Interval = TimeSpan.FromMilliseconds(75) };
    private AccessorySimulationViewport? simulationViewport;
    private AccessoryEditResult? simulationContent;
    private string? simulationError;
    private readonly DispatcherTimer operationWatch;
    private readonly Action<AccessoryEditResult>? previewCallback;
    private readonly Action? previewInvalidatedCallback;
    private DispatcherOperation? queuedPreview;
    private bool previewActive, previewWanted;
    private readonly string profile = Path.Combine(Path.GetTempPath(), "Dango", "accessory-editor", Guid.NewGuid().ToString("N"));
    private bool initializing;
    private bool released;
    internal AccessoryEditResult? Result => session.Result;
    internal AccessoryItem Draft => session.Parameters;
    internal string StatusMessage => status.Text;

    internal AccessoryUnifiedEditorWindow(AccessoryEditDraft draft, AccessoryEditorSourceAsset? source,
        AccessoryEditorProject? project, Action<AccessoryEditResult>? preview = null, Action? previewInvalidated = null,
        DangoAppearanceConfiguration? appearance = null)
    {
        session = new(draft, source, project); previewCallback = preview; previewInvalidatedCallback = previewInvalidated;
        simulation = new(appearance ?? DangoAppearanceConfiguration.Default)
        { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        AccessoryEditorTheme.Install(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/Dango.Desktop;component/Accessories/Editing/AccessoryUnifiedEditorStyles.xaml", UriKind.Relative) });
        Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/Dango.Desktop;component/Resources/ProductWindowChromeResources.xaml", UriKind.Relative) });
        Title = "编辑配饰"; Width = 1120; Height = 760; MinWidth = 800; MinHeight = 640;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0, CornerRadius = new CornerRadius(14), GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(7), UseAeroCaptionButtons = false,
        });
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "HomeBackground");
        var layout = new Grid { Margin = new Thickness(8) };
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        browserStage.Children.Add(browser); browserStage.Children.Add(simulation); layout.Children.Add(browserStage);
        // HwndHost cannot be covered by WPF content. Its matching native cutout leaves
        // these controls reachable without adding a title row or shrinking the viewport.
        browser.WindowControlsCutout = new System.Windows.Size(132, 38);
        var chrome = new Grid { Width = 132, Height = 38, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top };
        chrome.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "HomeBackground");
        chrome.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource != chrome) return;
            if (e.ClickCount == 2) ToggleWindowSize();
            else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            e.Handled = true;
        };
        var windowButtons = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 1, 0) };
        var minimize = WindowButton("最小化窗口", "最小化", "M 5,10 L 17,10", () => WindowState = WindowState.Minimized);
        var maximize = WindowButton("最大化窗口", "最大化", "M 6,5 L 16,5 L 16,15 L 6,15 Z", ToggleWindowSize);
        StateChanged += (_, _) =>
        {
            var maximized = WindowState == WindowState.Maximized;
            maximize.ToolTip = maximized ? "还原" : "最大化";
            AutomationProperties.SetName(maximize, maximized ? "还原窗口" : "最大化窗口");
            ((System.Windows.Shapes.Path)maximize.Content).Data = Geometry.Parse(maximized
                ? "M 8,5 L 17,5 L 17,14 M 5,8 L 14,8 L 14,17 L 5,17 Z" : "M 6,5 L 16,5 L 16,15 L 6,15 Z");
            UpdateSimulationVisibility(); ScheduleSimulationFocus();
        };
        windowButtons.Children.Add(minimize); windowButtons.Children.Add(maximize);
        windowButtons.Children.Add(WindowButton("关闭窗口", "关闭", "M 6,5 L 16,15 M 16,5 L 6,15", Close));
        chrome.Children.Add(windowButtons); layout.Children.Add(chrome);
        AutomationProperties.SetName(browser, "配饰统一编辑器，图像和骨架工具位于左侧，图层组和组属性位于右侧");

        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        status.Margin = new Thickness(0, 0, 16, 0); footer.Children.Add(status);
        status.SetResourceReference(TextBlock.ForegroundProperty, "HomeInk");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        retry = ActionButton("重试", AccessoryEditorTheme.ResetIcon, () => _ = InitializeAsync());
        actions.Children.Add(retry);
        cancelButton = ActionButton("取消", AccessoryEditorTheme.CancelIcon, Cancel); cancelButton.IsCancel = true;
        actions.Children.Add(cancelButton);
        confirm = ActionButton("确认", AccessoryEditorTheme.ApplyIcon, () => RequestSnapshot(true), true);
        // Enter belongs to the active browser tool or input, never to modal confirmation.
        confirm.IsDefault = false;
        actions.Children.Add(confirm); Grid.SetColumn(actions, 1); footer.Children.Add(actions);
        Grid.SetRow(footer, 1); layout.Children.Add(footer); Content = layout;

        session.Outgoing += Send;
        session.Changed += Refresh;
        session.PreviewReady += OnPreview;
        session.ContentChanged += OnContentChanged;
        session.ViewCommandReceived += OnViewCommand;
        session.SimulationViewportReceived += OnSimulationViewport;
        session.WearChanged += OnWearChanged;
        simulation.AnchorPicked += session.RequestWearEdit;
        simulation.ToolChanged += OnSimulationToolChanged;
        simulation.Failed += OnSimulationFailed;
        simulation.Activated += ScheduleSimulationFocus;
        browser.Runtime.MessageReceived += session.Receive;
        browser.Runtime.StateChanged += RuntimeChanged;
        Loaded += (_, _) => _ = InitializeAsync();
        layout.SizeChanged += (_, _) => UpdateFooterLayout(layout.ActualWidth);
        browserStage.SizeChanged += (_, _) => UpdateSimulationViewport();
        Closed += (_, _) => Release();
        Deactivated += (_, _) => { simulationFocusTimer.Stop(); simulation.ClearInput(); };
        Activated += (_, _) => ScheduleSimulationFocus();
        IsEnabledChanged += (_, _) => { if (!IsEnabled) simulation.ClearInput(); else ScheduleSimulationFocus(); };
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnEditorFocusChanged), true);
        AddHandler(Button.ClickEvent, new RoutedEventHandler((_, _) => ScheduleSimulationFocus()), true);
        ContextMenuOpening += (_, _) => { nativeMenuOpen = true; simulationFocusTimer.Stop(); simulation.ClearInput(); };
        ContextMenuClosing += (_, _) => { nativeMenuOpen = false; ScheduleSimulationFocus(); };
        simulationFocusTimer.Tick += (_, _) => { simulationFocusTimer.Stop(); TryRestoreSimulationFocus(); };
        IsVisibleChanged += (_, _) => UpdateSimulationVisibility();
        PreviewKeyDown += OnSimulationKeyDown;
        operationWatch = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => session.CheckTimeout(), Dispatcher);
        operationWatch.Stop(); Refresh();
    }

    private async Task InitializeAsync()
    {
        if (released || initializing) return;
        initializing = true; retry.IsEnabled = false;
        try
        {
            status.Text = "正在加载本地图片编辑器…";
            if (await browser.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "AccessoryEditor"), profile) && !released)
            { operationWatch.Start(); session.Open(); }
            else if (!released) session.Fail(browser.Runtime.ErrorMessage ?? "编辑器无法启动。草稿仍保留，可重试或取消。");
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException)
        { if (!released) session.Fail("编辑器无法启动。草稿仍保留，可重试或取消。"); }
        finally { initializing = false; if (!released) Refresh(); }
    }

    private void Send(string json)
    {
        if (released) return;
        try { if (!browser.Runtime.Post(json)) session.Fail("编辑器连接已失效。草稿仍保留，可重试或取消。"); }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { session.Fail("编辑器连接已失效。草稿仍保留，可重试或取消。"); }
    }

    private void RuntimeChanged()
    {
        if (!released && browser.Runtime.State == AccessoryEditorRuntimeState.Failed)
            session.Fail(browser.Runtime.ErrorMessage ?? "编辑器进程已退出。草稿仍保留，可重试或取消。");
    }

    private void RequestSnapshot(bool accept)
    {
        if (released) return;
        previewWanted = false; CancelQueuedPreview(); session.RequestSnapshot(accept);
    }
    private void OnPreview(AccessoryEditResult result)
    {
        if (released) return;
        previewActive = dynamicVisible; previewCallback?.Invoke(result);
        simulationContent = result;
        simulation.SetContent(result);
        UpdateSimulationVisibility();
        session.PostSimulationState(simulation.Tool);
    }

    private void OnViewCommand(string command, double x, double y)
    {
        if (released) return;
        if (command == "window-drag")
        {
            if (IsActive && Mouse.LeftButton == MouseButtonState.Pressed)
            { try { DragMove(); } catch (InvalidOperationException) { } }
            return;
        }
        if (command == "preview-hide")
        {
            simulationFocusTimer.Stop(); browserInputReserved = false;
            dynamicVisible = false; cancelButton.IsCancel = true; previewActive = false; previewWanted = false; CancelQueuedPreview();
            UpdateSimulationVisibility(); return;
        }
        if (command == "preview-show")
        {
            browserInputReserved = false;
            dynamicVisible = true; cancelButton.IsCancel = false; previewActive = true; previewWanted = true; QueuePreview();
            UpdateSimulationVisibility(); session.PostSimulationState(simulation.Tool, simulationError, updating: true); return;
        }
        if (command == "simulation-focus")
        {
            browserInputReserved = x == 1;
            if (browserInputReserved) { simulationFocusTimer.Stop(); simulation.ClearInput(); }
            else ScheduleSimulationFocus();
            return;
        }
        if (dynamicVisible && command == "simulation-refresh")
        { RefreshSimulationBody(); return; }
        if (dynamicVisible && command == "simulation-tool")
        { simulation.SetTool(new[] { "default", "pick", "pan", "zoom" }[(int)x]); ScheduleSimulationFocus(); }
    }

    private async void RefreshSimulationBody()
    {
        try
        {
            session.PostSimulationState(simulation.Tool, updating: true);
            await simulation.RefreshBodyAsync();
            if (released) return;
            if (simulation.Error is { } failure) { OnSimulationFailed(failure); return; }
            simulationError = null;
            UpdateSimulationVisibility();
            session.PostSimulationState(simulation.Tool);
            ScheduleSimulationFocus();
        }
        catch (Exception error)
        {
            if (!released) OnSimulationFailed(error.Message);
        }
    }

    private void OnSimulationViewport(AccessorySimulationViewport viewport)
    {
        simulationViewport = viewport; UpdateSimulationViewport();
    }

    private void UpdateSimulationViewport()
    {
        if (released) return;
        var rectangle = Rect.Empty;
        if (dynamicVisible && simulationViewport is { Visible: true } viewport)
        {
            rectangle = new(viewport.X, Math.Max(38, viewport.Y), viewport.Width, viewport.Height);
            rectangle.Intersect(new Rect(0, 38, Math.Max(0, browserStage.ActualWidth), Math.Max(0, browserStage.ActualHeight - 38)));
        }
        if (rectangle.IsEmpty || rectangle.Width < 1 || rectangle.Height < 1) rectangle = Rect.Empty;
        browser.SimulationCutout = rectangle;
        if (!rectangle.IsEmpty)
        {
            simulation.Margin = new(rectangle.X, rectangle.Y, 0, 0);
            simulation.Width = rectangle.Width; simulation.Height = rectangle.Height;
        }
        simulation.Visibility = rectangle.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        UpdateSimulationVisibility(updateBounds: false);
    }

    private void UpdateSimulationVisibility(bool updateBounds = true)
    {
        if (released) return;
        if (updateBounds) { UpdateSimulationViewport(); return; }
        simulation.SetActive(dynamicVisible && simulation.Visibility == Visibility.Visible && IsVisible &&
            WindowState != WindowState.Minimized && simulationError is null &&
            session.State is AccessoryUnifiedEditorState.Ready or AccessoryUnifiedEditorState.Busy);
        if (simulation.IsActive) ScheduleSimulationFocus();
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();

    private void ScheduleSimulationFocus()
    {
        simulationFocusTimer.Stop();
        if (!released && dynamicVisible) simulationFocusTimer.Start();
    }

    private static bool RequiresNativeInput(IInputElement? focus)
    {
        for (var node = focus as DependencyObject; node is not null;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox or System.Windows.Controls.ComboBox or
                System.Windows.Controls.Primitives.RangeBase or System.Windows.Controls.Primitives.MenuBase) return true;
        return false;
    }

    // The optional test seams evaluate qualification and focus intent without activating
    // the offscreen HWND. Production always uses this exact window's current state.
    internal bool TryRestoreSimulationFocus(Func<bool>? windowEligible = null, Action? restore = null)
    {
        if (released || !dynamicVisible || !simulation.IsActive || !simulation.IsVisible || !simulation.IsEnabled ||
            browserInputReserved || nativeMenuOpen || RequiresNativeInput(Keyboard.FocusedElement)) return false;
        var eligible = windowEligible ?? (() => IsLoaded && IsVisible && IsEnabled && IsActive &&
            WindowState != WindowState.Minimized && new WindowInteropHelper(this).Handle == GetForegroundWindow());
        if (!eligible()) return false;
        if (restore is not null) restore();
        else
        {
            InputMethod.SetIsInputMethodEnabled(simulation, false);
            simulation.Focus();
            simulation.ReconcileKeyboard(new[] { Key.A, Key.D, Key.W, Key.S, Key.Space }.Where(Keyboard.IsKeyDown));
        }
        return true;
    }

    private void OnEditorFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!simulation.IsKeyboardFocusWithin) simulation.ClearInput();
        if (RequiresNativeInput(e.NewFocus)) simulationFocusTimer.Stop();
        else ScheduleSimulationFocus();
    }

    private void OnWearChanged()
    {
        if (simulationContent is { } content)
        {
            simulationContent = new(content.Project, content.Image, session.Parameters);
            simulation.SetContent(simulationContent);
        }
    }

    private void OnSimulationToolChanged(string tool) => session.PostSimulationState(tool, simulationError ?? session.ErrorMessage,
        previewWanted || session.State == AccessoryUnifiedEditorState.Busy);
    private void OnSimulationFailed(string error)
    {
        simulationError = "物理预览暂不可用，请点击左侧“刷新团子”重试。";
        simulation.SetActive(false); session.PostSimulationState(simulation.Tool, simulationError);
    }

    private void OnSimulationKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!dynamicVisible || !simulation.IsKeyboardFocusWithin) return;
        if (e.Key == Key.Escape) { simulation.CancelPick(); simulation.SetTool("default"); e.Handled = true; return; }
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key is Key.Z or Key.Y)
        {
            e.Handled = true;
            var method = e.Key == Key.Y || (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? "redo" : "undo";
            if (browser.Runtime.Core is { } core) _ = core.ExecuteScriptAsync($"window.DangoMiniPaint?.{method}()");
        }
    }
    private void OnContentChanged()
    {
        if (released || !previewActive) return;
        previewInvalidatedCallback?.Invoke();
        previewWanted = true; QueuePreview();
    }
    private void QueuePreview()
    {
        if (released || !previewActive || !previewWanted || queuedPreview is not null || session.State != AccessoryUnifiedEditorState.Ready) return;
        queuedPreview = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            queuedPreview = null;
            if (released || !previewWanted || session.State != AccessoryUnifiedEditorState.Ready) return;
            // Consume this request before dispatch. A failed export must wait for another real
            // edit/user request, rather than repeatedly retrying from its Ready/Error transition.
            previewWanted = false; RequestSnapshot(false);
        }));
    }
    private void CancelQueuedPreview()
    {
        queuedPreview?.Abort(); queuedPreview = null;
    }
    private void Refresh()
    {
        if (released) return;
        if (session.State is AccessoryUnifiedEditorState.Failed or AccessoryUnifiedEditorState.Closed)
        {
            previewWanted = false; previewActive = false; CancelQueuedPreview();
            dynamicVisible = false; cancelButton.IsCancel = true; UpdateSimulationVisibility();
        }
        else QueuePreview();
        if (dynamicVisible) session.PostSimulationState(simulation.Tool, simulationError ?? session.ErrorMessage,
            previewWanted || session.State == AccessoryUnifiedEditorState.Busy);
        var ready = session.State == AccessoryUnifiedEditorState.Ready;
        confirm.IsEnabled = ready;
        retry.Visibility = session.State == AccessoryUnifiedEditorState.Failed ? Visibility.Visible : Visibility.Collapsed;
        retry.IsEnabled = !initializing;
        status.Text = session.ErrorMessage ?? (previewWanted ? "已修改，正在更新动态预览…" : session.State == AccessoryUnifiedEditorState.Busy ? "正在校验图片与工程…" :
            session.State == AccessoryUnifiedEditorState.Loading ? "正在加载本地配饰编辑器…" :
            session.RestoredCheckpoint ? "已恢复最近完整预览或初始工程；未导出的画布修改未恢复。" : "确认后返回草稿，应用后保存。");
        if (session.State == AccessoryUnifiedEditorState.Failed)
            status.Text += " 重试将恢复最近完整预览或初始工程，未导出的画布修改可能丢失。";
        if (session.State == AccessoryUnifiedEditorState.Closed && IsLoaded)
        { operationWatch.Stop(); DialogResult = session.Result is not null; }
    }

    private void UpdateFooterLayout(double width)
    {
        var narrow = width < 868;
        Grid.SetColumnSpan(status, narrow ? 2 : 1);
        Grid.SetRow(actions, narrow ? 1 : 0); Grid.SetColumn(actions, narrow ? 0 : 1); Grid.SetColumnSpan(actions, narrow ? 2 : 1);
        status.Margin = narrow ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 16, 0);
    }

    private static Button ActionButton(string label, string icon, Action action, bool primary = false)
    {
        var result = AccessoryEditorTheme.IconButton(label, icon, label, action, primary);
        result.Width = 32; result.Height = 32; result.MinWidth = 32; result.MinHeight = 32;
        result.Padding = new Thickness(0); result.Margin = new Thickness(8, 0, 0, 0);
        result.Focusable = false; return result;
    }
    private Button WindowButton(string name, string tooltip, string geometry, Action action)
    {
        var button = new Button { Style = (Style)FindResource("ProductWindowButton"), ToolTip = tooltip, Focusable = false };
        var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        path.SetBinding(System.Windows.Shapes.Path.StrokeProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = button });
        button.Content = path;
        AutomationProperties.SetName(button, name); ToolTipService.SetInitialShowDelay(button, 250);
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        button.Click += (_, _) => action();
        return button;
    }
    private void ToggleWindowSize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Cancel() { session.Cancel(); DialogResult = false; }
    private void Release()
    {
        if (released) return;
        simulationFocusTimer.Stop();
        released = true; previewWanted = false; CancelQueuedPreview(); operationWatch.Stop();
        simulation.Dispose();
        session.ViewCommandReceived -= OnViewCommand;
        session.SimulationViewportReceived -= OnSimulationViewport; session.WearChanged -= OnWearChanged;
        session.Outgoing -= Send; session.Changed -= Refresh; session.PreviewReady -= OnPreview; session.ContentChanged -= OnContentChanged;
        browser.Runtime.MessageReceived -= session.Receive; browser.Runtime.StateChanged -= RuntimeChanged;
        session.Dispose(); browser.ReleaseBrowser(); browser.Dispose();
    }
}
