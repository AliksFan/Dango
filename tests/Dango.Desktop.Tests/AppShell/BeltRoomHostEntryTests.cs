using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dango.Core.Furniture;
using Dango.Core.Animation;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Physics.Tuning;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Dango.Desktop.Tests.AppShell;

[TestClass, DoNotParallelize]
public sealed class BeltRoomHostEntryTests
{
    [STATestMethod]
    public void HiddenRoomDriverAdvancesOnlyItsOwnerAndRendersTheNativeContour()
    {
        var module = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE") ??
            Path.Combine(TestRepository.Root, "artifacts", "belt-room-f7-connection", "native", "build", "Release", "dango_belt_room.dll");
        if (!File.Exists(module)) Assert.Inconclusive("Independent belt room DLL is required.");
        using var runtime = StackedDangoSceneRuntime.CreateBeltRoom(module);
        var controls = new DangoBodyControls(120, 100, 0);
        runtime.ConfigureBeltRoomAppearance(DangoAppearanceConfiguration.Default.WithBody(controls));
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var source = new ManualRoomFrames();
        long ticks = 0;
        var view = new LocalRoomSceneView();
        using var hiddenSource = new HwndSource(new HwndSourceParameters("Fictional hidden belt room verification")
        {
            Width = 960, Height = 600, WindowStyle = unchecked((int)0x80000000),
        }) { RootVisual = view };
        Assert.IsFalse(IsWindowVisible(hiddenSource.Handle), "The native verification surface must remain hidden.");
        view.EnableWindowCamera();
        view.Measure(new Size(960, 600));
        view.Arrange(new Rect(0, 0, 960, 600));
        view.UpdateLayout();
        Assert.IsTrue(view.IsVisible, "Hidden HwndSource must actually satisfy the driver's visibility precondition.");
        using var driver = new LocalRoomSceneDriver(new DesktopSceneSession(), runtime, source, () => ticks, TimeSpan.TicksPerSecond);
        var scheduler = Field<HostFrameScheduler>(driver, "scheduler");
        var healthyNotifications = 0;
        driver.HealthyFrameReady += _ => healthyNotifications++;
        driver.Start(view, 1, enterWorld: true);
        void Advance() { ticks += HostFrameClock.FixedStep.Ticks; source.Raise(ticks); }
        for (var i = 0; i < 12; i++) Advance();
        Assert.IsTrue(view.HasHealthyFrame, runtime.LastError);
        Assert.AreEqual(controls, runtime.EffectiveAppearance.Body);
        Assert.IsTrue(runtime.LastPhysicsFrame!.Succeeded);
        Assert.AreEqual(256, runtime.LastFrame!.Bodies.Single().Contour.Count);
        Assert.AreSame(scheduler, runtime.FrameDriver);
        Assert.AreEqual((ulong)scheduler.Snapshot.SuccessfulSteps, runtime.LastBeltRoomFrame!.Tick);
        Assert.AreEqual(0, healthyNotifications, "Advancing manual physics frames is not a presentation acknowledgement.");
        Assert.IsFalse(driver.Key(Key.D, true, false), "Real input admission must await an actual host presentation acknowledgement.");
        var before = runtime.LastBeltRoomFrame;
        source.Raise(ticks);
        Assert.AreEqual(before.Tick, runtime.LastBeltRoomFrame!.Tick, "Duplicate callbacks must not advance native physics.");
        var otherSource = new ManualRoomFrames();
        using var other = new LocalRoomSceneDriver(new DesktopSceneSession(), runtime, otherSource, () => ticks, TimeSpan.TicksPerSecond);
        Assert.ThrowsExactly<InvalidOperationException>(() => other.Start(view, 2, enterWorld: false));
        var pixels = Render(view);
        PumpUntil(() => healthyNotifications == 1);
        Assert.IsTrue(driver.Key(Key.D, true, false), "A real hidden composition acknowledgement must admit input.");
        var startingX = runtime.CurrentSelectedCenter!.Value.X;
        for (var i = 0; i < 30; i++) Advance();
        Assert.IsTrue(runtime.CurrentSelectedCenter!.Value.X > startingX);
        Assert.IsTrue(driver.Key(Key.D, false, false));
        Assert.IsTrue(driver.Key(Key.W, true, false));
        Advance();
        Assert.IsTrue(Field<DirectionalControlIntent>(runtime, "directionalIntent").Stretch);
        Assert.IsTrue(driver.Key(Key.W, false, false));
        Assert.IsTrue(driver.Key(Key.S, true, false));
        Advance();
        Assert.IsTrue(Field<DirectionalControlIntent>(runtime, "directionalIntent").Squash);
        Assert.IsTrue(driver.Key(Key.S, false, false));
        Assert.IsTrue(driver.Key(Key.Space, true, false));
        Advance();
        Assert.IsTrue(Field<DirectionalControlIntent>(runtime, "directionalIntent").Jump);
        Assert.IsTrue(driver.Key(Key.Space, false, false));
        Advance();
        Assert.IsTrue(runtime.LastPhysicsFrame!.Succeeded);
        Assert.IsTrue(pixels.Any(value => value != 0), "RenderTargetBitmap must contain actual rendered pixels.");
        var body = Field<DeformableDangoVisual>(view, "body");
        Assert.AreEqual(DangoContourFailure.None, body.LastContourFailure);
        var path = (System.Windows.Shapes.Path)body.FindName("BodyPath");
        var polygon = PathGeometry.CreateFromGeometry(path.Data).Figures.Single();
        var drawn = new List<System.Windows.Point> { polygon.StartPoint };
        foreach (var segment in polygon.Segments)
        {
            if (segment is PolyLineSegment line) drawn.AddRange(line.Points);
            else if (segment is LineSegment single) drawn.Add(single.Point);
            else Assert.Fail("The continuous native boundary must remain a polygon, not an old fitted spline.");
        }
        Assert.AreEqual(256, drawn.Count);
        var contour = runtime.LastFrame!.Bodies.Single().Contour;
        for (var i = 0; i < contour.Count; i++)
        {
            Assert.AreEqual(body.Width / 2 + contour[i].X * body.Width * 72 / 160, drawn[i].X, 1e-7);
            Assert.AreEqual(body.Height * .55 - contour[i].Y * body.Width * 72 / 160, drawn[i].Y, 1e-7);
        }
        var withBody = Render(view);
        path.Visibility = Visibility.Hidden;
        byte[] withoutBody;
        try { withoutBody = Render(view); }
        finally { path.Visibility = Visibility.Visible; }
        Assert.IsTrue(withBody.Where((value, index) => value != withoutBody[index]).Count() > 100,
            "The actual 256-point path must contribute pixels to the bitmap.");
        var bounds = view.PetScreenBounds!.Value;
        var center = new System.Windows.Point(bounds.Center.X, bounds.Center.Y);
        Assert.IsTrue(view.IsPetHit(center), "Painted interior must be hit through the native contour path.");
        Assert.IsFalse(view.IsPetHit(new(bounds.X - 100, bounds.Y - 100)));
        driver.Stop();
        var stopped = runtime.LastBeltRoomFrame!.Tick;
        Assert.IsNull(runtime.FrameDriver);
        for (var i = 0; i < 6; i++) Advance();
        Assert.AreEqual(stopped, runtime.LastBeltRoomFrame.Tick);
        driver.Start(view, 3, enterWorld: true);
        for (var i = 0; i < 8; i++) Advance();
        Assert.IsTrue(runtime.LastBeltRoomFrame.Tick > stopped);
        Assert.IsTrue(view.HasHealthyFrame);
        Assert.AreSame(scheduler, runtime.FrameDriver);
        Assert.AreEqual(1, healthyNotifications, "Re-entry still awaits its own composition acknowledgement.");
        Render(view);
        PumpUntil(() => healthyNotifications == 2);
        Assert.IsTrue(driver.Key(Key.A, true, false));
        driver.Stop();
        Assert.IsFalse(driver.Key(Key.A, true, false));
        Assert.IsFalse(IsWindowVisible(hiddenSource.Handle), "No stage of the test may show the hidden surface.");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    private static byte[] Render(FrameworkElement view)
    {
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(960, 600, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var pixels = new byte[960 * 600 * 4];
        bitmap.CopyPixels(pixels, 960 * 4, 0);
        return pixels;
    }

    private sealed class ManualRoomFrames : IHostFrameSource
    {
        public event EventHandler? Rendering;
        internal void Raise(long ticks) => Rendering?.Invoke(this,
            (EventArgs)Activator.CreateInstance(typeof(RenderingEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { TimeSpan.FromTicks(ticks) }, null)!);
    }

    [TestMethod]
    public void ProductDataRootDoesNotRequireAModelSpecificModeOrModuleOption()
    {
        var root = NewRoot();
        string[] args = ["--data-root", root];
        App.ValidateStartupArguments(args);
        Assert.AreEqual(Path.Combine(root, "settings.json"), App.ResolveProductSettingsFile(args));
        Assert.IsNull(App.ResolveProductSettingsFile([]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ValidateStartupArguments(["--belt-room-module", "retired.dll"]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveProductSettingsFile(["--data-root", "relative"]));
    }

    [STATestMethod]
    public void OrdinaryProductSelectsTheRoomModuleWithoutAReviewSwitch()
    {
        var path = Path.Combine(NewRoot(), "settings.json");
        using var host = new DangoApplicationHost(settingsFile: path);
        Assert.IsTrue(Field<StackedDangoSceneRuntime>(host, "stackedRuntime").UsesBeltRoom);
        Assert.AreEqual(0, Field<StackedDangoSceneRuntime>(host, "stackedRuntime").EntityIds.Count);
        Assert.ThrowsExactly<ArgumentException>(() => new DangoApplicationHost(settingsFile: path, roomModulePath: "relative.dll"));
    }

    [TestMethod]
    [DataRow(FurnitureItemKind.StaticPlatform)]
    [DataRow(FurnitureItemKind.RoundBall)]
    public void CandidateValidatesExistingFurnitureKindsWithoutChangingLayout(FurnitureItemKind kind)
    {
        var layout = FurnitureLayout.ForItems(1, Guid.NewGuid(), [new(Guid.NewGuid(), kind, FurnitureLayoutContract.OrdinaryRoom.GetDefaultAnchor(kind))]);
        var before = FurnitureLayoutSerializer.Serialize(layout, FurnitureLayoutContract.OrdinaryRoom);
        DangoApplicationHost.ValidateBeltRoomAdmission(layout, false, true);
        CollectionAssert.AreEqual(before, FurnitureLayoutSerializer.Serialize(layout, FurnitureLayoutContract.OrdinaryRoom));
        DangoApplicationHost.ValidateBeltRoomAdmission(layout, false, false);
        DangoApplicationHost.ValidateBeltRoomAdmission(FurnitureLayout.Empty, false, true);
        DangoApplicationHost.ValidateBeltRoomAdmission(FurnitureLayout.Empty, true, true);
    }

    [STATestMethod]
    public void HiddenSourceCannotInitializeOrSelectARoomAndKeepsExistingLayout()
    {
        var root = NewRoot();
        var catalog = new LocalRoomCatalogStore(root);
        var initial = catalog.Refresh();
        Assert.IsTrue(initial.Success, initial.Warning);
        var id = initial.Catalog!.Rooms.Single().RoomId;
        var layout = FurnitureLayout.ForItems(1, Guid.NewGuid(),
            [new(Guid.NewGuid(), FurnitureItemKind.RoundBall, FurnitureLayoutContract.OrdinaryRoom.GetDefaultAnchor(FurnitureItemKind.RoundBall))]);
        var bytes = FurnitureLayoutSerializer.Serialize(layout, FurnitureLayoutContract.OrdinaryRoom, id);
        File.WriteAllBytes(catalog.LayoutPath(id), bytes);
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"),
            roomModulePath: Path.Combine(root, "missing-candidate.dll"));
        var window = (ControlCenterWindow)typeof(DangoApplicationHost)
            .GetMethod("GetOrCreateControlCenter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        Assert.IsFalse(window.IsVisible, "This integration check must not occupy the screen.");
        Assert.IsNotNull(window.ProductHome.BoundBeltBody);
        Assert.IsNull(window.ProductHome.BoundRuntime, "The homepage retains its own candidate runtime.");
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom);
        host.EnterCatalogRoom(id, false);
        PumpUntil(() => !host.LocalHandoffPending);
        Assert.IsNull(host.LocalRoomFailure, "An invisible source cancels before module admission or a room handoff starts.");
        Assert.AreEqual(0, runtime.EntityIds.Count);
        Assert.IsNull(Field<Guid?>(host, "selectedCatalogRoomId"));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(catalog.LayoutPath(id)));
        Assert.IsFalse(window.IsVisible);
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void PumpUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 8000)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
        Assert.IsTrue(condition(), "Hidden host operation did not complete.");
    }

    private static string NewRoot()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "belt-room-entry", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
