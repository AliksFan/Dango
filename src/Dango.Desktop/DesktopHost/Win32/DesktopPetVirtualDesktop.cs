namespace Dango.Desktop.DesktopHost.Win32;

internal interface IDesktopPetVirtualDesktop : IDisposable
{
    // Call once, on the owning UI thread, before enabling the pet's display/input.
    void Attach(nint petWindow);
    bool IsCurrent { get; }
}

// This session owns only a Dango anchor, never another application's window.
// Retain it across Hide/Show. Recreating an anchor would select a new desktop.
internal sealed class DesktopPetVirtualDesktop : IDesktopPetVirtualDesktop
{
    private readonly IDesktopPetVirtualDesktopPlatform platform;
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private nint anchor;
    private nint pet;
    private bool disposed;

    internal DesktopPetVirtualDesktop() : this(new DesktopPetVirtualDesktopNative()) { }
    internal DesktopPetVirtualDesktop(IDesktopPetVirtualDesktopPlatform platform) =>
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));

    public void Attach(nint petWindow)
    {
        VerifyAccess();
        if (pet != 0 || anchor != 0) throw new InvalidOperationException("桌宠已关联虚拟桌面。");
        if (petWindow == 0) throw new ArgumentException("桌宠窗口尚未建立。", nameof(petWindow));
        platform.ValidateWindow(petWindow);
        try
        {
            anchor = platform.CreateAnchor();
            _ = ReadDesktop(anchor);
            if (!platform.IsCurrent(anchor)) throw new InvalidOperationException("无法确认进入时的虚拟桌面。");
            platform.SetOwner(petWindow, anchor);
            pet = petWindow;
            if (!IsCurrent) throw new InvalidOperationException("无法确认桌宠的初始虚拟桌面归属。");
        }
        catch
        {
            // A failed session cannot later become a partially attached success.
            Dispose();
            throw;
        }
    }

    public bool IsCurrent
    {
        get
        {
            VerifyAccess();
            if (anchor == 0 || pet == 0) throw new InvalidOperationException("桌宠尚未关联虚拟桌面。");
            platform.ValidateWindow(anchor);
            platform.ValidateWindow(pet);
            platform.ValidateOwner(pet, anchor);
            // The ordinary owner is the sole desktop authority. A tool window
            // may have no independent application view (empty GUID) or report
            // current on every desktop, even with a valid native owner link.
            // Do not query it or try to move such an unregistered tool view.
            var ownerDesktop = ReadDesktop(anchor);
            var ownerCurrent = platform.IsCurrent(anchor);
            // Observe Windows' migration after desktop deletion, without ever
            // moving back to a remembered, now deleted desktop identifier.
            if (ReadDesktop(anchor) != ownerDesktop) return false;
            // A switch can occur between COM calls. A mixed sample must not open
            // the display/input gate. No COM failure is converted into success.
            return ownerCurrent && platform.IsCurrent(anchor);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        VerifyAccess();
        disposed = true;
        try { if (pet != 0) platform.RestoreOwner(pet, anchor); }
        finally
        {
            pet = anchor = 0;
            platform.Dispose();
        }
    }

    private Guid ReadDesktop(nint window)
    {
        var desktop = platform.GetDesktop(window);
        if (desktop == Guid.Empty) throw new InvalidOperationException("Windows 未返回可确认的虚拟桌面归属。");
        return desktop;
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != threadId)
            throw new InvalidOperationException("虚拟桌面关联只能在所属界面线程使用。");
    }
}

internal interface IDesktopPetVirtualDesktopPlatform : IDisposable
{
    void ValidateWindow(nint window);
    nint CreateAnchor();
    void SetOwner(nint pet, nint anchor);
    void ValidateOwner(nint pet, nint anchor);
    void RestoreOwner(nint pet, nint anchor);
    Guid GetDesktop(nint window);
    bool IsCurrent(nint window);
}
