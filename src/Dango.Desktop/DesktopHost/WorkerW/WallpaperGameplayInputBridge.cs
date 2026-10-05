using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using Dango.Desktop.DesktopHost.Win32;

namespace Dango.Desktop.DesktopHost.WorkerW;

internal readonly record struct WallpaperGameplayKeyData(int VirtualKey, uint Flags, bool HasModifier);
internal readonly record struct WallpaperGameplayMouseData(WallpaperWheelPoint Point, uint Flags, bool Shift, bool HasForbiddenModifier);
internal sealed record WallpaperGameplayInputActions(Func<Key, bool, bool, bool> Key,
    Func<WallpaperWheelPoint, bool, bool> PointerDown, Func<WallpaperWheelPoint, bool, bool> PointerMove,
    Func<WallpaperWheelPoint, bool, bool> PointerUp, Func<WallpaperWheelPoint, bool> IsPetHit,
    Action MenuRequested, Action ClearInput);

internal interface IWallpaperGameplayInputPlatform
{
    nint InstallKeyboard(NativeMethods.LowLevelKeyboardProcedure callback);
    nint InstallMouse(NativeMethods.LowLevelMouseProcedure callback);
    bool UninstallKeyboard(nint hook);
    bool UninstallMouse(nint hook);
    nint NextKeyboard(nint hook, int code, nint message, nint data);
    nint NextMouse(nint hook, int code, nint message, nint data);
    WallpaperGameplayKeyData ReadKeyboard(nint data);
    WallpaperGameplayMouseData ReadMouse(nint data);
    bool IsKeyPressed(int virtualKey);
    WallpaperWheelInputEnvironment Inspect(WallpaperWheelInputContext context, WallpaperWheelPoint? point, bool atEvent, bool allowShift);
}

