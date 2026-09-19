using System.IO;

namespace Dango.Desktop.DesktopHost;

public enum KeyboardControlScheme
{
    ArrowKeys,
    Wasd,
}

public sealed record KeyboardControlSettings(bool IsEnabled, KeyboardControlScheme Scheme)
{
    public static KeyboardControlSettings CreateDefault() => new(false, KeyboardControlScheme.ArrowKeys);
}

public static class KeyboardControlSettingsValidator
{
    public static void Validate(KeyboardControlSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Scheme))
        {
            throw new InvalidDataException("键盘控制方案无效。");
        }
    }
}

public interface IKeyboardControl
{
    KeyboardControlSettings GetKeyboardControlSettings();

    void ApplyKeyboardControlSettings(KeyboardControlSettings settings);
}
