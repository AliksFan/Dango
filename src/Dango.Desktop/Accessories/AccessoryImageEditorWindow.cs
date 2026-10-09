using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;

namespace Dango.Desktop.Accessories;

/// <summary>Non-destructive image transforms; accepting returns a draft, never writes assets.</summary>
internal sealed class AccessoryImageEditorWindow : Window
{
    private AccessoryItem draft;
    private readonly AccessoryImageEditorSurface surface;
    private readonly ScrollViewer viewport;
    private readonly Dictionary<string, Slider> sliders = [];
    private readonly Dictionary<string, TextBox> valueTexts = [];
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) };
    private bool refreshing;
    internal AccessoryItem? Result { get; private set; }
    internal AccessoryItem Draft => draft;
    internal double ViewZoom { get; private set; } = 1;
    internal string StatusMessage => status.Text;

    internal AccessoryImageEditorWindow(AccessoryItem item, BitmapSource image, string? importNotice = null)
    {
        draft = item;
        AccessoryEditorTheme.Install(this);
        Title = "编辑配饰图片";
        Width = 850; Height = 620; MinWidth = 560; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "HomeBackground");
        surface = new AccessoryImageEditorSurface(image, item);
        surface.PivotSelected += SetImagePivot;
        var layout = new Grid { Margin = new Thickness(16) };
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        viewport = new ScrollViewer
        {
            Content = surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 14, 0),
        };
        var previewFrame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Child = viewport };
        previewFrame.SetResourceReference(Border.BorderBrushProperty, "HomeLine");
        Grid.SetRowSpan(previewFrame, 2); layout.Children.Add(previewFrame);
        var controls = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        if (!string.IsNullOrWhiteSpace(importNotice))
        {
            var notice = new TextBlock { Text = importNotice, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 12) };
            notice.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
            AutomationProperties.SetName(notice, "图片压缩提示");
            controls.Children.Add(notice);
        }
        var parameterScroll = new ScrollViewer
        {
            Content = controls,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetColumn(parameterScroll, 1); layout.Children.Add(parameterScroll);
        controls.Children.Add(new TextBlock { Text = "点选图片连接支点", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        controls.Children.Add(new TextBlock
        {
            Text = "支点可放在透明区域。缩放与旋转保留原图，确认后返回换装草稿。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12),
        });
        ((TextBlock)controls.Children[^1]).SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
        AddNumber(controls, "画布缩放 %", 25, 400, 100, value => SetViewZoom(value / 100), "zoom");
        AddNumber(controls, "配饰尺寸 %", 10, 400, item.SizePercent, value => SetDraft(draft with { SizePercent = value }), "size");
        AddNumber(controls, "基础角度 °", -180, 180, item.BaseAngleDegrees, value => SetDraft(draft with { BaseAngleDegrees = value }), "angle");
        AddNumber(controls, "图片支点 X", 0, 1, item.ImagePivotX, value => SetImagePivot(new(value, draft.ImagePivotY)), "pivotX");
        AddNumber(controls, "图片支点 Y", 0, 1, item.ImagePivotY, value => SetImagePivot(new(draft.ImagePivotX, value)), "pivotY");
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var reset = AccessoryEditorTheme.IconButton("复位图片变换", AccessoryEditorTheme.ResetIcon,
            "复位当前图片的尺寸、旋转与支点；确认后仅返回草稿。",
            () => SetDraft(draft with { SizePercent = 100, BaseAngleDegrees = 0, ImagePivotX = .5, ImagePivotY = .5 }, discardInput: true));
        reset.Margin = new Thickness(0, 0, 8, 0);
        var cancel = AccessoryEditorTheme.IconButton("取消", AccessoryEditorTheme.CancelIcon, "取消 · 放弃这个图片编辑窗口的修改。", () => { });
        cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 8, 0);
        var accept = AccessoryEditorTheme.IconButton("确认", AccessoryEditorTheme.ApplyIcon,
            "确认 · 返回换装草稿；仍需在换装中点击应用才保存到本机。",
            () => { if (!CommitPendingInputs()) return; Result = draft; DialogResult = true; }, primary: true);
        accept.IsDefault = true;
        actions.Children.Add(reset); actions.Children.Add(cancel); actions.Children.Add(accept);
        var footer = new StackPanel { Margin = new Thickness(16, 16, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        footer.Children.Add(status); footer.Children.Add(actions);
        Grid.SetColumn(footer, 1); Grid.SetRow(footer, 1); layout.Children.Add(footer);
        Content = layout;
        Loaded += (_, _) => CenterPreview();
        PreviewMouseWheel += (_, e) =>
        {
            if (!viewport.IsMouseOver) return;
            SetViewZoom(Math.Clamp(ViewZoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), .25, 4));
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            // This is an independent modal input surface; navigation stays here.
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        };
    }

    internal void SetViewZoom(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom < .25 || zoom > 4) throw new ArgumentOutOfRangeException(nameof(zoom));
        ViewZoom = zoom;
        refreshing = true;
        sliders["zoom"].Value = zoom * 100;
        valueTexts["zoom"].Text = Format(sliders["zoom"].Value);
        refreshing = false;
        surface.SetDraft(draft, zoom); CenterPreview();
    }

    internal void SetImagePivot(Point normalized)
    {
        if (!double.IsFinite(normalized.X) || !double.IsFinite(normalized.Y) || normalized.X is < 0 or > 1 || normalized.Y is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(normalized));
        if (!CommitPendingInputs()) return;
        SetDraft(draft with { ImagePivotX = normalized.X, ImagePivotY = normalized.Y });
    }

    private Dictionary<string, string> BufferedInputs() => valueTexts
        .Where(pair => pair.Value.Text != Format(sliders[pair.Key].Value)).ToDictionary(pair => pair.Key, pair => pair.Value.Text);

    private static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    private bool CommitPendingInputs()
    {
        if (refreshing || BufferedInputs().Count == 0) return true;
        var numbers = new Dictionary<string, double>();
        foreach (var (key, text) in valueTexts)
        {
            if (!AccessoryEditorTheme.TryReadNumber(text, sliders[key], out var number))
            {
                status.Text = $"{AutomationProperties.GetName(sliders[key])}需为 {Format(sliders[key].Minimum)}–{Format(sliders[key].Maximum)} 的有限数值；输入已保留，修正后再确认。";
                return false;
            }
            numbers[key] = number;
        }
        ViewZoom = numbers["zoom"] / 100;
        SetDraft(draft with
        {
            SizePercent = numbers["size"],
            BaseAngleDegrees = numbers["angle"],
            ImagePivotX = numbers["pivotX"],
            ImagePivotY = numbers["pivotY"],
        }, discardInput: true);
        return true;
    }

    private void SetDraft(AccessoryItem item, bool discardInput = false)
    {
        var buffered = discardInput ? [] : BufferedInputs();
        draft = item;
        refreshing = true;
        sliders["zoom"].Value = ViewZoom * 100;
        sliders["size"].Value = item.SizePercent; sliders["angle"].Value = item.BaseAngleDegrees;
        sliders["pivotX"].Value = item.ImagePivotX; sliders["pivotY"].Value = item.ImagePivotY;
        foreach (var (key, text) in valueTexts) text.Text = Format(sliders[key].Value);
        foreach (var (key, text) in buffered) valueTexts[key].Text = text;
        refreshing = false;
        if (discardInput) status.Text = "";
        surface.SetDraft(item, ViewZoom); CenterPreview();
    }

    private void CenterPreview()
    {
        if (!IsLoaded) return;
        viewport.UpdateLayout();
        viewport.ScrollToHorizontalOffset(Math.Max(0, (surface.Width - viewport.ViewportWidth) / 2));
        viewport.ScrollToVerticalOffset(Math.Max(0, (surface.Height - viewport.ViewportHeight) / 2));
    }

    private void AddNumber(StackPanel parent, string label, double min, double max, double initial, Action<double> update, string key)
    {
        var number = AccessoryEditorTheme.Number(label, label, min, max, initial, value =>
        {
            if (refreshing) return;
            if (!CommitPendingInputs()) { SetDraft(draft); return; }
            update(value);
        }, () => CommitPendingInputs());
        sliders[key] = number.Slider; valueTexts[key] = number.Value; parent.Children.Add(number.Row);
    }
}

