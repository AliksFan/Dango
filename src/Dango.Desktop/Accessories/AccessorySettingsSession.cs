using System.IO;
using System.Windows.Media.Imaging;

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
        IsCompatibilityMode = loaded.State == AccessorySettingsLoadState.Missing;
        IsWriteProtected = loaded.State == AccessorySettingsLoadState.Protected;
        SavedConfiguration = IsCompatibilityMode ? AccessoryConfiguration.FromLegacyHat(legacyHatPresetId) : loaded.Configuration;
        DraftConfiguration = SavedConfiguration;
        StatusMessage = loaded.Warning ?? (IsCompatibilityMode
            ? "已读取旧配饰兼容外观；应用后才保存新清单。" : "配饰已从本机读取。");
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
        var usedAssets = configuration.Items.Select(item => item.AssetId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in pendingAssets.Keys.Where(id => !usedAssets.Contains(id)).ToArray()) pendingAssets.Remove(id);
        foreach (var id in imageWarnings.Keys.Where(id => !usedAssets.Contains(id)).ToArray()) imageWarnings.Remove(id);
        StatusMessage = "配饰预览中；应用后才保存到本机。";
    }

    public void CancelPreview()
    {
        VerifyAccess();
        DraftConfiguration = SavedConfiguration;
        pendingAssets.Clear();
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
        if (!HasPendingSave) return true;
        try
        {
            store.Save(settingsRoot, DraftConfiguration, pendingAssets);
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
        finally { pendingAssets.Clear(); }
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
        if (IsCompatibilityMode ? loaded.State != AccessorySettingsLoadState.Missing :
            loaded.State != AccessorySettingsLoadState.Valid || !loaded.Configuration.Equals(SavedConfiguration))
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
                store.Save(settingsRoot, nextSaved);
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
        var next = new AccessoryConfiguration(DraftConfiguration.Items.Append(item));
        pendingAssets.TryAdd(asset.AssetId, asset);
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

    public BitmapSource? TryGetImage(string assetId)
    {
        VerifyAccess();
        if (AccessoryItem.IsBuiltInAsset(assetId)) return null;
        try
        {
            BitmapSource image;
            if (pendingAssets.TryGetValue(assetId, out var pending)) image = pending.Bitmap;
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
