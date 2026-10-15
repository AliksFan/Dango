using System.Reflection;
using System.Windows.Controls;
using System.Xml.Linq;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.StandardWindow;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class DesktopHostModuleIntegrationTests
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1d / 60);
    private static readonly LogicalRect Bounds = new(400, 500, 160, 120);
    private static PhysicsPresentationInput Primary(LogicalRect? bounds = null) => new(
        "fixture-dango", bounds ?? Bounds, DangoActionState.Idle, 1000, 17,
        "fixture-ground", new(0, -1), 0, 1, false, true, []);

    [TestMethod]
    public void FormalRuntimeMustNotSilentlyKeepTheSingleBodyPopulationLock()
    {
        using var runtime = new StackedDangoSceneRuntime();
        runtime.Start();
        Assert.IsTrue(runtime.AddDango(), "已批准接入的正式 runtime 必须能增加第二只，不得保留单体锁定。");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void HostRuntimeMatchesNativeForEveryBodyAndEveryFrame(int count)
    {
        using var runtime = new StackedDangoSceneRuntime();
        using var reference = DangoPhysicsWorld.Create(Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName), configuration: DangoBodyMaterial.Configure(DangoBodyControls.Default));
        var ids = new List<DangoEntityId> { reference.Add(0, 0) };
        runtime.Start();
        for (var i = 1; i < count; i++)
        {
            runtime.AddDango();
            ids.Add(reference.Add(i == 1 ? -2.8 : 2.8, 0));
        }
        for (var index = 0; index < 900; index++)
        {
            var right = index is >= 100 and < 180;
            var down = index is >= 400 and < 430;
            var jump = index is >= 300 and < 330;
            runtime.SetDirectionalControl(new(false, right, jump, down));
            var frame = runtime.Tick(Primary(), Step);
            var expected = reference.Step(Step, ids.Select((id, i) => i == 0
                ? new DangoPhysicsInput(id, right ? 1 : 0, down ? 1 : 0, index == 300)
                : new DangoPhysicsInput(id)).ToArray());
            Assert.IsNull(runtime.LastError, $"count={count}, frame={index}");
            Assert.IsFalse(frame.ConstraintsReleased);
            Assert.AreEqual(expected.ContactStepCount, runtime.LastPhysicsFrame!.ContactStepCount);
            foreach (var a in expected.Bodies)
            {
                var b = runtime.LastPhysicsFrame.Bodies.Single(value => value.EntityId == a.EntityId);
                Assert.AreEqual(a with { Contour = b.Contour }, b);
                CollectionAssert.AreEqual(a.Contour.ToArray(), b.Contour.ToArray());
            }
            CollectionAssert.AreEquivalent(ids.ToArray(), frame.EntityIds.ToArray());
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void RemovingAnySelectedEntityPreservesRemainingIdsAndColors(int removedIndex)
    {
        using var runtime = new StackedDangoSceneRuntime();
        runtime.Start(); runtime.AddDango(); runtime.AddDango();
        var before = runtime.Tick(Primary(), Step);
        var removed = before.EntityIds[removedIndex];
        var surviving = before.EntityIds.Where(id => id != removed).ToArray();
        var colors = before.EntityIds.Select((id, i) => (id, color: before.ColorSlots[i])).ToDictionary(p => p.id, p => p.color);
        runtime.BeginPhysicalDrag(removed, Bounds.Center);
        Assert.IsTrue(runtime.IsDragging);
        runtime.RemoveDango();
        Assert.IsFalse(runtime.IsDragging);
        runtime.RegenerateDango();
        var after = runtime.Tick(Primary(), Step);
        CollectionAssert.AreEquivalent(surviving, after.EntityIds.ToArray());
        foreach (var id in surviving) Assert.AreEqual(colors[id], after.ColorSlots[after.EntityIds.ToList().IndexOf(id)]);
        Assert.ThrowsExactly<DangoPhysicsException>(() => runtime.SelectEntity(removed));
        runtime.AddDango();
        Assert.IsTrue(runtime.EntityIds.Max(id => id.Value) > removed.Value);
    }

    [TestMethod]
    public void MissingModuleLatchesVisibleErrorWithoutLoadingLegacyPhysics()
    {
        using var runtime = new StackedDangoSceneRuntime(Path.Combine(TestRepository.Root, "artifacts", "missing-host-module.dll"));
        runtime.Start();
        var frame = runtime.Tick(Primary(), Step);
        Assert.IsTrue(frame.ConstraintsReleased);
        Assert.IsNotNull(runtime.LastError);
        Assert.IsNull(runtime.LastPhysicsFrame);
        Assert.AreSame(frame, runtime.Tick(Primary(), Step));
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.AddDango());
        runtime.Stop(); runtime.Start();
        Assert.IsNotNull(runtime.LastError);
        runtime.RegenerateDango();
        Assert.IsNull(runtime.LastError);
        Assert.IsTrue(runtime.Tick(Primary(), Step).ConstraintsReleased);
    }

    [TestMethod]
    public void MigrationAndPauseCancelTheGestureButKeepTheWorldAndIds()
    {
        using var runtime = new StackedDangoSceneRuntime();
        runtime.Start(); runtime.AddDango(); runtime.AddDango();
        var ids = runtime.Tick(Primary(), Step).EntityIds.ToArray();
        runtime.BeginPhysicalDrag(ids[1], Bounds.Center);
        runtime.Tick(Primary(new(100, 200, 160, 120)), Step);
        Assert.IsFalse(runtime.IsDragging);
        runtime.Stop();
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.Tick(Primary(), Step));
        runtime.Start(); runtime.Start();
        CollectionAssert.AreEqual(ids, runtime.Tick(Primary(), Step).EntityIds.ToArray());
    }

    [TestMethod]
    public void HeldKeyboardInputCannotTransferToNewSelectionThroughRepeat()
    {
        var held = new DirectionalControlHeldState();
        held.Set(DirectionalControlKey.Jump, true);
        held.ClearUntilRelease();
        held.Set(DirectionalControlKey.Jump, true);
        Assert.IsFalse(held.Snapshot.Jump);
        held.Set(DirectionalControlKey.Jump, false);
        held.Set(DirectionalControlKey.Jump, true);
        Assert.IsTrue(held.Snapshot.Jump);
    }

    [TestMethod]
    [DataRow(480, 360)]
    [DataRow(800, 600)]
    [DataRow(1920, 1080)]
    public void ActualHostSpawnKeepsThreeBodiesInsideTheViewport(int width, int height)
    {
        using var runtime = new StackedDangoSceneRuntime();
        var session = new DesktopSceneSession();
        var viewport = new LogicalRect(0, 0, width, height);
        var snapshot = session.AttachSurface(viewport, 1000);
        var primary = PhysicsPresentationInput.FromSnapshot(snapshot, 0, 0, DangoFacingDirection.Center, null);
        runtime.Start(); runtime.SetViewport(viewport);
        runtime.Tick(primary, Step);
        Assert.IsTrue(runtime.AddDangoInViewport(viewport));
        runtime.Tick(primary, Step);
        Assert.IsTrue(runtime.AddDangoInViewport(viewport));
        var frame = runtime.Tick(primary, Step);
        Assert.IsNull(runtime.LastError);
        Assert.HasCount(3, frame.Bodies);
        for (var i = 0; i < 3; i++) Assert.IsTrue(viewport.Contains(DangoHostFrameBinding.Bounds(frame, i)), $"{width}x{height}, body={i}");
        session.Exit(1100);
    }

    [TestMethod]
    public void HostVerificationSeparatesBothHostsAndThreeBodyAcceptance()
    {
        var definition = Dango.Desktop.Verification.VerificationDefinition.CreatePhysicsModuleHosts();
        Assert.AreEqual(definition.Items.Count, definition.Items.Select(item => item.Id).Distinct().Count());
        foreach (var host in new[] { "window", "wallpaper" })
        {
            Assert.IsTrue(definition.Items.Any(item => item.Id == host + "-2-contact"));
            Assert.IsTrue(definition.Items.Any(item => item.Id == host + "-3-contact"));
        }
        var app = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "App.xaml.cs"));
        StringAssert.Contains(app, "--physics-host-review");
        StringAssert.Contains(app, ".PhysicsHostReview");
        var hostCode = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "AppShell", "DangoApplicationHost.cs"));
        StringAssert.Contains(hostCode, "Path.Combine(AppContext.BaseDirectory, \"physics-review-state\", \"settings.json\")");
    }

    [STATestMethod]
    public void CompiledHostsBindAllVisualsToIdsAndOverlayKeepsHatsLast()
    {
        using var runtime = new StackedDangoSceneRuntime();
        var session = new DesktopSceneSession();
        var window = new DangoSceneWindow(session, runtime);
        var wallpaper = new WorkerWWindow(session, runtime);
        var overlay = new DesktopDangoSceneOverlayWindow(
            p => new(p.X, p.Y), p => new(p.X, p.Y), (_, _) => false, _ => { }, () => { });
        try
        {
            runtime.Start(); runtime.AddDango(); runtime.AddDango();
            var frame = runtime.Tick(Primary(), Step);
            foreach (var host in new object[] { window, wallpaper })
            {
                host.GetType().GetMethod("ApplyPresentationFrame", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(host, [frame]);
                foreach (var (name, i) in new[] { ("DangoVisual", 0), ("MiddleDangoVisual", 1), ("TopDangoVisual", 2) })
                {
                    var visual = (DeformableDangoVisual)((System.Windows.FrameworkElement)host).FindName(name);
                    Assert.AreEqual(frame.EntityIds[i], visual.Tag);
                    Assert.IsTrue(visual.IsHitTestVisible);
                }
            }
            overlay.ApplyFrame(frame);
            var children = ((Canvas)overlay.Content).Children.Cast<object>().ToArray();
            Assert.IsTrue(children.Take(3).All(value => value is DeformableDangoVisual));
            Assert.IsTrue(children.Skip(3).All(value => value is DangoHatVisual));
            var overlap = frame with { RenderOffsets = [new(0, 0), new(0, 0), new(0, 0)] };
            Assert.AreEqual(frame.EntityIds[2], DangoHostFrameBinding.HitTest(overlap, Bounds.Center));
            Assert.IsNull(DangoHostFrameBinding.HitTest(overlap, new(-1000, -1000)));
            SaveRender((Canvas)overlay.Content, "above-icons-three.png", new(overlay.Width, overlay.Height));
            SaveRender((Canvas)window.Content, "window-three.png", new(900, 700));
        }
        finally
        {
            overlay.Close();
            window.PrepareForExit(); window.Close();
            wallpaper.Close();
            session.Exit(Environment.TickCount64);
        }
    }

    private static void SaveRender(System.Windows.FrameworkElement visual, string name, System.Windows.Size size)
    {
        visual.Measure(size);
        visual.Arrange(new System.Windows.Rect(size));
        visual.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(size.Width),
            (int)Math.Ceiling(size.Height), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        using var stream = TestEvidence.Create("host-render", name);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    [TestMethod]
    [DataRow("Window", "DangoSceneWindow.xaml")]
    [DataRow("WorkerW", "WorkerWWindow.xaml")]
    public void EveryBodyHasTheSameDragAndCaptureHandlers(string host, string file)
    {
        var document = XDocument.Load(Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "DesktopHost", host, file));
        var bodies = document.Descendants().Where(e => e.Name.LocalName == "DeformableDangoVisual").ToArray();
        Assert.AreEqual(3, bodies.Length);
        foreach (var body in bodies)
        {
            Assert.AreEqual("OnDangoMouseLeftButtonDown", (string?)body.Attribute("MouseLeftButtonDown"));
            Assert.AreEqual("OnDangoMouseLeftButtonUp", (string?)body.Attribute("MouseLeftButtonUp"));
            Assert.AreEqual("OnDangoMouseMove", (string?)body.Attribute("MouseMove"));
            Assert.AreEqual("OnDangoLostMouseCapture", (string?)body.Attribute("LostMouseCapture"));
        }
    }
}