internal sealed class AccessoryImageEditorSurface : FrameworkElement
{
    private readonly BitmapSource image;
    private AccessoryItem item;
    private double zoom = 1;
    private static readonly Brush Checkerboard = CreateCheckerboard();
    internal event Action<Point>? PivotSelected;
    private Size ImageSize => new(image.PixelWidth * ImageScale, image.PixelHeight * ImageScale);
    private double ImageScale => 64d / Math.Max(image.PixelWidth, image.PixelHeight) * item.SizePercent / 100 * 3 * zoom;

    internal AccessoryImageEditorSurface(BitmapSource image, AccessoryItem item)
    {
        this.image = image; this.item = item;
        Focusable = true; Cursor = Cursors.Cross;
        AutomationProperties.SetName(this, "配饰原图与图片支点预览");
        SetDraft(item, 1);
        MouseLeftButtonDown += (_, e) =>
        {
            var point = e.GetPosition(this); var center = new Point(Width / 2, Height / 2);
            var inverse = new RotateTransform(-item.BaseAngleDegrees, center.X, center.Y).Transform(point);
            var size = ImageSize;
            var x = (inverse.X - center.X) / size.Width + item.ImagePivotX;
            var y = (inverse.Y - center.Y) / size.Height + item.ImagePivotY;
            if (x is >= 0 and <= 1 && y is >= 0 and <= 1) PivotSelected?.Invoke(new(x, y));
            Focus(); e.Handled = true;
        };
    }

