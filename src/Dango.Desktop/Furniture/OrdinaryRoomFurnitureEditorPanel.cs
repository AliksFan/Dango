using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Core.Furniture;
using Path = System.Windows.Shapes.Path;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;
using StackPanel = System.Windows.Controls.StackPanel;
using Orientation = System.Windows.Controls.Orientation;
using FontFamily = System.Windows.Media.FontFamily;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace Dango.Desktop.Furniture;

internal enum OrdinaryRoomFurnitureTool { Select, Glove }

/// <summary>Operation controls only. The existing room remains the sole scene and frame owner.</summary>
internal sealed class OrdinaryRoomFurnitureEditorPanel : UserControl
{
    private readonly Button select;
    private readonly Button glove;
    private readonly Button delete;
    private readonly Button confirm;
    private readonly Button cancel;
    private readonly Button exit;
    private readonly Button recover;
    private readonly Button platform;
    private readonly Button ball;
    private readonly System.Windows.Controls.Image platformThumbnail;
    private readonly System.Windows.Controls.Image ballThumbnail;
    private readonly TextBlock itemCount;
    private readonly List<Button> nudges = [];
    private readonly TextBlock selection;
    private readonly TextBlock status;
    internal event Action<OrdinaryRoomFurnitureTool>? ToolRequested;
    internal event Action<int, int>? NudgeRequested;
    internal event Action<FurnitureItemKind>? AddRequested;
    internal event Action? DeleteRequested;
    internal event Action? ConfirmRequested;
    internal event Action? CancelRequested;
    internal event Action? ExitRequested;
    internal event Action? RecoverRequested;

