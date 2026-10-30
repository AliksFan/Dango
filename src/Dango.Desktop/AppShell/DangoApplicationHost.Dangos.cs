using System.IO;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Dangos;
using Dango.Desktop.Furniture;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.AppShell;

internal sealed partial class DangoApplicationHost
{
    private DangoRosterStore? dangoStore;
    private DangoRosterSnapshot? dangoRoster;
    private bool dangoWriteUnknown, dangoSelectionPending;
    private string? dangoWarning;
    private Guid? activeDangoId;
    private Guid? preparingDangoId;
    private long dangoOperationGeneration;
    private Guid creationOperationId;
    private (Guid RoomId, Guid DangoId)? uncertainCheckpoint;
    private object? dangoPersistenceToken;

    internal DangoRosterSnapshot? DangoRoster => dangoRoster;
    internal Guid? ActiveDangoId => activeDangoId;
    private string DataRoot => Path.GetDirectoryName(Path.GetFullPath(settingsPath))!;

    private void InitializeDangos()
    {
        dangoStore = new(DataRoot);
        ReadDangoRoster();
        AdoptDangoSessions(CreateDangoSessions(dangoRoster?.SelectedId));
    }

    private void ReadDangoRoster()
    {
        var loaded = dangoStore!.LoadOrInitialize();
        dangoRoster = loaded.Snapshot;
        dangoWriteUnknown = loaded.IsProtected || loaded.Snapshot is null;
        dangoWarning = loaded.Warning;
    }

    private sealed record DangoEditorBinding(AppearanceSettingsSession Appearance,
        AccessorySettingsSession Accessories, object Token);

    private void AdoptDangoSessions(DangoEditorBinding binding)
    {
        appearance = binding.Appearance;
        accessories = binding.Accessories;
        dangoPersistenceToken = binding.Token;
    }

    private DangoEditorBinding CreateDangoSessions(Guid? id)
    {
        var bindingToken = new object();
        var appearancePersistence = new BoundAppearancePersistence(
            () =>
            {
                var profile = id is { } current ? dangoRoster?.Find(current) : null;
                return new(new AppearanceSettings(profile?.Appearance ?? DangoAppearanceConfiguration.Default),
                    id.HasValue ? dangoWarning : null,
                    PreserveOriginal: id.HasValue && (dangoWriteUnknown || profile is null),
                    RequiresMigration: profile?.RequiresBodyPreview == true,
                    SourceBodyPhysicsModel: profile?.RequiresBodyPreview == true
                        ? DangoBodyMaterial.LegacyModelVersion : profile?.Appearance.BodyPhysicsModel);
            }, configuration =>
            {
                if (id is not { } target || !CanSaveDango(target, bindingToken))
                    throw new InvalidDataException("当前团子不可保存，请返回首页核对资料。");
                RequireDangoCommit(dangoStore!.SaveAppearance(dangoRoster!, target, configuration, Guid.NewGuid()));
            });
        var accessoryPersistence = new BoundAccessoryPersistence(
            () => id is { } target && dangoRoster?.Find(target) is not null
                ? new(dangoRoster.EffectiveAccessories(target),
                    dangoWriteUnknown ? AccessorySettingsLoadState.Protected : AccessorySettingsLoadState.Valid,
                    dangoWarning, IsTransientReadFailure: dangoWriteUnknown, ManifestRevision: AccessoryRevision(dangoRoster, target))
                : new(AccessoryConfiguration.Empty, AccessorySettingsLoadState.Valid),
            (configuration, assets, projects) =>
            {
                if (id is not { } target || !CanSaveDango(target, bindingToken))
                    throw new InvalidDataException("当前团子不可保存，请返回首页核对资料。");
                var result = dangoStore!.SaveAccessories(dangoRoster!, target, configuration, assets, Guid.NewGuid(), projects);
                RequireDangoCommit(result);
                return AccessoryRevision(result.Snapshot!, target);
            });
        // These paths are session labels only; the adapters publish exclusively to the roster.
        var label = Path.Combine(DataRoot, "dangos", id?.ToString("D") ?? "draft", "appearance.json");
        return new(new(label, appearancePersistence, (_, _) =>
            throw new IOException("多团子资料受保护，请重新读取；不能用单对象恢复覆盖目录。")),
            new(DataRoot, store: accessoryPersistence), bindingToken);
    }

