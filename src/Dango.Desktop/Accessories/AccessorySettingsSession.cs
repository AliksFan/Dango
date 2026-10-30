using System.IO;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Accessories;

/// <summary>Explicit draft commits and independent deletion; canceled imports never touch the formal library.</summary>
public sealed class AccessorySettingsSession
{
    public const int MaximumCachedImages = 16;
    public const long MaximumPendingPixels = AccessoryConfiguration.MaximumEquippedItems * AccessoryImageAsset.MaximumPixels;
    public const long MaximumPendingPngBytes = AccessoryConfiguration.MaximumEquippedItems * AccessoryImageAsset.MaximumFileBytes;
    private readonly int ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly string settingsRoot;
    private readonly IAccessorySettingsPersistence store;
    private readonly long maximumPendingPixels;
    private readonly long maximumPendingPngBytes;
    private readonly Dictionary<string, AccessoryImageAsset> pendingAssets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AccessoryEditorProject> pendingProjects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AccessoryEditorSourceAsset> pendingSources = new(StringComparer.Ordinal);
    private readonly string sessionId = Guid.NewGuid().ToString("N");
    private long generation;
    private AccessoryEditDraft? activeEdit;
    private string manifestRevision;
    private readonly Dictionary<string, BitmapSource> images = new(StringComparer.Ordinal);
    private readonly LinkedList<string> imageRecency = new();
    private readonly Dictionary<string, string> imageWarnings = new(StringComparer.Ordinal);
    private string statusMessage = string.Empty;
    private string? cacheWarning;

    public AccessorySettingsSession(string settingsRoot, string legacyHatPresetId = "sprout", IAccessorySettingsPersistence? store = null)
        : this(settingsRoot, legacyHatPresetId, store, MaximumPendingPixels, MaximumPendingPngBytes)
    { }

