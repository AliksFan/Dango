using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Control = System.Windows.Controls.Control;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;

namespace Dango.Desktop.Accessories;

internal enum AccessoryEditorSection { Library, Placement, Connection, Rotation }

/// <summary>Local wardrobe draft UI; host navigation owns the outer cancellation boundary.</summary>
internal sealed class AccessoryEditorPanel : StackPanel
{
    private AccessorySettingsSession? session;
    private readonly ListBox list = new() { Height = 240, MinWidth = 0, Padding = new Thickness(0), Margin = new Thickness(0, 6, 0, 6) };
    private readonly StackPanel library = new();
    private readonly TextBox name = new() { MinWidth = 0, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel editor = new();
    private readonly Dictionary<string, Slider> sliders = [];
    private readonly Dictionary<string, TextBox> valueTexts = [];
    private readonly Dictionary<AccessoryEditorSection, StackPanel> sections = [];
    private readonly Dictionary<AccessoryEditorSection, Button> sectionButtons = [];
    private readonly Dictionary<string, LibraryCard> libraryCards = [];
    private readonly Dictionary<string, BitmapSource?> thumbnails = [];
    private readonly LinkedList<string> thumbnailRecency = new();
    private readonly HashSet<Image> thumbnailOwners = [];
    private ScrollViewer? outerScroll;
    private bool thumbnailUpdateQueued;
    private readonly CheckBox equipped = new() { Content = "佩戴", Margin = new Thickness(0, 4, 0, 4) };
    private readonly CheckBox lockRotation = new() { Content = "锁定朝向", Margin = new Thickness(0, 4, 0, 4) };
    private readonly CheckBox limitSwing = new() { Content = "限制摆角", Margin = new Thickness(0, 4, 0, 4) };
    private readonly ComboBox layer = new() { MinWidth = 0, Margin = new Thickness(0, 3, 0, 8) };
    private readonly Button apply;
    private readonly Button reset;
    private bool refreshing;
    private long generation;
    private string? selectedId;
    private string? previewError;
    private string? operationError;
    private string previewNotice = "拖动即时预览 · 应用后保存";
    private bool? reportedDirty;

    internal AccessoryItem? SelectedItem => session?.DraftConfiguration.Items.FirstOrDefault(item => item.Id == selectedId);
    internal string StatusMessage => status.Text;
    internal FrameworkElement SaveActions { get; }
    internal FrameworkElement SectionTabs { get; }
    internal AccessoryEditorSection SelectedSection { get; private set; } = AccessoryEditorSection.Library;
    internal event Action? SectionChanged;
    internal Func<string?>? ImagePicker { get; set; }
    internal const int MaximumThumbnailCache = 32;
    internal const int ThumbnailEdge = 48;
    internal int ThumbnailCacheCount => thumbnails.Count;
    internal int ThumbnailDecodeCount { get; private set; }
    internal int VisibleThumbnailCount => thumbnailOwners.Count;
    internal string PreviewNotice => previewNotice;
    internal event Action? PresentationChanged;
    internal event Action<AccessoryConfiguration>? PreviewChanged;
    internal event Action<AccessoryConfiguration>? Applied;
    internal event Action? CancelRequested;
    internal Action? BodyAnchorPickRequested { get; set; }
    internal Func<AccessoryItem, BitmapSource, AccessoryItem?>? ImageEditor { get; set; }

    internal AccessoryEditorPanel()
    {
        AccessoryEditorTheme.Install(this);
        Loaded += (_, _) =>
        {
            AccessoryEditorTheme.AdoptParent(this);
            outerScroll = Ancestors(this).OfType<ScrollViewer>().FirstOrDefault();
            if (outerScroll is not null) outerScroll.ScrollChanged += OnLibraryScrollChanged;
            QueueThumbnailUpdate();
        };
        Unloaded += (_, _) =>
        {
            if (outerScroll is not null) outerScroll.ScrollChanged -= OnLibraryScrollChanged;
            outerScroll = null;
            ClearVisibleThumbnails();
        };
        var tabs = new StackPanel();
        AccessoryEditorTheme.Install(tabs);
        tabs.Loaded += (_, _) => AccessoryEditorTheme.AdoptParent(tabs);
        SectionTabs = tabs;
        AddSectionTab(tabs, AccessoryEditorSection.Library, "配饰库", "M3,3 L10,3 L10,10 L3,10 Z M14,3 L21,3 L21,10 L14,10 Z M3,14 L10,14 L10,21 L3,21 Z M14,14 L21,14 L21,21 L14,21 Z");
        AddSectionTab(tabs, AccessoryEditorSection.Placement, "图像与位置", "M4,4 L20,4 L20,20 L4,20 Z M4,16 L10,10 L15,15 L18,12 L20,14 M8,7 L8,8");
        AddSectionTab(tabs, AccessoryEditorSection.Connection, "柔性与连接", "M4,5 C17,3 5,21 20,19 M5,8 L3,5 L6,3 M18,16 L21,19 L18,21");
        AddSectionTab(tabs, AccessoryEditorSection.Rotation, "旋转与重力", "M5,8 A8,8 0 1 1 4,15 M5,3 L5,8 L10,8 M12,8 L12,16 M9,13 L12,16 L15,13");
        library.Children.Add(new TextBlock { Text = "配饰库" });
        AutomationProperties.SetName(list, "配饰库，勾选标记表示已佩戴");
        list.SetResourceReference(Control.BackgroundProperty, "HomeSoft");
        list.SetResourceReference(Control.BorderBrushProperty, "HomeLine");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        ScrollViewer.SetCanContentScroll(list, false);
        var cells = new FrameworkElementFactory(typeof(UniformGrid)); cells.SetValue(UniformGrid.ColumnsProperty, 4);
        cells.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        list.ItemsPanel = new ItemsPanelTemplate(cells);
        list.SizeChanged += (_, _) => QueueThumbnailUpdate();
        list.IsVisibleChanged += (_, _) => QueueThumbnailUpdate();
        list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnLibraryScrollChanged));
        library.Children.Add(list);
        list.SelectionChanged += (_, _) =>
        {
            if (refreshing) return;
            if ((list.SelectedItem as ListBoxItem)?.Tag is string nextId) SelectItem(nextId);
        };
        var libraryActions = new WrapPanel();
        libraryActions.Children.Add(ActionButton("创建新配饰", Import, "创建新配饰 · 先选择透明 PNG，再编辑图片与连接支点；应用后保存。"));
        libraryActions.Children.Add(ActionButton("复制", () =>
        {
            if (session is null || session.IsWriteProtected || !CommitPendingInputs() || SelectedItem is not { } item) return;
            Execute(() => { selectedId = session.Duplicate(item.Id).Id; Changed(discardInput: true); });
        }));
        library.Children.Add(libraryActions); Children.Add(library); Children.Add(editor);
        sections[AccessoryEditorSection.Library] = library;
        var placement = AddGroup(AccessoryEditorSection.Placement, "图像与位置");
        var connection = AddGroup(AccessoryEditorSection.Connection, "柔性与连接");
        var rotation = AddGroup(AccessoryEditorSection.Rotation, "旋转与重力");
        placement.Children.Add(new TextBlock { Text = "名称" });
        name.SetResourceReference(Control.BackgroundProperty, "HomeSoft");
        name.SetResourceReference(Control.BorderBrushProperty, "HomeLine");
        AutomationProperties.SetName(name, "配饰名称"); placement.Children.Add(name);
        name.LostKeyboardFocus += (_, _) => CommitPendingInputs();
        name.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitPendingInputs(); e.Handled = true; } };
        name.TextChanged += (_, _) => { if (!refreshing) UpdateStatus(); };
        placement.Children.Add(equipped);
        equipped.Click += (_, _) =>
        {
            var selected = equipped.IsChecked == true;
            Edit(item => item with { IsEquipped = selected });
        };
        placement.Children.Add(new TextBlock { Text = "图层" });
        layer.Items.Add("团子后"); layer.Items.Add("团子前");
        AutomationProperties.SetName(layer, "配饰前后图层"); placement.Children.Add(layer);
        layer.SelectionChanged += (_, _) =>
        {
            if (refreshing || layer.SelectedIndex < 0) return;
            var selected = layer.SelectedIndex == 0 ? AccessoryLayer.Behind : AccessoryLayer.Front;
            Edit(item => item with { Layer = selected });
        };
        var ordering = new WrapPanel();
        var moveUp = AccessoryEditorTheme.IconButton("上移", "M6,14 L12,8 L18,14", "上移 · 叠在同层配饰前面", () => MoveSelected(1));
        moveUp.Margin = new Thickness(0, 0, 8, 0);
        ordering.Children.Add(moveUp);
        ordering.Children.Add(AccessoryEditorTheme.IconButton("下移", "M6,10 L12,16 L18,10", "下移 · 放在同层配饰后面", () => MoveSelected(-1)));
        placement.Children.Add(ordering);
        placement.Children.Add(ActionButton("图片／支点", EditSelectedImage, "编辑原图尺寸、旋转与连接支点；确认只返回草稿。"));
        AddNumber(placement, "配饰尺寸 %", "尺寸 %", "size", 10, 400, value => Edit(item => item with { SizePercent = value }));
        AddNumber(placement, "基础角度 °", "角度 °", "angle", -180, 180, value => Edit(item => item with { BaseAngleDegrees = value }));
        placement.Children.Add(ActionButton("团子上选支点", () => BodyAnchorPickRequested?.Invoke()));
        AddNumber(placement, "身体附着 X", "附着 X", "bodyX", 0, 1, value => Edit(item => item with { BodyAnchorX = value }));
        AddNumber(placement, "身体附着 Y", "附着 Y", "bodyY", 0, 1, value => Edit(item => item with { BodyAnchorY = value }));
        AddNumber(connection, "柔软度", "柔软度", "softness", 0, 100, value => Edit(item => item with { Softness = value }));
        AddNumber(connection, "连接强度", "连接强度", "connection", 0, 100, value => Edit(item => item with { ConnectionStrength = value }));
        AddNumber(rotation, "旋转阻尼", "旋转阻尼", "damping", 0, 100, value => Edit(item => item with { RotationDamping = value }));
        rotation.Children.Add(lockRotation);
        lockRotation.Click += (_, _) =>
        {
            var selected = lockRotation.IsChecked == true;
            Edit(item => item with { LockRotation = selected });
        };
        AddNumber(rotation, "回正强度", "回正强度", "return", 0, 100, value => Edit(item => item with { ReturnStrength = value }));
        rotation.Children.Add(limitSwing);
        limitSwing.Click += (_, _) =>
        {
            var selected = limitSwing.IsChecked == true;
            Edit(item => item with { LimitSwing = selected });
        };
        AddNumber(rotation, "最大摆角 °", "摆角 °", "swing", 0, 180, value => Edit(item => item with { MaxSwingDegrees = value }));
        AddNumber(rotation, "重力影响", "重力影响", "gravity", 0, 100, value => Edit(item => item with { GravityInfluence = value }));
        status.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(status);
        var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        reset = AccessoryEditorTheme.IconButton("复位当前配饰", AccessoryEditorTheme.ResetIcon, "复位当前配饰 · 先预览，应用后保存；不影响其他配饰。", ResetSelected);
        reset.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(reset);
        var cancel = AccessoryEditorTheme.IconButton("取消", AccessoryEditorTheme.CancelIcon, "取消 · 放弃本次未保存修改，恢复上次已保存的整套配饰。", CancelDraft);
        cancel.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(cancel);
        apply = AccessoryEditorTheme.IconButton("应用", AccessoryEditorTheme.ApplyIcon, "应用 · 将整套配饰与导入图片保存到本机。", ApplyDraft, primary: true);
        actions.Children.Add(apply); footer.Children.Add(actions);
        SaveActions = footer; Children.Add(footer);
        UpdateSections();
        Refresh();
    }

    internal void Bind(AccessorySettingsSession value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ClearVisibleThumbnails();
        thumbnails.Clear(); thumbnailRecency.Clear();
        foreach (var card in libraryCards.Values) card.ImageUnavailable = false;
        session = value; previewError = null; selectedId = value.DraftConfiguration.Items.FirstOrDefault()?.Id;
        SelectedSection = AccessoryEditorSection.Library; UpdateSections();
        Refresh(discardInput: true);
    }

    internal void Refresh() => Refresh(discardInput: false);

    internal void DiscardInputsAndRefresh() => Refresh(discardInput: true);

    private void Refresh(bool discardInput)
    {
        var inputOwner = selectedId;
        var pendingName = !discardInput && SelectedItem is { } previous && name.Text != previous.Name ? name.Text : null;
        var pendingNumbers = discardInput ? [] : valueTexts.Where(pair => pair.Value.Text != Format(sliders[pair.Key].Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Text);
        generation++;
        refreshing = true;
        var items = session?.DraftConfiguration.Items ?? [];
        if (!list.Items.Cast<ListBoxItem>().Select(row => (string)row.Tag).SequenceEqual(items.Select(item => item.Id)))
        {
            ClearVisibleThumbnails(); list.Items.Clear(); libraryCards.Clear();
            foreach (var item in items)
            {
                var card = CreateLibraryCard(item);
                libraryCards[item.Id] = card; list.Items.Add(card.Row);
            }
        }
        foreach (var item in items)
        {
            var card = libraryCards[item.Id];
            if (card.AssetId != item.AssetId)
            {
                card.AssetId = item.AssetId; card.ImageUnavailable = false;
                card.Thumbnail.Source = null; thumbnailOwners.Remove(card.Thumbnail);
            }
            RefreshLibraryCard(card, item);
            if (item.Id == selectedId) list.SelectedItem = card.Row;
        }
        if (list.SelectedItem is null && list.Items.Count > 0)
        {
            list.SelectedIndex = 0; selectedId = (list.SelectedItem as ListBoxItem)?.Tag as string;
        }
        refreshing = false;
        RefreshFields();
        if (!discardInput && inputOwner == selectedId)
        {
            refreshing = true;
            if (pendingName is not null) name.Text = pendingName;
            foreach (var (key, text) in pendingNumbers) valueTexts[key].Text = text;
            refreshing = false;
        }
        operationError = null;
        UpdateStatus();
        QueueThumbnailUpdate();
    }

    /// <summary>Saving and visible preview are separate results; only the host can clear its drawing error.</summary>
    internal void SetPreviewError(string? error)
    {
        previewError = string.IsNullOrWhiteSpace(error) ? null : error;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var savedStatus = session?.StatusMessage ?? "导入透明 PNG，编辑后应用到本机。";
        var text = string.Join("\n", new[] { operationError, previewError, savedStatus }.Where(message => !string.IsNullOrWhiteSpace(message)));
        var buffered = HasBufferedInput;
        apply.IsEnabled = session is not null && !session.IsWriteProtected && (session.HasPendingSave || buffered);
        var notice = session?.IsWriteProtected == true ? "清单受保护 · 只读预览" : buffered ? "输入待确认 · 应用后保存" :
            session?.IsDirty == true ? "草稿即时预览 · 应用后保存" : "拖动即时预览 · 应用后保存";
        var dirty = session?.HasPendingSave == true || buffered;
        var changed = status.Text != text || previewNotice != notice || reportedDirty != dirty;
        status.Text = text; previewNotice = notice; reportedDirty = dirty;
        if (changed) PresentationChanged?.Invoke();
    }

    private void RefreshFields()
    {
        refreshing = true;
        var item = SelectedItem;
        var editable = item is not null && session?.IsWriteProtected != true;
        editor.IsEnabled = editable;
        reset.IsEnabled = editable;
        if (item is not null)
        {
            name.Text = item.Name; equipped.IsChecked = item.IsEquipped;
            layer.SelectedIndex = item.Layer == AccessoryLayer.Behind ? 0 : 1;
            lockRotation.IsChecked = item.LockRotation; limitSwing.IsChecked = item.LimitSwing;
            sliders["size"].Value = item.SizePercent; sliders["angle"].Value = item.BaseAngleDegrees;
            sliders["bodyX"].Value = item.BodyAnchorX; sliders["bodyY"].Value = item.BodyAnchorY;
            sliders["softness"].Value = item.Softness; sliders["connection"].Value = item.ConnectionStrength;
            sliders["damping"].Value = item.RotationDamping; sliders["return"].Value = item.ReturnStrength;
            sliders["swing"].Value = item.MaxSwingDegrees; sliders["gravity"].Value = item.GravityInfluence;
            limitSwing.IsEnabled = editable && !item.LockRotation;
            foreach (var (key, slider) in sliders)
            {
                // IsEnabled getters include ancestor coercion; store only this control's own rule.
                var numberEditable = editable && (key switch
                {
                    "return" or "damping" => !item.LockRotation,
                    "swing" => !item.LockRotation && item.LimitSwing,
                    _ => true,
                });
                slider.IsEnabled = numberEditable;
                valueTexts[key].Text = slider.Value.ToString("0.###", CultureInfo.CurrentCulture);
                valueTexts[key].IsEnabled = numberEditable;
            }
        }
        refreshing = false;
    }

    internal void SelectItem(string id)
    {
        if (id == selectedId) return;
        if (!CommitPendingInputs())
        {
            refreshing = true;
            list.SelectedItem = list.Items.Cast<ListBoxItem>().FirstOrDefault(row => (string)row.Tag == selectedId);
            refreshing = false;
            return;
        }
        selectedId = id; Refresh(discardInput: true);
    }

    internal bool SelectSection(AccessoryEditorSection section)
    {
        if (!Enum.IsDefined(section)) throw new ArgumentOutOfRangeException(nameof(section));
        if (section == SelectedSection) return true;
        if (!CommitPendingInputs()) return false;
        SelectedSection = section;
        UpdateSections();
        SectionChanged?.Invoke();
        return true;
    }

    private void UpdateSections()
    {
        foreach (var (section, body) in sections)
            body.Visibility = section == SelectedSection ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (section, button) in sectionButtons)
        {
            var selected = section == SelectedSection;
            button.Tag = selected ? "Selected" : null;
            button.SetResourceReference(Control.ForegroundProperty, selected ? "HomePrimaryInk" : "HomeInk");
            AutomationProperties.SetItemStatus(button, selected ? "已选" : "未选");
        }
        QueueThumbnailUpdate();
    }

    internal void SetBodyAnchor(Point normalized)
    {
        if (!double.IsFinite(normalized.X) || !double.IsFinite(normalized.Y) || normalized.X is < 0 or > 1 || normalized.Y is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(normalized));
        Edit(item => item with { BodyAnchorX = normalized.X, BodyAnchorY = normalized.Y });
    }

    internal void EditSelectedImage()
    {
        if (session is null || session.IsWriteProtected || !CommitPendingInputs() || SelectedItem is not { } item) return;
        var image = session.TryGetImage(item.AssetId) ?? BuiltinAccessoryImages.Get(item.AssetId);
        if (image is null) { SetOperationError("这件配饰的图片不可用，请恢复原图片后重试。"); return; }
        var currentSession = session; var editGeneration = generation;
        var result = ImageEditor is not null ? ImageEditor(item, image) : OpenImageEditor(item, image);
        if (result is null || editGeneration != generation || !ReferenceEquals(session, currentSession) || !IsEnabled) return;
        Edit(_ => result);
    }

    private AccessoryItem? OpenImageEditor(AccessoryItem item, BitmapSource image)
    {
        var window = new AccessoryImageEditorWindow(item, image) { Owner = Window.GetWindow(this) };
        AccessoryEditorTheme.CopyFrom(this, window);
        return window.ShowDialog() == true ? window.Result : null;
    }

    internal void Edit(Func<AccessoryItem, AccessoryItem> update, bool discardInput = false)
    {
        if (refreshing || session is null || session.IsWriteProtected) return;
        if (!discardInput && !CommitPendingInputs())
        {
            var error = operationError;
            Refresh();
            if (error is not null) SetOperationError(error);
            return;
        }
        if (SelectedItem is not { } item) return;
        Execute(() =>
        {
            var changed = update(item);
            session.Stage(new AccessoryConfiguration(session.DraftConfiguration.Items.Select(candidate => candidate.Id == item.Id ? changed : candidate)));
            Changed(discardInput);
        });
    }

    private void Changed(bool discardInput = false)
    {
        Refresh(discardInput);
        if (session is not null) PreviewChanged?.Invoke(session.DraftConfiguration);
    }

    private bool HasBufferedInput => SelectedItem is { } item &&
        (name.Text != item.Name || valueTexts.Any(pair => pair.Value.Text != Format(sliders[pair.Key].Value)));

    private static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    private bool CommitPendingInputs()
    {
        if (refreshing || SelectedItem is not { } item || session is null) return true;
        if (!HasBufferedInput) return true;
        if (session.IsWriteProtected) return false;
        var value = name.Text.Trim();
        if (value.Length == 0 || value.Length > 128 || value.Any(char.IsControl))
        { SetOperationError("配饰名称需为 1–128 个字符，不含控制字符。"); return false; }
        var numbers = new Dictionary<string, double>();
        foreach (var (key, text) in valueTexts)
        {
            if (!AccessoryEditorTheme.TryReadNumber(text, sliders[key], out var number))
            {
                SetOperationError($"{AutomationProperties.GetName(sliders[key])}需为 {Format(sliders[key].Minimum)}–{Format(sliders[key].Maximum)} 的有限数值；输入已保留，修正后再应用。");
                return false;
            }
            numbers[key] = number;
        }
        var changed = item with
        {
            Name = value,
            SizePercent = numbers["size"],
            BaseAngleDegrees = numbers["angle"],
            BodyAnchorX = numbers["bodyX"],
            BodyAnchorY = numbers["bodyY"],
            Softness = numbers["softness"],
            ConnectionStrength = numbers["connection"],
            RotationDamping = numbers["damping"],
            ReturnStrength = numbers["return"],
            MaxSwingDegrees = numbers["swing"],
            GravityInfluence = numbers["gravity"],
        };
        if (changed != item)
        {
            session.Stage(new AccessoryConfiguration(session.DraftConfiguration.Items.Select(candidate => candidate.Id == item.Id ? changed : candidate)));
            Changed(discardInput: true);
        }
        else Refresh(discardInput: true);
        return true;
    }

    private void Import()
    {
        if (session is null || session.IsWriteProtected || !CommitPendingInputs()) return;
        var importGeneration = generation;
        string? path;
        if (ImagePicker is not null) path = ImagePicker();
        else
        {
            var picker = new Microsoft.Win32.OpenFileDialog
            {
                Title = "创建新配饰 · 选择透明 PNG",
                Filter = "透明 PNG (*.png)|*.png|所有文件 (*.*)|*.*",
                DefaultExt = ".png",
                CheckFileExists = true,
            };
            path = picker.ShowDialog(Window.GetWindow(this)) == true ? picker.FileName : null;
        }
        if (path is null || generation != importGeneration || !IsEnabled) return;
        Execute(() =>
        {
            selectedId = session.ImportPng(path).Id;
            Changed(discardInput: true);
            SelectSection(AccessoryEditorSection.Placement);
            EditSelectedImage();
        });
    }

    internal void MoveSelected(int direction)
    {
        if (session is null || session.IsWriteProtected || direction is not (-1 or 1) || !CommitPendingInputs() || SelectedItem is not { } item) return;
        var ordered = session.DraftConfiguration.Items.Where(candidate => candidate.Layer == item.Layer).OrderBy(candidate => candidate.Order).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).ToArray();
        var index = Array.FindIndex(ordered, candidate => candidate.Id == item.Id); var other = index + direction;
        if (other < 0 || other >= ordered.Length) return;
        (ordered[index], ordered[other]) = (ordered[other], ordered[index]);
        var ranks = ordered.Select((candidate, rank) => (candidate.Id, rank)).ToDictionary(pair => pair.Id, pair => pair.rank);
        Execute(() =>
        {
            session.Stage(new AccessoryConfiguration(session.DraftConfiguration.Items.Select(candidate => candidate.Layer == item.Layer ? candidate with { Order = ranks[candidate.Id] } : candidate)));
            Changed();
        });
    }

    private void ResetSelected() => Edit(item => item with
    {
        ImagePivotX = .5,
        ImagePivotY = .5,
        BodyAnchorX = .5,
        BodyAnchorY = 0,
        SizePercent = 100,
        BaseAngleDegrees = 0,
        Softness = 0,
        ConnectionStrength = 100,
        RotationDamping = 60,
        ReturnStrength = 60,
        GravityInfluence = 0,
        LockRotation = true,
        LimitSwing = true,
        MaxSwingDegrees = 45,
    }, discardInput: true);

    internal void ApplyDraft()
    {
        if (session is null) return;
        if (!CommitPendingInputs()) return;
        if (session.TrySave()) { Refresh(); Applied?.Invoke(session.DraftConfiguration); }
        else Refresh();
    }

    internal void CancelDraft()
    {
        generation++;
        session?.CancelPreview(); Refresh(discardInput: true);
        if (session is not null) PreviewChanged?.Invoke(session.DraftConfiguration);
        CancelRequested?.Invoke();
    }

    private void Execute(Action operation)
    {
        try { operation(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or NotSupportedException)
        { RefreshFields(); SetOperationError($"操作未完成：{error.Message}"); }
    }

    private void SetOperationError(string message)
    {
        operationError = message; UpdateStatus();
    }

    private static Button ActionButton(string text, Action action, string? tooltip = null)
    {
        var button = new Button { Content = text, ToolTip = tooltip ?? text, Margin = new Thickness(0, 3, 5, 3), Padding = new Thickness(7, 4, 7, 4), MinWidth = 0, MinHeight = 32 };
        button.SetResourceReference(FrameworkElement.StyleProperty, typeof(Button));
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action();
        return button;
    }

    private void AddSectionTab(StackPanel tabs, AccessoryEditorSection section, string name, string path)
    {
        var button = AccessoryEditorTheme.IconButton("配饰：" + name, path, name, () => SelectSection(section));
        button.Margin = new Thickness(0, 0, 0, 8);
        sectionButtons[section] = button; tabs.Children.Add(button);
    }

    private StackPanel AddGroup(AccessoryEditorSection section, string name)
    {
        var body = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        body.Children.Add(new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        AutomationProperties.SetName(body, "配饰参数：" + name);
        sections[section] = body; editor.Children.Add(body);
        return body;
    }

    private sealed record LibraryCard(ListBoxItem Row, Image Thumbnail, TextBlock Equipped, Border Warning)
    {
        public string? AssetId { get; set; }
        public bool ImageUnavailable { get; set; }
    }

    private static void RefreshLibraryCard(LibraryCard card, AccessoryItem item)
    {
        card.Row.ToolTip = card.ImageUnavailable ? $"{item.Name} · 图片不可用" : item.Name;
        AutomationProperties.SetName(card.Row, $"{(item.IsEquipped ? "✓ " : "")}{item.Name}");
        var wearing = item.IsEquipped ? "已佩戴" : "未佩戴";
        AutomationProperties.SetItemStatus(card.Row, card.ImageUnavailable ? $"{wearing} · 图片不可用" : wearing);
        card.Equipped.Visibility = item.IsEquipped ? Visibility.Visible : Visibility.Collapsed;
        card.Warning.Visibility = card.ImageUnavailable ? Visibility.Visible : Visibility.Collapsed;
    }

    private LibraryCard CreateLibraryCard(AccessoryItem item)
    {
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            MaxWidth = ThumbnailEdge,
            MaxHeight = ThumbnailEdge,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2),
            IsHitTestVisible = false
        };
        var badge = new TextBlock
        {
            Text = "✓",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        badge.SetResourceReference(TextBlock.ForegroundProperty, "HomeInk");
        var warningText = new TextBlock
        {
            Text = "!",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        warningText.SetResourceReference(TextBlock.ForegroundProperty, "HomeInk");
        var warning = new Border
        {
            Child = warningText,
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        warning.SetResourceReference(Border.BackgroundProperty, "HomeSoft");
        warning.SetResourceReference(Border.BorderBrushProperty, "HomeInk");
        AutomationProperties.SetName(warning, "图片不可用");
        var content = new Grid { Height = 56, MinWidth = 0 };
        content.Children.Add(image); content.Children.Add(badge); content.Children.Add(warning);
        var row = new ListBoxItem
        {
            Content = content,
            Tag = item.Id,
            MinWidth = 0,
            Padding = new Thickness(2),
            Margin = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        row.SetResourceReference(FrameworkElement.StyleProperty, "AccessoryLibraryItem");
        image.Loaded += (_, _) => QueueThumbnailUpdate();
        return new(row, image, badge, warning) { AssetId = item.AssetId };
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject target)
    {
        for (var parent = VisualTreeHelper.GetParent(target); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            yield return parent;
    }

    private void OnLibraryScrollChanged(object sender, ScrollChangedEventArgs e) => QueueThumbnailUpdate();

    private void QueueThumbnailUpdate()
    {
        if (thumbnailUpdateQueued) return;
        thumbnailUpdateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            thumbnailUpdateQueued = false;
            UpdateVisibleThumbnails();
        }, DispatcherPriority.Background);
    }

    private void UpdateVisibleThumbnails()
    {
        if (session is null) return;
        if (SelectedSection != AccessoryEditorSection.Library || !IsLoaded || !library.IsVisible)
        {
            ClearVisibleThumbnails();
            return;
        }
        foreach (var item in session.DraftConfiguration.Items)
        {
            if (!libraryCards.TryGetValue(item.Id, out var card)) continue;
            if (!card.Row.IsVisible || !IsThumbnailVisible(card.Row))
            {
                card.Thumbnail.Source = null;
                thumbnailOwners.Remove(card.Thumbnail);
                continue;
            }
            if (!thumbnails.TryGetValue(item.AssetId, out var thumbnail))
            {
                var source = session.TryGetImage(item.AssetId) ?? BuiltinAccessoryImages.Get(item.AssetId);
                thumbnail = source is null ? null : CreateThumbnail(source);
                thumbnails[item.AssetId] = thumbnail;
                ThumbnailDecodeCount++;
            }
            thumbnailRecency.Remove(item.AssetId); thumbnailRecency.AddLast(item.AssetId);
            while (thumbnails.Count > MaximumThumbnailCache)
            {
                var oldest = thumbnailRecency.First!;
                thumbnails.Remove(oldest.Value); thumbnailRecency.RemoveFirst();
            }
            card.Thumbnail.Source = thumbnail;
            card.ImageUnavailable = thumbnail is null;
            RefreshLibraryCard(card, item);
            if (thumbnail is not null) thumbnailOwners.Add(card.Thumbnail);
            else thumbnailOwners.Remove(card.Thumbnail);
        }
        UpdateStatus();
    }

    private void ClearVisibleThumbnails()
    {
        foreach (var image in thumbnailOwners) image.Source = null;
        thumbnailOwners.Clear();
    }

    private static bool IsThumbnailVisible(FrameworkElement card)
    {
        // An empty Image has no intrinsic size. Its card is laid out before the bitmap exists.
        if (card.ActualWidth <= 0 || card.ActualHeight <= 0) return false;
        var bounds = new Rect(0, 0, card.ActualWidth, card.ActualHeight);
        foreach (var viewport in Ancestors(card).OfType<ScrollViewer>())
        {
            var relative = card.TransformToAncestor(viewport).TransformBounds(bounds);
            if (!relative.IntersectsWith(new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight))) return false;
        }
        return true;
    }

    private static BitmapSource CreateThumbnail(BitmapSource source)
    {
        var scale = (double)ThumbnailEdge / Math.Max(source.PixelWidth, source.PixelHeight);
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawImage(source, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing); bitmap.Freeze();
        return bitmap;
    }

    private void AddNumber(StackPanel parent, string label, string visibleLabel, string key, double min, double max, Action<double> update)
    {
        var number = AccessoryEditorTheme.Number(label, visibleLabel, min, max, Math.Clamp(0, min, max),
            value => { if (!refreshing) update(value); }, () => CommitPendingInputs());
        sliders[key] = number.Slider; valueTexts[key] = number.Value;
        number.Value.TextChanged += (_, _) => { if (!refreshing) UpdateStatus(); };
        parent.Children.Add(number.Row);
    }
}
