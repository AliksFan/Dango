using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Furniture;

namespace Dango.LocalRoomProbe;

internal static partial class Program
{
    // Explicit opt-in, owned fictional storage and a hidden native surface only.
    // This never starts the application shell or captures the desktop.
    private static void CapturePublicPreview(string root, string module)
    {
        Require(!File.Exists(Path.Combine(root, "public-preview-result.json")), "Use a fresh preview root.");
        Require(!Directory.Exists(Path.Combine(root, "rooms")), "Preview requires unused fictional storage.");
        new AppearanceSettingsStore().Save(Path.Combine(root, "appearance.json"), RecoveryAppearance);
        var catalog = new LocalRoomCatalogStore(root);
        var current = catalog.Refresh();
        Require(current.Success, current.Warning ?? "Preview catalog failed.");
        var room = current.Catalog!.Rooms.Single();
        var layout = FurnitureLayout.ForItems(1, Guid.NewGuid(), [
            new(Guid.NewGuid(), FurnitureItemKind.StaticPlatform, new(1440, 150)),
            new(Guid.NewGuid(), FurnitureItemKind.RoundBall, new(1800, 66))]);
        using (var lease = catalog.CreateLayoutStore(room.RoomId).AcquireLease(catalog.LayoutPath(room.RoomId)))
        using (var stage = lease.Stage(layout, 0))
            Require(stage.Commit().State == FurnitureCommitState.Committed, "Preview layout failed.");
        using var rig = new RecoveryRig(root, module, catalog, room, null);
        rig.AttachFurniture();
        rig.WaitReady();
        rig.View.SetDangoName("Dango");
        rig.Advance(100);
        Capture("room-whole.png", RoomViewPreset.Whole);
        Capture("room-near.png", RoomViewPreset.Near);
        RecoveryReport(root, "public-preview-result.json", new {
            passed = true, visibleWindows = 0, fictional = true,
            scope = "Actual product scene offscreen rendering; hidden HwndSource, manual frame ticks; no application shell, desktop capture or input acceptance.",
            moduleSha256 = RecoveryHash(module), evidence = rig.Evidence(),
            files = new[] { "room-whole.png", "room-near.png" }
        });

        void Capture(string name, RoomViewPreset preset)
        {
            rig.View.SelectView(preset);
            rig.Advance(12);
            rig.WaitReady();
            Require(rig.View.IsVisible && rig.View.HasHealthyFrame, "Hidden product view must remain renderable.");
            rig.View.UpdateLayout();
            var bitmap = new RenderTargetBitmap(960, 600, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(rig.View);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(root, name));
            encoder.Save(output);
        }
    }
}
