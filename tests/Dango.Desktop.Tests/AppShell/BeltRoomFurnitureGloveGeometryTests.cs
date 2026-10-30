using System.Reflection;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.AppShell;

[TestClass, DoNotParallelize]
public sealed class BeltRoomFurnitureGloveGeometryTests
{
    [STATestMethod, TestCategory("NativeIntegration")]
    public void GloveStartsAtTheContinuousNativeAttachmentWithoutLegacyNodeContraction()
    {
        var module = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE")
            ?? throw new InvalidOperationException("An explicit independent room module is required.");
        var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "belt-glove-geometry", Guid.NewGuid().ToString("N"));
        using var runtime = StackedDangoSceneRuntime.CreateBeltRoom(module);
        runtime.ConfigureBeltRoomAppearance(DangoAppearanceConfiguration.Default, beginRoom: true);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        runtime.ConfigureOrdinaryFurniture(root);
        runtime.SetHorizontalBounds(24, 2136);
        var session = new DesktopSceneSession(); session.AttachSurface(new(0, 0, 2160, 360), 0);
        var c = session.Controller!;
        var input = PhysicsPresentationInput.FromSnapshot(c.Snapshot, c.SquashAmount, c.GroundFrictionCue, c.FacingDirection, null);
        runtime.Start();
        // A controlled synchronous owner token satisfies the same furniture boundary without creating a window.
        runtime.FrameDriver = this;
        try
        {
            for (var i = 0; i < 180; i++) runtime.Tick(input, HostFrameClock.FixedStep);
            Assert.IsTrue(runtime.AttachOrdinaryFurniture(), runtime.Furniture?.Status);
            runtime.SetDirectionalControl(new(false, true, false, false));
            for (var i = 0; i < 20; i++) runtime.Tick(input, HostFrameClock.FixedStep);
            runtime.SetDirectionalControl(DirectionalControlIntent.None);
            Assert.IsNull(runtime.LastError);
            var before = runtime.LastBeltRoomFrame!;
            var expected = BeltRoomBodyGeometry.DragAttachment(before);
            var scene = Field<DesktopPhysicsScene>(runtime, "scene");
            var host = scene.ToHost(expected);
            Assert.IsTrue(runtime.BeginOrdinaryFurnitureGlove(host.X, scene.GroundY - host.Y));
            var target = Field<LogicalPoint>(runtime, "gloveTargetNative");
            Assert.AreEqual(expected.X, target.X, 1e-12);
            Assert.AreEqual(expected.Y, target.Y, 1e-12);
            var nativeInputTarget = Field<LogicalPoint>(scene, "dragTarget");
            Assert.AreEqual(expected.X, nativeInputTarget.X, 1e-10);
            Assert.AreEqual(expected.Y, nativeInputTarget.Y, 1e-10);
            runtime.Tick(input, HostFrameClock.FixedStep);
            Assert.IsNull(runtime.LastError);
            var dragging = runtime.LastBeltRoomFrame!;
            Assert.AreEqual(1d, dragging.BodySnapshot[83]);
            // Native selected theta on the pre-step q. Compare independently with the initial glove target.
            var nativeBegin = Evaluate(before, dragging.BodySnapshot[80]);
            Assert.AreEqual(nativeBegin.X, target.X, 1e-7);
            Assert.AreEqual(nativeBegin.Y, target.Y, 1e-7);
            var frozen = Evaluate(dragging, dragging.BodySnapshot[80]);
            var helper = BeltRoomBodyGeometry.DragAttachment(dragging);
            Assert.AreEqual(frozen.X, helper.X, 1e-12);
            Assert.AreEqual(frozen.Y, helper.Y, 1e-12);
            runtime.EndOrdinaryFurnitureGlove();
        }
        finally { runtime.FrameDriver = null; runtime.Stop(); }
    }
    private static LogicalPoint Evaluate(DangoBeltRoomFrame frame, double theta)
    {
        var q = frame.BodySnapshot;
        var x = q[2]; var y = q[3];
        for (var n = 1; n <= 8; n++)
        {
            x += q[4 * n] * Math.Cos(n * theta) + q[4 * n + 2] * Math.Sin(n * theta);
            y += q[4 * n + 1] * Math.Cos(n * theta) + q[4 * n + 3] * Math.Sin(n * theta);
        }
        return new(x, y);
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
}
