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
    private LocalRoomPresentation preferredRoomPresentation = LocalRoomPresentation.Window;
    private string? localRoomFailure;
    private bool restoringRoomSource;
    private bool roomWorldNeedsRestore;
    internal LocalRoomPresentation LocalPresentation => roomHandoff.Current;
    internal bool LocalHandoffPending => roomHandoff.Pending;
    internal LocalRoomWindow? LocalWindow => localRoomWindow;
    internal string? LocalRoomFailure => localRoomFailure;

    private void BindLocalRoom(ControlCenterWindow homeWindow)
    {
        localRoomDriver = new(sceneSession, stackedRuntime);
        localRoomWindow = new(localRoomDriver);
        localRoomWallpaper = new();
        homeWindow.ProductHome.EnableLocalRoomDoor();
        homeWindow.ProductHome.LocalRoomRequested += EnterLocalRoom;
        homeWindow.ProductHome.HomeRequested += () =>
        {
            if (localRoomSelected && !roomHandoff.Pending && roomHandoff.Current == LocalRoomPresentation.Hidden)
            { localRoomSelected = false; homeWindow.ProductHome.ResumeHomeAfterRoom(); }
        };
        homeWindow.ProductHome.HealthyPorchFrameReady += attempt => CompleteLocalHandoff(attempt, LocalRoomPresentation.Porch);
        localRoomWindow.ReturnRequested += ReturnToPorch;
        localRoomWindow.HideRequested += HideLocalRoom;
        localRoomWindow.PresentationRequested += SwitchLocalRoomPresentation;
        localRoomWindow.RecoverRequested += RecoverLocalRoom;
        localRoomWallpaper.ReturnRequested += ReturnToPorch;
        localRoomWallpaper.WindowRequested += () => SwitchLocalRoomPresentation(LocalRoomPresentation.Window);
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
        localRoomDriver!.ResetEntryPosition();
        BeginLocalHandoff(LocalRoomPresentation.Window, enterWorld: true);
    }

    internal void SwitchLocalRoomPresentation(LocalRoomPresentation target)
    {
        if (disposed || roomHandoff.Pending || target is not (LocalRoomPresentation.Window or LocalRoomPresentation.Wallpaper)) return;
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
        BeginLocalHandoff(preferredRoomPresentation, enterWorld: roomWorldNeedsRestore);
    }

    internal void ReturnToPorch()
    {
        if (disposed) return;
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
        localRoomDriver.Stop();
        if (selected)
        {
            controlCenterWindow!.ProductHome.SuspendForRoom();
            controlCenterWindow.Hide();
        }
        localRoomWindow!.Hide();
        localRoomWallpaper!.Hide();
        roomHandoff.Hide();
        if (wasEntering)
        {
            localRoomSelected = false;
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
        if (disposed || !roomHandoff.TryComplete(attempt, target, true)) return;
        roomHandoffTimeout.Stop();
        restoringRoomSource = false;
        if (target == LocalRoomPresentation.Porch)
        {
            localRoomSelected = false;
            localRoomWindow!.Hide();
            localRoomWallpaper!.Hide();
        }
        else
        {
            roomWorldNeedsRestore = false;
            localRoomSelected = true;
            preferredRoomPresentation = target;
            controlCenterWindow!.Hide();
            if (target == LocalRoomPresentation.Window)
            { localRoomWallpaper!.Hide(); localRoomWindow!.FocusScene(); }
            else
            { localRoomWindow!.Hide(); localRoomWallpaper!.ConfirmReady(attempt); }
        }
    }

    private void FailLocalHandoff(long attempt, string message)
    {
        if (!disposed && !roomHandoff.Pending && attempt == roomHandoff.Generation && localRoomSelected)
        {
            localRoomFailure = message;
            localRoomDriver!.Stop();
            if (roomHandoff.Current == LocalRoomPresentation.Window)
                localRoomWindow!.Scene.ShowUnavailable("团子暂不可用，请选择恢复团子，或从房门返回。");
            else HideLocalRoom();
            trayIcon?.ShowRoomWarning(message + " 可恢复团子后重试或返回房门。");
            return;
        }
        if (disposed || !roomHandoff.Fail(attempt)) return;
        roomHandoffTimeout.Stop();
        localRoomFailure = message;
        localRoomDriver!.Stop();
        controlCenterWindow!.ProductHome.SuspendForRoom();
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
        controlCenterWindow!.ProductHome.SuspendForRoom();
        if (!stackedRuntime.TryInitialize())
        { localRoomWindow!.Scene.ShowUnavailable("团子初始化失败，请从房门返回设置重试。"); return; }
        stackedRuntime.RegenerateDango();
        BeginLocalHandoff(preferredRoomPresentation, enterWorld: true);
    }

    private LocalRoomTrayActions CreateLocalRoomTrayActions() => new(
        () => localRoomSelected || roomHandoff.Pending,
        () => roomHandoff.Current is LocalRoomPresentation.Window or LocalRoomPresentation.Wallpaper,
        RestoreLocalRoom, HideLocalRoom, ReturnToPorch, SwitchLocalRoomPresentation,
        preset => ActiveRoomScene()?.SelectView(preset), RecoverLocalRoom);
    private LocalRoomSceneView? ActiveRoomScene() => roomHandoff.Current == LocalRoomPresentation.Wallpaper
        ? localRoomWallpaper?.Scene : localRoomWindow?.Scene;
    private void DisposeLocalRoom()
    {
        roomHandoffTimeout.Stop();
        localRoomDriver?.Dispose();
        localRoomWallpaper?.Dispose();
        if (localRoomWindow is not null) { localRoomWindow.PrepareForExit(); localRoomWindow.Close(); }
    }
}
