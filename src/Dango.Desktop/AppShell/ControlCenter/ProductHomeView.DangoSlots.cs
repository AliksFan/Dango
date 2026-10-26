using System.Globalization;
using Dango.Desktop.Accessories;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Physics.Belt;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Button = System.Windows.Controls.Button;

namespace Dango.Desktop.AppShell.ControlCenter;

internal sealed record DangoSlotView(int Number, Guid? Id, string? Name, bool Selected);

public partial class ProductHomeView
{
    private IReadOnlyList<DangoSlotView>? dangoSlots;
    private Guid? selectedDangoId;
    private bool slotsBlocked, slotPreviewPending, endingCreation;
    private int? creationSlot;
    private AppearanceSettingsSession? originalAppearance;
    private AccessorySettingsSession? originalAccessories;
    private long slotPreviewGeneration;
    internal event Action<int>? DangoSlotRequested;
    internal event Action<int, string, DangoAppearanceConfiguration>? DangoCreateRequested;
    internal event Action? DangoCreationCancelled;
    internal event Action<Guid, string>? DangoRenameRequested;
    internal event Action<Guid>? DangoDeleteRequested;
    internal event Action<bool>? OrdinaryRoomNameSourceRequested;
    internal Func<string, string, bool> DangoDeleteConfirmation { get; set; } = (title, text) =>
        System.Windows.MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    internal Func<string, string?>? DangoRenamePrompt { get; set; }
    internal bool IsCreatingDango => creationSlot.HasValue;
    internal bool IsDangoCreating => IsCreatingDango;
    private DateTime slotsTransitionUntil;
    private bool slotDialogOpen;
    private bool IsDangoInputBlocked => slotDialogOpen || DangoNameButton.ContextMenu?.IsOpen == true;
    private bool ConfirmDangoDeletion(string title, string text)
    {
        slotDialogOpen = true;
        ClearLiveDirectionalInput();
        try { return DangoDeleteConfirmation(title, text); }
        finally { slotDialogOpen = false; }
    }
    internal event Action? DangoReloadRequested;
    private void OnDangoReloadClick(object sender, RoutedEventArgs e) => DangoReloadRequested?.Invoke();
    private bool CanUseDangoSlots => dangoSlots is not null && !slotsBlocked && !slotPreviewPending && !IsEditing && !IsInPorch &&
        !IsDangoAway && !IsSettingsOpen && !liveSceneSuspended && !beltRoomSuspended && Visibility == Visibility.Visible && (!IsLoaded || IsVisible) && DateTime.UtcNow >= slotsTransitionUntil;

    internal void ConfigureDangoSlots(IReadOnlyList<DangoSlotView> slots, Guid? selectedId, string? selectedName, bool blocked, string? warning = null)
    {
        if (slots.Count != 3 || slots.Select(x => x.Number).Order().SequenceEqual(new[] { 1, 2, 3 }) == false)
            throw new ArgumentException("团子栏位必须保留固定的 1、2、3 位置。", nameof(slots));
        dangoSlots = slots.OrderBy(x => x.Number).ToArray();
        selectedDangoId = selectedId;
        slotsBlocked = blocked;
        DangoCreationNote.Text = IsCreatingDango && blocked && !string.IsNullOrEmpty(warning) ? "创建结果待核对，请重新读取。" : "创建后才会占用栏位。";
        DangoNameButton.Content = IsCreatingDango ? CreationPreviewName :
            selectedName ?? (selectedId.HasValue ? "请为团子起名" : "尚未创建团子");
        DangoSlotWarning.Text = warning ?? string.Empty;
        DangoSlotWarning.Visibility = string.IsNullOrEmpty(warning) ? Visibility.Collapsed : Visibility.Visible;
        DangoReloadButton.Visibility = string.IsNullOrEmpty(warning) ? Visibility.Collapsed : Visibility.Visible;
        RefreshAppearanceControls();
        RefreshDangoSlots();
        RefreshBeltVisibility();
        UpdateTransition(false);
    }

