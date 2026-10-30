using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost.Win32;
using Forms = System.Windows.Forms;

namespace Dango.Desktop.Tests.AppShell;

// Background lifecycle checks only. No driver, physical frame, real desktop
// association, visible pixels, activation, capture or global input is involved.
[TestClass]
[DoNotParallelize]
public sealed class DesktopPetRoomDesktopLifecycleTests
{
    [STATestMethod]
    [TestCategory("BackgroundNativeLifecycle")]
    public void LeavingAndReturningSuspendsWithoutFaultOrRecreatingTheEmptyPetWindow()
    {
        WithEmptyHost((host, desktop, foreground) =>
        {
            var window = host.PetWindow!;
            var handle = window.Handle;
            var availability = new List<bool>();
            var losses = 0;
            host.DesktopAvailabilityChanged += availability.Add;
            host.LostDesktop += () => losses++;
            desktop.Current = false;
            host.CheckSurfaceHealth();
            host.CheckSurfaceHealth();
            Assert.IsTrue(host.IsDesktopSuspended);
            Assert.IsFalse(host.IsVisible);
            Assert.IsFalse(host.IsReady);
            Assert.IsFalse(window.IsVisible);
            AssertEmptyAndInactive(window, foreground);
            CollectionAssert.AreEqual(new[] { false }, availability);
            desktop.Current = true;
            host.CheckSurfaceHealth();
            host.CheckSurfaceHealth();
            DrainDispatcher();
            Assert.IsFalse(host.IsDesktopSuspended);
            Assert.IsTrue(host.IsVisible);
            Assert.IsFalse(host.IsReady, "Returning cannot fabricate a healthy physical frame.");
            Assert.AreSame(window, host.PetWindow);
            Assert.AreEqual(handle, window.Handle);
            Assert.AreEqual(1, desktop.AttachCount);
            CollectionAssert.AreEqual(new[] { false, true }, availability);
            Assert.AreEqual(0, losses);
            Assert.AreEqual(string.Empty, host.LastFailureReason);
            AssertEmptyAndInactive(window, foreground);
        });
    }

    [STATestMethod]
    [TestCategory("BackgroundNativeLifecycle")]
    public void ManualHideAndRestoreRetainTheSameEmptyWindowAndDesktopSession()
    {
        WithEmptyHost((host, desktop, foreground) =>
        {
            var window = host.PetWindow!;
            var handle = window.Handle;
            host.Hide(preserveDesktop: true);
            Assert.IsTrue(host.HasRetainedPresentation);
            Assert.AreSame(window, host.PetWindow);
            Assert.IsFalse(window.IsVisible);
            Assert.AreEqual(0, desktop.DisposeCount);
            AssertEmptyAndInactive(window, foreground);
            Assert.IsTrue(host.RequestRestoreOnCurrentDesktop());
            host.Show(20, PrimaryScreen());
            Assert.AreSame(window, host.PetWindow);
            Assert.AreEqual(handle, window.Handle);
            Assert.AreEqual(1, desktop.AttachCount);
            Assert.AreEqual(0, desktop.DisposeCount);
            Assert.IsFalse(host.IsReady);
            AssertEmptyAndInactive(window, foreground);
        });
    }