    internal OrdinaryRoomFurnitureEditorPanel(ResourceDictionary? homeResources)
    {
        Name = "RoomFurnitureEditor";
        Width = 216;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei");
        FontSize = 14;
        UseLayoutRounding = true;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        if (homeResources is not null) Resources.MergedDictionaries.Add(homeResources);
        SetResourceReference(ForegroundProperty, "HomeInk");
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        select = Icon("RoomFurnitureSelect", "选择 / 拖动家具", "M5,3 L5,20 L9,16 L13,22 L16,20 L12,14 L18,14 Z", () => ToolRequested?.Invoke(OrdinaryRoomFurnitureTool.Select));
        glove = Icon("RoomFurnitureGlove", "手套 · 拖动自己的团子", "M5,13 L5,8 Q5,6 7,6 Q9,6 9,8 L9,12 L9,4 Q9,2 11,2 Q13,2 13,4 L13,11 L13,5 Q13,3 15,3 Q17,3 17,5 L17,12 L17,8 Q17,6 19,6 Q21,6 21,8 L21,16 Q21,22 14,22 L10,22 Q7,22 5,19 L2,14 Q1,12 3,12 Z", () => ToolRequested?.Invoke(OrdinaryRoomFurnitureTool.Glove));
        delete = Icon("RoomFurnitureDelete", "删除已选择家具", "M4,6 L20,6 M9,6 L9,3 L15,3 L15,6 M6,6 L7,21 L17,21 L18,6 M10,10 L10,17 M14,10 L14,17", () => DeleteRequested?.Invoke());
        exit = Icon("RoomFurnitureExit", "退出布置 · 保留已确认家具", "M10,4 L4,4 L4,20 L10,20 M10,12 L21,12 M17,8 L21,12 L17,16", () => ExitRequested?.Invoke());
        foreach (var button in new[] { select, glove, delete, exit }) tools.Children.Add(button);
        root.Children.Add(tools);

        var sections = new StackPanel();
        sections.Children.Add(Heading("微调"));
        var arrows = new UniformGrid { Rows = 2, Columns = 3, Width = 112, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 12) };
        arrows.Children.Add(new Border());
        AddNudge("RoomFurnitureUp", "向上微调", "M5,14 L12,7 L19,14 M12,7 L12,21", 0, 1);
        arrows.Children.Add(new Border());
        AddNudge("RoomFurnitureLeft", "向左微调", "M14,5 L7,12 L14,19 M7,12 L21,12", -1, 0);
        AddNudge("RoomFurnitureDown", "向下微调", "M5,10 L12,17 L19,10 M12,3 L12,17", 0, -1);
        AddNudge("RoomFurnitureRight", "向右微调", "M10,5 L17,12 L10,19 M3,12 L17,12", 1, 0);
        sections.Children.Add(arrows);
        sections.Children.Add(Divider());
        sections.Children.Add(Heading("家具参数"));
        selection = new TextBlock { Margin = new Thickness(0, 6, 0, 3) };
        sections.Children.Add(selection);
        var future = new TextBlock { Text = "大小等参数后续开放", FontSize = 12, Margin = new Thickness(0, 0, 0, 12) };
        future.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
        sections.Children.Add(future);
        sections.Children.Add(Divider());
        var warehouseHeading = new DockPanel();
        itemCount = new TextBlock { Name = "RoomFurnitureCount", Text = "0/30", FontSize = 12, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        itemCount.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
        DockPanel.SetDock(itemCount, Dock.Right); warehouseHeading.Children.Add(itemCount);
        warehouseHeading.Children.Add(Heading("家具仓库"));
        sections.Children.Add(warehouseHeading);
        var warehouse = new UniformGrid { Name = "RoomFurnitureWarehouse", Columns = 2, Margin = new Thickness(0, 6, 0, 8) };
        platform = WarehouseButton("RoomFurniturePlatform", "木平台 · 双击或 Enter 添加预览", FurnitureItemKind.StaticPlatform);
        platformThumbnail = new System.Windows.Controls.Image { Name = "RoomFurniturePlatformThumbnail", Source = OfficialWoodPlatformVisual.CurrentImage,
            Width = 58, Height = 18, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        platform.Content = platformThumbnail;
        platform.Width = 72; platform.Height = 54;
        platform.Margin = new Thickness(0, 0, 8, 0);
        warehouse.Children.Add(platform);
        ball = WarehouseButton("RoomFurnitureBall", "皮球 · 双击或 Enter 添加预览", FurnitureItemKind.RoundBall);
        ballThumbnail = new System.Windows.Controls.Image { Name = "RoomFurnitureBallThumbnail", Source = OfficialRoundBallVisual.CurrentImage,
            Width = 38, Height = 38, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        ball.Content = ballThumbnail;
        ball.Width = 72; ball.Height = 54;
        ball.Margin = new Thickness(0, 0, 8, 0);
        warehouse.Children.Add(ball);
        sections.Children.Add(warehouse);
        var scroller = new ScrollViewer { Name = "RoomFurnitureSections", Content = sections,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, CanContentScroll = false };
        scroller.Resources.Add(typeof(ScrollBar), CreateThinScrollBarStyle());
        Grid.SetRow(scroller, 1); root.Children.Add(scroller);

        var footer = new StackPanel();
        status = new TextBlock { Name = "RoomFurnitureStatus", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 8), MaxHeight = 68 };
        status.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
        footer.Children.Add(status);
        var commits = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        recover = Icon("RoomFurnitureRecover", "核对并恢复已保存家具", "M4,10 A8,8 0 1 1 5,18 M4,4 L4,10 L10,10", () => RecoverRequested?.Invoke());
        cancel = Icon("RoomFurnitureCancel", "取消当前预览", "M5,5 L19,19 M19,5 L5,19", () => CancelRequested?.Invoke());
        confirm = Icon("RoomFurnitureConfirm", "确认并保存当前家具", "M4,12 L10,18 L21,6", () => ConfirmRequested?.Invoke());
        confirm.Tag = "Primary";
        confirm.SetResourceReference(Button.BackgroundProperty, "HomeSelected");
        confirm.SetResourceReference(Button.BorderBrushProperty, "HomeAccent");
        confirm.SetResourceReference(Button.ForegroundProperty, "HomePrimaryInk");
        foreach (var button in new[] { recover, cancel, confirm }) commits.Children.Add(button);
        footer.Children.Add(commits); Grid.SetRow(footer, 2); root.Children.Add(footer);
        var border = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = root };
        border.SetResourceReference(Border.BackgroundProperty, "HomeBackground");
        border.SetResourceReference(Border.BorderBrushProperty, "HomeLine");
        Content = border;
        // Sidebar owns its wheel, even when there is no overflowing content.
        PreviewMouseWheel += (_, e) =>
        {
            if (scroller.IsMouseOver) scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta / 3d);
            e.Handled = true;
        };