    private void RefreshDangoSlots()
    {
        if (DangoSlotPanel is null) return;
        DangoSlotPanel.Visibility = dangoSlots is null ? Visibility.Collapsed : Visibility.Visible;
        if (dangoSlots is null) return;
        var buttons = new[] { DangoSlot1, DangoSlot2, DangoSlot3 };
        for (var i = 0; i < 3; i++)
        {
            var slot = dangoSlots[i];
            var button = buttons[i];
            button.Content = slot.Id.HasValue ? slot.Number.ToString() : "+";
            var caption = slot.Id.HasValue ? $"选择第 {slot.Number} 栏：{slot.Name ?? "待起名"}" : $"在第 {slot.Number} 栏创建团子";
            button.ToolTip = caption;
            AutomationProperties.SetName(button, caption);
            button.IsEnabled = CanUseDangoSlots;
            var selected = slot.Id.HasValue && slot.Id == selectedDangoId;
            Select(button, selected);
            button.BorderBrush = (Brush)Resources[selected ? "HomeAccent" : "HomeLine"];
        }
        DangoNameButton.IsEnabled = CanUseDangoSlots && selectedDangoId.HasValue;
        var showBody = selectedDangoId.HasValue || IsCreatingDango || slotPreviewPending;
        LiveEntityLayer.Visibility = showBody ? Visibility.Visible : Visibility.Hidden;
        PetShadow.Visibility = showBody && !IsInPorch ? Visibility.Visible : Visibility.Hidden;
        if (!showBody) LiveSceneStatusText.Visibility = Visibility.Collapsed;
        FaceButton.IsEnabled = WardrobeButton.IsEnabled = EnterRoomButton.IsEnabled = selectedDangoId.HasValue && !slotsBlocked && !slotPreviewPending && !IsEditing && !IsInPorch;
    }

    private void OnDangoSlotClick(object sender, RoutedEventArgs e)
    {
        if (!CanUseDangoSlots || sender is not Button button) return;
        var index = Array.IndexOf(new[] { DangoSlot1, DangoSlot2, DangoSlot3 }, button);
        if (index < 0) return;
        ClearLiveDirectionalInput();
        DangoSlotRequested?.Invoke(index);
    }

