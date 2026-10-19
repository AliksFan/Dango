using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.AppShell;

internal sealed partial class DangoApplicationHost
{
    internal ProductSettingsSceneSnapshot ReadProductSettingsScene()
    {
        if (beltBodyReview) return controlCenterWindow?.ProductHome.ReadBeltBodySettingsScene()
            ?? new("新身体 · 单人捏脸", "等待窗口启动");
        var pending = LocalHandoffPending;
        var room = localRoomSelected;
        var presentation = roomHandoff.Pending ? roomHandoff.Target
            : catalogEntryLoading ? LocalRoomPresentation.Window : preferredRoomPresentation;
        var name = room || pending ? presentation switch
        {
            LocalRoomPresentation.DesktopPet => "桌宠房 · 本地单人",
            LocalRoomPresentation.Window => "普通房 · 窗口",
            LocalRoomPresentation.Wallpaper => "普通房 · 壁纸",
            _ => "门廊",
        } : controlCenterWindow?.ProductHome.IsInPorch == true ? "门廊" : "我的团子";
        var ready = stackedRuntime.InitializationState == SceneInitializationState.Ready;
        var busy = stackedRuntime.Furniture?.Busy == true;
        var error = localRoomFailure ?? stackedRuntime.LastError ?? stackedRuntime.AttachmentError ?? stackedRuntime.QuietIdleError
            ?? stackedRuntime.LightInteractionError ?? stackedRuntime.GaitError
            ?? (!room ? controlCenterWindow?.ProductHome.CurrentSurfaceFailure : null);
        var hidden = room && roomHandoff.Current == LocalRoomPresentation.Hidden;
        var surfaceReady = room && (presentation == LocalRoomPresentation.DesktopPet
            ? desktopPetRoom?.IsReady == true && desktopPetRoom.IsVisible
            : localRoomDriver?.IsReady == true && ActiveRoomScene()?.HasHealthyFrame == true);
        var status = pending ? "正在切换场景，请稍候" : busy ? "正在保存家具，请稍候"
            : error is not null ? "需要恢复" : hidden ? "暂时隐藏，保留本次房间状态"
            : !ready ? "团子尚未启动" : room ? surfaceReady ? "正在运行" : "等待房间画面就绪" : "场景已就绪";
        var canAct = !disposed && !pending && !busy;
        return new(name, status, error,
            CanRestoreDisplay: canAct && room && ready && (hidden || presentation == LocalRoomPresentation.Window && localRoomWindow?.WindowState == System.Windows.WindowState.Minimized),
            CanReinitialize: canAct && (!ready || error is not null),
            Diagnostics: $"场景切换：{pending}\n房间已选择：{room}\n房间隐藏：{hidden}\n房间画面就绪：{surfaceReady}\n物理已初始化：{ready}\n房间持有帧驱动：{localRoomDriver?.OwnsRuntime == true}\n家具保存中：{busy}",
            Revision: roomHandoff.Generation);
    }

    private void RestoreRoomDisplayFromSettings()
    {
        if (!ReadProductSettingsScene().CanRestoreDisplay) return;
        RestoreLocalRoom();
    }

    private void ReinitializeSceneFromSettings()
    {
        if (beltBodyReview) { controlCenterWindow?.ProductHome.RecoverBeltBody(); return; }
        if (!ReadProductSettingsScene().CanReinitialize) return;
        if (localRoomSelected) { RecoverLocalRoom(); return; }
        // A home/porch failure must not enter the remembered room as a side effect.
        if (!stackedRuntime.TryDetachOrdinaryFurniture())
        {
            localRoomFailure = "家具状态尚未核对，请完成当前操作后重试。";
            return;
        }
        controlCenterWindow!.ProductHome.SuspendForRoom();
        if (!stackedRuntime.TryInitialize())
        {
            localRoomFailure = stackedRuntime.LastError ?? "团子初始化失败，请重试。";
            return;
        }
        stackedRuntime.RegenerateDango();
        localRoomFailure = null;
        controlCenterWindow.ProductHome.ResumeAfterSettingsRecovery();
    }
}
