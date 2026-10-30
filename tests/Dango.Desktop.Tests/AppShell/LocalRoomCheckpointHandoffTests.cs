using System.Reflection;
using System.Threading;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Dangos;
using Dango.Desktop.Furniture;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

/// <summary>Unshown Host callback ordering; no rendered-frame or input acceptance claims.</summary>
[TestClass]
[DoNotParallelize]
public sealed class LocalRoomCheckpointHandoffTests
{
    [STATestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void PendingCheckpointMustFinishBeforeAdmissionAndCancelledGenerationNeverRehosts(int outcome)
    {
        using var fixture = new Fixture();
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.SetCheckpoint(save.Task);
        var context = fixture.BeginCompletion();
        Assert.IsTrue(fixture.Gate.Pending, "A rendered target must not attempt admission while its own checkpoint write is pending.");
        Assert.AreEqual(LocalRoomPresentation.Porch, fixture.Gate.Current);
        fixture.Gate.Hide();
        if (outcome == 0) save.SetResult();
        else if (outcome == 1) save.SetCanceled();
        else save.SetException(new IOException("fictional checkpoint failure"));
        ProductHostTestSupport.WaitFor(() => context.Completed, "The stale completion must leave its await without reopening a window.");
        Assert.AreEqual(LocalRoomPresentation.Hidden, fixture.Gate.Current);
        Assert.IsFalse(fixture.Gate.Pending);
        Assert.IsFalse(fixture.Host.LocalWindow!.IsVisible);
        Assert.IsFalse(fixture.Window.IsVisible);
    }

    [STATestMethod]
    public void AdmissionAlsoWaitsForACheckpointAppendedDuringTheFirstAwait()
    {
        using var fixture = new Fixture();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.SetCheckpoint(first.Task);
        var context = fixture.BeginCompletion();
        Assert.IsTrue(fixture.Gate.Pending);
        fixture.SetCheckpoint(second.Task);
        first.SetResult();
        ProductHostTestSupport.WaitFor(() => context.Posts > 0, "The checkpoint continuation must be posted to its owning Dispatcher.");
        var barrier = fixture.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        ProductHostTestSupport.WaitFor(() => barrier.Task.IsCompleted, "Observe the continuation after the first save.");
        Assert.IsTrue(fixture.Gate.Pending, "Completing an older save cannot bypass a newer queued checkpoint.");
        Assert.IsFalse(context.Completed);
        fixture.Gate.Hide(); second.SetResult();
        ProductHostTestSupport.WaitFor(() => context.Completed, "The cancelled completion must finish after the latest checkpoint.");
        Assert.AreEqual(LocalRoomPresentation.Hidden, fixture.Gate.Current);
        Assert.IsFalse(fixture.Host.LocalWindow!.IsVisible);
    }

    private sealed class CompletionContext(Dispatcher dispatcher) : SynchronizationContext
    {
        public bool Completed;
        public int Posts;
        public override void OperationCompleted() => Completed = true;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref Posts);
            dispatcher.BeginInvoke(() => callback(state));
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(TestRepository.Root, "artifacts", "ordinary-checkpoint-handoff", Guid.NewGuid().ToString("N"));
        internal DangoApplicationHost Host { get; }
        internal ControlCenterWindow Window { get; }
        internal LocalRoomHandoffGate Gate { get; }
        internal Fixture()
        {
            Directory.CreateDirectory(root);
            var store = new DangoRosterStore(root);
            var created = store.Create(store.LoadOrInitialize().Snapshot!, 0, "虚构团子", DangoAppearanceConfiguration.Default, Guid.NewGuid());
            Assert.AreEqual(FurnitureCommitState.Committed, created.State);
            Host = new(settingsFile: Path.Combine(root, "settings.json"));
            Window = (ControlCenterWindow)typeof(DangoApplicationHost).GetMethod("GetOrCreateControlCenter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Host, null)!;
            ProductHostTestSupport.WaitFor(() => Window.ProductHome.CatalogSnapshot is not null, "Real catalog preparation must finish before the fixture owns it.");
            SetField("selectedCatalogRoomId", Window.ProductHome.CatalogSnapshot!.Rooms.First().RoomId);
            Gate = (LocalRoomHandoffGate)typeof(DangoApplicationHost).GetField("roomHandoff", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Host)!;
        }
        internal void SetCheckpoint(Task task) => SetField("roomCheckpointTask", task);
        internal CompletionContext BeginCompletion()
        {
            var attempt = Gate.Begin(LocalRoomPresentation.Window);
            var context = new CompletionContext(Window.Dispatcher);
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                typeof(DangoApplicationHost).GetMethod("CompleteLocalHandoff", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(Host, [attempt, LocalRoomPresentation.Window]);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            return context;
        }
        private void SetField(string name, object value) => typeof(DangoApplicationHost).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Host, value);
        public void Dispose()
        {
            // Do not leave an intentionally pending/faulted synthetic task for Host's shutdown flush.
            SetCheckpoint(Task.CompletedTask);
            Host.Dispose();
        }
    }
}