    internal void ReplaceDangoSessions(AppearanceSettingsSession appearance, AccessorySettingsSession accessories)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(accessories);
        ClearLiveDirectionalInput();
        appearanceGeneration++;
        ClearAppearanceComparison();
        appearanceSession = appearance;
        BindAccessories(accessories);
        BoundRuntime?.SetAppearance(appearance.DraftAppearance);
        appearance.RequestBodyApplication();
        beltRequestedRevision = beltRequestedBodyEpoch = -1;
        beltPendingDisplay = beltSnapshot = null;
        UpdateBeltAppearance(true);
        RefreshAppearanceControls();
    }

    private void RestartSlotBody()
    {
        if (beltSettingsPath is not { } path) return;
        var composition = beltComposition;
        UnbindBeltBody();
        beltError = null;
        beltFatalError = false;
        beltRequestedRevision = beltRequestedBodyEpoch = -1;
        BindBeltBody(path, composition);
    }

    internal async Task<bool> PrepareDangoPreviewAsync(AppearanceSettingsSession appearance, AccessorySettingsSession accessories)
    {
        var previousAppearance = appearanceSession;
        var previousAccessories = accessorySession;
        var generation = ++slotPreviewGeneration;
        slotPreviewPending = true;
        RefreshDangoSlots();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var origin = beltRuntime;
        void Confirm(BeltBodySnapshot snapshot)
        {
            if (generation == slotPreviewGeneration && ReferenceEquals(appearanceSession, appearance) &&
                !ReferenceEquals(origin, beltRuntime) && snapshot.Epoch == beltRuntime?.Epoch &&
                snapshot.BodyEpoch == appearance.BodyEpoch && snapshot.ConfigurationRevision == appearance.BodyConfigurationRevision &&
                snapshot.Body == appearance.DraftAppearance.Body && snapshot.ModelVersion == appearance.DraftAppearance.BodyPhysicsModel)
                completion.TrySetResult(true);
        }
        BeltBodyDisplayConfirmed += Confirm;
        try
        {
            ReplaceDangoSessions(appearance, accessories);
            RestartSlotBody();
            var success = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(12))) == completion.Task && await completion.Task;
            if (generation != slotPreviewGeneration) return false;
            if (!success && previousAppearance is not null && previousAccessories is not null)
            {
                ReplaceDangoSessions(previousAppearance, previousAccessories);
                RestartSlotBody();
            }
            return success;
        }
        finally
        {
            BeltBodyDisplayConfirmed -= Confirm;
            if (generation == slotPreviewGeneration) { slotPreviewPending = false; RefreshDangoSlots(); }
        }
    }

    internal void BeginDangoCreation(int slotIndex, AppearanceSettingsSession draft, AccessorySettingsSession draftAcc)
    {
        if (slotIndex is < 0 or > 2 || IsCreatingDango) throw new InvalidOperationException("创建栏位无效。");
        originalAppearance = appearanceSession;
        originalAccessories = accessorySession;
        creationSlot = slotIndex;
        ReplaceDangoSessions(draft, draftAcc);
        RestartSlotBody();
        DangoCreationName.Text = string.Empty;
        DangoCreationFields.Visibility = Visibility.Visible;
        ApplyButton.Content = "创建";
        ApplyButton.Width = 64;
        ApplyButton.ToolTip = "创建 · 保存新团子";
        AutomationProperties.SetName(ApplyButton, "创建团子");
        ShowEditor(HomeEditor.Face);
        RefreshAppearanceControls();
        RefreshDangoSlots();
    }

    internal void EndDangoCreation()
    {
        endingCreation = true;
        creationSlot = null;
        originalAppearance = null;
        originalAccessories = null;
        DangoCreationFields.Visibility = Visibility.Collapsed;
        ApplyButton.Content = originalApplyContent;
        ApplyButton.Width = 32;
        ApplyButton.ToolTip = "应用 · 保存整套外观";
        AutomationProperties.SetName(ApplyButton, "应用并保存外观");
        CloseEditor();
        endingCreation = false;
        RefreshAppearanceControls();
        RefreshDangoSlots();
    }

    private object? originalApplyContent;
    private void CancelDangoCreation()
    {
        if (!IsCreatingDango || endingCreation || slotsBlocked) return;
        var appearance = originalAppearance;
        var accessories = originalAccessories;
        EndDangoCreation();
        if (appearance is not null && accessories is not null) { ReplaceDangoSessions(appearance, accessories); RestartSlotBody(); }
        DangoCreationCancelled?.Invoke();
    }

    internal static bool TryNormalizeDangoName(string? input, out string name)
    {
        name = input?.Trim() ?? string.Empty;
        if (name.Length == 0) return false;
        for (var i = 0; i < name.Length; i++)
        {
            var character = name[i];
            if (char.IsHighSurrogate(character))
            {
                if (i + 1 >= name.Length || !char.IsLowSurrogate(name[++i])) return false;
            }
            else if (char.IsLowSurrogate(character) || char.IsControl(character) ||
                char.GetUnicodeCategory(character) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) return false;
        }
        return StringInfo.ParseCombiningCharacters(name).Length <= 24;
    }

    private string CreationPreviewName => TryNormalizeDangoName(DangoCreationName.Text, out var name)
        ? name + " · 创建预览" : "新团子草稿";

    private void OnDangoCreationNameChanged(object sender, TextChangedEventArgs e)
    {
        if (DangoCreationValidation is not null)
            DangoCreationValidation.Text = TryNormalizeDangoName(DangoCreationName.Text, out _) ? string.Empty : "请输入 1–24 个字的名字，不含控制字符或换行。";
        if (IsCreatingDango) DangoNameButton.Content = CreationPreviewName;
        RefreshAppearanceControls();
    }

    private void OnDangoNameClick(object sender, RoutedEventArgs e)
    {
        if (!CanUseDangoSlots || selectedDangoId is not { } id) return;
        ClearLiveDirectionalInput();
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "改名" };
        var delete = new MenuItem { Header = "删除团子…" };
        rename.Click += (_, _) => RenameDango(id);
        delete.Click += (_, _) => RequestDangoDeletion(id);
        menu.Items.Add(rename); menu.Items.Add(delete);
        DangoNameButton.ContextMenu = menu;
        menu.PlacementTarget = DangoNameButton;
        menu.IsOpen = true;
    }

    private void RenameDango(Guid id)
    {
        var slot = dangoSlots?.FirstOrDefault(x => x.Id == id);
        if (slot is null || !CanUseDangoSlots) return;
        string? name;
        slotDialogOpen = true;
        try { name = DangoRenamePrompt is not null ? DangoRenamePrompt(slot.Name ?? string.Empty) : ShowDangoRenameDialog(slot.Name ?? string.Empty); }
        finally { slotDialogOpen = false; }
        if (TryNormalizeDangoName(name, out var normalized) && CanUseDangoSlots && dangoSlots?.Any(x => x.Id == id) == true)
            DangoRenameRequested?.Invoke(id, normalized);
    }

    private string? ShowDangoRenameDialog(string current)
    {
        var input = new TextBox { Text = current, Margin = new Thickness(0, 0, 0, 16), MinWidth = 240 };
        var save = new Button { Content = "改名", IsDefault = true, MinHeight = 40 };
        var cancel = new Button { Content = "取消", IsCancel = true, MinHeight = 40, Margin = new Thickness(8, 0, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        actions.Children.Add(save); actions.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "团子名字", Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(input); panel.Children.Add(actions);
        var dialog = new Window { Title = "改名", Content = panel, Owner = Window.GetWindow(this), SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)Resources["HomeBackground"], Foreground = (Brush)Resources["HomeInk"], FontSize = 14 };
        save.Click += (_, _) => { if (TryNormalizeDangoName(input.Text, out _)) dialog.DialogResult = true; };
        input.TextChanged += (_, _) => save.IsEnabled = TryNormalizeDangoName(input.Text, out _);
        save.IsEnabled = TryNormalizeDangoName(input.Text, out _);
        input.ToolTip = "1–24 个字，不含控制字符或换行";
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    internal void RequestDangoDeletion(Guid id)
    {
        var slot = dangoSlots?.FirstOrDefault(x => x.Id == id);
        if (slot is null || !CanUseDangoSlots) return;
        ClearLiveDirectionalInput();
        var description = $"第 {slot.Number} 栏的「{slot.Name}」";
        bool StillValid() => CanUseDangoSlots && dangoSlots?.Any(x => x.Id == id && x.Number == slot.Number && x.Name == slot.Name) == true;
        if (!ConfirmDangoDeletion("删除团子", $"确定删除{description}吗？其外观与穿戴将被永久删除。") || !StillValid()) return;
        if (!ConfirmDangoDeletion("永久删除确认", $"再次确认：永久删除{description}。此操作无法撤销。") || !StillValid()) return;
        DangoDeleteRequested?.Invoke(id);
    }

    internal void SetOrdinaryRoomNamePreference(bool useAccount, bool accountAvailable, string? warning = null)
    {
        OrdinaryRoomNameOptions.Visibility = Visibility.Visible;
        OrdinaryRoomDangoName.IsChecked = !useAccount;
        OrdinaryRoomAccountName.IsChecked = useAccount;
        OrdinaryRoomAccountName.IsEnabled = accountAvailable;
        OrdinaryRoomNameWarning.Text = warning ?? (accountAvailable ? string.Empty : "账号尚未接入，暂不可使用账号显示名。");
    }
    private void OnOrdinaryRoomDangoNameClick(object sender, RoutedEventArgs e) => OrdinaryRoomNameSourceRequested?.Invoke(false);
    private void OnOrdinaryRoomAccountNameClick(object sender, RoutedEventArgs e)
    {
        if (OrdinaryRoomAccountName.IsEnabled) OrdinaryRoomNameSourceRequested?.Invoke(true);
    }
}

