using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Desktop.Accessories;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Diagnostics;
using Dango.Desktop.Rendering.Visuals;
using Dango.Core.Coordinates;
using Point = System.Windows.Point;
using VisualPath = System.Windows.Shapes.Path;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class PhysicsLabGaitTests
{
    private const double Dt = 1d / 120;
    private static readonly PhysicsLabGaitContext Context = new(120d / 72, 200, 200, 1000, 800);

    [STATestMethod]
    public void RealTranslationProducesBothPhasesAndOneSharedBottomAnchoredBoundedTransform()
    {
        using var fixture = new Fixture();
        fixture.SaveAccessories();
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings) { GaitEnabled = true };
        Warm(presenter);
        var compressed = false; var stretched = false;
        for (var tick = 80; tick < 400; tick++)
        {
            var frame = Frame(tick, (tick - 79) * .5 * Dt, 1);
            presenter.Update(frame);
            Assert.IsNull(presenter.GaitState.Error);
            var gait = presenter.GaitFrame;
            if (gait is null) continue;
            compressed |= gait.ScaleY < 1 - 1e-7;
            stretched |= gait.ScaleY > 1 + 1e-7;
            Assert.AreEqual(tick, gait.StepIndex);
            Assert.AreEqual(tick, presenter.CurrentAttachments!.StepIndex);
            Assert.IsTrue(gait.Matches(Body(presenter), gait.EntityId));
            var layer = Layer(presenter);
            Assert.AreEqual(5, layer.Children.Count);
            Assert.AreEqual(1, ((Canvas)presenter.View).Children.Count);
            var transform = layer.RenderTransform;
            var shape = presenter.CurrentAttachments.SourceShape!;
            var bottom = 66 - shape.Bottom * 160;
            var anchor = transform.Transform(new Point(gait.CenterX, bottom));
            Assert.AreEqual(bottom, anchor.Y, 1e-10);
            Assert.AreEqual(bottom, gait.BottomY, 1e-10);
            var points = shape.Boundary.Select(p => new Point(80 + p.X * 160, 66 - p.Y * 160))
                .Concat(new[] { presenter.CurrentAttachments.Face.Left, presenter.CurrentAttachments.Face.Right }
                    .Where(eye => eye.Visible).SelectMany(EyeOutline.Create)
                    .Select(p => new Point(80 + p.X * 160, 66 - p.Y * 160)))
                .Concat(presenter.AccessoryFrames.SelectMany(mesh => mesh.Vertices));
            foreach (var point in points)
            {
                var mapped = transform.Transform(point);
                Assert.AreEqual(gait.Map(new(point.X, point.Y)).X, mapped.X, 1e-10);
                Assert.AreEqual(gait.Map(new(point.X, point.Y)).Y, mapped.Y, 1e-10);
                Assert.IsTrue((mapped - point).Length * Context.DisplayScale <= 1.99 + 1e-10);
                Assert.IsTrue(mapped.X * Context.DisplayScale is >= 0 and <= 160 * (120d / 72));
                Assert.IsTrue(mapped.Y * Context.DisplayScale is >= -16 and <= 120 * (120d / 72));
                Assert.IsTrue(mapped.X * Context.DisplayScale + Context.Left <= Context.ViewportWidth);
                Assert.IsTrue(mapped.Y * Context.DisplayScale + Context.Top <= Context.ViewportHeight);
            }
            foreach (UIElement child in layer.Children) Assert.IsTrue(child.RenderTransform.Value.IsIdentity);
            Assert.AreEqual(2, presenter.RenderedAccessoryCount);
            Assert.AreEqual(2, presenter.AccessoryFrames.Count);
        }
        Assert.IsTrue(compressed, "An identity or always-stretch implementation must fail.");
        Assert.IsTrue(stretched, "A one-sided or suppressed implementation must fail.");
    }

    [STATestMethod]
    public void DuplicateTicksAndRedrawDoNotAdvanceEyesAccessoriesOrGait()
    {
        using var fixture = new Fixture(); fixture.SaveAccessories();
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings) { GaitEnabled = true };
        Warm(presenter);
        for (var tick = 80; tick < 130; tick++) presenter.Update(Frame(tick, (tick - 79) * .5 * Dt, 1));
        var state = presenter.GaitState; var gait = presenter.GaitFrame;
        var eyes = presenter.CurrentAttachments; var mesh = presenter.AccessoryFrames[0];
        presenter.Update(Frame(129, 200, -1));
        presenter.View.InvalidateVisual(); presenter.View.UpdateLayout();
        Assert.AreSame(state, presenter.GaitState); Assert.AreSame(gait, presenter.GaitFrame);
        Assert.AreSame(eyes, presenter.CurrentAttachments); Assert.AreSame(mesh, presenter.AccessoryFrames[0]);
    }

    [STATestMethod]
    public void HeldInputWithoutDisplacementCannotStartPhase()
    {
        using var fixture = new Fixture();
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings) { GaitEnabled = true };
        Warm(presenter);
        for (var tick = 80; tick < 160; tick++) presenter.Update(Frame(tick, 0, 1));
        Assert.AreEqual(0d, presenter.GaitState.Phase);
        Assert.AreEqual(0d, presenter.GaitState.DisplayedStrain);
    }

    [STATestMethod]
    [DataRow("down")]
    [DataRow("stretch")]
    [DataRow("jump")]
    [DataRow("drag")]
    [DataRow("airborne")]
    public void UnsafeMotionClearsDisplayAndRecoveryDoesNotCatchHiddenDistance(string input)
    {
        using var fixture = new Fixture();
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings) { GaitEnabled = true };
        Warm(presenter);
        for (var tick = 80; tick < 130; tick++) presenter.Update(Frame(tick, (tick - 79) * .5 * Dt, 1));
        Assert.IsNotNull(presenter.GaitFrame);
        var interrupted = Frame(130, 10, 1) with
        {
            Down = input == "down", Stretch = input == "stretch", Jump = input == "jump",
            Drag = input == "drag", Grounded = input != "airborne",
        };
        presenter.Update(interrupted);
        Assert.IsNull(presenter.GaitFrame); Assert.AreEqual(0d, presenter.GaitState.DisplayedStrain);
        var phase = presenter.GaitState.Phase;
        presenter.Update(Frame(131, 100, 1));
        Assert.AreEqual(phase, presenter.GaitState.Phase);
        Assert.IsNull(presenter.GaitFrame);
        for (var tick = 132; tick < 200; tick++) presenter.Update(Frame(tick, 100 + (tick - 131) * .5 * Dt, 1));
        Assert.IsNotNull(presenter.GaitFrame, "Recovery must re-establish support without requiring reset.");
    }

    [STATestMethod]
    public void PauseDisableResetAndSamplingInvalidationCannotCatchHiddenDistance()
    {
        using var fixture = new Fixture();
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings) { GaitEnabled = true };
        Warm(presenter);
        for (var tick = 80; tick < 130; tick++) presenter.Update(Frame(tick, (tick - 79) * .5 * Dt, 1));
        Assert.IsNotNull(presenter.GaitFrame);
        var phase = presenter.GaitState.Phase;
        presenter.InvalidateGaitSampling(); presenter.Update(Frame(130, 100, 1));
        Assert.AreEqual(phase, presenter.GaitState.Phase);
        presenter.Paused = true;
        Assert.IsNull(presenter.GaitFrame); Assert.IsTrue(Layer(presenter).RenderTransform.Value.IsIdentity);
        presenter.Update(Frame(131, 200, 1)); Assert.AreEqual(130L, presenter.GaitState.Tick);
        presenter.Paused = false; presenter.Update(Frame(131, 200, 1));
        Assert.AreEqual(0d, presenter.GaitState.Phase);
        presenter.GaitEnabled = false;
        Assert.IsNull(presenter.GaitFrame); Assert.IsTrue(Layer(presenter).RenderTransform.Value.IsIdentity);
        presenter.Update(Frame(132, 300, 1));
        presenter.GaitEnabled = true; presenter.Update(Frame(133, 400, 1));
        Assert.AreEqual(0d, presenter.GaitState.Phase);
        presenter.Reset(); Assert.IsNull(presenter.GaitFrame); Assert.AreEqual(-1L, presenter.GaitState.Tick);
        presenter.Update(Frame(0, 500, 1)); Assert.AreEqual(0d, presenter.GaitState.Phase);
    }

    [STATestMethod]
    public void InvalidGaitContextFailsOnlyGaitAndRetainsBodyEyesAndAccessories()
    {
        using var fixture = new Fixture(); fixture.SaveAccessories();
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings) { GaitEnabled = true };
        presenter.Update(Frame(0) with { GaitContext = Context with { DisplayScale = double.NaN } });
        Assert.AreEqual("InvalidGaitFrame", presenter.GaitState.Error);
        Assert.IsNull(presenter.GaitFrame); Assert.IsNotNull(presenter.CurrentAttachments);
        Assert.AreEqual(2, presenter.RenderedAccessoryCount);
        Assert.IsTrue(((VisualPath)Layer(presenter).Children[1]).Data.GetArea() > 0);
        Assert.IsFalse(presenter.Status.Contains("失败", StringComparison.Ordinal));
        presenter.Update(Frame(1)); Assert.AreEqual(1L, presenter.CurrentAttachments.StepIndex);
    }

    [STATestMethod]
    public void LaboratoryFifteenPercentCompressionGateRemainsVisibleAndSuppressesGait()
    {
        using var fixture = new Fixture();
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings) { GaitEnabled = true };
        Warm(presenter);
        var frame = Frame(80, .5 * Dt, 1);
        frame = frame with { Boundary = frame.Boundary.Select(p => p with { Y = -.7 + (p.Y + .7) * .84 }).ToArray() };
        presenter.Update(frame);
        Assert.AreEqual("LabShapeGate15Percent", presenter.GaitState.SuppressionReason);
        Assert.AreEqual(0d, presenter.GaitState.RequestedStrain); Assert.IsNull(presenter.GaitFrame);
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void GaitToggleKeepsIdenticalRawEyesAndActualMeshes(bool enabled)
    {
        using var fixture = new Fixture(); fixture.SaveAccessories();
        using var baseline = new PhysicsLabAppearancePresenter(fixture.Settings);
        using var disabled = new PhysicsLabAppearancePresenter(fixture.Settings) { GaitEnabled = true };
        disabled.GaitEnabled = enabled;
        Warm(baseline); Warm(disabled);
        for (var tick = 80; tick < 110; tick++)
        {
            var frame = Frame(tick, (tick - 79) * .5 * Dt, 1);
            baseline.Update(frame); disabled.Update(frame);
            Assert.AreEqual(baseline.CurrentAttachments!.Face, disabled.CurrentAttachments!.Face);
            Assert.IsNotNull(baseline.CurrentAttachments.Hat?.Pose);
            for (var i = 0; i < 2; i++)
                CollectionAssert.AreEqual(baseline.AccessoryFrames[i].Vertices.ToArray(), disabled.AccessoryFrames[i].Vertices.ToArray());
            if (!enabled) Assert.IsTrue(Layer(disabled).RenderTransform.Value.IsIdentity);
        }
        if (enabled) Assert.IsNotNull(disabled.GaitFrame);
    }

    [STATestMethod]
    public void ActualOutOfViewportAccessorySuppressesGaitEvenWhenBodyAndEyesAreSafe()
    {
        using var fixture = new Fixture();
        new AccessorySettingsStore().Save(fixture.Root, new([
            new("oversize", "Fictional high crown", "builtin-top-hat", 64, 64),
        ]));
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings) { GaitEnabled = true };
        for (var tick = 0; tick < 80; tick++) presenter.Update(Frame(tick) with { GaitContext = Context with { Top = 20 } });
        var observedRequest = false;
        for (var tick = 80; tick < 110; tick++)
        {
            presenter.Update(Frame(tick, (tick - 79) * .5 * Dt, 1) with { GaitContext = Context with { Top = 20 } });
            observedRequest |= presenter.GaitState.RequestedStrain != 0;
            Assert.IsNull(presenter.GaitFrame);
            Assert.IsNull(presenter.GaitState.Error);
            Assert.AreEqual(1, presenter.RenderedAccessoryCount);
        }
        Assert.IsTrue(observedRequest);
        Assert.AreEqual("OriginalGeometryOrMeshSafety", presenter.GaitState.SuppressionReason);
    }

    [STATestMethod]
    public void VisibleAccessoryBeyondLegacyOverlayStillSharesBothBoundedGaitPhases()
    {
        using var fixture = new Fixture();
        new AccessorySettingsStore().Save(fixture.Root, new([
            new("crown", "Fictional high crown", "builtin-top-hat", 64, 64),
        ]));
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings) { GaitEnabled = true };
        Warm(presenter);
        var compressed = false; var stretched = false; var outsideLegacyOverlay = false;
        for (var tick = 80; tick < 400; tick++)
        {
            presenter.Update(Frame(tick, (tick - 79) * .5 * Dt, 1));
            outsideLegacyOverlay |= presenter.AccessoryFrames.SelectMany(mesh => mesh.Vertices)
                .Any(point => point.Y * Context.DisplayScale < -16);
            if (presenter.GaitFrame is not { } gait) continue;
            compressed |= gait.ScaleY < 1 - 1e-7; stretched |= gait.ScaleY > 1 + 1e-7;
            Assert.IsTrue(presenter.GaitState.MaximumDisplacementDip <= 1.99 + 1e-10);
            foreach (var point in presenter.AccessoryFrames.SelectMany(mesh => mesh.Vertices))
            {
                var mapped = gait.Map(new(point.X, point.Y));
                Assert.IsTrue(mapped.Y * Context.DisplayScale + Context.Top >= 0);
                Assert.IsTrue(mapped.Y * Context.DisplayScale + Context.Top <= Context.ViewportHeight);
            }
        }
        Assert.IsTrue(outsideLegacyOverlay, "The regression must exercise real unclipped mesh overflow.");
        Assert.IsTrue(compressed); Assert.IsTrue(stretched);
    }

    [STATestMethod]
    [DataRow(-.6)]
    [DataRow(.6)]
    public void BodyShiftedAroundCoreUsesActualUnclippedViewportWhileProductTemplateGateRemains(double offset)
    {
        using var fixture = new Fixture(); fixture.SaveAccessories();
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings) { GaitEnabled = true };
        PhysicsLabAppearanceFrame Shifted(long tick, double x = 0, double direction = 0)
        {
            var frame = Frame(tick, x, direction);
            return frame with { Boundary = frame.Boundary.Select(p => p with { X = p.X + offset }).ToArray() };
        }
        for (var tick = 0; tick < 80; tick++) presenter.Update(Shifted(tick));
        var compressed = false; var stretched = false; var outsideTemplate = false;
        for (var tick = 80; tick < 400; tick++)
        {
            presenter.Update(Shifted(tick, (tick - 79) * .5 * Dt, 1));
            var points = presenter.CurrentAttachments!.SourceShape!.Boundary.Select(GaitGeometry.Canvas).ToArray();
            outsideTemplate |= points.Any(p => p.X < 0 || p.X > 160);
            if (presenter.GaitFrame is not { } gait) continue;
            compressed |= gait.ScaleY < 1 - 1e-7; stretched |= gait.ScaleY > 1 + 1e-7;
            Assert.IsNull(GaitGeometry.Create(Body(presenter), gait.EntityId,
                new(Context.Left, Context.Top, 160 * Context.DisplayScale, 120 * Context.DisplayScale),
                new(0, 0, Context.ViewportWidth, Context.ViewportHeight), -.01, false, 1),
                "The ordinary host must still enforce its original template boundary.");
            Assert.IsTrue(presenter.GaitState.MaximumDisplacementDip <= 1.99 + 1e-10);
            foreach (var point in points)
            {
                var mapped = gait.Map(point);
                Assert.IsTrue(mapped.X * Context.DisplayScale + Context.Left is >= 0 and <= 1000);
                Assert.IsTrue(mapped.Y * Context.DisplayScale + Context.Top is >= 0 and <= 800);
            }
        }
        Assert.IsTrue(outsideTemplate); Assert.IsTrue(compressed); Assert.IsTrue(stretched);
    }

    [STATestMethod]
    [DataRow(-.6, 0d)]
    [DataRow(.6, 750d)]
    public void UnclippedBodyStillCannotEscapeActualViewport(double offset, double left)
    {
        using var fixture = new Fixture();
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings) { GaitEnabled = true };
        var requested = false;
        for (var tick = 0; tick < 200; tick++)
        {
            var frame = Frame(tick, Math.Max(0, tick - 79) * .5 * Dt, tick < 80 ? 0 : 1);
            presenter.Update(frame with
            {
                Boundary = frame.Boundary.Select(p => p with { X = p.X + offset }).ToArray(),
                GaitContext = Context with { Left = left },
            });
            requested |= presenter.GaitState.RequestedStrain != 0;
            Assert.IsNull(presenter.GaitFrame);
        }
        Assert.IsTrue(requested);
        Assert.AreEqual("OriginalGeometryOrMeshSafety", presenter.GaitState.SuppressionReason);
    }

    private static Canvas Layer(PhysicsLabAppearancePresenter presenter) => (Canvas)((Canvas)presenter.View).Children[0];
    private static Dango.Desktop.Physics.Presentation.PhysicsPresentationFrame Body(PhysicsLabAppearancePresenter presenter) =>
        new(true, [], new(new(0, 0), 0), new(new(0, 0), 0),
            Dango.Desktop.Physics.Presentation.PhysicsPresentationFallbackReason.None,
            new(Dango.Desktop.Physics.Presentation.PhysicsPresentationBackend.Box2D, 1, Dango.Desktop.Physics.Presentation.PhysicsPresentationDiagnosticCode.SoftBodyActive))
        { Attachments = presenter.CurrentAttachments };
    private static void Warm(PhysicsLabAppearancePresenter presenter)
    { for (var tick = 0; tick < 80; tick++) presenter.Update(Frame(tick)); }
    private static PhysicsLabAppearanceFrame Frame(long tick, double x = 0, double direction = 0) => new(tick, Dt,
        Enumerable.Range(0, 32).Select(index => new PhysicsLabBoundaryPoint(
            x + .7 * Math.Cos(index * 2 * Math.PI / 32), .7 * Math.Sin(index * 2 * Math.PI / 32))).ToArray(),
        x, 0, direction * .5, 0, true, direction, false, false, false, false, Context);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Dango-Lab-Gait-" + Guid.NewGuid().ToString("N"));
        public string Settings => Path.Combine(Root, "settings.json");
        public Fixture() => Directory.CreateDirectory(Root);
        public void SaveAccessories() => new AccessorySettingsStore().Save(Root, new([
            new("back", "Fictional rear", "builtin-top-hat", 64, 64) { Layer = AccessoryLayer.Behind, BodyAnchorY = .4, SizePercent = 70 },
            new("front", "Fictional front", "builtin-sprout", 64, 64) { Layer = AccessoryLayer.Front, BodyAnchorY = .65, SizePercent = 70 },
        ]));
        public void Dispose() => Directory.Delete(Root, true);
    }
}
