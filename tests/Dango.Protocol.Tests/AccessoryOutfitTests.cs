using System.Text.Json;
using System.Text;
using Dango.Protocol.Accessories;

namespace Dango.Protocol.Tests;

[TestClass]
public sealed class AccessoryOutfitTests
{
    private static AccessoryOutfitItem Item(string id = "one", string? asset = null) =>
        new(id, "配饰", asset ?? new string('a', 64), 100, 100);
    private static AccessoryOutfitManifest Manifest(params AccessoryOutfitItem[] items) => new(1, items);

    [TestMethod]
    public void EmptyOutfitAndEightIndependentInstancesRoundTrip()
    {
        Assert.HasCount(0, AccessoryOutfitManifest.Empty.Items);
        var original = Manifest(Enumerable.Range(0, 8).Select(index => Item("item-" + index) with
        {
            Layer = AccessoryOutfitLayer.Behind, Order = index, SizePercent = 400,
            BaseAngleDegrees = -180, ImagePivotX = 0, ImagePivotY = 1, BodyAnchorX = 1, BodyAnchorY = 0,
            Softness = 100, AllowFolding = true, MaxDeformationPercent = 200, ConnectionStrength = 0,
            MaxConnectionTravelPercent = 200, RotationDamping = 0, LockRotation = false, ReturnStrength = 0,
            MaxSwingDegrees = 180, LimitSwing = false, GravityInfluence = 100,
        }).ToArray());
        Assert.AreEqual(original, JsonSerializer.Deserialize<AccessoryOutfitManifest>(JsonSerializer.Serialize(original)));
    }

