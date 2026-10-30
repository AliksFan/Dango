using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Furniture;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Furniture;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.LocalRoomProbe;

internal static partial class Program
{
    private const string RecoveryFixtureName = "recovery-fixture.json";
    private static readonly JsonSerializerOptions RecoveryJson = new() { WriteIndented = true };
    private static DangoAppearanceConfiguration RecoveryAppearance => new(new(91, 132, 174), new(76, 44, 92),
        new EyeLayoutParameters(115, 12, 45, 20), new DangoBodyControls(110, 50, 50));
    private sealed record RecoveryItem(Guid Id, FurnitureItemKind Kind, FurnitureAnchor Anchor);
    private sealed record RecoveryRoom(Guid Id, long LayoutVersion, RoomSafePosition Position, RoomCameraSnapshot Camera,
        string LayoutHash, RecoveryItem[] Items, FurnitureAnchor MovedBall);
    private sealed record RecoveryFixture(int WriterPid, Guid ProfileId, string AssetId, string AssetHash,
        string AppearanceHash, string AccessoryHash, RecoveryRoom[] Rooms);

    private static string RecoveryHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void RecoveryNear(double actual, double expected, double tolerance, string message) =>
        Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance,
            $"{message}: expected={expected:R}, actual={actual:R}, tolerance={tolerance:R}");
    private static void RecoveryReport(string root, string name, object value) =>
        File.WriteAllText(Path.Combine(root, name), JsonSerializer.Serialize(value, RecoveryJson));

    private static void WriteBeltRecovery(string root, string module)
    {
        Require(!File.Exists(Path.Combine(root, RecoveryFixtureName)), "Recovery writer requires a fresh fixture.");
        var appearancePath = Path.Combine(root, "appearance.json");
        new AppearanceSettingsStore().Save(appearancePath, RecoveryAppearance);
        var pixels = Enumerable.Range(0, 8 * 8).SelectMany(i => new byte[] { 60, (byte)(100 + i), 160, (byte)(i == 0 ? 0 : 255) }).ToArray();
        var bitmap = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, pixels, 32);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var sourceImage = Path.Combine(root, "fictional-import.png");
        using (var output = File.Create(sourceImage)) encoder.Save(output);
        var asset = AccessoryImageAsset.ImportPng(sourceImage);
        var accessories = new AccessoryConfiguration([new("fictional-recovery-accessory", "虚构恢复配饰", asset.AssetId, 8, 8)
            { SizePercent = 85, BodyAnchorX = .45, BodyAnchorY = .1 }]);
        var accessoryStore = new AccessorySettingsStore();
        accessoryStore.Save(root, accessories, new Dictionary<string, AccessoryImageAsset> { [asset.AssetId] = asset });
        var catalog = new LocalRoomCatalogStore(root);
        var current = catalog.Refresh(); Require(current.Success, current.Warning ?? "Catalog failed.");
        var first = current.Catalog!.Rooms.Single();
        var secondId = Guid.NewGuid();
        Require(catalog.Create(Guid.NewGuid(), secondId, "虚构恢复乙房", LocalRoomKind.Private, 6).Success, "Second room creation failed.");
        var rooms = new[] { first, catalog.ReadCurrent().Find(secondId)! };
        var expected = new List<RecoveryRoom>();
        for (var i = 0; i < rooms.Length; i++)
        {
            var room = rooms[i];
            var items = new FurnitureItemInstance[] {
                new(Guid.NewGuid(), FurnitureItemKind.StaticPlatform, new(1440 + i * 80, 150 + i * 30)),
                new(Guid.NewGuid(), FurnitureItemKind.RoundBall, new(1800 + i * 80, 150 + i * 40)) };
            using (var lease = catalog.CreateLayoutStore(room.RoomId).AcquireLease(catalog.LayoutPath(room.RoomId)))
            using (var stage = lease.Stage(FurnitureLayout.ForItems(1, Guid.NewGuid(), items), 0))
                Require(stage.Commit().State == FurnitureCommitState.Committed, "Fixture layout did not commit.");
            using var rig = new RecoveryRig(root, module, catalog, room, null);
            rig.WaitReady();
            var startingX = rig.Runtime.CurrentSelectedCenter!.Value.X;
            var key = i == 0 ? Key.A : Key.D;
            Require(rig.Driver.Key(key, true, false), "Driver did not admit local movement after presentation.");
            rig.Advance(24);
            Require(rig.Driver.Key(key, false, false), "Driver did not release movement.");
            rig.Advance(100);
            Require(Math.Abs(rig.Runtime.CurrentSelectedCenter!.Value.X - startingX) > 1, "Writer body did not actually move.");
            rig.View.Camera.RestoreState(new(i == 0 ? RoomCameraMode.Manual : RoomCameraMode.EdgeFollow,
                RoomViewPreset.Custom, 120 - i * 15, 400 + i * 150, 40));
            rig.Advance(12);
            var checkpoint = rig.Driver.CaptureCheckpoint() ?? throw new InvalidDataException("Real driver could not capture a checkpoint.");
            var point = checkpoint.Position ?? throw new InvalidDataException("Writer did not reach a verified floor checkpoint.");
            Require(rig.Runtime.HasFloorSupport, "Writer checkpoint must use actual floor support.");
            RecoveryNear(point.X, rig.Runtime.CurrentSelectedCenter!.Value.X, .5, "Captured point must follow the real native body");
            var movedBall = rig.Runtime.Furniture!.RenderItems.Single(item => item.Kind == FurnitureItemKind.RoundBall).Anchor;
            Require(Math.Abs(movedBall.Y - items[1].Anchor.Y) > 10, "Writer ball must really leave its saved layout anchor.");
            Require(new RoomResumeStore(catalog).Save(room.RoomId, checkpoint.LayoutVersion, checkpoint.Position, checkpoint.Camera).State
                == FurnitureCommitState.Committed, "Actual captured checkpoint was not persisted.");
            expected.Add(new(room.RoomId, checkpoint.LayoutVersion, point, checkpoint.Camera,
                RecoveryHash(catalog.LayoutPath(room.RoomId)), items.Select(item => new RecoveryItem(item.InstanceId, item.Kind, item.Anchor)).ToArray(), movedBall));
            rig.VerifyAppearance(asset.AssetId);
            RecoveryReport(root, $"writer-room-{i}.json", rig.Evidence());
        }
        var resume = new RoomResumeStore(catalog);
        Require(resume.SaveSelection(secondId).State == FurnitureCommitState.Committed, "Selection did not commit.");
        Require(Math.Abs(expected[0].Position.X - expected[1].Position.X) > 1, "The two rooms must have distinct real checkpoints.");
        RecoveryReport(root, RecoveryFixtureName, new RecoveryFixture(Environment.ProcessId, catalog.ReadCurrent().ProfileId,
            asset.AssetId, RecoveryHash(AccessorySettingsStore.GetAssetPath(root, asset.AssetId)), RecoveryHash(appearancePath),
            RecoveryHash(AccessorySettingsStore.GetManifestPath(root)), expected.ToArray()));
        RecoveryReport(root, "writer-result.json", new { passed = true, pid = Environment.ProcessId, rooms = expected.Count,
            visibleWindows = 0, scope = "packaged product stores, real native room driver, hidden WPF surface; no application shell" });
    }

    private static void ReadBeltRecovery(string root, string module, bool unsafeCheckpoint)
    {
        var fixture = JsonSerializer.Deserialize<RecoveryFixture>(File.ReadAllText(Path.Combine(root, RecoveryFixtureName)))
            ?? throw new InvalidDataException("Missing recovery fixture.");
        Require(fixture.WriterPid != Environment.ProcessId, "Reader must be an independent OS process.");
        var catalog = new LocalRoomCatalogStore(root);
        var current = catalog.Refresh();
        Require(current.Success && current.Catalog!.ProfileId == fixture.ProfileId && current.Catalog.ActiveCount == 2,
            "Restart lost profile identity or rooms.");
        Require(RecoveryHash(Path.Combine(root, "appearance.json")) == fixture.AppearanceHash, "Appearance bytes changed before restart.");
        Require(new AppearanceSettingsStore().Load(Path.Combine(root, "appearance.json")).Configuration == RecoveryAppearance,
            "Appearance values did not restore.");
        Require(RecoveryHash(AccessorySettingsStore.GetManifestPath(root)) == fixture.AccessoryHash, "Accessory configuration bytes changed.");
        Require(RecoveryHash(AccessorySettingsStore.GetAssetPath(root, fixture.AssetId)) == fixture.AssetHash, "Internal PNG asset changed.");
        var accessory = new AccessorySettingsStore().Load(root);
        Require(accessory.State == AccessorySettingsLoadState.Valid && accessory.Configuration.Items.Single().AssetId == fixture.AssetId,
            "Accessory manifest did not restore the imported PNG reference.");
        var resume = new RoomResumeStore(catalog);
        Require(resume.LoadSelection(out _) == fixture.Rooms[1].Id, "Last selection did not survive process exit.");
        var reports = new List<object>();
        for (var i = 0; i < fixture.Rooms.Length; i++)
        {
            var expected = fixture.Rooms[i];
            var room = current.Catalog!.Find(expected.Id)!;
            Require(RecoveryHash(catalog.LayoutPath(room.RoomId)) == expected.LayoutHash, "Room layout identity/content crossed rooms.");
            var layout = catalog.ReadLayout(room.RoomId);
            Require(layout.LayoutVersion == expected.LayoutVersion && layout.Items.Select(item => new RecoveryItem(item.InstanceId, item.Kind, item.Anchor))
                .SequenceEqual(expected.Items), "Restart changed instance IDs, kinds or anchors.");
            if (unsafeCheckpoint && i == 0)
                Require(resume.Save(room.RoomId, expected.LayoutVersion, new(-500, 0), expected.Camera).State
                    == FurnitureCommitState.Committed, "Unsafe-point fixture did not commit.");
            var saved = resume.Load(room.RoomId);
            Require(saved.Valid && saved.Value is not null, "Resume file failed to load.");
            var originalBytes = saved.Snapshot.Bytes!;
            using (var rig = new RecoveryRig(root, module, catalog, room, saved.Value))
            {
                rig.AttachFurniture();
                var initialBall = rig.Runtime.Furniture!.RenderItems.Single(item => item.Kind == FurnitureItemKind.RoundBall).Anchor;
                var anchor = expected.Items.Single(item => item.Kind == FurnitureItemKind.RoundBall).Anchor;
                RecoveryNear(initialBall.X, anchor.X, 1, "New process ball must start at the layout X anchor");
                RecoveryNear(initialBall.Y, anchor.Y, 1, "New process ball must start at the layout Y anchor");
                Require(Math.Abs(initialBall.Y - expected.MovedBall.Y) > 5, "Reader reused the writer's transient ball state.");
                rig.WaitReady();
                var actualX = rig.Runtime.CurrentSelectedCenter!.Value.X;
                if (unsafeCheckpoint && i == 0)
                {
                    Require(rig.Driver.ResumeWarning is not null, "Unsafe checkpoint silently restored without a warning.");
                    Require(RoomResumeSafety.CanRestore(new(actualX, 0), layout, rig.Runtime.SupportEnvelope!.Value), "Fallback position is not safe for actual native geometry.");
                    Require(Math.Abs(actualX - saved.Value!.Position!.X) > 100, "Out-of-bounds checkpoint was actually restored.");
                }
                else
                {
                    Require(rig.Driver.ResumeWarning is null, rig.Driver.ResumeWarning ?? "Unexpected checkpoint rejection.");
                    RecoveryNear(actualX, expected.Position.X, 1, "Restart did not restore the correct room's real checkpoint");
                }
                var actualCamera = rig.View.Camera.CaptureState();
                Require(actualCamera.Mode == expected.Camera.Mode && actualCamera.Preset == expected.Camera.Preset,
                    "Per-room camera mode or preset did not restore.");
                RecoveryNear(actualCamera.RequestedPercent, expected.Camera.RequestedPercent, 1e-9, "Camera zoom did not restore");
                if (expected.Camera.Mode == RoomCameraMode.Manual)
                {
                    RecoveryNear(actualCamera.X, expected.Camera.X, 1e-7, "Manual camera X did not restore");
                    RecoveryNear(actualCamera.Y, expected.Camera.Y, 1e-7, "Manual camera Y did not restore");
                }
                rig.VerifyAppearance(fixture.AssetId);
                Require(originalBytes.SequenceEqual(resume.Load(room.RoomId).Snapshot.Bytes!), "Binding a checkpoint rewrote the original resume file.");
                reports.Add(new { roomId = room.RoomId, actualX, expected.Position, camera = actualCamera,
                    initialBall, writerTransientBall = expected.MovedBall, warning = rig.Driver.ResumeWarning, evidence = rig.Evidence() });
            }
            Require(originalBytes.SequenceEqual(resume.Load(room.RoomId).Snapshot.Bytes!), "Runtime disposal rewrote the original resume file.");
            Require(RecoveryHash(catalog.LayoutPath(room.RoomId)) == expected.LayoutHash, "Binding, fallback or disposal changed the persisted room layout.");
        }
        Require(RecoveryHash(Path.Combine(root, "appearance.json")) == fixture.AppearanceHash &&
            RecoveryHash(AccessorySettingsStore.GetManifestPath(root)) == fixture.AccessoryHash &&
            RecoveryHash(AccessorySettingsStore.GetAssetPath(root, fixture.AssetId)) == fixture.AssetHash,
            "Read-only restoration changed persisted appearance, accessories or the imported asset.");
        RecoveryReport(root, unsafeCheckpoint ? "unsafe-result.json" : "reader-result.json",
            new { passed = true, pid = Environment.ProcessId, writerPid = fixture.WriterPid, visibleWindows = 0, rooms = reports,
                scope = "actual packaged product storage/runtime/driver; no normal application shell, real keyboard or wall-clock performance claim" });
    }

    private sealed class RecoveryRig : IDisposable
    {
        private readonly HwndSource source;
        private readonly RecoveryFrames frames = new();
        private readonly List<string> failures = [];
        private long ticks;
        internal StackedDangoSceneRuntime Runtime { get; }
        internal LocalRoomSceneView View { get; }
        internal LocalRoomSceneDriver Driver { get; }
        internal RecoveryRig(string root, string module, LocalRoomCatalogStore catalog, LocalRoomRecord room, RoomResumeDocument? resume)
        {
            Require(Path.GetFileName(module) == "dango_belt_room.dll" && File.Exists(module), "Specify the actual packaged room module.");
            Runtime = StackedDangoSceneRuntime.CreateBeltRoom(Path.GetFullPath(module));
            HwndSource? createdSurface = null;
            LocalRoomSceneDriver? createdDriver = null;
            try
            {
                Runtime.ConfigureBeltRoomAppearance(new AppearanceSettingsStore().Load(Path.Combine(root, "appearance.json")).Configuration, beginRoom: true);
                Runtime.BindAccessories(new AccessorySettingsSession(root));
                Require(Runtime.TryInitialize(), Runtime.LastError ?? "Native initialization failed.");
                View = new LocalRoomSceneView(); View.EnableWindowCamera();
                source = createdSurface = new HwndSource(new HwndSourceParameters("Fictional hidden room recovery verification")
                    { Width = 960, Height = 600, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = View };
                Require(!RecoveryIsWindowVisible(source.Handle), "Verification surface became visible.");
                View.Measure(new(960, 600)); View.Arrange(new(0, 0, 960, 600)); View.UpdateLayout();
                Require(View.IsVisible, "Hidden native surface must satisfy actual driver visibility conditions.");
                Driver = createdDriver = new LocalRoomSceneDriver(new DesktopSceneSession(), Runtime, frames, () => ticks, TimeSpan.TicksPerSecond);
                Driver.Failed += (_, failure) => failures.Add(failure);
                Driver.BindRoom(catalog, room, resume);
                Driver.Start(View, 1, enterWorld: true);
            }
            catch
            {
                createdDriver?.Dispose(); createdSurface?.Dispose(); Runtime.Dispose();
                throw;
            }
        }
        internal void Advance(int count)
        {
            for (var i = 0; i < count; i++)
            {
                ticks += HostFrameClock.FixedStep.Ticks; frames.Raise(ticks);
                Require(!RecoveryIsWindowVisible(source.Handle), "Hidden recovery surface became visible during advancement.");
                Require(Runtime.LastError is null && failures.Count == 0, Runtime.LastError ?? string.Join(" | ", failures));
            }
        }
        internal void WaitReady()
        {
            Advance(12);
            var bitmap = new RenderTargetBitmap(960, 600, 96, 96, PixelFormats.Pbgra32); bitmap.Render(View);
            var watch = Stopwatch.StartNew();
            while (!Driver.IsReady && watch.ElapsedMilliseconds < 3000)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame); Thread.Sleep(1);
            }
            Require(Driver.IsReady && View.HasHealthyFrame && Runtime.LastBeltRoomFrame is { Fault: 0, Tick: > 0 }, "Real hidden driver did not present a healthy advancing native frame.");
            Require(Driver.OwnsRuntime && Runtime.FrameDriver is HostFrameScheduler scheduler &&
                (ulong)scheduler.Snapshot.SuccessfulSteps == Runtime.LastBeltRoomFrame!.Tick,
                "The single frame owner and actual native step count diverged.");
        }
        internal void AttachFurniture()
        {
            // The first scheduler callback establishes its clock; attachment occurs in real Prepare.
            for (var i = 0; i < 4 && Runtime.Furniture?.IsAttached != true; i++) Advance(1);
            Require(Runtime.Furniture?.IsAttached == true, "The real driver did not attach the restored furniture.");
        }
        internal void VerifyAppearance(string assetId)
        {
            Require(Runtime.EffectiveAppearance == RecoveryAppearance, "Runtime did not use restored colors, eyes and body parameters.");
            Require(Runtime.AccessoriesEnabled && Runtime.AccessoryError is null, Runtime.AccessoryError ?? "Restored accessories were not enabled.");
            var accessory = Runtime.GetAccessoryFrames(Runtime.LastFrame!, 0).Single();
            Require(accessory.Item.AssetId == assetId && accessory.Texture.PixelWidth == 8 && accessory.Texture.PixelHeight == 8,
                "Actual room frame did not render the restored imported PNG accessory.");
        }
        internal object Evidence() => new { nativeTick = Runtime.LastBeltRoomFrame!.Tick,
            nativeFault = Runtime.LastBeltRoomFrame.Fault, ownerEpoch = Runtime.LastBeltRoomFrame.OwnerEpoch,
            successfulSteps = Driver.FrameDiagnostics.SuccessfulSteps, ready = Driver.IsReady, hidden = !RecoveryIsWindowVisible(source.Handle),
            camera = View.Camera.CaptureState(), appearance = Runtime.EffectiveAppearance, accessories = Runtime.GetAccessoryFrames(Runtime.LastFrame!, 0).Count };
        public void Dispose()
        {
            try
            {
                Driver.Dispose();
                Require(Runtime.FrameDriver is null && !Runtime.IsRunning, "Driver did not release the native owner.");
            }
            finally { source.Dispose(); Runtime.Dispose(); }
        }
    }
    private sealed class RecoveryFrames : IHostFrameSource
    {
        public event EventHandler? Rendering;
        internal void Raise(long ticks) => Rendering?.Invoke(this,
            (EventArgs)Activator.CreateInstance(typeof(RenderingEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { TimeSpan.FromTicks(ticks) }, null)!);
    }
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RecoveryIsWindowVisible(nint window);
}