    private bool CanSaveDango(Guid id, object token) => ReferenceEquals(token, dangoPersistenceToken) &&
        !disposed && !dangoWriteUnknown && !dangoSelectionPending &&
        dangoRoster?.SelectedId == id && dangoRoster.Find(id) is not null &&
        !localRoomSelected && !roomHandoff.Pending && !catalogEntryLoading &&
        controlCenterWindow?.ProductHome is { IsDangoAway: false, IsInPorch: false, IsDangoCreating: false };

    private void BindDangoHome(ControlCenterWindow window)
    {
        var home = window.ProductHome;
        home.DangoSlotRequested += OnDangoSlotRequested;
        home.DangoCreateRequested += OnDangoCreateRequested;
        home.DangoCreationCancelled += () => { creationOperationId = Guid.Empty; RefreshDangoHome(); };
        home.DangoRenameRequested += OnDangoRenameRequested;
        home.DangoDeleteRequested += OnDangoDeleteRequested;
        home.DangoReloadRequested += ReloadDangos;
        home.OrdinaryRoomNameSourceRequested += OnOrdinaryRoomNameSourceRequested;
        RefreshDangoHome();
    }

    private void ReleaseDangoAtSafeHome()
    {
        if (!localRoomSelected && !roomHandoff.Pending && !catalogEntryLoading &&
            roomHandoff.Current == LocalRoomPresentation.Porch && localRoomDriver?.OwnsRuntime != true &&
            controlCenterWindow?.ProductHome is { IsInPorch: false, IsDangoAway: false }) activeDangoId = null;
    }

    private bool CanChangeDango()
    {
        ReleaseDangoAtSafeHome();
        return !disposed && !dangoWriteUnknown && !dangoSelectionPending && uncertainCheckpoint is null &&
            roomCheckpointTask.IsCompleted && dangoRoster is not null &&
            activeDangoId is null && !localRoomSelected && !roomHandoff.Pending && !catalogEntryLoading &&
            controlCenterWindow?.ProductHome is { IsEditing: false, IsInPorch: false,
                IsDangoAway: false, IsSettingsOpen: false, IsDangoCreating: false };
    }

    private void RefreshDangoHome()
    {
        if (controlCenterWindow?.ProductHome is not { } home || uiMode != ApplicationUiMode.Product) return;
        var slots = Enumerable.Range(0, 3).Select(index =>
        {
            var id = dangoRoster?.Slots[index];
            return new DangoSlotView(index + 1, id, id is { } value ? dangoRoster?.Find(value)?.Name : null,
                id.HasValue && id == dangoRoster?.SelectedId);
        }).ToArray();
        var blocked = dangoWriteUnknown || dangoSelectionPending || uncertainCheckpoint is not null ||
            localRoomSelected || roomHandoff.Pending || catalogEntryLoading;
        var selectedName = dangoRoster?.SelectedId is { } selected ? dangoRoster.Find(selected)?.Name : null;
        var caption = dangoSelectionPending && preparingDangoId is { } preparing
            ? "正在准备：" + (dangoRoster?.Find(preparing)?.Name ?? "待起名团子") : selectedName;
        home.ConfigureDangoSlots(slots, dangoRoster?.SelectedId, caption, blocked, dangoWarning);
        home.SetOrdinaryRoomNamePreference(dangoRoster?.NameSource == OrdinaryRoomNameSource.AccountDisplayName,
            accountAvailable: false, warning: "账号显示名尚未接入；普通房可显示团子名字。");
        UpdateDangoRoomNames();
    }

