using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.AppShell;
using Dango.Desktop.Physics.Belt;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessorySimulationViewTests
{
    [STATestMethod]
    public void ConstructionAndInactiveLayoutDoNotCreateANativeOwner()
    {
        var creates = 0;
        var composition = new ProductPhysicsComposition(() => { creates++; throw new InvalidOperationException("Owner creation is unexpected."); },
            () => throw new InvalidOperationException("A room must never be created."));
        using var view = new AccessorySimulationView(DangoAppearanceConfiguration.Default, composition);
        view.SetActive(true);
        Assert.AreEqual(0, creates, "A zero-sized surface cannot start its native owner.");
        view.SetActive(false);
        view.Measure(new Size(640, 480)); view.Arrange(new Rect(0, 0, 640, 480)); view.UpdateLayout();
        view.SetContent(Result("synthetic-lazy", true));
        view.ClearInput(); view.SetTool("pick"); view.CancelPick();
        Assert.AreEqual(0, creates); Assert.IsNull(view.Runtime);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void BlankFirstPreviewPresentsRealBodyAndAddsArtworkWithoutReplacingItsPoseOrCamera()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll")))
            Assert.Inconclusive("The real product belt module is required.");
        using var view = new AccessorySimulationView(DangoAppearanceConfiguration.Default);
        view.Measure(new Size(640, 480)); view.Arrange(new Rect(0, 0, 640, 480)); view.UpdateLayout();
        view.SetActive(true);
        Wait(() => view.Snapshot is not null || view.Error is not null);
        Assert.IsNull(view.Error); Assert.IsNotNull(view.Snapshot?.Frame);
        Assert.AreEqual(0, view.Presenter.EquippedCount); Assert.AreEqual(0, view.Presenter.Inspect().Accessories.Count);
        var owner = view.Runtime; var snapshot = view.Snapshot; var epoch = owner!.Epoch;
        var anchor = new Point(.38, .46);
        var point = view.PresenterToViewport(AccessoryBodyMapper.Map(view.Presenter.Inspect().Body!, view.Presenter.GaitFrame, anchor.X, anchor.Y));
        Point? picked = null; view.AnchorPicked += (x, y) => picked = new(x, y);
        view.SetTool("pick"); Assert.IsTrue(view.UpdatePick(point));
        Assert.AreEqual(0, view.AnchorGhost.Children.Count); Assert.AreEqual(Visibility.Collapsed, view.AnchorGhost.Visibility);
        Assert.IsTrue(view.CommitPick(point)); Assert.IsNotNull(picked);
        Assert.AreEqual(anchor.X, picked.Value.X, 1e-7); Assert.AreEqual(anchor.Y, picked.Value.Y, 1e-7);
        view.PanBy(new(12, -8)); view.ZoomAt(point, 1.2);
        var scale = view.CameraScale; var pan = view.CameraPan;
        view.SetContent(Result("synthetic-first-artwork", true));
        Assert.AreSame(owner, view.Runtime); Assert.AreSame(snapshot, view.Snapshot); Assert.AreEqual(epoch, owner.Epoch);
        Assert.AreEqual(scale, view.CameraScale); Assert.AreEqual(pan, view.CameraPan);
        Assert.AreEqual(1, view.Presenter.EquippedCount); Assert.AreEqual(1, view.Presenter.Inspect().Accessories.Count);
        Assert.IsNull(view.Error); Assert.IsNull(view.Presenter.AccessoryError);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void HideDuringInitialNativeCreationCanReenterWithoutFaultingOrReplacingWorld()
    {
        var module = Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll");
        if (!File.Exists(module)) Assert.Inconclusive("The real product belt module is required.");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var creates = 0;
        var runtime = new BeltBodyRuntime(body =>
        {
            Interlocked.Increment(ref creates); entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Native creation test barrier timed out.");
            return new BeltNativeSession(module, body);
        }, true);
        using var view = new AccessorySimulationView(DangoAppearanceConfiguration.Default, runtime);
        try
        {
            view.Measure(new Size(640, 480)); view.Arrange(new Rect(0, 0, 640, 480)); view.UpdateLayout();
            view.SetContent(Result("synthetic-early-hide", true)); view.SetActive(true);
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(3)));
            view.SetActive(false); view.SetActive(true); release.Set();
            Wait(() => view.IsActive || view.Error is not null);
            Assert.IsNull(view.Error); Assert.IsTrue(view.IsActive); Assert.AreEqual(1, creates);
            Assert.IsTrue(runtime.AdvanceAsync().GetAwaiter().GetResult()); view.PresentFrame(1d / 60);
            Assert.IsNotNull(view.Snapshot?.Frame); Assert.IsFalse(view.Snapshot.ActualInput.DragActive);
        }
        finally { release.Set(); }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ProductionContinuousOwnerActuallyStopsSteppingWhenTheModeExits()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll")))
            Assert.Inconclusive("The real product belt module is required.");
        using var view = new AccessorySimulationView(DangoAppearanceConfiguration.Default);
        view.Measure(new Size(640, 480)); view.Arrange(new Rect(0, 0, 640, 480)); view.UpdateLayout();
        view.SetContent(Result("synthetic-continuous", true)); view.SetActive(true);
        Wait(() => view.Snapshot is not null || view.Error is not null);
        Assert.IsNull(view.Error); Assert.IsNotNull(view.Snapshot?.Frame);
        var runtime = (BeltBodyRuntime)view.Runtime!;
        Assert.IsTrue(runtime.Metrics.NativeStepCalls > 0);
        view.SetActive(false); runtime.OwnerBarrierAsync().GetAwaiter().GetResult();
        var stopped = runtime.Metrics.NativeStepCalls;
        var watch = Stopwatch.StartNew(); Wait(() => watch.ElapsedMilliseconds >= 100);
        Assert.AreEqual(stopped, runtime.Metrics.NativeStepCalls);
        Assert.IsFalse(runtime.Metrics.Running); Assert.IsFalse(view.IsActive);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void IndependentNativeOwnerPresentsTheExactAppearanceAndOnlyTheEditedAccessory()
    {
        using var rig = new Rig();
        var result = Result("synthetic-first", false);
        rig.View.SetContent(result);
        rig.Step(5);
        Assert.IsNotNull(rig.View.Snapshot?.Frame);
        Assert.AreEqual(rig.Appearance.Body, rig.View.Snapshot.Body);
        Assert.AreEqual(1, rig.View.Presenter.EquippedCount);
        Assert.AreEqual(1, rig.View.Presenter.Inspect().Accessories.Count);
        Assert.IsNull(rig.View.Presenter.AccessoryError);
        Assert.IsNull(rig.View.Error);
        var source = rig.View.Snapshot;
        var calls = rig.Runtime.Metrics.NativeStepCalls;
        var image = new RenderTargetBitmap(640, 480, 96, 96, PixelFormats.Pbgra32);
        rig.View.UpdateLayout(); image.Render(rig.View);
        var pixels = new byte[640 * 480 * 4]; image.CopyPixels(pixels, 640 * 4, 0);
        Assert.IsTrue(Enumerable.Range(0, 640 * 480).Any(i => pixels[i * 4 + 3] > 0));
        Assert.AreSame(source, rig.View.Snapshot);
        Assert.AreEqual(calls, rig.Runtime.Metrics.NativeStepCalls, "Painting cannot advance the native owner.");
        rig.View.SetContent(Result("synthetic-second", true));
        rig.Step(2);
        Assert.AreEqual(1, rig.View.Presenter.EquippedCount);
        Assert.AreEqual(1, rig.View.Presenter.Inspect().Accessories.Count);
        Assert.IsNull(rig.View.Presenter.AccessoryError);
        Assert.IsFalse(result.Item.IsEquipped, "The source wearing value remains untouched.");
        Assert.AreEqual(1, rig.Sessions.Count);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void DefaultPointerDragsOnlyTheRealBodyAndReleaseCannotLeaveAForceHeld()
    {
        using var rig = new Rig(); rig.View.SetContent(Result("synthetic-drag", true)); rig.Step(4);
        Assert.IsFalse(rig.View.BeginPointer(new(4, 4)), "Empty space cannot start body input.");
        var centre = rig.BodyPoint(.5, .5);
        Assert.IsTrue(rig.View.BeginPointer(centre)); Assert.IsTrue(rig.View.IsDragging);
        var destination = centre + new Vector(30, -35);
        var target = rig.View.ViewportToWorld(destination);
        rig.View.MovePointer(destination); rig.Step();
        var input = rig.View.Snapshot!.ActualInput;
        Assert.IsTrue(input.DragActive);
        Assert.AreEqual(target.X, input.DragTargetX, 1e-8); Assert.AreEqual(target.Y, input.DragTargetY, 1e-8);
        Assert.AreEqual(0d, input.Horizontal); Assert.IsFalse(input.JumpPressed);
        rig.View.EndPointer(); rig.View.MovePointer(destination + new Vector(10, -10)); rig.Step();
        Assert.IsFalse(rig.View.IsDragging); Assert.IsFalse(rig.View.Snapshot!.ActualInput.DragActive);
        rig.View.SetTool("pan");
        Assert.IsTrue(rig.View.BeginPointer(new(20, 20))); rig.View.MovePointer(new(40, 35)); rig.Step();
        Assert.IsFalse(rig.View.Snapshot!.ActualInput.DragActive);
        rig.View.EndPointer();
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void CameraAndGhostUseActualBodyMappingWithoutMutatingWorldOrWearingValues()
    {
        using var rig = new Rig();
        var result = Result("synthetic-pick", true); rig.View.SetContent(result); rig.Step(6);
        var frame = rig.View.Snapshot; var nativeCalls = rig.Runtime.Metrics.NativeStepCalls;
        var bounds = rig.Sessions.Single().BoundsChanges;
        var mesh = rig.View.Presenter.Inspect().Accessories.Single().Frame;
        var vertices = mesh.Vertices.ToArray();
        var anchor = new Point(.31, .43);
        var before = rig.BodyPoint(anchor.X, anchor.Y);
        var worldBefore = rig.View.ViewportToWorld(before);
        rig.View.ZoomAt(before, 1.35); rig.View.PanBy(new(12, -6));
        var point = rig.BodyPoint(anchor.X, anchor.Y);
        var actualWorld = rig.View.ViewportToWorld(point);
        Assert.AreEqual(worldBefore.X, actualWorld.X, 1e-8); Assert.AreEqual(worldBefore.Y, actualWorld.Y, 1e-8);
        Assert.AreSame(frame, rig.View.Snapshot); Assert.AreEqual(nativeCalls, rig.Runtime.Metrics.NativeStepCalls);
        Assert.AreEqual(bounds, rig.Sessions.Single().BoundsChanges, "The camera must not move native walls.");
        Point? committed = null; rig.View.AnchorPicked += (x, y) => committed = new(x, y);
        rig.View.SetTool("pick");
        Assert.IsFalse(rig.View.UpdatePick(new(double.NaN, 0)));
        Assert.IsFalse(rig.View.CommitPick(new(0, 0)));
        Assert.IsTrue(rig.View.UpdatePick(point));
        Assert.AreEqual(Visibility.Visible, rig.View.AnchorGhost.Visibility);
        Assert.AreEqual(.45, rig.View.AnchorGhost.Opacity);
        Assert.IsNull(committed); Assert.AreEqual(.5, result.Item.BodyAnchorX);
        CollectionAssert.AreEqual(vertices, rig.View.Presenter.Inspect().Accessories.Single().Frame.Vertices.ToArray(),
            "A hover ghost must not perturb the running accessory solver.");
        rig.View.CancelPick(); Assert.IsNull(committed); Assert.IsFalse(rig.View.IsPicking);
        Assert.AreEqual(Visibility.Collapsed, rig.View.AnchorGhost.Visibility);
        rig.View.SetTool("pick"); Assert.IsTrue(rig.View.CommitPick(point));
        Assert.IsNotNull(committed); Assert.AreEqual(anchor.X, committed.Value.X, 1e-7); Assert.AreEqual(anchor.Y, committed.Value.Y, 1e-7);
        Assert.AreEqual(.5, result.Item.BodyAnchorX); Assert.IsFalse(rig.View.IsPicking);
        Assert.AreSame(frame, rig.View.Snapshot); Assert.AreEqual(nativeCalls, rig.Runtime.Metrics.NativeStepCalls);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ExitStopsInputAndOwnerAndRapidReentryKeepsOnlyTheLatestLifecycle()
    {
        using var rig = new Rig(); rig.View.SetContent(Result("synthetic-lifecycle", true)); rig.Step(3);
        Assert.IsTrue(rig.View.BeginPointer(rig.BodyPoint(.5, .5))); rig.Step();
        Assert.IsTrue(rig.View.Snapshot!.ActualInput.DragActive);
        rig.View.SetActive(false); Wait(() => !rig.Runtime.Metrics.Running);
        var calls = rig.Runtime.Metrics.NativeStepCalls;
        Assert.IsFalse(rig.View.IsDragging); Assert.IsFalse(rig.View.IsActive);
        rig.View.MovePointer(new(300, 100)); rig.View.PresentFrame(1d / 60);
        Assert.AreEqual(calls, rig.Runtime.Metrics.NativeStepCalls);
        rig.View.SetActive(true); rig.View.SetActive(false); rig.View.SetActive(true);
        Wait(() => rig.View.IsActive); rig.Step();
        Assert.IsFalse(rig.View.Snapshot!.ActualInput.DragActive, "Old held input cannot revive on reentry.");
        Assert.AreEqual(1, rig.Sessions.Count);
        rig.View.Dispose();
        Assert.AreEqual(1, rig.Sessions.Single().Disposals);
        Assert.IsFalse(rig.Runtime.Metrics.Running);
        Assert.IsTrue(rig.Sessions.Single().Threads.Distinct().Count() == 1, "Create/step/dispose belong to one native owner thread.");
        rig.View.Dispose(); Assert.AreEqual(1, rig.Sessions.Single().Disposals);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ProductKeyboardCommandsReachTheNativeOwnerAndCombineWithDragWithoutRepeatingJump()
    {
        using var rig = new Rig(); rig.Step(8);
        var initialX = rig.View.Snapshot!.Frame!.CoreX;
        Assert.IsTrue(rig.View.SetKey(Key.D, true)); rig.Step(24);
        Assert.AreEqual(1d, rig.View.Snapshot!.ActualInput.Horizontal);
        Assert.IsTrue(rig.View.Snapshot.Frame!.CoreX > initialX + .001, "The native frame must actually move under the product directional command.");
        rig.View.SetKey(Key.A, true); rig.Step(); Assert.AreEqual(0d, rig.View.Snapshot!.ActualInput.Horizontal);
        rig.View.SetKey(Key.D, false); rig.Step(); Assert.AreEqual(-1d, rig.View.Snapshot!.ActualInput.Horizontal);
        rig.View.SetKey(Key.W, true); rig.View.SetKey(Key.S, true); rig.Step();
        Assert.AreEqual(1d, rig.View.Snapshot!.ActualInput.Stretch); Assert.AreEqual(1d, rig.View.Snapshot.ActualInput.Downward);
        rig.View.SetKey(Key.Space, true); rig.Step(); Assert.IsTrue(rig.View.Snapshot!.ActualInput.JumpPressed);
        rig.View.SetKey(Key.Space, true, true); rig.Step(); Assert.IsFalse(rig.View.Snapshot!.ActualInput.JumpPressed);
        rig.View.SetKey(Key.Space, false); rig.View.SetKey(Key.Space, true); rig.Step();
        Assert.IsTrue(rig.View.Snapshot!.ActualInput.JumpPressed);
        Assert.IsTrue(rig.View.BeginPointer(rig.BodyPoint(.5, .5))); rig.Step();
        Assert.IsTrue(rig.View.Snapshot!.ActualInput.DragActive); Assert.AreEqual(-1d, rig.View.Snapshot.ActualInput.Horizontal);
        rig.View.EndPointer(); rig.Step();
        Assert.IsFalse(rig.View.Snapshot!.ActualInput.DragActive); Assert.AreEqual(-1d, rig.View.Snapshot.ActualInput.Horizontal);
        rig.View.ClearInput(); rig.Step();
        Assert.AreEqual(0d, rig.View.Snapshot!.ActualInput.Horizontal); Assert.AreEqual(0d, rig.View.Snapshot.ActualInput.Stretch);
        Assert.AreEqual(0d, rig.View.Snapshot.ActualInput.Downward); Assert.IsFalse(rig.View.Snapshot.ActualInput.JumpPressed);
        rig.View.ReconcileKeyboard([Key.A, Key.W, Key.S, Key.Space]);
        rig.View.SetKey(Key.A, true, true); rig.View.SetKey(Key.Space, true, true); rig.Step();
        Assert.AreEqual(0d, rig.View.Snapshot!.ActualInput.Horizontal); Assert.IsFalse(rig.View.Snapshot.ActualInput.JumpPressed);
        rig.View.SetKey(Key.A, false); rig.View.SetKey(Key.A, true); rig.Step();
        Assert.AreEqual(-1d, rig.View.Snapshot!.ActualInput.Horizontal);
        rig.View.SetActive(false); Assert.IsFalse(rig.View.SetKey(Key.D, true));
        rig.View.SetActive(true); Wait(() => rig.View.IsActive); rig.Step();
        Assert.AreEqual(0d, rig.View.Snapshot!.ActualInput.Horizontal); Assert.IsFalse(rig.View.Snapshot.ActualInput.DragActive);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void WalkingIntoBothSceneWallsKeepsOwnerHealthyAndCanReverseAway()
    {
        using var rig = new Rig(); rig.Step(8);
        foreach (var direction in new[] { 1, -1 })
        {
            var toward = direction > 0 ? Key.D : Key.A;
            var away = direction > 0 ? Key.A : Key.D;
            rig.View.SetKey(toward, true);
            rig.Step(1400);
            var wallX = rig.View.Snapshot!.Frame!.CoreX;
            var half = 640 / (2 * 1.75 * BeltBodyAppearancePresenter.WorldToCanvas);
            var curve = rig.View.Snapshot.Frame.Curve;
            Assert.IsTrue(Enumerable.Range(0, curve.Length / 2).All(i => curve[i * 2] >= -half - 1e-5 && curve[i * 2] <= half + 1e-5), "The whole physical boundary remains inside the scene walls.");
            Assert.IsTrue(direction * wallX > .25, "The real body must reach the selected side.");
            rig.View.SetKey(toward, false); rig.View.SetKey(away, true);
            rig.Step(240);
            Assert.IsTrue(direction * (wallX - rig.View.Snapshot!.Frame!.CoreX) > .1,
                "A reversed walking command must leave the wall.");
            rig.View.ClearInput();
            Assert.IsNull(rig.Runtime.Metrics.Error);
            Assert.IsTrue(rig.Runtime.Metrics.Running);
            Assert.IsTrue(rig.Runtime.Metrics.QueueDepth < 4);
        }
    }
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void RefreshResetsRealNativePoseAndMotionButPreservesOwnerContentAndCamera()
    {
        using var rig = new Rig(); rig.Step(8);
        rig.View.PanBy(new(19, -12)); rig.View.ZoomAt(new(280, 200), 1.3);
        var scale = rig.View.CameraScale; var pan = rig.View.CameraPan;
        var owner = rig.View.Runtime;
        rig.View.SetKey(Key.D, true); rig.Step(48);
        Assert.IsTrue(rig.View.Snapshot!.Frame!.CoreX > .01);
        var epoch = rig.Runtime.Epoch;
        var reset = rig.View.RefreshBodyAsync();
        Assert.AreSame(reset, rig.View.RefreshBodyAsync(), "Rapid repeated refreshes share a single reset.");
        Wait(() => reset.IsCompleted); reset.GetAwaiter().GetResult();
        rig.Step();
        Assert.AreSame(owner, rig.View.Runtime); Assert.IsTrue(rig.Runtime.Epoch > epoch);
        Assert.AreEqual(0, rig.View.Snapshot!.Frame!.CoreX, .001);
        Assert.AreEqual(0d, rig.View.Snapshot.ActualInput.Horizontal);
        Assert.IsFalse(rig.View.Snapshot.ActualInput.DragActive);
        Assert.AreEqual(scale, rig.View.CameraScale); Assert.AreEqual(pan, rig.View.CameraPan);
        Assert.AreEqual(1, rig.View.Presenter.EquippedCount);
        Assert.AreEqual(2, rig.Sessions.Count); Assert.AreEqual(1, rig.Sessions[0].Disposals);
        rig.View.SetKey(Key.D, true, true); rig.Step();
        Assert.AreEqual(0d, rig.View.Snapshot.ActualInput.Horizontal, "Held keys stay suppressed until released.");
        rig.View.SetKey(Key.D, false); rig.View.SetKey(Key.D, true); rig.Step();
        Assert.AreEqual(1d, rig.View.Snapshot.ActualInput.Horizontal);
        var exitingReset = rig.View.RefreshBodyAsync(); rig.View.SetActive(false);
        Wait(() => exitingReset.IsCompleted); exitingReset.GetAwaiter().GetResult();
        rig.Runtime.OwnerBarrierAsync().GetAwaiter().GetResult();
        Assert.IsFalse(rig.View.IsActive); Assert.IsFalse(rig.Runtime.Metrics.Running);
    }
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void GuardOnlyBoundsStillRejectAndRefreshRecoversTheSameFailedOwner()
    {
        using var rig = new Rig(); rig.Step(8);
        rig.Runtime.SetSolidWallsAsync(false).GetAwaiter().GetResult();
        rig.View.SetKey(Key.D, true);
        for (var i = 0; i < 500 && rig.Runtime.Metrics.Error is null; i++)
        {
            rig.Runtime.AdvanceAsync().GetAwaiter().GetResult(); rig.View.PresentFrame(1d / 60);
        }
        Assert.IsNotNull(rig.Runtime.Metrics.Error, "The existing guard-only default must continue rejecting an escaped boundary.");
        Assert.IsNotNull(rig.View.Error); Assert.IsFalse(rig.Runtime.Metrics.Running);
        var owner = rig.View.Runtime; var reset = rig.View.RefreshBodyAsync();
        Wait(() => reset.IsCompleted); reset.GetAwaiter().GetResult(); rig.Step(4);
        Assert.AreSame(owner, rig.View.Runtime); Assert.IsNull(rig.Runtime.Metrics.Error);
        Assert.IsNull(rig.Runtime.LastFailure); Assert.IsNull(rig.View.Error);
        Assert.AreEqual(0, rig.View.Snapshot!.Frame!.CoreX, .001);
        Assert.AreEqual(2, rig.Sessions.Count); Assert.AreEqual(1, rig.Sessions[0].Disposals);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ResizingAroundAnEdgePoseKeepsTheNativeBodyInsideAndOwnerResponsive()
    {
        using var rig = new Rig(); rig.Step(8);
        rig.View.SetKey(Key.D, true); rig.Step(220);
        var x = rig.View.Snapshot!.Frame!.CoreX;
        Assert.IsTrue(x > 1);
        rig.View.Measure(new Size(420, 480)); rig.View.Arrange(new Rect(0, 0, 420, 480)); rig.View.UpdateLayout();
        rig.Runtime.OwnerBarrierAsync().GetAwaiter().GetResult(); rig.Step(20);
        var half = 420 / (2 * 1.75 * BeltBodyAppearancePresenter.WorldToCanvas);
        Assert.IsTrue(rig.View.Snapshot!.Frame!.MaximumX <= half + 1e-5);
        rig.View.SetKey(Key.D, false); rig.View.SetKey(Key.A, true); rig.Step(180);
        Assert.IsTrue(rig.View.Snapshot.Frame.CoreX < x - .1);
        Assert.IsNull(rig.Runtime.Metrics.Error); Assert.IsTrue(rig.Runtime.Metrics.Running);
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    public void DefaultGuardOnlyNativeWorldStillRejectsEscapingItsBounds()
    {
        var module = Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll");
        if (!File.Exists(module)) Assert.Inconclusive("The real product belt module is required.");
        using var world = new DangoBeltWorld(module, DangoBodyMaterial.Configure(new(105, 35, 65)));
        world.SetBounds(-2.54, 2.54);
        Exception? failure = null;
        for (var i = 0; i < 500 && failure is null; i++)
        {
            try { world.Step(DangoBeltWorld.FixedDt, new(Horizontal: 1)); }
            catch (InvalidOperationException error) { failure = error; }
        }
        Assert.IsNotNull(failure, "SetBounds must remain guard-only unless solid walls are explicitly enabled.");
        Assert.AreEqual(14d, world.ReadSolverDiagnostics()[3]);
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    public void SideWallContactReportsRealMomentumImpulseAndFiniteEnergyAccounts()
    {
        var module = Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll");
        if (!File.Exists(module)) Assert.Inconclusive("The real product belt module is required.");
        using var world = new DangoBeltWorld(module, DangoBodyMaterial.Configure(new(105, 35, 65)));
        world.SetSolidWalls(true); world.SetBounds(-2.54, 2.54);
        var wallImpulse = false; var released = false;
        for (var i = 0; i < 280; i++)
        {
            world.Step(DangoBeltWorld.FixedDt, released ? default : new(Horizontal: 1));
            var frame = world.Read();
            var diagnostics = world.ReadDiagnostics();
            Assert.IsTrue(diagnostics.All(float.IsFinite));
            Assert.AreEqual(diagnostics[13] - diagnostics[12], diagnostics[14], 1e-5);
            if (released && frame.MaximumX > 2.535 && diagnostics[35] < -1e-6)
            {
                wallImpulse = true;
                var accounted = diagnostics[35] + diagnostics[38] + diagnostics[41] + diagnostics[44] +
                    frame.HorizontalForce * DangoBeltWorld.FixedDt;
                Assert.AreEqual(diagnostics[32] - diagnostics[29], accounted, .002,
                    "The side-wall impulse must appear in the public momentum account.");
            }
            if (frame.MaximumX > 2.53) released = true;
        }
        Assert.IsTrue(wallImpulse, "A right-wall collision must report a nonzero leftward contact impulse.");
    }

    private static AccessoryEditResult Result(string id, bool skeleton)
    {
        var pixels = Enumerable.Repeat(unchecked((int)0xff285d45), 32 * 32).ToArray();
        for (var index = 0; index < 32; index++)
        {
            pixels[index] = pixels[31 * 32 + index] = 0;
            pixels[index * 32] = pixels[index * 32 + 31] = 0;
        }
        var bitmap = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 128); bitmap.Freeze();
        var source = AccessoryEditorSourceAsset.FromBitmap(bitmap);
        var image = AccessoryImageAsset.FromRenderedPng(source.CopyPngBytes());
        var rig = new AccessorySkeletonConfiguration(new("P", 16, 16),
            skeleton ? [new("A", "Synthetic branch", null, [new("AR", 16, 16), new("AJ", 16, 30)], new())] : [],
            [new("L1", skeleton ? "A" : null)]);
        var project = new AccessoryEditorProject(32, 32, 1, image.AssetId,
            [new("L1", "Synthetic layer", source.AssetId, 0, 0, 32, 32)], [source], rig, "Synthetic test accessory",
            [new(skeleton ? "A" : null, source.AssetId)]);
        var item = new AccessoryItem(id, project.Name, image.AssetId, 32, 32)
        {
            ProjectId = project.ProjectId, ProjectVersion = project.Version, EditRevision = 1,
            Skeleton = skeleton ? rig : null, DeformationMode = skeleton ? AccessoryDeformationMode.Skeleton : AccessoryDeformationMode.Rigid,
            IsEquipped = false, ImagePivotX = .5, ImagePivotY = .5, BodyAnchorX = .5, BodyAnchorY = .2,
        };
        return new(project, image, item);
    }

    private sealed class Rig : IDisposable
    {
        internal readonly List<TrackedNativeSession> Sessions = [];
        internal readonly BeltBodyRuntime Runtime;
        internal readonly DangoAppearanceConfiguration Appearance = DangoAppearanceConfiguration.Default.WithBody(new(105, 35, 65));
        internal readonly AccessorySimulationView View;
        internal Rig()
        {
            var module = Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll");
            if (!File.Exists(module)) Assert.Inconclusive("The real product belt module is required.");
            Runtime = new(body => { var session = new TrackedNativeSession(module, body); Sessions.Add(session); return session; }, true);
            View = new(Appearance, Runtime);
            View.Measure(new Size(640, 480)); View.Arrange(new Rect(0, 0, 640, 480)); View.UpdateLayout();
            View.SetContent(Result("synthetic-initial", true));
            View.SetActive(true); Wait(() => View.IsActive || View.Error is not null);
            Assert.IsNull(View.Error); Assert.IsTrue(View.IsActive);
        }
        internal void Step(int count = 1)
        {
            for (var i = 0; i < count; i++)
            {
                Assert.IsTrue(Runtime.AdvanceAsync().GetAwaiter().GetResult(), $"Step {i}: {Runtime.Metrics.Error}; last x={View.Snapshot?.Frame?.CoreX}"); View.PresentFrame(1d / 60);
                Assert.IsNull(View.Error); Assert.IsNull(View.Presenter.RuntimeError);
            }
        }
        internal Point BodyPoint(double x, double y) => View.PresenterToViewport(AccessoryBodyMapper.Map(
            View.Presenter.Inspect().Body!, View.Presenter.GaitFrame, x, y));
        public void Dispose() => View.Dispose();
    }

    private sealed class TrackedNativeSession : IBeltBodySession
    {
        private readonly BeltNativeSession inner;
        internal readonly List<int> Threads = [];
        internal int Disposals, BoundsChanges;
        internal TrackedNativeSession(string module, DangoBodyControls body)
        { Threads.Add(Environment.CurrentManagedThreadId); inner = new(module, body); }
        public void Reconfigure(DangoBodyControls body) { Threads.Add(Environment.CurrentManagedThreadId); inner.Reconfigure(body); }
        public void SetSolidWalls(bool enabled) { Threads.Add(Environment.CurrentManagedThreadId); inner.SetSolidWalls(enabled); }
        public void SetBounds(double left, double right) { Threads.Add(Environment.CurrentManagedThreadId); BoundsChanges++; inner.SetBounds(left, right); }
        public BeltNativeStep Step(DangoBeltInput input) { Threads.Add(Environment.CurrentManagedThreadId); return inner.Step(input); }
        public void Dispose() { Threads.Add(Environment.CurrentManagedThreadId); inner.Dispose(); Disposals++; }
    }

    private static void Wait(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 5000)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(5) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        Assert.IsTrue(condition(), "The detached native lifecycle did not reach its expected state.");
    }
}
