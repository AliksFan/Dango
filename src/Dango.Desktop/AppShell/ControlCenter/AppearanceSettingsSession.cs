using System.IO;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>Application-owned preview state that never piggybacks on main settings persistence.</summary>
public sealed class AppearanceSettingsSession
{
    private readonly string path;
    private readonly IAppearanceSettingsPersistence store;
    private readonly Action<string, string> backup;
    private bool pendingSave;
    private bool bodyPreviewRequired;
    public long BodyConfigurationRevision { get; private set; }
    public long BodyEpoch { get; private set; }
    public string? SourceBodyPhysicsModel { get; }

    public AppearanceSettingsSession(string path, IAppearanceSettingsPersistence? store = null,
        Action<string, string>? backup = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
        this.store = store ?? new AppearanceSettingsStore();
        this.backup = backup ?? ((source, destination) => File.Copy(source, destination, overwrite: false));
        var result = this.store.LoadOrDefault(path);
        SavedAppearance = result.Settings.Configuration;
        SourceBodyPhysicsModel = result.SourceBodyPhysicsModel;
        bodyPreviewRequired = result.SourceBodyPhysicsModel == DangoBodyMaterial.LegacyModelVersion;
        DraftAppearance = SavedAppearance;
        pendingSave = result.RequiresMigration;
        StartupWarning = result.Warning;
        IsWriteProtected = result.PreserveOriginal;
        StatusMessage = result.Warning ?? (result.RequiresMigration ? "外观设置已读取；显式应用后升级保存版本。" : "外观设置已读取。");
    }

    public DangoAppearanceConfiguration SavedAppearance { get; private set; }
    public DangoAppearanceConfiguration DraftAppearance { get; private set; }
    public OpaqueSrgbColor SavedBodyColor => SavedAppearance.BodyColor;
    public OpaqueSrgbColor PreviewBodyColor => DraftAppearance.BodyColor;
    public OpaqueSrgbColor SavedEyeColor => SavedAppearance.EyeColor;
    public OpaqueSrgbColor PreviewEyeColor => DraftAppearance.EyeColor;
    public OpaqueSrgbColor SavedColor => SavedBodyColor;
    public OpaqueSrgbColor PreviewColor => PreviewBodyColor;
    public bool IsDirty => DraftAppearance != SavedAppearance;
    public bool HasPendingSave => IsDirty || pendingSave;
    public bool IsWriteProtected { get; private set; }
    public string? StartupWarning { get; }
    public string StatusMessage { get; private set; }

    public void StageBodyPreview(OpaqueSrgbColor color) =>
        DraftAppearance = DraftAppearance.WithBodyColor(color);

    public void StageEyePreview(OpaqueSrgbColor color) =>
        DraftAppearance = DraftAppearance.WithEyeColor(color);

    public void StagePreview(OpaqueSrgbColor color) => StageBodyPreview(color);

    public void StagePreview(DangoAppearanceConfiguration appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        if (DraftAppearance.Body != appearance.Body)
        {
            RequestBodyApplication();
        }
        DraftAppearance = appearance;
    }

    public void StageEyes(EyeLayoutParameters eyes) =>
        DraftAppearance = DraftAppearance.WithEyeLayout(eyes);

    public void StageBody(DangoBodyControls body)
    {
        ArgumentNullException.ThrowIfNull(body);
        StagePreview(DraftAppearance.WithBody(body));
    }

    /// <summary>Invalidate the display acknowledgement, including when reattaching a runtime.</summary>
    public long RequestBodyApplication()
    {
        BodyConfigurationRevision++;
        bodyPreviewRequired = true;
        PreviewedBody = null;
        return BodyConfigurationRevision;
    }

    public (DangoBodyControls Body, string ModelVersion)? PreviewedBody { get; private set; }

    /// <summary>Only the exact currently displayed target may unlock persistence.</summary>
    public bool MarkBodyApplied(DangoBodyControls body, string modelVersion, long revision, long epoch)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (revision != BodyConfigurationRevision || epoch != BodyEpoch ||
            modelVersion != DraftAppearance.BodyPhysicsModel || body != DraftAppearance.Body)
            return false;
        PreviewedBody = (body, modelVersion);
        bodyPreviewRequired = false;
        return true;
    }

    public bool RequiresBodyPreview => bodyPreviewRequired;

    public void CancelPreview()
    {
        var needsRestore = bodyPreviewRequired || DraftAppearance.Body != SavedAppearance.Body;
        BodyEpoch++;
        DraftAppearance = SavedAppearance;
        PreviewedBody = null;
        if (needsRestore) RequestBodyApplication();
        StatusMessage = needsRestore ? "正在恢复已保存的身体。" : "已取消未保存的外观预览。";
    }

    public void RestoreDefaultBody()
    {
        StageBodyPreview(OpaqueSrgbColor.Default);
        StatusMessage = "已预览默认身体颜色；保存后才会写入本机。";
    }

    public void RestoreDefaultEye()
    {
        StageEyePreview(AppearanceSettings.DefaultEyeColor);
        StatusMessage = "已预览默认眼睛颜色；保存后才会写入本机。";
    }

    public void RestoreDefault()
    {
        StagePreview(DangoAppearanceConfiguration.Default);
        StatusMessage = RequiresBodyPreview ? "默认外观已进入草稿；身体更新完成后可应用。" : "已预览默认外观；应用后才会写入本机。";
    }

    public bool TrySave()
    {
        if (IsWriteProtected)
        {
            StatusMessage = "本次外观未保存：原外观文件已保护。请先备份原文件并恢复保存，或取消预览。";
            return false;
        }
        if (RequiresBodyPreview)
        {
            StatusMessage = "身体更新尚未成功显示，请等待更新完成后再应用。";
            return false;
        }

        if (!HasPendingSave) return true;

        try
        {
            store.Save(path, DraftAppearance);
            SavedAppearance = DraftAppearance;
            pendingSave = false;
            StatusMessage = "外观已保存到本机。";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            StatusMessage = $"外观预览仍保留，但未保存（{exception.GetType().Name}）。可重试或取消预览。";
            return false;
        }
    }

    public bool TryRecoverAndSave()
    {
        if (RequiresBodyPreview)
        {
            StatusMessage = "身体更新尚未成功显示，请等待更新完成后再应用。";
            return false;
        }
        if (!IsWriteProtected) return TrySave();
        try
        {
            BackupOriginal();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"外观备份失败，原文件仍受保护且未保存（{exception.GetType().Name}）。可取消预览。";
            return false;
        }

        // A successfully captured byte-for-byte backup makes the original safe to replace.
        // Keep this unlocked even if the following atomic write fails so retry stays possible.
        IsWriteProtected = false;
        pendingSave = true;
        return TrySave();
    }

    private void BackupOriginal() => backup(path, path + ".backup-" + Guid.NewGuid().ToString("N"));
}