    private async void OnDangoSlotRequested(int index)
    {
        if (index is < 0 or > 2 || !CanChangeDango()) return;
        var home = controlCenterWindow!.ProductHome;
        if (dangoRoster!.Slots[index] is not { } id)
        {
            creationOperationId = Guid.NewGuid();
            var draft = CreateDangoSessions(null);
            home.BeginDangoCreation(index, draft.Appearance, draft.Accessories);
            return;
        }
        if (dangoRoster.SelectedId == id) return;
        var origin = dangoRoster;
        var generation = ++dangoOperationGeneration;
        var candidate = CreateDangoSessions(id);
        preparingDangoId = id;
        dangoSelectionPending = true;
        RefreshDangoHome();
        try
        {
            if (!await home.PrepareDangoPreviewAsync(candidate.Appearance, candidate.Accessories))
            {
                if (generation == dangoOperationGeneration)
                    dangoWarning = "团子预览准备失败，原选择保留；可重新选择重试。";
                return;
            }
            if (disposed || generation != dangoOperationGeneration) return;
            var result = dangoStore!.Select(origin, id, Guid.NewGuid());
            if (AcceptDangoCommit(result)) AdoptDangoSessions(candidate);
            else RestoreSelectedDangoSessions();
        }
        catch (Exception error) when (Rooms.Storage.RoomFileStore.IsStorageError(error))
        {
            dangoWarning = "切换未完成，原选择保留（" + error.GetType().Name + "）。";
            RestoreSelectedDangoSessions();
        }
        finally
        {
            if (generation == dangoOperationGeneration)
            {
                dangoSelectionPending = false;
                preparingDangoId = null;
                RefreshDangoHome();
            }
        }
    }

    private void OnDangoCreateRequested(int index, string name, DangoAppearanceConfiguration configuration)
    {
        if (disposed || dangoWriteUnknown || dangoSelectionPending || dangoRoster is null ||
            localRoomSelected || roomHandoff.Pending || catalogEntryLoading ||
            controlCenterWindow?.ProductHome is not { IsDangoCreating: true } home ||
            home.BoundAppearance?.RequiresBodyPreview != false) return;
        if (creationOperationId == Guid.Empty) return;
        var result = dangoStore!.Create(dangoRoster, index, name, configuration, creationOperationId);
        if (!AcceptDangoCommit(result)) return;
        home.EndDangoCreation();
        creationOperationId = Guid.Empty;
        RestoreSelectedDangoSessions();
        RefreshDangoHome();
    }

    private void OnDangoRenameRequested(Guid id, string name)
    {
        if (!CanChangeDango() || dangoRoster!.SelectedId != id) return;
        AcceptDangoCommit(dangoStore!.Rename(dangoRoster, id, name, Guid.NewGuid()));
    }

    private void OnDangoDeleteRequested(Guid id)
    {
        if (!CanChangeDango() || dangoRoster!.SelectedId != id) return;
        if (!AcceptDangoCommit(dangoStore!.Delete(dangoRoster, id, Guid.NewGuid()))) return;
        RestoreSelectedDangoSessions();
        RefreshDangoHome();
    }

    private void OnOrdinaryRoomNameSourceRequested(bool account)
    {
        if (disposed || dangoWriteUnknown || dangoSelectionPending || dangoRoster is null) return;
        if (account)
        {
            dangoWarning = "账号显示名尚不可用，未改变当前名字来源。";
            RefreshDangoHome();
            return;
        }
        AcceptDangoCommit(dangoStore!.SetNameSource(dangoRoster, OrdinaryRoomNameSource.DangoName, Guid.NewGuid()));
    }

    private void ReloadDangos()
    {
        if (disposed || dangoSelectionPending || localRoomSelected || roomHandoff.Pending || catalogEntryLoading ||
            controlCenterWindow?.ProductHome is not { IsInPorch: false } home) return;
        ++dangoOperationGeneration;
        if (uncertainCheckpoint is { } checkpoint)
        {
            var observed = roomResumeStore?.LoadForDango(checkpoint.RoomId, checkpoint.DangoId, dangoRoster?.LegacyDangoId);
            if (observed?.Valid != true)
            {
                dangoWarning = observed?.Snapshot.Warning ?? "位置／相机仍无法核对，暂不能切换团子。";
                RefreshDangoHome();
                return;
            }
            uncertainCheckpoint = null;
        }
        ReadDangoRoster();
        if (!dangoWriteUnknown)
        {
            home.EndDangoCreation();
            creationOperationId = Guid.Empty;
            RestoreSelectedDangoSessions();
        }
        RefreshDangoHome();
    }

