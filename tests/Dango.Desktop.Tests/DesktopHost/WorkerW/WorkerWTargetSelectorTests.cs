using Dango.Desktop.DesktopHost.WorkerW;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class WorkerWTargetSelectorTests
{
    [TestMethod]
    public void RaisedDesktopUsesProgmanParentBetweenShellViewAndWallpaperWorker()
    {
        var candidates = new[]
        {
            Window(1, 0, 12292, 73, "WorkerW", false, 136, 38),
            Window(
                10,
                0,
                12292,
                186,
                "Progman",
                true,
                1536,
                960,
                hasShellView: true,
                shellViewHandle: 12,
                hasNoRedirectionBitmap: true),
            Window(11, 10, 12292, 0, "WorkerW", true, 1536, 960),
        };

        var selected = WorkerWTargetSelector.Select(candidates);

        Assert.IsNotNull(selected);
        Assert.AreEqual((nint)10, selected.ParentHandle);
        Assert.AreEqual((nint)11, selected.WallpaperLayerHandle);
        Assert.AreEqual((nint)12, selected.InsertAfterHandle);
        Assert.AreEqual((nint)12, selected.ShellViewHandle);
        Assert.IsTrue(selected.IsRaisedDesktop);
    }

    [TestMethod]
    public void LegacyTopLevelWorkerWAfterShellViewHostRemainsSupported()
    {
        var candidates = new[]
        {
            Window(10, 0, 100, 20, "Progman", true, 1920, 1080, hasShellView: true),
            Window(20, 0, 100, 21, "WorkerW", true, 1920, 1080),
        };

        var selected = WorkerWTargetSelector.Select(candidates);

        Assert.IsNotNull(selected);
        Assert.AreEqual((nint)20, selected.ParentHandle);
        Assert.AreEqual((nint)20, selected.WallpaperLayerHandle);
        Assert.AreEqual(nint.Zero, selected.InsertAfterHandle);
        Assert.IsFalse(selected.IsRaisedDesktop);
    }

    [TestMethod]
    public void UnrelatedInvisibleOrTinyWorkerWsAreRejected()
    {
        var candidates = new[]
        {
            Window(10, 0, 100, 20, "Progman", true, 1536, 960, hasShellView: true),
            Window(20, 10, 999, 0, "WorkerW", true, 1536, 960),
            Window(21, 10, 100, 0, "WorkerW", false, 1536, 960),
            Window(22, 10, 100, 0, "WorkerW", true, 136, 38),
        };

        var selected = WorkerWTargetSelector.Select(candidates);

        Assert.IsNull(selected);
    }

    [TestMethod]
    public void ProgmanChildWorkerWithoutRaisedDesktopMarkerIsRejected()
    {
        var candidates = new[]
        {
            Window(10, 0, 100, 20, "Progman", true, 1536, 960, hasShellView: true, shellViewHandle: 12),
            Window(11, 10, 100, 0, "WorkerW", true, 1536, 960),
        };

        var selected = WorkerWTargetSelector.Select(candidates);

        Assert.IsNull(selected);
    }

    private static ShellWindowCandidate Window(
        long handle,
        long parent,
        int processId,
        int zOrder,
        string className,
        bool isVisible,
        int width,
        int height,
        bool hasShellView = false,
        long shellViewHandle = 0,
        bool hasNoRedirectionBitmap = false) =>
        new(
            (nint)handle,
            (nint)parent,
            processId,
            zOrder,
            className,
            isVisible,
            width,
            height,
            hasShellView,
            (nint)shellViewHandle,
            hasNoRedirectionBitmap);
}
