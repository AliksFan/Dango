using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Furniture;

namespace Dango.LocalRoomProbe;

internal static class Program
{
    private const string Marker = ".dango-local-room-catalog-probe.json";
    private const string FurnitureHash = "BA6E92D57CAFBD5CC806F709890AA1E8ED784F97CF42A09F4168EE08065E97A0";
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("Specify mode and owned root.");
            var root = ValidateRoot(args[1], args[0] == "init");
            var store = new LocalRoomCatalogStore(root);
            switch (args[0])
            {
                case "init": break;
                case "write": Write(store); break;
                case "read": Read(store); break;
                case "dissolve":
                    Require(store.Dissolve(Guid.NewGuid(), Guid.Parse(args[2])).Success, "Concurrent dissolution failed."); break;
                case "seed-legacy":
                    var oldPath = Path.Combine(root, LegacyRoomImport.LegacyPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
                    var oldBytes = FurnitureLayoutSerializer.Serialize(new FurnitureLayout(7, Guid.NewGuid(), new(Guid.NewGuid(), new(1440, 120))), FurnitureLayoutContract.OrdinaryRoom);
                    File.WriteAllBytes(oldPath, oldBytes);
                    File.WriteAllText(Path.Combine(root, "legacy.sha256"), Convert.ToHexString(SHA256.HashData(oldBytes))); break;
                case "race":
                    File.WriteAllText(Path.Combine(root, args[2] + ".ready"), "ready");
                    WaitForFile(Path.Combine(root, "race.go"));
                    var result = store.Create(Guid.NewGuid(), Guid.NewGuid(), "虚构并发房", LocalRoomKind.Private, 6);
                    Console.WriteLine(JsonSerializer.Serialize(new { success = result.Success, result.Warning }));
                    return result.Success ? 0 : 3;
                case "read-race":
                    Require(store.Refresh().Catalog?.ActiveCount == 3, "Concurrent writers did not produce exactly three rooms."); break;
                case "pause-import":
                    _ = new LocalRoomCatalogStore(root, new PauseCatalogWriter(root)).Refresh();
                    throw new InvalidOperationException("Import pause unexpectedly returned.");
                case "read-import":
                    var prepared = RoomJson.Read<LegacyRoomImport>(File.ReadAllBytes(Path.Combine(root, "rooms/legacy-import.json")));
                    var imported = store.Refresh();
                    Require(imported.Success && imported.Catalog!.Rooms.Single().RoomId == prepared.RoomId, "Interrupted import changed identity.");
                    var oldSource = File.ReadAllBytes(Path.Combine(root, LegacyRoomImport.LegacyPath));
                    Require(Convert.ToHexString(SHA256.HashData(oldSource)) == File.ReadAllText(Path.Combine(root, "legacy.sha256")), "Import changed the original bytes.");
                    Require(store.ReadLayout(prepared.RoomId) == FurnitureLayoutSerializer.Deserialize(oldSource, FurnitureLayoutContract.OrdinaryRoom), "Import changed legacy content."); break;
                case "checkpoint-wait":
                    var id = store.Refresh().Catalog!.Rooms.First().RoomId;
                    var camera = new RoomCameraSnapshot(RoomCameraMode.Manual, RoomViewPreset.Custom, 125, 450, 70);
                    Require(new RoomResumeStore(store).Save(id, 1, new(600, 0), camera).State == FurnitureCommitState.Committed, "Checkpoint failed.");
                    File.WriteAllText(Path.Combine(root, "checkpoint.ready"), "ready");
                    Thread.Sleep(Timeout.Infinite); break;
                case "read-checkpoint":
                    var resume = new RoomResumeStore(store).Load(store.Refresh().Catalog!.Rooms.First().RoomId).Value;
                    Require(resume?.Position?.X == 600 && resume.Camera.RequestedPercent == 125, "Killed process lost its last committed checkpoint."); break;
                case "ui-check": Show(root, args[2], verify: true); break;
                case "show": Show(root, args[2], verify: false); break;
                default: throw new ArgumentException("Unknown probe mode.");
            }
            Console.WriteLine(JsonSerializer.Serialize(new { passed = true, mode = args[0] })); return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error.ToString()); return 1; }
    }
    private static string ValidateRoot(string value, bool initialize)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        if (repository is null) throw new InvalidDataException("Cannot locate source repository.");
        var allowed = Path.Combine(repository.FullName, "artifacts", "local-room-catalog", "runs") + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(value);
        if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Probe root must be inside this worktree's dedicated runs directory.");
        for (var current = new DirectoryInfo(root); current is not null && current.FullName.Length >= repository.FullName.Length; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Probe roots cannot traverse links.");
        if (initialize)
        {
            if (Directory.Exists(root)) throw new IOException("Use a fresh root; existing data is preserved.");
            Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, Marker), "{\"format\":1}");
        }
        if (!File.Exists(Path.Combine(root, Marker)) || File.ReadAllText(Path.Combine(root, Marker)) != "{\"format\":1}")
            throw new InvalidDataException("Missing owned probe marker.");
        return root;
    }
    private static void Write(LocalRoomCatalogStore store)
    {
        var initial = store.Refresh(); Require(initial.Success, initial.Warning ?? "Initialize failed.");
        var a = initial.Catalog!.Rooms.Single().RoomId; var b = Guid.NewGuid();
        Require(store.Create(Guid.NewGuid(), b, "虚构乙房", LocalRoomKind.Private, 6).Success, "Create failed.");
        WriteLayout(store, a, FurnitureItemKind.StaticPlatform, new(1440, 120));
        WriteLayout(store, b, FurnitureItemKind.RoundBall, new(1800, 66));
        var resume = new RoomResumeStore(store);
        Require(resume.Save(a, 1, new(500, 0), new(RoomCameraMode.Manual, RoomViewPreset.Custom, 120, 400, 75)).State == FurnitureCommitState.Committed, "A resume failed.");
        Require(resume.Save(b, 1, new(800, 0), new(RoomCameraMode.EdgeFollow, RoomViewPreset.Whole, 100, 20, 0)).State == FurnitureCommitState.Committed, "B resume failed.");
        Require(resume.SaveSelection(b).State == FurnitureCommitState.Committed, "Selection failed.");
    }
    private static void WriteLayout(LocalRoomCatalogStore store, Guid id, FurnitureItemKind kind, FurnitureAnchor anchor)
    {
        using var lease = store.CreateLayoutStore(id).AcquireLease(store.LayoutPath(id));
        using var stage = lease.Stage(FurnitureLayout.ForItems(1, Guid.NewGuid(), [new(Guid.NewGuid(), kind, anchor)]), 0);
        Require(stage.Commit().State == FurnitureCommitState.Committed, "Layout commit failed.");
    }
    private static void Read(LocalRoomCatalogStore store)
    {
        var current = store.Refresh(); Require(current.Success && current.Catalog!.ActiveCount == 2, "Catalog did not restore two rooms.");
        var rooms = current.Catalog!.Rooms; var resume = new RoomResumeStore(store);
        for (var i = 0; i < 2; i++)
        {
            var layout = store.ReadLayout(rooms[i].RoomId); var item = layout.Items.Single();
            Require(layout.LayoutVersion == 1 && item.Kind == (i == 0 ? FurnitureItemKind.StaticPlatform : FurnitureItemKind.RoundBall), "Rooms shared or lost furniture.");
            Require(item.Anchor == (i == 0 ? new FurnitureAnchor(1440, 120) : new(1800, 66)), "Anchor changed.");
            var saved = resume.Load(rooms[i].RoomId).Value;
            Require(saved?.Position?.X == (i == 0 ? 500 : 800) && saved.LayoutVersion == 1 &&
                saved.Camera.Mode == (i == 0 ? RoomCameraMode.Manual : RoomCameraMode.EdgeFollow), "Resume crossed identities or lost content.");
        }
        Require(resume.LoadSelection(out _) == rooms[1].RoomId, "Selection did not restore.");
    }
    private static void Show(string root, string native, bool verify)
    {
        native = Path.GetFullPath(native);
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native))) == FurnitureHash, "Native candidate differs from admitted furniture DLL.");
        using var singleton = new Mutex(true, "Local\\Dango.RoomCatalogReview." + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root))), out var created);
        Require(created, "This owned review root is already running.");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var host = new DangoApplicationHost(isolatedLocalRoomReview: true, settingsFile: Path.Combine(root, "settings.json"), nativeLibraryPath: native);
        host.Start();
        if (!verify) { app.Run(); return; }
        try
        {
            var home = ((ControlCenterWindow)typeof(DangoApplicationHost).GetField("controlCenterWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!).ProductHome;
            Pump(() => home.CatalogSnapshot is not null, "Catalog startup");
            Require(!home.IsDangoAway && !home.IsInPorch && host.LocalWindow?.IsVisible == false, "Startup automatically entered a room.");
            home.ResumePorch(); Pump(() => home.OwnsLiveFrameDriver && home.PorchScene.LiveBody.IsVisible, "Porch healthy frame");
            var catalog = home.CatalogSnapshot!; Require(catalog.ActiveCount >= 2, "UI verification needs the two-room fixture.");
            host.EnterCatalogRoom(catalog.Rooms[0].RoomId, false);
            host.ReturnToPorch();
            Pump(() => !host.LocalHandoffPending && home.OwnsLiveFrameDriver, "Cancel preparation");
            var cancelled = (Task)typeof(DangoApplicationHost).GetField("roomCheckpointTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            Require(cancelled.IsCompleted && host.LocalWindow!.IsVisible == false && !home.IsDangoAway, "A cancelled preparation changed presentation.");
            host.EnterCatalogRoom(catalog.Rooms[0].RoomId, false);
            var prepared = (Task)typeof(DangoApplicationHost).GetField("roomPreparationTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            Require(prepared.Wait(TimeSpan.FromSeconds(10)), "Target preparation did not finish before concurrent dissolution.");
            // This STA Dispatcher stays paused while an independent executable dissolves the target.
            var concurrent = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            concurrent.ArgumentList.Add("dissolve"); concurrent.ArgumentList.Add(root); concurrent.ArgumentList.Add(catalog.Rooms[0].RoomId.ToString("D"));
            using (var child = Process.Start(concurrent)!)
                Require(child.WaitForExit(10000) && child.ExitCode == 0, "Concurrent dissolution process did not exit successfully.");
            Pump(() => !host.LocalHandoffPending, "Dissolved target callback");
            Require(host.LocalPresentation == LocalRoomPresentation.Porch && !host.LocalWindow!.IsVisible && !home.IsDangoAway, "A concurrently dissolved target was entered.");
            Require(new LocalRoomCatalogStore(root).Restore(Guid.NewGuid(), catalog.Rooms[0].RoomId).Success, "Fixture restoration failed.");
            object? runtime = null; Dango.Physics.DangoEntityId? entity = null;
            foreach (var room in new[] { catalog.Rooms[0], catalog.Rooms[1], catalog.Rooms[0] })
            {
                host.EnterCatalogRoom(room.RoomId, false);
                Pump(() => host.LocalPresentation == LocalRoomPresentation.Window && !host.LocalHandoffPending && host.LocalWindow!.Scene.HasHealthyFrame, "Enter " + room.Name);
                var actual = host.LocalWindow!.Scene.LastAppliedRuntime!;
                if (runtime is null) { runtime = actual; entity = actual.AppearanceEntity; }
                Require(ReferenceEquals(runtime, actual) && entity is not null && actual.AppearanceEntity == entity, "Room handoff replaced the Dango identity or runtime.");
                Require(actual.Furniture!.PersistedLayout == new LocalRoomCatalogStore(root).ReadLayout(room.RoomId), "Wrong room layout attached.");
                Require(actual.FrameDriver is not null && !home.OwnsLiveFrameDriver && actual.DangoCount == 1 &&
                    host.LocalWindow.Scene.Camera.CameraMode == (room.RoomId == catalog.Rooms[0].RoomId ? RoomCameraMode.Manual : RoomCameraMode.EdgeFollow), "Owner or per-room camera incorrect.");
                var userCamera = host.LocalWindow.Scene.Camera.CaptureState();
                Require(host.LocalWindow.BeginFurnitureEditing(), "Furniture editor could not enter.");
                host.ReturnToPorch(); Pump(() => !host.LocalHandoffPending && home.OwnsLiveFrameDriver && host.LocalWindow.IsVisible == false, "Return to porch");
                Pump(() => ((Task)typeof(DangoApplicationHost).GetField("roomCheckpointTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!).IsCompleted, "Checkpoint flush");
                var saved = new RoomResumeStore(new LocalRoomCatalogStore(root)).Load(room.RoomId).Value!;
                Require(saved.Camera.Preset == userCamera.Preset && saved.Camera.RequestedPercent == userCamera.RequestedPercent, "Temporary furniture camera overwrote the room checkpoint.");
            }
            host.EnterDesktopPetRoom();
            Pump(() => host.LocalPresentation == LocalRoomPresentation.DesktopPet && !host.LocalHandoffPending, "Desktop pet healthy layered frame");
            host.ReturnToPorch(); Pump(() => !host.LocalHandoffPending && home.OwnsLiveFrameDriver, "Desktop pet return");
            File.WriteAllText(Path.Combine(root, "ui-check-result.json"), JsonSerializer.Serialize(new { passed = true, checks = "startup, cancelled preparation, concurrent dissolution, A-B-A identity/layout/camera, editor camera protection, unique owner, desktop-pet handoff", manualInput = false }));
        }
        finally { host.Dispose(); app.Shutdown(); }
    }
    private static void Pump(Func<bool> condition, string operation)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            var frame = new DispatcherFrame();
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        Require(condition(), "Timed out: " + operation);
    }
    private static void WaitForFile(string path)
    {
        var timer = Stopwatch.StartNew();
        while (!File.Exists(path) && timer.Elapsed < TimeSpan.FromSeconds(15)) Thread.Sleep(20);
        Require(File.Exists(path), "Probe barrier timed out.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
    private sealed class PauseCatalogWriter(string root) : IFurnitureFileWriter
    {
        private readonly FurnitureAtomicFileWriter atomic = new();
        public void WriteStaged(string path, byte[] bytes) => atomic.WriteStaged(path, bytes);
        public void Commit(string staged, string target, bool existed)
        {
            if (Path.GetFileName(target) == "catalog.json")
            { File.WriteAllText(Path.Combine(root, "import.ready"), "ready"); Thread.Sleep(Timeout.Infinite); }
            atomic.Commit(staged, target, existed);
        }
    }
}
