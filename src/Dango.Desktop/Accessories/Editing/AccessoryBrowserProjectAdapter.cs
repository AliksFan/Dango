using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Dango.Desktop.Accessories.Editing;

/// <summary>A modal-scoped adapter. Browser aliases are never persisted as native asset identities.</summary>
public sealed class AccessoryBrowserProjectAdapter
{
    private readonly Dictionary<int, string> layerIds = [];
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 24 };

    public JsonElement CreateOpenProject(AccessoryEditorSourceAsset source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CreateOpenProject(new AccessoryEditorProject(source.PixelWidth, source.PixelHeight, 0, source.AssetId,
            [new("layer-1", "图片", source.AssetId, 0, 0, source.PixelWidth, source.PixelHeight)], [source]));
    }

    public JsonElement CreateOpenProject(AccessoryEditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var aliases = project.Assets.Keys.Select((id, index) => (id, alias: "asset-" + (index + 1)))
            .ToDictionary(a => a.id, a => a.alias, StringComparer.Ordinal);
        var layers = project.Layers.Select((layer, index) => WriteLayer(layer, index + 1, aliases)).ToArray();
        var result = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, width = project.PixelWidth, height = project.PixelHeight, layers,
            assets = project.Assets.Values.Select(a => new { id = aliases[a.AssetId], pngBase64 = Convert.ToBase64String(a.CopyPngBytes()) }),
        }, JsonOptions);
        CheckBytes(result);
        layerIds.Clear();
        for (var index = 0; index < project.Layers.Count; index++) layerIds.Add(index + 1, project.Layers[index].Id);
        return result;
    }

    /// <summary>Parameters are a native snapshot; the caller must first enforce its bridge session/request identity.</summary>
    public AccessoryEditResult ReadResult(JsonElement browserProject, JsonElement exportedPng,
        AccessoryEditDraft draft, AccessoryItem parameters)
    {
        ArgumentNullException.ThrowIfNull(draft); ArgumentNullException.ThrowIfNull(parameters);
        try
        {
            if (parameters.Id != draft.ItemId || draft.Revision < 0) throw Invalid();
            CheckBytes(browserProject); CheckBytes(exportedPng);
            var pngFields = Object(exportedPng, ["width", "height", "pngBase64"]);
            var image = AccessoryImageAsset.FromRenderedPng(DecodePng(pngFields["pngBase64"]));
            if (Integer(pngFields["width"], 1, 4096) != image.PixelWidth ||
                Integer(pngFields["height"], 1, 4096) != image.PixelHeight) throw Invalid();
            var project = ReadProject(browserProject, image, draft.Revision);
            var item = parameters with
            {
                AssetId = image.AssetId, PixelWidth = image.PixelWidth, PixelHeight = image.PixelHeight,
                ProjectId = project.ProjectId, ProjectVersion = project.Version, EditRevision = draft.Revision,
                IsPendingCompletion = false,
            };
            return new(project, image, item);
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or OverflowException or ArgumentException)
        { throw new InvalidDataException("浏览器工程导出无效，当前草稿未更改。", error); }
    }

    private AccessoryEditorProject ReadProject(JsonElement value, AccessoryImageAsset image, long revision)
    {
        var root = Object(value, ["schemaVersion", "width", "height", "layers", "assets"]);
        if (Integer(root["schemaVersion"], 1, 1) != 1 || Integer(root["width"], 1, 4096) != image.PixelWidth ||
            Integer(root["height"], 1, 4096) != image.PixelHeight) throw Invalid();
        var layers = Array(root["layers"], 1, AccessoryEditorProject.MaximumLayers);
        var assetElements = Array(root["assets"], 0, AccessoryEditorProject.MaximumLayers);
        var encoded = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long pixels = 0;
        // Inspect every header and the aggregate before decoding any project asset.
        foreach (var element in assetElements)
        {
            var asset = Object(element, ["id", "pngBase64"]);
            var alias = Text(asset["id"]);
            if (!IsAlias(alias) || encoded.ContainsKey(alias)) throw Invalid();
            var bytes = DecodePng(asset["pngBase64"]);
            var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
            if (width > 4096 || height > 4096) throw Invalid();
            AccessoryImageAsset.ValidateDimensions((int)width, (int)height);
            pixels += (long)width * height;
            if (pixels > AccessoryEditorProject.MaximumLayerPixels) throw Invalid();
            encoded.Add(alias, bytes);
        }
        var assets = encoded.ToDictionary(a => a.Key, a => AccessoryEditorSourceAsset.FromPng(a.Value), StringComparer.Ordinal);
        var ids = new HashSet<int>();
        var nativeIds = new HashSet<string>(layerIds.Values, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(double Order, AccessoryProjectLayer Layer)>();
        foreach (var element in layers)
        {
            var fields = Object(element,
                ["id", "name", "type", "x", "y", "width", "height", "visible", "is_vector", "opacity", "order", "composition", "rotate", "data", "params", "status", "color", "render_function"],
                ["parent_id", "hide_selection_if_active", "width_original", "height_original", "assetId"]);
            var id = Integer(fields["id"], 1, 1_000_000);
            if (!ids.Add(id)) throw Invalid();
            if (!layerIds.TryGetValue(id, out var nativeId))
            {
                nativeId = "browser-layer-" + id;
                while (!nativeIds.Add(nativeId)) nativeId += "_";
            }
            var type = fields["type"].ValueKind == JsonValueKind.Null ? null : Text(fields["type"]);
            if (type is not (null or "image" or "brush" or "text") || Text(fields["composition"]) != "source-over" ||
                fields["status"].ValueKind != JsonValueKind.Null ||
                fields.TryGetValue("parent_id", out var parent) && Integer(parent, 0, 0) != 0) throw Invalid();
            if (fields.TryGetValue("hide_selection_if_active", out var hide)) _ = Boolean(hide);
            if (Boolean(fields["is_vector"]) != (type is "brush" or "text")) throw Invalid();
            var renderer = fields["render_function"];
            if (type is "brush" or "text")
            {
                var parts = Array(renderer, 2, 2);
                if (Text(parts[0]) != type || Text(parts[1]) != "render") throw Invalid();
            }
            else if (renderer.ValueKind != JsonValueKind.Null) throw Invalid();
            string? assetId = null; AccessoryBrushContent? brush = null; AccessoryTextContent? text = null;
            if (type == "image")
            {
                if (!fields.TryGetValue("assetId", out var assetRef) || !assets.TryGetValue(Text(assetRef), out var asset)) throw Invalid();
                used.Add(Text(assetRef)); assetId = asset.AssetId;
            }
            else if (fields.ContainsKey("assetId")) throw Invalid();
            if (type == "brush") brush = ReadBrush(fields["data"], fields["params"]);
            else if (type == "text") text = ReadText(fields["data"], fields["params"]);
            else if (fields["data"].ValueKind != JsonValueKind.Null || Object(fields["params"], []).Count != 0) throw Invalid();
            var rotation = type == "brush" && fields["rotate"].ValueKind == JsonValueKind.Null ? 0 : Number(fields["rotate"], -360, 360);
            var width = type is null && fields["width"].ValueKind == JsonValueKind.Null ? 0 : Number(fields["width"], type is null ? 0 : .001, type is null ? 0 : 4096);
            var height = type is null && fields["height"].ValueKind == JsonValueKind.Null ? 0 : Number(fields["height"], type is null ? 0 : .001, type is null ? 0 : 4096);
            result.Add((Number(fields["order"], 0, 1_000_000), new(nativeId, Text(fields["name"]), assetId,
                Number(fields["x"], -8192, 8192), Number(fields["y"], -8192, 8192), width, height,
                Number(fields["opacity"], 0, 100) / 100, Boolean(fields["visible"]), type, rotation, Text(fields["color"]),
                OptionalDimension(fields, "width_original"), OptionalDimension(fields, "height_original"), brush, text)));
        }
        if (used.Count != assets.Count) throw Invalid();
        // miniPaint stable-sorts descending, then renders backwards. Reverse equal-order ties too,
        // so a valid browser document with tied stack orders keeps its actual painted appearance.
        return new(image.PixelWidth, image.PixelHeight, revision, image.AssetId, result.OrderByDescending(l => l.Order).Reverse().Select(l => l.Layer),
            assets.Values.DistinctBy(a => a.AssetId));
    }

    private static Dictionary<string, object?> WriteLayer(AccessoryProjectLayer layer, int id, Dictionary<string, string> aliases)
    {
        var result = new Dictionary<string, object?>
        {
            ["id"] = id, ["parent_id"] = 0, ["name"] = layer.Name, ["type"] = layer.Type,
            ["x"] = layer.X, ["y"] = layer.Y, ["width"] = layer.Width, ["height"] = layer.Height,
            ["width_original"] = layer.OriginalWidth, ["height_original"] = layer.OriginalHeight,
            ["visible"] = layer.Visible, ["is_vector"] = layer.Type is "brush" or "text", ["hide_selection_if_active"] = false,
            ["opacity"] = layer.Opacity * 100, ["order"] = id, ["composition"] = "source-over", ["rotate"] = layer.Rotation,
            ["status"] = null, ["color"] = layer.Color, ["data"] = null, ["params"] = new { }, ["render_function"] = null,
        };
        if (layer.Type == "image") result["assetId"] = aliases[layer.AssetId!];
        if (layer.Brush is { } brush)
        {
            result["data"] = brush.Strokes.Select(s => s.Select(p => new[] { p.X, p.Y, p.Pressure }));
            result["params"] = new { size = brush.Size, pressure = brush.Pressure };
            result["render_function"] = new[] { "brush", "render" };
        }
        if (layer.Text is { } text)
        {
            result["data"] = text.Lines.Select(l => l.Select(s => new { text = s.Text, meta = WriteStyle(s.Meta) }));
            result["params"] = new { boundary = text.Boundary, kerning = text.Kerning, text_direction = text.TextDirection,
                wrap_direction = text.WrapDirection, halign = text.HorizontalAlign, valign = text.VerticalAlign, wrap = text.Wrap };
            result["render_function"] = new[] { "text", "render" };
        }
        return result;
    }

    private static object WriteStyle(AccessoryTextStyle m) => new { family = m.Family, size = m.Size, kerning = m.Kerning,
        leading = m.Leading, bold = m.Bold, italic = m.Italic, underline = m.Underline, strikethrough = m.Strikethrough,
        fill_color = m.FillColor, stroke_color = m.StrokeColor, stroke_size = m.StrokeSize };

    private static AccessoryBrushContent ReadBrush(JsonElement data, JsonElement parameters)
    {
        var p = Object(parameters, ["size", "pressure"]);
        var groups = Array(data, 1, 1024);
        if (groups.Sum(g => (long)(g.ValueKind == JsonValueKind.Array ? g.GetArrayLength() : throw Invalid())) > 32768) throw Invalid();
        var strokes = groups.Select(group => (IReadOnlyList<AccessoryBrushPoint>)Array(group, 1, 32768).Select(point =>
        {
            var v = Array(point, 3, 3);
            return new AccessoryBrushPoint(Number(v[0], -8192, 8192), Number(v[1], -8192, 8192), Number(v[2], .001, 2048));
        }).ToArray()).ToArray();
        return new(Number(p["size"], 1, 999), Boolean(p["pressure"]), strokes);
    }

    private static AccessoryTextContent ReadText(JsonElement data, JsonElement parameters)
    {
        var p = Object(parameters, ["boundary", "kerning", "text_direction", "wrap_direction", "halign", "valign", "wrap"]);
        var lineElements = Array(data, 1, 2048);
        if (lineElements.Sum(l => (long)(l.ValueKind == JsonValueKind.Array ? l.GetArrayLength() : throw Invalid())) > 8192) throw Invalid();
        long characters = 0;
        var lines = lineElements.Select(line => (IReadOnlyList<AccessoryTextSpan>)Array(line, 1, 8192).Select(span =>
        {
            var s = Object(span, ["text", "meta"]);
            var m = Object(s["meta"], [], ["family", "size", "kerning", "leading", "bold", "italic", "underline", "strikethrough", "fill_color", "stroke_color", "stroke_size"]);
            string String(string key, string fallback) => m.TryGetValue(key, out var v) ? Text(v) : fallback;
            double Num(string key, double fallback, double min, double max) => m.TryGetValue(key, out var v) ? Number(v, min, max) : fallback;
            bool Bool(string key) => m.TryGetValue(key, out var v) && Boolean(v);
            var content = Text(s["text"]);
            characters += content.Length;
            if (characters > 65536) throw Invalid();
            // miniPaint deliberately omits default span styles; materialize its actual defaults.
            return new AccessoryTextSpan(content, new(String("family", "Arial"), Num("size", 40, 1, 512),
                Num("kerning", 0, -999, 999), Num("leading", 0, -999, 999), Bool("bold"), Bool("italic"), Bool("underline"),
                Bool("strikethrough"), String("fill_color", "#008800"), String("stroke_color", "#000000"), Num("stroke_size", 0, 0, 64)));
        }).ToArray()).ToArray();
        return new(lines, Text(p["boundary"]), Text(p["kerning"]), Text(p["text_direction"]), Text(p["wrap_direction"]),
            Text(p["halign"]), Text(p["valign"]), Text(p["wrap"]));
    }

    private static Dictionary<string, JsonElement> Object(JsonElement value, string[] required, string[]? optional = null)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in value.EnumerateObject())
            if ((!required.Contains(p.Name) && !(optional?.Contains(p.Name) ?? false)) || !result.TryAdd(p.Name, p.Value)) throw Invalid();
        if (required.Any(name => !result.ContainsKey(name))) throw Invalid();
        return result;
    }
    private static JsonElement[] Array(JsonElement value, int min, int max)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < min || value.GetArrayLength() > max) throw Invalid();
        return value.EnumerateArray().ToArray();
    }
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid();
    private static bool Boolean(JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw Invalid();
    private static double Number(JsonElement value, double min, double max) => value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var v) && AccessoryBrushContent.Bound(v, min, max) ? v : throw Invalid();
    private static int Integer(JsonElement value, int min, int max) => value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var v) && v >= min && v <= max ? v : throw Invalid();
    private static double? OptionalDimension(Dictionary<string, JsonElement> fields, string key) =>
        fields.TryGetValue(key, out var v) && v.ValueKind != JsonValueKind.Null ? Number(v, 1, 4096) : null;
    private static bool IsAlias(string value) => value.StartsWith("asset-", StringComparison.Ordinal) && value.Length > 6 && value.AsSpan(6).ToString().All(char.IsAsciiDigit);
    private static void CheckBytes(JsonElement value)
    {
        if (Encoding.UTF8.GetByteCount(value.GetRawText()) > AccessoryEditorProject.MaximumEncodedBytes) throw Invalid();
    }
    private static byte[] DecodePng(JsonElement value)
    {
        var text = Text(value);
        if (text.Length < 44 || text.Length > (AccessoryImageAsset.MaximumFileBytes + 2) / 3 * 4) throw Invalid();
        var bytes = Convert.FromBase64String(text);
        if (bytes.LongLength > AccessoryImageAsset.MaximumFileBytes || bytes.Length < 33 ||
            !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) != 13 || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) throw Invalid();
        return bytes;
    }
    private static InvalidDataException Invalid() => new("浏览器工程字段、引用或资源范围无效，当前草稿未更改。");
}