    private void RestoreSelectedDangoSessions()
    {
        AdoptDangoSessions(CreateDangoSessions(dangoRoster?.SelectedId));
        controlCenterWindow?.ProductHome.ReplaceDangoSessions(appearance, accessories);
    }

    private bool AcceptDangoCommit(DangoRosterWriteResult result)
    {
        if (result.State == FurnitureCommitState.Committed && result.Snapshot is { } committed)
        {
            dangoRoster = committed;
            dangoWriteUnknown = false;
            dangoWarning = result.Warning;
            RefreshDangoHome();
            return true;
        }
        dangoWarning = result.Warning ?? "团子资料未保存，原件保留。";
        if (result.State == FurnitureCommitState.Unknown) dangoWriteUnknown = true;
        RefreshDangoHome();
        return false;
    }

    private void RequireDangoCommit(DangoRosterWriteResult result)
    {
        if (!AcceptDangoCommit(result)) throw new IOException(dangoWarning);
    }

    private bool TryLockDangoForEntry(out Guid id)
    {
        id = default;
        if (dangoRoster?.SelectedId is not { } selected || dangoRoster.Find(selected) is not { } profile ||
            dangoWriteUnknown || dangoSelectionPending || uncertainCheckpoint is not null || !roomCheckpointTask.IsCompleted ||
            controlCenterWindow?.ProductHome.IsDangoCreating == true ||
            string.IsNullOrWhiteSpace(profile.Name) || appearance.RequiresBodyPreview ||
            activeDangoId is { } active && active != selected)
        {
            dangoWarning = "请先在首页选择并命名团子，等待外观预览就绪。";
            RefreshDangoHome();
            return false;
        }
        activeDangoId = selected;
        id = selected;
        return true;
    }

    private void UpdateDangoRoomNames()
    {
        var id = activeDangoId ?? dangoRoster?.SelectedId;
        var name = dangoRoster?.NameSource == OrdinaryRoomNameSource.DangoName && id is { } value
            ? dangoRoster.Find(value)?.Name : null;
        localRoomWindow?.Scene.SetDangoName(name);
        localRoomWallpaper?.Scene.SetDangoName(name);
        desktopPetRoom?.Scene.SetDangoName(null);
    }

    private sealed class BoundAppearancePersistence(Func<AppearanceSettingsLoadResult> read,
        Action<DangoAppearanceConfiguration> save) : IAppearanceSettingsPersistence
    {
        public AppearanceSettingsLoadResult LoadOrDefault(string path) => read();
        public void Save(string path, DangoAppearanceConfiguration configuration) => save(configuration);
    }

    private static string AccessoryRevision(DangoRosterSnapshot roster, Guid id) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            Rooms.Storage.RoomJson.Write(new { DangoId = id, Items = roster.EffectiveAccessories(id).Items })));

    private sealed class BoundAccessoryPersistence(Func<AccessorySettingsLoadResult> read,
        Func<AccessoryConfiguration, IReadOnlyDictionary<string, AccessoryImageAsset>?,
            IReadOnlyDictionary<string, AccessoryEditorProject>?, string> save) : IAccessorySettingsPersistence
    {
        public AccessorySettingsLoadResult Load(string settingsRoot) => read();
        public string Save(string settingsRoot, AccessoryConfiguration configuration,
            IReadOnlyDictionary<string, AccessoryImageAsset>? pendingAssets = null,
            IReadOnlyDictionary<string, AccessoryEditorProject>? pendingProjects = null, string? expectedManifestRevision = null)
        {
            if (expectedManifestRevision is not null && expectedManifestRevision != (read().ManifestRevision ?? "missing"))
                throw new InvalidDataException("团子配饰资料已变化，请重新读取。");
            return save(configuration, pendingAssets, pendingProjects);
        }
        public AccessoryImageAsset LoadImage(string settingsRoot, string assetId) => new AccessorySettingsStore().LoadImage(settingsRoot, assetId);
        public AccessoryEditorProject LoadProject(string settingsRoot, string projectId) => new AccessorySettingsStore().LoadProject(settingsRoot, projectId);
    }
}
