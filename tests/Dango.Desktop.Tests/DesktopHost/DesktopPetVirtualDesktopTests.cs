using Dango.Desktop.DesktopHost.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost;

[TestClass]
public sealed class DesktopPetVirtualDesktopTests
{
    [TestMethod]
    public void AttachmentUsesOnlyTheNewOwnerDesktopWithoutQueryingOrMovingTheToolView()
    {
        var platform = new FakePlatform();
        using var desktop = new DesktopPetVirtualDesktop(platform);
        desktop.Attach(FakePlatform.Pet);
        Assert.IsTrue(desktop.IsCurrent);
        Assert.AreEqual(1, platform.AnchorCount);
        Assert.AreEqual(0, platform.ToolQueryCount);
        CollectionAssert.AreEqual(new[] { "create", "owner" }, platform.Mutations.ToArray());
    }

    [TestMethod]
    public void LeavingAndReturningKeepsTheSameOwnerAndNeverMovesToTheOtherDesktop()
    {
        var platform = new FakePlatform();
        using var desktop = new DesktopPetVirtualDesktop(platform);
        desktop.Attach(FakePlatform.Pet);
        platform.OwnerCurrent = false;
        // Tool windows may claim current independently. The ordinary owner is
        // necessary: testing only the pet would incorrectly keep displaying it.
        Assert.IsFalse(desktop.IsCurrent);
        platform.OwnerCurrent = true;
        Assert.IsTrue(desktop.IsCurrent);
        Assert.AreEqual(1, platform.AnchorCount);
        Assert.AreEqual(0, platform.ToolQueryCount);
    }

    [TestMethod]
    public void WindowsMigrationIsObservedWithoutMovingBackToADeletedDesktop()
    {
        var platform = new FakePlatform();
        using var desktop = new DesktopPetVirtualDesktop(platform);
        desktop.Attach(FakePlatform.Pet);
        var original = platform.Desktop;
        platform.Desktop = Guid.NewGuid();
        Assert.IsTrue(desktop.IsCurrent);
        Assert.AreNotEqual(original, platform.Desktop);
        CollectionAssert.AreEqual(new[] { "create", "owner" }, platform.Mutations.ToArray());
    }

