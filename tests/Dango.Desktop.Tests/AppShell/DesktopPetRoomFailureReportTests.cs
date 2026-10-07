using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Tray;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class DesktopPetRoomFailureReportTests
{
    private const uint DisplayChange = 0x007E;
    private const string ReportName = "desktop-pet-last-failure.json";
    private static readonly string[] HealthFields =
    [
        "WallpaperDisposed", "WallpaperRootMatches", "SceneVisible", "SceneHealthy", "RuntimeFrameCurrent",
        "PetVisible", "PetHealthy", "PetFrameMatches", "RuntimeOwnerPresent", "RuntimeRunning", "RuntimeHealthy",
        "PointerCaptured", "PointerActive", "PhysicalDragActive",
    ];
    private static readonly string[] SchedulerCounters =
    [
        "Callbacks", "DuplicateCallbacks", "SuccessfulSteps", "AppliedFrames", "RefreshFrames",
        "Overloads", "LongPauses", "ClockResets", "Faults", "OverloadDroppedTicks", "PauseDroppedTicks",
        "FaultDroppedTicks", "LifecycleDroppedTicks", "RemainderTicks", "LastStartedTimestamp", "LastFinishedTimestamp",
    ];

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void OwnDisplayFailurePersistsOnlyBoundedDiagnosticsAndTrayActionRestoresTheSameEntity()
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Enter();
        var entity = rig.Runtime.EntityIds.Single();
        var window = rig.Window;
        var pet = window.Handle;
        var wallpaper = rig.Pet.WallpaperSource!.Handle;
        EstablishOwnCapture(rig, window);
        var before = DateTimeOffset.UtcNow;
        InvalidateOwnSurface(rig, pet);
        AssertFullyStopped(rig, window, pet, wallpaper);
        var occurred = AssertReport(rig, before, DateTimeOffset.UtcNow);
        Assert.AreEqual(rig.Pet.LastFailureSnapshot!.OccurredAtUtc, occurred,
            "The persisted report must retain the actual loss snapshot, rather than a later synthetic record.");
        RestoreThroughTrayAction(rig);
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        Assert.IsTrue(rig.Driver.OwnsRuntime);
        Assert.IsFalse(rig.Home.OwnsLiveFrameDriver);
        Assert.AreSame(rig.Runtime.LastFrame, rig.Pet.Scene.LastAppliedFrame);
        Assert.AreSame(rig.Pet.Scene.LastAppliedFrame, rig.Window.LastAppliedFrame);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void RepeatedOwnDisplayFailuresReplaceOneLatestReportInsteadOfAppendingHistory()
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Enter();
        var path = ReportPath(rig);
        var firstWindow = rig.Window;
        var firstPet = firstWindow.Handle;
        var firstWallpaper = rig.Pet.WallpaperSource!.Handle;
        var firstBefore = DateTimeOffset.UtcNow;
        InvalidateOwnSurface(rig, firstPet);
        AssertFullyStopped(rig, firstWindow, firstPet, firstWallpaper);
        var first = AssertReport(rig, firstBefore, DateTimeOffset.UtcNow);
        var firstText = File.ReadAllText(path);
        RestoreThroughTrayAction(rig);
        DesktopPetRoomFixture.WaitUntil(() => DateTimeOffset.UtcNow > first, rig.Runtime);
        var secondWindow = rig.Window;
        var secondPet = secondWindow.Handle;
        var secondWallpaper = rig.Pet.WallpaperSource!.Handle;
        var secondBefore = DateTimeOffset.UtcNow;
        InvalidateOwnSurface(rig, secondPet);
        AssertFullyStopped(rig, secondWindow, secondPet, secondWallpaper);
        var second = AssertReport(rig, secondBefore, DateTimeOffset.UtcNow);
        Assert.IsTrue(second > first, "A second real failure must replace the first UTC snapshot.");
        Assert.AreEqual(path, ReportPath(rig));
        Assert.AreNotEqual(firstText, File.ReadAllText(path));
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ReportWriteFailureCannotPreventDualSurfaceShutdownOrTrayRecovery()
    {
        using var rig = new DesktopPetRoomFixture();
        var path = ReportPath(rig);
        Assert.IsFalse(File.Exists(path));
        Assert.IsFalse(Directory.Exists(path), "The obstruction must belong only to this unique fixture.");
        Directory.CreateDirectory(path);
        try
        {
            rig.Enter();
            var entity = rig.Runtime.EntityIds.Single();
            var window = rig.Window;
            var pet = window.Handle;
            var wallpaper = rig.Pet.WallpaperSource!.Handle;
            InvalidateOwnSurface(rig, pet);
            AssertFullyStopped(rig, window, pet, wallpaper);
            Assert.IsFalse(File.Exists(path));
            Assert.IsTrue(Directory.Exists(path));
            Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(path).Count(),
                "A failed diagnostic write must not create alternate report content.");
            RestoreThroughTrayAction(rig);
            Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
            Assert.IsTrue(rig.Driver.OwnsRuntime);
            Assert.IsFalse(rig.Home.OwnsLiveFrameDriver);
        }
        finally
        {
            // Remove only the empty obstruction created above; keep all fixture evidence.
            Directory.Delete(path, recursive: false);
        }
    }

    private static void EstablishOwnCapture(DesktopPetRoomFixture rig, DesktopPetDangoWindow window)
    {
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        var start = rig.ToScene(new(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        var move = rig.ToScene(new(center.X + 9, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        nint ClientParameter(Point scenePoint)
        {
            var screen = rig.Pet.Scene.PointToScreen(scenePoint);
            var origin = window.PointToScreen(default(Point));
            var x = Math.Round(screen.X - origin.X);
            var y = Math.Round(screen.Y - origin.Y);
            Assert.IsTrue(x is >= short.MinValue and <= short.MaxValue && y is >= short.MinValue and <= short.MaxValue);
            return (nint)((uint)(ushort)(short)x | (uint)(ushort)(short)y << 16);
        }
        Assert.IsTrue(IsWindow(window.Handle));
        _ = SendMessage(window.Handle, 0x0201, 1, ClientParameter(start));
        Assert.IsTrue(window.IsPointerCaptured, "The owned pet surface must establish actual native capture first.");
        _ = SendMessage(window.Handle, 0x0200, 1, ClientParameter(move));
        Assert.IsTrue(rig.Driver.IsPointerActive);
        Assert.IsTrue(rig.Runtime.IsDragging);
    }

    private static void InvalidateOwnSurface(DesktopPetRoomFixture rig, nint pet)
    {
        Assert.AreEqual(rig.Window.Handle, pet);
        Assert.IsTrue(IsWindow(pet));
        // Target only this fixture's HWND; do not change the machine's display configuration.
        _ = SendMessage(pet, DisplayChange, 0, 0);
        DesktopPetRoomFixture.WaitUntil(() => rig.Host.LocalPresentation == LocalRoomPresentation.Hidden &&
            !rig.Pet.IsVisible && rig.Runtime.FrameDriver is null, rig.Runtime,
            diagnostic: () => rig.Pet.LastSurfaceFailure);
    }

    private static void AssertFullyStopped(DesktopPetRoomFixture rig, DesktopPetDangoWindow window, nint pet, nint wallpaper)
    {
        Assert.AreEqual(LocalRoomPresentation.Hidden, rig.Host.LocalPresentation);
        Assert.IsFalse(rig.Host.LocalHandoffPending);
        Assert.IsFalse(rig.Pet.IsVisible);
        Assert.IsFalse(rig.Pet.IsReady);
        Assert.IsFalse(window.IsPointerCaptured);
        Assert.IsFalse(window.InteractionEnabled);
        Assert.IsFalse(rig.Driver.IsPointerActive);
        Assert.IsFalse(rig.Runtime.IsDragging);
        Assert.IsFalse(rig.Runtime.IsRunning);
        Assert.IsNull(rig.Runtime.FrameDriver);
        Assert.IsFalse(rig.Driver.OwnsRuntime);
        Assert.IsFalse(IsWindow(pet));
        Assert.IsFalse(IsWindow(wallpaper));
        Assert.IsTrue(DesktopPetRoomFixture.Field<bool>(rig.Host, "localRoomSelected"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(rig.Host.LocalRoomFailure));
        var snapshot = rig.Pet.LastFailureSnapshot ?? throw new AssertFailedException("The actual loss snapshot must survive shutdown.");
        Assert.AreEqual("pet native display message: 0x007E", snapshot.Reason);
    }

    private static void RestoreThroughTrayAction(DesktopPetRoomFixture rig)
    {
        var factory = typeof(DangoApplicationHost).GetMethod("CreateLocalRoomTrayActions", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The product's actual tray-action factory must exist.");
        var actions = (LocalRoomTrayActions)factory.Invoke(rig.Host, null)!;
        Assert.IsTrue(actions.IsSelected());
        Assert.IsFalse(actions.IsVisible());
        Assert.IsTrue(actions.IsDesktopPet?.Invoke() == true);
        actions.Restore();
        Assert.IsTrue(rig.Host.LocalHandoffPending, "Recovery must require fresh joint-surface readiness.");
        Assert.IsFalse(rig.Window.InteractionEnabled);
        rig.WaitForEntry();
        Assert.IsTrue(actions.IsVisible());
    }

    private static DateTimeOffset AssertReport(DesktopPetRoomFixture rig, DateTimeOffset before, DateTimeOffset after)
    {
        var path = ReportPath(rig);
        Assert.IsTrue(File.Exists(path), "The actual LostDesktop path must persist its local report.");
        var bytes = File.ReadAllBytes(path);
        Assert.IsTrue(bytes.Length is > 0 and <= 8192, "The single latest report must remain bounded.");
        CollectionAssert.AreEquivalent(new[] { path }, Directory.GetFiles(Path.GetDirectoryName(path)!, "desktop-pet-*.json"),
            "The fixture must retain exactly one latest report, with no failure-history files.");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Assert.AreEqual(JsonValueKind.Object, root.ValueKind);
        CollectionAssert.AreEquivalent(HealthFields.Concat(new[]
        {
            "OccurredAtUtc", "Reason", "NativeErrorCode", "NativeMessage", "PetFrameRejection", "Scheduler",
        }).ToArray(), root.EnumerateObject().Select(property => property.Name).ToArray(),
            "Only fixed diagnostics are allowed; coordinates, pointer history, HWNDs and desktop content are excluded.");
        Assert.AreEqual(JsonValueKind.String, root.GetProperty("OccurredAtUtc").ValueKind);
        var occurred = root.GetProperty("OccurredAtUtc").GetDateTimeOffset();
        Assert.AreEqual(TimeSpan.Zero, occurred.Offset);
        Assert.IsTrue(occurred >= before && occurred <= after, "UTC must identify this real failure.");
        Assert.AreEqual("pet native display message: 0x007E", root.GetProperty("Reason").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("NativeErrorCode").ValueKind);
        Assert.AreEqual((int)DisplayChange, root.GetProperty("NativeMessage").GetInt32());
        Assert.AreEqual(string.Empty, root.GetProperty("PetFrameRejection").GetString());
        foreach (var field in HealthFields)
            Assert.IsTrue(root.GetProperty(field).ValueKind is JsonValueKind.True or JsonValueKind.False, field);
        Assert.IsFalse(root.GetProperty("WallpaperDisposed").GetBoolean());
        foreach (var field in new[] { "WallpaperRootMatches", "SceneVisible", "SceneHealthy", "PetVisible", "PetHealthy",
            "PetFrameMatches", "RuntimeOwnerPresent", "RuntimeRunning", "RuntimeHealthy" })
            Assert.IsTrue(root.GetProperty(field).GetBoolean(), $"{field} must describe the actual pre-shutdown snapshot.");
        var scheduler = root.GetProperty("Scheduler");
        Assert.AreEqual(JsonValueKind.Object, scheduler.ValueKind);
        CollectionAssert.AreEquivalent(SchedulerCounters.Append("IsRunning").ToArray(),
            scheduler.EnumerateObject().Select(property => property.Name).ToArray(),
            "Scheduler diagnostics may contain only aggregate counters and timing, never per-input samples.");
        foreach (var field in SchedulerCounters)
        {
            Assert.AreEqual(JsonValueKind.Number, scheduler.GetProperty(field).ValueKind, field);
            Assert.IsTrue(scheduler.GetProperty(field).TryGetInt64(out var count) && count >= 0, field);
        }
        Assert.IsTrue(scheduler.GetProperty("IsRunning").GetBoolean());
        Assert.IsTrue(scheduler.GetProperty("SuccessfulSteps").GetInt64() > 0);
        Assert.IsTrue(scheduler.GetProperty("AppliedFrames").GetInt64() > 0);
        return occurred;
    }

    private static string ReportPath(DesktopPetRoomFixture rig) => Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(DesktopPetRoomFixture.Field<string>(rig.Host, "settingsPath")))!, ReportName);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}