    [TestMethod]
    public void SourceListCannotChangeSnapshot()
    {
        var source = new[] { Item() };
        var snapshot = Manifest(source);
        source[0] = Item("changed");
        Assert.AreEqual("one", snapshot.Items[0].Id);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AccessoryOutfitItem>)snapshot.Items).Add(Item()));
    }

    [TestMethod]
    public void RejectsFutureSchemaExcessCountNullDuplicateAndDimensionMismatch()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryOutfitManifest(2, []));
        Assert.ThrowsExactly<InvalidDataException>(() => Manifest(Enumerable.Range(0, 9).Select(index => Item("i" + index)).ToArray()));
        Assert.ThrowsExactly<InvalidDataException>(() => Manifest([null!]));
        Assert.ThrowsExactly<InvalidDataException>(() => Manifest(Item(), Item()));
        Assert.ThrowsExactly<InvalidDataException>(() => Manifest(Item(), Item("two") with { PixelWidth = 99 }));
    }

    [TestMethod]
    public void RejectsInvalidIdentityAndBuiltinDimensions()
    {
        var invalid = new[]
        {
            Item() with { Id = "../../private" }, Item() with { Id = new string('a', 65) },
            Item() with { Name = " " }, Item() with { Name = "a\nb" }, Item() with { Name = new string('a', 129) },
            Item() with { AssetId = new string('A', 64) }, Item() with { AssetId = "builtin-other" },
            Item() with { PixelWidth = 0 }, Item() with { PixelWidth = 4097 },
            Item() with { PixelWidth = 4096, PixelHeight = 4096 },
            Item() with { AssetId = "builtin-sprout", PixelWidth = 512, PixelHeight = 512 },
            Item() with { Layer = (AccessoryOutfitLayer)3 }, Item() with { Order = -1 },
        };
        foreach (var value in invalid) Assert.ThrowsExactly<InvalidDataException>(() => Manifest(value));
        Manifest(Item() with { AssetId = "builtin-sprout", PixelWidth = 64, PixelHeight = 64 });
    }

    [TestMethod]
    public void RejectsEveryOutOfRangeOrNonFiniteParameter()
    {
        var invalid = new[]
        {
            Item() with { SizePercent = 9 }, Item() with { SizePercent = 401 },
            Item() with { BaseAngleDegrees = -181 }, Item() with { BaseAngleDegrees = 181 },
            Item() with { ImagePivotX = -.01 }, Item() with { ImagePivotY = 1.01 },
            Item() with { BodyAnchorX = -.01 }, Item() with { BodyAnchorY = 1.01 },
            Item() with { Softness = -1 }, Item() with { ConnectionStrength = 101 },
            Item() with { MaxDeformationPercent = 201 }, Item() with { MaxConnectionTravelPercent = -1 },
            Item() with { RotationDamping = 101 }, Item() with { ReturnStrength = -1 },
            Item() with { MaxSwingDegrees = 181 }, Item() with { GravityInfluence = 101 },
            Item() with { SizePercent = double.NaN }, Item() with { BaseAngleDegrees = double.PositiveInfinity },
            Item() with { ImagePivotX = double.NegativeInfinity }, Item() with { ImagePivotY = double.NaN },
            Item() with { BodyAnchorX = double.NaN }, Item() with { BodyAnchorY = double.NaN },
            Item() with { Softness = double.NaN }, Item() with { ConnectionStrength = double.NaN },
            Item() with { MaxDeformationPercent = double.NaN }, Item() with { MaxConnectionTravelPercent = double.NaN },
            Item() with { RotationDamping = double.NaN }, Item() with { ReturnStrength = double.NaN },
            Item() with { MaxSwingDegrees = double.NaN }, Item() with { GravityInfluence = double.NaN },
        };
        foreach (var value in invalid) Assert.ThrowsExactly<InvalidDataException>(() => Manifest(value));
    }

    [TestMethod]
    public void ServerUploadMappingRejectsUnsupportedNormalizationAndBadHashes()
    {
        new UploadedAccessoryAsset(new string('a', 64), new string('b', 64), 100, 100, 1).Validate();
        Assert.ThrowsExactly<InvalidDataException>(() => new UploadedAccessoryAsset("builtin-sprout", new string('b', 64), 100, 100, 1).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => new UploadedAccessoryAsset(new string('a', 64), "bad", 100, 100, 1).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => new UploadedAccessoryAsset(new string('a', 64), new string('b', 64), 4096, 4096, 1).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => new UploadedAccessoryAsset(new string('a', 64), new string('b', 64), 100, 100, 2).Validate());
    }

    [TestMethod]
    public void RevisionAndIdempotencyMustBeValid()
    {
        new StoredAccessoryOutfit(0, AccessoryOutfitManifest.Empty).Validate();
        new CommitAccessoryOutfitRequest(0, Guid.NewGuid().ToString("N"), AccessoryOutfitManifest.Empty).Validate();
        Assert.ThrowsExactly<InvalidDataException>(() => new StoredAccessoryOutfit(-1, AccessoryOutfitManifest.Empty).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => new CommitAccessoryOutfitRequest(-1, Guid.NewGuid().ToString("N"), AccessoryOutfitManifest.Empty).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => new CommitAccessoryOutfitRequest(0, "unbounded-or-reused-arbitrary-key", AccessoryOutfitManifest.Empty).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => new StoredAccessoryOutfit(0, null!).Validate());
    }

    [TestMethod]
    public void WireOutfitRejectsUnknownLocalPathOrLibraryFields()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<AccessoryOutfitManifest>("{\"SchemaVersion\":1,\"Items\":[],\"SourcePath\":\"private.png\"}"));
        var itemJson = JsonSerializer.Serialize(Item());
        var json = "{\"SchemaVersion\":1,\"Items\":[" + itemJson[..^1] + ",\"IsEquipped\":false}]}";
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<AccessoryOutfitManifest>(json));
    }

    [TestMethod]
    public void CompleteWireRecipeRoundTripsAndRejectsDuplicateOrMissingParameters()
    {
        var original = Manifest(Item() with { Layer = AccessoryOutfitLayer.Behind, AllowFolding = true });
        var json = AccessoryOutfitJson.Serialize(original);
        Assert.AreEqual(original, AccessoryOutfitJson.Deserialize(Encoding.UTF8.GetBytes(json)));
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryOutfitJson.Deserialize(
            Encoding.UTF8.GetBytes(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal))));
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryOutfitJson.Deserialize(
            Encoding.UTF8.GetBytes(json.Replace("\"allowFolding\":true,", "", StringComparison.Ordinal))));
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryOutfitJson.Deserialize(
            Encoding.UTF8.GetBytes(json.Replace("\"order\":0", "\"order\":0,\"order\":1", StringComparison.Ordinal))));
        Assert.ThrowsExactly<JsonException>(() => AccessoryOutfitJson.Deserialize(
            Encoding.UTF8.GetBytes(json.Replace("\"layer\":\"behind\"", "\"layer\":0", StringComparison.Ordinal))));
    }

    [TestMethod]
    public void WireManifestHasAByteBudgetBeforeParsing()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryOutfitJson.Deserialize(new byte[AccessoryOutfitJson.MaximumManifestBytes + 1]));
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryOutfitJson.Deserialize(ReadOnlyMemory<byte>.Empty));
    }
}
