using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Diagnostics;
using Dango.Desktop.Rendering.Visuals;
using Path = System.IO.Path;
using VisualPath = System.Windows.Shapes.Path;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class PhysicsLabAppearanceTests
{
    [STATestMethod]
    public void SavedAppearanceAndOnlyEquippedAssetsAreLoadedWithoutWritingOrStartingAWorld()
    {
        using var fixture = new Fixture();
        var saved = new DangoAppearanceConfiguration(new(1, 2, 3), new(4, 5, 6), new(115, 25, 55, 20));
        new AppearanceSettingsStore().Save(Path.Combine(fixture.Root, "appearance.json"), saved);
        var configuration = new AccessoryConfiguration([
            new("back", "Fictional rear", "builtin-top-hat", 64, 64) { Layer = AccessoryLayer.Behind },
            new("front", "Fictional front", "builtin-sprout", 64, 64) { Layer = AccessoryLayer.Front },
            // A missing unequipped image must never be decoded or cause a wearing error.
            new("unused", "Fictional unused", new string('a', 64), 64, 64) { IsEquipped = false },
        ]);
        File.WriteAllText(AccessorySettingsStore.GetManifestPath(fixture.Root), JsonSerializer.Serialize(
            new { AccessorySchemaVersion = 1, configuration.Items }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter<AccessoryLayer>(JsonNamingPolicy.CamelCase) },
            }));
        var before = Directory.GetFiles(fixture.Root).ToDictionary(path => path, File.ReadAllBytes);
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings);
        presenter.Update(Frame(0));
        Assert.AreEqual(2, presenter.EquippedCount);
        Assert.AreEqual(2, presenter.RenderedAccessoryCount, presenter.Status);
        Assert.IsFalse(presenter.Status.Contains("失败", StringComparison.Ordinal));
        var view = (Canvas)presenter.View;
        Assert.AreEqual(160d, view.Width); Assert.AreEqual(120d, view.Height);
        Assert.AreEqual(1, ((Canvas)view.Children[0]).Children.Count);
        Assert.AreEqual(1, ((Canvas)view.Children[4]).Children.Count);
        Assert.AreEqual(Color.FromRgb(1, 2, 3), ((SolidColorBrush)((VisualPath)view.Children[1]).Fill).Color);
        Assert.AreEqual(Color.FromRgb(4, 5, 6), ((SolidColorBrush)((VisualPath)view.Children[2]).Fill).Color);
        Assert.AreEqual(before.Count, Directory.GetFiles(fixture.Root).Length);
        foreach (var (path, bytes) in before) CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        Assert.IsFalse(File.Exists(fixture.Settings));
    }

    [STATestMethod]
    public void MissingAndCorruptSnapshotsHaveExplicitStatusAndNeverInventAnEquippedAccessory()
    {
        using var fixture = new Fixture();
        using (var missing = new PhysicsLabAppearancePresenter(fixture.Settings))
        {
            missing.Update(Frame(0));
            Assert.AreEqual(0, missing.EquippedCount); Assert.AreEqual(0, missing.RenderedAccessoryCount);
            StringAssert.Contains(missing.Status, "未找到保存外观"); StringAssert.Contains(missing.Status, "未找到配饰清单");
            Assert.AreEqual(0, Directory.GetFiles(fixture.Root).Length);
        }
        File.WriteAllText(Path.Combine(fixture.Root, "appearance.json"), "not-json");
        File.WriteAllText(Path.Combine(fixture.Root, "accessories.json"), "not-json");
        using var corrupt = new PhysicsLabAppearancePresenter(fixture.Settings);
        Assert.AreEqual(0, corrupt.EquippedCount);
        StringAssert.Contains(corrupt.Status, "JsonReaderException");
        Assert.IsFalse(corrupt.Status.Contains(fixture.Root, StringComparison.Ordinal));
        Assert.AreEqual("not-json", File.ReadAllText(Path.Combine(fixture.Root, "accessories.json")));
    }

    [STATestMethod]
    public void DuplicateTicksDoNotAdvanceAndPauseResetClearAccessoryInertia()
    {
        using var fixture = new Fixture();
        new AccessorySettingsStore().Save(fixture.Root, new([
            new("flex", "Fictional flexible", "builtin-top-hat", 64, 64)
            { Softness = 100, ConnectionStrength = 0, LockRotation = false, GravityInfluence = 50 },
        ]));
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings);
        presenter.Update(Frame(0)); presenter.Update(Frame(1, .02)); presenter.Update(Frame(2, .10));
        var eyes = presenter.CurrentAttachments;
        var mesh = presenter.AccessoryFrames.Single();
        Assert.IsTrue(mesh.ConnectionOffset.Length > 0);
        presenter.Update(Frame(2, .30));
        Assert.AreSame(eyes, presenter.CurrentAttachments);
        Assert.AreSame(mesh, presenter.AccessoryFrames.Single());
        presenter.Paused = true;
        Assert.AreEqual(0, presenter.AccessoryFrames.Single().ConnectionOffset.Length, 1e-12);
        presenter.Update(Frame(3, .30));
        Assert.AreEqual(2L, presenter.CurrentAttachments!.StepIndex);
        presenter.Paused = false;
        presenter.Update(Frame(3, .30));
        Assert.AreEqual(3L, presenter.CurrentAttachments!.StepIndex);
        presenter.Reset();
        Assert.IsNull(presenter.CurrentAttachments); Assert.AreEqual(0, presenter.RenderedAccessoryCount);
        presenter.Update(Frame(0));
        Assert.AreEqual(0L, presenter.CurrentAttachments!.StepIndex);
    }

    [STATestMethod]
    public void ExactBodyBoundaryAndFormalFacingMotionUseTheSameCoordinateSystem()
    {
        using var fixture = new Fixture();
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings);
        var first = Frame(0, 4) with { Horizontal = 1 };
        presenter.Update(first);
        var center = presenter.CurrentAttachments!.Face.Left.Center.X;
        presenter.Update(Frame(1, 4) with { Horizontal = 1 });
        Assert.IsTrue(presenter.CurrentAttachments!.Face.Left.Center.X > center);
        var shape = presenter.CurrentAttachments.SourceShape!;
        Assert.AreEqual(256, shape.Boundary.Count);
        for (var i = 0; i < shape.Boundary.Count; i++)
        {
            Assert.AreEqual((first.Boundary[i].X - 4) * 72 / 160, shape.Boundary[i].X, 1e-12);
            Assert.AreEqual(first.Boundary[i].Y * 72 / 160, shape.Boundary[i].Y, 1e-12);
        }
        var body = (VisualPath)((Canvas)presenter.View).Children[1];
        var figure = body.Data.GetFlattenedPathGeometry().Figures.Single();
        // WPF StreamGeometry stores rendered coordinates as single-precision values.
        Assert.AreEqual(80 + shape.Boundary[0].X * 160, figure.StartPoint.X, 1e-4);
        Assert.AreEqual(66 - shape.Boundary[0].Y * 160, figure.StartPoint.Y, 1e-4);
    }

    [STATestMethod]
    public void SuccessfulIdlePhysicsTicksDriveTheFormalBlinkArtwork()
    {
        using var fixture = new Fixture();
        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings);
        var blinkObserved = false;
        var sample = Frame(0) with
        {
            Boundary = Enumerable.Range(0, 16).Select(index => new PhysicsLabBoundaryPoint(
                .7 * Math.Cos(index * 2 * Math.PI / 16), .7 * Math.Sin(index * 2 * Math.PI / 16))).ToArray(),
        };
        // The production idle scheduler blinks after .5 s stable + 4–8 s random delay.
        for (var tick = 0; tick < 1100; tick++)
        {
            presenter.Update(sample with { Tick = tick });
            if (presenter.CurrentAttachments!.Face.Left.Openness >= .9) continue;
            var eye = (VisualPath)((Canvas)presenter.View).Children[2];
            Assert.IsFalse(eye.Data is EllipseGeometry);
            Assert.IsTrue(eye.Data.GetArea() > 0);
            blinkObserved = true;
            break;
        }
        Assert.IsTrue(blinkObserved, "Formal idle blink must run from successful physics ticks alone.");
    }

    private static PhysicsLabAppearanceFrame Frame(long tick, double x = 0) => new(tick, 1d / 120,
        Enumerable.Range(0, 256).Select(index =>
        {
            var angle = index * 2 * Math.PI / 256;
            return new PhysicsLabBoundaryPoint(x + .7 * Math.Cos(angle), .7 * Math.Sin(angle));
        }).ToArray(), x, 0, 0, 0, true, 0, false, false, false, false);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Dango-Lab-Appearance-" + Guid.NewGuid().ToString("N"));
        public string Settings => Path.Combine(Root, "settings.json");
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
