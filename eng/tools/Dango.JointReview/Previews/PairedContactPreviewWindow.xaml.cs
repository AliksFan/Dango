using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Physics.Presentation;

namespace Dango.JointReview.Previews;

public partial class PairedContactPreviewWindow : Window
{
    private static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60d);
    private const double SeparatedFirstX = 245;
    private const double ContactFirstX = 310;
    private const double ContactSecondX = 410;
    private const double SeparatedSecondX = 475;

    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer frameTimer;
    private Box2DPairedContactPresentationWorld world;
    private long cycle;
    private bool resourcesReleased;
    internal bool ResourcesReleased => resourcesReleased && !frameTimer.IsEnabled;
    internal void VerifyNativeFrame() => OnTick(null, EventArgs.Empty);

    public PairedContactPreviewWindow()
    {
        IsolatedPreviews.RequireLegacyNative();
        InitializeComponent();
        FirstDango.BodyFill = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(255, 226, 181));
        SecondDango.BodyFill = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(201, 226, 255));
        world = CreateWorld();
        frameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = FixedStep };
        frameTimer.Tick += OnTick;
        Closed += OnClosed;
        try { VerifyNativeFrame(); frameTimer.Start(); }
        catch { OnClosed(null, EventArgs.Empty); throw; }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var elapsed = clock.Elapsed.TotalSeconds;
        var currentCycle = (long)(elapsed / 8);
        if (currentCycle != cycle)
        {
            world.Dispose();
            world = CreateWorld();
            cycle = currentCycle;
        }

        var phaseTime = elapsed % 8;
        var approachAmount = phaseTime switch
        {
            < 1 => 0,
            < 2.5 => Ease((phaseTime - 1) / 1.5),
            < 5 => 1,
            < 6.5 => 1 - Ease((phaseTime - 5) / 1.5),
            _ => 0,
        };
        var firstX = Lerp(SeparatedFirstX, ContactFirstX, approachAmount);
        var secondX = Lerp(SeparatedSecondX, ContactSecondX, approachAmount);
        var contactActive = phaseTime is >= 1 and < 5;
        var firstInput = CreateInput("paired-proof-a", firstX);
        var secondInput = CreateInput("paired-proof-b", secondX);
        var cue = contactActive
            ? new PairedPresentationContactCue(
                [firstInput.DangoId, secondInput.DangoId],
                new LogicalVector(1, 0),
                phaseTime < 2.5 ? Ease((phaseTime - 1) / 1.5) : 1)
            : null;
        var frame = world.Step(firstInput, secondInput, cue, FixedStep);
        if (frame.First.FallbackReason != PhysicsPresentationFallbackReason.None ||
            frame.Second.FallbackReason != PhysicsPresentationFallbackReason.None)
            throw new InvalidOperationException($"旧模块 paired-contact 预览初始化或推进失败：{frame.First.FallbackReason}/{frame.Second.FallbackReason}");

        Canvas.SetLeft(FirstDango, firstX + frame.FirstRenderOffsetX);
        Canvas.SetLeft(SecondDango, secondX + frame.SecondRenderOffsetX);
        FirstDango.ApplyFrame(frame.First);
        SecondDango.ApplyFrame(frame.Second);
        PhaseText.Text = phaseTime switch
        {
            < 1 => "分离稳定",
            < 2.5 => "共同 cue：靠近",
            < 5 => "真实接触与互补挤压",
            < 6.5 => "取消 cue：释放",
            _ => "有界收束",
        };
        ContactText.Text = $"Box2D contact {frame.ContactCount}　最大节点速度 {frame.MaximumNodeSpeed:F3}　约束已释放 {frame.ConstraintsReleased}";
    }

    private static Box2DPairedContactPresentationWorld CreateWorld() =>
        new(Box2DNativeLoader.DefaultLibraryPath);

    private static PhysicsPresentationInput CreateInput(string id, double x) =>
        new(
            id,
            new LogicalRect(x, 240, 160, 120),
            DangoActionState.Idle,
            1_000,
            17,
            "paired-proof-ground",
            new LogicalVector(0, -1),
            0,
            1,
            false,
            true,
            []);

    private static double Ease(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - (2 * value));
    }

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);

    private void OnSkeletonDisplayChanged(object sender, RoutedEventArgs e)
    {
        var show = ShowSkeletonToggle.IsChecked == true;
        FirstDango.ShowDebugGeometry = show;
        SecondDango.ShowDebugGeometry = show;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        frameTimer.Stop();
        world.Dispose();
        resourcesReleased = true;
    }
}
