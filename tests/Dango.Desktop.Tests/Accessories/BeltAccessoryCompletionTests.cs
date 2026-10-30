using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Core.Animation;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Rendering.Visuals;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.Accessories;

[TestClass, DoNotParallelize]
public sealed class BeltAccessoryCompletionTests
{
    private static BeltBodyAppearanceFrame Frame(long tick, double x = 0) => new(tick, 1d / 60,
        Enumerable.Range(0, 256).Select(i => new BeltBodyBoundaryPoint(x + .75 * Math.Cos(i * Math.Tau / 256), .65 * Math.Sin(i * Math.Tau / 256))).ToArray(),
        x, 0, 1, 0, true, 1, false, false, false, false, new(1, 100, 100, 600, 600));
    private static string Root()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "belt-accessory-completion", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); return root;
    }
    private static AccessoryItem Item(string id = "fictional") => new(id, "Fictional accessory", "builtin-sprout", 64, 64);

    [STATestMethod]
    public void DraftHotUpdatePreservesBodyEyesGaitAndStableInstanceMotion()
    {
        using var presenter = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default) { GaitEnabled = true };
        var item = Item() with { DeformationMode = AccessoryDeformationMode.SingleChain,
            Rig = new([new(.5, .5), new(.7, .8)], .8), GravityInfluence = 100, ConnectionStrength = 40 };
        presenter.SetAccessories(new([item]), _ => throw new AssertFailedException("Built-ins do not load from disk."));
        for (var tick = 0; tick < 150; tick++) presenter.Update(Frame(tick, tick < 60 ? 0 : (tick - 60) / 100d) with { Horizontal = tick < 60 ? 0 : 1 });
        Assert.IsTrue(presenter.SubmitAttachment(new SetEyes(EyeSelection.Both, 0, EyeShape.Sleeping, 1)));
        presenter.Update(Frame(150, 1.5));
        var before = presenter.Inspect(); var gait = presenter.GaitFrame;
        var visual = AccessoryVisuals(presenter).Single();
        var motion = before.Accessories.Single().Frame;
        var replacement = item with { BaseAngleDegrees = 35, Layer = AccessoryLayer.Behind };
        presenter.SetAccessories(new([replacement]), _ => null);
        var after = presenter.Inspect();
        Assert.AreSame(before.Body, after.Body);
        Assert.AreSame(before.Shape, after.Shape);
        Assert.AreSame(gait, presenter.GaitFrame);
        Assert.AreSame(visual, AccessoryVisuals(presenter).Single());
        Assert.AreEqual(before.Tick, after.Tick);
        Assert.AreEqual(motion.StepIndex, after.Accessories.Single().Frame.StepIndex);
        Assert.AreEqual(default(Vector), after.Accessories.Single().Frame.ConnectionOffset,
            "A changed configuration projects its rest state until the next owner step rebases it.");
        Assert.AreEqual(AccessoryLayer.Behind, after.Accessories.Single().Layer);
        Assert.AreNotEqual(motion.Vertices[0], after.Accessories.Single().Frame.Vertices[0]);
        presenter.Update(Frame(151, 1.51));
        Assert.AreEqual(151L, presenter.Inspect().Accessories.Single().Frame.StepIndex);
    }

    [STATestMethod]
    public void MissingImagesAreIsolatedAndLayerOrderAndEmptyDraftAreReal()
    {
        using var presenter = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        presenter.Update(Frame(0));
        var bad = Item("bad") with { AssetId = new string('a', 64) };
        var back = Item("back") with { Layer = AccessoryLayer.Behind };
        var first = Item("first") with { Order = 1, SizePercent = 120 };
        var last = Item("last") with { Order = 3, SizePercent = 200 };
        var reads = 0;
        presenter.SetAccessories(new([last, bad, first, back, bad with { Id = "bad-copy" }]), _ =>
        { reads++; throw new IOException("fictional file unavailable"); });
        Assert.AreEqual(1, reads, "A shared broken image is read at most once per refresh.");
        Assert.AreEqual(5, presenter.EquippedCount);
        Assert.HasCount(3, presenter.Inspect().Accessories);
        Assert.IsNotNull(presenter.AccessoryError);
        Assert.IsNull(presenter.RuntimeError, "A missing accessory must not latch a body fault.");
        var layers = (Canvas)presenter.View.Children[0];
        Assert.HasCount(1, ((Canvas)layers.Children[0]).Children);
        var fronts = ((Canvas)layers.Children[4]).Children.OfType<AccessoryMeshVisual>().ToArray();
        Assert.HasCount(2, fronts);
        Assert.AreSame(presenter.Inspect().Accessories[1].Frame, fronts[0].Frame);
        Assert.AreSame(presenter.Inspect().Accessories[2].Frame, fronts[1].Frame);
        presenter.SetAccessories(AccessoryConfiguration.Empty, _ => null);
        Assert.AreEqual(0, presenter.RenderedAccessoryCount);
        Assert.AreEqual(0, presenter.EquippedCount);
        Assert.IsNull(presenter.AccessoryError);
        Assert.HasCount(0, presenter.Inspect().Accessories);
    }

    [STATestMethod]
    public void PickingInvertsCurrentGaitOnActualContinuousBoundary()
    {
        using var presenter = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default) { GaitEnabled = true };
        Assert.IsFalse(presenter.TryPickBodyAnchor(new(80, 66), out _));
        var gaitFrames = 0;
        for (var tick = 0; tick < 180; tick++)
        {
            presenter.Update(Frame(tick, tick < 60 ? 0 : (tick - 60) / 100d) with { Horizontal = tick < 60 ? 0 : 1 });
            if (presenter.GaitFrame is not null) gaitFrames++;
            foreach (var expected in new[] { new Point(.21, .27), new Point(.73, .62) })
            {
                var displayed = AccessoryBodyMapper.Map(presenter.Inspect().Body!, presenter.GaitFrame, expected.X, expected.Y);
                Assert.IsTrue(presenter.TryPickBodyAnchor(displayed, out var actual));
                Assert.AreEqual(expected.X, actual.X, 1e-8);
                Assert.AreEqual(expected.Y, actual.Y, 1e-8);
            }
        }
        Assert.IsGreaterThan(0, gaitFrames, "The inverse gate must exercise nonidentity gait.");
        Assert.IsFalse(presenter.TryPickBodyAnchor(new(double.NaN, 0), out _));
    }

    [STATestMethod]
    public void HiddenHomeUsesPendingTextureAndExplicitApplyThenRestoresOnCloseLeaveHide()
    {
        var root = Root(); var session = new AccessorySettingsSession(root);
        using var presenter = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        presenter.Update(Frame(0));
        var home = new ProductHomeView { ReduceMotion = true, Width = 1024, Height = 680 };
        typeof(ProductHomeView).GetField("beltPresenter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(home, presenter);
        home.BindAccessories(session);
        using var source = new HwndSource(new HwndSourceParameters("Fictional belt accessory background test")
            { Width = 1024, Height = 680, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000000 });
        source.RootVisual = home;
        home.Measure(new(1024, 680)); home.Arrange(new Rect(0, 0, 1024, 680)); home.UpdateLayout();
        var panel = (AccessoryEditorPanel)typeof(ProductHomeView).GetField("accessoryPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home)!;
        var saved = session.SavedConfiguration;
        home.ShowEditor(HomeEditor.Wardrobe);
        panel.SetBodyAnchor(new(.23, .31));
        Assert.AreEqual(.23, session.DraftConfiguration.Items.Single(i => i.IsEquipped).BodyAnchorX);
        Assert.AreNotEqual(AccessoryBodyMapper.Map(presenter.Inspect().Body!, null, .5, 0), presenter.Inspect().Accessories.Single().RawBodyAnchor);
        Assert.AreEqual(saved, new AccessorySettingsSession(root).SavedConfiguration);
        home.CloseEditor();
        Assert.AreEqual(saved, session.DraftConfiguration);
        Assert.AreEqual(AccessoryBodyMapper.Map(presenter.Inspect().Body!, null, .5, 0), presenter.Inspect().Accessories.Single().RawBodyAnchor);
        home.ShowEditor(HomeEditor.Wardrobe);
        var imported = session.ImportPng(WritePng(root));
        home.SynchronizeBeltAccessories();
        Assert.AreEqual(2, presenter.RenderedAccessoryCount, "The uncommitted imported image must be visible.");
        Assert.AreEqual(1, session.PendingImageCount);
        Assert.IsFalse(File.Exists(Path.Combine(root, "accessories-v2.json")));
        var apply = Descendants(panel.SaveActions).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "应用");
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsFalse(session.HasPendingSave);
        Assert.AreEqual(0, session.PendingImageCount);
        var reloaded = new AccessorySettingsSession(root);
        Assert.AreEqual(session.SavedConfiguration, reloaded.SavedConfiguration);
        Assert.IsNotNull(reloaded.TryGetImage(imported.AssetId));
        panel.SetBodyAnchor(new(.34, .56));
        home.ShowEditor(HomeEditor.Face);
        Assert.AreEqual(session.SavedConfiguration, session.DraftConfiguration);
        home.ShowEditor(HomeEditor.Wardrobe);
        panel.SetBodyAnchor(new(.12, .34));
        home.Visibility = Visibility.Hidden;
        Assert.AreEqual(session.SavedConfiguration, session.DraftConfiguration);
        Assert.AreEqual(2, presenter.RenderedAccessoryCount);
        Assert.IsNull(home.BoundRuntime);
        Assert.IsNull(home.BoundBeltBody, "This component gate must not create any physics world.");
        home.Visibility = Visibility.Visible;
        home.CloseEditor();
        home.ShowEditor(HomeEditor.Wardrobe);
        session.ImportPng(WritePng(root)); home.SynchronizeBeltAccessories();
        Assert.AreEqual(3, presenter.RenderedAccessoryCount);
        Assert.AreEqual(1, session.PendingImageCount);
        home.CloseEditor();
        Assert.AreEqual(0, session.PendingImageCount);
        Assert.AreEqual(2, presenter.RenderedAccessoryCount);
        Assert.AreEqual(reloaded.SavedConfiguration, new AccessorySettingsSession(root).SavedConfiguration);
        source.RootVisual = null;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static AccessoryMeshVisual[] AccessoryVisuals(BeltBodyAppearancePresenter presenter)
    {
        var layer = (Canvas)presenter.View.Children[0];
        return layer.Children.OfType<Canvas>().SelectMany(canvas => canvas.Children.OfType<AccessoryMeshVisual>()).ToArray();
    }
    private static string WritePng(string root)
    {
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 0, 0, 20, 80, 120, 255, 30, 90, 130, 128, 0, 0, 0, 0 }, 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(root, "fictional.png"); using var stream = File.Create(path); encoder.Save(stream); return path;
    }
}
