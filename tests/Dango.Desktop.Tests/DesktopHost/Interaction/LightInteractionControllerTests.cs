using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Physics;

namespace Dango.Desktop.Tests.DesktopHost.Interaction;

[TestClass]
public sealed class LightInteractionControllerTests
{
    private static readonly LightInteractionTarget Target = new(new DangoEntityId(7), 3);
    private static readonly LogicalPoint Origin = new(100, 100);
    private static readonly LogicalRect Bounds = new(80, 80, 40, 40);

    [TestMethod]
    public void OrdinaryClickRequiresShortPressBelowThreeDipAndCurrentHit()
    {
        var controller = new LightInteractionController();
        Assert.IsTrue(controller.TryBegin(Target, Origin, 1000, false, out var session));
        Assert.AreEqual(LightInteractionResult.None, controller.Move(session, new(102.99, 100), 1100, false, Bounds));
        Assert.AreEqual(LightInteractionResult.Click, controller.End(session, new(102.99, 100), 1250, false, Bounds));
        Assert.IsFalse(controller.IsPressed);

        Assert.IsTrue(controller.TryBegin(Target, Origin, 2000, false, out session));
        Assert.AreEqual(LightInteractionResult.None, controller.End(session, new(2_000, 100), 2100, false, null));
        Assert.IsFalse(controller.IsPressed, "离开后再返回不能只看最终点；缺命中框不得反馈。");
    }

    [TestMethod]
    public void OrdinaryThresholdAndLateOrMissedMoveNeverClick()
    {
        var controller = new LightInteractionController();
        Assert.IsTrue(controller.TryBegin(Target, Origin, 0, false, out var session));
        Assert.AreEqual(LightInteractionResult.BeginDrag, controller.Move(session, new(103, 100), 1, false, Bounds));
        Assert.IsFalse(controller.IsPressed);

        Assert.IsTrue(controller.TryBegin(Target, Origin, 10, false, out session));
        Assert.AreEqual(LightInteractionResult.None, controller.End(session, new(103, 100), 11, false, Bounds));
        Assert.IsFalse(controller.IsPressed, "仅在 Up 收到越阈值坐标时也不能补发点击或开始拖动。");

        Assert.IsTrue(controller.TryBegin(Target, Origin, 20, false, out session));
        Assert.AreEqual(LightInteractionResult.None, controller.End(session, Origin, 271, false, Bounds));
    }

    [TestMethod]
    public void OrdinaryBranchIgnoresShiftAddedAfterDown()
    {
        var controller = new LightInteractionController();
        Assert.IsTrue(controller.TryBegin(Target, Origin, 0, false, out var session));
        Assert.AreEqual(LightInteractionResult.BeginDrag, controller.Move(session, new(116, 100), 100, true, Bounds));
        Assert.IsFalse(controller.IsPressed);
    }

    [TestMethod]
    public void PetUsesNetHorizontalDistanceAndSingleConsumption()
    {
        var controller = new LightInteractionController();
        Assert.IsTrue(controller.TryBegin(Target, Origin, 0, true, out var session));
        Assert.AreEqual(LightInteractionResult.None, controller.Move(session, new(112, 100), 100, true, Bounds));
        Assert.AreEqual(LightInteractionResult.None, controller.Move(session, Origin, 200, true, Bounds));
        Assert.AreEqual(LightInteractionResult.None, controller.Move(session, new(112, 100), 300, true, Bounds), "折返微抖不能按累计距离触发抚摸。");
        Assert.AreEqual(LightInteractionResult.Pet, controller.Move(session, new(116, 108), 600, true, Bounds));
        Assert.IsTrue(controller.IsPressed);
        Assert.AreEqual(LightInteractionResult.None, controller.Move(session, new(80, 100), 601, true, Bounds));
        Assert.AreEqual(LightInteractionResult.None, controller.End(session, new(80, 100), 602, false, Bounds));
        Assert.IsFalse(controller.IsPressed, "已消费抚摸的正常结束或 Shift 松开不能反向取消反馈。");
    }

    [TestMethod]
    public void PetCancelsBeforeConsumptionForShiftBoundsVerticalAndTimeFailures()
    {
        foreach (var failure in new[]
        {
            new { Point = new LogicalPoint(116, 100), Time = 100L, Shift = false, Bounds = (LogicalRect?)Bounds },
            new { Point = new LogicalPoint(116, 109), Time = 100L, Shift = true, Bounds = (LogicalRect?)Bounds },
            new { Point = new LogicalPoint(116, 100), Time = 601L, Shift = true, Bounds = (LogicalRect?)Bounds },
            new { Point = new LogicalPoint(121, 100), Time = 100L, Shift = true, Bounds = (LogicalRect?)new LogicalRect(80, 80, 30, 40) },
        })
        {
            var controller = new LightInteractionController();
            Assert.IsTrue(controller.TryBegin(Target, Origin, 0, true, out var session));
            Assert.AreEqual(LightInteractionResult.Cancelled, controller.Move(session, failure.Point, failure.Time, failure.Shift, failure.Bounds));
            Assert.IsFalse(controller.IsPressed);
        }
    }

    [TestMethod]
    public void PetRecognizedOnUpStillEndsThePress()
    {
        var controller = new LightInteractionController();
        Assert.IsTrue(controller.TryBegin(Target, Origin, 0, true, out var session));
        Assert.AreEqual(LightInteractionResult.Pet, controller.End(session, new(116, 108), 600, true, Bounds));
        Assert.IsFalse(controller.IsPressed);
    }

    [TestMethod]
    public void StaleEventsCannotAffectReplacementSessionAndExplicitCancellationIsIdempotent()
    {
        var controller = new LightInteractionController();
        Assert.IsTrue(controller.TryBegin(Target, Origin, 0, false, out var first));
        Assert.IsTrue(controller.Cancel(first));
        Assert.IsFalse(controller.Cancel(first));
        Assert.IsTrue(controller.TryBegin(Target, Origin, 10, false, out var second));
        Assert.AreNotEqual(first, second);

        Assert.AreEqual(LightInteractionResult.None, controller.Move(first, new(103, 100), 11, false, Bounds));
        Assert.AreEqual(second, controller.SessionId);
        Assert.IsTrue(controller.IsPressed);
        Assert.AreEqual(LightInteractionResult.Click, controller.End(second, Origin, 12, false, Bounds));
    }

    [TestMethod]
    public void InvalidOrBackwardInputClearsWithoutInventingFeedback()
    {
        var controller = new LightInteractionController();
        Assert.IsFalse(controller.TryBegin(Target, new(double.NaN, 0), 0, false, out _));
        Assert.IsTrue(controller.TryBegin(Target, Origin, 100, false, out var session));
        Assert.AreEqual(LightInteractionResult.Cancelled, controller.Move(session, Origin, 99, false, Bounds));
        Assert.IsFalse(controller.IsPressed);
    }
}