        void AddNudge(string name, string label, string geometry, int x, int y)
        {
            var button = Icon(name, label, geometry, () => NudgeRequested?.Invoke(x, y));
            button.Margin = new Thickness(2); nudges.Add(button); arrows.Children.Add(button);
        }
    }

    internal void Refresh(bool canEdit, bool canStep, bool busy, bool selected, bool hasPlatform,
        bool hasPreview, bool canConfirm, bool canRecover, string message, OrdinaryRoomFurnitureTool tool)
        => Refresh(canEdit, canStep, busy, selected ? FurnitureItemKind.StaticPlatform : null, selected && hasPlatform,
            hasPlatform ? 1 : 0, hasPreview, canConfirm, canRecover, message, tool);

    internal void Refresh(bool canEdit, bool canStep, bool busy, FurnitureItemKind? selectedKind, bool selectedApplied, int count,
        bool hasPreview, bool canConfirm, bool canRecover, string message, OrdinaryRoomFurnitureTool tool)
    {
        select.IsEnabled = canEdit && !busy;
        platform.IsEnabled = ball.IsEnabled = canEdit && !busy && count < FurnitureLayoutContract.OrdinaryRoom.MaximumItems;
        glove.IsEnabled = canStep && canEdit && !busy;
        delete.IsEnabled = canEdit && !busy && selectedApplied;
        confirm.Visibility = cancel.Visibility = hasPreview ? Visibility.Visible : Visibility.Collapsed;
        confirm.IsEnabled = canEdit && !busy && canConfirm;
        cancel.IsEnabled = canEdit && !busy;
        exit.IsEnabled = !busy;
        recover.Visibility = canRecover ? Visibility.Visible : Visibility.Collapsed;
        recover.IsEnabled = canRecover && !busy;
        foreach (var button in nudges) button.IsEnabled = canEdit && !busy && selectedKind is not null;
        selection.Text = selectedKind switch { FurnitureItemKind.StaticPlatform => "木平台", FurnitureItemKind.RoundBall => "皮球", _ => "未选择家具" };
        platformThumbnail.Source = OfficialWoodPlatformVisual.CurrentImage;
        ballThumbnail.Source = OfficialRoundBallVisual.CurrentImage;
        itemCount.Text = $"{count}/{FurnitureLayoutContract.OrdinaryRoom.MaximumItems}";
        AutomationProperties.SetName(itemCount, $"已放置 {count} 件家具，上限 {FurnitureLayoutContract.OrdinaryRoom.MaximumItems} 件");
        status.Text = count >= FurnitureLayoutContract.OrdinaryRoom.MaximumItems && string.IsNullOrEmpty(message)
            ? "家具已达 30 件上限，可移动或删除。" : message;
        ToolTip = message;
        SetSelected(select, tool == OrdinaryRoomFurnitureTool.Select);
        SetSelected(glove, tool == OrdinaryRoomFurnitureTool.Glove);
    }

    private Button WarehouseButton(string name, string label, FurnitureItemKind kind)
    {
        // A Click (including the first click of a double-click) never creates
        // furniture. Only the second mouse press creates one pending preview.
        var button = ActionButton(name, label, () => { });
        button.MouseDoubleClick += (_, e) =>
        {
            // Control synthesizes this event only for the second raw mouse
            // press, but its fresh args have ClickCount == 1.
            if (e.ChangedButton != MouseButton.Left || !button.IsEnabled) return;
            e.Handled = true;
            AddRequested?.Invoke(kind);
        };
        button.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || e.IsRepeat || !button.IsEnabled || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            AddRequested?.Invoke(kind);
        };
        return button;
    }

    private Button Icon(string name, string label, string geometry, Action action)
    {
        var button = ActionButton(name, label, action);
        var path = new Path { Data = Geometry.Parse(geometry), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 20, Height = 20, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        path.SetBinding(Path.StrokeProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = button });
        button.Content = path;
        return button;
    }

    private Button ActionButton(string name, string label, Action action)
    {
        var hint = new System.Windows.Controls.ToolTip { Content = label, Placement = PlacementMode.Bottom };
        var button = new Button { Name = name, Width = 32, Height = 32, Padding = new Thickness(5), Margin = new Thickness(0, 0, 8, 0), ToolTip = hint };
        if (TryFindResource("FaceIconButton") is Style style) button.Style = style;
        AutomationProperties.SetName(button, label);
        ToolTipService.SetInitialShowDelay(button, 250);
        ToolTipService.SetShowOnDisabled(button, true);
        button.Click += (_, _) => action();
        button.GotKeyboardFocus += (_, _) => { hint.PlacementTarget = button; hint.IsOpen = true; };
        button.LostKeyboardFocus += (_, _) => hint.IsOpen = false;
        button.Unloaded += (_, _) => hint.IsOpen = false;
        return button;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold };
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
    private static Border Divider()
    {
        var line = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 10) };
        line.SetResourceReference(Border.BackgroundProperty, "HomeLine"); return line;
    }
    private static void SetSelected(Button button, bool selected)
    {
        button.Tag = selected ? "Selected" : null;
        button.SetResourceReference(Button.ForegroundProperty, selected ? "HomePrimaryInk" : "HomeInk");
        AutomationProperties.SetItemStatus(button, selected ? "已选择" : "未选择");
        if (selected) button.SetResourceReference(Button.BackgroundProperty, "HomeSelected");
        else button.ClearValue(Button.BackgroundProperty);
    }
}
