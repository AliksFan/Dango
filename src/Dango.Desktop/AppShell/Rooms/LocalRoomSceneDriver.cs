using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.AppShell.Rooms;

internal sealed class LocalRoomSceneDriver : IDisposable
{
    private static readonly LogicalRect Surface = new(0, 0, LocalRoomSceneView.WorldWidth, LocalRoomSceneView.WorldHeight);
    private readonly DesktopSceneSession session;
    private readonly StackedDangoSceneRuntime runtime;
    private readonly HostFrameScheduler scheduler;
    private LocalRoomSceneView? view;
    private DirectionalControlIntent intent;
    private bool alignmentPending;
    private bool readySent;
    private long generation;
    private EventHandler? confirmation;
    private double resumeX = LocalRoomSceneView.WorldWidth / 2;
    internal event Action<long>? HealthyFrameReady;
    internal event Action<long, string>? Failed;
    internal bool OwnsRuntime => scheduler.OwnsRuntime;
    internal void ResetEntryPosition() => resumeX = LocalRoomSceneView.WorldWidth / 2;
    internal LocalRoomSceneDriver(DesktopSceneSession session, StackedDangoSceneRuntime runtime)
    {
        this.session = session;
        this.runtime = runtime;
        scheduler = new(runtime, Prepare, CreateInput, Apply);
    }

    internal void Start(LocalRoomSceneView target, long attempt, bool enterWorld)
    {
        scheduler.VerifyCanStart();
        view = target;
        generation = attempt;
        readySent = false;
        intent = DirectionalControlIntent.None;
        target.BeginPresentation();
        if (enterWorld)
        {
            session.AttachSurface(Surface, Environment.TickCount64);
            var bounds = session.Controller!.Snapshot.Dango.VisualBounds;
            session.Controller.MoveTo(new(resumeX - bounds.Width / 2, Surface.Bottom - bounds.Height));
            runtime.SetViewport(Surface);
            alignmentPending = true;
        }
        runtime.Start();
        runtime.AttachQuietIdleHost(this, true);
        runtime.UpdateWanderScreenWidth(this, Surface.Width);
        scheduler.Start();
    }

    internal void Stop()
    {
        if (runtime.CurrentSelectedCenter is { } center && scheduler.OwnsRuntime) resumeX = center.X;
        ClearInput();
        scheduler.Stop();
        if (confirmation is not null) CompositionTarget.Rendering -= confirmation;
        confirmation = null;
        ++generation;
        if (runtime.FrameDriver is null && runtime.IsRunning) runtime.Stop();
    }

    internal void ClearInput()
    {
        intent = DirectionalControlIntent.None;
        view?.ClearWheel();
        if (scheduler.OwnsRuntime) runtime.CancelInput();
    }

    internal bool Key(System.Windows.Input.Key key, bool down, bool repeat)
    {
        if (!scheduler.OwnsRuntime || view?.InputBlocked == true || !readySent) return false;
        if (down && repeat) return key is System.Windows.Input.Key.A or System.Windows.Input.Key.D or System.Windows.Input.Key.S or System.Windows.Input.Key.Space;
        intent = key switch
        {
            System.Windows.Input.Key.A => intent with { Left = down },
            System.Windows.Input.Key.D => intent with { Right = down },
            System.Windows.Input.Key.S => intent with { Squash = down },
            System.Windows.Input.Key.Space => intent with { Jump = down },
            _ => intent,
        };
        return key is System.Windows.Input.Key.A or System.Windows.Input.Key.D or System.Windows.Input.Key.S or System.Windows.Input.Key.Space;
    }

    private bool Prepare()
    {
        if (!scheduler.OwnsRuntime || view is null || !view.IsVisible || view.ActualWidth <= 0 || view.ActualHeight <= 0) return false;
        if (runtime.LastError is not null) { ReportFailure("团子暂不可用，请返回设置重试。"); return false; }
        try
        {
            if (alignmentPending)
            {
                if (runtime.LastFrame is null)
                { runtime.SetDirectionalControl(DirectionalControlIntent.None); return true; }
                var bounds = session.Controller!.Snapshot.Dango.VisualBounds;
                if (!runtime.TryGetHorizontalPlacement(bounds.Center, 24, Surface.Right - 24, out var safe))
                { ReportFailure("房间放置不可用，请返回门廊重试。"); return false; }
                session.Controller.MoveTo(new(safe.X - bounds.Width / 2, bounds.Y));
                if (!runtime.RehostSelectedEntityAt(session.Controller.Snapshot.Dango.VisualBounds.Center)) return false;
                runtime.SetHorizontalBounds(24, Surface.Right - 24);
                alignmentPending = false;
            }
            runtime.SetDirectionalControl(view.InputBlocked ? DirectionalControlIntent.None : intent);
            runtime.UpdateQuietIdleHost(this, true);
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        { ReportFailure("房间物理准备失败，请返回门廊重试。"); return false; }
    }

    private PhysicsPresentationInput CreateInput()
    {
        var c = session.Controller!;
        return PhysicsPresentationInput.FromSnapshot(c.Snapshot, c.SquashAmount, c.GroundFrictionCue, c.FacingDirection, null);
    }

    private void Apply(StackedPhysicsPresentationFrame frame)
    {
        if (view is null || alignmentPending || !view.ApplyFrame(frame, runtime))
        { if (runtime.LastError is not null) ReportFailure("房间首帧不可用，请返回门廊重试。"); return; }
        if (readySent || confirmation is not null) return;
        var attempt = generation;
        var target = view;
        // Wait one subsequent composition callback after the healthy body/hat were applied.
        confirmation = (_, _) =>
        {
            CompositionTarget.Rendering -= confirmation;
            confirmation = null;
            target.Dispatcher.BeginInvoke(() =>
            {
                if (generation != attempt || !scheduler.OwnsRuntime || !ReferenceEquals(target, view) ||
                    !target.HasHealthyFrame || !target.IsVisible || PresentationSource.FromVisual(target) is null || runtime.LastError is not null) return;
                readySent = true;
                HealthyFrameReady?.Invoke(attempt);
            }, DispatcherPriority.Render);
        };
        CompositionTarget.Rendering += confirmation;
    }

    private void ReportFailure(string message)
    {
        var attempt = generation;
        view!.Dispatcher.BeginInvoke(() => { if (attempt == generation) Failed?.Invoke(attempt, message); });
    }

    public void Dispose() => Stop();
}
