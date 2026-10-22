using System.Windows.Input;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Belt;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class HomeBodyPreviewSessionTests
{
    private static (HomeBodyPreviewSession Session, RecordingRuntime Runtime) Bound()
    {
        var runtime = new RecordingRuntime();
        var session = new HomeBodyPreviewSession();
        session.Bind(new ProductPhysicsComposition(() => runtime, () => throw new InvalidOperationException("No room owner in an editor session.")));
        return (session, runtime);
    }

    [TestMethod]
    public void StretchSquashAndJumpEdgesRemainIndependent()
    {
        var (session, runtime) = Bound();
        session.SetKey(Key.W, true, false);
        session.SetKey(Key.S, true, false);
        session.SetKey(Key.Space, true, false);
        Assert.AreEqual(1d, runtime.Inputs[^1].Stretch);
        Assert.AreEqual(1d, runtime.Inputs[^1].Downward);
        Assert.IsTrue(runtime.Inputs[^1].JumpPressed);
        session.SetKey(Key.Space, true, true);
        Assert.IsFalse(runtime.Inputs[^1].JumpPressed, "Auto-repeat must not create another jump edge.");
        session.SetKey(Key.Space, false, false);
        session.SetKey(Key.Space, true, false);
        Assert.IsTrue(runtime.Inputs[^1].JumpPressed);
        session.Close();
    }

    [TestMethod]
    public void FocusClearSuppressesHeldKeysUntilTheirPhysicalRelease()
    {
        var (session, runtime) = Bound();
        session.SetKey(Key.D, true, false);
        session.BeginDrag(new Point(4, 5)); session.PublishInput();
        var released = false;
        session.ClearInput(() => { released = true; Assert.IsFalse(session.Dragging); });
        Assert.IsTrue(released);
        Assert.IsTrue(runtime.Inputs[^1].CancelForces);
        Assert.AreEqual(0d, runtime.Inputs[^1].Horizontal);
        Assert.AreEqual(1, runtime.Clears);
        session.ReconcileKeyboard([Key.D]);
        session.SetKey(Key.D, true, true);
        Assert.AreEqual(0d, runtime.Inputs[^1].Horizontal, "Returning focus cannot revive a held movement key.");
        session.SetKey(Key.D, false, false);
        session.SetKey(Key.D, true, false);
        Assert.AreEqual(1d, runtime.Inputs[^1].Horizontal);
        Assert.IsFalse(runtime.Inputs[^1].CancelForces);
        session.Close();
    }

    [TestMethod]
    public void ClearingAnAlreadySleepingBodyOnlyReleasesCapture()
    {
        var (session, runtime) = Bound(); runtime.Sleeping = true;
        var releases = 0;
        session.ClearInput(() => releases++);
        Assert.AreEqual(1, releases);
        Assert.AreEqual(0, runtime.Inputs.Count, "Eye-only or focus changes must not wake a quiet body.");
        Assert.AreEqual(0, runtime.Clears);
        session.Close();
    }

    [TestMethod]
    public void KeyboardReconciliationDropsLostKeysAndDoesNotAdoptNewHeldKeys()
    {
        var (session, runtime) = Bound();
        session.SetKey(Key.D, true, false);
        session.SetKey(Key.Space, true, false);
        session.ReconcileKeyboard([Key.A, Key.Space]);
        Assert.AreEqual(0d, runtime.Inputs[^1].Horizontal);
        Assert.IsFalse(runtime.Inputs[^1].JumpPressed);
        session.SetKey(Key.A, true, false);
        Assert.AreEqual(0d, runtime.Inputs[^1].Horizontal);
        session.SetKey(Key.A, false, false);
        session.SetKey(Key.A, true, false);
        Assert.AreEqual(-1d, runtime.Inputs[^1].Horizontal);
        session.Close();
    }

    [TestMethod]
    public void DragReleaseAndCaptureLossDoNotLeaveAForceOrRepeatCancel()
    {
        var (session, runtime) = Bound();
        session.BeginDrag(new Point(4, 5)); session.PublishInput();
        session.MoveDrag(new Point(7, 8));
        Assert.AreEqual(7d, runtime.Inputs[^1].DragTargetX);
        session.EndDrag(releaseCapture: () =>
        {
            Assert.IsFalse(session.Dragging);
            Assert.IsTrue(runtime.Inputs[^1].DragActive, "Clear capture before publishing the released pose.");
        });
        Assert.IsFalse(runtime.Inputs[^1].DragActive);
        session.BeginDrag(new Point(7, 8)); session.PublishInput();
        session.EndDrag(cancelForces: true);
        Assert.IsTrue(runtime.Inputs[^1].CancelForces);
        session.SetKey(Key.W, true, false);
        Assert.IsFalse(runtime.Inputs[^1].CancelForces);
        session.Close();
    }

    [TestMethod]
    public async Task CloseAcknowledgesTheOwnerBeforeARecoveryCanRebind()
    {
        var (session, runtime) = Bound();
        runtime.CloseBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = Task.Run(session.Close);
        await runtime.Closing.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreSame(runtime, session.Runtime);
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Bind());
        runtime.CloseBarrier.SetResult();
        await closing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsNull(session.Runtime);
        session.Bind();
        Assert.AreSame(runtime, session.Runtime, "Recovery retains the selected composition instead of silently choosing the default DLL.");
        session.Close();
    }

    private sealed class RecordingRuntime : IBodyPreviewRuntime
    {
        internal List<DangoBeltInput> Inputs { get; } = [];
        internal int Clears { get; private set; }
        internal bool Sleeping { get; set; }
        internal TaskCompletionSource? CloseBarrier { get; set; }
        internal TaskCompletionSource Closing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long Epoch => 1;
        public bool SleepEnabled { get; set; }
        public BeltBodyMetrics Metrics => new(1, 0, 0, 0, 0, 0, 0, false, Sleeping, false, 0, 0, 0, 0, 0, 0, null, 0, 0);
        public Task ConfigureAsync(DangoBodyControls body, long revision, long bodyEpoch) => Task.CompletedTask;
        public Task<long> PauseAsync() => Task.FromResult(1L);
        public Task<bool> ResumeAsync() => Task.FromResult(true);
        public Task SetBoundsAsync(double left, double right) => Task.CompletedTask;
        public void PublishInput(DangoBeltInput input) => Inputs.Add(input);
        public void ClearInput() => Clears++;
        public bool TryDequeue(out BeltBodySnapshot? snapshot) { snapshot = null; return false; }
        public bool MarkDisplayed(BeltBodySnapshot snapshot) => false;
        public Task CloseAsync() { Closing.TrySetResult(); return CloseBarrier?.Task ?? Task.CompletedTask; }
        public ValueTask DisposeAsync() => new(CloseAsync());
    }
}
