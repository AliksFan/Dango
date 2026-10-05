using System.IO;
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
        SavedBodyColor = result.Settings.BodyColor;
        PreviewBodyColor = SavedBodyColor;
        SavedEyeColor = result.Settings.EyeColor;
        PreviewEyeColor = SavedEyeColor;
        pendingSave = result.RequiresMigration;
        StartupWarning = result.Warning;
        IsWriteProtected = result.PreserveOriginal;
        StatusMessage = result.Warning ?? "外观设置已读取。";
    }

    public OpaqueSrgbColor SavedBodyColor { get; private set; }
    public OpaqueSrgbColor PreviewBodyColor { get; private set; }
    public OpaqueSrgbColor SavedEyeColor { get; private set; }
    public OpaqueSrgbColor PreviewEyeColor { get; private set; }
    public OpaqueSrgbColor SavedColor => SavedBodyColor;
    public OpaqueSrgbColor PreviewColor => PreviewBodyColor;
    public bool IsDirty => PreviewBodyColor != SavedBodyColor || PreviewEyeColor != SavedEyeColor;
    public bool HasPendingSave => IsDirty || pendingSave;
    public bool IsWriteProtected { get; private set; }
    public string? StartupWarning { get; }
    public string StatusMessage { get; private set; }

    public void StageBodyPreview(OpaqueSrgbColor color) => PreviewBodyColor = color;

    public void StageEyePreview(OpaqueSrgbColor color) => PreviewEyeColor = color;

    public void StagePreview(OpaqueSrgbColor color) => StageBodyPreview(color);

    public void CancelPreview()
    {
        PreviewBodyColor = SavedBodyColor;
        PreviewEyeColor = SavedEyeColor;
        StatusMessage = "已取消未保存的颜色预览。";
    }

    public void RestoreDefaultBody()
    {
        PreviewBodyColor = OpaqueSrgbColor.Default;
        StatusMessage = "已预览默认身体颜色；保存后才会写入本机。";
    }

    public void RestoreDefaultEye()
    {
        PreviewEyeColor = AppearanceSettings.DefaultEyeColor;
        StatusMessage = "已预览默认眼睛颜色；保存后才会写入本机。";
    }

    public void RestoreDefault() => RestoreDefaultBody();

    public bool TrySave()
    {
        if (IsWriteProtected)
        {
            StatusMessage = "本次颜色未保存：原外观文件已保护。请先备份原文件并恢复保存，或取消预览。";
            return false;
        }
        if (!HasPendingSave) return true;

        try
        {
            store.Save(path, new AppearanceSettings(PreviewBodyColor, PreviewEyeColor));
            SavedBodyColor = PreviewBodyColor;
            SavedEyeColor = PreviewEyeColor;
            pendingSave = false;
            StatusMessage = "颜色已保存到本机。";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            StatusMessage = $"颜色预览已应用，但未保存（{exception.GetType().Name}）。可重试或取消预览。";
            return false;
        }
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
