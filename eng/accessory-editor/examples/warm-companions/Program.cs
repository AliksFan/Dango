using System.Globalization;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.Accessories.Rig;
using Point = System.Windows.Point;

namespace WarmCompanions;

// Original vector artwork. All generation is offscreen; no Window/Application/input hook is created.
internal static class Program
{
    private const string Brown = "#77513F", Fur = "#D8A47B", Cream = "#FFF0D6", Pink = "#E9AD9D", Coral = "#C97868";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    private sealed record Shape(string D, string Fill);
    private sealed record Layer(string Id, string Name, string GroupId, Shape[] Shapes);
    private sealed record Design(string Id, string Name, int Width, int Height, Point Pivot, Layer[] Layers,
        AccessorySkeletonGroup[] Groups, AccessoryLayer WearingLayer, double Size, double AnchorX, double AnchorY);
    private sealed record Generated(Design Design, AccessoryEditorProject Project, AccessoryImageAsset Image, AccessoryItem Item);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length is not (1 or 3) || args.Length == 3 && args[1] != "--append-test-library")
                throw new ArgumentException("Usage: WarmCompanions <output-directory> [--append-test-library <fictional-settings-root>]");
            var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            var generated = Designs().Select(d => Generate(d, output)).ToArray();
            var validation = generated.Select(g => ValidateAndSimulate(g, output)).ToArray();
            WriteSheet(generated, Path.Combine(output, "collection-preview.png"));
            WriteWearingSketch(generated, Path.Combine(output, "wearing-preview.png"));
            File.WriteAllText(Path.Combine(output, "validation.json"), JsonSerializer.Serialize(new { schema = 1,
                nativeProjectVersion = AccessoryEditorProject.CurrentVersion, framesPerItem = 600, secondsPerItem = 5,
                desktopAssemblySha256 = Hash(File.ReadAllBytes(typeof(AccessoryEditorProject).Assembly.Location)),
                items = validation, boundary = "Native project/PNG/solver/skin/offscreen verification only. Foreground wearing and human acceptance remain unverified." }, Json));
            if (args.Length == 3) Append(generated, Path.GetFullPath(args[2]));
            Console.WriteLine($"Generated and validated {generated.Length} original editable accessories in {output}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static Shape S(string d, string fill) => new(d, fill);
    private static AccessorySkeletonGroup G(string id, string name, double bend, double damping, double restore, double gravity,
        (double X, double Y)[] points, string? target = null, double elasticity = 0) => new(id, name, target,
            points.Select((p, i) => new AccessorySkeletonNode(id + "-" + i, p.X, p.Y)).ToArray(), new(bend, damping, restore, gravity, elasticity));

    private static Design[] Designs() =>
    [
        new("warm-cat-ears", "奶糖猫耳", 512, 224, new(256, 208),
        [
            new("ear-left-fur", "左耳·奶糖绒", "ear-left", [S("M 57,195 C 45,158 48,79 65,25 C 72,7 86,16 104,28 C 148,60 183,112 204,176 C 170,188 118,197 57,195 Z", Fur)]),
            new("ear-left-inner", "左耳·柔粉耳心", "ear-left", [S("M 79,162 C 70,121 73,76 79,52 C 112,76 146,117 162,158 C 134,166 109,168 79,162 Z", Pink)]),
            new("ear-right-fur", "右耳·奶糖绒", "ear-right", [S("M 455,195 C 467,158 464,79 447,25 C 440,7 426,16 408,28 C 364,60 329,112 308,176 C 342,188 394,197 455,195 Z", Fur)]),
            new("ear-right-inner", "右耳·柔粉耳心", "ear-right", [S("M 433,162 C 442,121 439,76 433,52 C 400,76 366,117 350,158 C 378,166 403,168 433,162 Z", Pink)]),
        ], [G("ear-left", "左耳", .12, 12, 18, 0, [(129,184),(105,111),(78,34)]), G("ear-right", "右耳", .12, 12, 18, 0, [(383,184),(407,111),(434,34)])], AccessoryLayer.Front, 160, .5, .065),
        new("warm-cat-tail", "奶糖卷尾", 256, 384, new(48, 328),
        [
            new("tail-fur", "尾巴·奶糖绒", "tail", [S("M 43,347 C 88,339 147,310 182,267 C 216,226 230,168 210,122 C 194,85 165,69 135,73 C 102,77 84,102 92,126 C 99,148 123,155 137,140 C 149,127 142,115 134,113 C 157,111 170,127 177,149 C 191,195 166,238 137,267 C 108,296 76,306 43,309 C 25,311 26,344 43,347 Z", Fur)]),
            new("tail-tip", "尾尖·奶油帽", "tail", [S("M 136,73 C 104,76 84,102 92,126 C 99,148 123,155 137,140 C 149,127 142,115 134,113 C 148,112 158,119 165,129 C 171,111 176,105 176,101 C 162,80 149,74 136,73 Z", Cream)]),
        ], [G("tail", "卷尾柔性链", .24, 8, 11, 3, [(48,328),(103,307),(153,271),(185,224),(199,175),(190,133),(165,102),(132,98),(114,120)], elasticity: .015)], AccessoryLayer.Behind, 155, .87, .68),
        new("warm-ribbon", "莓杏蝴蝶结", 512, 384, new(256, 154),
        [
            new("ribbon-left", "左飘带", "ribbon-left", [S("M 243,166 C 229,211 218,266 198,330 L 161,303 L 122,313 C 159,252 182,206 208,160 Z", Coral)]),
            new("ribbon-right", "右飘带", "ribbon-right", [S("M 269,166 C 283,211 294,266 314,330 L 351,303 L 390,313 C 353,252 330,206 304,160 Z", Coral)]),
            new("wing-left", "左结翼", "wing-left", [S("M 239,138 C 194,112 138,50 91,60 C 66,66 63,95 69,125 C 62,157 68,197 92,205 C 133,218 196,180 240,170 Z", Coral)]),
            new("wing-right", "右结翼", "wing-right", [S("M 273,138 C 318,112 374,50 421,60 C 446,66 449,95 443,125 C 450,157 444,197 420,205 C 379,218 316,180 272,170 Z", Coral)]),
            new("knot", "中心结", "knot", [S("M 234,126 C 246,120 267,120 278,126 C 285,142 285,165 278,181 C 264,188 247,188 234,181 C 227,165 227,142 234,126 Z", Pink)]),
        ], [G("ribbon-left", "左飘带", .22, 9, 10, 8, [(256,154),(207,234),(177,309)], "knot-0"),
            G("ribbon-right", "右飘带", .22, 9, 10, 8, [(256,154),(299,234),(335,309)], "knot-0"),
            G("wing-left", "左结翼", .10, 12, 18, 1, [(256,154),(164,139),(88,130)], "knot-0"),
            G("wing-right", "右结翼", .10, 12, 18, 1, [(256,154),(348,139),(424,130)], "knot-0"),
            G("knot", "中心固定结", 0, 15, 25, 0, [(256,154)])], AccessoryLayer.Front, 100, .5, .70)
    ];

    private static Generated Generate(Design design, string output)
    {
        var folder = Path.Combine(output, design.Id); Directory.CreateDirectory(folder);
        var assets = new Dictionary<string, AccessoryEditorSourceAsset>();
        var layers = new List<AccessoryProjectLayer>(); var pieces = new List<AccessoryProjectRenderPiece>();
        foreach (var layer in design.Layers)
        {
            var source = AccessoryEditorSourceAsset.FromBitmap(Render(design.Width, design.Height, dc => Draw(dc, layer.Shapes)));
            assets.TryAdd(source.AssetId, source); File.WriteAllBytes(Path.Combine(folder, layer.Id + ".png"), source.CopyPngBytes());
            File.WriteAllText(Path.Combine(folder, layer.Id + ".svg"), Svg(design, layer.Shapes));
            layers.Add(new(layer.Id, layer.Name, source.AssetId, 0, 0, design.Width, design.Height,
                OriginalWidth: design.Width, OriginalHeight: design.Height));
        }
        foreach (var group in design.Layers.GroupBy(l => l.GroupId))
        {
            var source = AccessoryEditorSourceAsset.FromBitmap(Render(design.Width, design.Height, dc => Draw(dc, group.SelectMany(l => l.Shapes))));
            assets.TryAdd(source.AssetId, source); pieces.Add(new(group.Key, source.AssetId));
        }
        var finalSource = AccessoryEditorSourceAsset.FromBitmap(Render(design.Width, design.Height, dc => Draw(dc, design.Layers.SelectMany(l => l.Shapes))));
        var final = AccessoryImageAsset.FromRenderedPng(finalSource.CopyPngBytes());
        if (Hash(finalSource.CopyPngBytes()) != final.AssetId) throw new InvalidDataException("PNG normalization was not stable.");
        File.WriteAllBytes(Path.Combine(folder, "final.png"), finalSource.CopyPngBytes());
        File.WriteAllText(Path.Combine(folder, "source.svg"), Svg(design, design.Layers.SelectMany(l => l.Shapes)));
        var skeleton = new AccessorySkeletonConfiguration(new("P", design.Pivot.X, design.Pivot.Y), design.Groups,
            design.Layers.Select(l => new AccessorySkeletonLayer(l.Id, l.GroupId)).ToArray());
        var project = new AccessoryEditorProject(design.Width, design.Height, 1, final.AssetId, layers, assets.Values, skeleton, design.Name, pieces);
        var item = new AccessoryItem("example-" + design.Id, design.Name, final.AssetId, design.Width, design.Height)
        {
            ProjectId = project.ProjectId, ProjectVersion = project.Version, EditRevision = 1,
            DeformationMode = AccessoryDeformationMode.Skeleton, Skeleton = skeleton,
            IsEquipped = true, Layer = design.WearingLayer, Order = design.WearingLayer == AccessoryLayer.Behind ? 0 : design.Id == "warm-ribbon" ? 2 : 1,
            SizePercent = design.Size, ImagePivotX = design.Pivot.X / design.Width, ImagePivotY = design.Pivot.Y / design.Height,
            BodyAnchorX = design.AnchorX, BodyAnchorY = design.AnchorY,
            ConnectionStrength = 100, MaxConnectionTravelPercent = 10, RotationDamping = 85,
            LockRotation = true, ReturnStrength = 90, MaxSwingDegrees = 12, LimitSwing = true, GravityInfluence = 0,
        };
        project.ValidateResult(item, final);
        File.WriteAllBytes(Path.Combine(folder, "project.json"), project.CopyBytes());
        File.WriteAllText(Path.Combine(folder, "item.json"), JsonSerializer.Serialize(item, Json));
        return new(design, project, final, item);
    }

    private static object ValidateAndSimulate(Generated g, string output)
    {
        var read = AccessoryEditorProject.Read(g.Project.CopyBytes(), g.Project.ProjectId); read.ValidateResult(g.Item, g.Image);
        var skins = read.RenderPieces.Select(p => new AccessorySkeletonSkin(read.Skeleton, read.PixelWidth, read.PixelHeight, p.GroupId)).ToArray();
        var solver = new AccessorySkeletonSolver(read.Skeleton, read.PixelWidth, read.PixelHeight);
        var pose = new AccessoryRigPose(new(300, 210), 0, read.PixelHeight * .5);
        solver.Reset(read.Skeleton, read.PixelWidth, read.PixelHeight, pose);
        var rest = solver.Refresh(); var maxRelativeMotion = 0d; var maxLengthError = 0d;
        var sampleFrames = new List<AccessorySkeletonSnapshot> { rest };
        for (var i = 1; i <= 600; i++)
        {
            var t = i / 120d; var active = t < 3;
            var root = new Point(300 + (active ? 18 * Math.Sin(t * 4) : 0), 210 + (active ? 10 * Math.Sin(t * 6) : 0));
            var angle = active ? .14 * Math.Sin(t * 3) : 0;
            solver.Advance(i, 1d / 120, new(root, angle, pose.Scale)); var snapshot = solver.Refresh();
            for (var j = 0; j < skins.Length; j++)
            {
                var mesh = skins[j].Deform(snapshot);
                if (mesh.Vertices.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) throw new InvalidDataException("Nonfinite skin.");
            }
            foreach (var group in read.Skeleton.Groups)
            {
                var nodes = snapshot.Groups[group.Id];
                for (var n = 0; n < nodes.Count; n++)
                {
                    var local = new Vector((group.Nodes[n].X - read.Skeleton.Pivot.X) / read.PixelHeight,
                        (group.Nodes[n].Y - read.Skeleton.Pivot.Y) / read.PixelHeight) * pose.Scale;
                    var rotated = new Vector(local.X * Math.Cos(angle) - local.Y * Math.Sin(angle), local.X * Math.Sin(angle) + local.Y * Math.Cos(angle));
                    maxRelativeMotion = Math.Max(maxRelativeMotion, (nodes[n] - (root + rotated)).Length);
                    if (n > 0)
                    {
                        var expected = new Vector(group.Nodes[n].X - group.Nodes[n - 1].X, group.Nodes[n].Y - group.Nodes[n - 1].Y).Length / read.PixelHeight * pose.Scale;
                        var allowed = expected * group.Parameters.Elasticity;
                        maxLengthError = Math.Max(maxLengthError, Math.Max(0, Math.Abs((nodes[n] - nodes[n - 1]).Length - expected) - allowed));
                    }
                }
                if (group.TargetId is { } target)
                {
                    var owner = read.Skeleton.Groups.Single(x => x.Nodes.Any(n => n.Id == target));
                    var index = owner.Nodes.Select((n, k) => (n.Id, k)).Single(x => x.Id == target).k;
                    if ((nodes[0] - snapshot.Groups[owner.Id][index]).Length > 1e-9) throw new InvalidDataException("Disconnected root.");
                }
            }
            if (i is 145 or 300 or 600) sampleFrames.Add(snapshot);
        }
        if (maxRelativeMotion < .01 || maxLengthError > 1) throw new InvalidDataException("Simulation did not cover finite flexible response or length limits.");
        var repeated = solver.Refresh();
        if (repeated.Tick != 600 || solver.Advance(600, 1d / 120, repeated.Pose)) throw new InvalidDataException("Refresh/duplicate tick ownership violated.");
        var preview = Render(1200, 440, dc =>
        {
            dc.DrawRectangle(Brush("#FDFCF9"), null, new(0, 0, 1200, 440));
            for (var i = 0; i < sampleFrames.Count; i++)
            {
                dc.PushTransform(new TranslateTransform(i * 300 - 150, 0));
                foreach (var pair in read.RenderPieces.Select((p, k) => (p, k)))
                    dc.DrawImage(RasterMesh(read.Assets[pair.p.AssetId].Bitmap, skins[pair.k].Deform(sampleFrames[i]), 600, 410), new(0, 0, 600, 410));
                dc.Pop(); Text(dc, new[] { "静止", "运动 1.21 s", "运动 2.50 s", "回稳 5.00 s" }[i], i * 300 + 20, 410, 16);
            }
        });
        Save(preview, Path.Combine(output, g.Design.Id, "motion-preview.png"));
        return new { g.Item.Id, g.Item.Name, projectId = read.ProjectId, assetId = g.Item.AssetId,
            layerCount = read.Layers.Count, groupCount = read.Skeleton.Groups.Count,
            nodeCount = read.Skeleton.Groups.Sum(x => x.Nodes.Count) + 1, renderPieces = read.RenderPieces.Count,
            projectRoundtrip = true, validateResult = true, frames = 600, finiteMeshes = true, attachmentsCoincident = true,
            maxRelativeMotionPixels = Math.Round(maxRelativeMotion, 6), maxLengthErrorBeyondElasticityPixels = Math.Round(maxLengthError, 6),
            duplicateTickRejected = true };
    }

    private static void Append(Generated[] generated, string root)
    {
        // An explicit caller-created marker prevents accidental default-profile writes.
        var marker = Path.Combine(root, "dango-fictional-test-root.marker");
        if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != "fictional-accessory-test-data-only")
            throw new InvalidDataException("Append requires the documented fictional-test-root marker; no library was written.");
        var store = new AccessorySettingsStore(); var loaded = store.Load(root);
        if (loaded.State is not (AccessorySettingsLoadState.Valid or AccessorySettingsLoadState.Missing)) throw new InvalidDataException("Library protected or legacy; cannot append.");
        var previous = loaded.Configuration.Items.ToArray();
        var additions = generated.Where(g => !previous.Any(i => i.Id == g.Item.Id)).ToArray();
        var room = AccessoryConfiguration.MaximumEquippedItems - previous.Count(i => i.IsEquipped);
        var items = previous.Concat(additions.Select((g, i) => g.Item with { IsEquipped = i < room })).ToArray();
        if (additions.Length == 0) { Console.WriteLine("Examples already present; existing entries preserved."); return; }
        store.Save(root, new(items), additions.ToDictionary(g => g.Image.AssetId, g => g.Image),
            additions.ToDictionary(g => g.Project.ProjectId, g => g.Project), loaded.ManifestRevision ?? "missing");
        var reloaded = store.Load(root);
        if (reloaded.State != AccessorySettingsLoadState.Valid || previous.Any(i => !reloaded.Configuration.Items.Contains(i)))
            throw new InvalidDataException("Append preservation verification failed.");
        foreach (var g in additions) store.LoadProject(root, g.Project.ProjectId).ValidateResult(
            reloaded.Configuration.Items.Single(i => i.Id == g.Item.Id), store.LoadImage(root, g.Image.AssetId));
        Console.WriteLine($"Appended {additions.Length}; preserved {previous.Length} prior entries. Reopen the independent test app to reload.");
    }

    private static void WriteSheet(Generated[] gs, string file)
    {
        var sheet = Render(1500, 760, dc =>
        {
            dc.DrawRectangle(Brush("#FDFCF9"), null, new(0, 0, 1500, 760));
            Text(dc, "奶糖小伙伴 · 原创可编辑配饰", 40, 25, 25);
            for (var i = 0; i < gs.Length; i++)
            {
                var g = gs[i]; var x = i * 500d;
                dc.DrawRoundedRectangle(Brush("#F4EDE3"), null, new(x + 25, 85, 450, 470), 22, 22);
                var factor = Math.Min(400d / g.Design.Width, 370d / g.Design.Height);
                dc.DrawImage(g.Image.Bitmap, new(x + 250 - g.Design.Width * factor / 2, 110, g.Design.Width * factor, g.Design.Height * factor));
                Text(dc, g.Item.Name, x + 50, 505, 23);
                Text(dc, $"{g.Project.Layers.Count} 图层 / {g.Project.Skeleton.Groups.Count} 骨架组", x + 50, 575, 18);
                Text(dc, g.Item.Layer == AccessoryLayer.Behind ? "身体后层 · 柔性卷尾" : "身体前层 · 独立可调", x + 50, 610, 18);
            }
            Text(dc, "PNG 为真实分层资产；工程含骨架、参数和分组绘制片。下方参数说明与动态预览见各配饰目录。", 40, 700, 18);
        }); Save(sheet, file);
    }

    private static void WriteWearingSketch(Generated[] generated, string file)
    {
        Save(Render(1000, 780, dc =>
        {
            dc.DrawRectangle(Brush("#FDFCF9"), null, new(0, 0, 1000, 780));
            Text(dc, "佩戴尺寸与前后层示意", 40, 30, 25);
            Text(dc, "原创圆形身体用于比例说明；正式团子外观与实时佩戴仍需在应用内验收。", 40, 80, 18);
            dc.PushTransform(new TranslateTransform(230, 280)); dc.PushTransform(new ScaleTransform(3, 3));
            // Elliptical 160 × 132 DIP rest body. X follows each horizontal body section, as the native mapper does.
            Point Anchor(AccessoryItem item)
            {
                var y = item.BodyAnchorY * 132; var half = 80 * Math.Sqrt(Math.Max(0, 1 - Math.Pow((y - 66) / 66, 2)));
                return new(80 - half + 2 * half * item.BodyAnchorX, y);
            }
            void Accessory(Generated g)
            {
                var longest = 64 * g.Item.SizePercent / 100; var scale = longest / Math.Max(g.Design.Width, g.Design.Height);
                var anchor = Anchor(g.Item);
                dc.DrawImage(g.Image.Bitmap, new(anchor.X - g.Design.Pivot.X * scale, anchor.Y - g.Design.Pivot.Y * scale,
                    g.Design.Width * scale, g.Design.Height * scale));
            }
            foreach (var g in generated.Where(g => g.Item.Layer == AccessoryLayer.Behind)) Accessory(g);
            dc.DrawEllipse(Brush("#FFF1D9"), new(Brush(Brown), 1.4), new(80,66), 80,66);
            dc.DrawEllipse(Brush(Brown), null, new(57,64), 3,4); dc.DrawEllipse(Brush(Brown), null, new(103,64),3,4);
            dc.DrawEllipse(Brush("#EFC1AE"), null, new(39,78), 9,4); dc.DrawEllipse(Brush("#EFC1AE"), null, new(121,78),9,4);
            dc.DrawGeometry(null, new(Brush(Brown),1.4), Geometry.Parse("M 74,77 Q 80,82 86,77"));
            foreach (var g in generated.Where(g => g.Item.Layer == AccessoryLayer.Front).OrderBy(g => g.Item.Order)) Accessory(g);
            dc.Pop(); dc.Pop();
            Text(dc, "猫耳 160% · 身体 (0.50, 0.065) · 前层", 40, 675, 18);
            Text(dc, "卷尾 155% · 身体 (0.87, 0.68) · 后层     蝴蝶结 100% · 身体 (0.50, 0.70) · 前层", 40, 715, 18);
        }), file);
    }

    private static void Draw(DrawingContext dc, IEnumerable<Shape> shapes)
    {
        foreach (var shape in shapes)
        {
            dc.DrawGeometry(Brush(shape.Fill), null, Geometry.Parse(shape.D));
        }
    }
    private static BitmapSource Render(int width, int height, Action<DrawingContext> paint)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) paint(dc);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromInvariantString(color)!;
    private static void Save(BitmapSource image, string path)
    { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); }
    private static void Text(DrawingContext dc, string text, double x, double y, double size) => dc.DrawText(new FormattedText(text,
        CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), size, Brush("#77513F"), 1), new(x,y));
    private static string Svg(Design design, IEnumerable<Shape> shapes) => $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{design.Width}\" height=\"{design.Height}\" viewBox=\"0 0 {design.Width} {design.Height}\">\n" +
        string.Join("\n", shapes.Select(s => $"  <path d=\"{SecurityElement.Escape(s.D)}\" fill=\"{s.Fill}\" stroke=\"none\"/>")) + "\n</svg>\n";
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    // Rasterizes the exact native skin output with one pixel write per triangle, avoiding antialiased clip seams.
    private static BitmapSource RasterMesh(BitmapSource bitmap, AccessoryMeshFrame mesh, int width, int height)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var source = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(source, converted.PixelWidth * 4, 0);
        var target = new byte[width * height * 4];
        for (var i = 0; i < mesh.TriangleIndices.Count; i += 3)
        {
            var ia = mesh.TriangleIndices[i]; var ib = mesh.TriangleIndices[i + 1]; var ic = mesh.TriangleIndices[i + 2];
            var a = mesh.Vertices[ia]; var b = mesh.Vertices[ib]; var c = mesh.Vertices[ic];
            var sa = mesh.TextureCoordinates[ia]; var sb = mesh.TextureCoordinates[ib]; var sc = mesh.TextureCoordinates[ic];
            sa = new(sa.X * bitmap.PixelWidth, sa.Y * bitmap.PixelHeight); sb = new(sb.X * bitmap.PixelWidth, sb.Y * bitmap.PixelHeight); sc = new(sc.X * bitmap.PixelWidth, sc.Y * bitmap.PixelHeight);
            var u = b - a; var v = c - a; var det = u.X * v.Y - u.Y * v.X;
            if (Math.Abs(det) < 1e-10) continue;
            var minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
            var maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            var minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
            var maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            for (var y = minY; y <= maxY; y++) for (var x = minX; x <= maxX; x++)
            {
                var p = new Point(x + .5, y + .5) - a;
                var s = (p.X * v.Y - p.Y * v.X) / det; var t = (u.X * p.Y - u.Y * p.X) / det;
                if (s < -1e-8 || t < -1e-8 || s + t > 1 + 1e-8) continue;
                var uv = sa + (sb - sa) * s + (sc - sa) * t;
                var sx = Math.Clamp((int)uv.X, 0, bitmap.PixelWidth - 1); var sy = Math.Clamp((int)uv.Y, 0, bitmap.PixelHeight - 1);
                var si = (sy * bitmap.PixelWidth + sx) * 4; var di = (y * width + x) * 4;
                Array.Copy(source, si, target, di, 4);
            }
        }
        var result = BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,target,width*4); result.Freeze(); return result;
    }
}
