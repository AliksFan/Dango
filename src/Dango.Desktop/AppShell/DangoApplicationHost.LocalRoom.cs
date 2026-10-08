using System.IO;
using System.Windows;
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
    private DesktopPetRoomHost? desktopPetRoom;
    private bool localRoomSelected;
    private bool roomControlCenterRequested;
    private LocalRoomPresentation preferredRoomPresentation = LocalRoomPresentation.Window;
    private string? localRoomFailure;
    private bool restoringRoomSource;
    private bool roomWorldNeedsRestore;
    internal LocalRoomPresentation LocalPresentation => roomHandoff.Current;
    internal bool LocalHandoffPending => roomHandoff.Pending;
    internal LocalRoomWindow? LocalWindow => localRoomWindow;
    internal DesktopPetRoomHost? DesktopPetHost => desktopPetRoom;
    internal string? LocalRoomFailure => localRoomFailure;

    private void BindLocalRoom(ControlCenterWindow homeWindow)
    {
        localRoomDriver = new(sceneSession, stackedRuntime);
        localRoomWindow = new(localRoomDriver);
        desktopPetRoom = new();
        desktopPetRoom.Bind(localRoomDriver);
        homeWindow.ProductHome.EnableLocalRoomDoor();
        homeWindow.ProductHome.EnableDesktopPetRoomDoor();
        homeWindow.ProductHome.LocalRoomRequested += EnterLocalRoom;
        homeWindow.ProductHome.DesktopPetRoomRequested += EnterDesktopPetRoom;
        homeWindow.ProductHome.HealthyPorchFrameReady += attempt => CompleteLocalHandoff(attempt, LocalRoomPresentation.Porch);
        localRoomWindow.ReturnRequested += ReturnToPorch;
        localRoomWindow.HideRequested += HideLocalRoom;
        localRoomWindow.RecoverRequested += RecoverLocalRoom;
        localRoomWindow.ControlCenterRequested += ShowControlCenter;
        localRoomDriver.HealthyFrameReady += attempt =>
        {
            // A wallpaper environment frame alone cannot commit the layered desktop-pet room.
            if (roomHandoff.Target != LocalRoomPresentation.DesktopPet)
                CompleteLocalHandoff(attempt, roomHandoff.Target);
        };
        desktopPetRoom.HealthyFrameReady += attempt => CompleteLocalHandoff(attempt, LocalRoomPresentation.DesktopPet);
        desktopPetRoom.ReturnRequested += ReturnToPorch;
        desktopPetRoom.HideRequested += HideLocalRoom;
        desktopPetRoom.ControlCenterRequested += ShowControlCenter;
        desktopPetRoom.RecoverRequested += RecoverLocalRoom;
        desktopPetRoom.LostDesktop += () =>
        {
            SaveDesktopPetFailureReport();
            const string message = "桌宠房间显示已失效，请从托盘恢复或返回房门重试。";
            if (roomHandoff.Pending) FailLocalHandoff(roomHandoff.Generation, message);
            else if (roomHandoff.Current == LocalRoomPresentation.DesktopPet)
            {
                HideLocalRoom();
                localRoomFailure = message;
                trayIcon?.ShowRoomWarning(message);
            }
        };
        localRoomDriver.Failed += (attempt, message) =>
        {
            if (attempt == roomHandoff.Generation && desktopPetRoom.IsVisible)
            {
                desktopPetRoom.RecordDriverFailure();
                SaveDesktopPetFailureReport();
            }
            FailLocalHandoff(attempt, message);
        };
        roomHandoffTimeout.Tick += (_, _) => FailLocalHandoff(roomHandoff.Generation, "场景首帧未能就绪，可重试或返回房门。");
    }

    private void SaveDesktopPetFailureReport()
    {
        if (desktopPetRoom?.LastFailureSnapshot is not { } snapshot) return;
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(settingsPath))!;
            Directory.CreateDirectory(folder);
            // One bounded local report: no pointer positions, HWNDs, desktop
            // content, user input history or exception messages are persisted.
            File.WriteAllText(Path.Combine(folder, "desktop-pet-last-failure.json"),
                System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Diagnostic storage must not prevent the existing loss/recovery path.
            System.Diagnostics.Trace.WriteLine($"Desktop pet failure report unavailable: {error.GetType().Name}");
        }
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
        localRoomDriver!.ResetEntryPosition();
        BeginLocalHandoff(LocalRoomPresentation.DesktopPet, enterWorld: true);
    }

    internal void RestoreLocalRoom()
    {
        if (roomHandoff.Pending) return;
        if (!localRoomSelected) { ShowControlCenter(); return; }
        if (roomHandoff.Current == LocalRoomPresentation.Window && localRoomDriver!.OwnsRuntime) { localRoomWindow!.FocusScene(); return; }
        if (roomHandoff.Current == LocalRoomPresentation.DesktopPet) return;
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
            desktopPetRoom!.Hide();
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
            controlCenterWindow!.ProductHome.SetDangoAway(true);
            if (!roomControlCenterRequested) controlCenterWindow.Hide();
        }
        localRoomWindow!.Hide();
        desktopPetRoom!.Hide();
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
        desktopPetRoom!.StopInput();
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
                var screen = Forms.Screen.PrimaryScreen ?? throw new InvalidOperationException("没有可用主显示器。");
                desktopPetRoom.Show(attempt, screen);
                localRoomDriver.Start(desktopPetRoom.Scene, attempt, enterWorld);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(target), target, "不支持的房间呈现目标。");
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            FailLocalHandoff(attempt, target == LocalRoomPresentation.DesktopPet ? "桌宠房间挂接失败，请返回门廊重试。" :
            "房间准备失败，请返回门廊重试。");
        }
    }

    private void CompleteLocalHandoff(long attempt, LocalRoomPresentation target)
    {
        if (disposed || !roomHandoff.TryComplete(attempt, target, true)) return;
        roomHandoffTimeout.Stop();
        restoringRoomSource = false;
        if (target == LocalRoomPresentation.Porch)
        {
            localRoomSelected = false;
            roomControlCenterRequested = false;
            localRoomWindow!.Hide();
            desktopPetRoom!.Hide();
        }
        else
        {
            roomWorldNeedsRestore = false;
            localRoomSelected = true;
            preferredRoomPresentation = target;
            controlCenterWindow!.ProductHome.SetDangoAway(true);
            if (!roomControlCenterRequested) controlCenterWindow.Hide();
            if (target == LocalRoomPresentation.Window)
            {
                desktopPetRoom!.Hide();
                localRoomWindow!.FocusScene();
            }
            else if (target == LocalRoomPresentation.DesktopPet)
            {
                localRoomWindow!.Hide();
                desktopPetRoom!.ConfirmReady(attempt);
            }
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
        desktopPetRoom!.StopInput();
        if (localRoomSelected) controlCenterWindow!.ProductHome.SetDangoAway(true);
        else controlCenterWindow!.ProductHome.SuspendForRoom();
        var source = roomHandoff.Current;
        // Restore the previous presentation through a new healthy-frame handshake.
        if (source == LocalRoomPresentation.Window && localRoomSelected && !restoringRoomSource)
        {
            desktopPetRoom!.Hide();
            restoringRoomSource = true;
            BeginLocalHandoff(LocalRoomPresentation.Window, enterWorld: true);
            localRoomFailure = message;
            trayIcon?.ShowRoomWarning(message);
        }
        else if (source == LocalRoomPresentation.Window && localRoomSelected)
        {
            desktopPetRoom!.Hide();
            restoringRoomSource = false;
            localRoomWindow!.Show();
            localRoomWindow.FocusScene();
            trayIcon?.ShowRoomWarning(message + " 可从房门返回门廊，或隐藏后重试。");
        }
        else if (localRoomSelected && source == LocalRoomPresentation.DesktopPet)
        {
            localRoomWindow!.Hide();
            desktopPetRoom!.Hide();
            roomHandoff.Hide();
            trayIcon?.ShowRoomWarning(message + " 请从托盘恢复房间或返回房门。");
        }
        else if (source == LocalRoomPresentation.Hidden && localRoomSelected &&
            preferredRoomPresentation == LocalRoomPresentation.DesktopPet)
        {
            // A failed restoration must not erase the hidden room selection or rehost its entity into the porch.
            localRoomWindow!.Hide();
            desktopPetRoom!.Hide();
            roomHandoff.Hide();
            ShowControlCenter();
            controlCenterWindow!.ProductHome.SetNotice("桌宠房间暂不可用 · 查看", message);
            trayIcon?.ShowRoomWarning(message + " 可从托盘恢复房间重试，或从房门返回。");
        }
        else
        {
            localRoomWindow!.Hide();
            desktopPetRoom!.Hide();
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
        desktopPetRoom!.StopInput();
        controlCenterWindow!.ProductHome.SuspendForRoom();
        if (!stackedRuntime.TryInitialize())
        {
            if (preferredRoomPresentation == LocalRoomPresentation.DesktopPet)
            {
                HideLocalRoom();
                localRoomFailure = "团子初始化失败，请返回房门或在设置中重试。";
                ShowControlCenter();
                controlCenterWindow.ProductHome.SetNotice("团子暂不可用 · 查看", localRoomFailure);
            }
            else localRoomWindow!.Scene.ShowUnavailable("团子初始化失败，请从房门返回设置重试。");
            return;
        }
        stackedRuntime.RegenerateDango();
        BeginLocalHandoff(preferredRoomPresentation, enterWorld: true);
    }

    private LocalRoomTrayActions CreateLocalRoomTrayActions() => new(
        () => localRoomSelected || roomHandoff.Pending,
        () => roomHandoff.Current is LocalRoomPresentation.Window or LocalRoomPresentation.DesktopPet,
        RestoreLocalRoom, HideLocalRoom, ReturnToPorch,
        preset => { if (preferredRoomPresentation != LocalRoomPresentation.DesktopPet) localRoomWindow?.Scene.SelectView(preset); },
        RecoverLocalRoom, () => preferredRoomPresentation == LocalRoomPresentation.DesktopPet);
    private void DisposeLocalRoom()
    {
        roomHandoffTimeout.Stop();
        localRoomDriver?.Dispose();
        desktopPetRoom?.Dispose();
        if (localRoomWindow is not null) { localRoomWindow.PrepareForExit(); localRoomWindow.Close(); }
    }
}
