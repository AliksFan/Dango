using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Tray;
using Forms = System.Windows.Forms;

namespace Dango.Desktop.AppShell;

internal sealed partial class DangoApplicationHost
{
    private readonly LocalRoomHandoffGate roomHandoff = new();
    private readonly DispatcherTimer roomHandoffTimeout = new() { Interval = TimeSpan.FromSeconds(3) };
    private LocalRoomSceneDriver? localRoomDriver;
    private LocalRoomWindow? localRoomWindow;
    private LocalRoomWallpaperHost? localRoomWallpaper;
    private bool localRoomSelected;
    private bool desktopPetSelected;
    private bool roomControlCenterRequested;
    private bool openRoomMenuAfterHandoff;
    private LocalRoomPresentation preferredRoomPresentation = LocalRoomPresentation.Window;
    private string? localRoomFailure;
    private bool restoringRoomSource;
    private bool roomWorldNeedsRestore;
    internal LocalRoomPresentation LocalPresentation => roomHandoff.Current;
    internal bool LocalHandoffPending => roomHandoff.Pending;
    internal LocalRoomWindow? LocalWindow => localRoomWindow;
    internal string? LocalRoomFailure => localRoomFailure;
    internal bool DesktopPetSelected => desktopPetSelected && localRoomSelected;

    private void BindLocalRoom(ControlCenterWindow homeWindow)
    {
        localRoomDriver = new(sceneSession, stackedRuntime);
        localRoomWindow = new(localRoomDriver);
        localRoomWallpaper = new();
        localRoomWallpaper.Bind(localRoomDriver);
        homeWindow.ProductHome.EnableLocalRoomDoor();
        homeWindow.ProductHome.EnableDesktopPetRoomDoor();
        homeWindow.ProductHome.LocalRoomRequested += EnterLocalRoom;
        homeWindow.ProductHome.DesktopPetRoomRequested += EnterDesktopPetRoom;
        workerWWindow.DesktopPetRoomFrameReady += attempt => CompleteLocalHandoff(attempt, LocalRoomPresentation.DesktopPet);
        workerWWindow.DesktopPetRoomFailed += FailLocalHandoff;
        workerWWindow.DesktopPetRoomDoorRequested += ReturnToPorch;
        workerWWindow.DesktopPetRoomHideRequested += HideLocalRoom;
        workerWWindow.DesktopPetRoomControlCenterRequested += ShowControlCenter;
        homeWindow.RoomSettingsActionHandler = HandleDesktopPetSettingsAction;
        homeWindow.ProductHome.HealthyPorchFrameReady += attempt => CompleteLocalHandoff(attempt, LocalRoomPresentation.Porch);
        localRoomWindow.ReturnRequested += ReturnToPorch;
        localRoomWindow.HideRequested += HideLocalRoom;
        localRoomWindow.PresentationRequested += SwitchLocalRoomPresentation;
        localRoomWindow.RecoverRequested += RecoverLocalRoom;
        localRoomWindow.ControlCenterRequested += ShowControlCenter;
        localRoomWallpaper.ReturnRequested += ReturnToPorch;
        localRoomWallpaper.WindowRequested += () => SwitchLocalRoomPresentation(LocalRoomPresentation.Window);
        localRoomWallpaper.MenuRequested += ShowLocalRoomMenu;
        localRoomWallpaper.ControlCenterRequested += ShowControlCenter;
        localRoomWallpaper.RecoverRequested += RecoverLocalRoom;
        localRoomDriver.HealthyFrameReady += attempt => CompleteLocalHandoff(attempt, roomHandoff.Target);
        localRoomDriver.Failed += FailLocalHandoff;
        localRoomWallpaper.LostDesktop += () =>
        {
            if (roomHandoff.Pending) FailLocalHandoff(roomHandoff.Generation, "桌面宿主已失效，可重试壁纸或用窗口打开。");
            else if (roomHandoff.Current == LocalRoomPresentation.Wallpaper)
            {
                HideLocalRoom();
                localRoomFailure = "桌面宿主已失效，请从托盘重试壁纸、用窗口打开或返回房门。";
                trayIcon?.ShowRoomWarning(localRoomFailure);
            }
        };
        roomHandoffTimeout.Tick += (_, _) => FailLocalHandoff(roomHandoff.Generation, "场景首帧未能就绪，可重试或返回房门。");
    }

