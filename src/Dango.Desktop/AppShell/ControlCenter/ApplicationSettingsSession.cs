using System.IO;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>One application-owned preference snapshot and protected save path.</summary>
public sealed class ApplicationSettingsSession
{
    private readonly string path;
    private readonly ControlCenterWindowSettingsStore store;
    private readonly Action<string, string> backup;
    private bool migrationBackupRequired;
    private bool pending;

    public ApplicationSettingsSession(string path, ControlCenterWindowSettingsStore? store = null,
        Action<string, string>? backup = null)
    {
        this.path = path;
        this.store = store ?? new();
        this.backup = backup ?? ((source, destination) => File.Copy(source, destination, overwrite: false));
        var result = this.store.LoadOrDefault(path);
        Current = result.Settings;
        StartupWarning = result.Warning;
        HasWarning = result.Warning is not null;
        IsWriteProtected = result.PreserveOriginal;
        IsPresetWriteProtected = result.PreservePresetOriginal;
        migrationBackupRequired = result.WasMigrated;
        pending = result.WasMigrated || IsPresetWriteProtected || !File.Exists(path) ||
            !File.Exists(ControlCenterWindowSettingsStore.GetPresetPath(path));
        StatusMessage = result.Warning ?? "设置已读取。";
    }

    public ControlCenterWindowSettings Current { get; private set; }
    public string? StartupWarning { get; }
    public bool HasWarning { get; private set; }
    public bool IsWriteProtected { get; private set; }
    public bool IsPresetWriteProtected { get; private set; }
    public string StatusMessage { get; private set; }

    public void Stage(ControlCenterWindowSettings settings)
    {
        ControlCenterWindowSettingsValidator.Validate(settings);
        pending |= Current != settings;
        Current = settings;
    }

    public bool TrySave(ControlCenterWindowSettings settings)
    {
        try
        {
            Stage(settings);
        }
        catch (InvalidDataException)
        {
            HasWarning = true;
            StatusMessage = "本次设置未保存：窗口尺寸或偏好超出安全范围。原设置保持完整；可以不保存退出。";
            return false;
        }
        return TrySave();
    }

    public bool TrySave()
    {
        if (IsWriteProtected)
        {
            HasWarning = true;
            StatusMessage = "本次设置未保存：原文件已保护。请先备份原文件并恢复保存，或不保存退出。";
            return false;
        }
        if (!pending) return true;
        try
        {
            if (migrationBackupRequired)
            {
                BackupOriginal();
                migrationBackupRequired = false;
            }
            store.Save(path, Current, preservePresetOriginal: IsPresetWriteProtected);
            if (IsPresetWriteProtected)
            {
                HasWarning = true;
                StatusMessage = "其他偏好已保存；窗口预设未保存，原预设文件已保护。可备份后恢复保存，或不保存退出。";
                return false;
            }
            pending = false;
            HasWarning = false;
            StatusMessage = "已保存到本机。";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            HasWarning = true;
            StatusMessage = $"本次已应用，但未保存，下次启动可能恢复旧设置（{exception.GetType().Name}）。可重试或不保存退出。";
            return false;
        }
    }

    public bool TryRecoverAndSave()
    {
        if (!IsWriteProtected && !IsPresetWriteProtected) return TrySave();
        try
        {
            if (IsWriteProtected) BackupOriginal();
            if (IsPresetWriteProtected)
            {
                var presetPath = ControlCenterWindowSettingsStore.GetPresetPath(path);
                backup(presetPath, presetPath + ".backup-" + Guid.NewGuid().ToString("N"));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            HasWarning = true;
            StatusMessage = $"备份失败，原文件仍受保护且未保存（{exception.GetType().Name}）；可以不保存退出。";
            return false;
        }
        IsWriteProtected = false;
        IsPresetWriteProtected = false;
        pending = true;
        return TrySave();
    }

    private void BackupOriginal() => backup(path, path + ".backup-" + Guid.NewGuid().ToString("N"));
}
