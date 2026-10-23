using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.StandardWindow;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Canvas = System.Windows.Controls.Canvas;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryIntegrationTests
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(.02);
    private static readonly PhysicsPresentationInput Input = new("accessory-fictional", new(400, 350, 160, 120),
        DangoActionState.Idle, 0, 1, "ground", new(0, 1), 0, 1, false, true, []);
    private static string NativePath => Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);

    private static AccessoryConfiguration Multiple(bool dynamic = false) => new([
        new("behind", "Fictional rear", "builtin-sprout", 64, 64)
        {
            Layer = AccessoryLayer.Behind, Order = 2, BodyAnchorX = .4, BodyAnchorY = .3,
            SizePercent = 160, DeformationMode = dynamic ? AccessoryDeformationMode.SingleChain : AccessoryDeformationMode.Rigid,
            Rig = dynamic ? new([new(.5, .5), new(.7, .7), new(.8, .9)], .95) : null, GravityInfluence = dynamic ? 90 : 0,
            ConnectionStrength = dynamic ? 20 : 100, LockRotation = !dynamic,
        },
        new("front-low", "Fictional front lower", "builtin-top-hat", 64, 64)
        {
            Layer = AccessoryLayer.Front, Order = 5, BodyAnchorX = .7, BodyAnchorY = .4,
            DeformationMode = dynamic ? AccessoryDeformationMode.SingleChain : AccessoryDeformationMode.Rigid,
            Rig = dynamic ? new([new(.2, .15), new(.5, .6), new(.8, .9)], .7) : null, GravityInfluence = dynamic ? 60 : 0,
            ConnectionStrength = dynamic ? 45 : 100, ImagePivotX = .2, ImagePivotY = .15,
        },
        new("front-high", "Fictional front upper", "builtin-bowler", 64, 64)
        {
            Layer = AccessoryLayer.Front, Order = 9, BodyAnchorX = .5, BodyAnchorY = 0,
            DeformationMode = dynamic ? AccessoryDeformationMode.SingleChain : AccessoryDeformationMode.Rigid,
            Rig = dynamic ? new([new(.5, .5), new(.7, .7), new(.8, .9)], .45) : null, GravityInfluence = dynamic ? 30 : 0,
            ConnectionStrength = dynamic ? 60 : 100, BaseAngleDegrees = 15,
        },
    ]);

    private static StackedDangoSceneRuntime Ready(AccessoryConfiguration configuration, bool gait = true)
    {
        Assert.IsTrue(File.Exists(NativePath), "The approved native physics candidate must be provided to this integration gate.");
        var session = new AccessorySettingsSession(NewRoot()); session.Stage(configuration);
        var runtime = StackedDangoSceneRuntime.CreateProduct(NativePath);
        runtime.BindAccessories(session); runtime.SetAccessories(configuration);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        runtime.GaitEnabled = gait;
        runtime.Start(); runtime.SetViewport(new(0, 0, 1600, 900));
        var owner = new object(); runtime.AttachQuietIdleHost(owner, true); runtime.UpdateWanderScreenWidth(owner, 1600);
        runtime.Wander.Next = (minimum, _) => minimum;
        runtime.QuietIdle.Next = (minimum, _) => minimum;
        return runtime;
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void MultipleSingleChainAndElasticAccessoriesCannotAlterAnyNativeOrBodyStep(bool maximumBending)
    {
        using var bare = Ready(AccessoryConfiguration.Empty);
        var extremes = new AccessoryConfiguration(Multiple(dynamic: true).Items.Select(item => item with
        {
            ConnectionStrength = 0,
            Rig = new(item.Rig!.Nodes, maximumBending ? 1 : .7),
        }));
        using var dressed = Ready(extremes);
        var sawDeformedTexture = false; var sawAirborne = false;
        AccessoryMeshFrame? originalMesh = null;
        for (var step = 0; step < 550; step++)
        {
            var command = step switch
            {
                < 150 => DirectionalControlIntent.None,
                < 235 => new(false, true, false, false),
                < 270 => new(false, false, false, true),
                < 340 => DirectionalControlIntent.None,
                < 360 => new(false, false, true, false),
                _ => DirectionalControlIntent.None,
            };
            bare.SetDirectionalControl(command); dressed.SetDirectionalControl(command);
            var a = bare.Tick(Input, Step); var b = dressed.Tick(Input, Step);
            Assert.IsNull(bare.LastError); Assert.IsNull(dressed.LastError);
            Assert.IsNull(dressed.AccessoryError);
            Assert.AreEqual(JsonSerializer.Serialize(bare.LastPhysicsFrame), JsonSerializer.Serialize(dressed.LastPhysicsFrame), $"Native body changed at step {step}.");
            Assert.AreEqual(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b), $"Accessory altered body, contour, gait, attachments or positioning at step {step}.");
            Assert.AreEqual(a.Bodies[0].Diagnostics.SimulatedStepCount, b.Bodies[0].Diagnostics.SimulatedStepCount);
            Assert.HasCount(0, bare.GetAccessoryFrames(a, 0));
            var outputs = dressed.GetAccessoryFrames(b, 0); Assert.HasCount(3, outputs);
            var mesh = outputs[0].Mesh; originalMesh ??= mesh;
            sawDeformedTexture |= Math.Abs((mesh.Vertices[0] - mesh.Vertices[^1]).Length - (originalMesh.Vertices[0] - originalMesh.Vertices[^1]).Length) > .1;
            sawAirborne |= !dressed.LastPhysicsFrame!.Bodies[0].IsGrounded && step >= 340;
        }
        Assert.IsTrue(sawDeformedTexture, "The comparison must exercise real internal deformation rather than dormant options.");
        Assert.IsTrue(sawAirborne, "The comparison must include actual native jump motion.");
    }

    [STATestMethod]
    [DataRow("Cancel")]
    [DataRow("Face")]
    [DataRow("Hide")]
    [DataRow("Settings")]
    public void LeavingActualHomeWardrobeRestoresSavedConfigurationWithoutWriting(string action)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root); session.Stage(Multiple()); Assert.IsTrue(session.TrySave());
        var manifest = Path.Combine(root, "accessories-v2.json"); var originalBytes = File.ReadAllBytes(manifest);
        var saved = session.SavedConfiguration;
        var home = new ProductHomeView { ReduceMotion = true }; home.BindAccessories(session);
        var panel = Field<AccessoryEditorPanel>(home, "accessoryPanel");
        var window = new Window { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show(); Pump(20); home.ShowEditor(HomeEditor.Wardrobe);
            panel.SelectItem("front-high"); panel.Edit(item => item with { SizePercent = 270, GravityInfluence = 95 });
            Assert.IsTrue(session.IsDirty);
            switch (action)
            {
                case "Cancel": panel.CancelDraft(); break;
                case "Face": home.ShowEditor(HomeEditor.Face); break;
                case "Hide": window.Hide(); break;
                default: home.ShowSettingsPage(); break;
            }
            Assert.AreEqual(saved, session.DraftConfiguration);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(manifest), "Navigation or hiding silently persisted the wardrobe draft.");
            Assert.AreEqual(saved, new AccessorySettingsSession(root).SavedConfiguration);
            Assert.IsFalse(session.IsDirty);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    public void StableEquippedHostNeverReshowsOrRebuildsItsHiddenLegacyHatWhileAllEightMeshesAdvance()
    {
        var configuration = new AccessoryConfiguration(Enumerable.Range(0, 8).Select(index =>
            new AccessoryItem($"fictional-eight-{index}", $"Fictional accessory {index}",
                index % 2 == 0 ? "builtin-sprout" : "builtin-bowler", 64, 64)
            {
                Layer = index % 2 == 0 ? AccessoryLayer.Behind : AccessoryLayer.Front,
                Order = index,
                BodyAnchorX = .2 + index * .08,
                BodyAnchorY = .2 + index % 3 * .15,
                DeformationMode = AccessoryDeformationMode.SingleChain,
                Rig = new([new(.5, .5), new(.7, .8)], .35),
                GravityInfluence = 20,
                LockRotation = false,
            }).ToArray());
        using var runtime = Ready(configuration);
        for (var step = 0; step < 150; step++) runtime.Tick(Input, Step);
        var canvas = new Canvas();
        var body = new DeformableDangoVisual { ShowHat = false }; var hat = new DangoHatVisual();
        canvas.Children.Add(body); canvas.Children.Add(hat);
        DangoHostFrameBinding.Apply(body, hat, runtime.LastFrame!, 0, runtime: runtime);
        Assert.AreEqual(Visibility.Collapsed, hat.Visibility);
        var gaitLayer = (UIElement)hat.FindName("GaitLayer");
        var hatLayer = (Canvas)hat.FindName("HatLayer");
        var originalGait = gaitLayer.RenderTransform; var originalPlacement = hatLayer.RenderTransform;
        var originalPaths = hatLayer.Children.Cast<System.Windows.Shapes.Path>().ToArray();
        var visibilityChanges = 0; var gaitChanges = 0; var placementChanges = 0; var gaitFrames = 0;
        var visibility = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(DangoHatVisual))!;
        var transform = DependencyPropertyDescriptor.FromProperty(UIElement.RenderTransformProperty, typeof(UIElement))!;
        void VisibilityChanged(object? sender, EventArgs args) => visibilityChanges++;
        void GaitChanged(object? sender, EventArgs args) => gaitChanges++;
        void PlacementChanged(object? sender, EventArgs args) => placementChanges++;
        try
        {
            visibility.AddValueChanged(hat, VisibilityChanged);
            transform.AddValueChanged(gaitLayer, GaitChanged); transform.AddValueChanged(hatLayer, PlacementChanged);
            for (var step = 0; step < 30; step++)
            {
                runtime.SetDirectionalControl(new(false, true, false, false));
                var frame = runtime.Tick(Input, Step);
                var physical = runtime.LastPhysicsFrame; var serialized = JsonSerializer.Serialize(frame);
                var nativeStep = frame.Bodies[0].Diagnostics.SimulatedStepCount;
                Assert.IsNotNull(frame.Bodies[0].Attachments!.Hat!.Pose, "Hidden external visuals must retain the full runtime Hat solver.");
                if (frame.Gait is { IsIdentity: false }) gaitFrames++;
                DangoHostFrameBinding.Apply(body, hat, frame, 0,
                    appearance: step % 2 == 0 ? HatAppearance.TopHat : HatAppearance.Bowler,
                    hatSize: 1 + step * .01, runtime: runtime);
                Assert.AreSame(physical, runtime.LastPhysicsFrame);
                Assert.AreEqual(nativeStep, runtime.LastFrame!.Bodies[0].Diagnostics.SimulatedStepCount);
                Assert.AreEqual(serialized, JsonSerializer.Serialize(runtime.LastFrame), "Binding must preserve body, Face, Hat, Gait and source geometry.");
                Assert.AreEqual(Visibility.Visible, body.Visibility);
                Assert.AreEqual(DangoContourFailure.None, body.LastContourFailure);
                var outputs = runtime.GetAccessoryFrames(frame, 0); Assert.HasCount(8, outputs);
                var meshes = canvas.Children.OfType<Canvas>().SelectMany(layer => layer.Children.OfType<AccessoryMeshVisual>()).ToArray();
                Assert.HasCount(8, meshes);
                Assert.IsTrue(meshes.All(mesh => mesh.Frame!.StepIndex == outputs[0].Mesh.StepIndex && mesh.CaptureBitmap() is not null));
            }
        }
        finally
        {
            visibility.RemoveValueChanged(hat, VisibilityChanged);
            transform.RemoveValueChanged(gaitLayer, GaitChanged); transform.RemoveValueChanged(hatLayer, PlacementChanged);
        }
        Assert.IsTrue(gaitFrames > 0, "The counterexample must retain real nonidentity walking gait.");
        Assert.AreEqual(0, visibilityChanges, "An already hidden legacy hat must not alternate Visible and Collapsed on each applied accessory frame.");
        Assert.AreEqual(0, gaitChanges); Assert.AreEqual(0, placementChanges);
        Assert.AreSame(originalGait, gaitLayer.RenderTransform); Assert.AreSame(originalPlacement, hatLayer.RenderTransform);
        CollectionAssert.AreEqual(originalPaths, hatLayer.Children.Cast<System.Windows.Shapes.Path>().ToArray(), "Changing hidden appearance must not rebuild its paths.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    public void CancelingCompatibilityPreviewRestoresTheCurrentLegacyAppearanceHatPoseAndGaitImmediately()
    {
        var session = new AccessorySettingsSession(NewRoot(), "top-hat");
        Assert.IsTrue(session.IsCompatibilityMode);
        using var runtime = StackedDangoSceneRuntime.CreateProduct(NativePath);
        runtime.BindAccessories(session); Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        runtime.Start(); runtime.SetViewport(new(0, 0, 1600, 900));
        var owner = new object(); runtime.AttachQuietIdleHost(owner, true); runtime.UpdateWanderScreenWidth(owner, null);
        for (var step = 0; step < 150; step++) runtime.Tick(Input, Step);
        var canvas = new Canvas(); var body = new DeformableDangoVisual { ShowHat = false }; var hat = new DangoHatVisual();
        canvas.Children.Add(body); canvas.Children.Add(hat);
        DangoHostFrameBinding.Apply(body, hat, runtime.LastFrame!, 0, appearance: HatAppearance.TopHat, runtime: runtime);
        Assert.AreEqual(Visibility.Visible, hat.Visibility);
        session.Stage(Multiple(dynamic: true)); runtime.SetAccessories(session.DraftConfiguration);
        for (var step = 0; step < 30; step++)
        {
            runtime.SetDirectionalControl(new(false, true, false, false));
            var frame = runtime.Tick(Input, Step);
            DangoHostFrameBinding.Apply(body, hat, frame, 0, appearance: HatAppearance.Bowler, hatSize: 1.7, runtime: runtime);
            Assert.AreEqual(Visibility.Collapsed, hat.Visibility);
        }
        var current = runtime.LastFrame!; var physical = runtime.LastPhysicsFrame;
        var count = current.Bodies[0].Diagnostics.SimulatedStepCount;
        Assert.IsTrue(current.Gait is { IsIdentity: false }, "Restoration must consume a current nonidentity gait.");
        Assert.IsNotNull(current.Bodies[0].Attachments!.Hat!.Pose);
        session.CancelPreview(); runtime.SetAccessories(session.SavedConfiguration, !session.IsCompatibilityMode);
        Assert.IsFalse(runtime.AccessoriesEnabled);
        var expected = new DangoHatVisual { Appearance = HatAppearance.Bowler, HatSize = 1.7 };
        expected.ApplyFrame(current.Bodies[0], current.Gait);
        DangoHostFrameBinding.Apply(body, hat, current, 0, appearance: HatAppearance.Bowler, hatSize: 1.7, runtime: runtime);
        Assert.AreEqual(Visibility.Visible, hat.Visibility);
        Assert.AreEqual(expected.Appearance, hat.Appearance); Assert.AreEqual(expected.HatSize, hat.HatSize);
        foreach (var layer in new[] { "HatLayer", "GaitLayer" })
            Assert.AreEqual(((UIElement)expected.FindName(layer)).RenderTransform.Value,
                ((UIElement)hat.FindName(layer)).RenderTransform.Value, "The first restored binding must use the current frame rather than stale hidden geometry.");
        CollectionAssert.AreEqual(((Canvas)expected.FindName("HatLayer")).Children.Cast<System.Windows.Shapes.Path>().Select(path => path.Data.ToString()).ToArray(),
            ((Canvas)hat.FindName("HatLayer")).Children.Cast<System.Windows.Shapes.Path>().Select(path => path.Data.ToString()).ToArray());
        Assert.IsFalse(canvas.Children.OfType<Canvas>().Any(), "Compatibility cancellation must remove the accessory host layers.");
        Assert.AreSame(physical, runtime.LastPhysicsFrame); Assert.AreEqual(count, runtime.LastFrame!.Bodies[0].Diagnostics.SimulatedStepCount);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    public void AccessoryHatSuppressionRequiresACanvasHostAndUsesTheRuntimeAppearanceEntity()
    {
        using var runtime = Ready(Multiple());
        for (var step = 0; step < 150; step++) runtime.Tick(Input, Step);
        Assert.IsTrue(runtime.AddDango()); var frame = runtime.Tick(Input, Step);
        var grid = new Grid(); var body = new DeformableDangoVisual { ShowHat = false }; var hat = new DangoHatVisual();
        grid.Children.Add(body); grid.Children.Add(hat);
        DangoHostFrameBinding.Apply(body, hat, frame, 0, appearance: HatAppearance.Bowler, runtime: runtime);
        Assert.AreEqual(Visibility.Visible, hat.Visibility, "Non-Canvas hosts have no accessory layers and must retain their ordinary hat.");
        grid.Children.Clear(); var canvas = new Canvas(); canvas.Children.Add(body); canvas.Children.Add(hat);
        // This optional color/appearance override cannot select the accessory owning entity.
        DangoHostFrameBinding.Apply(body, hat, frame, 0, appearanceEntity: frame.EntityIds[1], runtime: runtime);
        Assert.AreEqual(Visibility.Collapsed, hat.Visibility);
        var other = new DeformableDangoVisual { ShowHat = false }; var otherHat = new DangoHatVisual();
        canvas.Children.Add(other); canvas.Children.Add(otherHat);
        DangoHostFrameBinding.Apply(other, otherHat, frame, 1, appearanceEntity: frame.EntityIds[0], runtime: runtime);
        Assert.AreEqual(Visibility.Visible, otherHat.Visibility);
        // Ordinary binding without a runtime also restores the current frame immediately.
        DangoHostFrameBinding.Apply(body, hat, frame, 0, appearance: HatAppearance.TopHat);
        Assert.AreEqual(Visibility.Visible, hat.Visibility); Assert.AreEqual(HatAppearance.TopHat, hat.Appearance);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    public void ActualHostCanvasGroupsLayersByEntityAndHidesOnlyTheCustomizedLegacyHat()
    {
        using var runtime = Ready(Multiple());
        for (var step = 0; step < 150; step++) runtime.Tick(Input, Step);
        Assert.IsTrue(runtime.AddDango());
        var frame = runtime.Tick(Input, Step);
        Assert.HasCount(2, frame.Bodies);
        var canvas = new Canvas { Width = 500, Height = 300, ClipToBounds = false };
        var first = new DeformableDangoVisual { ShowHat = false }; var second = new DeformableDangoVisual { ShowHat = false };
        var firstHat = new DangoHatVisual(); var secondHat = new DangoHatVisual();
        canvas.Children.Add(first); canvas.Children.Add(second); canvas.Children.Add(firstHat); canvas.Children.Add(secondHat);
        // The same host coordinate translation deliberately overlaps the two real bodies.
        var bounds0 = DangoHostFrameBinding.Bounds(frame, 0); var bounds1 = DangoHostFrameBinding.Bounds(frame, 1);
        DangoHostFrameBinding.Apply(first, firstHat, frame, 0, new(170 - bounds0.X, 100 - bounds0.Y), runtime: runtime);
        DangoHostFrameBinding.Apply(second, secondHat, frame, 1, new(210 - bounds1.X, 110 - bounds1.Y), runtime: runtime);
        var layers = canvas.Children.OfType<Canvas>().Where(layer => layer.Children.OfType<AccessoryMeshVisual>().Any()).ToArray();
        Assert.HasCount(2, layers);
        var behind = layers.Single(layer => Canvas.GetZIndex(layer) < Canvas.GetZIndex(first));
        var front = layers.Single(layer => Canvas.GetZIndex(layer) > Canvas.GetZIndex(first));
        Assert.AreEqual(Visibility.Collapsed, firstHat.Visibility, "New accessories must not draw an additional legacy hat.");
        Assert.AreEqual(Visibility.Visible, secondHat.Visibility, "The diagnostic second entity retains its own existing appearance.");
        Assert.IsTrue(Canvas.GetZIndex(behind) < Canvas.GetZIndex(first));
        Assert.IsTrue(Canvas.GetZIndex(first) < Canvas.GetZIndex(front));
        Assert.IsTrue(Canvas.GetZIndex(front) < Canvas.GetZIndex(second), "The first entity's front accessories must not float above the next entity.");
        CollectionAssert.AreEqual(new[] { 5, 9 }, front.Children.OfType<AccessoryMeshVisual>().Select(Canvas.GetZIndex).Order().ToArray());
        Assert.HasCount(0, runtime.GetAccessoryFrames(frame, 1));
        Assert.IsTrue(layers.All(layer => !layer.IsHitTestVisible && layer.Children.OfType<AccessoryMeshVisual>().All(visual => !visual.IsHitTestVisible)));
        var physical = runtime.LastPhysicsFrame; var count = frame.Bodies[0].Diagnostics.SimulatedStepCount;
        DangoHostFrameBinding.Apply(first, firstHat, frame, 0, runtime: runtime);
        Assert.AreSame(physical, runtime.LastPhysicsFrame); Assert.AreEqual(count, runtime.LastFrame!.Bodies[0].Diagnostics.SimulatedStepCount);
        // Reapply the overlap after verifying duplicate consumption before capturing the control only.
        DangoHostFrameBinding.Apply(first, firstHat, frame, 0, new(170 - bounds0.X, 100 - bounds0.Y), runtime: runtime);
        Capture(canvas, "entity-layers");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    public void EqualOrderAccessoriesKeepStableCanvasOrderAfterCrossLayerPreviewMigration()
    {
        var a = new AccessoryItem("a", "Fictional migrating accessory", "builtin-sprout", 64, 64)
        {
            Layer = AccessoryLayer.Behind,
            Order = 0,
            BodyAnchorX = .2,
            BodyAnchorY = .3,
            LockRotation = true,
        };
        var b = new AccessoryItem("b", "Fictional existing front accessory", "builtin-bowler", 64, 64)
        {
            Layer = AccessoryLayer.Front,
            Order = 0,
            BodyAnchorX = .8,
            BodyAnchorY = .4,
            LockRotation = true,
        };
        // Deliberately reverse the configuration enumeration: equal Order must use stable Id ordering.
        using var runtime = Ready(new([b, a]));
        for (var step = 0; step < 150; step++) runtime.Tick(Input, Step);
        Assert.IsNull(runtime.LastError); Assert.IsNull(runtime.AccessoryError);
        var frame = runtime.LastFrame!;
        var canvas = new Canvas { Width = 500, Height = 300 };
        var body = new DeformableDangoVisual { ShowHat = false }; var hat = new DangoHatVisual();
        canvas.Children.Add(body); canvas.Children.Add(hat);
        DangoHostFrameBinding.Apply(body, hat, frame, 0, runtime: runtime);
        var layers = canvas.Children.OfType<Canvas>().ToArray(); Assert.HasCount(2, layers);
        var behind = layers.Single(layer => Canvas.GetZIndex(layer) < Canvas.GetZIndex(body));
        var front = layers.Single(layer => Canvas.GetZIndex(layer) > Canvas.GetZIndex(body));
        var aVisual = behind.Children.OfType<AccessoryMeshVisual>().Single();
        var bVisual = front.Children.OfType<AccessoryMeshVisual>().Single();
        Assert.AreEqual(0, Canvas.GetZIndex(aVisual)); Assert.AreEqual(0, Canvas.GetZIndex(bVisual));
        Assert.IsTrue((aVisual.Frame!.ImagePivot - bVisual.Frame!.ImagePivot).Length > 20,
            "The counterexample must distinguish the two actual anchors.");
        var physical = runtime.LastPhysicsFrame; var count = frame.Bodies[0].Diagnostics.SimulatedStepCount;
        var originalSteps = runtime.GetAccessoryFrames(frame, 0).ToDictionary(output => output.Item.Id, output => output.Mesh.StepIndex);
        var previewEvents = 0;
        runtime.AccessoryPreviewChanged += () =>
        {
            previewEvents++;
            DangoHostFrameBinding.Apply(body, hat, frame, 0, runtime: runtime);
        };

        runtime.SetAccessories(new([b, a with { Layer = AccessoryLayer.Front }]));

        Assert.AreEqual(1, previewEvents);
        Assert.AreEqual(Visibility.Collapsed, behind.Visibility); Assert.AreEqual(0, behind.Children.Count);
        Assert.AreEqual(Visibility.Visible, front.Visibility);
        // Read insertion order directly. Sorting here would hide the WPF equal-ZIndex regression.
        var actual = front.Children.OfType<AccessoryMeshVisual>().ToArray(); Assert.HasCount(2, actual);
        Assert.AreSame(aVisual, actual[0], "The migrated Id a must precede existing Id b at equal ZIndex.");
        Assert.AreSame(bVisual, actual[1]);
        var outputs = runtime.GetAccessoryFrames(frame, 0);
        CollectionAssert.AreEqual(new[] { "a", "b" }, outputs.Select(output => output.Item.Id).ToArray());
        var bounds = DangoHostFrameBinding.Bounds(frame, 0);
        for (var index = 0; index < actual.Length; index++)
        {
            Assert.AreEqual(0, Canvas.GetZIndex(actual[index]), "Stable ordering must not rewrite the user's layer Order.");
            Assert.AreEqual(outputs[index].Mesh.ImagePivot.X - bounds.X, actual[index].Frame!.ImagePivot.X, 1e-8);
            Assert.AreEqual(outputs[index].Mesh.ImagePivot.Y - bounds.Y, actual[index].Frame!.ImagePivot.Y, 1e-8);
            Assert.AreEqual(originalSteps[outputs[index].Item.Id], outputs[index].Mesh.StepIndex);
        }
        DangoHostFrameBinding.Apply(body, hat, frame, 0, runtime: runtime);
        CollectionAssert.AreEqual(actual, front.Children.OfType<AccessoryMeshVisual>().ToArray());
        Assert.AreSame(physical, runtime.LastPhysicsFrame); Assert.AreSame(frame, runtime.LastFrame);
        Assert.AreEqual(count, runtime.LastFrame!.Bodies[0].Diagnostics.SimulatedStepCount,
            "Preview and display-only rebinding must not advance the native body.");
        Assert.IsNull(runtime.AccessoryError);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    public void ActualCurvedBodyKeepsTopAndInteriorAnchorsOnItsDisplayedShapeThroughGaitSquashAndJump()
    {
        var top = new AccessoryItem("top", "Top geometric anchor", "builtin-sprout", 64, 64) { BodyAnchorX = .5, BodyAnchorY = 0 };
        var interior = top with { Id = "interior", Name = "Interior geometric anchor", BodyAnchorX = .85, BodyAnchorY = .25 };
        using var runtime = Ready(new([top, interior]));
        var sawGait = false; var sawAirborne = false; var largestHeight = 0d; var smallestHeight = double.PositiveInfinity;
        for (var step = 0; step < 540; step++)
        {
            var command = step switch
            {
                < 150 => DirectionalControlIntent.None,
                < 285 => new(false, true, false, false),
                < 320 => new(false, false, false, true),
                < 380 => DirectionalControlIntent.None,
                < 400 => new(false, false, true, false),
                _ => DirectionalControlIntent.None,
            };
            runtime.SetDirectionalControl(command);
            var frame = runtime.Tick(Input, Step); Assert.IsNull(runtime.LastError); Assert.IsNull(runtime.AccessoryError);
            var body = frame.Bodies[0]; var geometry = DangoStreamGeometryFactory.TryCreate(body.Contour, 160, 120);
            Assert.IsTrue(geometry.Succeeded);
            var displayed = geometry.Geometry!.Clone(); displayed.Transform = DeformableDangoVisual.CreateGaitTransform(body, frame.Gait);
            var outputs = runtime.GetAccessoryFrames(frame, 0); Assert.HasCount(2, outputs);
            var bounds = DangoHostFrameBinding.Bounds(frame, 0);
            var crown = outputs.Single(output => output.Item.Id == "top").Mesh.ImagePivot - new Vector(bounds.X, bounds.Y);
            var inner = outputs.Single(output => output.Item.Id == "interior").Mesh.ImagePivot - new Vector(bounds.X, bounds.Y);
            Assert.AreEqual(displayed.Bounds.Top, crown.Y, .02, "Top anchor must follow the actual drawn spline and gait rather than the control rectangle.");
            Assert.IsTrue(displayed.FillContains(inner), "An interior anchor escaped the actual curved body; bounding-box attachment is insufficient.");
            Assert.IsTrue(crown.X >= displayed.Bounds.Left && crown.X <= displayed.Bounds.Right);
            sawGait |= frame.Gait is { IsIdentity: false };
            sawAirborne |= step >= 380 && !runtime.LastPhysicsFrame!.Bodies[0].IsGrounded;
            if (step >= 150) { largestHeight = Math.Max(largestHeight, displayed.Bounds.Height); smallestHeight = Math.Min(smallestHeight, displayed.Bounds.Height); }
        }
        Assert.IsTrue(sawGait, "This gate requires actual non-identity gait.");
        Assert.IsTrue(sawAirborne, "This gate requires actual native airborne motion.");
        Assert.IsTrue(largestHeight - smallestHeight > 8, "The anchor gate must include meaningful physical body deformation.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void SavedAccessoriesFollowDefaultHomeAndRoomAcrossWindowWallpaperAndReturn()
        => VerifySavedAccessoriesAcrossHosts(desktopPet: false);

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("ForegroundInteraction")]
    public void SavedRigAccessoriesFollowHomeRoomDesktopPetAndReturn()
        => VerifySavedAccessoriesAcrossHosts(desktopPet: true);

    private static void VerifySavedAccessoriesAcrossHosts(bool desktopPet)
    {
        var root = NewRoot(); var configuration = Multiple(dynamic: desktopPet); var session = new AccessorySettingsSession(root);
        session.Stage(configuration); Assert.IsTrue(session.TrySave());
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"));
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom);
        host.ShowControlCenter();
        Dango.Tests.Support.ProductHostTestSupport.WaitForHome(host);
        var home = Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome; home.ReduceMotion = true;
        var homeOwner = home.BoundBeltBody!;
        WaitUntil(() => home.BoundBeltPresenter!.Inspect().Accessories.Count == configuration.Items.Count);
        home.ShowEditor(HomeEditor.Wardrobe); Capture(home, "home-wardrobe"); home.CloseEditor();
        home.ResumePorch();
        var porch = E<PorchSceneView>(home, "PorchScene");
        WaitUntil(() => home.IsInPorch && porch.HasBeltBody && homeOwner.Metrics.Running);
        Assert.AreEqual(configuration.Items.Count, home.BoundBeltPresenter!.Inspect().Accessories.Count);
        host.EnterLocalRoom(); WaitForRoom(host, LocalRoomPresentation.Window);
        var id = runtime.EntityIds.Single();
        var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver"); var owner = runtime.FrameDriver;
        Assert.IsTrue(driver.OwnsRuntime); Assert.IsFalse(homeOwner.Metrics.Running);
        AssertAccessories(runtime, Field<DeformableDangoVisual>(host.LocalWindow!.Scene, "body"), id, configuration);
        Capture(host.LocalWindow.Scene, "local-room-window");
        host.ShowControlCenter(); Assert.IsTrue(home.IsDangoAway); Assert.AreSame(owner, runtime.FrameDriver);
        if (desktopPet)
        {
            // Desktop pet is a distinct product room entered from the porch. It is not
            // an ordinary-room presentation toggle and must use its actual entry path.
            host.ReturnToPorch(); WaitForRoom(host, LocalRoomPresentation.Porch);
            WaitUntil(() => homeOwner.Metrics.Running && porch.HasBeltBody);
            Assert.IsFalse(driver.OwnsRuntime); Assert.IsNull(runtime.FrameDriver);
            host.EnterDesktopPetRoom();
            WaitUntil(() => host.LocalPresentation == LocalRoomPresentation.DesktopPet || host.LocalRoomFailure is not null, 4500);
            WaitForRoom(host, LocalRoomPresentation.DesktopPet);
            var scene = Field<DesktopPetRoomHost>(host, "desktopPetRoom").Scene;
            Assert.IsTrue(scene.HasHealthyFrame); Assert.AreSame(owner, runtime.FrameDriver);
            Assert.IsTrue(driver.OwnsRuntime); Assert.IsFalse(homeOwner.Metrics.Running);
            AssertAccessories(runtime, Field<DeformableDangoVisual>(scene, "body"), runtime.EntityIds.Single(), configuration);
        }
        else
        {
            host.SwitchLocalRoomPresentation(LocalRoomPresentation.Wallpaper); WaitForRoom(host, LocalRoomPresentation.Wallpaper);
            var scene = Field<LocalRoomWallpaperHost>(host, "localRoomWallpaper").Scene;
            Assert.IsTrue(scene.HasHealthyFrame); Assert.AreSame(owner, runtime.FrameDriver);
            AssertAccessories(runtime, Field<DeformableDangoVisual>(scene, "body"), id, configuration);
            host.SwitchLocalRoomPresentation(LocalRoomPresentation.Window); WaitForRoom(host, LocalRoomPresentation.Window);
            AssertAccessories(runtime, Field<DeformableDangoVisual>(host.LocalWindow.Scene, "body"), id, configuration);
        }
        host.ReturnToPorch(); WaitForRoom(host, LocalRoomPresentation.Porch);
        WaitUntil(() => homeOwner.Metrics.Running && porch.HasBeltBody);
        Assert.IsFalse(driver.OwnsRuntime); Assert.IsNull(runtime.FrameDriver);
        Assert.AreSame(homeOwner, home.BoundBeltBody);
        Assert.AreEqual(configuration.Items.Count, home.BoundBeltPresenter!.Inspect().Accessories.Count);
        host.ShowControlCenter(); WaitUntil(() => !home.IsInPorch && home.BoundBeltPresenter!.View.IsVisible);
        Assert.AreEqual(configuration.Items.Count, home.BoundBeltPresenter!.Inspect().Accessories.Count);
        Assert.AreEqual(configuration, new AccessorySettingsSession(root).SavedConfiguration);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void StandardAndWorkerWControlsConsumeOneFrameAndLayerChangesKeepLegacyHatHidden(bool wallpaper)
    {
        var configuration = Multiple(); using var runtime = Ready(configuration);
        WithHostFixture(runtime, wallpaper, host =>
        {
            var body = (DeformableDangoVisual)((FrameworkElement)host).FindName("DangoVisual");
            var hat = (DangoHatVisual)((FrameworkElement)host).FindName("DangoHat");
            AssertAccessories(runtime, body, runtime.EntityIds.Single(), configuration);
            Assert.AreEqual(Visibility.Collapsed, hat.Visibility);
            var physical = runtime.LastPhysicsFrame; var count = runtime.LastFrame!.Bodies[0].Diagnostics.SimulatedStepCount;
            var step = runtime.GetAccessoryFrames(runtime.LastFrame, 0)[0].Mesh.StepIndex;
            Invoke<object?>(host, "ApplyPresentationFrame", runtime.LastFrame);
            Invoke<object?>(host, "ApplyPresentationFrame", runtime.LastFrame);
            Assert.AreSame(physical, runtime.LastPhysicsFrame); Assert.AreEqual(count, runtime.LastFrame!.Bodies[0].Diagnostics.SimulatedStepCount);
            if (host is WorkerWWindow worker)
            {
                worker.ApplyLayeringSettings(DesktopLayeringSettings.CreateDefault() with { DangoLayer = DesktopObjectLayer.AboveDesktopIcons });
                var overlay = Field<DesktopDangoSceneOverlayWindow>(worker, "dangoAboveWindow");
                Assert.IsTrue(overlay.IsVisible);
                Assert.AreEqual(Visibility.Collapsed, body.Visibility); Assert.AreEqual(Visibility.Collapsed, hat.Visibility);
                AssertAccessories(runtime, Field<DeformableDangoVisual[]>(overlay, "bodies")[0], runtime.EntityIds.Single(), configuration);
                worker.ApplyLayeringSettings(DesktopLayeringSettings.CreateDefault());
                Assert.IsFalse(overlay.IsVisible);
                AssertAccessories(runtime, body, runtime.EntityIds.Single(), configuration);
                Assert.AreEqual(Visibility.Collapsed, hat.Visibility, "Visibility restoration must not resurrect the obsolete hat.");
                worker.SetInteractionEnabled(false); worker.SetInteractionEnabled(true);
                AssertAccessories(runtime, body, runtime.EntityIds.Single(), configuration);
                Assert.AreEqual(Visibility.Collapsed, hat.Visibility);
            }
            Assert.AreSame(physical, runtime.LastPhysicsFrame); Assert.AreEqual(count, runtime.LastFrame!.Bodies[0].Diagnostics.SimulatedStepCount);
            Assert.AreEqual(step, runtime.GetAccessoryFrames(runtime.LastFrame, 0)[0].Mesh.StepIndex, "Display-only host/layer changes invented an accessory step.");
            Capture((Canvas)body.Parent, wallpaper ? "workerw-control-accessories" : "standard-window-accessories");
        });
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ValidEmptyAndProtectedManifestsNeverFallbackToLegacyHatInActualHosts(bool wallpaper, bool corrupt)
    {
        var root = NewRoot();
        if (corrupt) File.WriteAllText(AccessorySettingsStore.GetManifestPath(root), "{fictional-invalid-json}");
        else
        {
            var saved = new AccessorySettingsSession(root); saved.Stage(AccessoryConfiguration.Empty); Assert.IsTrue(saved.TrySave());
        }
        var manifest = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var session = new AccessorySettingsSession(root, "top-hat");
        Assert.IsFalse(session.IsCompatibilityMode); Assert.AreEqual(corrupt, session.IsWriteProtected);
        using var runtime = StackedDangoSceneRuntime.CreateProduct(NativePath); runtime.BindAccessories(session);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        WithHostFixture(runtime, wallpaper, host =>
        {
            var body = (DeformableDangoVisual)((FrameworkElement)host).FindName("DangoVisual");
            var hat = (DangoHatVisual)((FrameworkElement)host).FindName("DangoHat");
            Assert.AreEqual(Visibility.Visible, body.Visibility); Assert.AreEqual(Visibility.Collapsed, hat.Visibility);
            Assert.HasCount(0, runtime.GetAccessoryFrames(runtime.LastFrame!, 0));
            Assert.IsFalse(body.ShowHat);
            Assert.IsFalse(((Canvas)body.Parent).Children.OfType<Canvas>().Any(layer => layer.Visibility == Visibility.Visible && layer.Children.OfType<AccessoryMeshVisual>().Any()));
            if (host is WorkerWWindow worker)
            {
                worker.ApplyLayeringSettings(DesktopLayeringSettings.CreateDefault() with { DangoLayer = DesktopObjectLayer.AboveDesktopIcons });
                var overlay = Field<DesktopDangoSceneOverlayWindow>(worker, "dangoAboveWindow");
                Assert.AreEqual(Visibility.Collapsed, Field<DangoHatVisual[]>(overlay, "hats")[0].Visibility);
                worker.ApplyLayeringSettings(DesktopLayeringSettings.CreateDefault());
                Assert.AreEqual(Visibility.Collapsed, hat.Visibility);
            }
            CollectionAssert.AreEqual(manifest, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
        });
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    public void ActualWorkerWAttachmentReceivesAccessoriesAcrossAnExclusiveWindowHandoff()
    {
        var configuration = Multiple(); using var runtime = Ready(configuration);
        var scene = new DesktopSceneSession();
        var standard = new DangoSceneWindow(scene, runtime) { Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false };
        var worker = new WorkerWWindow(scene, runtime);
        var standardScheduler = Field<HostFrameScheduler>(standard, "motionScheduler");
        var workerScheduler = Field<HostFrameScheduler>(worker, "motionScheduler");
        HideFixtureOverlays(worker);
        try
        {
            standard.SetVisibility(true); WaitUntil(() => standardScheduler.Snapshot.SuccessfulSteps > 0);
            var id = runtime.EntityIds.Single();
            Assert.AreSame(standardScheduler, runtime.FrameDriver); Assert.IsFalse(workerScheduler.OwnsRuntime);
            AssertAccessories(runtime, (DeformableDangoVisual)standard.FindName("DangoVisual"), id, configuration);
            standard.SetVisibility(false); Assert.IsNull(runtime.FrameDriver);
            worker.SetVisibility(true);
            var until = Environment.TickCount64 + 5000;
            while (!worker.GetSnapshot().IsVisible && worker.GetSnapshot().LastError is null && Environment.TickCount64 < until) Pump(20);
            if (!worker.GetSnapshot().IsVisible)
                Assert.Inconclusive($"Actual WorkerW attachment was not verified in this environment: {worker.GetSnapshot().LastError ?? "attachment timeout"}. Hidden-control frame gates remain separate.");
            WaitUntil(() => workerScheduler.Snapshot.SuccessfulSteps > 0);
            Assert.AreSame(workerScheduler, runtime.FrameDriver); Assert.IsFalse(standardScheduler.OwnsRuntime);
            AssertAccessories(runtime, (DeformableDangoVisual)worker.FindName("DangoVisual"), id, configuration);
            workerScheduler.Stop();
            var physical = runtime.LastPhysicsFrame; var count = runtime.LastFrame!.Bodies[0].Diagnostics.SimulatedStepCount;
            worker.ApplyLayeringSettings(DesktopLayeringSettings.CreateDefault() with { DangoLayer = DesktopObjectLayer.AboveDesktopIcons });
            var overlay = Field<DesktopDangoSceneOverlayWindow>(worker, "dangoAboveWindow");
            Assert.IsTrue(overlay.IsVisible); AssertAccessories(runtime, Field<DeformableDangoVisual[]>(overlay, "bodies")[0], id, configuration);
            worker.ApplyLayeringSettings(DesktopLayeringSettings.CreateDefault());
            AssertAccessories(runtime, (DeformableDangoVisual)worker.FindName("DangoVisual"), id, configuration);
            Assert.AreSame(physical, runtime.LastPhysicsFrame); Assert.AreEqual(count, runtime.LastFrame!.Bodies[0].Diagnostics.SimulatedStepCount);
            worker.SetVisibility(false); Assert.IsNull(runtime.FrameDriver);
            var previous = standardScheduler.Snapshot.SuccessfulSteps;
            standard.SetVisibility(true); WaitUntil(() => standardScheduler.Snapshot.SuccessfulSteps > previous);
            Assert.AreSame(standardScheduler, runtime.FrameDriver); Assert.IsFalse(workerScheduler.OwnsRuntime);
            AssertAccessories(runtime, (DeformableDangoVisual)standard.FindName("DangoVisual"), id, configuration);
        }
        finally { standard.PrepareForExit(); standard.Close(); worker.Close(); scene.Exit(Environment.TickCount64); }
    }

    private static void WithHostFixture(StackedDangoSceneRuntime runtime, bool wallpaper, Action<object> verify)
    {
        var scene = new DesktopSceneSession(); scene.AttachSurface(new(0, 0, 800, 600), 1000);
        object host = wallpaper ? new WorkerWWindow(scene, runtime) : new DangoSceneWindow(scene, runtime);
        var fixture = host as Window ?? new Window { Content = host, Width = 800, Height = 600 };
        fixture.Left = -10000; fixture.Top = -10000; fixture.ShowActivated = false; fixture.ShowInTaskbar = false; fixture.Opacity = 0;
        if (host is WorkerWWindow worker) HideFixtureOverlays(worker);
        try
        {
            fixture.Show(); fixture.UpdateLayout();
            Field<HostFrameScheduler>(host, "motionScheduler").Stop();
            if (wallpaper) typeof(WorkerWWindow).GetField("isVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, true);
            runtime.SetViewport(new(0, 0, 800, 600)); runtime.Start();
            var input = Invoke<PhysicsPresentationInput>(host, "CreateMotionInput");
            for (var step = 0; step < 150; step++) runtime.Tick(input, Step);
            Assert.IsNull(runtime.LastError);
            Invoke<object?>(host, "ApplyPresentationFrame", runtime.LastFrame!);
            verify(host);
        }
        finally
        {
            if (host is DangoSceneWindow standard) standard.PrepareForExit();
            else ((WorkerWWindow)host).Close();
            fixture.Close(); scene.Exit(Environment.TickCount64);
        }
    }

    private static void HideFixtureOverlays(WorkerWWindow worker)
    {
        // These are only this fixture's auxiliary windows. Preserve the user's other windows.
        Field<DesktopDangoSceneOverlayWindow>(worker, "dangoAboveWindow").Opacity = 0;
        Field<DesktopObjectOverlayWindow>(worker, "cushionAboveWindow").Opacity = 0;
        Field<IslandOverlayWindow>(worker, "islandOverlayWindow").Opacity = 0;
    }

    private static void AssertAccessories(StackedDangoSceneRuntime runtime, DeformableDangoVisual body, DangoEntityId id, AccessoryConfiguration configuration)
    {
        Assert.AreEqual(id, runtime.EntityIds.Single()); Assert.AreEqual(id, runtime.AppearanceEntity); Assert.AreEqual(id, body.Tag);
        Assert.IsNull(runtime.LastError); Assert.IsNull(runtime.AccessoryError); Assert.AreEqual(Visibility.Visible, body.Visibility);
        var frame = runtime.LastFrame!; var outputs = runtime.GetAccessoryFrames(frame, 0); Assert.HasCount(3, outputs);
        var parent = (Canvas)body.Parent;
        var layers = parent.Children.OfType<Canvas>().Where(layer => layer.Visibility == Visibility.Visible && layer.Children.OfType<AccessoryMeshVisual>().Any()).ToArray();
        Assert.HasCount(2, layers);
        var visuals = layers.SelectMany(layer => layer.Children.OfType<AccessoryMeshVisual>()).ToArray(); Assert.HasCount(3, visuals);
        foreach (var layer in layers)
        {
            Assert.AreEqual(Canvas.GetLeft(body), Canvas.GetLeft(layer), 1e-8);
            Assert.AreEqual(Canvas.GetTop(body), Canvas.GetTop(layer), 1e-8);
            var side = Canvas.GetZIndex(layer) < Canvas.GetZIndex(body) ? AccessoryLayer.Behind : AccessoryLayer.Front;
            var expected = configuration.Items.Where(item => item.IsEquipped && item.Layer == side).OrderBy(item => item.Order).ToArray();
            var actual = layer.Children.OfType<AccessoryMeshVisual>().OrderBy(Canvas.GetZIndex).ToArray(); Assert.HasCount(expected.Length, actual);
            for (var i = 0; i < expected.Length; i++)
            {
                var output = outputs.Single(candidate => candidate.Item.Id == expected[i].Id); var consumed = actual[i].Frame!;
                Assert.AreEqual(expected[i].Order, Canvas.GetZIndex(actual[i]));
                Assert.AreEqual(output.Mesh.StepIndex, consumed.StepIndex, "A visible host consumed a stale or independently advanced accessory frame.");
                var bounds = DangoHostFrameBinding.Bounds(frame, 0);
                Assert.AreEqual(output.Mesh.ImagePivot.X - bounds.X, consumed.ImagePivot.X, 1e-8);
                Assert.AreEqual(output.Mesh.ImagePivot.Y - bounds.Y, consumed.ImagePivot.Y, 1e-8);
                var raster = actual[i].CaptureBitmap(); Assert.IsNotNull(raster, "The host has a mesh but no rendered accessory texture.");
                var pixels = new int[raster.PixelWidth * raster.PixelHeight]; raster.CopyPixels(pixels, raster.PixelWidth * 4, 0);
                Assert.IsTrue(pixels.Any(pixel => ((uint)pixel >> 24) != 0), "The visible accessory texture contains no nontransparent pixels.");
            }
        }
        Assert.IsTrue(parent.Children.OfType<DangoHatVisual>().All(hat => hat.Visibility != Visibility.Visible));
    }

    private static void Capture(FrameworkElement control, string name)
    {
        if (control.ActualWidth <= 0 || control.ActualHeight <= 0)
        { control.Measure(new Size(control.Width, control.Height)); control.Arrange(new Rect(0, 0, control.Width, control.Height)); }
        control.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(control.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(control.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(control);
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-verification", "screenshots"); Directory.CreateDirectory(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(root, $"{name}-{Guid.NewGuid():N}.png")); encoder.Save(stream);
    }

    private static void WaitForRoom(DangoApplicationHost host, LocalRoomPresentation expected)
    {
        WaitUntil(() => !host.LocalHandoffPending, 4500);
        Assert.AreEqual(expected, host.LocalPresentation, host.LocalRoomFailure); Assert.IsNull(host.LocalRoomFailure);
    }

    private static void WaitUntil(Func<bool> condition, int milliseconds = 2500)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (!condition() && Environment.TickCount64 < until) Pump(20);
        Assert.IsTrue(condition(), "The actual accessory host did not become ready before the bounded wait expired.");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-verification", "integration", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root;
    }

    private static T E<T>(ProductHomeView home, string name) where T : class => (T)home.FindName(name);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static T Invoke<T>(object instance, string name, params object?[] args) => (T)instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args)!;
}