    internal void EnterLocalRoom()
    {
        if (disposed || roomHandoff.Pending || localRoomSelected) return;
        GetOrCreateControlCenter();
        roomControlCenterRequested = false;
        localRoomDriver!.ResetEntryPosition();
        BeginLocalHandoff(LocalRoomPresentation.Window, enterWorld: true);
    }

    internal void EnterDesktopPetRoom()
    {
        if (disposed || roomHandoff.Pending || localRoomSelected) return;
        GetOrCreateControlCenter();
        roomControlCenterRequested = false;
        BeginLocalHandoff(LocalRoomPresentation.DesktopPet, enterWorld: true);
    }

    internal void SwitchLocalRoomPresentation(LocalRoomPresentation target)
    {
        if (disposed || roomHandoff.Pending || desktopPetSelected ||
            target is not (LocalRoomPresentation.Window or LocalRoomPresentation.Wallpaper)) return;
        GetOrCreateControlCenter();
        if (roomHandoff.Current == target && localRoomSelected) return;
        if (!localRoomSelected) localRoomDriver!.ResetEntryPosition();
        BeginLocalHandoff(target, enterWorld: !localRoomSelected || roomWorldNeedsRestore);
    }

    internal void RestoreLocalRoom()
    {
        if (roomHandoff.Pending) return;
        if (!localRoomSelected) { ShowControlCenter(); return; }
        if (roomHandoff.Current == LocalRoomPresentation.Window && localRoomDriver!.OwnsRuntime) { localRoomWindow!.FocusScene(); return; }
        if (roomHandoff.Current == LocalRoomPresentation.Wallpaper) return;
        if (roomHandoff.Current == LocalRoomPresentation.DesktopPet && workerWWindow.DesktopPetRoomReady) return;
        BeginLocalHandoff(preferredRoomPresentation, enterWorld: roomWorldNeedsRestore);
    }

    internal void ReturnToPorch()
    {
        if (disposed) return;
        openRoomMenuAfterHandoff = false;
        GetOrCreateControlCenter();
        if (roomHandoff.Pending)
        {
            // Cancel invalidates all queued first-frame/input work before any recovery.
            roomHandoff.Fail(roomHandoff.Generation);
            roomHandoffTimeout.Stop();
        }
        if (!localRoomSelected && roomHandoff.Current == LocalRoomPresentation.Porch)
        {
            localRoomDriver!.Stop();
            workerWWindow.SuspendDesktopPetRoom();
            workerWWindow.SetVisibility(false);
            desktopPetSelected = false;
            desktopHost.SetRoomPresentationAllowed(false);
            desktopHost.SetRoomDisplayModeLock(null);
            controlCenterWindow!.ProductHome.SetDesktopPetRoomContext(false);
            localRoomWindow!.Hide();
            localRoomWallpaper!.Hide();
            ShowAndActivate(controlCenterWindow!);
            controlCenterWindow!.ShowOverview();
            controlCenterWindow.ProductHome.ResumePorch();
            return;
        }
        BeginLocalHandoff(LocalRoomPresentation.Porch, enterWorld: false);
    }

