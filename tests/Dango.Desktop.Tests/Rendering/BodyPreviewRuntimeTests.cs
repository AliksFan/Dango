using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using System.Windows.Media;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class BodyPreviewRuntimeTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    private static string Module => Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll");

    private static (StackedDangoSceneRuntime Runtime, PhysicsPresentationInput Input) Start(DangoBodyControls? saved = null)
    {
        Assert.IsTrue(File.Exists(Module), "Build with -p:DangoPhysicsModulePath set to the body-material module.");
        var runtime = StackedDangoSceneRuntime.CreateProduct(Module);
        if (saved is not null) runtime.SetAppearance(DangoAppearanceConfiguration.Default.WithBody(saved));
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var desktop = new DesktopSceneSession();
        desktop.AttachSurface(new(0, 0, 1600, 900), 0);
        var controller = desktop.Controller!;
        controller.MoveTo(new(800, 400));
        var input = PhysicsPresentationInput.FromSnapshot(controller.Snapshot, controller.SquashAmount,
            controller.GroundFrictionCue, controller.FacingDirection, null);
        runtime.SetViewport(new(0, 0, 1600, 900));
        runtime.Start();
        return (runtime, input);
    }

    private static double Width(StackedDangoSceneRuntime runtime)
    {
        var body = runtime.LastPhysicsFrame!.Bodies[0];
        var xs = Enumerable.Range(0, body.PointCount).Select(i => (double)body.Contour[2 * i]).ToArray();
        return xs.Max() - xs.Min();
    }

    [STATestMethod, TestCategory("NativeIntegration")]
    public void SavedBodyIsUsedFromTheFirstWorldAndCalibration()
    {
        var (runtime, input) = Start(new(120, 50, 50));
        using (runtime)
        {
            for (var i = 0; i < 120; i++) runtime.Tick(input, Dt);
            Assert.AreEqual(new DangoBodyControls(120, 50, 50), runtime.PhysicsBody);
            Assert.AreEqual(.045 * 1.2, runtime.NodeRadiusOf(runtime.EntityIds[0]), 1e-12);
        }
    }

    [STATestMethod, TestCategory("NativeIntegration")]
    public void BodyPreviewRebuildsTheWorldAndKeepsOtherDangos()
    {
        var (runtime, input) = Start();
        using (runtime)
        {
            for (var i = 0; i < 240; i++) runtime.Tick(input, Dt);
            var defaultWidth = Width(runtime);
            Assert.IsTrue(runtime.AddDango());
            var calibrated = runtime.CalibrationStepCount;

            Assert.IsTrue(runtime.TryApplyBody(new(120, 100, 0)), runtime.BodyPreviewError);
            Assert.AreEqual(new DangoBodyControls(120, 100, 0), runtime.PhysicsBody);
            Assert.AreEqual(2, runtime.DangoCount);
            Assert.AreEqual(.045 * 1.2, runtime.NodeRadiusOf(runtime.EntityIds[0]), 1e-12);
            Assert.AreEqual(.045, runtime.NodeRadiusOf(runtime.EntityIds[1]), 1e-12, "Other Dangos keep the default body.");
            Assert.AreEqual(240, runtime.CalibrationStepCount, "Attachments are recalibrated for the new body.");
            Assert.AreEqual(240, calibrated);
            Assert.IsNull(runtime.LastFrame, "No frame from the old world is presented as the new body.");

            for (var i = 0; i < 240; i++) runtime.Tick(input, Dt);
            Assert.IsNull(runtime.LastError);
            Assert.IsTrue(Width(runtime) > defaultWidth * 1.2, $"Large fat body must land wider: {Width(runtime)} vs {defaultWidth}");

            Assert.IsTrue(runtime.TryApplyBody(DangoBodyControls.Default), runtime.BodyPreviewError);
            for (var i = 0; i < 240; i++) runtime.Tick(input, Dt);
            Assert.AreEqual(defaultWidth, Width(runtime), defaultWidth * .02);
        }
    }

    [STATestMethod, TestCategory("NativeIntegration")]
    public void BodyPreviewIsRefusedBeforeInitializationWithoutChangingTheBody()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct(Module);
        Assert.IsFalse(runtime.TryApplyBody(new(90, 50, 50)));
        Assert.IsNotNull(runtime.BodyPreviewError);
        Assert.AreEqual(DangoBodyControls.Default, runtime.PhysicsBody);
    }

    [STATestMethod, TestCategory("NativeIntegration")]
    [DataRow(80)]
    [DataRow(100)]
    [DataRow(120)]
    public void BodySizeReachesTheActualHostWithoutFittingTheContourIntoTheDefaultBox(int size)
    {
        var (runtime, input) = Start(new(size, 50, 50));
        using (runtime)
        {
            runtime.GaitEnabled = false;
            for (var i = 0; i < 120; i++) runtime.Tick(input, Dt);
            Assert.IsNull(runtime.LastError);
            var frame = runtime.LastFrame!;
            var bounds = DangoHostFrameBinding.Bounds(frame, 0);
            var scale = size / 100d;
            Assert.AreEqual(160 * scale, bounds.Width, 1e-10);
            Assert.AreEqual(120 * scale, bounds.Height, 1e-10);
            var visual = new DeformableDangoVisual();
            var hat = new DangoHatVisual();
            DangoHostFrameBinding.Apply(visual, hat, frame, 0, runtime: runtime);
            Assert.AreEqual(DangoContourFailure.None, visual.LastContourFailure);
            var displayed = visual.RenderTransform.TransformBounds(visual.BodyPath.Data.Bounds);
            var native = runtime.LastPhysicsFrame!.Bodies[0];
            var raw = Enumerable.Range(0, native.PointCount).Select(i =>
                new PresentationPoint(native.Contour[2 * i], native.Contour[2 * i + 1])).ToArray();
            var expected = DangoStreamGeometryFactory.TryCreate(raw, 160, 120);
            Assert.IsTrue(expected.Succeeded);
            Assert.AreEqual(expected.Geometry!.Bounds.Width, displayed.Width, 1e-5);
            Assert.AreEqual(expected.Geometry.Bounds.Height, displayed.Height, 1e-5);
            Assert.AreEqual(frame.EntityIds[0], DangoHostFrameBinding.HitTest(frame,
                new(bounds.Right - .01, bounds.Bottom - .01)));
            Assert.IsNull(DangoHostFrameBinding.HitTest(frame, new(bounds.Right + .01, bounds.Bottom + .01)));
        }
    }
}
