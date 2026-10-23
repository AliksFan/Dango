using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Buffers.Binary;

namespace Dango.Desktop.Accessories.Editing;

/// <summary>miniPaint's supported raster layer interchange. IDs refer only to embedded, normalized PNGs.</summary>
public sealed record AccessoryProjectLayer(string Id, string Name, string? AssetId, double X, double Y,
    double Width, double Height, double Opacity = 1, bool Visible = true, string? Type = "image",
    double Rotation = 0, string Color = "#000000", double? OriginalWidth = null, double? OriginalHeight = null,
    AccessoryBrushContent? Brush = null, AccessoryTextContent? Text = null);

public sealed class AccessoryEditorProject
{
    public const int CurrentVersion = 1;
    public const long MaximumEncodedBytes = 64L * 1024 * 1024;
    public const int MaximumLayers = 16;
    public const long MaximumLayerPixels = 64_000_000;
    public const long MaximumPendingSourcePixels = 128_000_000;
    public const long MaximumPendingSourceBytes = 256L * 1024 * 1024;
    public const long MaximumPendingProjectBytes = 128L * 1024 * 1024;
    public const long MaximumPendingProjectPixels = 128_000_000;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };
    private readonly byte[] bytes;
    private readonly IReadOnlyDictionary<string, AccessoryEditorSourceAsset> assets;
    public string ProjectId { get; }
    public int Version => CurrentVersion;
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public long Revision { get; }
    public string FinalAssetId { get; }
    public IReadOnlyList<AccessoryProjectLayer> Layers { get; }
    public IReadOnlyDictionary<string, AccessoryEditorSourceAsset> Assets => assets;
    public long EncodedByteLength => bytes.LongLength;
    public long DecodedLayerPixels { get; }

    public AccessoryEditorProject(int pixelWidth, int pixelHeight, long revision, string finalAssetId,
        IEnumerable<AccessoryProjectLayer> layers, IEnumerable<AccessoryEditorSourceAsset> assets)
    {
        AccessoryImageAsset.ValidateDimensions(pixelWidth, pixelHeight);
        if (revision < 0 || !IsHash(finalAssetId)) throw new InvalidDataException("工程输出引用无效。");
        var layerArray = layers.Take(MaximumLayers + 1).ToArray();
        var assetArray = assets.Take(MaximumLayers + 1).ToArray();
        if (layerArray.Length is < 1 or > MaximumLayers || assetArray.Length > MaximumLayers ||
            assetArray.Any(a => a is null) || assetArray.Select(a => a.AssetId).Distinct().Count() != assetArray.Length)
            throw new InvalidDataException("工程图层或资产数量无效。");
        var lookup = assetArray.ToDictionary(a => a.AssetId, StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long renderedPixels = 0;
        foreach (var layer in layerArray)
        {
            if (layer is null || !IsLocalId(layer.Id) || !ids.Add(layer.Id) || string.IsNullOrWhiteSpace(layer.Name) ||
                layer.Name.Length > 128 || layer.Name.Any(char.IsControl) || layer.Name.Contains('<') || layer.Name.Contains('>') ||
                !FiniteBound(layer.X, -8192, 8192) || !FiniteBound(layer.Y, -8192, 8192) ||
                !FiniteBound(layer.Opacity, 0, 1) || !FiniteBound(layer.Rotation, -360, 360) || !AccessoryTextContent.IsColor(layer.Color) ||
                layer.Type is not (null or "image" or "brush" or "text") ||
                layer.OriginalWidth is { } ow && !FiniteBound(ow, 1, 4096) || layer.OriginalHeight is { } oh && !FiniteBound(oh, 1, 4096))
                throw new InvalidDataException("工程图层字段无效；只接受受控内部图片引用。");
            if (layer.Type is null)
            {
                if (layer.Width != 0 || layer.Height != 0 || layer.AssetId is not null || layer.Brush is not null || layer.Text is not null)
                    throw new InvalidDataException("空图层不能带内容。");
            }
            else if (!FiniteBound(layer.Width, .001, 4096) || !FiniteBound(layer.Height, .001, 4096) ||
                Math.Ceiling(layer.Width) * Math.Ceiling(layer.Height) > AccessoryImageAsset.MaximumPixels ||
                layer.Type == "image" && (layer.AssetId is null || !lookup.ContainsKey(layer.AssetId) || layer.Brush is not null || layer.Text is not null) ||
                layer.Type == "brush" && (layer.Brush is null || layer.Text is not null || layer.AssetId is not null) ||
                layer.Type == "text" && (layer.Text is null || layer.Brush is not null || layer.AssetId is not null))
                throw new InvalidDataException("工程图层内容无效。");
            renderedPixels += (long)Math.Ceiling(layer.Width) * (long)Math.Ceiling(layer.Height);
        }
        if (lookup.Keys.Any(id => !layerArray.Any(layer => layer.AssetId == id)))
            throw new InvalidDataException("工程含未使用资产。");
        DecodedLayerPixels = assetArray.Sum(a => (long)a.PixelWidth * a.PixelHeight);
        if (DecodedLayerPixels > MaximumLayerPixels || renderedPixels > MaximumLayerPixels)
            throw new InvalidDataException("工程图层像素超过上限。");
        PixelWidth = pixelWidth; PixelHeight = pixelHeight; Revision = revision; FinalAssetId = finalAssetId;
        Layers = Array.AsReadOnly(layerArray);
        this.assets = new System.Collections.ObjectModel.ReadOnlyDictionary<string, AccessoryEditorSourceAsset>(lookup);
        // Check source byte total before base64 allocation; then enforce the actual UTF-8 wrapper bound.
        if (assetArray.Sum(a => a.EncodedByteLength) > MaximumEncodedBytes * 3 / 4 - 65536)
            throw new InvalidDataException("工程编码超过上限。");
        bytes = JsonSerializer.SerializeToUtf8Bytes(new ProjectDocument(CurrentVersion, "minipaint-v1",
            pixelWidth, pixelHeight, revision, finalAssetId, layerArray,
            assetArray.Select(a => new ProjectAsset(a.AssetId, Convert.ToBase64String(a.CopyPngBytes()))).ToArray()), Options);
        if (bytes.LongLength > MaximumEncodedBytes) throw new InvalidDataException("工程编码超过上限。");
        ProjectId = Hash(bytes);
    }

    public byte[] CopyBytes() => (byte[])bytes.Clone();

    public void ValidateResult(AccessoryItem item, AccessoryImageAsset image)
    {
        item.Validate();
        if (item.IsPendingCompletion || item.ProjectId != ProjectId || item.ProjectVersion != Version ||
            item.EditRevision != Revision || item.AssetId != FinalAssetId || image.AssetId != FinalAssetId ||
            item.PixelWidth != PixelWidth || item.PixelHeight != PixelHeight ||
            image.PixelWidth != PixelWidth || image.PixelHeight != PixelHeight)
            throw new InvalidDataException("工程、成品 PNG、参数与编辑修订不一致。");
    }

    public static AccessoryEditorProject Read(byte[] bytes, string? expectedId = null)
        => Read(bytes, expectedId, AccessoryEditorSourceAsset.FromPng);

    internal static AccessoryEditorProject Read(byte[] bytes, string? expectedId, Func<byte[], AccessoryEditorSourceAsset> decode)
    {
        try { return ReadCore(bytes, expectedId, decode); }
        catch (Exception error) when (error is JsonException or FormatException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("工程格式无效。", error); }
    }

    private static AccessoryEditorProject ReadCore(byte[] bytes, string? expectedId, Func<byte[], AccessoryEditorSourceAsset> decode)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.LongLength is < 1 or > MaximumEncodedBytes) throw new InvalidDataException("工程大小无效。");
        bytes = (byte[])bytes.Clone();
        if (expectedId is not null && Hash(bytes) != expectedId) throw new InvalidDataException("工程散列无效。");
        using var json = JsonDocument.Parse(bytes, new() { MaxDepth = 16 });
        RejectDuplicates(json.RootElement);
        var root = AccessoryEditorBridge.ExactObject(json.RootElement,
            ["version", "engine", "pixelWidth", "pixelHeight", "revision", "finalAssetId", "layers", "assets"]);
        foreach (var layer in root["layers"].EnumerateArray()) _ = AccessoryEditorBridge.ExactObject(layer,
            ["id", "name", "assetId", "x", "y", "width", "height", "opacity", "visible", "type", "rotation", "color", "originalWidth", "originalHeight", "brush", "text"]);
        foreach (var asset in root["assets"].EnumerateArray()) _ = AccessoryEditorBridge.ExactObject(asset, ["id", "png"]);
        var data = JsonSerializer.Deserialize<ProjectDocument>(bytes, Options) ?? throw new InvalidDataException("工程为空。");
        if (data.Version != CurrentVersion || data.Engine != "minipaint-v1" || data.Layers is null || data.Assets is null ||
            data.Assets.Length > MaximumLayers || data.Layers.Length > MaximumLayers)
            throw new InvalidDataException("工程版本或引擎不受支持。");
        // Inspect every bounded header before allocating any decoded pixel surface.
        long decodedPixels = 0;
        var assetIds = new HashSet<string>(StringComparer.Ordinal);
        Span<byte> header = stackalloc byte[33];
        foreach (var asset in data.Assets)
        {
            if (asset is null || !IsHash(asset.Id) || !assetIds.Add(asset.Id) || asset.Png is null ||
                asset.Png.Length < 44 || asset.Png.Length > AccessoryImageAsset.MaximumFileBytes * 4 / 3 + 4)
                throw new InvalidDataException("工程图片声明无效。");
            if (!Convert.TryFromBase64Chars(asset.Png.AsSpan(0, 44), header, out var length) || length != 33 ||
                !header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                BinaryPrimitives.ReadUInt32BigEndian(header[8..12]) != 13 || !header[12..16].SequenceEqual("IHDR"u8))
                throw new InvalidDataException("工程 PNG 文件头无效。");
            var width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
            var height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
            if (width > int.MaxValue || height > int.MaxValue) throw new InvalidDataException("工程 PNG 尺寸无效。");
            AccessoryImageAsset.ValidateDimensions((int)width, (int)height);
            decodedPixels += (long)width * height;
            if (decodedPixels > MaximumLayerPixels) throw new InvalidDataException("工程图层累计像素超过上限，未解码图片。");
        }
        var decoded = data.Assets.Select(a =>
        {
            if (a is null || !IsHash(a.Id) || a.Png is null || a.Png.Length > AccessoryImageAsset.MaximumFileBytes * 4 / 3 + 4)
                throw new InvalidDataException("工程图片无效。");
            var png = Convert.FromBase64String(a.Png);
            var asset = decode(png);
            if (asset.AssetId != a.Id || !png.AsSpan().SequenceEqual(asset.CopyPngBytes()))
                throw new InvalidDataException("工程内部图片散列或规范化编码不一致。");
            return asset;
        }).ToArray();
        var project = new AccessoryEditorProject(data.PixelWidth, data.PixelHeight, data.Revision, data.FinalAssetId, data.Layers, decoded);
        // Exact bytes are the immutable identity, including valid whitespace/property ordering.
        return new AccessoryEditorProject(project, bytes);
    }

    private AccessoryEditorProject(AccessoryEditorProject validated, byte[] original)
    {
        bytes = (byte[])original.Clone(); ProjectId = Hash(bytes); PixelWidth = validated.PixelWidth;
        PixelHeight = validated.PixelHeight; Revision = validated.Revision; FinalAssetId = validated.FinalAssetId;
        Layers = validated.Layers; assets = validated.assets; DecodedLayerPixels = validated.DecodedLayerPixels;
    }

    public static AccessoryEditorProject FromImage(AccessoryImageAsset image, long revision = 0) =>
        new(image.PixelWidth, image.PixelHeight, revision, image.AssetId,
            [new("layer-1", "图片", image.AssetId, 0, 0, image.PixelWidth, image.PixelHeight)],
            [AccessoryEditorSourceAsset.FromPng(image.CopyPngBytes())]);

    internal static bool IsHash(string? id) => id is { Length: 64 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool IsLocalId(string? id) => id is { Length: > 0 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static bool FiniteBound(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException("工程有重复字段。"); RejectDuplicates(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var value in element.EnumerateArray()) RejectDuplicates(value);
    }
    private sealed record ProjectAsset(string Id, string Png);
    private sealed record ProjectDocument(int Version, string Engine, int PixelWidth, int PixelHeight, long Revision,
        string FinalAssetId, AccessoryProjectLayer[] Layers, ProjectAsset[] Assets);
}
