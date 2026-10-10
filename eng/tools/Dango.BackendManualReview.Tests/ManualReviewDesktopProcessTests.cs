using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.BackendManualReview.Tests;

[TestClass]
public sealed class ManualReviewDesktopProcessTests
{
    private static readonly Uri Origin = new("https://127.0.0.1:54321");
    private static readonly VerifiedManualReviewCandidate Candidate = new(@"C:\source", @"C:\source\artifacts\local-room\candidate", @"C:\source\artifacts\local-room\candidate\Dango.Desktop.exe", new('A',64), @"C:\source\artifacts\local-room\candidate\dango_physics_module.dll",new('B',64));
    [TestMethod] public void UnauthorizedLaunchAndWaitDoNotTouchResources()
    {
        var fake = new Resources(); var service = new ManualReviewDesktopProcess(fake);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Launch(false, Candidate, Origin));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.WaitForOwnedExitAsync(false, new(new Process(), new(1, DateTimeOffset.UtcNow, "a", Origin, 1)),TimeSpan.FromSeconds(1)));
        Assert.AreEqual(0, fake.VerifyCount + fake.MutexReads + fake.Starts.Count);
    }
    [TestMethod] public void ExistingIsolatedMutexCannotActivateOrLaunchOldCandidate()
    {
        var fake = new Resources { Existing = true }; var service = new ManualReviewDesktopProcess(fake);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Launch(true, Candidate, Origin));
        Assert.AreEqual(0, fake.Starts.Count);
    }
    [TestMethod] public void ReviewModePreflightRejectsUnauthorizedBeforeMutexRead()
    {
        var fake=new Resources{Existing=true};var service=new ManualReviewDesktopProcess(fake);
        Assert.ThrowsExactly<InvalidOperationException>(()=>service.AssertNoReviewModeProcess(false));
        Assert.AreEqual(0,fake.MutexReads+fake.VerifyCount+fake.Starts.Count);
        Assert.ThrowsExactly<InvalidOperationException>(()=>service.AssertNoReviewModeProcess(true));
        Assert.AreEqual(1,fake.MutexReads);Assert.AreEqual(0,fake.VerifyCount+fake.Starts.Count);
    }
    [TestMethod] public void ReviewModePreflightAbsentInstanceOnlyReadsItsMutex()
    {
        var fake=new Resources();var service=new ManualReviewDesktopProcess(fake);
        service.AssertNoReviewModeProcess(true);
        Assert.AreEqual(1,fake.MutexReads);Assert.AreEqual(0,fake.VerifyCount+fake.Starts.Count);
    }
    [TestMethod] public void LaunchPinsOnlyFixedOriginAndReviewArgument()
    {
        var fake = new Resources(); var service = new ManualReviewDesktopProcess(fake);
        var owned = service.Launch(true, Candidate, Origin);
        Assert.AreEqual("--local-room-review", fake.Starts.Single().Argument);
        Assert.AreEqual(Origin, fake.Starts.Single().Origin);
        Assert.AreEqual(Candidate.ExecutablePath, owned.Receipt.ExecutablePath);
        Assert.AreEqual(1, owned.Receipt.LaunchNumber);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Launch(true, Candidate, new("https://127.0.0.1:54322")));
        Assert.AreEqual(1, fake.Starts.Count);
    }
    [TestMethod] public async Task FourthLaunchAllowedFifthRefusedAndOwnedExitRequiredBeforeRestart()
    {
        var fake = new Resources(); var service = new ManualReviewDesktopProcess(fake);
        for (var i = 1; i <= 4; i++)
        {
            var owned = service.Launch(true, Candidate, Origin);
            Assert.AreEqual(i, owned.Receipt.LaunchNumber);
            Assert.ThrowsExactly<InvalidOperationException>(() => service.Launch(true, Candidate, Origin));
            ((Process)owned.Process).Exited = true;
            Assert.IsTrue(await service.WaitForOwnedExitAsync(true, owned,TimeSpan.FromSeconds(1)));
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Launch(true, Candidate, Origin));
        Assert.AreEqual(4, fake.Starts.Count);
    }
    [TestMethod] public async Task HiddenWindowDoesNotProveProcessExitAndTimeoutDoesNotKill()
    {
        var fake = new Resources(); var service = new ManualReviewDesktopProcess(fake);
        var owned = service.Launch(true, Candidate, Origin);
        Assert.IsFalse(await service.WaitForOwnedExitAsync(true, owned, TimeSpan.FromMilliseconds(20)));
        Assert.IsFalse(((Process)owned.Process).Disposed);
    }
    [TestMethod] public void ForgedProcessReceiptDoesNotAuthorizeWaiting()
    {
        var fake = new Resources(); var service = new ManualReviewDesktopProcess(fake);
        var owned = service.Launch(true, Candidate, Origin);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.WaitForOwnedExitAsync(true, new(owned.Process, owned.Receipt),TimeSpan.FromSeconds(1)));
    }
    [TestMethod] public void ReusedPidWrongCreateTimeFailsIdentity()
    {
        var fake = new Resources(); var service = new ManualReviewDesktopProcess(fake);
        var owned = service.Launch(true, Candidate, Origin);
        ((Process)owned.Process).Created = DateTimeOffset.UtcNow.AddDays(1);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.WaitForOwnedExitAsync(true, owned,TimeSpan.FromSeconds(1)));
    }
    [TestMethod] public void CandidatePathHashAndNativeEscapeFailBeforeResources()
    {
        var fake=new Resources();var service=new ManualReviewDesktopProcess(fake);
        foreach(var candidate in new[]{Candidate with {ExecutableSha256="invalid"},Candidate with{NativeModulePath=@"C:\daily\dango_box2d.dll"},Candidate with{CandidateRoot=@"C:\daily"},Candidate with{SourceRoot=@"C:\source\..\other"}})
            Assert.ThrowsExactly<InvalidDataException>(()=>service.Launch(true,candidate,Origin));
        Assert.AreEqual(0,fake.VerifyCount+fake.MutexReads+fake.Starts.Count);
    }
    [TestMethod] public async Task ExitReceiptCanBeCheckedAgainWithoutReadingDisposedHandle()
    {
        var fake=new Resources();var service=new ManualReviewDesktopProcess(fake);var owned=service.Launch(true,Candidate,Origin);
        ((Process)owned.Process).Exited=true;
        Assert.IsTrue(await service.WaitForOwnedExitAsync(true,owned,TimeSpan.FromSeconds(1)));
        Assert.IsTrue(await service.WaitForOwnedExitAsync(true,owned,TimeSpan.FromSeconds(1)));
        Assert.AreEqual(1,service.SnapshotOwnedProcesses().Count);
    }
    [TestMethod] public async Task ConcurrentOwnedExitChecksShareHandleWithoutDoubleDisposal()
    {
        var fake=new Resources();var service=new ManualReviewDesktopProcess(fake);var owned=service.Launch(true,Candidate,Origin);
        var first=service.WaitForOwnedExitAsync(true,owned,TimeSpan.FromSeconds(1));
        var second=service.WaitForOwnedExitAsync(true,owned,TimeSpan.FromSeconds(1));
        ((Process)owned.Process).CompleteExit();
        CollectionAssert.AreEqual(new[]{true,true},await Task.WhenAll(first,second));
        Assert.AreEqual(1,((Process)owned.Process).DisposeCount);
    }
    [TestMethod] public void StartIdentityFailureStillRetainsStartedCapabilityAndRejectsCleanup()
    {
        foreach(var field in new[]{"pid","created","path"})
        {
            var fake=new Resources{FailureField=field};var service=new ManualReviewDesktopProcess(fake);
            Assert.ThrowsExactly<IOException>(()=>service.Launch(true,Candidate,Origin));
            Assert.AreEqual(1,fake.Starts.Count);
            var owned=service.SnapshotOwnedProcesses().Single();
            Assert.IsFalse(owned.Receipt.IdentityVerified);
            Assert.IsNotNull(owned.Receipt.CapabilityId);
            Assert.AreEqual("manual_desktop_identity_observation_failed",owned.Receipt.IdentityObservationFailure);
            Assert.ThrowsExactly<InvalidOperationException>(()=>service.WaitForOwnedExitAsync(true,owned,TimeSpan.FromSeconds(1)));
            Assert.IsFalse(((Process)owned.Process).Disposed);
            Assert.ThrowsExactly<InvalidOperationException>(()=>service.Launch(true,Candidate,Origin));
            Assert.AreEqual(1,fake.Starts.Count);
        }
    }
    private sealed class Resources : IManualReviewDesktopResources
    {
        public int VerifyCount { get; private set; } public int MutexReads { get; private set; } public bool Existing { get; init; } public string? FailureField {get;init;}
        public List<ManualReviewDesktopLaunch> Starts { get; } = [];
        public void VerifyCandidate(VerifiedManualReviewCandidate candidate) => VerifyCount++;
        public bool LocalRoomReviewExists() { MutexReads++; return Existing; }
        public IManualReviewProcess Start(ManualReviewDesktopLaunch launch) { Starts.Add(launch); return new Process { Path = launch.ExecutablePath,FailureField=FailureField }; }
    }
    private sealed class Process : IManualReviewProcess
    {
        public string? FailureField {get;init;}
        public int ProcessId => FailureField!="pid"?4242:throw new IOException(); public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset CreationTimeUtc => FailureField!="created"?Created:throw new IOException(); public string Path { get; set; } = Candidate.ExecutablePath;
        private readonly TaskCompletionSource exitCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ExecutablePath => FailureField!="path"?Path:throw new IOException(); public bool Exited { get; set; } public bool HasExited => !Disposed?Exited:throw new ObjectDisposedException(nameof(Process));
        public bool Disposed { get; private set; } public int DisposeCount {get;private set;}
        public async Task WaitForExitAsync(CancellationToken ct) { if (!Exited) await exitCompletion.Task.WaitAsync(ct); }
        public void CompleteExit(){Exited=true;exitCompletion.TrySetResult();}
        public void Dispose() {Disposed=true;DisposeCount++;}
    }
}
