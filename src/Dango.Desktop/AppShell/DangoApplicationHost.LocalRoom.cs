using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Tray;
using Forms = System.Windows.Forms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Furniture;

namespace Dango.Desktop.AppShell;

internal sealed partial class DangoApplicationHost
{
    private readonly LocalRoomHandoffGate roomHandoff = new();
    private readonly DispatcherTimer roomHandoffTimeout = new() { Interval = TimeSpan.FromSeconds(3) };
    private LocalRoomSceneDriver? localRoomDriver;
    private LocalRoomWindow? localRoomWindow;
    private LocalRoomWallpaperHost? localRoomWallpaper;
    private DesktopPetRoomHost? desktopPetRoom;
    private bool localRoomSelected;
    private bool roomControlCenterRequested;
    private bool openRoomMenuAfterHandoff;
    private bool openFurnitureAfterHandoff;
    private LocalRoomPresentation preferredRoomPresentation = LocalRoomPresentation.Window;
    private string? localRoomFailure;
    private bool restoringRoomSource;
    private bool roomWorldNeedsRestore;
    private LocalRoomCatalogStore? roomCatalogStore;
    private RoomResumeStore? roomResumeStore;
    private Guid? selectedCatalogRoomId;
    private bool catalogEntryLoading;
    private long catalogEntryGeneration;
    private readonly DispatcherTimer roomCheckpointTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private Task roomCheckpointTask = Task.CompletedTask;
    private Task roomPreparationTask = Task.CompletedTask;
    internal LocalRoomPresentation LocalPresentation => roomHandoff.Current;
    internal bool LocalHandoffPending => roomHandoff.Pending || catalogEntryLoading;
    internal LocalRoomWindow? LocalWindow => localRoomWindow;
    internal DesktopPetRoomHost? DesktopPetHost => desktopPetRoom;
    internal string? LocalRoomFailure => localRoomFailure;