    // Lower budgets allow real cumulative behavior tests without allocating hundreds of MiB.
    internal AccessorySettingsSession(string settingsRoot, string legacyHatPresetId, IAccessorySettingsPersistence? store,
        long maximumPendingPixels, long maximumPendingPngBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsRoot);
        if (maximumPendingPixels is < 1 or > MaximumPendingPixels || maximumPendingPngBytes is < 1 or > MaximumPendingPngBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingPixels));
        this.settingsRoot = Path.GetFullPath(settingsRoot);
        this.store = store ?? new AccessorySettingsStore();
        this.maximumPendingPixels = maximumPendingPixels;
        this.maximumPendingPngBytes = maximumPendingPngBytes;
        var loaded = this.store.Load(this.settingsRoot);
        manifestRevision = loaded.ManifestRevision ?? "missing";
        IsCompatibilityMode = loaded.State == AccessorySettingsLoadState.Missing;
        IsWriteProtected = loaded.State == AccessorySettingsLoadState.Protected;
        SavedConfiguration = IsCompatibilityMode ? AccessoryConfiguration.FromLegacyHat(legacyHatPresetId) : loaded.Configuration;
        DraftConfiguration = SavedConfiguration;
        StatusMessage = loaded.Warning ?? (IsCompatibilityMode
            ? "已准备内置配饰；应用后才保存新格式清单。" : "配饰已从本机读取。");
    }

    public AccessoryConfiguration SavedConfiguration { get; private set; }
    public AccessoryConfiguration DraftConfiguration { get; private set; }
    public bool IsWriteProtected { get; private set; }
    public bool IsCompatibilityMode { get; private set; }
    public AccessoryImageImportInfo? LastImportInfo { get; private set; }
    public bool IsDirty => !DraftConfiguration.Equals(SavedConfiguration);
    public bool HasPendingSave => IsDirty || IsCompatibilityMode;
    public string StatusMessage
    {
        get
        {
            VerifyAccess();
            var imageNotice = imageWarnings.Count == 0 ? string.Empty :
                $" 部分配饰图片无法显示，已隔离 {imageWarnings.Count} 张图片；其配置仍保留。";
            return statusMessage + imageNotice + (cacheWarning is null ? string.Empty : " " + cacheWarning);
        }
        private set => statusMessage = value;
    }
    internal int CachedImageCount => images.Count;
    internal int PendingImageCount => pendingAssets.Count;
    internal long PendingPixelCount => pendingAssets.Values.Sum(asset => (long)asset.PixelWidth * asset.PixelHeight);
    internal long PendingPngBytes => pendingAssets.Values.Sum(asset => asset.EncodedByteLength);

    public void Stage(AccessoryConfiguration configuration)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        DraftConfiguration = configuration;
        InvalidateEdit();
        var usedAssets = configuration.Items.Select(item => item.AssetId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in pendingAssets.Keys.Where(id => !usedAssets.Contains(id)).ToArray()) pendingAssets.Remove(id);
        PrunePending(configuration);
        foreach (var id in imageWarnings.Keys.Where(id => !usedAssets.Contains(id)).ToArray()) imageWarnings.Remove(id);
        StatusMessage = "配饰预览中；应用后才保存到本机。";
    }

    public void CancelPreview()
    {
        VerifyAccess();
        DraftConfiguration = SavedConfiguration;
        InvalidateEdit();
        pendingAssets.Clear();
        pendingProjects.Clear(); pendingSources.Clear();
        var savedAssets = SavedConfiguration.Items.Select(item => item.AssetId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in imageWarnings.Keys.Where(id => !savedAssets.Contains(id)).ToArray()) imageWarnings.Remove(id);
        StatusMessage = "已取消未保存的配饰预览。";
    }

    public bool TrySave()
    {
        VerifyAccess();
        var loaded = store.Load(settingsRoot);
        if (IsWriteProtected || loaded.State == AccessorySettingsLoadState.Protected && !loaded.IsTransientReadFailure)
        {
            IsWriteProtected = true;
            StatusMessage = "配饰未保存：原清单已保护；当前版本不提供覆盖恢复。可取消预览。";
            return false;
        }
        if (loaded.IsTransientReadFailure)
        {
            StatusMessage = "配饰未保存：原清单暂时无法读取。草稿保留，可解除文件占用后重试或取消。";
            return false;
        }
        if ((loaded.ManifestRevision ?? "missing") != manifestRevision)
        {
            StatusMessage = "配饰未保存：清单已在外部更改。草稿仍保留，请重新读取。";
            return false;
        }
        var incomplete = DraftConfiguration.Items.Where(item => item.IsPendingCompletion).Select(item => item.Name).ToArray();
        if (incomplete.Length > 0)
        {
            StatusMessage = "配饰待完成，整套草稿未保存：" + string.Join("、", incomplete) + "。请继续编辑。";
            return false;
        }
        if (!HasPendingSave) return true;
        string committedRevision;
        try
        {
            committedRevision = store.Save(settingsRoot, DraftConfiguration, pendingAssets, pendingProjects, manifestRevision);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            var afterFailure = store.Load(settingsRoot);
            IsWriteProtected = afterFailure.State == AccessorySettingsLoadState.Protected && !afterFailure.IsTransientReadFailure;
            StatusMessage = $"配饰预览仍保留，但未保存（{error.GetType().Name}）。可重试或取消。";
            return false;
        }
        // The disk commit is complete. Cache/presentation failures cannot undo it or return a save failure.
        SavedConfiguration = DraftConfiguration;
        manifestRevision = committedRevision;
        InvalidateEdit();
        IsCompatibilityMode = false;
        StatusMessage = "配饰已保存到本机。";
        cacheWarning = null;
        try
        {
            foreach (var asset in pendingAssets.Values) CacheImage(asset.AssetId, asset.Bitmap);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            images.Clear();
            imageRecency.Clear();
            cacheWarning = $"图片缓存未刷新（{error.GetType().Name}）；配置已写入，可重试画面。";
        }
        finally { pendingAssets.Clear(); pendingProjects.Clear(); pendingSources.Clear(); }
        return true;
    }

    public bool TryDeleteItem(string id)
    {
        VerifyAccess();
        var wasSaved = SavedConfiguration.Items.Any(item => item.Id == id);
        if (!wasSaved && !DraftConfiguration.Items.Any(item => item.Id == id))
        {
            StatusMessage = "配饰未删除：要删除的配饰不存在。";
            return false;
        }
        var loaded = store.Load(settingsRoot);
        if (IsWriteProtected || loaded.State == AccessorySettingsLoadState.Protected && !loaded.IsTransientReadFailure)
        {
            IsWriteProtected = true;
            StatusMessage = "配饰未删除：原清单已保护；当前版本不提供覆盖恢复。草稿仍保留。";
            return false;
        }
        if (loaded.IsTransientReadFailure)
        {
            StatusMessage = "配饰未删除：原清单暂时无法读取。草稿仍保留，可解除文件占用后重试。";
            return false;
        }
        if ((loaded.ManifestRevision ?? "missing") != manifestRevision)
        {
            StatusMessage = "配饰未删除：清单已在外部更改。草稿仍保留，请重新读取后再删除。";
            return false;
        }
        var nextSaved = wasSaved ? new AccessoryConfiguration(SavedConfiguration.Items.Where(item => item.Id != id)) : SavedConfiguration;
        var nextDraft = new AccessoryConfiguration(DraftConfiguration.Items.Where(item => item.Id != id));
        if (wasSaved)
        {
            try
            {
                // Deletion commits only the saved snapshot minus this ID, never pending images or other draft edits.
                manifestRevision = store.Save(settingsRoot, nextSaved, expectedManifestRevision: manifestRevision);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                var afterFailure = store.Load(settingsRoot);
                IsWriteProtected = afterFailure.State == AccessorySettingsLoadState.Protected && !afterFailure.IsTransientReadFailure;
                StatusMessage = $"配饰未删除（{error.GetType().Name}）。原配置及草稿仍保留，可重试。";
                return false;
            }
        }
        // Store.Save also confirms an atomic commit when a writer throws after replacing the manifest.
        SavedConfiguration = nextSaved;
        DraftConfiguration = nextDraft;
        InvalidateEdit();
        PrunePending(nextDraft);
        if (wasSaved) IsCompatibilityMode = false;
        var usedAssets = nextDraft.Items.Select(item => item.AssetId).ToHashSet(StringComparer.Ordinal);
        foreach (var assetId in pendingAssets.Keys.Where(assetId => !usedAssets.Contains(assetId)).ToArray()) pendingAssets.Remove(assetId);
        foreach (var assetId in imageWarnings.Keys.Where(assetId => !usedAssets.Contains(assetId)).ToArray()) imageWarnings.Remove(assetId);
        StatusMessage = wasSaved ? "配饰已删除；其他未应用修改仍保留。" : "已删除未应用的配饰；其他草稿仍保留。";
        return true;
    }

    public AccessoryItem ImportPng(string path)
    {
        VerifyAccess();
        LastImportInfo = null;
        var asset = AccessoryImageAsset.ImportPng(path);
        if (!pendingAssets.ContainsKey(asset.AssetId) &&
            (PendingPixelCount + (long)asset.PixelWidth * asset.PixelHeight > maximumPendingPixels ||
             PendingPngBytes + asset.EncodedByteLength > maximumPendingPngBytes))
        {
            StatusMessage = "未导入：未应用图片达到累计资源上限。请先应用或取消当前草稿，再导入。";
            throw new InvalidDataException(StatusMessage);
        }
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name)) name = "新配饰";
        if (name.Length > 128) name = name[..128];
        var equip = DraftConfiguration.Items.Count(item => item.IsEquipped) < AccessoryConfiguration.MaximumEquippedItems;
        var item = new AccessoryItem(Guid.NewGuid().ToString("N"), name, asset.AssetId, asset.PixelWidth, asset.PixelHeight)
        {
            IsEquipped = equip,
            Order = NextOrder(AccessoryLayer.Front),
        };
        var project = AccessoryEditorProject.FromImage(asset);
        item = item with { ProjectId = project.ProjectId, ProjectVersion = project.Version };
        CheckProjectBudget(project);
        var next = new AccessoryConfiguration(DraftConfiguration.Items.Append(item));
        pendingAssets.TryAdd(asset.AssetId, asset);
        pendingProjects.TryAdd(project.ProjectId, project);
        images.Remove(asset.AssetId);
        imageRecency.Remove(asset.AssetId);
        Stage(next);
        LastImportInfo = asset.ImportInfo;
        if (!equip) StatusMessage = "图片已导入草稿；已佩戴 8 件，新配饰暂未佩戴。应用后才保存。";
        return item;
    }

    public AccessoryItem Duplicate(string id)
    {
        VerifyAccess();
        var original = DraftConfiguration.Items.SingleOrDefault(item => item.Id == id) ??
            throw new InvalidDataException("要复制的配饰不存在。");
        var equip = original.IsEquipped && DraftConfiguration.Items.Count(item => item.IsEquipped) < AccessoryConfiguration.MaximumEquippedItems;
        var name = original.Name.Length > 125 ? original.Name[..125] : original.Name;
        var item = original with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name + " 副本",
            Order = NextOrder(original.Layer),
            IsEquipped = equip,
        };
        Stage(new AccessoryConfiguration(DraftConfiguration.Items.Append(item)));
        if (original.IsEquipped && !equip) StatusMessage = "已复制到草稿；已佩戴 8 件，副本暂未佩戴。应用后才保存。";
        return item;
    }

    /// <summary>Create an incomplete empty project in memory; only a complete editor result can be saved.</summary>
    public AccessoryItem CreateBlank()
    {
        VerifyAccess();
        const int edge = 512;
        var bitmap = BitmapSource.Create(edge, edge, 96, 96, System.Windows.Media.PixelFormats.Bgra32,
            null, new byte[edge * edge * 4], edge * 4);
        var source = AccessoryEditorSourceAsset.FromBitmap(bitmap);
        if (!pendingSources.ContainsKey(source.AssetId) &&
            (pendingSources.Values.Sum(a => (long)a.PixelWidth * a.PixelHeight) + (long)edge * edge > AccessoryEditorProject.MaximumPendingSourcePixels ||
             pendingSources.Values.Sum(a => a.EncodedByteLength) + source.EncodedByteLength > AccessoryEditorProject.MaximumPendingSourceBytes))
            throw new InvalidDataException("未应用源素材达到累计资源上限。请先应用或取消当前草稿。");
        var project = new AccessoryEditorProject(edge, edge, 0, source.AssetId,
            [new("layer-1", "空白图层", null, 0, 0, 0, 0, Type: null)], [], name: "新配饰");
        CheckProjectBudget(project);
        var item = new AccessoryItem(Guid.NewGuid().ToString("N"), project.Name, source.AssetId, edge, edge)
        {
            IsPendingCompletion = true,
            ProjectId = project.ProjectId, ProjectVersion = project.Version,
            IsEquipped = DraftConfiguration.Items.Count(i => i.IsEquipped) < AccessoryConfiguration.MaximumEquippedItems,
            Order = NextOrder(AccessoryLayer.Front),
        };
        var next = new AccessoryConfiguration(DraftConfiguration.Items.Append(item));
        pendingSources.TryAdd(source.AssetId, source);
        pendingProjects.TryAdd(project.ProjectId, project);
        Stage(next);
        LastImportInfo = null;
        StatusMessage = "空白配饰已加入待完成草稿；编辑并导出有效成品后应用。";
        return item;
    }

    /// <summary>A successful import creates an incomplete outer draft immediately; no filesystem writes occur.</summary>
    public AccessoryItem ImportSource(string path)
    {
        VerifyAccess();
        LastImportInfo = null;
        var source = AccessoryEditorSourceAsset.Import(path);
        if (!pendingSources.ContainsKey(source.AssetId) &&
            (pendingSources.Values.Sum(a => (long)a.PixelWidth * a.PixelHeight) + (long)source.PixelWidth * source.PixelHeight > AccessoryEditorProject.MaximumPendingSourcePixels ||
             pendingSources.Values.Sum(a => a.EncodedByteLength) + source.EncodedByteLength > AccessoryEditorProject.MaximumPendingSourceBytes))
            throw new InvalidDataException("未应用源素材达到累计资源上限。请先应用或取消当前草稿。");
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name)) name = "新配饰";
        if (name.Length > 128) name = name[..128];
        var item = new AccessoryItem(Guid.NewGuid().ToString("N"), name, source.AssetId, source.PixelWidth, source.PixelHeight)
        {
            IsPendingCompletion = true,
            IsEquipped = DraftConfiguration.Items.Count(i => i.IsEquipped) < AccessoryConfiguration.MaximumEquippedItems,
            Order = NextOrder(AccessoryLayer.Front),
        };
        var next = new AccessoryConfiguration(DraftConfiguration.Items.Append(item));
        pendingSources.TryAdd(source.AssetId, source);
        Stage(next);
        LastImportInfo = source.ImportInfo;
        StatusMessage = "素材已加入待完成草稿；请继续编辑并导出有效成品后应用。";
        return item;
    }

    public AccessoryEditorSourceAsset? GetPendingSource(string assetId)
    { VerifyAccess(); return pendingSources.GetValueOrDefault(assetId); }

    public AccessoryEditorProject? TryGetProject(string itemId)
    {
        VerifyAccess();
        var item = DraftConfiguration.Items.SingleOrDefault(i => i.Id == itemId);
        return item is null ? null : TryGetProject(item);
    }

    /// <summary>Resolve the immutable item's exact project, including saved/draft versions of the same ID.</summary>
    public AccessoryEditorProject? TryGetProject(AccessoryItem item)
    {
        VerifyAccess();
        if (item.ProjectId is not { } projectId) return null;
        try
        {
            var project = pendingProjects.GetValueOrDefault(projectId) ?? store.LoadProject(settingsRoot, projectId);
            if (project.Version != item.ProjectVersion || project.Revision != item.EditRevision || project.FinalAssetId != item.AssetId ||
                project.PixelWidth != item.PixelWidth || project.PixelHeight != item.PixelHeight)
                throw new InvalidDataException("工程引用与配饰不一致。");
            return project;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or FormatException)
        {
            StatusMessage = $"配饰工程已隔离（{error.GetType().Name}）；原条目保留，可重新选择素材或取消。";
            return null;
        }
    }

    public AccessoryEditDraft BeginEdit(string itemId)
    {
        VerifyAccess();
        var item = DraftConfiguration.Items.SingleOrDefault(i => i.Id == itemId) ?? throw new InvalidDataException("要编辑的配饰不存在。");
        InvalidateEdit();
        return activeEdit = new(sessionId, generation, item.Id, checked(item.EditRevision + 1), item);
    }

    public void CancelEdit(AccessoryEditDraft draft)
    {
        VerifyAccess();
        if (activeEdit == draft) InvalidateEdit();
        // The outer draft was never changed, including a newly selected incomplete item.
    }

    public bool TryCompleteEdit(AccessoryEditDraft draft, AccessoryEditResult result)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(draft); ArgumentNullException.ThrowIfNull(result);
        if (activeEdit != draft || draft.SessionId != sessionId || draft.Generation != generation ||
            result.Item.Id != draft.ItemId || result.Item.EditRevision != draft.Revision ||
            !DraftConfiguration.Items.Any(i => i.Id == draft.ItemId && i.Equals(draft.Original))) return false;
        result.Project.ValidateResult(result.Item, result.Image);
        CheckImageBudget(result.Image); CheckProjectBudget(result.Project);
        var next = new AccessoryConfiguration(DraftConfiguration.Items.Select(i => i.Id == draft.ItemId ? result.Item : i));
        pendingAssets.TryAdd(result.Image.AssetId, result.Image);
        pendingProjects.TryAdd(result.Project.ProjectId, result.Project);
        Stage(next);
        StatusMessage = "完整编辑结果已加入配饰草稿；应用后才保存到本机。";
        return true;
    }

    private void CheckImageBudget(AccessoryImageAsset asset)
    {
        if (!pendingAssets.ContainsKey(asset.AssetId) &&
            (PendingPixelCount + (long)asset.PixelWidth * asset.PixelHeight > maximumPendingPixels || PendingPngBytes + asset.EncodedByteLength > maximumPendingPngBytes))
            throw new InvalidDataException("未应用成品达到累计资源上限；当前草稿保留。");
    }

    private void CheckProjectBudget(AccessoryEditorProject project)
    {
        if (!pendingProjects.ContainsKey(project.ProjectId) &&
            (pendingProjects.Values.Sum(p => p.EncodedByteLength) + project.EncodedByteLength > AccessoryEditorProject.MaximumPendingProjectBytes ||
             pendingProjects.Values.Sum(p => p.DecodedLayerPixels) + project.DecodedLayerPixels > AccessoryEditorProject.MaximumPendingProjectPixels))
            throw new InvalidDataException("未应用工程达到累计资源上限；当前草稿保留。");
    }

    private void InvalidateEdit() { generation = checked(generation + 1); activeEdit = null; }

    private void PrunePending(AccessoryConfiguration configuration)
    {
        var projects = configuration.Items.Select(i => i.ProjectId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in pendingProjects.Keys.Where(id => !projects.Contains(id)).ToArray()) pendingProjects.Remove(id);
        var sources = configuration.Items.Where(i => i.IsPendingCompletion).Select(i => i.AssetId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in pendingSources.Keys.Where(id => !sources.Contains(id)).ToArray()) pendingSources.Remove(id);
    }

    public BitmapSource? TryGetImage(string assetId)
    {
        VerifyAccess();
        if (AccessoryItem.IsBuiltInAsset(assetId)) return null;
        try
        {
            BitmapSource image;
            if (pendingSources.TryGetValue(assetId, out var source)) image = source.Bitmap;
            else if (pendingAssets.TryGetValue(assetId, out var pending)) image = pending.Bitmap;
            else if (images.TryGetValue(assetId, out var cached))
            {
                image = cached;
                TouchImage(assetId);
            }
            else image = store.LoadImage(settingsRoot, assetId).Bitmap;
            if (DraftConfiguration.Items.Where(item => item.AssetId == assetId).Any(item =>
                item.PixelWidth != image.PixelWidth || item.PixelHeight != image.PixelHeight))
                throw new InvalidDataException("配饰图片尺寸与清单不一致。");
            if (!pendingAssets.ContainsKey(assetId)) CacheImage(assetId, image);
            imageWarnings.Remove(assetId);
            cacheWarning = null;
            return image;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            if (DraftConfiguration.Items.Any(item => item.AssetId == assetId)) imageWarnings[assetId] = error.GetType().Name;
            return null;
        }
    }

    private void CacheImage(string assetId, BitmapSource bitmap)
    {
        images[assetId] = bitmap;
        TouchImage(assetId);
        while (images.Count > MaximumCachedImages)
        {
            var equippedAssets = DraftConfiguration.Items.Where(item => item.IsEquipped)
                .Select(item => item.AssetId).ToHashSet(StringComparer.Ordinal);
            var oldest = imageRecency.First;
            while (oldest is not null && equippedAssets.Contains(oldest.Value)) oldest = oldest.Next;
            oldest ??= imageRecency.First!;
            images.Remove(oldest.Value);
            imageRecency.Remove(oldest);
        }
    }

    private void TouchImage(string assetId)
    {
        imageRecency.Remove(assetId);
        imageRecency.AddLast(assetId);
    }

    private int NextOrder(AccessoryLayer layer)
    {
        var maximum = DraftConfiguration.Items.Where(item => item.Layer == layer).Select(item => item.Order).DefaultIfEmpty(-1).Max();
        if (maximum == int.MaxValue) throw new InvalidDataException("配饰排序需要先调整。");
        return maximum + 1;
    }

    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != ownerThreadId)
            throw new InvalidOperationException("配饰会话须在创建线程使用；冻结图片可跨线程读取。");
    }
}
