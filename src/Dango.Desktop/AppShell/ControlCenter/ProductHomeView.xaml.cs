using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using UserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.AppShell.ControlCenter;

public enum HomeEditor { Face, Wardrobe }

/// <summary>The approved home composition. Preview choices never mutate the desktop or persist settings.</summary>
public partial class ProductHomeView : UserControl
{
    private int transitionVersion;
    private bool reduceMotion;
    public event Action? SettingsRequested;
    public event Action? NoticeRequested;
    public bool IsEditing { get; private set; }
    public HomeEditor SelectedEditor { get; private set; }
    public Color PreviewColor { get; private set; } = Color.FromRgb(245, 213, 165);
    public bool ReduceMotion
    {
        get => reduceMotion;
        set { reduceMotion = value; if (value) UpdateTransition(false); }
    }

    public ProductHomeView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateColorSelection();
    }

    public void ShowEditor(HomeEditor editor)
    {
        var wasOpen = IsEditing;
        IsEditing = !IsEditing || SelectedEditor != editor;
        SelectedEditor = editor;
        FaceOptions.Visibility = editor == HomeEditor.Face ? Visibility.Visible : Visibility.Collapsed;
        WardrobeOptions.Visibility = editor == HomeEditor.Wardrobe ? Visibility.Visible : Visibility.Collapsed;
        EditorTitle.Text = editor == HomeEditor.Face ? "捏脸" : "换装";
        if (!IsEditing) FocusLauncher();
        else if (wasOpen && EditorPanel.IsKeyboardFocusWithin) FocusLauncher();
        UpdateTransition(true);
    }

    public void CloseEditor()
    {
        if (!IsEditing) return;
        FocusLauncher();
        IsEditing = false;
        UpdateTransition(true);
    }

    private void FocusLauncher() => (SelectedEditor == HomeEditor.Face ? FaceButton : WardrobeButton).Focus();
    private void OnFaceClick(object sender, RoutedEventArgs e) => ShowEditor(HomeEditor.Face);
    private void OnWardrobeClick(object sender, RoutedEventArgs e) => ShowEditor(HomeEditor.Wardrobe);
    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseEditor();
    private void OnHomeClick(object sender, RoutedEventArgs e) => CloseEditor();
    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void OnNoticeClick(object sender, RoutedEventArgs e) => NoticeRequested?.Invoke();
    public void SetNotice(string? text, string? detail)
    {
        NoticeButton.Content = text;
        NoticeButton.ToolTip = detail;
        NoticeButton.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !IsEditing) return;
        CloseEditor();
        e.Handled = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged += OnSystemPreferenceChanged;
        UpdateTheme();
        UpdateTransition(false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemPreferenceChanged;
        UpdateTransition(false);
    }

    private void OnSystemPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast)) UpdateTheme();
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation) && !SystemParameters.ClientAreaAnimation)
                UpdateTransition(false);
        });
    }

    private void UpdateTheme()
    {
        var contrast = SystemParameters.HighContrast;
        Background = contrast ? System.Windows.SystemColors.WindowBrush : Brush("#FDFCF9");
        Foreground = contrast ? System.Windows.SystemColors.WindowTextBrush : Brush("#443D34");
        Resources["HomeInk"] = Foreground;
        Resources["HomeMuted"] = contrast ? Foreground : Brush("#807568");
        Resources["HomeLine"] = contrast ? Foreground : Brush("#E9E4DA");
        Resources["HomeSoft"] = contrast ? Background : Brush("#F3EFE8");
        Resources["HomeAccent"] = contrast ? System.Windows.SystemColors.HighlightBrush : Brush("#885E3E");
        Resources["HomeSelected"] = contrast ? System.Windows.SystemColors.HighlightBrush : Brush("#EFE3D4");
        UpdateLauncherSelection();
        UpdateColorSelection();
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditing || SelectedEditor != HomeEditor.Face || sender is not Button { Tag: string color }) return;
        PreviewColor = (Color)ColorConverter.ConvertFromString(color);
        PetBody.Fill = new SolidColorBrush(PreviewColor);
        UpdateColorSelection();
    }

    private void UpdateColorSelection()
    {
        foreach (var (button, check) in new[] { (CreamButton, CreamCheck), (PeachButton, PeachCheck), (GreenButton, GreenCheck) })
        {
            var selected = PreviewColor == (Color)ColorConverter.ConvertFromString((string)button.Tag);
            button.BorderBrush = selected ? (System.Windows.Media.Brush)Resources["HomeAccent"] : Brushes.Transparent;
            check.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            AutomationProperties.SetItemStatus(button, selected ? "已选，仅预览" : "未选");
        }
    }

    private void UpdateLauncherSelection()
    {
        foreach (var (button, editor) in new[] { (FaceButton, HomeEditor.Face), (WardrobeButton, HomeEditor.Wardrobe) })
        {
            var selected = IsEditing && SelectedEditor == editor;
            button.Background = selected ? (System.Windows.Media.Brush)Resources["HomeSelected"] : Brushes.Transparent;
            button.Foreground = selected ? SystemParameters.HighContrast ? System.Windows.SystemColors.HighlightTextBrush
                : (System.Windows.Media.Brush)Resources["HomeAccent"] : (System.Windows.Media.Brush)Resources["HomeInk"];
            AutomationProperties.SetItemStatus(button, selected ? "已展开" : "已收起");
        }
    }

    private void OnHomeSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Stage is null) return;
        var compact = ActualWidth <= 680;
        var narrow = ActualWidth <= 420;
        TopNavigation.Margin = compact ? new Thickness(16) : new Thickness(30, 22, 30, 22);
        Brand.Margin = new Thickness(0, 0, compact ? 12 : 32, 0);
        Grid.SetRow(ToolsNavigation, compact ? 1 : 0);
        Grid.SetColumn(ToolsNavigation, compact ? 0 : 2);
        Grid.SetColumnSpan(ToolsNavigation, compact ? 3 : 1);
        MainNavigation.HorizontalAlignment = compact ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
        foreach (Button button in MainNavigation.Children) button.Margin = new Thickness(compact ? 4 : 10, 0, compact ? 4 : 10, 0);
        Stage.Margin = narrow ? new Thickness(12, 8, 12, 20) : compact ? new Thickness(16, 12, 16, 24) : new Thickness(38, 20, 38, 30);
        Stage.MinHeight = compact ? 380 : 420;
        // Keep a fixed centerline even when optional details make the panel taller.
        Presence.Margin = new Thickness(0, (Stage.MinHeight - 116 - (narrow ? 16 : compact ? 32 : 40) - 40) / 2, 0, 0);
        LeftColumn.Width = new GridLength(narrow ? 44 : compact ? 50 : 57, GridUnitType.Star);
        RightColumn.Width = new GridLength(narrow ? 56 : compact ? 50 : 43, GridUnitType.Star);
        GapColumn.Width = new GridLength(narrow ? 12 : compact ? 22 : 46);
        PetPreview.Width = narrow ? 144 * .74 : 144;
        PetPreview.Height = narrow ? 116 * .74 : 116;
        PetScale.ScaleX = PetScale.ScaleY = narrow ? 1 : 1.15;
        Launchers.Margin = new Thickness(0, narrow ? 16 : compact ? 32 : 40, 0, 0);
        WardrobeButton.Margin = new Thickness(narrow ? 4 : 12, 0, 0, 0);
        foreach (var button in new[] { FaceButton, WardrobeButton })
        {
            button.MinWidth = narrow ? 44 : 72;
            button.Padding = new Thickness(narrow ? 6 : 14, 8, narrow ? 6 : 14, 8);
        }
        foreach (var button in new[] { CreamButton, PeachButton, GreenButton }) button.Margin = new Thickness(0, 0, narrow ? 4 : 8, 0);
        Colors.Margin = new Thickness(0, 0, 0, narrow ? 20 : 24);
        EyePresetButton.Margin = new Thickness(0, 0, 0, narrow ? 20 : 24);
        ApplyButton.MinWidth = narrow ? 72 : 90;
        EditorPanel.Margin = new Thickness(4, compact ? 18 : 22, 4, 24);
        EditorPanel.MinHeight = compact ? 338 : 374;
        HomeFooter.Margin = new Thickness(compact ? 22 : 30, 10, 30, 16);
        UpdateTransition(false);
    }

    private void OnStageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTransition(false);

    private void UpdateTransition(bool animate)
    {
        if (EditorPanel is null) return;
        var version = ++transitionVersion;
        var width = Math.Max(0, Stage.ActualWidth);
        var gap = GapColumn.Width.Value;
        var available = Math.Max(0, width - gap);
        var left = available * LeftColumn.Width.Value / (LeftColumn.Width.Value + RightColumn.Width.Value);
        Presence.Width = Math.Min(210, left);
        var offset = IsEditing ? -(width - left) / 2 : 0;
        animate &= IsLoaded && !ReduceMotion && SystemParameters.ClientAreaAnimation;
        UpdateLauncherSelection();
        EditorPanel.IsEnabled = EditorPanel.IsHitTestVisible = IsEditing;
        if (IsEditing) EditorPanel.Visibility = Visibility.Visible;
        Animate(PresenceTranslation, TranslateTransform.XProperty, offset, animate);
        Animate(PanelTranslation, TranslateTransform.XProperty, IsEditing ? 0 : 46, animate);
        Animate(EditorPanel, OpacityProperty, IsEditing ? 1 : 0, animate, () =>
        {
            if (version == transitionVersion && !IsEditing) EditorPanel.Visibility = Visibility.Hidden;
        });
    }

    private static void Animate(Animatable target, DependencyProperty property, double destination, bool animate)
    {
        var from = (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        target.SetValue(property, destination);
        if (animate) target.BeginAnimation(property, CreateAnimation(from, destination), HandoffBehavior.SnapshotAndReplace);
    }

    private static void Animate(UIElement target, DependencyProperty property, double destination, bool animate, Action completed)
    {
        var from = (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        target.SetValue(property, destination);
        if (!animate) { completed(); return; }
        var animation = CreateAnimation(from, destination);
        animation.Completed += (_, _) => completed();
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimationUsingKeyFrames CreateAnimation(double from, double destination) => new()
    {
        Duration = TimeSpan.FromMilliseconds(440),
        FillBehavior = FillBehavior.Stop,
        KeyFrames =
        {
            new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)),
            new SplineDoubleKeyFrame(destination, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(440)), new KeySpline(.22, 1, .36, 1)),
        },
    };

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
}
