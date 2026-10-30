using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.Rendering.Visuals;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using IDataObject = System.Windows.IDataObject;
using ListBox = System.Windows.Controls.ListBox;
using MenuItem = System.Windows.Controls.MenuItem;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using System.Globalization;

namespace Dango.Desktop.Accessories;

internal enum AccessoryEditorSection { Library, Placement }

/// <summary>Local wardrobe draft UI; host navigation owns the outer cancellation boundary.</summary>
internal sealed class AccessoryEditorPanel : StackPanel
{
    private AccessorySettingsSession? session;
    private readonly ListBox list = new() { MinWidth = 0, Padding = new Thickness(0) };
    private readonly ListBox unequippedList = new() { MinWidth = 0, Padding = new Thickness(0) };
    private readonly ListBox placementList = new() { MinWidth = 0, Padding = new Thickness(0) };
    private readonly ScrollViewer placementViewport = new() { Height = 92, Margin = new Thickness(0, 0, 8, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly ScrollViewer libraryViewport = new() { Height = 240, Margin = new Thickness(0, 6, 0, 6), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel library = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel editor = new();
    private readonly Dictionary<AccessoryEditorSection, StackPanel> sections = [];
    private readonly Dictionary<AccessoryEditorSection, Button> sectionButtons = [];
    private readonly Dictionary<string, LibraryCard> libraryCards = [];
    private readonly Dictionary<string, LibraryCard> placementCards = [];
    private readonly Dictionary<string, BitmapSource?> thumbnails = [];
    private readonly LinkedList<string> thumbnailRecency = new();
    private readonly HashSet<Image> thumbnailOwners = [];
    private ScrollViewer? outerScroll;
    private bool thumbnailUpdateQueued;
    private int thumbnailPixelEdge = ThumbnailEdge;
    private readonly CheckBox lockRotation = new() { Content = "锁定整体转动", Margin = new Thickness(0, 8, 0, 4) };
    private readonly CheckBox limitSwing = new() { Content = "限制摆角", Margin = new Thickness(0, 8, 0, 4) };
    private readonly TextBlock rotationReason = new() { Text = "已锁定，转动参数保留当前值。", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock connectionSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock motionSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, (StackPanel Row, Slider Slider, TextBox Value)> wearNumbers = [];
    private readonly Button moveUp;
    private readonly Button moveDown;
    private readonly Button frontLayer;
    private readonly Button behindLayer;
    private readonly Button apply;
    private readonly Button createTool;
    private readonly Button duplicateTool;
    private readonly Button deleteTool;
    private bool confirmingDeletion;
    private string? dragItemId;
    private Point dragStart;
    private bool dragInProgress;
    private bool suppressDoubleClick;
    private bool refreshing;
    private long generation;
    private string? selectedId;
    private string? previewError;
    private string? operationError;
    private string? reportedSessionStatus;
    private string previewNotice = "拖动即时预览 · 应用后保存";
    private bool? reportedDirty;

    internal AccessoryItem? SelectedItem => session?.DraftConfiguration.Items.FirstOrDefault(item => item.Id == selectedId);
    internal string StatusMessage => status.Text;
    internal FrameworkElement SaveActions { get; }
    internal FrameworkElement SectionTabs { get; }
    internal FrameworkElement ActionTools { get; }
    internal AccessoryEditorSection SelectedSection { get; private set; } = AccessoryEditorSection.Library;
    internal event Action? SectionChanged;
    internal Func<AccessoryItem, int, bool>? DeleteConfirmation { get; set; }
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
    internal Func<DangoAppearanceConfiguration>? SimulationAppearanceFactory { get; set; }
    internal Func<AccessoryEditDraft, AccessoryEditorSourceAsset?, AccessoryEditorProject?, Action<AccessoryEditResult>, AccessoryEditResult?>? UnifiedEditor { get; set; }
    internal bool IsUnifiedEditorOpen { get; private set; }
    internal event Action? ModalOpenChanged;
    internal AccessoryEditResult? ModalPreviewResult { get; private set; }
    internal BitmapSource? ModalPreviewImage => ModalPreviewResult?.Image.Bitmap;
    internal event Action? ModalPreviewChanged;
    internal BitmapSource? ResolvePreviewImage(string assetId) => ModalPreviewResult is { } modal && modal.Image.AssetId == assetId
        ? modal.Image.Bitmap : session?.TryGetImage(assetId) ?? BuiltinAccessoryImages.Get(assetId);
    internal AccessoryEditorProject? ResolvePreviewProject(AccessoryItem item) => ModalPreviewResult is { } modal &&
        modal.Item.Id == item.Id && modal.Item.ProjectId == item.ProjectId && modal.Item.EditRevision == item.EditRevision &&
        modal.Project.ProjectId == item.ProjectId && modal.Project.Revision == item.EditRevision
        ? modal.Project : session?.TryGetProject(item);

    internal AccessoryEditorPanel()
    {
        AccessoryEditorTheme.Install(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/Dango.Desktop;component/Accessories/Editing/AccessoryUnifiedEditorStyles.xaml", UriKind.Relative) });
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
        AddSectionTab(tabs, AccessoryEditorSection.Placement, "配饰", "M4,4 L20,4 L20,20 L4,20 Z M4,16 L10,10 L15,15 L18,12 L20,14 M8,7 L8,8");
        ConfigureLibraryList(list, "已佩戴配饰"); ConfigureLibraryList(unequippedList, "未佩戴配饰");
        var libraryGroups = new StackPanel();
        libraryGroups.Children.Add(new TextBlock { Text = "已佩戴", Margin = new Thickness(4, 4, 4, 4) });
        libraryGroups.Children.Add(list);
        libraryGroups.Children.Add(new TextBlock { Text = "未佩戴", Margin = new Thickness(4, 8, 4, 4) });
        libraryGroups.Children.Add(unequippedList);
        libraryViewport.Content = libraryGroups;
        libraryViewport.SetResourceReference(Control.BackgroundProperty, "HomeSoft");
        AutomationProperties.SetName(libraryViewport, "配饰库，双击或右键佩戴与取消佩戴");
        libraryViewport.ScrollChanged += OnLibraryScrollChanged;
        library.Children.Add(libraryViewport); Children.Add(library); Children.Add(editor);
        var tools = new StackPanel();
        AccessoryEditorTheme.Install(tools); tools.Loaded += (_, _) => AccessoryEditorTheme.AdoptParent(tools);
        ActionTools = tools;
        createTool = AccessoryEditorTheme.IconButton("创建新配饰", "M12,4 L12,20 M4,12 L20,12", "创建新配饰 · 打开空白画布，应用后保存", CreateBlank);
        duplicateTool = AccessoryEditorTheme.IconButton("复制配饰", "M8,3 L20,3 L20,16 L8,16 Z M4,8 L4,21 L16,21", "复制配饰 · 复制当前配饰到草稿", DuplicateSelected);
        deleteTool = AccessoryEditorTheme.IconButton("删除配饰", "M5,6 L19,6 M9,3 L15,3 M7,6 L8,21 L16,21 L17,6 M10,10 L10,17 M14,10 L14,17", "删除配饰 · 两次确认后删除当前条目", DeleteSelected);
        foreach (var tool in new[] { createTool, duplicateTool, deleteTool }) { tool.Margin = new Thickness(0, 0, 0, 8); tools.Children.Add(tool); }
        deleteTool.PreviewMouseLeftButtonDown += (_, _) => confirmingDeletion = true;
        deleteTool.LostMouseCapture += (_, _) => { if (!deleteTool.IsPressed) confirmingDeletion = false; };
        sections[AccessoryEditorSection.Library] = library;
        var placement = AddGroup(AccessoryEditorSection.Placement, "佩戴");
        var selectionTools = new Grid();
        selectionTools.ColumnDefinitions.Add(new ColumnDefinition());
        selectionTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var selectedActions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var editSelected = ActionButton("编辑", EditSelectedImage, "编辑名称、图像、图层组与骨架；确认只返回草稿。");
        AutomationProperties.SetName(editSelected, "编辑配饰");
        selectedActions.Children.Add(editSelected);
        var pickAnchor = ActionButton("选支点", () => BodyAnchorPickRequested?.Invoke(), "在真实团子预览选择挂点 A，不改变图片基准 P。",
            path: "M12,3 L12,7 M12,17 L12,21 M3,12 L7,12 M17,12 L21,12 M17,12 A5,5 0 1 1 7,12 A5,5 0 1 1 17,12");
        AutomationProperties.SetName(pickAnchor, "团子上选支点");
        selectedActions.Children.Add(pickAnchor);
        ConfigureLibraryList(placementList, "当前已选配饰", 1);
        placementList.Background = System.Windows.Media.Brushes.Transparent;
        placementViewport.Content = placementList;
        placementViewport.Background = System.Windows.Media.Brushes.Transparent;
        AutomationProperties.SetName(placementViewport, "当前已选配饰名称和缩略图");
        placementViewport.ScrollChanged += OnLibraryScrollChanged;
        Grid.SetColumn(placementViewport, 0); selectionTools.Children.Add(placementViewport);
        Grid.SetColumn(selectedActions, 1); selectionTools.Children.Add(selectedActions);
        placement.Children.Add(selectionTools);
        AddWearNumber(placement, "size", "尺寸 %", 10, 400, 100);
        AddWearNumber(placement, "angle", "基础角度 °", -180, 180, 0);
        AddWearNumber(placement, "gravity", "整体重力响应", 0, 100, 0);
        placement.Children.Add(new TextBlock { Text = "叠放", Margin = new Thickness(0, 10, 0, 2) });
        var ordering = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(0, 3, 0, 8) };
        frontLayer = ActionButton("团子前", () => Edit(item => item with { Layer = AccessoryLayer.Front }), "团子前 · 显示在团子前面",
            "M3,3 L14,3 L14,14 L3,14 Z M17,10 L21,10 L21,21 L10,21 L10,17");
        behindLayer = ActionButton("团子后", () => Edit(item => item with { Layer = AccessoryLayer.Behind }), "团子后 · 显示在团子后面",
            "M10,10 L21,10 L21,21 L10,21 Z M14,7 L14,3 L3,3 L3,14 L7,14");
        moveUp = ActionButton("上移", () => MoveSelected(1), "上移 · 叠在同层配饰前面", "M6,14 L12,8 L18,14");
        moveDown = ActionButton("下移", () => MoveSelected(-1), "下移 · 放在同层配饰后面", "M6,10 L12,16 L18,10");
        foreach (var button in new[] { frontLayer, behindLayer, moveUp, moveDown })
        {
            button.Margin = new Thickness(0, 0, 4, 0); button.Padding = new Thickness(3, 8, 3, 8);
            if (button.Content is StackPanel contents && contents.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
                label.Margin = new Thickness(3, 0, 0, 0);
            ordering.Children.Add(button);
        }
        ordering.SizeChanged += (_, _) =>
        {
            foreach (var button in ordering.Children.OfType<Button>())
                if (button.Content is StackPanel content && content.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
                    label.Visibility = ordering.ActualWidth >= 312 ? Visibility.Visible : Visibility.Collapsed;
        };
        placement.Children.Add(ordering);
        var connection = Foldout(placement, "连接", connectionSummary);
        AddWearNumber(connection, "connection", "连接强度", 0, 100, 100);
        AddWearNumber(connection, "travel", "最大行程 %", 0, 200, 75);
        var motion = Foldout(placement, "整体摆动", motionSummary);
        AutomationProperties.SetName(lockRotation, "锁定整体转动");
        AutomationProperties.SetName(limitSwing, "限制摆角");
        lockRotation.Click += (_, _) => { var value = lockRotation.IsChecked == true; if (!refreshing) Edit(item => item with { LockRotation = value }); };
        limitSwing.Click += (_, _) => { var value = limitSwing.IsChecked == true; if (!refreshing) Edit(item => item with { LimitSwing = value }); };
        motion.Children.Add(lockRotation); motion.Children.Add(rotationReason);
        rotationReason.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
        AddWearNumber(motion, "rotationDamping", "旋转阻尼", 0, 100, 60);
        AddWearNumber(motion, "return", "整体回正", 0, 100, 60);
        motion.Children.Add(limitSwing);
        AddWearNumber(motion, "swing", "最大摆角 °", 0, 180, 45);
        status.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(status);
        var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
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
        foreach (var card in libraryCards.Values.Concat(placementCards.Values)) card.ImageUnavailable = false;
        ModalPreviewResult = null; ModalPreviewChanged?.Invoke();
        session = value; previewError = null; selectedId = value.DraftConfiguration.Items.FirstOrDefault()?.Id;
        SelectedSection = AccessoryEditorSection.Library; UpdateSections();
        Refresh(discardInput: true);
    }

    internal void Refresh() => Refresh(discardInput: false);

    internal void DiscardInputsAndRefresh() => Refresh(discardInput: true);

    private void Refresh(bool discardInput)
    {
        var inputOwner = selectedId;
        var pendingNumbers = discardInput ? new Dictionary<string, string>() : wearNumbers
            .Where(p => p.Value.Value.Text != Format(p.Value.Slider.Value)).ToDictionary(p => p.Key, p => p.Value.Value.Text);
        generation++;
        refreshing = true;
        var items = session?.DraftConfiguration.Items ?? [];
        var equippedItems = items.Where(item => item.IsEquipped).OrderBy(item => item.Layer).ThenBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var unequippedItems = items.Where(item => !item.IsEquipped).ToArray();
        if (!list.Items.Cast<ListBoxItem>().Select(row => (string)row.Tag).SequenceEqual(equippedItems.Select(item => item.Id)) ||
            !unequippedList.Items.Cast<ListBoxItem>().Select(row => (string)row.Tag).SequenceEqual(unequippedItems.Select(item => item.Id)))
        {
            list.Items.Clear(); unequippedList.Items.Clear();
            var currentIds = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in libraryCards.Keys.Where(id => !currentIds.Contains(id)).ToArray())
            {
                var removed = libraryCards[id]; removed.Thumbnail.Source = null;
                thumbnailOwners.Remove(removed.Thumbnail); libraryCards.Remove(id);
            }
            foreach (var item in items)
            {
                if (!libraryCards.ContainsKey(item.Id)) libraryCards[item.Id] = CreateLibraryCard(item);
            }
            foreach (var item in equippedItems) list.Items.Add(libraryCards[item.Id].Row);
            foreach (var item in unequippedItems) unequippedList.Items.Add(libraryCards[item.Id].Row);
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
        }
        if (!items.Any(item => item.Id == selectedId)) selectedId = equippedItems.Concat(unequippedItems).FirstOrDefault()?.Id;
        var selected = items.FirstOrDefault(item => item.Id == selectedId);
        var visiblePlacementIds = selected is null ? Array.Empty<string>() : [selected.Id];
        if (!placementList.Items.Cast<ListBoxItem>().Select(row => (string)row.Tag).SequenceEqual(visiblePlacementIds))
        {
            placementList.Items.Clear();
            foreach (var id in placementCards.Keys.Where(id => id != selected?.Id).ToArray())
            {
                var removed = placementCards[id]; removed.Thumbnail.Source = null;
                thumbnailOwners.Remove(removed.Thumbnail); placementCards.Remove(id);
            }
            if (selected is not null)
            {
                if (!placementCards.ContainsKey(selected.Id)) placementCards[selected.Id] = CreateLibraryCard(selected, showName: true);
                placementList.Items.Add(placementCards[selected.Id].Row);
            }
        }
        if (selected is not null)
        {
            var card = placementCards[selected.Id];
            if (card.AssetId != selected.AssetId)
            {
                card.AssetId = selected.AssetId; card.ImageUnavailable = false;
                card.Thumbnail.Source = null; thumbnailOwners.Remove(card.Thumbnail);
            }
            RefreshLibraryCard(card, selected);
        }
        SyncLibrarySelection();
        refreshing = false;
        RefreshFields();
        if (!discardInput && inputOwner == selectedId)
        {
            refreshing = true;
            foreach (var (key, text) in pendingNumbers) wearNumbers[key].Value.Text = text;
            refreshing = false;
        }
        operationError = null;
        UpdateStatus();
        QueueThumbnailUpdate();
    }

    /// <summary>Saving and visible preview are separate results; only the host can clear its drawing error.</summary>
    internal void SetPreviewError(string? error)
    {
        var next = string.IsNullOrWhiteSpace(error) ? null : error;
        if (previewError == next && reportedSessionStatus == session?.StatusMessage) return;
        previewError = next;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var savedStatus = session?.StatusMessage ?? "新建配饰，编辑后应用到本机。";
        reportedSessionStatus = savedStatus;
        var text = string.Join("\n", new[] { operationError, previewError, SelectedItem?.IsPendingCompletion == true ? "待完成 · 继续编辑并导出有效成品后应用。" : null, savedStatus }.Where(message => !string.IsNullOrWhiteSpace(message)));
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
        createTool.IsEnabled = session is not null && !session.IsWriteProtected;
        duplicateTool.IsEnabled = deleteTool.IsEnabled = editable;
        if (item is not null)
        {
            foreach (var (key, control) in wearNumbers)
            { var value = WearValue(item, key); control.Slider.Value = value; control.Value.Text = Format(value); }
            lockRotation.IsChecked = item.LockRotation; limitSwing.IsChecked = item.LimitSwing;
            wearNumbers["rotationDamping"].Row.IsEnabled = !item.LockRotation;
            wearNumbers["return"].Row.IsEnabled = !item.LockRotation;
            limitSwing.IsEnabled = !item.LockRotation;
            wearNumbers["swing"].Row.IsEnabled = !item.LockRotation && item.LimitSwing;
            rotationReason.Visibility = item.LockRotation ? Visibility.Visible : Visibility.Collapsed;
            connectionSummary.Text = $"连接 · 强度 {Format(item.ConnectionStrength)} · 行程 {Format(item.MaxConnectionTravelPercent)}%";
            motionSummary.Text = item.LockRotation ? "整体摆动 · 已锁定" :
                $"整体摆动 · 回正 {Format(item.ReturnStrength)} · {(item.LimitSwing ? "限幅 " + Format(item.MaxSwingDegrees) + "°" : "不限摆角")}";
            var ordered = OrderedLayer(item);
            var index = Array.FindIndex(ordered, candidate => candidate.Id == item.Id);
            moveUp.IsEnabled = index >= 0 && index < ordered.Length - 1;
            moveDown.IsEnabled = index > 0;
            SetLayerSelected(frontLayer, item.Layer == AccessoryLayer.Front);
            SetLayerSelected(behindLayer, item.Layer == AccessoryLayer.Behind);
        }
        else { moveUp.IsEnabled = moveDown.IsEnabled = false; }
        refreshing = false;
    }

    internal void SelectItem(string id)
    {
        if (id == selectedId) return;
        if (!CommitPendingInputs())
        {
            refreshing = true;
            SyncLibrarySelection();
            refreshing = false;
            return;
        }
        selectedId = id; Refresh(discardInput: true);
    }

    private void SyncLibrarySelection()
    {
        foreach (var group in new[] { list, unequippedList, placementList })
            group.SelectedItem = group.Items.Cast<ListBoxItem>().FirstOrDefault(row => (string)row.Tag == selectedId);
    }

    internal void ToggleWear(string id)
    {
        if (session is null || session.IsWriteProtected || !CommitPendingInputs()) return;
        var item = session.DraftConfiguration.Items.FirstOrDefault(candidate => candidate.Id == id);
        if (item is null) return;
        if (!item.IsEquipped && session.DraftConfiguration.Items.Count(candidate => candidate.IsEquipped) >= AccessoryConfiguration.MaximumEquippedItems)
        { SetOperationError("最多同时佩戴 8 件配饰，请先取消佩戴一件。"); return; }
        Execute(() =>
        {
            session.Stage(new(session.DraftConfiguration.Items.Select(candidate => candidate.Id == id ? candidate with { IsEquipped = !candidate.IsEquipped } : candidate)));
            Changed();
        });
    }

    private sealed record AccessoryDragPayload(AccessoryEditorPanel Owner, AccessorySettingsSession? Session, string Id);

    internal DataObject CreateDragData(string id) => new(typeof(AccessoryDragPayload), new AccessoryDragPayload(this, session, id));

    private bool CanReorder(string fromId, string toId)
    {
        if (session is null || session.IsWriteProtected || fromId == toId) return false;
        var from = session.DraftConfiguration.Items.FirstOrDefault(item => item.Id == fromId);
        var to = session.DraftConfiguration.Items.FirstOrDefault(item => item.Id == toId);
        return from is not null && to is not null && from.IsEquipped == to.IsEquipped && (!from.IsEquipped || from.Layer == to.Layer);
    }

    internal bool ReorderItem(string fromId, string toId)
    {
        if (!CanReorder(fromId, toId) || !CommitPendingInputs() || session is null) return false;
        var source = session.DraftConfiguration.Items.Single(item => item.Id == fromId);
        var ordered = (source.IsEquipped
            ? session.DraftConfiguration.Items.Where(item => item.IsEquipped && item.Layer == source.Layer).OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal)
            : session.DraftConfiguration.Items.Where(item => !item.IsEquipped)).ToList();
        var targetIndex = ordered.FindIndex(item => item.Id == toId);
        ordered.RemoveAll(item => item.Id == fromId); ordered.Insert(targetIndex, source);
        var ranks = ordered.Select((item, rank) => (item.Id, rank)).ToDictionary(pair => pair.Id, pair => pair.rank);
        var unequipped = new Queue<AccessoryItem>(ordered);
        var reordered = false;
        Execute(() =>
        {
            session.Stage(new(session.DraftConfiguration.Items.Select(item => source.IsEquipped
                ? ranks.TryGetValue(item.Id, out var rank) ? item with { Order = rank } : item
                : item.IsEquipped ? item : unequipped.Dequeue())));
            Changed();
            reordered = true;
        });
        return reordered;
    }

    private AccessoryDragPayload? ReadDragData(IDataObject data) => data.GetData(typeof(AccessoryDragPayload)) is AccessoryDragPayload payload &&
        ReferenceEquals(payload.Owner, this) && ReferenceEquals(payload.Session, session) ? payload : null;

    private void DuplicateSelected()
    {
        if (session is null || session.IsWriteProtected || !CommitPendingInputs() || SelectedItem is not { } item) return;
        Execute(() => { selectedId = session.Duplicate(item.Id).Id; Changed(discardInput: true); });
    }

    internal void DeleteSelected()
    {
        try
        {
            if (session is null || session.IsWriteProtected || SelectedItem is not { } item) return;
            confirmingDeletion = true;
            var deleteSession = session; var deleteGeneration = generation;
            for (var step = 1; step <= 2; step++)
            {
                var confirmed = DeleteConfirmation?.Invoke(item, step) ?? ConfirmDelete(item, step);
                if (!confirmed || !ReferenceEquals(deleteSession, session) || generation != deleteGeneration || !IsEnabled) return;
            }
            if (!session.TryDeleteItem(item.Id)) { Refresh(); return; }
            selectedId = session.DraftConfiguration.Items.FirstOrDefault()?.Id;
            Changed(discardInput: true);
        }
        finally { confirmingDeletion = false; }
    }

    private bool ConfirmDelete(AccessoryItem item, int step)
    {
        var message = step == 1 ? $"删除配饰“{item.Name}”？" : $"再次确认永久删除“{item.Name}”。删除立即保存，关闭编辑器或取消草稿不会恢复此配饰；其他未应用修改仍保留为草稿。";
        return System.Windows.MessageBox.Show(Window.GetWindow(this), message, step == 1 ? "删除配饰" : "确认删除配饰",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
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
        if (IsUnifiedEditorOpen || session is null || session.IsWriteProtected || !CommitPendingInputs() || SelectedItem is not { } item) return;
        Execute(() =>
        {
            var currentSession = session; var editGeneration = generation;
            var project = currentSession.TryGetProject(item.Id);
            // A missing or quarantined saved project must never be flattened into a new one.
            if (item.ProjectId is not null && project is null)
            { SetOperationError(currentSession.StatusMessage); return; }
            var source = project is null ? currentSession.GetPendingSource(item.AssetId) : null;
            if (project is null && source is null)
            {
                var bitmap = currentSession.TryGetImage(item.AssetId) ?? BuiltinAccessoryImages.Get(item.AssetId);
                if (bitmap is null) { SetOperationError("这件配饰的图片不可用，请恢复原图片后重试。"); return; }
                source = AccessoryEditorSourceAsset.FromBitmap(bitmap);
            }
            var draft = currentSession.BeginEdit(item.Id);
            var editing = true;
            bool IsCurrent() => editing && generation == editGeneration && ReferenceEquals(session, currentSession) && IsEnabled;
            void Preview(AccessoryEditResult result)
            {
                if (!IsCurrent() || result.Item.Id != draft.ItemId || result.Item.EditRevision != draft.Revision) return;
                ModalPreviewResult = result;
                ModalPreviewChanged?.Invoke();
                // Trial wear is view-only, including an item which has not been equipped yet.
                var remainingSlots = AccessoryConfiguration.MaximumEquippedItems - 1;
                PreviewChanged?.Invoke(new(currentSession.DraftConfiguration.Items.Select(candidate =>
                    candidate.Id == item.Id ? result.Item with { IsEquipped = true } :
                    candidate.IsEquipped && remainingSlots-- <= 0 ? candidate with { IsEquipped = false } : candidate)));
            }
            try
            {
                IsUnifiedEditorOpen = true;
                ModalOpenChanged?.Invoke();
                AccessoryEditResult? result;
                if (UnifiedEditor is not null) result = UnifiedEditor(draft, source, project, Preview);
                else
                {
                    var window = new AccessoryUnifiedEditorWindow(draft, source, project,
                        appearance: SimulationAppearanceFactory?.Invoke()) { Owner = Window.GetWindow(this) };
                    AccessoryEditorTheme.CopyFrom(this, window);
                    result = window.ShowDialog() == true ? window.Result : null;
                }
                if (result is not null && IsCurrent())
                {
                    if (currentSession.TryCompleteEdit(draft, result)) Changed(discardInput: true);
                    else SetOperationError("编辑结果已过期，当前草稿保留；请重新打开编辑器。");
                }
            }
            finally
            {
                editing = false;
                try
                {
                    currentSession.CancelEdit(draft);
                    ModalPreviewResult = null; ModalPreviewChanged?.Invoke();
                    if (session is not null) PreviewChanged?.Invoke(session.DraftConfiguration);
                }
                finally
                {
                    IsUnifiedEditorOpen = false;
                    ModalOpenChanged?.Invoke();
                }
            }
        });
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

    private bool HasBufferedInput => SelectedItem is not null &&
        wearNumbers.Values.Any(control => control.Value.Text != Format(control.Slider.Value));

    private bool CommitPendingInputs()
    {
        if (refreshing || SelectedItem is not { } item || session is null) return true;
        if (!HasBufferedInput) return true;
        if (session.IsWriteProtected) return false;
        var changed = item;
        foreach (var (key, control) in wearNumbers)
        {
            if (!AccessoryEditorTheme.TryReadNumber(control.Value, control.Slider, out var number))
            { SetOperationError($"{AutomationProperties.GetName(control.Slider)}需要 {Format(control.Slider.Minimum)}–{Format(control.Slider.Maximum)} 的有限数值。"); return false; }
            changed = WithWearValue(changed, key, number);
        }
        if (changed != item)
        {
            session.Stage(new AccessoryConfiguration(session.DraftConfiguration.Items.Select(candidate => candidate.Id == item.Id ? changed : candidate)));
            Changed(discardInput: true);
        }
        else Refresh(discardInput: true);
        return true;
    }

    private void CreateBlank()
    {
        if (session is null || session.IsWriteProtected || !CommitPendingInputs()) return;
        Execute(() =>
        {
            selectedId = session.CreateBlank().Id;
            Changed(discardInput: true);
            SelectSection(AccessoryEditorSection.Placement);
            EditSelectedImage();
        });
    }

    internal void MoveSelected(int direction)
    {
        if (session is null || session.IsWriteProtected || direction is not (-1 or 1) || !CommitPendingInputs() || SelectedItem is not { } item) return;
        var ordered = OrderedLayer(item);
        var index = Array.FindIndex(ordered, candidate => candidate.Id == item.Id); var other = index + direction;
        if (other < 0 || other >= ordered.Length) return;
        ReorderItem(item.Id, ordered[other].Id);
    }

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
        ModalPreviewResult = null; ModalPreviewChanged?.Invoke();
        session?.CancelPreview(); Refresh(discardInput: true);
        if (session is not null) PreviewChanged?.Invoke(session.DraftConfiguration);
        CancelRequested?.Invoke();
    }

    private void Execute(Action operation)
    {
        try { operation(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        { RefreshFields(); SetOperationError($"操作未完成：{error.Message}"); }
    }

    private void SetOperationError(string message)
    {
        operationError = message; UpdateStatus();
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    private AccessoryItem[] OrderedLayer(AccessoryItem item) => session?.DraftConfiguration.Items
        .Where(candidate => candidate.IsEquipped == item.IsEquipped && candidate.Layer == item.Layer)
        .OrderBy(candidate => candidate.Order).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).ToArray() ?? [];

    private void AddWearNumber(StackPanel parent, string key, string label, double minimum, double maximum, double initial)
    {
        var control = AccessoryEditorTheme.Number(label, label, minimum, maximum, initial,
            value => { if (!refreshing) Edit(item => WithWearValue(item, key, value)); }, () => { if (!confirmingDeletion) CommitPendingInputs(); });
        control.Value.TextChanged += (_, _) => { if (!refreshing) UpdateStatus(); };
        wearNumbers.Add(key, control); parent.Children.Add(control.Row);
    }

    private static StackPanel Foldout(StackPanel parent, string name, TextBlock summary)
    {
        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var expander = new Expander { Header = summary, Content = body, IsExpanded = false, Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(expander, name); parent.Children.Add(expander); return body;
    }

    private static double WearValue(AccessoryItem item, string key) => key switch
    {
        "size" => item.SizePercent, "angle" => item.BaseAngleDegrees, "gravity" => item.GravityInfluence,
        "connection" => item.ConnectionStrength, "travel" => item.MaxConnectionTravelPercent,
        "rotationDamping" => item.RotationDamping, "return" => item.ReturnStrength, "swing" => item.MaxSwingDegrees,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private static AccessoryItem WithWearValue(AccessoryItem item, string key, double value) => key switch
    {
        "size" => item with { SizePercent = value }, "angle" => item with { BaseAngleDegrees = value },
        "gravity" => item with { GravityInfluence = value }, "connection" => item with { ConnectionStrength = value },
        "travel" => item with { MaxConnectionTravelPercent = value }, "rotationDamping" => item with { RotationDamping = value },
        "return" => item with { ReturnStrength = value }, "swing" => item with { MaxSwingDegrees = value },
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private static Button ActionButton(string text, Action action, string? tooltip = null,
        string path = "M4,17 L4,21 L8,21 L20,9 L16,5 Z M14,7 L18,11")
    {
        var button = AccessoryEditorTheme.IconButton(text, path, tooltip ?? text, action);
        var icon = (FrameworkElement)button.Content;
        button.Content = null;
        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        button.Content = content;
        button.Margin = new Thickness(0, 3, 5, 3); button.Padding = new Thickness(14, 8, 14, 8); button.MinHeight = 40;
        button.SetResourceReference(FrameworkElement.StyleProperty, typeof(Button));
        return button;
    }

    private void AddSectionTab(StackPanel tabs, AccessoryEditorSection section, string name, string path)
    {
        var button = AccessoryEditorTheme.IconButton("配饰：" + name, path, name, () => SelectSection(section));
        button.Margin = new Thickness(0, 0, 0, 8);
        sectionButtons[section] = button; tabs.Children.Add(button);
    }

    private static void SetLayerSelected(Button button, bool selected)
    {
        button.Tag = selected ? "Selected" : null;
        button.SetResourceReference(Control.ForegroundProperty, selected ? "HomePrimaryInk" : "HomeInk");
        button.SetResourceReference(Control.BackgroundProperty, selected ? "HomeSelected" : "HomeBackground");
        button.SetResourceReference(Control.BorderBrushProperty, selected ? "HomeAccent" : "HomeLine");
        Dango.Desktop.Resources.DesktopTheme.SetPreserveSelectedHover(button, true);
        AutomationProperties.SetItemStatus(button, selected ? "已选" : "未选");
    }

    private StackPanel AddGroup(AccessoryEditorSection section, string name)
    {
        var body = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        AutomationProperties.SetName(body, "配饰参数：" + name);
        sections[section] = body; editor.Children.Add(body);
        return body;
    }

    private void ConfigureLibraryList(ListBox group, string automationName, int columns = 4)
    {
        AutomationProperties.SetName(group, automationName);
        group.SetResourceReference(Control.BackgroundProperty, "HomeSoft");
        group.BorderThickness = new Thickness(0);
        ScrollViewer.SetHorizontalScrollBarVisibility(group, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(group, ScrollBarVisibility.Disabled);
        ScrollViewer.SetCanContentScroll(group, false);
        var cells = new FrameworkElementFactory(typeof(UniformGrid)); cells.SetValue(UniformGrid.ColumnsProperty, columns);
        cells.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        group.ItemsPanel = new ItemsPanelTemplate(cells);
        group.SizeChanged += (_, _) => QueueThumbnailUpdate();
        group.IsVisibleChanged += (_, _) => QueueThumbnailUpdate();
        group.SelectionChanged += (_, _) =>
        {
            if (!refreshing && (group.SelectedItem as ListBoxItem)?.Tag is string nextId) SelectItem(nextId);
        };
    }

    private sealed record LibraryCard(ListBoxItem Row, Image Thumbnail, TextBlock Equipped, Border Warning, TextBlock? Name)
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
        if (card.Name is not null) card.Name.Text = item.Name;
    }

    private LibraryCard CreateLibraryCard(AccessoryItem item, bool showName = false)
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
        TextBlock? label = null;
        FrameworkElement rowContent = content;
        if (showName)
        {
            label = new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(2, 1, 2, 2), IsHitTestVisible = false };
            var namedContent = new StackPanel { MinWidth = 0 };
            namedContent.Children.Add(label); namedContent.Children.Add(content); rowContent = namedContent;
        }
        var row = new ListBoxItem
        {
            Content = rowContent,
            Tag = item.Id,
            MinWidth = 0,
            Padding = new Thickness(2),
            Margin = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        row.SetResourceReference(FrameworkElement.StyleProperty, showName ? "AccessorySelectedPreviewItem" : "AccessoryLibraryItem");
        row.AllowDrop = true;
        row.PreviewMouseLeftButtonDown += (_, args) =>
        {
            args.Handled = true;
            if (args.ClickCount == 2)
            {
                dragItemId = null; return;
            }
            suppressDoubleClick = false; dragItemId = (string)row.Tag; dragStart = args.GetPosition(this);
            Mouse.Capture(row);
        };
        row.PreviewMouseMove += (_, args) =>
        {
            if (dragItemId != (string)row.Tag || args.LeftButton != MouseButtonState.Pressed || dragInProgress) return;
            var delta = args.GetPosition(this) - dragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (session?.IsWriteProtected != false || !CommitPendingInputs()) return;
            args.Handled = true; dragInProgress = true; suppressDoubleClick = true;
            Mouse.Capture(null);
            try { DragDrop.DoDragDrop(row, CreateDragData((string)row.Tag), DragDropEffects.Move); }
            finally { dragInProgress = false; dragItemId = null; }
        };
        row.PreviewMouseLeftButtonUp += (_, args) =>
        {
            args.Handled = true;
            if (dragItemId == (string)row.Tag && !dragInProgress && !suppressDoubleClick) SelectItem((string)row.Tag);
            dragItemId = null; if (ReferenceEquals(Mouse.Captured, row)) Mouse.Capture(null);
        };
        row.PreviewMouseDoubleClick += (_, args) =>
        {
            if (args.ChangedButton == MouseButton.Left)
            {
                if (!dragInProgress && !suppressDoubleClick) ToggleWear((string)row.Tag);
                suppressDoubleClick = false; args.Handled = true;
            }
        };
        row.PreviewDragOver += (_, args) =>
        {
            var payload = ReadDragData(args.Data);
            args.Effects = payload is not null && CanReorder(payload.Id, (string)row.Tag) ? DragDropEffects.Move : DragDropEffects.None;
            args.Handled = true;
        };
        row.PreviewDrop += (_, args) =>
        {
            var payload = ReadDragData(args.Data);
            args.Effects = payload is not null && ReorderItem(payload.Id, (string)row.Tag) ? DragDropEffects.Move : DragDropEffects.None;
            suppressDoubleClick = true; args.Handled = true;
        };
        var menu = new ContextMenu(); var toggle = new MenuItem(); menu.Items.Add(toggle); row.ContextMenu = menu;
        menu.Opened += (_, _) =>
        {
            var current = session?.DraftConfiguration.Items.FirstOrDefault(candidate => candidate.Id == (string)row.Tag);
            toggle.Header = current?.IsEquipped == true ? "取消佩戴" : "佩戴";
            toggle.IsEnabled = current is not null && session?.IsWriteProtected != true;
            AutomationProperties.SetName(toggle, toggle.Header.ToString()!);
        };
        toggle.Click += (_, _) => ToggleWear((string)row.Tag);
        row.PreviewMouseRightButtonDown += (_, args) => args.Handled = true;
        row.PreviewMouseRightButtonUp += (_, args) => { menu.PlacementTarget = row; menu.IsOpen = true; args.Handled = true; };
        image.Loaded += (_, _) => QueueThumbnailUpdate();
        return new(row, image, badge, warning, label) { AssetId = item.AssetId };
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject target)
    {
        for (var parent = VisualTreeHelper.GetParent(target); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            yield return parent;
    }

    private void OnLibraryScrollChanged(object sender, ScrollChangedEventArgs e) => QueueThumbnailUpdate();

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        QueueThumbnailUpdate();
    }

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
        if (!IsLoaded || !IsVisible)
        {
            ClearVisibleThumbnails();
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        var pixelEdge = (int)Math.Ceiling(ThumbnailEdge * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY));
        if (thumbnailPixelEdge != pixelEdge)
        {
            thumbnailPixelEdge = pixelEdge;
            ClearVisibleThumbnails(); thumbnails.Clear(); thumbnailRecency.Clear();
        }
        var activeCards = SelectedSection == AccessoryEditorSection.Library ? libraryCards : placementCards;
        foreach (var card in (SelectedSection == AccessoryEditorSection.Library ? placementCards : libraryCards).Values)
        { card.Thumbnail.Source = null; thumbnailOwners.Remove(card.Thumbnail); }
        foreach (var item in session.DraftConfiguration.Items)
        {
            if (!activeCards.TryGetValue(item.Id, out var card)) continue;
            if (!card.Row.IsVisible || !IsThumbnailVisible(card.Row))
            {
                card.Thumbnail.Source = null;
                thumbnailOwners.Remove(card.Thumbnail);
                continue;
            }
            if (!thumbnails.TryGetValue(item.AssetId, out var thumbnail))
            {
                var source = session.GetPendingSource(item.AssetId)?.Bitmap ?? session.TryGetImage(item.AssetId) ?? BuiltinAccessoryImages.Get(item.AssetId);
                thumbnail = source is null ? null : CreateThumbnail(source, thumbnailPixelEdge);
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

    private static BitmapSource CreateThumbnail(BitmapSource source, int pixelEdge)
    {
        var scale = (double)pixelEdge / Math.Max(source.PixelWidth, source.PixelHeight);
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawImage(source, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing); bitmap.Freeze();
        return bitmap;
    }

}
