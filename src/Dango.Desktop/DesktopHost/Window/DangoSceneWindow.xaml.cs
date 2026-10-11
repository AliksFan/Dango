using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Forms = System.Windows.Forms;
using WpfCanvas = System.Windows.Controls.Canvas;

namespace Dango.Desktop.DesktopHost.StandardWindow;

public partial class DangoSceneWindow : System.Windows.Window, IDesktopHostControl, IKeyboardControl, IDesktopHostSwitchGuard, IPhysicsDebugControl, IDangoPopulationControl, IDangoPhysicsTuningControl
{
    private readonly DesktopSceneSession sceneSession;
    private readonly StackedDangoSceneRuntime motionRuntime;
    private readonly LightInteractionSession lightInteraction;
    private readonly HostFrameScheduler motionScheduler;
    private KeyboardControlSettings keyboardSettings = KeyboardControlSettings.CreateDefault();
    private DirectionalControlIntent currentIntent;
    private LogicalPoint dragPointer;
    private bool physicalDragging;
    private DeformableDangoVisual? capturedDango;
    private bool isInteractionEnabled = true;
    private bool allowClose;
    private bool loaded;
    private SceneWindowSettings placement = SceneWindowSettings.CreateDefault();

    public DangoSceneWindow(
        DesktopSceneSession sceneSession,
        StackedDangoSceneRuntime motionRuntime)
    {
        this.sceneSession = sceneSession ?? throw new ArgumentNullException(nameof(sceneSession));
        this.motionRuntime = motionRuntime ?? throw new ArgumentNullException(nameof(motionRuntime));
        lightInteraction = new(motionRuntime);
        InitializeComponent();
        DangoVisual.ShowHat = false;
        MiddleDangoVisual.ShowHat = false;
        TopDangoVisual.ShowHat = false;
        MiddleDangoVisual.BodyFill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(201, 226, 255));
        TopDangoVisual.BodyFill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(221, 204, 248));
        motionScheduler = new(motionRuntime, PrepareMotionFrame, CreateMotionInput, frame =>
        {
            ApplyPresentationFrame(frame);
            sceneSession.Controller?.ObservePresentationFrame();
        });
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        StateChanged += OnStateChanged;
        Deactivated += OnDeactivated;
        DpiChanged += (_, _) => { if (motionRuntime.LightInteractionsEnabled) ClearInput(); };
        LocationChanged += (_, _) => { if (motionRuntime.LightInteractionsEnabled) ClearInput(); };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        Closing += OnClosing;
        motionRuntime.InputCancelled += ClearHostInput;
    }

    public DesktopHostSnapshot GetSnapshot() => new(
        "Window",
        IsVisible && WindowState != WindowState.Minimized,
        isInteractionEnabled,
        motionRuntime.LayoutWarning ?? "普通窗口模式（新物理接入待人工验收）",
        motionRuntime.LastError);

    public int DangoCount => motionRuntime.DangoCount;

    public bool AddDango() => motionRuntime.AddDangoInViewport(
        new(0, 0, Math.Max(480, SceneCanvas.ActualWidth), Math.Max(360, SceneCanvas.ActualHeight)));

    public bool RemoveDango() => motionRuntime.RemoveDango();

    public DangoSelfPhysicsParameters PhysicsParameters => motionRuntime.Parameters;

    public void ApplyPhysicsParameters(DangoSelfPhysicsParameters parameters) =>
        motionRuntime.ApplyPhysicsParameters(parameters);

    public void RestoreDefaultPhysicsParameters() => motionRuntime.RestoreDefaultPhysicsParameters();

    public void RegenerateDango() => motionRuntime.RegenerateDango();

    public void SetVisibility(bool isVisible)
    {
        if (isVisible)
        {
            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            StartMotion();
            return;
        }

        ClearInput();
        StopMotion();
        Hide();
    }

    public void SetInteractionEnabled(bool isEnabled)
    {
        if (!isEnabled)
        {
            ClearInput();
            EndPhysicalDrag();
        }

        isInteractionEnabled = isEnabled;
        motionRuntime.UpdateQuietIdleHost(this, isEnabled && IsVisible && WindowState != WindowState.Minimized);
        DangoVisual.IsHitTestVisible = isEnabled;
        MiddleDangoVisual.IsHitTestVisible = isEnabled;
        TopDangoVisual.IsHitTestVisible = isEnabled;
    }

    public KeyboardControlSettings GetKeyboardControlSettings() => keyboardSettings;

    public string? GetSwitchBlockReason() => motionRuntime.IsDragging || sceneSession.Controller?.IsDragging == true
        ? "团子拖动结束后才能切换显示模式。"
        : null;

    public void ApplyKeyboardControlSettings(KeyboardControlSettings settings)
    {
        KeyboardControlSettingsValidator.Validate(settings);
        ClearInput();
        keyboardSettings = settings;
    }

    public string CreateDiagnosticSummary()
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        return string.Join(
            Environment.NewLine,
            "Dango 普通窗口诊断",
            $"运行时：.NET {Environment.Version}",
            $"系统：{RuntimeInformation.OSDescription}",
            $"进程架构：{RuntimeInformation.ProcessArchitecture}",
            $"窗口显示：{GetSnapshot().IsVisible}",
            $"窗口状态：{WindowState}",
            $"窗口逻辑尺寸：{ActualWidth:F0} × {ActualHeight:F0}",
            $"DPI 比例：{dpi.DpiScaleX:F2} × {dpi.DpiScaleY:F2}",
            $"允许交互：{isInteractionEnabled}",
            $"键盘控制：{(keyboardSettings.IsEnabled ? "开启" : "关闭")}",
            $"键位方案：{keyboardSettings.Scheme}",
            $"物理世界运行：{motionRuntime.IsRunning}",
            motionRuntime.CreateDiagnosticSummary());
    }

    public void RestorePlacement(SceneWindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        placement = settings;
        Width = settings.Width;
        Height = settings.Height;

        var screen = Forms.Screen.AllScreens.FirstOrDefault(
            candidate => string.Equals(candidate.DeviceName, settings.MonitorKey, StringComparison.Ordinal))
            ?? Forms.Screen.PrimaryScreen;
        if (screen is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var workArea = screen.WorkingArea;
        var workLeft = workArea.Left / dpi.DpiScaleX;
        var workTop = workArea.Top / dpi.DpiScaleY;
        var workWidth = workArea.Width / dpi.DpiScaleX;
        var workHeight = workArea.Height / dpi.DpiScaleY;
        var requestedLeft = workLeft + (settings.NormalizedCenterX * workWidth) - (Width / 2d);
        var requestedTop = workTop + (settings.NormalizedCenterY * workHeight) - (Height / 2d);
        Left = Math.Clamp(requestedLeft, workLeft, Math.Max(workLeft, workLeft + workWidth - Width));
        Top = Math.Clamp(requestedTop, workTop, Math.Max(workTop, workTop + workHeight - Height));
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    public SceneWindowSettings CapturePlacement()
    {
        if (!loaded)
        {
            return placement;
        }

        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var screen = handle == nint.Zero ? Forms.Screen.PrimaryScreen : Forms.Screen.FromHandle(handle);
        if (screen is null || bounds.Width < MinWidth || bounds.Height < MinHeight)
        {
            return placement;
        }

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var workArea = screen.WorkingArea;
        var workLeft = workArea.Left / dpi.DpiScaleX;
        var workTop = workArea.Top / dpi.DpiScaleY;
        var workWidth = workArea.Width / dpi.DpiScaleX;
        var workHeight = workArea.Height / dpi.DpiScaleY;
        placement = new SceneWindowSettings(
            bounds.Width,
            bounds.Height,
            screen.DeviceName,
            Math.Clamp((bounds.Left + (bounds.Width / 2d) - workLeft) / workWidth, 0, 1),
            Math.Clamp((bounds.Top + (bounds.Height / 2d) - workTop) / workHeight, 0, 1));
        return placement;
    }

    public void PrepareForExit()
    {
        allowClose = true;
        ClearInput();
        StopMotion();
        motionRuntime.InputCancelled -= ClearHostInput;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        loaded = true;
        StartMotion();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (loaded && IsVisible && WindowState != WindowState.Minimized)
        {
            AttachSurface();
            ApplyAuthoritativePosition();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            ClearInput();
            StopMotion();
        }
        else if (IsVisible)
        {
            StartMotion();
        }
    }

    private void OnDeactivated(object? sender, EventArgs e) => ClearInput();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose)
        {
            return;
        }

        e.Cancel = true;
        ClearInput();
        StopMotion();
        Hide();
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClearInput();
            e.Handled = true;
            return;
        }
        if (!e.IsRepeat && TrySetKey(e.Key, true))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (TrySetKey(e.Key, false))
        {
            e.Handled = true;
        }
    }

    private bool TrySetKey(Key key, bool isDown)
    {
        if (!keyboardSettings.IsEnabled || !isInteractionEnabled || !IsActive || WindowState == WindowState.Minimized)
        {
            return false;
        }

        if (!KeyboardControlKeyPolicy.TryMap(KeyInterop.VirtualKeyFromKey(key), keyboardSettings.Scheme, out var mapped))
        {
            return false;
        }

        currentIntent = currentIntent with
        {
            Left = mapped == DirectionalControlKey.Left ? isDown : currentIntent.Left,
            Right = mapped == DirectionalControlKey.Right ? isDown : currentIntent.Right,
            Jump = mapped == DirectionalControlKey.Jump ? isDown : currentIntent.Jump,
            Squash = mapped == DirectionalControlKey.Squash ? isDown : currentIntent.Squash,
        };
        return true;
    }

    private void ClearInput()
    {
        currentIntent = DirectionalControlIntent.None;
        if (motionScheduler.OwnsRuntime || motionRuntime.FrameDriver is null) motionRuntime.CancelInput();
        else ClearHostInput();
    }

    private void ClearHostInput()
    {
        lightInteraction.Cancel();
        currentIntent = DirectionalControlIntent.None;
        physicalDragging = false;
        var captured = capturedDango;
        capturedDango = null;
        if (captured?.IsMouseCaptured == true) captured.ReleaseMouseCapture();
    }

    private void StartMotion()
    {
        if (motionScheduler.IsRunning) return;
        if (!loaded || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        motionScheduler.VerifyCanStart();
        AttachSurface();
        ApplyAuthoritativePosition();
        motionRuntime.Start();
        motionRuntime.AttachQuietIdleHost(this, isInteractionEnabled);
        motionScheduler.Start();
    }

    private void StopMotion()
    {
        var mayStopRuntime = motionScheduler.OwnsRuntime || motionRuntime.FrameDriver is null;
        motionScheduler.Stop();
        if (mayStopRuntime) motionRuntime.Stop();
    }

    private void AttachSurface()
    {
        var width = Math.Max(480, SceneCanvas.ActualWidth);
        var height = Math.Max(360, SceneCanvas.ActualHeight);
        motionRuntime.SetViewport(new(0, 0, width, height));
        sceneSession.AttachSurface(new LogicalRect(0, 0, width, height), NowMilliseconds());
    }

    private bool PrepareMotionFrame()
    {
        var controller = sceneSession.Controller;
        if (!IsVisible || WindowState == WindowState.Minimized || controller is null || !motionRuntime.IsRunning)
        {
            return false;
        }

        motionRuntime.SetDirectionalControl(currentIntent);
        motionRuntime.UpdateQuietIdleHost(this, isInteractionEnabled);
        var screen = Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        motionRuntime.UpdateWanderScreenWidth(this, screen.WorkingArea.Width / System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX);
        if (lightInteraction.IsActive)
            lightInteraction.RefreshButtons(NowMilliseconds(), NativeMethods.IsInputKeyPressed(0x10), leftDown: true);
        return true;
    }

    private PhysicsPresentationInput CreateMotionInput()
    {
        var controller = sceneSession.Controller!;
        return PhysicsPresentationInput.FromSnapshot(controller.Snapshot, controller.SquashAmount,
            controller.GroundFrictionCue, controller.FacingDirection, null);
    }

    private void OnDangoMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!isInteractionEnabled || sceneSession.Controller is null || motionRuntime.LastError is not null ||
            sender is not DeformableDangoVisual { Tag: DangoEntityId id } visual || !motionRuntime.EntityIds.Contains(id))
        {
            return;
        }

        if (motionRuntime.LightInteractionsEnabled)
        {
            if (!IsActive) return;
            var point = ToLogicalPoint(e.GetPosition(SceneCanvas));
            e.Handled = lightInteraction.TryBegin(id, point, NowMilliseconds(), NativeMethods.IsInputKeyPressed(0x10),
                () => { capturedDango = visual; return visual.CaptureMouse(); },
                () =>
                {
                    if (capturedDango == visual) capturedDango = null;
                    if (visual.IsMouseCaptured) visual.ReleaseMouseCapture();
                },
                () => IsVisible && IsActive && isInteractionEnabled && WindowState != WindowState.Minimized &&
                    NativeMethods.IsInputKeyPressed(0x01));
            return;
        }
        ClearInput();
        dragPointer = ToLogicalPoint(e.GetPosition(SceneCanvas));
        motionRuntime.BeginPhysicalDrag(id, dragPointer);
        physicalDragging = true;
        capturedDango = visual;
        if (!visual.CaptureMouse()) EndPhysicalDrag();
        e.Handled = true;
    }

    private void OnDangoMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (motionRuntime.LightInteractionsEnabled)
        {
            if (!lightInteraction.IsActive) return;
            if (e.LeftButton != MouseButtonState.Pressed) lightInteraction.Cancel();
            else lightInteraction.Move(ToLogicalPoint(e.GetPosition(SceneCanvas)), NowMilliseconds(), NativeMethods.IsInputKeyPressed(0x10));
            e.Handled = true;
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed || !physicalDragging)
        {
            return;
        }

        dragPointer = ToLogicalPoint(e.GetPosition(SceneCanvas));
        motionRuntime.MovePhysicalDrag(dragPointer);
        e.Handled = true;
    }

    private void OnDangoMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (motionRuntime.LightInteractionsEnabled)
        {
            if (!IsVisible || !IsActive || !isInteractionEnabled || WindowState == WindowState.Minimized)
                lightInteraction.Cancel();
            else lightInteraction.End(ToLogicalPoint(e.GetPosition(SceneCanvas)), NowMilliseconds(), NativeMethods.IsInputKeyPressed(0x10));
            e.Handled = true;
            return;
        }
        EndPhysicalDrag();
        e.Handled = true;
    }

    private void OnDangoLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (motionRuntime.LightInteractionsEnabled)
        {
            // Ignore the old capture's callback after normal Up or successful recapture.
            if (lightInteraction.IsActive && sender is DeformableDangoVisual visual && capturedDango == visual && !visual.IsMouseCaptured)
                lightInteraction.Cancel();
            return;
        }
        EndPhysicalDrag();
    }

    private void ApplyAuthoritativePosition()
    {
        if (sceneSession.Snapshot is not { } snapshot)
        {
            return;
        }

        // A render callback may have no fixed step. Only a complete presentation
        // frame may replace an existing body/hat pose, including after host changes.
        if (motionRuntime.LastFrame is { } frame)
        {
            ApplyPresentationFrame(frame);
        }
        else
        {
            WpfCanvas.SetLeft(DangoVisual, snapshot.Dango.VisualBounds.X);
            WpfCanvas.SetTop(DangoVisual, snapshot.Dango.VisualBounds.Y);
        }
        WpfCanvas.SetLeft(IslandVisual, snapshot.Island.VisualBounds.X);
        WpfCanvas.SetTop(IslandVisual, snapshot.Island.VisualBounds.Y);
    }

    public void SetDebugSkeletonVisible(bool isVisible)
    {
        DangoVisual.ShowDebugGeometry = isVisible;
        MiddleDangoVisual.ShowDebugGeometry = isVisible;
        TopDangoVisual.ShowDebugGeometry = isVisible;
    }

    public bool IsDebugSkeletonVisible => DangoVisual.ShowDebugGeometry;

    private void ApplyPresentationFrame(StackedPhysicsPresentationFrame frame)
    {
        DangoHostFrameBinding.Apply(DangoVisual, DangoHat, frame, 0, appearance: motionRuntime.DisplayHatAppearance, hatSize: motionRuntime.DisplayHatSize, bodyFillOverride: motionRuntime.BodyPreviewFill, eyeFillOverride: motionRuntime.EyePreviewFill, appearanceEntity: motionRuntime.AppearanceEntity, runtime: motionRuntime);
        DangoHostFrameBinding.Apply(MiddleDangoVisual, MiddleDangoHat, frame, 1, appearance: motionRuntime.DisplayHatAppearance, hatSize: motionRuntime.DisplayHatSize, bodyFillOverride: motionRuntime.BodyPreviewFill, eyeFillOverride: motionRuntime.EyePreviewFill, appearanceEntity: motionRuntime.AppearanceEntity, runtime: motionRuntime);
        DangoHostFrameBinding.Apply(TopDangoVisual, TopDangoHat, frame, 2, appearance: motionRuntime.DisplayHatAppearance, hatSize: motionRuntime.DisplayHatSize, bodyFillOverride: motionRuntime.BodyPreviewFill, eyeFillOverride: motionRuntime.EyePreviewFill, appearanceEntity: motionRuntime.AppearanceEntity, runtime: motionRuntime);
    }


    private static LogicalPoint ToLogicalPoint(System.Windows.Point point) => new(point.X, point.Y);

    private void EndPhysicalDrag()
    {
        if (!physicalDragging)
        {
            return;
        }
        physicalDragging = false;
        motionRuntime.EndPhysicalDrag();
        var captured = capturedDango;
        capturedDango = null;
        if (captured?.IsMouseCaptured == true) captured.ReleaseMouseCapture();
    }

    private static long NowMilliseconds() => (long)(Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency);
}
