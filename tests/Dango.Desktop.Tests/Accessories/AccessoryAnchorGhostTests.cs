using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Rendering.Visuals;
using Point = System.Windows.Point;
using Canvas = System.Windows.Controls.Canvas;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Panel = System.Windows.Controls.Panel;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryAnchorGhostTests
{
    [STATestMethod]
    public void ProjectionPreservesPivotAndBothAxesThroughGaitAndNestedViewportTransforms()
    {
        var gait = new TransformGroup(); gait.Children.Add(new ScaleTransform(1.13, .89)); gait.Children.Add(new RotateTransform(7, 80, 66));
        var viewport = new MatrixTransform(1.7, .1, -.05, 1.7, 24, -16);
        var anchor = new Point(46, 52);
        var transform = ProductHomeView.ProjectAccessoryAnchorGhost(anchor, gait, viewport);
        foreach (var point in new[] { new Point(), new Point(-23, 12), new Point(31, -17) })
        {
            var expected = viewport.Transform(gait.Transform(point + (Vector)anchor));
            Assert.AreEqual(expected.X, transform.Transform(point).X, 1e-9);
            Assert.AreEqual(expected.Y, transform.Transform(point).Y, 1e-9);
        }
    }

    [STATestMethod]
    public void OffscreenGhostUsesSelectedImageSizeAngleAndActualBodyMappingWithoutWritingUntilClick()
    {
        var (home, panel, session, presenter) = Fixture();
        using (presenter)
        try
        {
            var saved = session.SavedConfiguration;
            var selectedId = panel.SelectedItem!.Id;
            session.Stage(new(session.DraftConfiguration.Items.Select(item => item.Id == selectedId
                ? item with { SizePercent = 155, BaseAngleDegrees = 37, ImagePivotX = .21, ImagePivotY = .63 } : item)));
            panel.DiscardInputsAndRefresh();
            var original = session.DraftConfiguration;
            var selected = panel.SelectedItem!;
            Assert.IsTrue(home.BeginAccessoryAnchorPick());
            var ghost = home.AccessoryAnchorGhost;
            Assert.IsFalse(ghost.IsHitTestVisible); Assert.AreEqual(.45, ghost.Opacity);
            var mesh = ((AccessoryMeshVisual)ghost.Children[0]).Frame!;
            Assert.AreEqual(selected.BaseAngleDegrees, mesh.RotationDegrees, 1e-8);
            var expected = new AccessoryMotionSolver().Refresh(default, 64 * selected.SizePercent / 100,
                64 * selected.SizePercent / 100, selected);
            CollectionAssert.AreEqual(expected.Vertices.ToArray(), mesh.Vertices.ToArray());

            var uv = new Point(.34, .41);
            var displayed = AccessoryBodyMapper.Map(presenter.Inspect().Body!, presenter.GaitFrame, uv.X, uv.Y);
            var preview = (FrameworkElement)home.FindName("PetPreview");
            var pointer = presenter.View.TranslatePoint(displayed, preview);
            Assert.IsTrue(home.UpdateAccessoryAnchorGhost(pointer));
            Assert.AreEqual(Visibility.Visible, ghost.Visibility);
            var actualPivot = ghost.RenderTransform.Transform(default(Point));
            var expectedPivot = presenter.View.TranslatePoint(displayed, (FrameworkElement)home.FindName("PetVisualArea"));
            Assert.AreEqual(expectedPivot.X, actualPivot.X, 1e-7); Assert.AreEqual(expectedPivot.Y, actualPivot.Y, 1e-7);
            Assert.AreEqual(original, session.DraftConfiguration); Assert.AreEqual(saved, session.SavedConfiguration);
            var target = new RenderTargetBitmap(1200, 800, 96, 96, PixelFormats.Pbgra32); target.Render(home);
            Assert.IsTrue(home.CommitAccessoryAnchorPick(pointer));
            Assert.AreEqual(uv.X, panel.SelectedItem!.BodyAnchorX, 1e-6); Assert.AreEqual(uv.Y, panel.SelectedItem.BodyAnchorY, 1e-6);
            Assert.AreEqual(saved, session.SavedConfiguration); Assert.IsFalse(home.IsPickingAccessoryAnchor);
            Assert.AreEqual(0, ghost.Children.Count); Assert.AreEqual(Visibility.Collapsed, ghost.Visibility);
        }
        finally { home.CancelAccessoryAnchorPick(); }
    }

    [STATestMethod]
    public void InvalidPointerSelectionChangeEscapeAndPageExitDiscardOnlyTheTransientGhost()
    {
        var (home, panel, session, presenter) = Fixture();
        using (presenter)
        try
        {
            var original = session.DraftConfiguration;
            Assert.IsTrue(home.BeginAccessoryAnchorPick());
            Assert.IsFalse(home.UpdateAccessoryAnchorGhost(new(-10, 20)));
            Assert.IsFalse(home.CommitAccessoryAnchorPick(new(double.NaN, 20)));
            Assert.IsTrue(home.IsPickingAccessoryAnchor);
            panel.SelectItem(original.Items.First(item => item.Id != panel.SelectedItem!.Id).Id);
            Assert.IsFalse(home.UpdateAccessoryAnchorGhost(new(80, 60)));
            Assert.IsFalse(home.IsPickingAccessoryAnchor);
            Assert.IsTrue(home.BeginAccessoryAnchorPick());
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, new GhostPresentationSource(), 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            typeof(ProductHomeView).GetMethod("OnPreviewKeyDown", BindingFlags.NonPublic | BindingFlags.Instance,
                null, [typeof(object), typeof(KeyEventArgs)], null)!.Invoke(home, [home, key]);
            Assert.IsTrue(key.Handled); Assert.IsFalse(home.IsPickingAccessoryAnchor);
            Assert.IsTrue(home.IsEditing, "Esc cancels the placement before closing wardrobe.");
            Assert.IsTrue(home.BeginAccessoryAnchorPick());
            home.ShowEditor(HomeEditor.Face);
            Assert.IsFalse(home.IsPickingAccessoryAnchor);
            Assert.AreEqual(0, home.AccessoryAnchorGhost.Children.Count);
            Assert.AreEqual(original, session.DraftConfiguration); Assert.AreEqual(original, session.SavedConfiguration);
        }
        finally { home.CancelAccessoryAnchorPick(); }
    }

    private static (ProductHomeView Home, AccessoryEditorPanel Panel, AccessorySettingsSession Session, BeltBodyAppearancePresenter Presenter) Fixture()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var session = new AccessorySettingsSession(Path.Combine(TestRepository.Root, "artifacts", "anchor-ghost", Guid.NewGuid().ToString("N")));
        home.BindAccessories(session); home.ShowEditor(HomeEditor.Wardrobe);
        var presenter = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        var boundary = Enumerable.Range(0, 128).Select(i => new BeltBodyBoundaryPoint(.7 * Math.Cos(i * Math.Tau / 128), .7 * Math.Sin(i * Math.Tau / 128))).ToArray();
        presenter.Update(new(0, 1d / 60, boundary, 0, 0, 0, 0, true, 0, false, false, false, false));
        typeof(ProductHomeView).GetField("beltPresenter", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(home, presenter);
        ((Canvas)home.FindName("LiveEntityLayer")).Children.Add(presenter.View);
        // No HWND, foreground activation or OS input is involved in this layout/render fixture.
        home.Measure(new(1200, 800)); home.Arrange(new Rect(0, 0, 1200, 800)); home.UpdateLayout();
        return (home, (AccessoryEditorPanel)((Panel)home.FindName("WardrobeOptions")).Children[0], session, presenter);
    }

    private sealed class GhostPresentationSource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
}
