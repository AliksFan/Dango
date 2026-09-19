using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class AttachmentNativeCalibrationTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void AcceptedModuleSettledBodiesHaveFiniteWidthNeutralContact(int count)
    {
        using var world = DangoPhysicsWorld.Create(Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName));
        var ids = Enumerable.Range(0, count).Select(i => world.Add(0, i * 1.6)).ToArray();
        DangoPhysicsFrame? frame = null;
        for (var i = 0; i < 240; i++) frame = world.Step(TimeSpan.FromSeconds(1d / 60), ids.Select(id => new DangoPhysicsInput(id)).ToArray());
        Assert.IsTrue(frame!.Succeeded);
        foreach (var body in frame.Bodies)
        {
            var presenter = new DangoPhysicsPresentationAdapter(DangoPhysicsDefaults.Current);
            var presentation = presenter.Present(body, TimeSpan.FromSeconds(1d / 60), DangoFacingDirection.Center);
            var track = HatTrackSolver.Create(AttachmentShapeGeometry.Create(presentation.Contour));
            Assert.IsNotNull(track, $"count={count}, id={body.EntityId}");
            var pose = track.Fit(Math.Clamp(0, track.Minimum, track.Maximum));
            Assert.IsNotNull(pose);
            Assert.IsTrue(pose.MaximumPenetration <= 0.007);
            using var output = TestEvidence.Create("native-calibration", $"body-{count}-{body.EntityId.Value}.json");
            System.Text.Json.JsonSerializer.Serialize(output, new { count, body.EntityId, pose, presentation.Contour });
        }
    }
}