    internal void SetDraft(AccessoryItem draft, double viewZoom)
    {
        item = draft; zoom = viewZoom;
        var span = Math.Sqrt(ImageSize.Width * ImageSize.Width + ImageSize.Height * ImageSize.Height) * 2 + 80;
        Width = Math.Max(520, span); Height = Math.Max(420, span);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Checkerboard, null, new Rect(0, 0, Width, Height));
        var center = new Point(Width / 2, Height / 2); var size = ImageSize;
        dc.PushTransform(new RotateTransform(item.BaseAngleDegrees, center.X, center.Y));
        var rect = new Rect(center.X - size.Width * item.ImagePivotX, center.Y - size.Height * item.ImagePivotY, size.Width, size.Height);
        var line = TryFindResource("HomeLine") as Brush ?? Brushes.Gray;
        var accent = TryFindResource("HomeAccent") as Brush ?? Brushes.SaddleBrown;
        dc.DrawImage(image, rect); dc.DrawRectangle(null, new Pen(line, 1), rect); dc.Pop();
        var pen = new Pen(accent, 2);
        dc.DrawEllipse(Brushes.White, pen, center, 5, 5);
        dc.DrawLine(pen, new(center.X - 12, center.Y), new(center.X + 12, center.Y));
        dc.DrawLine(pen, new(center.X, center.Y - 12), new(center.X, center.Y + 12));
    }

    private static Brush CreateCheckerboard()
    {
        var group = new DrawingGroup();
        var light = new SolidColorBrush(Color.FromRgb(253, 252, 249)); var dark = new SolidColorBrush(Color.FromRgb(233, 228, 218));
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(12, 12, 12, 12))));
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 24, 24), Stretch = Stretch.None };
        brush.Freeze(); return brush;
    }
}
