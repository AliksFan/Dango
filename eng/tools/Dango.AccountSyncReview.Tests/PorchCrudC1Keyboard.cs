using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Dango.AccountSyncReview.Tests;

/// <summary>Injects only owned keys while the isolated scene window owns foreground.</summary>
internal sealed class PorchCrudC1Keyboard : IDisposable
{
    private readonly nint previous = GetForegroundWindow();
    private readonly nint handle;
    private readonly HashSet<ushort> owned = [];
    internal PorchCrudC1Keyboard(Window window)
    {
        foreach (var key in new[] { 0x01, 0x02, 0x10, 0x11, 0x12, 0x20, 0x41, 0x44, 0x57, 0x5B, 0x5C })
            Assert.AreEqual(0, GetAsyncKeyState(key) & 0x8000, "A user-held input must not be reused by the scene test.");
        var area = SystemParameters.WorkArea;
        window.Left = area.Left + 20; window.Top = area.Top + 20;
        window.Width = Math.Min(960, area.Width - 40); window.Height = Math.Min(540, area.Height - 40);
        window.UpdateLayout(); window.Activate();
        handle = new WindowInteropHelper(window).Handle;
        _ = SetForegroundWindow(handle); Pump();
        if (handle != GetForegroundWindow())
            Assert.Inconclusive("Windows refused foreground activation of this exact isolated scene; no native input was injected.");
    }
    internal void Press(ushort key) { Down(key); Up(key); }
    internal void Down(ushort key)
    {
        Assert.AreEqual(handle, GetForegroundWindow(), "Foreground changed; input must stay inside the owned scene.");
        if (!owned.Add(key)) return;
        Send(key, false); Pump();
    }
    internal void Up(ushort key)
    {
        if (!owned.Remove(key)) return;
        Send(key, true); Pump();
    }
    public void Dispose()
    {
        foreach (var key in owned.ToArray()) { Send(key, true); owned.Remove(key); }
        if (GetForegroundWindow() == handle && previous != 0 && IsWindow(previous)) _ = SetForegroundWindow(previous);
    }
    private static void Send(ushort key, bool up)
    {
        var input = new NativeInput { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Flags = up ? 2u : 0u } } };
        Assert.AreEqual(1u, SendInput(1, [input], Marshal.SizeOf<NativeInput>()));
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public Union Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Union
    { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public nuint ExtraInfo; }
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool IsWindow(nint window);
}