    internal void HideLocalRoom()
    {
        if (disposed || localRoomDriver is null) return;
        var selected = localRoomSelected;
        var wasEntering = roomHandoff.Pending && !selected;
        roomHandoffTimeout.Stop();
        restoringRoomSource = false;
        openRoomMenuAfterHandoff = false;
        localRoomDriver.Stop();
        workerWWindow.SuspendDesktopPetRoom();
        workerWWindow.SetVisibility(false);
        if (selected)
        {
            controlCenterWindow!.ProductHome.SetDangoAway(true);
            if (!roomControlCenterRequested) controlCenterWindow.Hide();
        }
        localRoomWindow!.Hide();
        localRoomWallpaper!.Hide();
        roomHandoff.Hide();
        if (wasEntering)
        {
            localRoomSelected = false;
            desktopPetSelected = false;
            desktopHost.SetRoomPresentationAllowed(false);
            desktopHost.SetRoomDisplayModeLock(null);
            controlCenterWindow!.ProductHome.SetDesktopPetRoomContext(false);
            ShowAndActivate(controlCenterWindow!);
            controlCenterWindow!.ProductHome.ResumePorch();
        }
    }

    private void BeginLocalHandoff(LocalRoomPresentation target, bool enterWorld)
    {
        if (roomHandoff.Pending) return;
        var attempt = roomHandoff.Begin(target);
        localRoomFailure = null;
        controlCenterWindow!.ProductHome.SuspendForRoom();
        localRoomDriver!.Stop();
        workerWWindow.SuspendDesktopPetRoom();
        localRoomWallpaper!.StopInput();
        roomHandoffTimeout.Start();
        try
        {
            if (target == LocalRoomPresentation.Porch)
            {
                roomWorldNeedsRestore = true;
                // Keep the room visible/frozen until the real porch body frame has rendered.
                controlCenterWindow.ShowOverview();
                ShowAndActivate(controlCenterWindow);
                controlCenterWindow.ProductHome.ResumePorch(attempt);
            }
            else if (target == LocalRoomPresentation.Window)
            {
                if (!localRoomWindow!.IsVisible) localRoomWindow.Show();
                if (localRoomWindow.WindowState == WindowState.Minimized) localRoomWindow.WindowState = WindowState.Normal;
                localRoomDriver.Start(localRoomWindow.Scene, attempt, enterWorld);
            }
            else if (target == LocalRoomPresentation.DesktopPet)
            {
                desktopHost.SetMode(Dango.Desktop.DesktopHost.DesktopDisplayMode.Wallpaper);
                desktopHost.SetRoomDisplayModeLock(Dango.Desktop.DesktopHost.DesktopDisplayMode.Wallpaper);
                workerWWindow.BeginDesktopPetRoom(attempt, enterWorld);
            }
            else
            {
                var screen = localRoomWindow?.IsVisible == true
                    ? Forms.Screen.FromHandle(new WindowInteropHelper(localRoomWindow).Handle)
                    : Forms.Screen.PrimaryScreen ?? throw new InvalidOperationException("没有可用显示器。");
                localRoomWallpaper.Show(attempt, screen);
                localRoomDriver.Start(localRoomWallpaper.Scene, attempt, enterWorld);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        { FailLocalHandoff(attempt, target == LocalRoomPresentation.Wallpaper ? "壁纸挂接失败，可重试或用窗口打开。" : "房间准备失败，请返回门廊重试。"); }
    }

    private void CompleteLocalHandoff(long attempt, LocalRoomPresentation target)
    {
        if (target == LocalRoomPresentation.DesktopPet)
        {
            if (disposed || !roomHandoff.Pending || attempt != roomHandoff.Generation || target != roomHandoff.Target) return;
            if (!workerWWindow.ConfirmDesktopPetRoom(attempt))
            { FailLocalHandoff(attempt, "桌宠画面未能确认，请从房门重试。"); return; }
        }
        if (disposed || !roomHandoff.TryComplete(attempt, target, true)) return;
        roomHandoffTimeout.Stop();
        restoringRoomSource = false;
        if (target == LocalRoomPresentation.Porch)
        {
            localRoomSelected = false;
            desktopPetSelected = false;
            roomControlCenterRequested = false;
            localRoomWindow!.Hide();
            localRoomWallpaper!.Hide();
            workerWWindow.SetVisibility(false);
            desktopHost.SetRoomPresentationAllowed(false);
            desktopHost.SetRoomDisplayModeLock(null);
            controlCenterWindow!.ProductHome.SetDesktopPetRoomContext(false);
        }
        else
        {
            roomWorldNeedsRestore = false;
            localRoomSelected = true;
            desktopPetSelected = target == LocalRoomPresentation.DesktopPet;
            controlCenterWindow!.ProductHome.SetDesktopPetRoomContext(desktopPetSelected);
            preferredRoomPresentation = target;
            controlCenterWindow!.ProductHome.SetDangoAway(true);
            if (!roomControlCenterRequested) controlCenterWindow.Hide();
            if (target == LocalRoomPresentation.Window)
            {
                localRoomWallpaper!.Hide(); localRoomWindow!.FocusScene();
                if (openRoomMenuAfterHandoff) localRoomWindow.OpenMenu();
                openRoomMenuAfterHandoff = false;
            }
            else if (target == LocalRoomPresentation.DesktopPet)
            {
                localRoomWindow!.Hide();
                localRoomWallpaper!.Hide();
                desktopHost.SetRoomPresentationAllowed(true);
                openRoomMenuAfterHandoff = false;
            }
            else
            { localRoomWindow!.Hide(); localRoomWallpaper!.ConfirmReady(attempt); }
        }
    }

    private void FailLocalHandoff(long attempt, string message)
    {
        if (!disposed && !roomHandoff.Pending && attempt == roomHandoff.Generation && localRoomSelected)
        {
            openRoomMenuAfterHandoff = false;
            localRoomFailure = message;
            localRoomDriver!.Stop();
            if (roomHandoff.Current == LocalRoomPresentation.Window)
                localRoomWindow!.Scene.ShowUnavailable("团子暂不可用，请选择恢复团子，或从房门返回。");
            else HideLocalRoom();
            trayIcon?.ShowRoomWarning(message + " 可恢复团子后重试或返回房门。");
            return;
        }
        if (disposed || !roomHandoff.Fail(attempt)) return;
        openRoomMenuAfterHandoff = false;
        roomHandoffTimeout.Stop();
        localRoomFailure = message;
        localRoomDriver!.Stop();
        workerWWindow.SuspendDesktopPetRoom();
        if (localRoomSelected) controlCenterWindow!.ProductHome.SetDangoAway(true);
        else controlCenterWindow!.ProductHome.SuspendForRoom();
        var source = roomHandoff.Current;
        // Restore the previous presentation through a new healthy-frame handshake.
        if (source == LocalRoomPresentation.Window && localRoomSelected && !restoringRoomSource)
        {
            localRoomWallpaper!.Hide();
            restoringRoomSource = true;
            BeginLocalHandoff(LocalRoomPresentation.Window, enterWorld: true);
            localRoomFailure = message;
            trayIcon?.ShowRoomWarning(message);
        }
        else if (source == LocalRoomPresentation.Window && localRoomSelected)
        {
            restoringRoomSource = false;
            localRoomWallpaper!.Hide();
            localRoomWindow!.Show();
            localRoomWindow.FocusScene();
            trayIcon?.ShowRoomWarning(message + " 可从房门返回门廊，或隐藏后重试。");
        }
        else if (desktopPetSelected && localRoomSelected)
        {
            workerWWindow.SetVisibility(false);
            roomHandoff.Hide();
            trayIcon?.ShowRoomWarning(message + " 请从托盘恢复桌宠房间或返回房门。");
        }
        else if (source == LocalRoomPresentation.Wallpaper && localRoomSelected)
        {
            localRoomWindow!.Hide();
            localRoomWallpaper!.Hide();
            roomHandoff.Hide();
            trayIcon?.ShowRoomWarning(message + " 请从托盘重试壁纸、用窗口打开或返回房门。");
        }
        else
        {
            localRoomWindow!.Hide();
            localRoomWallpaper!.Hide();
            localRoomSelected = false;
            desktopPetSelected = false;
            workerWWindow.SetVisibility(false);
            desktopHost.SetRoomPresentationAllowed(false);
            desktopHost.SetRoomDisplayModeLock(null);
            controlCenterWindow!.ProductHome.SetDesktopPetRoomContext(false);
            ShowAndActivate(controlCenterWindow);
            controlCenterWindow.ProductHome.ResumePorch();
            controlCenterWindow.ProductHome.SetNotice("房间暂不可用 · 查看", message);
        }
    }

    internal void RecoverLocalRoom()
    {
        if (disposed || roomHandoff.Pending) return;
        GetOrCreateControlCenter();
        localRoomDriver!.Stop();
        workerWWindow.SuspendDesktopPetRoom();
        controlCenterWindow!.ProductHome.SuspendForRoom();
        if (!stackedRuntime.TryInitialize())
        { localRoomWindow!.Scene.ShowUnavailable("团子初始化失败，请从房门返回设置重试。"); return; }
        stackedRuntime.RegenerateDango();
        BeginLocalHandoff(preferredRoomPresentation, enterWorld: true);
    }

    private bool HandleDesktopPetSettingsAction(ProductSettingsAction action)
    {
        if (!DesktopPetSelected) return false;
        switch (action)
        {
            case ProductSettingsAction.ToggleVisibility:
                if (roomHandoff.Current == LocalRoomPresentation.DesktopPet) HideLocalRoom(); else RestoreLocalRoom();
                return true;
            case ProductSettingsAction.WallpaperMode:
            case ProductSettingsAction.RetryWallpaper:
                RestoreLocalRoom();
                return true;
            case ProductSettingsAction.WindowMode:
                controlCenterWindow!.ProductHome.SetNotice("桌宠房间使用壁纸层", "从房门返回门廊后，可选择普通房窗口。");
                return true;
            case ProductSettingsAction.BelowIcons:
            case ProductSettingsAction.AboveIcons:
                controlCenterWindow!.ProductHome.ShowSettingsStatus("桌宠房间位于桌面图标下方。", false);
                return true;
            case ProductSettingsAction.RetryInitialization:
            case ProductSettingsAction.RestoreDango:
                RecoverLocalRoom();
                return true;
            default: return false;
        }
    }

    private void ShowLocalRoomMenu()
    {
        if (!localRoomSelected || roomHandoff.Pending) return;
        if (roomHandoff.Current == LocalRoomPresentation.Window)
        {
            localRoomWindow!.FocusScene();
            localRoomWindow.OpenMenu();
        }
        else if (roomHandoff.Current == LocalRoomPresentation.Wallpaper)
        {
            localRoomWallpaper!.OpenMenu();
        }
    }

    private LocalRoomTrayActions CreateLocalRoomTrayActions() => new(
        () => localRoomSelected || roomHandoff.Pending,
        () => roomHandoff.Current is LocalRoomPresentation.Window or LocalRoomPresentation.Wallpaper or LocalRoomPresentation.DesktopPet,
        RestoreLocalRoom, HideLocalRoom, ReturnToPorch, SwitchLocalRoomPresentation,
        preset => ActiveRoomScene()?.SelectView(preset), RecoverLocalRoom,
        () => desktopPetSelected || roomHandoff.Pending && roomHandoff.Target == LocalRoomPresentation.DesktopPet);
    private LocalRoomSceneView? ActiveRoomScene() => desktopPetSelected ? null : roomHandoff.Current == LocalRoomPresentation.Wallpaper
        ? localRoomWallpaper?.Scene : localRoomWindow?.Scene;
    private void DisposeLocalRoom()
    {
        workerWWindow.SetVisibility(false);
        roomHandoffTimeout.Stop();
        localRoomDriver?.Dispose();
        localRoomWallpaper?.Dispose();
        if (localRoomWindow is not null) { localRoomWindow.PrepareForExit(); localRoomWindow.Close(); }
    }
}
