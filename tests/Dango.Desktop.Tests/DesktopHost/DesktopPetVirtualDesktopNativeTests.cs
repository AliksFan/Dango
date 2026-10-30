using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Forms = System.Windows.Forms;

namespace Dango.Desktop.Tests.DesktopHost;

[TestClass]
[DoNotParallelize]
public sealed class DesktopPetVirtualDesktopNativeTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void SameUnboundRoomHostCanReplaceItsRealNativeDesktopSessionRepeatedlyWithoutPixelsOrFocus()
    {
        using var foreground = new OwnForegroundObservation();
        var screen = Forms.Screen.PrimaryScreen ?? throw new AssertFailedException("A primary desktop is required for the native integration test.");
        var sessions = new List<(DesktopPetDangoWindow Pet, HwndSource Source, Forms.NativeWindow Anchor, Forms.NativeWindow HiddenOwner)>();
        var host = new DesktopPetRoomHost(); // Production default virtual-desktop factory; no driver/frame.
        var lostDesktopCount = 0;
        host.LostDesktop += () => lostDesktopCount++;
        try
        {
            for (var attempt = 1; attempt <= 8; attempt++)
            {
                host.Show(attempt, screen);
                foreach (var previous in sessions)
                {
                    // Native HWND values may be recycled by the next session.
                    // The original managed NativeWindow instances must release
                    // their handles regardless of numeric HWND reuse.
                    Assert.IsTrue(previous.Pet.Handle == 0 && previous.Source.IsDisposed,
                        $"Replacement {attempt} must close the previous real WPF pet.");
                    Assert.IsTrue(previous.Anchor.Handle == 0 && previous.HiddenOwner.Handle == 0,
                        $"Replacement {attempt} must destroy the previous complete owner tree.");
                }

                var pet = host.PetWindow ?? throw new AssertFailedException("The real room host did not create its pet.");
                var source = HwndSource.FromHwnd(pet.Handle) ?? throw new AssertFailedException("The real pet has no HWND source.");
                var anchorHandle = GetWindow(pet.Handle, 4);
                var hiddenOwnerHandle = GetWindow(anchorHandle, 4);
                var anchor = Forms.NativeWindow.FromHandle(anchorHandle) ?? throw new AssertFailedException("The real anchor is not retained by its native owner.");
                var hiddenOwner = Forms.NativeWindow.FromHandle(hiddenOwnerHandle) ?? throw new AssertFailedException("The hidden taskbar owner is not retained.");
                sessions.Add((pet, source, anchor, hiddenOwner));
                Assert.IsTrue(anchorHandle != 0 && hiddenOwnerHandle != 0);
                Assert.IsFalse(IsWindowVisible(hiddenOwnerHandle));
                Assert.IsTrue(GetLayeredWindowAttributes(anchorHandle, out _, out var alpha, out var flags));
                Assert.IsTrue((flags & 2) != 0 && alpha == 0);
                AssertEmptyDisplayRegion(pet.Handle);
                Assert.IsFalse(pet.InteractionEnabled);
                Assert.IsFalse(pet.HasHealthyFrame);
                Assert.IsFalse(pet.IsGestureListening);
                Assert.IsFalse(host.IsReady);
                Assert.IsTrue(host.HasRetainedPresentation && host.IsVisible);
                Assert.IsTrue(host.RequestRestoreOnCurrentDesktop(), "The new real session must retain publicly verifiable current-desktop membership.");

                // Dispatch the real host's pending callbacks and desktop timer,
                // while keeping its native display region strictly empty.
                var settle = Stopwatch.StartNew();
                while (settle.ElapsedMilliseconds < 150)
                {
                    pet.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                    host.CheckSurfaceHealth();
                    AssertEmptyDisplayRegion(pet.Handle);
                    Assert.IsFalse(pet.InteractionEnabled);
                    Assert.AreEqual(string.Empty, host.LastFailureReason, host.LastSurfaceFailure);
                    Assert.AreEqual(0, lostDesktopCount, host.LastSurfaceFailure);
                    foreground.AssertNoActivation();
                    Thread.Sleep(25);
                }
            }
        }
        finally { host.Dispose(); }

        foreach (var session in sessions)
        {
            Assert.IsTrue(session.Pet.Handle == 0 && session.Source.IsDisposed);
            Assert.IsTrue(session.Anchor.Handle == 0 && session.HiddenOwner.Handle == 0);
        }
        Assert.IsFalse(host.HasRetainedPresentation);
        foreground.AssertNoActivation();
    }

    // Background-only integration: the 1-DIP unprepared pet always retains an
    // empty native display region, including real Show/Hide/Show calls. The
    // production 1x1 anchor has global alpha zero. No window can expose pixels
    // or activate. This does not test switching virtual desktops,
    // Task View, taskbar appearance, foreground interaction or visual rendering.
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void UnpreparedRealPetRetainsDesktopOwnershipAcrossShowHideShowWithoutPixelsOrFocus()
    {
        DesktopPetDangoWindow? window = null;
        DesktopPetVirtualDesktop? desktop = null;
        nint petHandle = 0;
        nint ownerHandle = 0;
        nint hiddenOwnerHandle = 0;
        using var foreground = new OwnForegroundObservation();
        try
        {
            var scene = new LocalRoomSceneView();
            scene.EnableDesktopPetPresentation();
            window = new DesktopPetDangoWindow(scene, point => point, point => point);
            petHandle = new WindowInteropHelper(window).EnsureHandle();
            Assert.IsTrue(petHandle != 0 && window.Handle == petHandle);
            Assert.IsFalse(window.IsVisible);
            Assert.IsFalse(IsWindowVisible(petHandle), "EnsureHandle must never show the pet.");
            AssertEmptyDisplayRegion(petHandle);

            desktop = new DesktopPetVirtualDesktop();
            desktop.Attach(petHandle);
            ownerHandle = GetWindow(petHandle, 4); // GW_OWNER; only our own pet.
            Assert.IsTrue(ownerHandle != 0 && ownerHandle != petHandle && IsWindow(ownerHandle),
                "The real pet must be owned by a live native anchor.");
            hiddenOwnerHandle = GetWindow(ownerHandle, 4);
            Assert.IsTrue(hiddenOwnerHandle != 0 && hiddenOwnerHandle != ownerHandle && IsWindow(hiddenOwnerHandle),
                "The anchor must have its own ordinary, hidden taskbar owner.");
            Assert.IsFalse(IsWindowVisible(hiddenOwnerHandle), "The taskbar owner must never be shown.");
            Assert.IsTrue(GetLayeredWindowAttributes(ownerHandle, out _, out var alpha, out var flags));
            Assert.IsTrue((flags & 2) != 0 && alpha == 0, "The anchor must have zero global opacity.");
            Assert.IsTrue(GetWindowRect(ownerHandle, out var bounds));
            Assert.IsTrue(bounds.Right - bounds.Left == 1 && bounds.Bottom - bounds.Top == 1,
                "The background-only anchor must remain one pixel wide and high.");
            Assert.IsTrue(desktop.IsCurrent, "The public COM service must confirm the newly created owner on the current desktop.");
            // Let Shell process the owned-window lifecycle: a registration that
            // disappears asynchronously must not pass as a usable desktop anchor.
            var stability = Stopwatch.StartNew();
            while (stability.ElapsedMilliseconds < 750)
            {
                window.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                Assert.IsTrue(desktop.IsCurrent, "The owned anchor must retain verifiable Shell membership after message dispatch.");
                Thread.Sleep(25);
            }

            window.Show();
            AssertUnpreparedOwnershipAndFocus();
            window.Hide();
            Assert.IsFalse(IsWindowVisible(petHandle));
            AssertUnpreparedOwnershipAndFocus();
            window.Show();
            AssertUnpreparedOwnershipAndFocus();
            window.Hide();
            Assert.IsFalse(IsWindowVisible(petHandle));
            AssertEmptyDisplayRegion(petHandle);
            Assert.IsFalse(window.InteractionEnabled);
            Assert.IsFalse(window.IsGestureListening);
            // Observe only self-owned activation, never external foreground history.
            foreground.AssertNoActivation();

            void AssertUnpreparedOwnershipAndFocus()
            {
                window.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                Assert.IsTrue(window.Handle == petHandle && IsWindow(petHandle), "Show/Hide must retain the same pet HWND.");
                Assert.IsTrue(GetWindow(petHandle, 4) == ownerHandle, "WPF Show/Hide must preserve the virtual-desktop owner.");
                AssertEmptyDisplayRegion(petHandle);
                Assert.IsTrue(desktop.IsCurrent, "Show/Hide must retain publicly verifiable desktop membership.");
                Assert.IsFalse(window.InteractionEnabled);
                Assert.IsFalse(window.HasHealthyFrame);
                Assert.IsFalse(window.IsGestureListening);
                foreground.AssertNoActivation();
            }
        }
        finally
        {
            // Closing the owned pet first also avoids owner destruction implicitly
            // destroying a still-live WPF window. Both paths run after any failure.
            try { window?.Close(); }
            finally { desktop?.Dispose(); }
        }

        Assert.IsFalse(IsWindow(petHandle), "The hidden real pet HWND must be released.");
        Assert.IsFalse(IsWindow(ownerHandle), "The transparent native owner HWND must be released.");
        Assert.IsFalse(IsWindow(hiddenOwnerHandle), "The never-shown taskbar owner HWND must be released.");
        foreground.AssertNoActivation();
    }

    private static void AssertEmptyDisplayRegion(nint window)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        Assert.IsTrue(region != 0);
        try { Assert.AreEqual(1, GetWindowRgn(window, region), "The unprepared pet must retain a NULLREGION and expose no pixels."); }
        finally { _ = DeleteObject(region); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }


    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(nint window, out uint colorKey, out byte alpha, out uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
}
