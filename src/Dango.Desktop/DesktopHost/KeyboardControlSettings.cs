using System.IO;

namespace Dango.Desktop.DesktopHost;

public enum KeyboardControlScheme
{
    // Retained only to deserialize settings written before the single-keyboard contract.
    ArrowKeys,
    Wasd,
}

public sealed record KeyboardControlSettings(bool IsEnabled, KeyboardControlScheme Scheme)
{
    public static KeyboardControlSettings CreateDefault() => new(false, KeyboardControlScheme.Wasd);
}

public static class KeyboardControlSettingsValidator
{
    public static void Validate(KeyboardControlSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Scheme != KeyboardControlScheme.Wasd)
        {
            throw new InvalidDataException("键盘控制方案无效。");
        }
    }
}

public enum DirectionalControlKey
{
    Left,
    Right,
    Jump,
    Squash,
}

public static class KeyboardControlKeyPolicy
{
    public static bool TryMap(int virtualKey, KeyboardControlScheme scheme, out DirectionalControlKey key)
    {
        key = default;
        if (scheme != KeyboardControlScheme.Wasd) return false;
        return virtualKey switch
        {
            0x41 => Set(DirectionalControlKey.Left, out key),
            0x44 => Set(DirectionalControlKey.Right, out key),
            0x20 => Set(DirectionalControlKey.Jump, out key),
            0x53 => Set(DirectionalControlKey.Squash, out key),
            _ => false,
        };
    }

    private static bool Set(DirectionalControlKey value, out DirectionalControlKey key)
    {
        key = value;
        return true;
    }
}

public interface IKeyboardControl
{
    KeyboardControlSettings GetKeyboardControlSettings();

    void ApplyKeyboardControlSettings(KeyboardControlSettings settings);
}