/// <summary>Observes Shell input without consuming it; commands are requalified on the Dispatcher.</summary>
internal sealed class WallpaperGameplayInputBridge : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Func<WallpaperWheelInputContext?> context;
    private readonly WallpaperGameplayInputActions actions;
    private readonly IWallpaperGameplayInputPlatform platform;
    private readonly NativeMethods.LowLevelKeyboardProcedure keyboardCallback;
    private readonly NativeMethods.LowLevelMouseProcedure mouseCallback;
    private readonly DispatcherTimer timer;
    private readonly List<Command> pending = [];
    private readonly HashSet<Key> physicalKeys = [];
    private readonly HashSet<Key> suppressedKeys = [];
    private readonly HashSet<Key> activeKeys = [];
    private nint keyboardHook, mouseHook;
    private long revision, pointerRevision;
    private bool active, disposed, stopping, inspecting, clearRequested, pointerArmed, pointerActive;
    private WallpaperWheelPoint lastPointer;
    internal bool IsListening => active && keyboardHook != 0 && mouseHook != 0 && !disposed;
    internal bool IsHookReleasePending => !active && (keyboardHook != 0 || mouseHook != 0);

    internal WallpaperGameplayInputBridge(Dispatcher dispatcher, Func<WallpaperWheelInputContext?> context,
        WallpaperGameplayInputActions actions, IWallpaperGameplayInputPlatform? platform = null)
    {
        this.dispatcher = dispatcher;
        this.context = context;
        this.actions = actions;
        this.platform = platform ?? new NativeWallpaperGameplayInputPlatform();
        dispatcher.VerifyAccess();
        keyboardCallback = OnKeyboard;
        mouseCallback = OnMouse;
        timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += OnTick;
        dispatcher.ShutdownStarted += OnShutdown;
    }

    internal void Start()
    {
        dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (active || stopping || dispatcher.HasShutdownStarted) return;
        revision++;
        try
        {
            if (!ReleaseHooks()) { actions.ClearInput(); return; }
            physicalKeys.Clear(); suppressedKeys.Clear();
            foreach (var virtualKey in new[] { 0x41, 0x44, 0x53, 0x20, 0x1B })
                if (platform.IsKeyPressed(virtualKey) && TryMapKey(virtualKey, out var key))
                { physicalKeys.Add(key); suppressedKeys.Add(key); }
            keyboardHook = platform.InstallKeyboard(keyboardCallback);
            mouseHook = platform.InstallMouse(mouseCallback);
            active = keyboardHook != 0 && mouseHook != 0;
            if (!active) { ReleaseHooks(); actions.ClearInput(); }
        }
        catch { active = false; ReleaseHooks(); actions.ClearInput(); }
    }

    internal void Stop()
    {
        dispatcher.VerifyAccess();
        if (stopping) return;
        stopping = true;
        active = false; revision++; pointerRevision++;
        timer.Stop(); pending.Clear(); activeKeys.Clear();
        pointerArmed = pointerActive = clearRequested = false;
        suppressedKeys.UnionWith(physicalKeys);
        try { ReleaseHooks(); }
        finally { try { actions.ClearInput(); } finally { stopping = false; } }
    }

    private bool ReleaseHooks()
    {
        if (keyboardHook != 0 && platform.UninstallKeyboard(keyboardHook)) keyboardHook = 0;
        if (mouseHook != 0 && platform.UninstallMouse(mouseHook)) mouseHook = 0;
        return keyboardHook == 0 && mouseHook == 0;
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true;
        try { Stop(); }
        finally { timer.Tick -= OnTick; dispatcher.ShutdownStarted -= OnShutdown; }
    }

    internal void CancelPointer()
    {
        pointerRevision++;
        pointerArmed = pointerActive = false;
        pending.RemoveAll(command => command.Kind != CommandKind.Key);
    }

    internal void ResetInput()
    {
        dispatcher.VerifyAccess();
        Invalidate(revision);
        if (clearRequested) { clearRequested = false; actions.ClearInput(); }
    }

    private bool IsCurrent(long version) => active && !disposed && revision == version && !dispatcher.HasShutdownStarted;

    private nint OnKeyboard(int code, nint message, nint data)
    {
        var version = revision;
        var hook = keyboardHook;
        try
        {
            if (code >= 0 && IsCurrent(version) && message is 0x0100 or 0x0101 or 0x0104 or 0x0105)
            {
                var input = platform.ReadKeyboard(data);
                if ((input.Flags & 0x10) == 0 && IsCurrent(version))
                {
                    if (!TryMapKey(input.VirtualKey, out var key))
                    {
                        if (input.VirtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C && activeKeys.Count != 0) Invalidate(version);
                    }
                    else
                    {
                        var down = message is 0x0100 or 0x0104;
                        var repeat = down && !physicalKeys.Add(key);
                        if (!down) physicalKeys.Remove(key);
                        var suppressed = suppressedKeys.Contains(key);
                        if (!down) suppressedKeys.Remove(key);
                        if (!suppressed)
                        {
                            var captured = context();
                            if (input.HasModifier || !Qualify(captured, null, version, true)) Invalidate(version);
                            else if (key != Key.Escape || (down && !repeat))
                                Queue(new(CommandKind.Key, captured!, version, pointerRevision, key, down, repeat, default, false));
                        }
                    }
                }
            }
        }
        catch { Invalidate(version); }
        return platform.NextKeyboard(hook, code, message, data);
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        var version = revision;
        var hook = mouseHook;
        try
        {
            if (code >= 0 && IsCurrent(version) && message is 0x0200 or 0x0201 or 0x0202)
            {
                var input = platform.ReadMouse(data);
                if ((input.Flags & 0x01) == 0 && IsCurrent(version) && (message == 0x0201 || pointerArmed))
                {
                    var captured = context();
                    if (input.HasForbiddenModifier || !Qualify(captured, input.Point, version, true)) Invalidate(version);
                    else if (message == 0x0201)
                    {
                        if (!pointerArmed && actions.IsPetHit(input.Point))
                        {
                            pointerArmed = true; pointerRevision++;
                            lastPointer = input.Point;
                            Queue(new(CommandKind.Down, captured!, version, pointerRevision, Key.None, false, false, input.Point, input.Shift));
                        }
                    }
                    else
                    {
                        lastPointer = input.Point;
                        Queue(new(message == 0x0200 ? CommandKind.Move : CommandKind.Up, captured!, version,
                            pointerRevision, Key.None, false, false, input.Point, input.Shift));
                    }
                }
            }
        }
        catch { Invalidate(version); }
        return platform.NextMouse(hook, code, message, data);
    }

    private bool Qualify(WallpaperWheelInputContext? captured, WallpaperWheelPoint? point, long version, bool atEvent)
    {
        if (inspecting || !IsCurrent(version) || !WallpaperWheelInputPolicy.IsContextEligible(captured)) return false;
        if (point is { } p && !WallpaperWheelInputPolicy.ContainsDevicePoint(captured!.ScreenBounds, p)) return false;
        inspecting = true;
        try
        {
            if (!WallpaperWheelInputPolicy.IsSamePresentation(captured!, context())) return false;
            var environment = platform.Inspect(captured!, point, atEvent, allowShift: point is not null);
            return IsCurrent(version) && WallpaperWheelInputPolicy.IsSamePresentation(captured!, context()) &&
                environment.IsKnown && environment.IsDesktopForeground && !environment.HasFullscreenApplication &&
                !environment.HasModifier && (point is null || (environment.IsPointerOnDesktop && !environment.IsDesktopIcon));
        }
        finally { inspecting = false; }
    }

    private void Queue(Command command)
    {
        if (pending.Count >= 32) { Invalidate(command.Revision); return; }
        if (command.Kind == CommandKind.Move && pending.Count != 0 && pending[^1].Kind == CommandKind.Move)
            pending[^1] = command;
        else pending.Add(command);
        timer.Start();
    }

    private void Invalidate(long version)
    {
        if (!IsCurrent(version)) return;
        revision++; pointerRevision++; pending.Clear(); activeKeys.Clear();
        pointerArmed = pointerActive = false;
        suppressedKeys.UnionWith(physicalKeys);
        clearRequested = true;
        timer.Start();
    }

    internal void FlushPendingInput()
    {
        dispatcher.VerifyAccess();
        if (inspecting) return;
        timer.Stop();
        if (!active || disposed || dispatcher.HasShutdownStarted) return;
        if (clearRequested) { clearRequested = false; actions.ClearInput(); }
        var batch = pending.ToArray(); pending.Clear();
        foreach (var command in batch)
        {
            if (!IsCurrent(command.Revision)) continue;
            if (command.Kind != CommandKind.Key && command.PointerRevision != pointerRevision) continue;
            if (!Qualify(command.Context, command.Kind == CommandKind.Key ? null : command.Point, command.Revision, false))
            {
                Invalidate(command.Revision);
                if (clearRequested) { clearRequested = false; actions.ClearInput(); }
                break;
            }
            // Native inspection may reenter a scene/capture cancellation without changing the host generation.
            if (!IsCurrent(command.Revision) || (command.Kind != CommandKind.Key && command.PointerRevision != pointerRevision)) continue;
            try
            {
                if (command.Kind == CommandKind.Key)
                {
                    if (command.Key == Key.Escape) actions.MenuRequested();
                    else
                    {
                        var accepted = actions.Key(command.Key, command.IsDown, command.Repeat);
                        if (accepted && IsCurrent(command.Revision))
                        { if (command.IsDown) activeKeys.Add(command.Key); else activeKeys.Remove(command.Key); }
                    }
                }
                else if (command.Kind == CommandKind.Down)
                {
                    var accepted = actions.PointerDown(command.Point, command.Shift);
                    if (IsCurrent(command.Revision) && command.PointerRevision == pointerRevision)
                    { pointerActive = accepted; if (!accepted) CancelPointer(); }
                }
                else if (pointerActive)
                {
                    var accepted = command.Kind == CommandKind.Move
                        ? actions.PointerMove(command.Point, command.Shift) : actions.PointerUp(command.Point, command.Shift);
                    if (IsCurrent(command.Revision) && command.PointerRevision == pointerRevision &&
                        (!accepted || command.Kind == CommandKind.Up))
                    { CancelPointer(); if (!accepted) actions.ClearInput(); }
                }
            }
            catch
            {
                Invalidate(command.Revision);
                if (clearRequested) { clearRequested = false; actions.ClearInput(); }
                break;
            }
        }
        if (active && !disposed && (pending.Count != 0 || clearRequested || activeKeys.Count != 0 || pointerArmed)) timer.Start();
    }

    internal void RevalidateActiveInput()
    {
        dispatcher.VerifyAccess();
        var captured = context();
        var version = revision;
        if ((activeKeys.Count != 0 && !Qualify(captured, null, version, false)) ||
            (pointerArmed && !Qualify(captured, lastPointer, version, false))) Invalidate(version);
    }

    private void OnTick(object? sender, EventArgs args) { RevalidateActiveInput(); FlushPendingInput(); }
    private void OnShutdown(object? sender, EventArgs args) => Stop();
    private static bool TryMapKey(int virtualKey, out Key key)
    {
        key = virtualKey switch { 0x41 => Key.A, 0x44 => Key.D, 0x53 => Key.S, 0x20 => Key.Space, 0x1B => Key.Escape, _ => Key.None };
        return key != Key.None;
    }
    private enum CommandKind { Key, Down, Move, Up }
    private readonly record struct Command(CommandKind Kind, WallpaperWheelInputContext Context, long Revision,
        long PointerRevision, Key Key, bool IsDown, bool Repeat, WallpaperWheelPoint Point, bool Shift);
}

