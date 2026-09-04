using System.Runtime.InteropServices;
using Dango.Core.Animation;
using Dango.Desktop.DesktopHost.Win32;

namespace Dango.Desktop.DesktopHost.WorkerW;

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
        return scheme switch
        {
            KeyboardControlScheme.ArrowKeys => virtualKey switch
            {
                0x25 => Set(DirectionalControlKey.Left, out key),
                0x27 => Set(DirectionalControlKey.Right, out key),
                0x26 => Set(DirectionalControlKey.Jump, out key),
                0x28 => Set(DirectionalControlKey.Squash, out key),
                _ => false,
            },
            KeyboardControlScheme.Wasd => virtualKey switch
            {
                0x41 => Set(DirectionalControlKey.Left, out key),
                0x44 => Set(DirectionalControlKey.Right, out key),
                0x57 => Set(DirectionalControlKey.Jump, out key),
                0x53 => Set(DirectionalControlKey.Squash, out key),
                _ => false,
            },
            _ => false,
        };
    }

    private static bool Set(DirectionalControlKey value, out DirectionalControlKey key)
    {
        key = value;
        return true;
    }
}

public sealed class DirectionalControlHeldState
{
    private readonly bool[] held = new bool[4];

    public DirectionalControlIntent Snapshot => new(
        held[(int)DirectionalControlKey.Left],
        held[(int)DirectionalControlKey.Right],
        held[(int)DirectionalControlKey.Jump],
        held[(int)DirectionalControlKey.Squash]);

    public void Set(DirectionalControlKey key, bool isDown) => held[(int)key] = isDown;

    public void Clear() => Array.Clear(held);
}

internal sealed class WorkerWKeyboardInputBridge : IDisposable
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;
    private readonly WorkerWDesktopTarget target;
    private readonly DirectionalControlHeldState heldState = new();
    private readonly NativeMethods.LowLevelKeyboardProcedure procedure;
    private KeyboardControlSettings settings = KeyboardControlSettings.CreateDefault();
    private nint hook;
    private bool disposed;
    private bool installationFailed;

    public WorkerWKeyboardInputBridge(WorkerWDesktopTarget target)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        procedure = OnKeyboard;
    }

    public DirectionalControlIntent CurrentIntent => heldState.Snapshot;

    public string State => hook != nint.Zero
        ? "Active"
        : installationFailed ? "Unavailable" : settings.IsEnabled ? "Inactive" : "Disabled";

    public void ApplySettings(KeyboardControlSettings value)
    {
        KeyboardControlSettingsValidator.Validate(value);
        settings = value;
        Deactivate();
        installationFailed = false;
    }

    public void Refresh(bool hostReady, bool interactionEnabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var shouldActivate = settings.IsEnabled && hostReady && interactionEnabled &&
            NativeMethods.IsExplorerDesktopForeground(target);
        if (!shouldActivate)
        {
            Deactivate();
            return;
        }

        if (hook == nint.Zero && !installationFailed)
        {
            hook = NativeMethods.InstallLowLevelKeyboardHook(procedure);
            installationFailed = hook == nint.Zero;
        }
    }

    public void Clear() => heldState.Clear();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Deactivate();
    }

    private nint OnKeyboard(int code, nint wordParameter, nint longParameter)
    {
        if (code < 0 || hook == nint.Zero || !NativeMethods.IsExplorerDesktopForeground(target))
        {
            return NativeMethods.CallNextKeyboardHook(hook, code, wordParameter, longParameter);
        }

        var data = Marshal.PtrToStructure<NativeMethods.LowLevelKeyboardInput>(longParameter);
        if ((data.Flags & LlkhfInjected) != 0 || NativeMethods.HasSystemKeyboardModifier() ||
            !KeyboardControlKeyPolicy.TryMap(unchecked((int)data.VirtualKey), settings.Scheme, out var key))
        {
            return NativeMethods.CallNextKeyboardHook(hook, code, wordParameter, longParameter);
        }

        var message = unchecked((int)wordParameter.ToInt64());
        if (message is WmKeyDown or WmSysKeyDown)
        {
            heldState.Set(key, true);
            return 1;
        }

        if (message is WmKeyUp or WmSysKeyUp)
        {
            heldState.Set(key, false);
            return 1;
        }

        return NativeMethods.CallNextKeyboardHook(hook, code, wordParameter, longParameter);
    }

    private void Deactivate()
    {
        heldState.Clear();
        if (hook == nint.Zero)
        {
            return;
        }

        _ = NativeMethods.UninstallKeyboardHook(hook);
        hook = nint.Zero;
    }
}
