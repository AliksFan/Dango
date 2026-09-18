using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.DesktopHost.Interaction;

[TestClass]
public sealed class LightInteractionSessionTests
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1d / 60);
    private static readonly LogicalRect Bounds = new(100, 200, 160, 120);
    private static PhysicsPresentationInput Primary => new("fixture", Bounds, DangoActionState.Idle, 1000, 17,
        "ground", new(0, -1), 0, 1, false, true, []);

    [TestMethod]
    public void NormalUpClearsCaptureSessionBeforeSynchronousReleaseCallback()
    {
        using var runtime = ReadyRuntime(out var id);
        var session = new LightInteractionSession(runtime);
        var captured = 0;
        var released = 0;
        Assert.IsTrue(session.TryBegin(id, BodyCenter(runtime, id), 0, false,
            () => { captured++; return true; }, () => { released++; session.Cancel(); }, () => true));

        session.End(BodyCenter(runtime, id), 1, false);

        Assert.AreEqual(1, captured);
        Assert.AreEqual(1, released);
        Assert.IsFalse(session.IsActive, "正常 Up 引发的同步 release callback 不能取消刚接纳的反馈。");
        Assert.IsTrue(runtime.Tick(Primary, Step).Bodies[0].Attachments!.Face.Left.Openness < 1,
            "正常 Up 的 release callback 不能撤销已接纳的 blink。");
    }

    [TestMethod]
    public void CaptureFailureLeavesNoSessionOrFeedbackPath()
    {
        using var runtime = ReadyRuntime(out var id);
        var session = new LightInteractionSession(runtime);
        var releases = 0;
        Assert.IsFalse(session.TryBegin(id, BodyCenter(runtime, id), 0, false, () => false, () => releases++, () => true));
        Assert.IsFalse(session.IsActive);
        Assert.AreEqual(1, releases);
        session.End(BodyCenter(runtime, id), 1, false);
    }

    [TestMethod]
    public void DragConversionSurvivesInputCancelledReentrancyOnlyAfterRecapture()
    {
        using var runtime = ReadyRuntime(out var id);
        var session = new LightInteractionSession(runtime);
        var captures = 0;
        var releases = 0;
        runtime.InputCancelled += session.Cancel;
        Assert.IsTrue(session.TryBegin(id, BodyCenter(runtime, id), 0, false,
            () => ++captures is <= 2, () => releases++, () => true));

        var point = BodyCenter(runtime, id);
        session.Move(new(point.X + 3, point.Y), 1, false);

        Assert.IsTrue(runtime.IsDragging);
        Assert.IsTrue(session.IsActive);
        Assert.AreEqual(2, captures, "转换后必须重新捕获。");
        Assert.IsTrue(releases >= 1);
    }

    [TestMethod]
    public void FailedRecaptureEndsDragWithoutFallbackFeedback()
    {
        using var runtime = ReadyRuntime(out var id);
        var session = new LightInteractionSession(runtime);
        var captures = 0;
        Assert.IsTrue(session.TryBegin(id, BodyCenter(runtime, id), 0, false,
            () => ++captures == 1, () => { }, () => true));

        var point = BodyCenter(runtime, id);
        session.Move(new(point.X + 3, point.Y), 1, false);

        Assert.IsFalse(runtime.IsDragging);
        Assert.IsFalse(session.IsActive);
        Assert.AreEqual(2, captures);
    }

    [TestMethod]
    public void ResetMakesOldSessionHarmlessAndCancelIsIdempotent()
    {
        using var runtime = ReadyRuntime(out var id);
        var session = new LightInteractionSession(runtime);
        var oldPoint = BodyCenter(runtime, id);
        Assert.IsTrue(session.TryBegin(id, oldPoint, 0, true, () => true, () => { }, () => true));
        runtime.RegenerateDango();
        session.Move(oldPoint, 1, true);
        Assert.IsFalse(session.IsActive);
        session.Cancel();
        session.Cancel();
    }

    private static StackedDangoSceneRuntime ReadyRuntime(out Dango.Physics.DangoEntityId id)
    {
        var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize());
        runtime.Start();
        runtime.Tick(Primary, Step);
        id = runtime.EntityIds.Single();
        return runtime;
    }

    private static LogicalPoint BodyCenter(StackedDangoSceneRuntime runtime, Dango.Physics.DangoEntityId id)
    {
        var index = runtime.LastFrame!.EntityIds.ToList().IndexOf(id);
        return DangoHostFrameBinding.Bounds(runtime.LastFrame, index).Center;
    }
}