internal sealed class NativeWallpaperGameplayInputPlatform : IWallpaperGameplayInputPlatform
{
    public nint InstallKeyboard(NativeMethods.LowLevelKeyboardProcedure callback) => NativeMethods.InstallRoomKeyboardHook(callback);
    public nint InstallMouse(NativeMethods.LowLevelMouseProcedure callback) => NativeMethods.InstallRoomMouseHook(callback);
    public bool UninstallKeyboard(nint hook) => NativeMethods.UninstallRoomKeyboardHook(hook);
    public bool UninstallMouse(nint hook) => NativeMethods.UninstallRoomMouseHook(hook);
    public nint NextKeyboard(nint hook, int code, nint message, nint data) => NativeMethods.CallNextKeyboardHook(hook, code, message, data);
    public nint NextMouse(nint hook, int code, nint message, nint data) => NativeMethods.CallNextKeyboardHook(hook, code, message, data);
    public WallpaperGameplayKeyData ReadKeyboard(nint data)
    {
        var value = Marshal.PtrToStructure<NativeMethods.LowLevelKeyboardInput>(data);
        return new(unchecked((int)value.VirtualKey), value.Flags, NativeMethods.IsInputKeyPressed(0x10) || NativeMethods.HasSystemKeyboardModifier());
    }
    public WallpaperGameplayMouseData ReadMouse(nint data) => NativeMethods.ReadRoomGameplayMouseInput(data);
    public bool IsKeyPressed(int virtualKey) => NativeMethods.IsInputKeyPressed(virtualKey);
    public WallpaperWheelInputEnvironment Inspect(WallpaperWheelInputContext context, WallpaperWheelPoint? point, bool atEvent, bool allowShift) =>
        NativeMethods.InspectRoomGameplayInput(context, point, atEvent, allowShift);
}
