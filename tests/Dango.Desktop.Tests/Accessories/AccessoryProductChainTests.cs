using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Canvas = System.Windows.Controls.Canvas;

namespace Dango.Desktop.Tests.Accessories;

[TestClass, DoNotParallelize]
public sealed class AccessoryProductChainTests
{
    [STATestMethod]
    public void RealBeltRoomRigSurvivesReadOnlyHostsPauseRehostAndDraftCancellationWithoutChangingBody()
    {
        var module = Path.Combine(AppContext.BaseDirectory, "dango_belt_room.dll");
        Assert.IsTrue(File.Exists(module), "The actual packaged product room module must be present.");
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-e4-product-chain", Guid.NewGuid().ToString("N"));
        var session = new AccessorySettingsSession(root);
        var configuration = new AccessoryConfiguration([
            new("fictional-rigid", "Fictional rigid hat", "builtin-top-hat", 64, 64) { Layer = AccessoryLayer.Behind },
            new("fictional-chain", "Fictional curved leaf", "builtin-sprout", 64, 64)
            {
                ImagePivotX = .2, ImagePivotY = .3, BodyAnchorX = .7, BodyAnchorY = .2,
                DeformationMode = AccessoryDeformationMode.SingleChain,
                Rig = new([new(.2, .3), new(.5, .6), new(.8, .8)], .8, 6, 5), GravityInfluence = 45,
            },
        ]);
        session.Stage(configuration); Assert.IsTrue(session.TrySave(), session.StatusMessage);
        using var dressed = StackedDangoSceneRuntime.CreateBeltRoom(module);
        using var bare = StackedDangoSceneRuntime.CreateBeltRoom(module);
        dressed.BindAccessories(session);
        Assert.IsTrue(dressed.TryInitialize(), dressed.LastError);
        Assert.IsTrue(bare.TryInitialize(), bare.LastError);
        dressed.Start(); bare.Start();
        dressed.SetViewport(new(0, 0, 1600, 900)); bare.SetViewport(new(0, 0, 1600, 900));
        var primary = InputAt(400);
        var step = TimeSpan.FromSeconds(1d / 60);
        var hosts = new[] { Host(), Host() };
        long previousStep = -1;
        double? startingX = null;
        var firstDistances = Array.Empty<double>();
        var bent = false;
        for (var tick = 0; tick < 100; tick++)
        {
            if (tick == 60)
            {
                dressed.Stop(); bare.Stop();
                var stopped = dressed.LastBeltRoomFrame!.Tick;
                dressed.SetAccessories(session.SavedConfiguration);
                Assert.AreEqual(stopped, dressed.LastBeltRoomFrame.Tick);
                Assert.AreEqual(previousStep, dressed.GetAccessoryFrames(dressed.LastFrame!, 0).Last().Mesh.StepIndex);
                dressed.Start(); bare.Start();
                primary = InputAt(900);
            }
            var intent = tick is >= 20 and < 60 ? new DirectionalControlIntent(false, true, false, false) : DirectionalControlIntent.None;
            dressed.SetDirectionalControl(intent); bare.SetDirectionalControl(intent);
            var actual = dressed.Tick(primary, step); var control = bare.Tick(primary, step);
            Assert.IsNull(dressed.LastError); Assert.IsNull(bare.LastError); Assert.IsNull(dressed.AccessoryError);
            Assert.AreEqual(JsonSerializer.Serialize(bare.LastPhysicsFrame), JsonSerializer.Serialize(dressed.LastPhysicsFrame), "Accessories cannot alter actual product body physics.");
            Assert.AreEqual(BodyOutput(control), BodyOutput(actual), "Accessories cannot alter body presentation or input boundaries.");
            var outputs = dressed.GetAccessoryFrames(actual, 0);
            Assert.HasCount(2, outputs);
            var chain = outputs.Single(output => output.Item.Id == "fictional-chain").Mesh;
            if (previousStep >= 0) Assert.AreEqual(previousStep + 1, chain.StepIndex);
            previousStep = chain.StepIndex;
            var distances = chain.Vertices.Select(vertex => (vertex - chain.ImagePivot).Length).ToArray();
            if (tick == 0) firstDistances = distances;
            bent |= distances.Where((value, index) => Math.Abs(value - firstDistances[index]) > .05).Any();
            startingX ??= dressed.CurrentSelectedCenter!.Value.X;
            if (tick == 59) Assert.IsTrue(dressed.CurrentSelectedCenter!.Value.X > startingX + 1, "The actual native body must move.");
            if (tick is 10 or 30 or 70)
            {
                var before = JsonSerializer.Serialize(chain);
                var physics = dressed.LastBeltRoomFrame;
                var owner = dressed.FrameDriver;
                foreach (var (body, hat) in hosts)
                    for (var paint = 0; paint < 3; paint++)
                        DangoHostFrameBinding.Apply(body, hat, actual, 0, default, runtime: dressed);
                dressed.SetAccessories(configuration);
                var after = dressed.GetAccessoryFrames(actual, 0).Single(output => output.Item.Id == "fictional-chain").Mesh;
                Assert.AreEqual(before, JsonSerializer.Serialize(after), "Host consumption and configuration refresh cannot integrate the rig.");
                Assert.AreSame(physics, dressed.LastBeltRoomFrame); Assert.AreSame(owner, dressed.FrameDriver);
                Assert.IsNull(dressed.AccessoryError);
            }
        }
        Assert.IsTrue(bent, "A dormant or rigid-only fixture cannot satisfy this product-chain gate.");
        session.Stage(new(configuration.Items.Select(item => item with { SizePercent = 175, GravityInfluence = 73 })));
        dressed.SetAccessories(session.DraftConfiguration);
        Assert.IsTrue(session.IsDirty);
        CollectionAssert.AreEquivalent(session.DraftConfiguration.Items.ToArray(),
            dressed.GetAccessoryFrames(dressed.LastFrame!, 0).Select(output => output.Item).ToArray(),
            "The uncommitted draft must actually reach the product consumer before cancellation is tested.");
        session.CancelPreview(); dressed.SetAccessories(session.SavedConfiguration);
        var restored = dressed.GetAccessoryFrames(dressed.LastFrame!, 0);
        CollectionAssert.AreEqual(configuration.Items.ToArray(), restored.Select(output => output.Item).ToArray());
        Assert.AreEqual(configuration, new AccessorySettingsSession(root).SavedConfiguration);
        Assert.AreEqual(previousStep, restored.Last().Mesh.StepIndex);
        dressed.Stop(); bare.Stop();

        static (DeformableDangoVisual Body, DangoHatVisual Hat) Host()
        {
            var canvas = new Canvas();
            var body = new DeformableDangoVisual { Width = 160, Height = 120 };
            var hat = new DangoHatVisual { Width = 160, Height = 120 };
            canvas.Children.Add(body); canvas.Children.Add(hat);
            return (body, hat);
        }
        static PhysicsPresentationInput InputAt(double left) => new("fictional-e4-room", new(left, 350, 160, 120),
            DangoActionState.Idle, 0, 1, "fictional-ground", new(0, 1), 0, 1, false, true, []);
        // Separate native worlds allocate distinct identities. Compare every physical/presentation
        // field, including actual tick and input diagnostics, after removing only those identities.
        static string BodyOutput(StackedPhysicsPresentationFrame frame) => JsonSerializer.Serialize(frame with
        { BeltRoom = frame.BeltRoom! with { OwnerEpoch = 0, Generation = 0 } });
    }
}