    private void BindLocalRoom(ControlCenterWindow homeWindow)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(settingsPath))!;
        roomCatalogStore = new(root); roomResumeStore = new(roomCatalogStore);
        localRoomDriver = new(sceneSession, stackedRuntime);
        localRoomWindow = new(localRoomDriver, homeWindow.ProductHome.Resources);
        localRoomWallpaper = new();
        localRoomWallpaper.Bind(localRoomDriver);
        desktopPetRoom = new();
        desktopPetRoom.Bind(localRoomDriver);
        homeWindow.ProductHome.EnableDesktopPetRoomDoor();
        homeWindow.ProductHome.CatalogRoomRequested += EnterCatalogRoom;
        _ = InitializeCatalogUiAsync(homeWindow);
        roomCheckpointTimer.Tick += (_, _) => SaveRoomCheckpoint();
        homeWindow.ProductHome.LocalRoomRequested += EnterLocalRoom;
        homeWindow.ProductHome.LocalFurnitureRequested += EnterLocalRoomFurniture;
        localRoomWindow.Deactivated += (_, _) => openFurnitureAfterHandoff = false;
        homeWindow.ProductHome.DesktopPetRoomRequested += EnterDesktopPetRoom;
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
        if (!TryLockDangoForEntry(out _)) return;
        if (roomCatalogStore is not null)
        {
            if (controlCenterWindow!.ProductHome.CatalogSnapshot?.Rooms.FirstOrDefault(room => room.Life == LocalRoomLife.Active) is { } room)
                EnterCatalogRoom(room.RoomId, false);
            return;
        }
        roomControlCenterRequested = false;
        PrepareBeltRoomEntry();
        localRoomDriver!.ResetEntryPosition();
        BeginLocalHandoff(LocalRoomPresentation.Window, enterWorld: true);
    }

    internal void EnterLocalRoomFurniture()
    {
        if (disposed || roomHandoff.Pending || localRoomSelected) return;
        GetOrCreateControlCenter();
        if (!TryLockDangoForEntry(out _)) return;
        if (controlCenterWindow?.ProductHome.CatalogSnapshot?.Rooms.FirstOrDefault(room => room.Life == LocalRoomLife.Active) is { } room)
        { EnterCatalogRoom(room.RoomId, true); return; }
        openFurnitureAfterHandoff = true;
        EnterLocalRoom();
    }

    internal async void EnterDesktopPetRoom()
    {
        if (disposed || roomHandoff.Pending || localRoomSelected || catalogEntryLoading) return;
        GetOrCreateControlCenter();
        var home = controlCenterWindow!.ProductHome;
        if (home.IsRoomDialogOpen) return;
        if (!TryLockDangoForEntry(out var enteringDango)) return;
        catalogEntryLoading = true;
        home.RoomEntryPending = true;
        home.PorchScene.CatalogInputBlocked = true;
        var request = ++catalogEntryGeneration;
        try
        {
            await home.PauseBeltForRoomAsync();
            await home.Dispatcher.InvokeAsync(() =>
            {
                if (disposed || request != catalogEntryGeneration || roomHandoff.Pending || localRoomSelected || !home.IsVisible ||
                    activeDangoId != enteringDango || dangoRoster?.SelectedId != enteringDango) return;
                PrepareBeltRoomEntry();
                if (!stackedRuntime.TryInitialize())
                    throw new InvalidOperationException("新身体房间模块初始化失败。");
                if (!stackedRuntime.TryDetachOrdinaryFurniture())
                    throw new InvalidOperationException(stackedRuntime.Furniture?.Status ?? "家具状态尚未核清。");
                selectedCatalogRoomId = null;
                roomControlCenterRequested = false;
                localRoomDriver!.ResetEntryPosition();
                BeginLocalHandoff(LocalRoomPresentation.DesktopPet, enterWorld: true);
            });
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            if (!home.Dispatcher.HasShutdownStarted) await home.Dispatcher.InvokeAsync(() =>
            {
                if (disposed || request != catalogEntryGeneration) return;
                localRoomFailure = "桌宠房间暂不可用，请返回房门重试。";
                home.SetNotice("桌宠房间暂不可用 · 查看", localRoomFailure);
            });
        }
        finally
        {
            if (!home.Dispatcher.HasShutdownStarted) await home.Dispatcher.InvokeAsync(() =>
            {
                if (request != catalogEntryGeneration) return;
                catalogEntryLoading = false;
                if (!roomHandoff.Pending)
                {
                    home.RoomEntryPending = false;
                    home.PorchScene.CatalogInputBlocked = false;
                    if (!localRoomSelected) home.RestoreBeltAfterRoomPreparation();
                }
            });
        }
    }

    internal void SwitchLocalRoomPresentation(LocalRoomPresentation target)
    {
        if (disposed || roomHandoff.Pending || target is not (LocalRoomPresentation.Window or LocalRoomPresentation.Wallpaper)) return;
        if (localRoomSelected && preferredRoomPresentation == LocalRoomPresentation.DesktopPet) return;
        GetOrCreateControlCenter();
        if (roomHandoff.Current == target && localRoomSelected) return;
        if (!localRoomSelected) { EnterLocalRoom(); return; }
        BeginLocalHandoff(target, enterWorld: !localRoomSelected || roomWorldNeedsRestore);
    }

    internal void RestoreLocalRoom()
    {
        if (roomHandoff.Pending) return;
        if (!localRoomSelected) { ShowControlCenter(); return; }
        if (roomHandoff.Current == LocalRoomPresentation.Window && localRoomDriver!.OwnsRuntime)
        {
            if (!localRoomWindow!.IsVisible) localRoomWindow.Show();
            if (localRoomWindow.WindowState == WindowState.Minimized) localRoomWindow.WindowState = WindowState.Normal;
            localRoomWindow.FocusScene();
            return;
        }
        if (roomHandoff.Current is LocalRoomPresentation.Wallpaper or LocalRoomPresentation.DesktopPet) return;
        BeginLocalHandoff(preferredRoomPresentation, enterWorld: roomWorldNeedsRestore);
    }

    internal void ReturnToPorch()
    {
        if (disposed) return;
        GetOrCreateControlCenter();
        ++catalogEntryGeneration; catalogEntryLoading = false;
        controlCenterWindow!.ProductHome.RoomEntryPending = false;
        controlCenterWindow.ProductHome.PorchScene.CatalogInputBlocked = controlCenterWindow.ProductHome.IsRoomDialogOpen;
        SaveRoomCheckpoint();
        roomCheckpointTimer.Stop();
        // A pending target can already own a platform before its rendered-frame acknowledgement.
        // Do not invalidate that handshake or resume the porch until the binding is cleared.
        if (!stackedRuntime.TryDetachOrdinaryFurniture())
        {
            if (selectedCatalogRoomId is not null) roomCheckpointTimer.Start();
            localRoomFailure = stackedRuntime.Furniture?.Status ?? "家具状态尚未核对，暂不能返回房门。";
            trayIcon?.ShowRoomWarning(localRoomFailure);
            return;
        }
        localRoomWindow?.EndFurnitureEditing();
        openRoomMenuAfterHandoff = openFurnitureAfterHandoff = false;
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
        ++catalogEntryGeneration; catalogEntryLoading = false;
        if (controlCenterWindow is not null)
        { controlCenterWindow.ProductHome.RoomEntryPending = false; controlCenterWindow.ProductHome.PorchScene.CatalogInputBlocked = controlCenterWindow.ProductHome.IsRoomDialogOpen; }
        SaveRoomCheckpoint(); roomCheckpointTimer.Stop();
        var selected = localRoomSelected;
        var wasEntering = roomHandoff.Pending && !selected;
        if (stackedRuntime.Furniture?.Busy == true)
        {
            if (selectedCatalogRoomId is not null) roomCheckpointTimer.Start();
            localRoomFailure = "家具正在保存，请完成后重试。";
            // Rendering owns the commit boundary. Keep it available while a file stage is pending.
            if (localRoomWindow?.WindowState == WindowState.Minimized)
            {
                localRoomWindow.WindowState = WindowState.Normal;
                localRoomWindow.Show();
                localRoomWindow.FocusScene();
            }
            return;
        }
        if (wasEntering && !stackedRuntime.TryDetachOrdinaryFurniture())
        {
            FailLocalHandoff(roomHandoff.Generation, "家具绑定尚未卸载，暂不能隐藏入房窗口。");
            return;
        }
        roomHandoffTimeout.Stop();
        restoringRoomSource = false;
        openRoomMenuAfterHandoff = openFurnitureAfterHandoff = false;
        localRoomDriver.Stop();
        if (selected)
        {
            controlCenterWindow!.ProductHome.SetDangoAway(true);
            if (!roomControlCenterRequested) controlCenterWindow.Hide();
        }
        localRoomWindow!.Hide();
        localRoomWallpaper!.Hide();
        desktopPetRoom!.Hide();
        roomHandoff.Hide();
        if (wasEntering)
        {
            localRoomSelected = false;
            ShowAndActivate(controlCenterWindow!);
            BeginLocalHandoff(LocalRoomPresentation.Porch, enterWorld: false);
        }
    }

    private void BeginLocalHandoff(LocalRoomPresentation target, bool enterWorld)
    {
        if (roomHandoff.Pending) return;
        if (target != LocalRoomPresentation.Window || enterWorld)
        {
            if (!stackedRuntime.TryDetachOrdinaryFurniture())
            {
                localRoomFailure = stackedRuntime.Furniture?.Status ?? "家具操作尚未完成，请稍后重试。";
                trayIcon?.ShowRoomWarning(localRoomFailure);
                return;
            }
            localRoomWindow?.EndFurnitureEditing();
        }
        var attempt = roomHandoff.Begin(target);
        RefreshDangoHome();
        localRoomFailure = null;
        controlCenterWindow!.ProductHome.SuspendForRoom();
        localRoomDriver!.Stop();
        localRoomWallpaper!.StopInput();
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
                var screen = localRoomWindow?.IsVisible == true
                    ? Forms.Screen.FromHandle(new WindowInteropHelper(localRoomWindow).Handle)
                    : Forms.Screen.PrimaryScreen ?? throw new InvalidOperationException("没有可用显示器。");
                localRoomWallpaper.Show(attempt, screen);
                localRoomDriver.Start(localRoomWallpaper.Scene, attempt, enterWorld);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            FailLocalHandoff(attempt, target == LocalRoomPresentation.DesktopPet ? "桌宠房间挂接失败，请返回门廊重试。" :
            target == LocalRoomPresentation.Wallpaper ? "壁纸挂接失败，可重试或用窗口打开。" : "房间准备失败，请返回门廊重试。");
        }
    }

    private void CompleteLocalHandoff(long attempt, LocalRoomPresentation target)
    {
        if (disposed || !roomHandoff.Pending || attempt != roomHandoff.Generation || target != roomHandoff.Target) return;
        try
        {
            // Preparation and the rendered-frame acknowledgement are separate async boundaries.
            // Another process may have dissolved the target while either callback was queued.
            using var admission = target == LocalRoomPresentation.Window && selectedCatalogRoomId is { } id && roomCatalogStore is not null
                ? roomCatalogStore.AcquireRoomGuard(id, requireEditing: openFurnitureAfterHandoff) : null;
            if (!roomHandoff.TryComplete(attempt, target, true)) return;
        }
        catch (Exception error) when (RoomFileStore.IsStorageError(error))
        {
            FailLocalHandoff(attempt, "房间状态已变化或暂不可核对，请返回门廊重试。");
            return;
        }
        roomHandoffTimeout.Stop();
        controlCenterWindow!.ProductHome.RoomEntryPending = false;
        controlCenterWindow.ProductHome.PorchScene.CatalogInputBlocked = false;
        restoringRoomSource = false;
        if (target == LocalRoomPresentation.Porch)
        {
            roomCheckpointTimer.Stop();
            localRoomSelected = false;
            roomControlCenterRequested = false;
            localRoomWindow!.Hide();
            localRoomWallpaper!.Hide();
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
                if (selectedCatalogRoomId is not null) roomCheckpointTimer.Start();
                localRoomWallpaper!.Hide(); desktopPetRoom!.Hide(); localRoomWindow!.FocusScene();
                if (openRoomMenuAfterHandoff) localRoomWindow.OpenMenu();
                openRoomMenuAfterHandoff = false;
                if (openFurnitureAfterHandoff) localRoomWindow.BeginFurnitureEditing();
                openFurnitureAfterHandoff = false;
            }
            else if (target == LocalRoomPresentation.DesktopPet)
            {
                localRoomWindow!.Hide();
                localRoomWallpaper!.Hide();
                desktopPetRoom!.ConfirmReady(attempt);
            }
            else
            { localRoomWindow!.Hide(); desktopPetRoom!.Hide(); localRoomWallpaper!.ConfirmReady(attempt); }
        }
        RefreshDangoHome();
    }

    private void FailLocalHandoff(long attempt, string message)
    {
        // A late callback must not detach the furniture of a newer successful room.
        if (disposed || attempt != roomHandoff.Generation || (!roomHandoff.Pending && !localRoomSelected)) return;
        controlCenterWindow!.ProductHome.RoomEntryPending = false;
        controlCenterWindow.ProductHome.PorchScene.CatalogInputBlocked = false;
        openFurnitureAfterHandoff = false;
        if (!stackedRuntime.TryDetachOrdinaryFurniture())
        {
            localRoomFailure = stackedRuntime.Furniture?.Status ?? message;
            if (stackedRuntime.Furniture?.Busy == true) return;
            if (roomHandoff.Pending) roomHandoff.Fail(attempt);
            roomHandoffTimeout.Stop();
            openRoomMenuAfterHandoff = false;
            restoringRoomSource = false;
            localRoomDriver!.Stop();
            localRoomWallpaper!.StopInput();
            desktopPetRoom!.StopInput();
            localRoomWindow!.Scene.ShowUnavailable("家具运行状态无法确认，房间已停止。请核对保存状态后重试。");
            trayIcon?.ShowRoomWarning(localRoomFailure);
            return;
        }
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
        desktopPetRoom!.StopInput();
        if (localRoomSelected) controlCenterWindow!.ProductHome.SetDangoAway(true);
        else controlCenterWindow!.ProductHome.SuspendForRoom();
        var source = roomHandoff.Current;
        // Restore the previous presentation through a new healthy-frame handshake.
        if (source == LocalRoomPresentation.Window && localRoomSelected && !restoringRoomSource)
        {
            desktopPetRoom!.Hide();
            localRoomWallpaper!.Hide();
            restoringRoomSource = true;
            BeginLocalHandoff(LocalRoomPresentation.Window, enterWorld: true);
            localRoomFailure = message;
            trayIcon?.ShowRoomWarning(message);
        }
        else if (source == LocalRoomPresentation.Window && localRoomSelected)
        {
            desktopPetRoom!.Hide();
            restoringRoomSource = false;
            localRoomWallpaper!.Hide();
            localRoomWindow!.Show();
            localRoomWindow.FocusScene();
            trayIcon?.ShowRoomWarning(message + " 可从房门返回门廊，或隐藏后重试。");
        }
        else if (localRoomSelected && (source is LocalRoomPresentation.Wallpaper or LocalRoomPresentation.DesktopPet))
        {
            localRoomWindow!.Hide();
            localRoomWallpaper!.Hide();
            desktopPetRoom!.Hide();
            roomHandoff.Hide();
            trayIcon?.ShowRoomWarning(message + " 请从托盘恢复房间或返回房门。");
        }
        else if (source == LocalRoomPresentation.Hidden && localRoomSelected &&
            preferredRoomPresentation == LocalRoomPresentation.DesktopPet)
        {
            // A failed restoration must not erase the hidden room selection or rehost its entity into the porch.
            localRoomWindow!.Hide();
            localRoomWallpaper!.Hide();
            desktopPetRoom!.Hide();
            roomHandoff.Hide();
            ShowControlCenter();
            controlCenterWindow!.ProductHome.SetNotice("桌宠房间暂不可用 · 查看", message);
            trayIcon?.ShowRoomWarning(message + " 可从托盘恢复房间重试，或从房门返回。");
        }
        else
        {
            localRoomWindow!.Hide();
            localRoomWallpaper!.Hide();
            desktopPetRoom!.Hide();
            localRoomSelected = false;
            ShowAndActivate(controlCenterWindow);
            controlCenterWindow.ProductHome.ResumePorch();
            controlCenterWindow.ProductHome.SetNotice("房间暂不可用 · 查看", message);
        }
    }

    internal void RecoverLocalRoom()
    {
        if (disposed || LocalHandoffPending || !localRoomSelected) return;
        GetOrCreateControlCenter();
        if (!stackedRuntime.TryDetachOrdinaryFurniture())
        {
            localRoomFailure = "家具状态尚未核对，暂不能重置团子。";
            trayIcon?.ShowRoomWarning(localRoomFailure);
            return;
        }
        localRoomWindow?.EndFurnitureEditing();
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
        preset => { if (preferredRoomPresentation != LocalRoomPresentation.DesktopPet) ActiveRoomScene()?.SelectView(preset); },
        RecoverLocalRoom, () => preferredRoomPresentation == LocalRoomPresentation.DesktopPet);
    private LocalRoomSceneView? ActiveRoomScene() => roomHandoff.Current switch
    {
        LocalRoomPresentation.DesktopPet => desktopPetRoom?.Scene,
        LocalRoomPresentation.Wallpaper => localRoomWallpaper?.Scene,
        _ => localRoomWindow?.Scene,
    };
    private void DisposeLocalRoom()
    {
        SaveRoomCheckpoint(flush: true);
        roomCheckpointTimer.Stop();
        roomHandoffTimeout.Stop();
        localRoomDriver?.Dispose();
        localRoomWallpaper?.Dispose();
        desktopPetRoom?.Dispose();
        if (localRoomWindow is not null) { localRoomWindow.PrepareForExit(); localRoomWindow.Close(); }
    }

    private async Task InitializeCatalogUiAsync(ControlCenterWindow home)
    {
        try { await home.ProductHome.BindRoomCatalogAsync(roomCatalogStore!); }
        catch (Exception error) when (RoomFileStore.IsStorageError(error))
        { home.ProductHome.SetNotice("房间目录暂不可用 · 查看", "原件已保护，请从门廊重新读取目录。"); }
    }
    internal async void EnterCatalogRoom(Guid id, bool furniture)
    {
        if (disposed || roomHandoff.Pending || localRoomSelected || catalogEntryLoading || roomCatalogStore is null) return;
        GetOrCreateControlCenter();
        var home = controlCenterWindow!.ProductHome;
        if (home.IsRoomDialogOpen) return;
        if (!TryLockDangoForEntry(out var enteringDango)) return;
        var legacyDangoId = dangoRoster?.LegacyDangoId;
        catalogEntryLoading = true; home.RoomEntryPending = true; home.PorchScene.CatalogInputBlocked = true;
        var request = ++catalogEntryGeneration;
        try
        {
            await home.PauseBeltForRoomAsync();
            var previousPreparation = roomPreparationTask;
            var previousCheckpoint = roomCheckpointTask;
            var preparation = Task.Run(async () =>
            {
                try { await previousPreparation.ConfigureAwait(false); }
                catch (Exception error) when (RoomFileStore.IsStorageError(error)) { /* Only serialize IO; a failed prior room must not block another room. */ }
                await previousCheckpoint.ConfigureAwait(false);
                LocalRoomRecord room; RoomDocument<RoomResumeDocument> resume;
                using (roomCatalogStore.AcquireRoomGuard(id))
                {
                    room = roomCatalogStore.ReadCurrent().Find(id)!;
                    if (furniture && !room.CanEditOffline) throw new InvalidOperationException("离线缓存布局只读，可单人游玩。");
                    ValidateBeltRoomAdmission(roomCatalogStore.ReadLayout(id));
                    resume = roomResumeStore!.LoadForDango(id, enteringDango, legacyDangoId);
                }
                // Admission must finish module/configuration checks before writing selection.
                return (Room: room, Resume: resume);
            });
            roomPreparationTask = preparation;
            var prepared = await preparation.ConfigureAwait(false);
            await home.Dispatcher.InvokeAsync(() =>
            {
                if (disposed || request != catalogEntryGeneration || roomHandoff.Pending || localRoomSelected || !home.IsVisible ||
                    activeDangoId != enteringDango || dangoRoster?.SelectedId != enteringDango) return;
                PrepareBeltRoomEntry();
                if (!stackedRuntime.TryInitialize())
                    throw new InvalidOperationException("新身体房间模块初始化失败；原房间布局保留。");
                if ((furniture || roomCatalogStore.ReadLayout(id).Items.Count != 0) && !stackedRuntime.SupportsFurnitureCollection)
                    throw new InvalidOperationException("当前新身体模块缺少家具集合能力，原布局保留。请使用支持家具的平台和皮球模块。");
                LocalRoomRecord currentRoom;
                using (roomCatalogStore.AcquireRoomGuard(id, requireEditing: furniture))
                {
                    currentRoom = roomCatalogStore.ReadCurrent().Find(id)!;
                    ValidateBeltRoomAdmission(roomCatalogStore.ReadLayout(id));
                    localRoomDriver!.BindRoom(roomCatalogStore, currentRoom, prepared.Resume.Value);
                }
                // Save only an admitted room, outside the guard acquired by SaveSelection itself.
                var selection = roomResumeStore!.SaveSelection(id);
                selectedCatalogRoomId = id;
                localRoomWindow!.Title = "Dango · " + currentRoom.Name + "（本地单人）";
                var resumeWarning = prepared.Resume.Snapshot.Warning ?? localRoomDriver.ResumeWarning ?? selection.Warning;
                localRoomWindow.SetResumeSaveStatus(resumeWarning);
                openFurnitureAfterHandoff = furniture; roomControlCenterRequested = false;
                BeginLocalHandoff(LocalRoomPresentation.Window, enterWorld: true);
            });
        }
        catch (Exception error) when (RoomFileStore.IsStorageError(error))
        {
            await home.Dispatcher.InvokeAsync(() =>
            {
                if (disposed || request != catalogEntryGeneration) return;
                localRoomFailure = error.Message;
                home.PorchScene.SetCatalogStatus("房间暂不可用，原件保留。请核对目录后重试。");
                home.SetNotice("房间暂不可用 · 查看", error.Message);
            });
        }
        catch (OperationCanceledException) when (disposed || home.Dispatcher.HasShutdownStarted) { }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            await home.Dispatcher.InvokeAsync(() =>
            {
                if (disposed || request != catalogEntryGeneration) return;
                localRoomFailure = "新身体房间模块不可用；请核对隔离模块后重试。";
                home.SetNotice("房间暂不可用 · 查看", localRoomFailure);
            });
        }
        finally
        {
            if (!home.Dispatcher.HasShutdownStarted) await home.Dispatcher.InvokeAsync(() =>
            {
                if (request != catalogEntryGeneration) return;
                catalogEntryLoading = false;
                if (!roomHandoff.Pending)
                {
                    home.RoomEntryPending = false; home.PorchScene.CatalogInputBlocked = false;
                    if (!localRoomSelected) home.RestoreBeltAfterRoomPreparation();
                }
            });
        }
    }

    private void PrepareBeltRoomEntry()
    {
        var profile = activeDangoId is { } id ? dangoRoster?.Find(id) : null;
        if (profile is null || dangoWriteUnknown)
            throw new InvalidOperationException("无法确认入场团子，请返回首页核对资料。");
        stackedRuntime.ConfigureBeltRoomAppearance(profile.Appearance, beginRoom: true);
        // The homepage owns a separate continuous-body presenter. Transfer only
        // committed accessories when either room takes ownership of this runtime.
        stackedRuntime.SetAccessories(dangoRoster!.EffectiveAccessories(profile.Id), enabled: true);
        UpdateDangoRoomNames();
    }

    internal static void ValidateBeltRoomAdmission(Dango.Core.Furniture.FurnitureLayout layout) =>
        layout.Validate(Dango.Core.Furniture.FurnitureLayoutContract.OrdinaryRoom);
    private void SaveRoomCheckpoint(bool flush = false)
    {
        if (selectedCatalogRoomId is not { } id || activeDangoId is not { } dangoId || roomResumeStore is null ||
            localRoomDriver?.CaptureCheckpoint() is not { } checkpoint) return;
        if (!flush && !roomCheckpointTask.IsCompleted) return;
        var previous = roomCheckpointTask;
        var camera = localRoomWindow!.ResumeCameraOverride ?? checkpoint.Camera;
        var roomName = controlCenterWindow?.ProductHome.CatalogSnapshot?.Find(id)?.Name ?? "普通房";
        var ownerDispatcher = localRoomWindow!.Dispatcher;
        roomCheckpointTask = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            var result = roomResumeStore.SaveForDango(id, dangoId, checkpoint.LayoutVersion, checkpoint.Position, camera);
            if (!flush && !ownerDispatcher.HasShutdownStarted) _ = ownerDispatcher.BeginInvoke(() =>
            {
                if (disposed) return;
                if (result.State == FurnitureCommitState.Unknown)
                {
                    uncertainCheckpoint = (id, dangoId);
                    dangoWarning = "位置／相机保存结果待核对，请在首页重新读取后切换团子。";
                    RefreshDangoHome();
                }
                controlCenterWindow?.ProductHome.SetRoomCheckpointSaveStatus(id,
                    result.State == FurnitureCommitState.Committed ? null : "「" + roomName + "」" + result.Warning);
                if (selectedCatalogRoomId == id)
                    localRoomWindow?.SetResumeSaveStatus(result.State == FurnitureCommitState.Committed ? null : result.Warning);
            });
        });
        if (flush) roomCheckpointTask.GetAwaiter().GetResult();
    }
}
