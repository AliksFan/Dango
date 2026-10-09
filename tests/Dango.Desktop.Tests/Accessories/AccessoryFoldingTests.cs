using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryFoldingTests
{
    private static AccessoryItem Item() => new("folding-test", "Fictional folding accessory", "builtin-sprout", 64, 64)
    {
        Softness = 100,
        ImagePivotX = 1,
        ImagePivotY = .5,
        BaseAngleDegrees = 90,
        GravityInfluence = 100,
        MaxDeformationPercent = 200,
    };

    [TestMethod]
    public void FoldingIsExplicitAndTheSameLoadActuallyFoldsOnlyWhenEnabled()
    {
        var normal = Settle(Item());
        var folded = Settle(Item() with { AllowFolding = true });
        Assert.IsFalse(normal.AllowFolding);
        Assert.IsTrue(folded.AllowFolding);
        Assert.IsTrue(Areas(normal).Min() >= .08, "The non-folding mode must preserve a meaningful area margin.");
        Assert.IsTrue(Areas(folded).Min() < -.05, "The enabled mode must really fold, rather than only accept a setting.");
        var solver = new AccessoryMotionSolver();
        for (var step = 0; step < 600; step++) solver.Update(step, 1d / 60, new(80, 80), 16, 16, Item() with { AllowFolding = true });
        var restored = solver.Refresh(new(80, 80), 16, 16, Item());
        Assert.IsTrue(Areas(restored).Min() >= .08, "Disabling folding must correct the current geometry without an extra step.");
        Assert.AreEqual(599, restored.StepIndex);
    }

    [TestMethod]
    [DataRow(0d, false)]
    [DataRow(5d, false)]
    [DataRow(25d, false)]
    [DataRow(100d, false)]
    [DataRow(200d, false)]
    [DataRow(0d, true)]
    [DataRow(5d, true)]
    [DataRow(25d, true)]
    [DataRow(100d, true)]
    [DataRow(200d, true)]
    public void UserDeformationLimitCapsEveryMaterialPointWithoutLimitingConnectionOrRotation(double percent, bool fold)
    {
        var item = Item() with
        {
            AllowFolding = fold,
            MaxDeformationPercent = percent,
            ConnectionStrength = 0,
            LockRotation = false,
            LimitSwing = false,
            ReturnStrength = 0,
            RotationDamping = 0,
        };
        var material = new AccessoryMotionSolver(); var rigid = new AccessoryMotionSolver();
        var limit = 16 * Math.Sqrt(2) * percent / 100;
        var largest = 0d; var sawConnection = false; var sawRotation = false;
        for (var step = 0; step < 180; step++)
        {
            var anchor = new Point(80 + 10 * Math.Sin(step * .2), 80 + 20 * Math.Cos(step * .14));
            var frame = material.Update(step, 1d / 60, anchor, 16, 16, item);
            var rigidFrame = rigid.Update(step, 1d / 60, anchor, 16, 16, item with { Softness = 0 });
            Assert.AreEqual(rigidFrame.ConnectionOffset, frame.ConnectionOffset);
            Assert.AreEqual(rigidFrame.RotationDegrees, frame.RotationDegrees);
            Assert.AreEqual(frame.ImagePivot, frame.Vertices[FindUv(frame, item.ImagePivotX, item.ImagePivotY)]);
            for (var vertex = 0; vertex < frame.Vertices.Count; vertex++)
            {
                var displacement = (frame.Vertices[vertex] - rigidFrame.Vertices[vertex]).Length;
                Assert.IsTrue(double.IsFinite(displacement) && displacement <= limit + 1e-8,
                    $"Material vertex {vertex} exceeds the {percent}% limit: {displacement} > {limit}.");
                largest = Math.Max(largest, displacement);
            }
            if (!fold) Assert.IsTrue(Areas(frame).Min() >= .08);
            sawConnection |= frame.ConnectionOffset.Length > 1;
            sawRotation |= Math.Abs(frame.RotationDegrees - item.BaseAngleDegrees) > 1;
        }
        Assert.IsTrue(sawConnection && sawRotation, "The fixture must really exercise the independent elastic and angular states.");
        if (percent is > 0 and <= 25) Assert.IsTrue(largest >= limit * .99, "A pressure fixture must actually reach the configured limit.");
        if (percent == 0) Assert.AreEqual(0d, largest);
    }

    [TestMethod]
    public void NewSettingsRoundTripAndOldManifestsLoadDefaultsWithoutBeingRewritten()
    {
        var root = NewRoot(); var store = new AccessorySettingsStore();
        var item = Item() with { AllowFolding = true, MaxDeformationPercent = 175, MaxConnectionTravelPercent = 150 };
        store.Save(root, new([item]));
        Assert.AreEqual(item, store.Load(root).Configuration.Items.Single());
        var path = AccessorySettingsStore.GetManifestPath(root);
        var legacy = JsonNode.Parse(File.ReadAllText(path))!;
        var legacyItem = legacy["items"]![0]!.AsObject();
        legacyItem.Remove("allowFolding"); legacyItem.Remove("maxDeformationPercent");
        legacyItem.Remove("maxConnectionTravelPercent");
        File.WriteAllText(path, legacy.ToJsonString());
        var before = File.ReadAllBytes(path);
        var loaded = store.Load(root);
        Assert.AreEqual(AccessorySettingsLoadState.Valid, loaded.State);
        Assert.IsFalse(loaded.Configuration.Items.Single().AllowFolding);
        Assert.AreEqual(100d, loaded.Configuration.Items.Single().MaxDeformationPercent);
        Assert.AreEqual(75d, loaded.Configuration.Items.Single().MaxConnectionTravelPercent);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
    }

    [TestMethod]
    [DataRow("allowFolding", "true")]
    [DataRow("maxDeformationPercent", "high")]
    [DataRow("maxDeformationPercent", -1)]
    [DataRow("maxDeformationPercent", 201)]
    [DataRow("maxConnectionTravelPercent", "far")]
    [DataRow("maxConnectionTravelPercent", -1)]
    [DataRow("maxConnectionTravelPercent", 201)]
    public void InvalidNewFieldsProtectTheManifest(string field, object value)
    {
        var root = NewRoot(); var store = new AccessorySettingsStore(); store.Save(root, new([Item()]));
        var path = AccessorySettingsStore.GetManifestPath(root); var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["items"]![0]![field] = value is string text ? JsonValue.Create(text) : JsonValue.Create((int)value);
        File.WriteAllText(path, json.ToJsonString()); var before = File.ReadAllBytes(path);
        Assert.AreEqual(AccessorySettingsLoadState.Protected, store.Load(root).State);
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(root, new([Item()])));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
    }

    [STATestMethod]
    public void RealEditorExposesAndAppliesFoldingAndTheNumericDeformationLimit()
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        session.Stage(new([Item() with { MaxDeformationPercent = 100 }])); Assert.IsTrue(session.TrySave());
        var panel = new AccessoryEditorPanel { Width = 260 }; panel.Bind(session);
        var scroll = new ScrollViewer { Content = panel };
        var window = new Window
        {
            Content = scroll,
            Width = 320,
            Height = 660,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "Fictional folding parameter verification",
        };
        try
        {
            window.Show(); Assert.IsTrue(panel.SelectSection(AccessoryEditorSection.Connection)); window.UpdateLayout();
            var check = Descendants<CheckBox>(panel).Single(control => AutomationProperties.GetName(control) == "允许翻折");
            var number = Descendants<TextBox>(panel).Single(control => AutomationProperties.GetName(control) == "最高允许形变量 % 数值");
            var travel = Descendants<TextBox>(panel).Single(control => AutomationProperties.GetName(control) == "连接最大行程 % 数值");
            Assert.IsTrue(check.IsVisible && check.IsEnabled && number.IsVisible && number.IsEnabled);
            Assert.IsFalse(check.IsChecked == true);
            number.Text = "175"; travel.Text = "150"; check.IsChecked = true;
            Assert.IsTrue(session.DraftConfiguration.Items.Single().AllowFolding);
            Assert.AreEqual(175d, session.DraftConfiguration.Items.Single().MaxDeformationPercent);
            Assert.AreEqual(150d, session.DraftConfiguration.Items.Single().MaxConnectionTravelPercent);
            panel.ApplyDraft(); Assert.IsFalse(session.IsDirty, panel.StatusMessage);
            var reloaded = new AccessorySettingsSession(root).SavedConfiguration.Items.Single();
            Assert.IsTrue(reloaded.AllowFolding); Assert.AreEqual(175d, reloaded.MaxDeformationPercent);
            Assert.AreEqual(150d, reloaded.MaxConnectionTravelPercent);
            panel.Edit(item => item with { AllowFolding = false, MaxDeformationPercent = 5 }); panel.CancelDraft();
            Assert.AreEqual(reloaded, session.DraftConfiguration.Items.Single());
            var reset = Descendants<Button>(panel.SaveActions).Single(button => AutomationProperties.GetName(button) == "复位当前配饰");
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsFalse(session.DraftConfiguration.Items.Single().AllowFolding);
            Assert.AreEqual(100d, session.DraftConfiguration.Items.Single().MaxDeformationPercent);
            Assert.AreEqual(75d, session.DraftConfiguration.Items.Single().MaxConnectionTravelPercent);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void FoldingTextureRenderSupportsReverseWindingWithoutChangingTextureCoordinates()
    {
        var frame = new AccessoryMotionSolver().Refresh(new(80, 80), 32, 32, Item() with { Softness = 0, BaseAngleDegrees = 0 });
        var reversed = frame.TriangleIndices.ToArray();
        for (var triangle = 0; triangle < reversed.Length; triangle += 3)
            (reversed[triangle + 1], reversed[triangle + 2]) = (reversed[triangle + 2], reversed[triangle + 1]);
        var texture = Texture([0x80800000u, 0xff00ff00u, 0x80000080u, 0u]);
        var forward = new AccessoryMeshVisual(); var backward = new AccessoryMeshVisual();
        forward.SetTexture(texture); backward.SetTexture(texture);
        forward.ApplyFrame(frame with { AllowFolding = true });
        var fold = frame with { TriangleIndices = Array.AsReadOnly(reversed), AllowFolding = true };
        backward.ApplyFrame(fold);
        CollectionAssert.AreEqual(Pixels(forward.CaptureBitmap()!), Pixels(backward.CaptureBitmap()!));
        Assert.ThrowsExactly<ArgumentException>(() => backward.ApplyFrame(fold with { AllowFolding = false }),
            "Changing folding policy must not bypass validation through the translation cache.");
        var batch = AccessoryMeshVisual.ApplyBatch([(backward, fold)]);
        Assert.HasCount(0, batch.Errors);
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void FoldingTextureRenderOverlapsRespectTransparency(bool transparent)
    {
        var positions = Array.AsReadOnly(new Point[] { new(0, 0), new(32, 0), new(0, 32), new(0, 0), new(32, 0), new(0, 32) });
        var uvs = Array.AsReadOnly(new Point[] { new(.125, .5), new(.125, .5), new(.125, .5), new(.875, .5), new(.875, .5), new(.875, .5) });
        var frame = new AccessoryMeshFrame(0, positions, uvs, Array.AsReadOnly(new[] { 0, 1, 2, 3, 5, 4 }),
            new(0, 0, 32, 32), new(), new(), 0)
        { AllowFolding = true };
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture([0x80800000u, 0u, 0u, transparent ? 0u : 0x80000080u]));
        visual.ApplyFrame(frame); var bitmap = visual.CaptureBitmap()!; var pixels = Pixels(bitmap);
        Assert.AreEqual(transparent ? 0x80800000u : 0xc0400080u, (uint)pixels[10 * bitmap.PixelWidth + 10]);
    }

    [STATestMethod]
    public void FoldingTextureRenderHasNoDoubleAlphaOnSharedGridEdges()
    {
        var frame = new AccessoryMotionSolver().Refresh(new(32, 32), 32, 32,
            Item() with { Softness = 0, BaseAngleDegrees = 0, AllowFolding = true });
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture([0x80800000u, 0x80800000u, 0x80800000u, 0x80800000u]));
        visual.ApplyFrame(frame); var bitmap = visual.CaptureBitmap()!; var pixels = Pixels(bitmap);
        for (var y = 2; y < 31; y++)
            for (var x = 2; x < 31; x++) Assert.AreEqual(128u, (uint)pixels[y * bitmap.PixelWidth + x] >> 24, $"Alpha seam at {x},{y}.");
    }

    [STATestMethod]
    public void MixedWindingFoldKeepsItsSharedMaterialEdgeVisibleExactlyOnce()
    {
        var frame = new AccessoryMeshFrame(0,
            Array.AsReadOnly(new Point[] { new(0, 0), new(0, 32), new(0, 32), new(32, 32) }),
            Array.AsReadOnly(new Point[] { new(0, 0), new(1, 0), new(0, 1), new(1, 1) }),
            Array.AsReadOnly(new[] { 0, 1, 3, 0, 3, 2 }), new(0, 0, 32, 32), new(), new(), 0)
        { AllowFolding = true };
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture([0x80800000u, 0x80800000u, 0x80800000u, 0x80800000u]));
        visual.ApplyFrame(frame); var bitmap = visual.CaptureBitmap()!; var pixels = Pixels(bitmap);
        for (var point = 2; point < 31; point++)
            Assert.AreEqual(128u, (uint)pixels[point * bitmap.PixelWidth + point] >> 24,
                $"Mixed-winding fold lost or duplicated its material seam at {point}.");
        Assert.AreEqual(192u, (uint)pixels[15 * bitmap.PixelWidth + 5] >> 24,
            "Actual overlapping interiors must still compose two translucent faces.");
    }

    private static AccessoryMeshFrame Settle(AccessoryItem item)
    {
        var solver = new AccessoryMotionSolver(); AccessoryMeshFrame? frame = null;
        for (var step = 0; step < 600; step++) frame = solver.Update(step, 1d / 60, new(80, 80), 16, 16, item);
        return frame!;
    }

    private static IEnumerable<double> Areas(AccessoryMeshFrame frame)
    {
        for (var triangle = 0; triangle < frame.TriangleIndices.Count; triangle += 3)
        {
            var a = frame.TriangleIndices[triangle]; var b = frame.TriangleIndices[triangle + 1]; var c = frame.TriangleIndices[triangle + 2];
            var uv = Vector.CrossProduct(frame.TextureCoordinates[b] - frame.TextureCoordinates[a], frame.TextureCoordinates[c] - frame.TextureCoordinates[a]);
            yield return Vector.CrossProduct(frame.Vertices[b] - frame.Vertices[a], frame.Vertices[c] - frame.Vertices[a]) / (uv * 16 * 16);
        }
    }

    private static int FindUv(AccessoryMeshFrame frame, double x, double y) =>
        Enumerable.Range(0, frame.TextureCoordinates.Count).Single(index => frame.TextureCoordinates[index] == new Point(x, y));

    private static string NewRoot() => Path.Combine(TestRepository.Root, "artifacts", "accessory-material-v5-verification", "cases", Guid.NewGuid().ToString("N"));

    private static BitmapSource Texture(uint[] colors)
    {
        var source = BitmapSource.Create(colors.Length, 1, 96, 96, PixelFormats.Pbgra32, null, colors.Select(color => (int)color).ToArray(), colors.Length * 4);
        source.Freeze(); return source;
    }

    private static int[] Pixels(BitmapSource bitmap)
    {
        var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
    }
}