    [TestMethod]
    public void UnverifiableMigrationFailsInsteadOfShowingOnEveryDesktop()
    {
        var platform = new FakePlatform();
        using var desktop = new DesktopPetVirtualDesktop(platform);
        desktop.Attach(FakePlatform.Pet);
        platform.Desktop = Guid.Empty;
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = desktop.IsCurrent);
        Assert.AreEqual(0, platform.ToolQueryCount);
    }

    [TestMethod]
    public void EmptyDesktopIdNeverQualifiesAsAnAssignedDesktop()
    {
        var platform = new FakePlatform { Desktop = Guid.Empty };
        var desktop = new DesktopPetVirtualDesktop(platform);
        Assert.ThrowsExactly<InvalidOperationException>(() => desktop.Attach(FakePlatform.Pet));
        Assert.IsTrue(platform.Disposed);
        Assert.AreEqual(0, platform.ToolQueryCount);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = desktop.IsCurrent);
    }

    [TestMethod]
    public void FailedInitialOwnerVerificationRestoresTheOriginalOwnerAndReleasesTheAnchor()
    {
        var platform = new FakePlatform { OwnerValid = false };
        var desktop = new DesktopPetVirtualDesktop(platform);
        Assert.ThrowsExactly<InvalidOperationException>(() => desktop.Attach(FakePlatform.Pet));
        Assert.IsTrue(platform.OwnerRestored);
        Assert.IsTrue(platform.Disposed);
        desktop.Dispose();
        Assert.AreEqual(1, platform.DisposeCount);
    }

    [TestMethod]
    public void FailedQueriesAndLostOwnershipDoNotReuseTheLastSuccessfulResult()
    {
        var platform = new FakePlatform();
        using var desktop = new DesktopPetVirtualDesktop(platform);
        desktop.Attach(FakePlatform.Pet);
        platform.FailQuery = true;
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = desktop.IsCurrent);
        platform.FailQuery = false;
        platform.OwnerValid = false;
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = desktop.IsCurrent);
    }

    [TestMethod]
    public void MixedDesktopSwitchSampleClosesTheDisplayGate()
    {
        var platform = new FakePlatform();
        using var desktop = new DesktopPetVirtualDesktop(platform);
        desktop.Attach(FakePlatform.Pet);
        platform.OwnerCurrentSamples.Enqueue(true);
        platform.OwnerCurrentSamples.Enqueue(false);
        Assert.IsFalse(desktop.IsCurrent);
    }

    [TestMethod]
    public void MigrationDuringASampleClosesTheGateUntilTheNextStableSample()
    {
        var platform = new FakePlatform();
        using var desktop = new DesktopPetVirtualDesktop(platform);
        desktop.Attach(FakePlatform.Pet);
        platform.OwnerDesktopSamples.Enqueue(platform.Desktop);
        platform.OwnerDesktopSamples.Enqueue(Guid.NewGuid());
        Assert.IsFalse(desktop.IsCurrent);
        Assert.IsTrue(desktop.IsCurrent);
    }

    [TestMethod]
    public void ASessionCannotReattachWhenRestoringAHiddenPet()
    {
        var platform = new FakePlatform();
        using var desktop = new DesktopPetVirtualDesktop(platform);
        desktop.Attach(FakePlatform.Pet);
        Assert.ThrowsExactly<InvalidOperationException>(() => desktop.Attach(FakePlatform.Pet));
        Assert.AreEqual(1, platform.AnchorCount);
    }

    [TestMethod]
    public void DisposeReleasesOwnerBeforeDestroyingItsPlatformAndRejectsLaterQueries()
    {
        var platform = new FakePlatform();
        var desktop = new DesktopPetVirtualDesktop(platform);
        desktop.Attach(FakePlatform.Pet);
        desktop.Dispose();
        desktop.Dispose();
        CollectionAssert.AreEqual(new[] { "create", "owner", "restore", "dispose" }, platform.Mutations.ToArray());
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = desktop.IsCurrent);
    }

    private sealed class FakePlatform : IDesktopPetVirtualDesktopPlatform
    {
        internal static readonly nint Pet = new(10);
        private static readonly nint Anchor = new(20);
        internal Guid Desktop = Guid.NewGuid();
        internal bool OwnerCurrent = true;
        internal bool OwnerValid = true;
        internal bool FailQuery;
        internal bool OwnerRestored;
        internal bool Disposed;
        internal int AnchorCount;
        internal int DisposeCount;
        internal int ToolQueryCount;
        internal readonly List<string> Mutations = [];
        internal readonly Queue<bool> OwnerCurrentSamples = [];
        internal readonly Queue<Guid> OwnerDesktopSamples = [];

        public void ValidateWindow(nint window)
        {
            Assert.IsTrue(window == Pet || window == Anchor);
            Assert.IsFalse(Disposed);
        }
        public nint CreateAnchor() { AnchorCount++; Mutations.Add("create"); return Anchor; }
        public void SetOwner(nint pet, nint anchor) { Assert.AreEqual(Pet, pet); Assert.AreEqual(Anchor, anchor); Mutations.Add("owner"); }
        public void ValidateOwner(nint pet, nint anchor)
        {
            if (!OwnerValid) throw new InvalidOperationException("owner lost");
        }
        public void RestoreOwner(nint pet, nint anchor) { OwnerRestored = true; Mutations.Add("restore"); }
        public Guid GetDesktop(nint window)
        {
            if (FailQuery) throw new InvalidOperationException("query failed");
            if (window == Pet) { ToolQueryCount++; return Guid.Empty; }
            return OwnerDesktopSamples.Count > 0 ? OwnerDesktopSamples.Dequeue() : Desktop;
        }
        public bool IsCurrent(nint window)
        {
            if (window == Pet) { ToolQueryCount++; return true; }
            return OwnerCurrentSamples.Count > 0 ? OwnerCurrentSamples.Dequeue() : OwnerCurrent;
        }
        public void Dispose() { DisposeCount++; Disposed = true; Mutations.Add("dispose"); }
    }
}
