using System.IO;
using System.Text;
using System.Text.Json;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Verification;

namespace Dango.Desktop.AppShell.ControlCenter;

// Independent local display preference; never changes body configuration or main settings.
internal sealed class DesktopPetDisplaySettingsSession
{
    internal const int DefaultPercent = 100;
    internal const string FileName = "desktop-pet-display.json";
    private readonly string path;
    private readonly IAtomicTextWriter writer;
    internal int Percent { get; private set; } = DefaultPercent;
    internal int SavedPercent { get; private set; } = DefaultPercent;
    internal bool IsWriteProtected { get; private set; }
    internal bool HasPendingSave => Percent != SavedPercent;
    internal string? StatusMessage { get; private set; }

    internal DesktopPetDisplaySettingsSession(string path, IAtomicTextWriter? writer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
        this.writer = writer ?? new AtomicTextFileWriter();
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 4096) throw new InvalidDataException();
            var document = RoomJson.Read<DisplaySettings>(bytes);
            if (document.SchemaVersion != 1) throw new InvalidDataException();
            Validate(document.Percent);
            Percent = SavedPercent = document.Percent;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
        catch (Exception error) when (IsStorageError(error))
        {
            IsWriteProtected = true;
            StatusMessage = "桌宠大小未能读取，当前使用 100%；原文件已保护。请核对原文件后重新打开应用。";
        }
    }

    internal void SetPercent(int percent)
    {
        Validate(percent);
        Percent = percent;
    }

    internal bool TrySave()
    {
        if (IsWriteProtected)
        {
            StatusMessage = $"当前桌宠大小为 {Percent}%，尚未保存；原文件已保护。请核对原文件后重新打开应用。";
            return false;
        }
        try
        {
            writer.Write(path, Encoding.UTF8.GetString(RoomJson.Write(new DisplaySettings(1, Percent))));
            SavedPercent = Percent;
            StatusMessage = null;
            return true;
        }
        catch (Exception error) when (IsStorageError(error))
        {
            StatusMessage = $"当前桌宠大小为 {Percent}%，尚未保存（{error.GetType().Name}）；可重试保存。";
            return false;
        }
    }

    private static void Validate(int percent)
    {
        if (percent is < 50 or > 200) throw new ArgumentOutOfRangeException(nameof(percent));
    }
    private static bool IsStorageError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException
        or JsonException or ArgumentException or System.Security.SecurityException;
    private sealed record DisplaySettings(int SchemaVersion, int Percent);
}