    [STATestMethod]
    [TestCategory("BackgroundNativeLifecycle")]
    public void RestoreRequestedElsewhereWaitsForReturnAndRejectsAnOldQueuedGeneration()
    {
        WithEmptyHost((host, desktop, foreground) =>
        {
            var window = host.PetWindow!;
            var requests = 0;
            host.RestoreOnCurrentDesktopRequested += () => requests++;
            host.Hide(preserveDesktop: true);
            desktop.Current = false;
            Assert.IsFalse(host.RequestRestoreOnCurrentDesktop());
            host.CheckSurfaceHealth();
            DrainDispatcher();
            Assert.AreEqual(0, requests);
            desktop.Current = true;
            host.CheckSurfaceHealth(); // Queue this generation's restore callback.
            var queuedGeneration = host.Generation;
            host.Hide(preserveDesktop: true); // Invalidate it before dispatch.
            Assert.AreNotEqual(queuedGeneration, host.Generation);
            DrainDispatcher();
            Assert.AreEqual(0, requests, "A cancelled generation cannot replay its restoration.");
            desktop.Current = false;
            Assert.IsFalse(host.RequestRestoreOnCurrentDesktop());
            desktop.Current = true;
            host.CheckSurfaceHealth();
            DrainDispatcher();
            Assert.AreEqual(1, requests);
            host.CheckSurfaceHealth();
            DrainDispatcher();
            Assert.AreEqual(1, requests, "One request must produce only one return notification.");
            Assert.AreSame(window, host.PetWindow);
            Assert.IsFalse(window.IsVisible, "The notification itself must not enable display or interaction.");
            Assert.AreEqual(1, desktop.AttachCount);
            AssertEmptyAndInactive(window, foreground);
        });
    }

    [STATestMethod]
    [TestCategory("BackgroundNativeLifecycle")]
    public void FullHideReleasesTheRetainedWindowAndDesktopSession()
    {
        WithEmptyHost((host, desktop, foreground) =>
        {
            var window = host.PetWindow!;
            var handle = window.Handle;
            host.Hide(preserveDesktop: true);
            host.Hide();
            Assert.IsNull(host.PetWindow);
            Assert.IsFalse(host.HasRetainedPresentation);
            Assert.IsFalse(host.IsVisible);
            Assert.IsFalse(host.IsDesktopSuspended);
            Assert.IsFalse(IsWindow(handle));
            Assert.IsNull(PresentationSource.FromVisual(host.Scene));
            Assert.AreEqual(1, desktop.DisposeCount);
            foreground.AssertNoActivation();
        });
    }

    private static void WithEmptyHost(Action<DesktopPetRoomHost, FakeDesktop, OwnForegroundObservation> assertion)
    {
        using var foreground = new OwnForegroundObservation();
        var desktop = new FakeDesktop();
        var created = 0;
        var host = new DesktopPetRoomHost(createVirtualDesktop: () => { created++; return desktop; });
        try
        {
            host.Show(10, PrimaryScreen());
            Assert.AreEqual(1, created);
            AssertEmptyAndInactive(host.PetWindow!, foreground);
            assertion(host, desktop, foreground);
            Assert.AreEqual(1, created, "Lifecycle transitions cannot replace the desktop session.");
        }
        finally
        {
            host.Dispose();
            foreground.AssertNoActivation();
        }
    }

    private static Forms.Screen PrimaryScreen() => Forms.Screen.PrimaryScreen
        ?? throw new AssertFailedException("The lifecycle check requires primary-display geometry.");

    private static void AssertEmptyAndInactive(DesktopPetDangoWindow window, OwnForegroundObservation foreground)
    {
        Assert.IsFalse(window.HasHealthyFrame);
        Assert.IsNull(window.LastAppliedFrame);
        Assert.IsFalse(window.InteractionEnabled);
        Assert.IsFalse(window.IsPointerCaptured);
        Assert.IsFalse(window.IsGestureListening);
        var region = CreateRectRgn(0, 0, 0, 0);
        Assert.AreNotEqual(nint.Zero, region);
        try
        {
            Assert.AreEqual(1, GetWindowRgn(window.Handle, region),
                "The HWND must retain an empty native region; no pixels may reach the desktop.");
        }
        finally { _ = DeleteObject(region); }
        foreground.AssertNoActivation();
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.Background);
        Dispatcher.PushFrame(frame);
    }

    private sealed class FakeDesktop : IDesktopPetVirtualDesktop
    {
        internal bool Current = true;
        internal int AttachCount, DisposeCount;
        public bool IsCurrent => Current;
        public void Attach(nint petWindow)
        {
            Assert.AreNotEqual(nint.Zero, petWindow);
            AttachCount++;
        }
        public void Dispose() => DisposeCount++;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);
}
