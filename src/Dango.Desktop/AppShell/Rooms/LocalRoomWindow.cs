using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;
using Button = System.Windows.Controls.Button;
using Grid = System.Windows.Controls.Grid;
using StackPanel = System.Windows.Controls.StackPanel;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;

namespace Dango.Desktop.AppShell.Rooms;

internal sealed class LocalRoomWindow : Window
{
    private readonly LocalRoomSceneDriver driver;
    private readonly LocalRoomMenuView menu = new()
    {
        Name = "RoomMenu",
        Visibility = Visibility.Collapsed,
        ShowCameraSettings = true,
    };
    private bool allowClose;
    private bool cameraPanActive;
    private Point lastCameraPanPoint;
    private readonly ResourceDictionary? homeResources;
    private readonly Border furnitureLayer = new()
    {
        Name = "RoomFurnitureLayer", Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 52, 16, 16),
    };
    private OrdinaryRoomFurnitureEditorPanel? furnitureEditor;
    private OrdinaryRoomFurnitureTool furnitureTool;
    private bool furnitureSelected;
    private bool furnitureDragActive;
    private bool furnitureGloveActive;
    private FurnitureAnchor furnitureDragOffset;
    private FurnitureAnchor? requestedFurnitureAnchor;
    private long nextFurniturePreview;
    private bool furniturePreviewCanConfirm;
    private RoomCameraSnapshot? previousFurnitureCamera;
    private string? furnitureCommandError;
    internal LocalRoomSceneView Scene { get; } = new();
    internal bool IsMenuOpen => menu.Visibility == Visibility.Visible;
    internal bool IsCameraPanActive => cameraPanActive;
    internal bool IsFurnitureEditing => furnitureLayer.Visibility == Visibility.Visible;
    internal bool IsFurnitureGestureActive => furnitureDragActive || furnitureGloveActive;
    internal event Action? ReturnRequested;
    internal event Action? HideRequested;
    internal event Action? RecoverRequested;
    internal event Action? ControlCenterRequested;
    internal event Action<LocalRoomPresentation>? PresentationRequested;
    internal LocalRoomWindow(LocalRoomSceneDriver driver, ResourceDictionary? homeResources = null)
    {
        this.driver = driver;
        this.homeResources = homeResources;
        Scene.EnableWindowCamera();
        Title = "Dango · 普通房（本地单人）";
        Width = 960; Height = 600; MinWidth = 640; MinHeight = 420;
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        { CaptionHeight = 36, ResizeBorderThickness = new Thickness(7), CornerRadius = new CornerRadius(14), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new Grid();
        root.Children.Add(Scene);
        var caption = new Grid { Height = 36, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent };
        var windowControls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        windowControls.Children.Add(CreateWindowControl("RoomHideButton", "—", "隐藏房间"));
        windowControls.Children.Add(CreateWindowControl("RoomCloseButton", "×", "隐藏房间，托盘恢复"));
        caption.Children.Add(windowControls);
        root.Children.Add(caption);
        WindowChrome.SetIsHitTestVisibleInChrome(furnitureLayer, true);
        root.Children.Add(furnitureLayer);

        menu.ReturnRequested += () => RunMenuAction(() => ReturnRequested?.Invoke());
        menu.ControlCenterRequested += () => RunMenuAction(() => ControlCenterRequested?.Invoke());
        menu.PresentationRequested += presentation => RunMenuAction(() => PresentationRequested?.Invoke(presentation));
        menu.RecoverRequested += () => RunMenuAction(() => RecoverRequested?.Invoke());
        menu.FurnitureRequested += () => { SetMenuOpen(false); BeginFurnitureEditing(); };
        menu.CameraModeRequested += mode => Scene.SelectCameraMode(mode);
        WindowChrome.SetIsHitTestVisibleInChrome(menu, true);
        menu.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, e) =>
        { Scene.ClearWheel(); if (!menu.IsCameraDropDownOpen) e.Handled = true; }), true);
        root.Children.Add(menu);
        Content = root;
        Scene.ViewChanged += RefreshLabel;
        Scene.SizeChanged += (_, _) => RefreshLabel();
        Deactivated += (_, _) => { CancelFurnitureGesture(); driver.ClearInput(); ReleaseScenePointerCapture(); };
        LostKeyboardFocus += (_, _) => { if (!Scene.IsKeyboardFocusWithin) { CancelCameraPan(); CancelFurnitureGesture(); driver.ClearInput(); } };
        Scene.MouseWheel += OnSceneWheel;
        Scene.MouseLeftButtonDown += OnScenePointerDown;
        Scene.MouseMove += OnScenePointerMove;
        Scene.MouseLeftButtonUp += OnScenePointerUp;
        Scene.MouseRightButtonDown += OnSceneCameraPanDown;
        Scene.MouseRightButtonUp += OnSceneCameraPanUp;
        Scene.InputInvalidated += CancelAllSceneGestures;
        Scene.LostMouseCapture += (_, _) =>
        {
            if (cameraPanActive) CancelCameraPan();
            if (IsFurnitureGestureActive) CancelFurnitureGesture();
            if (driver.IsPointerActive) driver.ClearInput();
        };
        driver.PointerCancelled += ReleaseScenePointerCapture;
        Scene.KeyDown += OnSceneKeyDown;
        Scene.KeyUp += (_, e) => { if (IsActive && Scene.IsKeyboardFocusWithin && !Scene.InputBlocked) e.Handled = driver.Key(e.Key, false, false); };
        PreviewKeyDown += OnPreviewKeyDown;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { CancelFurnitureGesture(); CancelFurniturePreview(); driver.ClearInput(); ReleaseScenePointerCapture(); SetMenuOpen(false); }
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        { driver.PointerCancelled -= ReleaseScenePointerCapture; Scene.InputInvalidated -= CancelAllSceneGestures; Scene.FrameApplied -= RefreshFurnitureEditor; };
        Scene.FrameApplied += RefreshFurnitureEditor;
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) HideRequested?.Invoke(); };
        RefreshLabel();
    }

    internal void PrepareForExit() => allowClose = true;
    private nint WindowMessage(nint hwnd, int message, nint w, nint l, ref bool handled)
    { if (message == 0x02E0) { CancelAllSceneGestures(); driver.ClearInput(); } return 0; }
    internal void FocusScene()
    {
        Activate();
        if (IsMenuOpen)
        {
            if (!menu.IsKeyboardFocusWithin) menu.Focus();
        }
        else Scene.Focus();
    }
    internal void OpenMenu() { if (IsVisible) { Activate(); SetMenuOpen(true); } }
    internal void CloseMenu() => SetMenuOpen(false);
    internal bool WheelEligible => IsActive && IsVisible && !Scene.InputBlocked && Scene.IsMouseOver &&
        NativeMethods.IsLocalRoomForeground(new WindowInteropHelper(this).Handle);
    private void OnSceneWheel(object sender, MouseWheelEventArgs e)
    {
        if (!WheelEligible || Keyboard.Modifiers != ModifierKeys.None) { Scene.ClearWheel(); return; }
        Scene.ApplyWheel(e.Delta);
        e.Handled = true;
        RefreshLabel();
    }

    private void OnSceneKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsActive || !Scene.IsKeyboardFocusWithin || Scene.InputBlocked || Keyboard.Modifiers != ModifierKeys.None) return;
        if (IsFurnitureEditing && e.Key is Key.Up or Key.Down or Key.Left or Key.Right)
        {
            e.Handled = true;
            NudgeFurniture(e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0,
                e.Key == Key.Down ? -1 : e.Key == Key.Up ? 1 : 0);
            return;
        }
        if (IsFurnitureEditing && e.Key == Key.Enter)
        {
            e.Handled = true;
            if (!e.IsRepeat && FurnitureCanInteract && furniturePreviewCanConfirm && requestedFurnitureAnchor is null && driver.Furniture is { CanEdit: true, Busy: false } furniture)
                StartFurnitureCommand(furniture.ConfirmAsync);
            return;
        }
        e.Handled = driver.Key(e.Key, true, e.IsRepeat);
    }

    private bool PointerEligible => IsActive && IsVisible && Scene.HasHealthyFrame &&
        Scene.IsKeyboardFocusWithin && !Scene.InputBlocked &&
        NativeMethods.IsLocalRoomForeground(new WindowInteropHelper(this).Handle);

    private void OnScenePointerDown(object sender, MouseButtonEventArgs e)
    {
        if (cameraPanActive) { CancelCameraPan(); e.Handled = true; return; }
        Scene.Focus();
        if (IsFurnitureEditing)
        {
            e.Handled = true;
            if (!PointerEligible || !FurnitureCanInteract || Keyboard.Modifiers != ModifierKeys.None) return;
            CancelFurnitureGesture();
            var point = e.GetPosition(Scene);
            if (furnitureTool == OrdinaryRoomFurnitureTool.Glove)
            {
                furnitureGloveActive = driver.BeginFurnitureGlove(point);
                if (furnitureGloveActive && !Scene.CaptureMouse()) CancelFurnitureGesture();
            }
            else
            {
                furnitureSelected = Scene.FurnitureAt(point) || IsFurniturePreviewHit(point);
                if (furnitureSelected && (driver.Furniture?.Preview ?? driver.Furniture?.Layout.Platform) is { } selected)
                {
                    var anchor = Scene.ScreenToFurnitureAnchor(point);
                    furnitureDragOffset = new(selected.Anchor.X - anchor.X, selected.Anchor.Y - anchor.Y);
                    furnitureDragActive = Scene.CaptureMouse();
                }
            }
            RefreshFurnitureEditor(); return;
        }
        if (!PointerEligible || !TryGetPointerShift(out var shift)) { driver.ClearInput(); return; }
        if (!driver.PointerDown(e.GetPosition(Scene), shift)) return;
        e.Handled = true;
        if (!Scene.CaptureMouse()) driver.CancelPointer();
    }

    private void OnScenePointerMove(object sender, MouseEventArgs e)
    {
        if (cameraPanActive)
        {
            e.Handled = MoveCameraPan(e.GetPosition(Scene), e.RightButton == MouseButtonState.Pressed, Keyboard.Modifiers);
            return;
        }
        if (IsFurnitureGestureActive)
        {
            e.Handled = true;
            if (e.LeftButton != MouseButtonState.Pressed || !PointerEligible || !FurnitureCanInteract || Keyboard.Modifiers != ModifierKeys.None)
            { CancelFurnitureGesture(); return; }
            var point = e.GetPosition(Scene);
            if (furnitureGloveActive) { if (!driver.MoveFurnitureGlove(point)) CancelFurnitureGesture(); }
            else
            {
                var anchor = Scene.ScreenToFurnitureAnchor(point);
                QueueFurniturePreview(new(anchor.X + furnitureDragOffset.X, anchor.Y + furnitureDragOffset.Y));
            }
            return;
        }
        if (!driver.IsPointerActive) return;
        if (e.LeftButton != MouseButtonState.Pressed || !PointerEligible || !TryGetPointerShift(out var shift))
        { driver.ClearInput(); return; }
        e.Handled = true;
        if (!driver.PointerMove(e.GetPosition(Scene), shift)) driver.CancelPointer();
    }

    private void OnScenePointerUp(object sender, MouseButtonEventArgs e)
    {
        if (IsFurnitureGestureActive)
        {
            e.Handled = true;
            if (furnitureGloveActive && PointerEligible) driver.EndFurnitureGlove(e.GetPosition(Scene));
            furnitureDragActive = furnitureGloveActive = false;
            driver.CancelFurnitureGlove();
            if (Mouse.Captured == Scene) Scene.ReleaseMouseCapture();
            RefreshFurnitureEditor(); return;
        }
        if (!driver.IsPointerActive) return;
        e.Handled = true;
        if (PointerEligible && TryGetPointerShift(out var shift))
        {
            if (!driver.PointerUp(e.GetPosition(Scene), shift)) driver.CancelPointer();
        }
        else driver.ClearInput();
        ReleaseScenePointerCapture();
    }

    private void ReleaseScenePointerCapture()
    {
        cameraPanActive = false;
        if (Mouse.Captured == Scene) Scene.ReleaseMouseCapture();
    }

    private void OnSceneCameraPanDown(object sender, MouseButtonEventArgs e)
    {
        if (!Scene.InputBlocked) Scene.Focus();
        e.Handled = TryBeginCameraPan(e.GetPosition(Scene), Keyboard.Modifiers, e.LeftButton == MouseButtonState.Pressed);
    }

    private void OnSceneCameraPanUp(object sender, MouseButtonEventArgs e)
    {
        if (!cameraPanActive) return;
        MoveCameraPan(e.GetPosition(Scene), true, Keyboard.Modifiers);
        EndCameraPan();
        e.Handled = true;
    }

    internal bool TryBeginCameraPan(Point sceneDip, ModifierKeys modifiers, bool leftPressed)
    {
        if (cameraPanActive || leftPressed || driver.IsPointerActive || IsFurnitureGestureActive || !CameraPanEligible(modifiers) ||
            !IsCameraPanStartPoint(sceneDip) || Mouse.Captured is { } captured && captured != Scene) return false;
        if (!Scene.CaptureMouse()) return false;
        if (!CameraPanEligible(modifiers) || driver.IsPointerActive || Mouse.Captured != Scene)
        {
            if (Mouse.Captured == Scene) Scene.ReleaseMouseCapture();
            return false;
        }
        lastCameraPanPoint = sceneDip;
        cameraPanActive = true;
        return true;
    }

    internal bool MoveCameraPan(Point sceneDip, bool rightPressed, ModifierKeys modifiers)
    {
        if (!cameraPanActive) return false;
        if (!rightPressed || !CameraPanEligible(modifiers) || Mouse.Captured != Scene ||
            !double.IsFinite(sceneDip.X) || !double.IsFinite(sceneDip.Y))
        { CancelCameraPan(); return false; }
        var delta = sceneDip - lastCameraPanPoint;
        lastCameraPanPoint = sceneDip;
        Scene.PanCamera(delta.X, delta.Y);
        return true;
    }

    internal bool EndCameraPan()
    {
        if (!cameraPanActive) return false;
        CancelCameraPan(); // Normal completion does not clear directional controls.
        return true;
    }

    private void CancelCameraPan()
    {
        if (!cameraPanActive) return;
        cameraPanActive = false;
        if (Mouse.Captured == Scene) Scene.ReleaseMouseCapture();
    }

    private bool CameraPanEligible(ModifierKeys modifiers) => PointerEligible && driver.OwnsRuntime &&
        Scene.Camera.CameraMode == RoomCameraMode.Manual && modifiers is ModifierKeys.None or ModifierKeys.Shift;

    private bool IsCameraPanStartPoint(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y) &&
        point.X >= 7 && point.X < Scene.ActualWidth - 7 && point.Y >= 36 && point.Y < Scene.ActualHeight - 7;

    private static bool TryGetPointerShift(out bool shift)
    {
        var modifiers = Keyboard.Modifiers;
        shift = modifiers == ModifierKeys.Shift;
        return modifiers is ModifierKeys.None or ModifierKeys.Shift;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsActive || !IsKeyboardFocusWithin || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.Escape)
        {
            if (!e.IsRepeat && !(IsMenuOpen && menu.CloseCameraDropDown()))
            {
                if (!IsMenuOpen && IsFurnitureEditing && (IsFurnitureGestureActive || cameraPanActive || requestedFurnitureAnchor is not null || driver.Furniture?.HasPreview == true))
                { CancelAllSceneGestures(); CancelFurniturePreview(); }
                else SetMenuOpen(!IsMenuOpen);
            }
            e.Handled = true;
        }
        else if (IsMenuOpen && e.Key is Key.A or Key.D or Key.S)
        {
            driver.ClearInput();
            e.Handled = true;
        }
    }

    private void SetMenuOpen(bool open)
    {
        if (open == IsMenuOpen) return;
        driver.ClearInput();
        CancelFurnitureGesture();
        ReleaseScenePointerCapture();
        Scene.ClearWheel();
        Scene.InputBlocked = open;
        Scene.IsHitTestVisible = !open;
        menu.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open) menu.Focus();
        else if (IsVisible && IsActive) Scene.Focus();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        CancelFurnitureGesture();
        driver.ClearInput();
        ReleaseScenePointerCapture();
        if (!allowClose) { e.Cancel = true; HideRequested?.Invoke(); }
    }
    private void RefreshLabel()
    {
        menu.RefreshStatus(Scene);
        RefreshFurnitureAvailability();
        Scene.ToolTip = "A / D 移动 · S 轻压 · 空格跳跃 · 滚轮调视距 · Shift + 左键横移抚摸 · Esc 菜单";
        if (Scene.Camera.CameraMode == RoomCameraMode.Manual) Scene.ToolTip += " · 右键拖动视角";
    }

    internal bool BeginFurnitureEditing()
    {
        if (IsFurnitureEditing) return true;
        if (!IsVisible || !Scene.HasHealthyFrame || !driver.IsReady || driver.Furniture is not { Busy: false } admissionFurniture ||
            (!admissionFurniture.CanEdit && !admissionFurniture.CanRecover) || !admissionFurniture.TryEnterEditing())
        { RefreshFurnitureAvailability(); return false; }
        SetMenuOpen(false);
        CancelAllSceneGestures(); driver.ClearInput();
        previousFurnitureCamera = Scene.CaptureFurnitureCamera();
        Scene.SelectCameraMode(RoomCameraMode.Manual);
        if (furnitureEditor is null)
        {
            furnitureEditor = new(homeResources);
            furnitureLayer.Child = furnitureEditor;
            furnitureEditor.ToolRequested += ChangeFurnitureTool;
            furnitureEditor.NudgeRequested += (x, y) => { NudgeFurniture(x, y); Scene.Focus(); };
            furnitureEditor.AddRequested += AddFurniture;
            furnitureEditor.DeleteRequested += () =>
            {
                if (FurnitureCanInteract && furnitureSelected && driver.Furniture is { CanEdit: true, Busy: false } furniture)
                    StartFurnitureCommand(furniture.DeleteAsync);
            };
            furnitureEditor.ConfirmRequested += () =>
            {
                if (FurnitureCanInteract && furniturePreviewCanConfirm && requestedFurnitureAnchor is null && driver.Furniture is { CanEdit: true, Busy: false } furniture)
                    StartFurnitureCommand(furniture.ConfirmAsync);
            };
            furnitureEditor.CancelRequested += CancelFurniturePreview;
            furnitureEditor.ExitRequested += () => EndFurnitureEditing();
            furnitureEditor.RecoverRequested += () =>
            {
                if (driver.IsReady && Scene.HasHealthyFrame && driver.Furniture is { CanRecover: true, Busy: false } furniture)
                    StartFurnitureCommand(furniture.RecoverAsync);
            };
        }
        furnitureLayer.Visibility = Visibility.Visible;
        furnitureTool = OrdinaryRoomFurnitureTool.Select;
        furnitureSelected = false;
        furnitureCommandError = null;
        RefreshFurnitureEditor(); Scene.Focus(); return true;
    }

    internal bool EndFurnitureEditing()
    {
        if (!IsFurnitureEditing) return true;
        if (driver.Furniture?.Busy == true) return false;
        CancelAllSceneGestures(); driver.ClearInput(); CancelFurniturePreview();
        furnitureSelected = false;
        furnitureLayer.Visibility = Visibility.Collapsed;
        Scene.SetFurnitureSelected(false);
        Scene.Cursor = Cursors.Arrow;
        if (previousFurnitureCamera is { } snapshot) Scene.RestoreFurnitureCamera(snapshot);
        previousFurnitureCamera = null;
        RefreshLabel();
        if (IsVisible && IsActive && !IsMenuOpen) Scene.Focus();
        return true;
    }

    private bool FurnitureCanInteract => IsFurnitureEditing && !IsMenuOpen && Scene.HasHealthyFrame &&
        driver.IsReady && driver.Furniture is { CanEdit: true, Busy: false };

    private void ChangeFurnitureTool(OrdinaryRoomFurnitureTool tool)
    {
        if (!FurnitureCanInteract) return;
        CancelAllSceneGestures(); driver.ClearInput();
        furnitureTool = tool;
        RefreshFurnitureEditor(); Scene.Focus();
    }

    private void AddFurniture()
    {
        if (!FurnitureCanInteract || driver.Furniture is not { } furniture) return;
        CancelAllSceneGestures(); driver.ClearInput();
        furnitureTool = OrdinaryRoomFurnitureTool.Select;
        furnitureSelected = true;
        if (furniture.Layout.Platform is null && !furniture.HasPreview)
        {
            // The overlay does not resize the world. Reveal the official default
            // placement by moving only the edit camera, never the Dango or anchor.
            var defaultAnchor = FurnitureLayoutContract.OrdinaryRoom.DefaultAnchor;
            var screen = Scene.AnchorToScreen(defaultAnchor);
            var availableWidth = Math.Max(80, Scene.ActualWidth - furnitureLayer.ActualWidth - furnitureLayer.Margin.Right);
            Scene.PanCamera(availableWidth / 2 - screen.X);
            QueueFurniturePreview(FurnitureLayoutContract.OrdinaryRoom.DefaultAnchor);
        }
        RefreshFurnitureEditor(); Scene.Focus();
    }

    private void NudgeFurniture(int x, int y)
    {
        if (!FurnitureCanInteract || furnitureTool != OrdinaryRoomFurnitureTool.Select || !furnitureSelected ||
            driver.Furniture is not { } furniture) return;
        var anchor = requestedFurnitureAnchor ?? furniture.Preview?.Anchor ?? furniture.Layout.Platform?.Anchor;
        if (anchor is not { } current) return;
        var step = FurnitureLayoutContract.OrdinaryRoom.NudgeStep;
        QueueFurniturePreview(new(current.X + x * step, current.Y + y * step));
    }

    private void QueueFurniturePreview(FurnitureAnchor anchor)
    {
        if (!FurnitureCanInteract || !double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y)) return;
        requestedFurnitureAnchor = anchor;
        RefreshFurnitureEditor();
    }

    private void RefreshFurnitureEditor()
    {
        var furniture = driver.Furniture;
        if (FurnitureCanInteract && Environment.TickCount64 >= nextFurniturePreview)
        {
            if (requestedFurnitureAnchor is { } anchor)
            {
                requestedFurnitureAnchor = null;
                furnitureCommandError = null;
                furniture!.PreviewPlatform(anchor.X, anchor.Y);
            }
            furniturePreviewCanConfirm = furniture!.HasPreview && furniture.CanConfirm;
            nextFurniturePreview = Environment.TickCount64 + 1000 / FurnitureLayoutContract.OrdinaryRoom.MaxNudgePerSecond;
        }
        if (furniture is not { CanEdit: true, HasPreview: true }) furniturePreviewCanConfirm = false;
        RefreshFurnitureAvailability();
        if (furnitureEditor is null || furniture is null) return;
        Scene.SetFurnitureFrame(furniture.Layout.Platform, IsFurnitureEditing ? furniture.Preview : null,
            furniturePreviewCanConfirm && requestedFurnitureAnchor is null);
        Scene.SetFurnitureSelected(IsFurnitureEditing && furnitureSelected && !furniture.HasPreview);
        furnitureEditor.Refresh(furniture.CanEdit && driver.IsReady && Scene.HasHealthyFrame, furniture.CanStep,
            furniture.Busy, furnitureSelected, furniture.Layout.Platform is not null, furniture.HasPreview || requestedFurnitureAnchor is not null,
            furniturePreviewCanConfirm && requestedFurnitureAnchor is null, furniture.CanRecover && driver.IsReady && Scene.HasHealthyFrame,
            furnitureCommandError ?? furniture.Status, furnitureTool);
        if (IsFurnitureEditing) Scene.Cursor = furnitureTool == OrdinaryRoomFurnitureTool.Glove ? Cursors.Hand : Cursors.Arrow;
    }

    private void RefreshFurnitureAvailability()
    {
        var furniture = driver.Furniture;
        var available = furniture is { Busy: false } && (furniture.CanEdit || furniture.CanRecover) && driver.IsReady && Scene.HasHealthyFrame;
        menu.SetFurnitureAvailability(available, available ? furniture!.CanRecover ? "核对已保存家具的运行结果" : "布置家具 · 本地单人" :
            furniture?.Status ?? "家具布局未就绪，当前无法布置。");
    }

    private bool IsFurniturePreviewHit(Point point)
    {
        if (driver.Furniture?.Preview is not { } preview) return false;
        var top = Scene.AnchorToScreen(preview.Anchor);
        var contract = FurnitureLayoutContract.OrdinaryRoom;
        var width = contract.PlatformWidth * Scene.CurrentProjection.Scale;
        var height = contract.PlatformHeight * Scene.CurrentProjection.Scale;
        return new Rect(top.X - width / 2, top.Y, width, height).Contains(point);
    }

    private void CancelFurniturePreview()
    {
        requestedFurnitureAnchor = null;
        furniturePreviewCanConfirm = false;
        if (driver.Furniture is { Busy: false } furniture) furniture.CancelPreview();
        RefreshFurnitureEditor();
    }

    private void CancelFurnitureGesture()
    {
        furnitureDragActive = furnitureGloveActive = false;
        requestedFurnitureAnchor = null;
        driver.CancelFurnitureGlove();
        if (Mouse.Captured == Scene) Scene.ReleaseMouseCapture();
    }

    private void CancelAllSceneGestures()
    {
        CancelCameraPan(); CancelFurnitureGesture();
    }

    private async void StartFurnitureCommand(Func<Task> command)
    {
        if (!driver.IsReady || !Scene.HasHealthyFrame || driver.Furniture is not { Busy: false } furniture) return;
        CancelAllSceneGestures(); driver.ClearInput(); furnitureCommandError = null;
        try
        {
            var pending = command(); RefreshFurnitureEditor(); await pending;
            furnitureSelected = furniture.Layout.Platform is not null || furniture.HasPreview;
        }
        catch (Exception error) when (error is InvalidOperationException or System.IO.IOException or ArgumentException)
        { furnitureCommandError = error.Message; }
        finally { RefreshFurnitureEditor(); }
    }

    private Button CreateWindowControl(string name, string label, string hint)
    {
        var button = new Button
        {
            Name = name, Content = label, ToolTip = hint, Width = 36, Height = 30,
            Margin = new Thickness(2, 3, 2, 3), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), FontSize = 16,
        };
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        button.Click += (_, _) => { CancelFurnitureGesture(); driver.ClearInput(); ReleaseScenePointerCapture(); HideRequested?.Invoke(); };
        return button;
    }

    private void RunMenuAction(Action action)
    {
        SetMenuOpen(false);
        action();
    }
}
