using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBox = System.Windows.Controls.TextBox;

namespace Dango.Desktop.Accessories;

/// <summary>Uses the Home theme in its resource tree; standalone editors receive the same fallback chrome.</summary>
internal static class AccessoryEditorTheme
{
    private static readonly ConditionalWeakTable<FrameworkElement, ResourceDictionary> Fallbacks = new();
    private static readonly object[] SharedKeys =
        ["HomeInk", "HomeMuted", "HomeLine", "HomeSoft", "HomeAccent", "HomeSelected", "HomePrimaryInk", "HomeBackground", typeof(Button), "FaceIconButton"];

    internal static void Install(FrameworkElement target)
    {
        var resources = (ResourceDictionary)XamlReader.Parse(FallbackXaml);
        target.Resources.MergedDictionaries.Add(resources);
        Fallbacks.Add(target, resources);
        target.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI, Microsoft YaHei"));
        target.SetValue(TextBlock.FontSizeProperty, 14d);
        target.SetResourceReference(TextBlock.ForegroundProperty, "HomeInk");
        target.UseLayoutRounding = true;
    }

    internal static void AdoptParent(FrameworkElement target)
    {
        var parent = target.Parent as FrameworkElement ?? VisualTreeHelper.GetParent(target) as FrameworkElement;
        if (parent is null || SharedKeys.Any(key => parent.TryFindResource(key) is null)) return;
        // In Home, inherit live resources so later theme changes are not hidden by a local snapshot.
        if (Fallbacks.TryGetValue(target, out var fallback))
        {
            target.Resources["AccessoryInlineNumber"] = fallback["AccessoryInlineNumber"];
            target.Resources["AccessoryLibraryItem"] = fallback["AccessoryLibraryItem"];
            target.Resources.MergedDictionaries.Remove(fallback);
        }
    }

    internal static void CopyFrom(FrameworkElement source, FrameworkElement target)
    {
        foreach (var key in SharedKeys)
            if (source.TryFindResource(key) is { } resource) target.Resources[key] = resource;
    }

