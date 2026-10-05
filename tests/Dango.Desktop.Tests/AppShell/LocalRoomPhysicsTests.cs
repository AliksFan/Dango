using System.Reflection;
using System.Windows;
using Dango.Core.Animation;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class LocalRoomPhysicsTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void RoomWorldKeepsRealGroundAndSidesAndSafeJumpEnvelopeAtMinimumViewport()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var session = new DesktopSceneSession();
        session.AttachSurface(new(0, 0, LocalRoomSceneView.WorldWidth, LocalRoomSceneView.WorldHeight), 0);
        var box = session.Controller!.Snapshot.Dango.VisualBounds;
        session.Controller.MoveTo(new(1000, 300));
        runtime.SetViewport(new(0, 0, LocalRoomSceneView.WorldWidth, LocalRoomSceneView.WorldHeight));
        runtime.Start();
        var c = session.Controller;
        var input = PhysicsPresentationInput.FromSnapshot(c.Snapshot, c.SquashAmount, c.GroundFrictionCue, c.FacingDirection, null);
        var step = TimeSpan.FromSeconds(1d / 60);
        for (var i = 0; i < 300; i++) runtime.Tick(input, step);
        Assert.IsTrue(runtime.RehostSelectedEntityAt(box.Center));
        // Keep the input reference and native host origin consistent after rehost.
        c.MoveTo(new(box.X, box.Y));
        input = PhysicsPresentationInput.FromSnapshot(c.Snapshot, c.SquashAmount, c.GroundFrictionCue, c.FacingDirection, null);
        runtime.SetHorizontalBounds(24, LocalRoomSceneView.WorldWidth - 24);
        var ground = runtime.LocalGroundY!.Value;
        var entity = runtime.EntityIds.Single();
        var view = new LocalRoomSceneView { Width = 640, Height = 384 };
        view.Measure(new(640, 384)); view.Arrange(new(0, 0, 640, 384));
        double lowestTop = double.PositiveInfinity;
        runtime.SetDirectionalControl(new(false, false, false, true));
        var jump = runtime.Tick(input, step);
        runtime.SetDirectionalControl(DirectionalControlIntent.None);
        foreach (var preset in new[] { RoomViewPreset.Standard, RoomViewPreset.Near, RoomViewPreset.Whole })
        {
            view.SelectView(preset);
            for (var i = 0; i < 120; i++)
            {
                var frame = i == 0 && preset == RoomViewPreset.Standard ? jump : runtime.Tick(input, step);
                Assert.IsTrue(view.ApplyFrame(frame, runtime));
                var bounds = DangoHostFrameBinding.Bounds(frame, 0);
                var visible = WanderVisibleBounds.Get(frame.Bodies[0], bounds, runtime.DisplayHatSize)!.Value;
                lowestTop = Math.Min(lowestTop, visible.Y + runtime.NeutralBodyGroundOffset);
                var screenTop = view.Camera.Projection.WorldToScreen(new(visible.X, visible.Y + runtime.NeutralBodyGroundOffset));
                Assert.IsTrue(screenTop.Y >= 23, $"hat/jump clipped at {preset}: {screenTop.Y}");
            }
        }
        Assert.IsTrue(ground - lowestTop <= LocalRoomSceneView.SafeActivityHeight);
        var resting = runtime.LastFrame!;
        var geometry = DangoStreamGeometryFactory.TryCreate(resting.Bodies[0].Contour, 160, 120).Geometry!;
        var bodyBottom = DangoHostFrameBinding.Bounds(resting, 0).Y + geometry.Bounds.Bottom + runtime.NeutralBodyGroundOffset;
        Assert.AreEqual(ground, bodyBottom, 2, "Neutral body must meet the fixed physical ground line.");
        runtime.SetDirectionalControl(new(false, true, false, false));
        for (var i = 0; i < 2600; i++) runtime.Tick(input, step);
        var right = runtime.CurrentSelectedCenter!.Value.X;
        Assert.IsTrue(right > 1800 && right < LocalRoomSceneView.WorldWidth - 24);
        runtime.SetDirectionalControl(new(true, false, false, false));
        for (var i = 0; i < 3000; i++) runtime.Tick(input, step);
        var left = runtime.CurrentSelectedCenter!.Value.X;
        Assert.IsTrue(left >= 24 && left < 240);
        Assert.AreEqual(ground, runtime.LocalGroundY.Value, 1e-9);
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.IsNull(runtime.LastError);
        runtime.Stop();
    }
}
