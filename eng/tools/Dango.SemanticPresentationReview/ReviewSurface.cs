using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Dango.SemanticPresentationReview;

internal sealed class ReviewSurface : Grid, IDisposable
{
    internal ReviewPane Left { get; }
    internal ReviewPane Right { get; }
    internal int CommonTimeChecks { get; private set; }
    internal double MaximumCommonCenterError { get; private set; }
    private readonly Border divider = new();

    internal ReviewSurface(ReviewInputs inputs, string scenario, string mutation = "none")
    {
        var xaml = XDocument.Load(inputs.Theme);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace names = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var brush in xaml.Descendants(presentation + "SolidColorBrush"))
        {
            var key = brush.Attribute(names + "Key")?.Value;
            if (key is not null && key.StartsWith("Home", StringComparison.Ordinal))
                Resources[key] = SystemParameters.HighContrast ?
                    key is "HomeBackground" or "HomeSoft" or "HomeSelected" ? SystemColors.WindowBrush : SystemColors.WindowTextBrush :
                    (Brush)new BrushConverter().ConvertFromString(brush.Attribute("Color")!.Value)!;
        }
        FontResources();
        SetResourceReference(BackgroundProperty, "HomeBackground");
        Left = new(inputs.Library, inputs.Traces[scenario], "视角 A", 0);
        Right = new(inputs.Library, inputs.Traces[scenario], "视角 B", .25, mutation,
            mutation == "shared-replica" ? Left.Replica : null);
        if (ReferenceEquals(Left.Replica, Right.Replica))
        {
            Left.Dispose(); Right.Dispose(); throw new GateFailure("replica-ownership");
        }
        divider.SetResourceReference(Border.BackgroundProperty, "HomeLine");
        Children.Add(Left.Surface); Children.Add(Right.Surface); Children.Add(divider);
        SizeChanged += (_, _) => SetOrientation(ActualWidth, ActualHeight);
        SetOrientation(1200, 600);
    }

    private void FontResources()
    {
        TextElement.SetFontFamily(this, new FontFamily("Segoe UI, Microsoft YaHei"));
        TextElement.SetFontSize(this, 14); TextElement.SetForeground(this, (Brush)Resources["HomeInk"]);
    }

    private void SetOrientation(double width, double height)
    {
        var vertical = height > width;
        RowDefinitions.Clear(); ColumnDefinitions.Clear();
        if (vertical)
        {
            RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = new GridLength(1) }); RowDefinitions.Add(new());
            Grid.SetRow(Left.Surface, 0); Grid.SetRow(Right.Surface, 2); Grid.SetRow(divider, 1);
            Grid.SetColumn(Left.Surface, 0); Grid.SetColumn(Right.Surface, 0); Grid.SetColumn(divider, 0);
        }
        else
        {
            ColumnDefinitions.Add(new()); ColumnDefinitions.Add(new() { Width = new GridLength(1) }); ColumnDefinitions.Add(new());
            Grid.SetColumn(Left.Surface, 0); Grid.SetColumn(Right.Surface, 2); Grid.SetColumn(divider, 1);
            Grid.SetRow(Left.Surface, 0); Grid.SetRow(Right.Surface, 0); Grid.SetRow(divider, 0);
        }
    }

    internal void Layout(double width, double height)
    {
        SetOrientation(width, height); Width = width; Height = height;
        Measure(new(width, height)); Arrange(new(0, 0, width, height)); UpdateLayout();
        Redraw();
    }

    internal void Redraw()
    {
        Left.DrawLast(); Right.DrawLast();
        if (Left.Current is not null) Left.Audit();
        if (Right.Current is not null) Right.Audit();
        CheckCommonTime();
    }

    internal void CheckCommonTime()
    {
        if (Left.Current is null || Right.Current is null || Left.Current.Tick != Right.Current.Tick) return;
        var error = Left.ActualCenters.Zip(Right.ActualCenters, (a, b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y))).Max();
        MaximumCommonCenterError = Math.Max(MaximumCommonCenterError, error); CommonTimeChecks++;
        if (!double.IsFinite(error) || error > 1e-5) throw new GateFailure("common-time-center");
        if (!Left.Current.Bodies.Select(body => body.Authority.EntityId).SequenceEqual(Right.Current.Bodies.Select(body => body.Authority.EntityId)) ||
            !Left.Current.Bodies.Select(body => body.Authority.SupportEntityId).SequenceEqual(Right.Current.Bodies.Select(body => body.Authority.SupportEntityId)))
            throw new GateFailure("common-time-membership");
    }

    internal void SaveImage(string path)
    {
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }

    public void Dispose() { Left.Dispose(); Right.Dispose(); }
}