    internal static Button IconButton(string name, string path, string tooltip, Action action, bool primary = false)
    {
        var button = new Button { ToolTip = tooltip, MinWidth = 0 };
        button.SetResourceReference(FrameworkElement.StyleProperty, "FaceIconButton");
        AutomationProperties.SetName(button, name);
        if (primary)
        {
            button.Tag = "Primary";
            button.SetResourceReference(Control.BackgroundProperty, "HomeSelected");
            button.SetResourceReference(Control.BorderBrushProperty, "HomeAccent");
            button.SetResourceReference(Control.ForegroundProperty, "HomePrimaryInk");
            button.IsEnabledChanged += (_, _) =>
                button.SetResourceReference(Control.ForegroundProperty, button.IsEnabled ? "HomePrimaryInk" : "HomeMuted");
        }
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        var line = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(path),
            StrokeThickness = primary ? 2 : 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        line.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,
            new Binding(nameof(Control.Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        canvas.Children.Add(line);
        button.Content = new Viewbox { Width = 20, Height = 20, Child = canvas, IsHitTestVisible = false };
        button.Click += (_, _) => action();
        return button;
    }

    internal static (StackPanel Row, Slider Slider, TextBox Value) Number(
        string label, string visibleLabel, double minimum, double maximum, double initial, Action<double> changed,
        Action? commitInput = null)
    {
        var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock { Text = visibleLabel, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var value = new TextBox
        {
            Width = 44,
            MinWidth = 0,
            Text = initial.ToString("0.###", CultureInfo.CurrentCulture),
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(4, 0, 0, 0),
        };
        value.SetResourceReference(FrameworkElement.StyleProperty, "AccessoryInlineNumber");
        Grid.SetColumn(value, 1); heading.Children.Add(value); row.Children.Add(heading);
        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = initial,
            SmallChange = maximum <= 1 ? .01 : 1,
            LargeChange = maximum <= 1 ? .1 : 10,
            Margin = new Thickness(0, 6, 0, 2),
        };
        AutomationProperties.SetName(slider, label); AutomationProperties.SetName(value, label + " 数值");
        row.Children.Add(slider);
        slider.ValueChanged += (_, _) =>
        {
            value.Text = slider.Value.ToString("0.###", CultureInfo.CurrentCulture);
            changed(slider.Value);
        };
        void Commit()
        {
            if (commitInput is not null) { commitInput(); return; }
            if (TryReadNumber(value, slider, out var number)) slider.Value = number;
        }
        value.LostKeyboardFocus += (_, _) => Commit();
        value.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        return (row, slider, value);
    }

    internal static bool TryReadNumber(TextBox value, Slider slider, out double number)
    {
        // Display rounding must not turn an untouched control into a configuration edit.
        if (value.Text == slider.Value.ToString("0.###", CultureInfo.CurrentCulture))
        {
            number = slider.Value;
            return true;
        }
        return double.TryParse(value.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out number) &&
            double.IsFinite(number) && number >= slider.Minimum && number <= slider.Maximum;
    }

    internal const string ResetIcon = "M4,9 A9,9 0 1 1 3,15 M4,3 L4,9 L10,9";
    internal const string CancelIcon = "M6,6 L18,18 M18,6 L6,18";
    internal const string ApplyIcon = "M4,12 L9,17 L20,6";

    private const string FallbackXaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="HomeInk" Color="#443D34" />
          <SolidColorBrush x:Key="HomeMuted" Color="#807568" />
          <SolidColorBrush x:Key="HomeLine" Color="#E9E4DA" />
          <SolidColorBrush x:Key="HomeSoft" Color="#F3EFE8" />
          <SolidColorBrush x:Key="HomeAccent" Color="#885E3E" />
          <SolidColorBrush x:Key="HomeSelected" Color="#EFE3D4" />
          <SolidColorBrush x:Key="HomePrimaryInk" Color="#443D34" />
          <SolidColorBrush x:Key="HomeBackground" Color="#FDFCF9" />
          <Style TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource HomeInk}" />
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="BorderBrush" Value="{DynamicResource HomeLine}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Padding" Value="14,8" />
            <Setter Property="MinHeight" Value="40" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="HorizontalContentAlignment" Value="Center" />
            <Setter Property="VerticalContentAlignment" Value="Center" />
            <Setter Property="ToolTipService.ShowOnDisabled" Value="True" />
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="Chrome" CornerRadius="7" Padding="{TemplateBinding Padding}"
                          Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}">
                    <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                      VerticalAlignment="{TemplateBinding VerticalContentAlignment}" />
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource HomeSoft}" />
                    </Trigger>
                    <MultiTrigger>
                      <MultiTrigger.Conditions><Condition Property="IsMouseOver" Value="True" /><Condition Property="Tag" Value="Primary" /></MultiTrigger.Conditions>
                      <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource HomeSelected}" />
                    </MultiTrigger>
                    <Trigger Property="IsKeyboardFocused" Value="True">
                      <Setter TargetName="Chrome" Property="BorderBrush" Value="{DynamicResource HomeAccent}" />
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter Property="Foreground" Value="{DynamicResource HomeMuted}" />
                      <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource HomeSoft}" />
                      <Setter Property="Cursor" Value="Arrow" />
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="FaceIconButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Width" Value="32" /><Setter Property="Height" Value="32" />
            <Setter Property="MinHeight" Value="32" /><Setter Property="Padding" Value="5" />
            <Setter Property="BorderBrush" Value="Transparent" />
            <Setter Property="ToolTipService.InitialShowDelay" Value="250" />
            <Style.Triggers><Trigger Property="Tag" Value="Selected"><Setter Property="BorderBrush" Value="{DynamicResource HomeAccent}" /></Trigger></Style.Triggers>
          </Style>
          <Style x:Key="AccessoryInlineNumber" TargetType="TextBox">
            <Setter Property="Foreground" Value="{DynamicResource HomeInk}" />
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="BorderBrush" Value="Transparent" />
            <Setter Property="BorderThickness" Value="0,0,0,1" />
            <Setter Property="Padding" Value="0,0,0,2" />
            <Setter Property="FontSize" Value="14" />
            <Style.Triggers>
              <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter Property="BorderBrush" Value="{DynamicResource HomeAccent}" /></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="{DynamicResource HomeMuted}" /></Trigger>
            </Style.Triggers>
          </Style>
          <Style x:Key="AccessoryLibraryItem" TargetType="ListBoxItem">
            <Setter Property="Foreground" Value="{DynamicResource HomeInk}" />
            <Setter Property="Padding" Value="4,3" />
            <Setter Property="HorizontalContentAlignment" Value="Stretch" />
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ListBoxItem">
                  <Border x:Name="Chrome" CornerRadius="4" Background="Transparent" BorderBrush="Transparent"
                          BorderThickness="1" Padding="{TemplateBinding Padding}">
                    <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" />
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Chrome" Property="Background" Value="{DynamicResource HomeSoft}" /></Trigger>
                    <Trigger Property="IsSelected" Value="True">
                      <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource HomeSelected}" />
                      <Setter TargetName="Chrome" Property="BorderBrush" Value="{DynamicResource HomeAccent}" />
                    </Trigger>
                    <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Chrome" Property="BorderBrush" Value="{DynamicResource HomeAccent}" /></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """;
}
