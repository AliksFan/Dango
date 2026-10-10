using System.Windows.Threading;
using Dango.Desktop.AppShell.Persistence;
using Dango.Desktop.AppShell.Rooms;
using Dango.Client.Sync.Pets;
using Dango.Protocol;
using Dango.Desktop.AppShell.ControlCenter;
using System.IO;

namespace Dango.Desktop.AppShell;

internal sealed partial class DangoApplicationHost
{
    private readonly LocalRoomSaveSession localRooms;
    private readonly DispatcherTimer roomSaveTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool roomSaveFailureNotified;
    internal LocalRoomSaveSession LocalRoomSaves => localRooms;

    // B5 supplies an already authenticated bound remote. Constructing this boundary performs no network or save.
    internal PetConfigurationChoiceCoordinator CreatePetConfigurationCoordinator(IRemotePetConfigurationClient remote)
    {
        ThrowIfDisposed();
        var paths = new DangoUserDataPaths(settingsPath);
        var repository = new LocalPetConfigurationRepository(paths.AppearancePath, appearance, workerWWindow.Dispatcher,
            configuration => { if (!disposed) { stackedRuntime.SetAppearance(configuration); controlCenterWindow?.ProductHome.RefreshAccountAppearance(); } });
        var journal = new PetConfigurationFileJournal(Path.Combine(
            paths.GetAccountDirectory(remote.Origin, remote.UserId, remote.PetId), "configuration-choices"));
        return new(remote, repository, journal);
    }

    private PetConfigurationLiveSyncCoordinator CreatePetConfigurationLiveSync(IRemotePetConfigurationClient remote,
        PetConfigurationSnapshot baseline)
    {
        ThrowIfDisposed();
        var paths = new DangoUserDataPaths(settingsPath);
        var repository = new LocalPetConfigurationRepository(paths.AppearancePath, appearance, workerWWindow.Dispatcher,
            configuration => { if (!disposed) { stackedRuntime.SetAppearance(configuration); controlCenterWindow?.ProductHome.RefreshAccountAppearance(); } });
        var journal = new PetConfigurationFileJournal(Path.Combine(
            paths.GetAccountDirectory(remote.Origin, remote.UserId, remote.PetId), "configuration-sync"));
        return new(remote, repository, journal, baseline);
    }

    private void RestoreLocalRoomCheckpoint(LocalRoomPresentation target)
    {
        var saved = target == LocalRoomPresentation.DesktopPet ? localRooms.DesktopPetRoom : localRooms.OrdinaryRoom;
        localRoomDriver!.RestoreEntryPosition(saved.ResumeCenterX);
    }

    private void SaveLocalRoomCheckpoint()
    {
        if (disposed || localRoomDriver is null) return;
        // Only a confirmed healthy owner may replace a checkpoint. A failed target cannot save its transient state.
        if (!roomHandoff.Pending && localRoomSelected && localRoomDriver?.CaptureStableCenterX() is { } center)
        {
            var desktop = preferredRoomPresentation == LocalRoomPresentation.DesktopPet;
            var saved = desktop ? localRooms.DesktopPetRoom : localRooms.OrdinaryRoom;
            localRooms.UpdateRoom(saved with
            {
                ResumeCenterX = center,
                Camera = desktop ? saved.Camera : localRoomWindow!.Scene.Camera.CaptureSaveData()
            });
        }
        if (!localRooms.HasPendingSave) return;
        var success = localRooms.TrySave();
        if (!success && !roomSaveFailureNotified)
        {
            trayIcon?.ShowRoomWarning(localRooms.StatusMessage);
            controlCenterWindow?.ProductHome.SetNotice("房间尚未保存 · 查看", localRooms.StatusMessage);
        }
        roomSaveFailureNotified = !success;
    }
}
