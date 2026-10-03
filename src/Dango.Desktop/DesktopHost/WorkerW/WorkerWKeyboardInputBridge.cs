using System.Runtime.InteropServices;
using Dango.Core.Animation;
using Dango.Desktop.DesktopHost.Win32;

namespace Dango.Desktop.DesktopHost.WorkerW;

public static class WorkerWKeyboardCapturePolicy
{
    public static bool ShouldActivate(bool settingsEnabled, bool hostReady, bool interactionEnabled,
        bool roomGameplayInputActive, bool explorerDesktopForeground) =>
        settingsEnabled && hostReady && interactionEnabled && roomGameplayInputActive && explorerDesktopForeground;
}

public sealed class DirectionalControlHeldState
{
    private readonly bool[] held = new bool[4];
    private readonly bool[] suppressed = new bool[4];

    public DirectionalControlIntent Snapshot => new(
        held[(int)DirectionalControlKey.Left],
        held[(int)DirectionalControlKey.Right],
        held[(int)DirectionalControlKey.Jump],
        held[(int)DirectionalControlKey.Squash]);

    public void Set(DirectionalControlKey key, bool isDown)
    {
        var index = (int)key;
        if (!isDown) suppressed[index] = false;
        held[index] = isDown && !suppressed[index];
    }

    public void Clear()
    {
        Array.Clear(held);
        Array.Clear(suppressed);
    }

    public void ClearUntilRelease()
    {
        for (var i = 0; i < held.Length; i++) suppressed[i] |= held[i];
        Array.Clear(held);
    }

    public void SuppressUntilRelease(DirectionalControlKey key)
    {
        held[(int)key] = false;
        suppressed[(int)key] = true;
    }
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
    private bool roomGameplayInputActive;

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

    public void Refresh(bool hostReady, bool interactionEnabled, bool roomGameplayInputActive)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        this.roomGameplayInputActive = roomGameplayInputActive;
        var shouldActivate = WorkerWKeyboardCapturePolicy.ShouldActivate(
            settings.IsEnabled, hostReady, interactionEnabled, roomGameplayInputActive,
            roomGameplayInputActive && NativeMethods.IsExplorerDesktopForeground(target));
        if (!shouldActivate)
        {
            Deactivate();
            return;
        }

        if (hook == nint.Zero && !installationFailed)
        {
            // Do not import an already-held key from a different focused app.
            var keys = new[] { 0x41, 0x44, 0x53, 0x20 };
            foreach (var virtualKey in keys)
                if (NativeMethods.IsInputKeyPressed(virtualKey) && KeyboardControlKeyPolicy.TryMap(virtualKey, settings.Scheme, out var key))
                    heldState.SuppressUntilRelease(key);
            hook = NativeMethods.InstallLowLevelKeyboardHook(procedure);
            installationFailed = hook == nint.Zero;
        }
    }

    public void Clear() => heldState.ClearUntilRelease();

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
        if (code < 0 || hook == nint.Zero || !roomGameplayInputActive ||
            !NativeMethods.IsExplorerDesktopForeground(target))
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
