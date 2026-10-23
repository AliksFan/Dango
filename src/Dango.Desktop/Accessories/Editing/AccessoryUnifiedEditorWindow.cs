using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace Dango.Desktop.Accessories.Editing;

/// <summary>One native modal transaction for image, attachment, chain and all shared motion parameters.</summary>
internal sealed class AccessoryUnifiedEditorWindow : Window
{
    private readonly AccessoryUnifiedEditorSession session;
    private readonly AccessoryEditorBrowserHost browser = new();
    private readonly Grid content = new();
    private readonly Grid footer = new() { Margin = new Thickness(0, 12, 0, 0), MinHeight = 60 };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = HorizontalAlignment.Right };
    private readonly StackPanel narrowNavigation = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8),
        Visibility = Visibility.Collapsed };
    private readonly Button imagePage;
    private readonly Button parameterPage;
    private readonly ScrollViewer parameters;
    private readonly StackPanel fields = new();
    private readonly Dictionary<string, (Slider Slider, TextBox Text)> numbers = [];
    private readonly TextBox name = new();
    private readonly ComboBox mode = new() { ItemsSource = new[] { "刚性", "骨架柔性" }, Margin = new Thickness(0, 6, 0, 6) };
    private readonly ComboBox layer = new() { ItemsSource = new[] { "团子前", "团子后" }, Margin = new Thickness(0, 6, 0, 6) };
    private readonly CheckBox lockRotation = new() { Content = "锁定整体朝向", Margin = new Thickness(0, 12, 0, 6) };
    private readonly CheckBox limitSwing = new() { Content = "限制整体摆角", Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button retry;
    private readonly Button confirm;
    private readonly Button? preview;
    private readonly DispatcherTimer operationWatch;
    private readonly Action<AccessoryEditResult>? previewCallback;
    private readonly string profile = Path.Combine(Path.GetTempPath(), "Dango", "accessory-editor", Guid.NewGuid().ToString("N"));
    private bool refreshing;
    private bool initializing;
    private bool released;
    private bool narrow;
    private bool showParameters;
    internal AccessoryEditResult? Result => session.Result;
    internal AccessoryItem Draft => session.Parameters;
    internal string StatusMessage => status.Text;

    internal AccessoryUnifiedEditorWindow(AccessoryEditDraft draft, AccessoryEditorSourceAsset? source,
        AccessoryEditorProject? project, Action<AccessoryEditResult>? preview = null)
    {
        session = new(draft, source, project); previewCallback = preview;
        AccessoryEditorTheme.Install(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/Dango.Desktop;component/Accessories/Editing/AccessoryUnifiedEditorStyles.xaml", UriKind.Relative) });
        Title = "编辑配饰"; Width = 1120; Height = 760; MinWidth = 560; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "HomeBackground");
        var layout = new Grid { Margin = new Thickness(16) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        imagePage = ActionButton("图像", "M3,4 L21,4 L21,20 L3,20 Z M4,16 L9,10 L14,15 L17,12 L21,17", () => SelectNarrowPage(false));
        parameterPage = ActionButton("参数", "M4,6 L20,6 M4,12 L20,12 M4,18 L20,18 M8,3 L8,9 M16,9 L16,15 M10,15 L10,21", () => SelectNarrowPage(true));
        imagePage.Margin = new Thickness(0, 0, 8, 0); parameterPage.Margin = new Thickness(0);
        narrowNavigation.Children.Add(imagePage); narrowNavigation.Children.Add(parameterPage); layout.Children.Add(narrowNavigation);
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(292) });
        content.RowDefinitions.Add(new RowDefinition());
        content.Children.Add(browser);
        parameters = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(16, 0, 0, 4) };
        Grid.SetColumn(parameters, 1); content.Children.Add(parameters); Grid.SetRow(content, 1); layout.Children.Add(content);
        AutomationProperties.SetName(browser, "配饰图片画布，滚轮缩放；图片和文字输入在画布内操作");
        AutomationProperties.SetName(parameters, "配饰参数");
        AutomationProperties.SetName(name, "配饰名称");
        fields.Children.Add(new TextBlock { Text = "名称" }); fields.Children.Add(name);
        fields.Children.Add(new TextBlock { Text = "形变方式", Margin = new Thickness(0, 12, 0, 0) }); fields.Children.Add(mode);
        AutomationProperties.SetName(mode, "形变方式");
        fields.Children.Add(layer); AutomationProperties.SetName(layer, "显示层次");
        AddNumber("size", "尺寸 %", 10, 400, draft.Original.SizePercent);
        AddNumber("angle", "基础角度 °", -180, 180, draft.Original.BaseAngleDegrees);
        AddNumber("pivotX", "图片根部 X", 0, 1, draft.Original.ImagePivotX);
        AddNumber("pivotY", "图片根部 Y", 0, 1, draft.Original.ImagePivotY);
        AddNumber("anchorX", "身体附着 X", 0, 1, draft.Original.BodyAnchorX);
        AddNumber("anchorY", "身体附着 Y", 0, 1, draft.Original.BodyAnchorY);
        AddNumber("connection", "连接强度", 0, 100, draft.Original.ConnectionStrength);
        AddNumber("travel", "连接最大行程 %", 0, 200, draft.Original.MaxConnectionTravelPercent);
        fields.Children.Add(lockRotation);
        AddNumber("rotationDamping", "整体旋转阻尼", 0, 100, draft.Original.RotationDamping);
        AddNumber("return", "整体回正", 0, 100, draft.Original.ReturnStrength);
        fields.Children.Add(limitSwing);
        AddNumber("swing", "整体最大摆角 °", 0, 180, draft.Original.MaxSwingDegrees);
        AddNumber("gravity", "重力影响", 0, 100, draft.Original.GravityInfluence);
        AddNumber("rigSoftness", "骨架柔软度", 0, 1, session.RigSoftness);
        AddNumber("rigDamping", "骨架阻尼 /s", 0, 30, session.RigDamping);
        AddNumber("rigRestoration", "骨架恢复 /s²", 0, 30, session.RigRestoration);
        var note = new TextBlock { Text = "连接点和中心线在图片画布内编辑。确认仅返回配饰草稿，应用后才保存。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted"); fields.Children.Add(note);

        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        status.Margin = new Thickness(0, 0, 16, 0); footer.Children.Add(status);
        status.SetResourceReference(TextBlock.ForegroundProperty, "HomeInk");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        retry = ActionButton("重试", AccessoryEditorTheme.ResetIcon, () => _ = InitializeAsync());
        actions.Children.Add(retry);
        if (preview is not null)
        {
            this.preview = ActionButton("试摆", "M3,12 L8,7 L8,17 Z M14,5 L21,12 L14,19", () => RequestSnapshot(false));
            actions.Children.Add(this.preview);
        }
        var cancel = ActionButton("取消", AccessoryEditorTheme.CancelIcon, Cancel); cancel.IsCancel = true;
        actions.Children.Add(cancel);
        confirm = ActionButton("确认", AccessoryEditorTheme.ApplyIcon, () => RequestSnapshot(true), true);
        confirm.IsDefault = true;
        actions.Children.Add(confirm); Grid.SetColumn(actions, 1); footer.Children.Add(actions);
        Grid.SetRow(footer, 2); layout.Children.Add(footer); Content = layout;

        session.Outgoing += Send;
        session.Changed += Refresh;
        session.PreviewReady += OnPreview;
        browser.Runtime.MessageReceived += session.Receive;
        browser.Runtime.StateChanged += RuntimeChanged;
        name.LostKeyboardFocus += (_, _) => { if (!refreshing) CommitInputs(); };
        mode.SelectionChanged += (_, _) => { if (!refreshing) CommitInputs(); };
        layer.SelectionChanged += (_, _) => { if (!refreshing) CommitInputs(); };
        lockRotation.Click += (_, _) => CommitInputs(); limitSwing.Click += (_, _) => CommitInputs();
        Loaded += (_, _) => _ = InitializeAsync();
        layout.SizeChanged += (_, _) => UpdateLayoutDirection(layout.ActualWidth);
        Closed += (_, _) => Release();
        operationWatch = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => session.CheckTimeout(), Dispatcher);
        operationWatch.Stop(); Refresh();
    }

    private void AddNumber(string key, string label, double minimum, double maximum, double initial)
    {
        var control = AccessoryEditorTheme.Number(label, label, minimum, maximum, initial,
            _ => { if (!refreshing) CommitInputs(); }, () => CommitInputs());
        numbers.Add(key, (control.Slider, control.Value)); fields.Children.Add(control.Row);
    }

    private bool CommitInputs()
    {
        if (refreshing || session.State != AccessoryUnifiedEditorState.Ready) return false;
        var values = new Dictionary<string, double>();
        foreach (var (key, control) in numbers)
        {
            if (!AccessoryEditorTheme.TryReadNumber(control.Text, control.Slider, out var value))
            {
                status.Text = $"{AutomationProperties.GetName(control.Slider)}需要 {Format(control.Slider.Minimum)}–{Format(control.Slider.Maximum)} 的有限数值；请修正后确认。";
                return false;
            }
            values.Add(key, value);
        }
        if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 128 || name.Text.Any(char.IsControl))
        { status.Text = "名称需要 1–128 个有效字符。"; return false; }
        var item = session.Parameters with
        {
            Name = name.Text, DeformationMode = mode.SelectedIndex == 1 ? AccessoryDeformationMode.SingleChain : AccessoryDeformationMode.Rigid,
            Layer = layer.SelectedIndex == 1 ? AccessoryLayer.Behind : AccessoryLayer.Front,
            SizePercent = values["size"], BaseAngleDegrees = values["angle"],
            BodyAnchorX = values["anchorX"], BodyAnchorY = values["anchorY"],
            ConnectionStrength = values["connection"], MaxConnectionTravelPercent = values["travel"],
            LockRotation = lockRotation.IsChecked == true, RotationDamping = values["rotationDamping"],
            ReturnStrength = values["return"], LimitSwing = limitSwing.IsChecked == true,
            MaxSwingDegrees = values["swing"], GravityInfluence = values["gravity"],
        };
        var oldGeometry = session.Geometry;
        var pivotChanged = oldGeometry.PivotX != values["pivotX"] || oldGeometry.PivotY != values["pivotY"];
        // Commit all buffered fields as one native update; intermediate refreshes must not overwrite another field.
        refreshing = true;
        try
        {
            session.UpdateParameters(item);
            session.UpdateRigParameters(values["rigSoftness"], values["rigDamping"], values["rigRestoration"]);
            if (pivotChanged)
            {
                var nodes = oldGeometry.Nodes.Select((n, i) => i == 0 ? new AccessoryRigNode(values["pivotX"], values["pivotY"]) : n).ToArray();
                var outside = nodes.Any(n => n.X is < 0 or > 1 || n.Y is < 0 or > 1);
                session.SetGeometry(new(values["pivotX"], values["pivotY"], nodes, outside));
            }
        }
        finally { refreshing = false; }
        Refresh(discardBuffers: true); return true;
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
    { if (CommitInputs()) session.RequestSnapshot(accept); }
    private void OnPreview(AccessoryEditResult result) => previewCallback?.Invoke(result);
    private void Refresh() => Refresh(false);
    private void Refresh(bool discardBuffers)
    {
        if (released || refreshing) return;
        var buffers = discardBuffers ? [] : numbers.Where(p => p.Value.Text.Text != Format(p.Value.Slider.Value))
            .ToDictionary(p => p.Key, p => p.Value.Text.Text);
        var editedName = name.IsKeyboardFocusWithin && name.Text != session.Parameters.Name ? name.Text : null;
        refreshing = true;
        try
        {
            var p = session.Parameters;
            name.Text = editedName ?? p.Name;
            mode.SelectedIndex = p.DeformationMode == AccessoryDeformationMode.SingleChain ? 1 : 0;
            layer.SelectedIndex = p.Layer == AccessoryLayer.Behind ? 1 : 0;
            lockRotation.IsChecked = p.LockRotation; limitSwing.IsChecked = p.LimitSwing;
            Set("size", p.SizePercent); Set("angle", p.BaseAngleDegrees);
            Set("pivotX", p.ImagePivotX); Set("pivotY", p.ImagePivotY);
            Set("anchorX", p.BodyAnchorX); Set("anchorY", p.BodyAnchorY);
            Set("connection", p.ConnectionStrength); Set("travel", p.MaxConnectionTravelPercent);
            Set("rotationDamping", p.RotationDamping); Set("return", p.ReturnStrength);
            Set("swing", p.MaxSwingDegrees); Set("gravity", p.GravityInfluence);
            Set("rigSoftness", session.RigSoftness); Set("rigDamping", session.RigDamping); Set("rigRestoration", session.RigRestoration);
            foreach (var buffer in buffers) numbers[buffer.Key].Text.Text = buffer.Value;
            var ready = session.State == AccessoryUnifiedEditorState.Ready;
            parameters.IsEnabled = ready;
            confirm.IsEnabled = ready && !session.Geometry.RequiresRebind;
            if (preview is not null) preview.IsEnabled = ready && !session.Geometry.RequiresRebind;
            retry.Visibility = session.State == AccessoryUnifiedEditorState.Failed ? Visibility.Visible : Visibility.Collapsed;
            retry.IsEnabled = !initializing;
            status.Text = session.ErrorMessage ?? (session.Geometry.RequiresRebind ? "裁剪移除了连接点或中心线。请重新定位，或在画布中撤销。" :
                session.State == AccessoryUnifiedEditorState.Busy ? "正在校验图片与工程…" :
                session.State == AccessoryUnifiedEditorState.Loading ? "正在加载本地图片编辑器…" :
                session.RestoredCheckpoint ? "已恢复最近完整预览或初始工程；未导出的画布修改未恢复。" : "确认后返回草稿，应用后保存。");
            if (session.State == AccessoryUnifiedEditorState.Failed)
                status.Text += " 重试将恢复最近完整预览或初始工程，未导出的画布修改可能丢失。";
            if (session.State == AccessoryUnifiedEditorState.Closed && IsLoaded)
            { operationWatch.Stop(); DialogResult = session.Result is not null; }
        }
        finally { refreshing = false; }
        void Set(string key, double value)
        { numbers[key].Slider.Value = value; numbers[key].Text.Text = Format(value); }
    }

    private void UpdateLayoutDirection(double width)
    {
        narrow = width < 868;
        content.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(292);
        Grid.SetColumn(parameters, narrow ? 0 : 1);
        parameters.Padding = narrow ? new Thickness(0, 0, 0, 4) : new Thickness(16, 0, 0, 4);
        narrowNavigation.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(status, narrow ? 2 : 1);
        Grid.SetRow(actions, narrow ? 1 : 0); Grid.SetColumn(actions, narrow ? 0 : 1); Grid.SetColumnSpan(actions, narrow ? 2 : 1);
        status.Margin = narrow ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 16, 0);
        UpdatePageVisibility();
    }

    private void SelectNarrowPage(bool parametersSelected)
    {
        showParameters = parametersSelected;
        UpdatePageVisibility();
    }

    private void UpdatePageVisibility()
    {
        // Hiding the child HWND preserves the document and its undo history; it does not reopen the browser.
        browser.Visibility = narrow && showParameters ? Visibility.Collapsed : Visibility.Visible;
        parameters.Visibility = narrow && !showParameters ? Visibility.Collapsed : Visibility.Visible;
        Select(imagePage, !showParameters); Select(parameterPage, showParameters);
        static void Select(Button button, bool selected)
        {
            button.Tag = selected ? "Selected" : null;
            button.SetResourceReference(BackgroundProperty, selected ? "HomeSelected" : "HomeBackground");
            button.SetResourceReference(BorderBrushProperty, selected ? "HomeAccent" : "HomeLine");
        }
    }

    private static Button ActionButton(string label, string icon, Action action, bool primary = false)
    {
        var result = AccessoryEditorTheme.IconButton(label, icon, label, action, primary);
        var image = result.Content; result.Content = null;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add((UIElement)image);
        content.Children.Add(new TextBlock { Text = label, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        result.Content = content; result.Width = double.NaN; result.Height = double.NaN; result.MinHeight = 40;
        result.Padding = new Thickness(14, 8, 14, 8); result.Margin = new Thickness(8, 0, 0, 0);
        result.Focusable = false; return result;
    }
    private static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);
    private void Cancel() { session.Cancel(); DialogResult = false; }
    private void Release()
    {
        if (released) return;
        released = true; operationWatch.Stop();
        session.Outgoing -= Send; session.Changed -= Refresh; session.PreviewReady -= OnPreview;
        browser.Runtime.MessageReceived -= session.Receive; browser.Runtime.StateChanged -= RuntimeChanged;
        session.Dispose(); browser.ReleaseBrowser(); browser.Dispose();
    }
}
