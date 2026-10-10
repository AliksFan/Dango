using System.IO;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>Application-owned preview state that never piggybacks on main settings persistence.</summary>
public sealed class AppearanceSettingsSession
{
    private readonly string path;
    private readonly AppearanceSettingsStore store;
    private readonly Action<string, string> backup;
    private bool pendingSave;

    public AppearanceSettingsSession(string path, AppearanceSettingsStore? store = null,
        Action<string, string>? backup = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
        this.store = store ?? new AppearanceSettingsStore();
        this.backup = backup ?? ((source, destination) => File.Copy(source, destination, overwrite: false));
        var result = this.store.LoadOrDefault(path);
        SavedAppearance = result.Settings.Configuration;
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
    /// <summary>Only a successful local user save emits this signal; preview and remote application do not.</summary>
    public event Action? SavedToDisk;

    public void StageBodyPreview(OpaqueSrgbColor color) =>
        DraftAppearance = new(color, DraftAppearance.EyeColor, DraftAppearance.EyeLayout);

    public void StageEyePreview(OpaqueSrgbColor color) =>
        DraftAppearance = new(DraftAppearance.BodyColor, color, DraftAppearance.EyeLayout);

    public void StagePreview(OpaqueSrgbColor color) => StageBodyPreview(color);

    public void StagePreview(DangoAppearanceConfiguration appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        DraftAppearance = appearance;
    }

    public void StageEyes(EyeLayoutParameters eyes) =>
        DraftAppearance = new(DraftAppearance.BodyColor, DraftAppearance.EyeColor, eyes);

    public void CancelPreview()
    {
        DraftAppearance = SavedAppearance;
        StatusMessage = "已取消未保存的外观预览。";
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
        DraftAppearance = DangoAppearanceConfiguration.Default;
        StatusMessage = "已预览默认外观；应用后才会写入本机。";
    }

    public bool TrySave()
    {
        if (IsWriteProtected)
        {
            StatusMessage = "本次外观未保存：原外观文件已保护。请先备份原文件并恢复保存，或取消预览。";
            return false;
        }
        if (!HasPendingSave) return true;

        try
        {
            store.Save(path, DraftAppearance);
            SavedAppearance = DraftAppearance;
            pendingSave = false;
            StatusMessage = "外观已保存到本机。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            StatusMessage = $"外观预览仍保留，但未保存（{exception.GetType().Name}）。可重试或取消预览。";
            return false;
        }
        // Observers see committed disk and memory. Their errors must not reclassify a durable save as failed.
        PublishSuccessfulLocalSave();
        return true;
    }

    private void PublishSuccessfulLocalSave() => SavedToDisk?.Invoke();

    internal void AcceptExternalSave(DangoAppearanceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (IsDirty || IsWriteProtected) throw new InvalidOperationException("local_unsaved_changes");
        SavedAppearance = DraftAppearance = configuration;
        pendingSave = false;
        StatusMessage = "已采用所选外观并保存到本机。";
    }

    public bool TryRecoverAndSave()
    {
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
