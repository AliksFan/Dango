using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class RoomViewControllerTests
{
    private static RoomViewController Create(double width = 960, double height = 600, double safeHeight = 200)
    {
        var view = new RoomViewController();
        view.Resize(width, height, 360, safeHeight);
        view.Update(view.WorldWidth / 2, 0);
        return view;
    }

    [TestMethod]
    public void ProjectionUsesOneScaleAndHasAnExactInverse()
    {
        var view = Create();
        foreach (var point in new[] { new LogicalPoint(0, 0), new(2160, 360), new(734.25, -42.5) })
        {
            var screen = view.Projection.WorldToScreen(point);
            var restored = view.Projection.ScreenToWorld(screen);
            Assert.AreEqual(point.X, restored.X, 1e-10);
            Assert.AreEqual(point.Y, restored.Y, 1e-10);
        }
        var first = view.Projection.WorldToScreen(new(400, 100));
        var second = view.Projection.WorldToScreen(new(500, 200));
        Assert.AreEqual(100 * view.Scale, second.X - first.X, 1e-10);
        Assert.AreEqual(second.X - first.X, second.Y - first.Y, 1e-10);
        Assert.AreEqual(536, view.Projection.WorldToScreen(new(800, 360)).Y, 1e-10);
    }

    [TestMethod]
    public void StandardScaleDoesNotGrowWithWindowWidth()
    {
        var view = Create();
        view.Resize(2400, 600, 360);
        Assert.AreEqual(1.4, view.Scale, 1e-10);
        view.Resize(4000, 600, 360);
        Assert.AreEqual(1.4, view.Scale, 1e-10);
        Assert.AreEqual(0, view.CameraX, 1e-10);
        Assert.AreEqual((4000 - 2160 * 1.4) / 2, view.Projection.OffsetX, 1e-10);
    }

    [TestMethod]
    public void NeutralVisibleWidthCalibratesOnceAndNeverTracksDeformation()
    {
        var view = Create();
        Assert.IsTrue(view.CalibrateStandardScale(100));
        Assert.AreEqual(154, 100 * view.StandardScale, 1e-10);
        Assert.IsFalse(view.CalibrateStandardScale(70));
        Assert.AreEqual(1.54, view.StandardScale, 1e-10);
        Assert.AreEqual(100, view.ZoomPercent, 1e-10);
    }

    [TestMethod]
    public void WholeShowsTheEntireWorldAndNeverEnlargesBeyondStandard()
    {
        var view = Create();
        view.SelectPreset(RoomViewPreset.Whole);
        Assert.AreEqual(960d / 2160, view.Scale, 1e-10);
        var topLeft = view.Projection.WorldToScreen(new(0, 0));
        var bottomRight = view.Projection.WorldToScreen(new(2160, 360));
        Assert.IsTrue(topLeft.X >= -1e-10 && topLeft.Y >= 24 - 1e-10);
        Assert.IsTrue(bottomRight.X <= 960 + 1e-10 && bottomRight.Y <= 536 + 1e-10);
        view.Resize(4000, 1800, 360);
        Assert.AreEqual(1.4, view.Scale, 1e-10);
        Assert.AreEqual(RoomViewPreset.Whole, view.Preset);
    }

    [TestMethod]
    public void StandardAndNearBothRespectTheCompleteActivityEnvelope()
    {
        var view = Create(960, 420, 300);
        Assert.AreEqual(332d / 300, view.Scale, 1e-10);
        view.SelectPreset(RoomViewPreset.Near);
        Assert.AreEqual(332d / 300, view.Scale, 1e-10);
        Assert.AreEqual(24, view.Projection.WorldToScreen(new(1000, 60)).Y, 1e-10);
        view.Resize(960, 650, 360, 300);
        Assert.AreEqual(1.4 * 1.3, view.Scale, 1e-10);
        Assert.AreEqual(RoomViewPreset.Near, view.Preset);
    }

    [TestMethod]
    public void WholeAndWheelRangeStayValidForAnEnvelopeTallerThanTheWorld()
    {
        var view = Create(4000, 420, 600);
        view.SelectPreset(RoomViewPreset.Whole);
        Assert.AreEqual(332d / 600, view.Scale, 1e-10);
        Assert.IsFalse(view.ApplyZoomSteps(100));
        Assert.AreEqual(332d / 600, view.Scale, 1e-10);
    }

    [TestMethod]
    public void WheelAddsStandardPercentagePointsAndSupportsIntermediateViews()
    {
        var view = Create();
        Assert.IsTrue(view.ApplyZoomSteps(1));
        Assert.AreEqual(110, view.ZoomPercent, 1e-10);
        Assert.IsTrue(view.ApplyZoomSteps(1));
        Assert.AreEqual(120, view.ZoomPercent, 1e-10);
        Assert.AreEqual(RoomViewPreset.Custom, view.Preset);
        view.ApplyZoomSteps(-1);
        Assert.AreEqual(110, view.ZoomPercent, 1e-10);
    }

    [TestMethod]
    public void WheelClampsAtWholeAndNearLimitsAndZeroDoesNotChangePreset()
    {
        var view = Create();
        Assert.IsFalse(view.ApplyZoomSteps(0));
        Assert.AreEqual(RoomViewPreset.Standard, view.Preset);
        view.ApplyZoomSteps(int.MaxValue);
        Assert.AreEqual(130, view.ZoomPercent, 1e-10);
        Assert.IsFalse(view.ApplyZoomSteps(1));
        view.ApplyZoomSteps(int.MinValue);
        Assert.AreEqual(960d / 2160, view.Scale, 1e-10);
        Assert.IsFalse(view.ApplyZoomSteps(-1));
        view.SelectPreset(RoomViewPreset.Standard);
        Assert.AreEqual(100, view.ZoomPercent, 1e-10);
    }

    [TestMethod]
    public void CameraStaysInsideTheFortyToSixtyPercentDeadZone()
    {
        var view = Create(640);
        var visibleWidth = 640 / view.Scale;
        var previous = view.CameraX;
        view.Update(previous + visibleWidth * .55, 100, true);
        Assert.AreEqual(previous, view.CameraX, 1e-10);
        view.Update(previous + visibleWidth * .7, 0, true);
        Assert.AreEqual(previous + visibleWidth * .1, view.CameraX, 1e-10);
        previous = view.CameraX;
        view.Update(previous + visibleWidth * .3, 0, true);
        Assert.AreEqual(previous - visibleWidth * .1, view.CameraX, 1e-10);
    }

    [TestMethod]
    public void FirstRealPetPositionIsVisibleImmediately()
    {
        var view = new RoomViewController();
        view.Resize(640, 600, 417);
        view.Update(220, 0);
        var pet = view.Projection.WorldToScreen(new(220, 417));
        Assert.IsTrue(pet.X >= 0 && pet.X <= 640);
        Assert.AreEqual(536, pet.Y, 1e-10);
    }

    [TestMethod]
    public void FollowDependsOnElapsedTimeAndCannotOvershootWorldEdges()
    {
        var fast = Create(320);
        var slow = Create(320);
        for (var i = 0; i < 10; i++) fast.Update(1400, 10);
        for (var i = 0; i < 2; i++) slow.Update(1400, 50);
        Assert.AreEqual(fast.CameraX, slow.CameraX, 1e-9);
        foreach (var pet in new[] { -500d, 5000d })
            for (var i = 0; i < 20; i++)
            {
                fast.Update(pet, 33);
                Assert.IsTrue(fast.CameraX >= 0 && fast.CameraX <= 2160 - 320 / fast.Scale);
            }
    }

    [TestMethod]
    public void LongAndNegativeElapsedTimesCannotReplayHiddenTime()
    {
        var late = Create(320);
        var bounded = Create(320);
        late.Update(1400, 10000);
        bounded.Update(1400, 100);
        Assert.AreEqual(bounded.CameraX, late.CameraX, 1e-10);
        var before = late.CameraX;
        late.Update(220, -16);
        Assert.AreEqual(before, late.CameraX, 1e-10);
    }

    [TestMethod]
    public void WholeNeverFollowsAndVerticalProjectionNeverTracksThePet()
    {
        var view = Create();
        var groundOffset = view.Projection.OffsetY;
        view.Update(200, 100, true);
        Assert.AreEqual(groundOffset, view.Projection.OffsetY, 1e-10);
        view.SelectPreset(RoomViewPreset.Whole);
        var projection = view.Projection;
        view.Update(2100, 100, true);
        Assert.AreEqual(projection, view.Projection);
        Assert.AreEqual(0, view.CameraX, 1e-10);
    }

    [TestMethod]
    public void ResizeAndPresetChangesKeepTheSameWorldAndReclampTheCamera()
    {
        var view = Create(320);
        view.Update(2100, 0, true);
        view.Resize(2400, 600, 417);
        Assert.IsTrue(view.CameraX <= 2160 - 2400 / view.Scale);
        Assert.AreEqual(536, view.Projection.WorldToScreen(new(2160, 417)).Y, 1e-10);
        Assert.AreEqual(2160, view.WorldWidth);
        Assert.AreEqual(360, view.WorldHeight);
    }

    [TestMethod]
    public void InvalidInputsCannotPoisonAnExistingProjection()
    {
        var view = Create();
        var projection = view.Projection;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => view.Resize(0, 600, 360));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => view.Resize(960, 88, 360));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => view.Resize(960, 600, double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => view.Resize(960, 600, 360, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => view.CalibrateStandardScale(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => view.Update(double.PositiveInfinity, 16));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => view.Update(800, double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => view.SelectPreset((RoomViewPreset)42));
        Assert.AreEqual(projection, view.Projection);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => default(RoomProjection).ScreenToWorld(new(1, 2)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => projection.WorldToScreen(new(double.NaN, 2)));
    }
}
