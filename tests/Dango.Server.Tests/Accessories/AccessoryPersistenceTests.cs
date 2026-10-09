using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dango.Protocol.Accessories;
using Dango.Server.Accessories;
using Dango.Server.Accounts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Accessories;

[TestClass]
public sealed class AccessoryPersistenceTests
{
    internal static CommitAccessoryOutfitRequest Request(long revision = 0, string? key = null) =>
        new(revision, key ?? new string('a', 32), new(1, [new("one", "private name", "builtin-sprout", 64, 64)]));
    internal static string Wire() => JsonSerializer.Serialize(Request(), AccessoryPersistence.Json);

    [TestMethod]
    public void CompleteRecipeRoundTripsEveryParameterAndHasStableDigest()
    {
        var request = Request() with { Outfit = new(1, [new("a", "flower", "builtin-bowler", 64, 64)
        { Layer = AccessoryOutfitLayer.Behind, Order = 4, SizePercent = 130, BaseAngleDegrees = -30, ImagePivotX = .25, ImagePivotY = .7,
          BodyAnchorX = .1, BodyAnchorY = .8, Softness = 5, AllowFolding = true, MaxDeformationPercent = 50, ConnectionStrength = 40,
          MaxConnectionTravelPercent = 35, RotationDamping = 25, LockRotation = false, ReturnStrength = 15, MaxSwingDegrees = 60,
          LimitSwing = false, GravityInfluence = 20 }] ) };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, AccessoryPersistence.Json);
        var parsed = AccessoryPersistence.ParseCommit(bytes);
        Assert.AreEqual(request, parsed);
        Assert.AreEqual(AccessoryPersistence.Digest(request), AccessoryPersistence.Digest(parsed));
        Assert.AreNotEqual(AccessoryPersistence.Digest(request), AccessoryPersistence.Digest(request with { ExpectedRevision = 1 }));
    }

    [TestMethod]
    [DataRow("userId")]
    [DataRow("petId")]
    [DataRow("localPath")]
    public void BodyCannotDeclareAccountOwnershipOrLocalPaths(string field)
    {
        var json = JsonNode.Parse(Wire())!.AsObject(); json[field] = "fictional";
        Reject(json.ToJsonString());
    }

    [TestMethod]
    public void EveryRecipeFieldIsRequiredAndUnknownItemFieldsAreRejected()
    {
        var complete = JsonNode.Parse(Wire())!;
        foreach (var property in complete["outfit"]!["items"]![0]!.AsObject().Select(p => p.Key).ToArray())
        {
            var changed = complete.DeepClone(); changed["outfit"]!["items"]![0]!.AsObject().Remove(property);
            Reject(changed.ToJsonString());
        }
        var extra = complete.DeepClone(); extra["outfit"]!["items"]![0]!["reviewApproved"] = true; Reject(extra.ToJsonString());
    }

    [TestMethod]
    [DataRow("Front")]
    [DataRow("unknown")]
    [DataRow("0")]
    public void LayerMustUseTheExactPublicWireFormat(string layer)
    {
        var changed = JsonNode.Parse(Wire())!;
        changed["outfit"]!["items"]![0]!["layer"] = layer == "0" ? JsonValue.Create(0) : JsonValue.Create(layer);
        Reject(changed.ToJsonString());
    }

    [TestMethod]
    public void DuplicatePropertiesAtEveryLevelAndOversizeJsonAreRejected()
    {
        Reject(Wire().Replace("\"expectedRevision\":0", "\"expectedRevision\":0,\"expectedRevision\":0", StringComparison.Ordinal));
        Reject(Wire().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal));
        Reject(Wire().Replace("\"order\":0", "\"order\":0,\"order\":0", StringComparison.Ordinal));
        Reject(new string(' ', AccessoryPersistence.MaximumJsonBytes + 1) + Wire());
    }

    [TestMethod]
    public void InvalidPersistentDataFailsClosedInsteadOfBeingRepaired()
    {
        var serialized = AccessoryPersistence.Serialize(Request().Outfit);
        Assert.AreEqual(Request().Outfit, AccessoryPersistence.ReadOutfit(serialized, 1));
        foreach (var invalid in new[] { "{}", serialized.Replace("\"softness\":0", "\"softness\":999", StringComparison.Ordinal),
                     serialized.Replace("\"gravityInfluence\":0", "\"gravityInfluence\":0,\"gravityInfluence\":0", StringComparison.Ordinal) })
            Assert.AreEqual("accessory_invalid_state", Assert.ThrowsExactly<AccountFault>(() => AccessoryPersistence.ReadOutfit(invalid, 1)).Code);
        Assert.ThrowsExactly<AccountFault>(() => AccessoryPersistence.ReadOutfit(serialized, 0));
    }
    [TestMethod]
    public void AValidButChangedReceiptRecipeCannotKeepTheOriginalDigest()
    {
        var request = Request(); var digest = AccessoryPersistence.Digest(request); var json = AccessoryPersistence.Serialize(request.Outfit);
        Assert.AreEqual(new StoredAccessoryOutfit(1, request.Outfit), AccessoryPersistence.ReadReceipt(1, request.IdempotencyKey, digest, json));
        foreach (var changed in new[] { json.Replace("private name", "changed valid name", StringComparison.Ordinal),
                     json.Replace("\"softness\":0", "\"softness\":12", StringComparison.Ordinal), AccessoryPersistence.Serialize(AccessoryOutfitManifest.Empty) })
            Assert.AreEqual("accessory_invalid_state", Assert.ThrowsExactly<AccountFault>(() => AccessoryPersistence.ReadReceipt(1, request.IdempotencyKey, digest, changed)).Code);
        Assert.ThrowsExactly<AccountFault>(() => AccessoryPersistence.ReadReceipt(2, request.IdempotencyKey, digest, json));
        Assert.ThrowsExactly<AccountFault>(() => AccessoryPersistence.ReadReceipt(1, new string('b', 32), digest, json));
    }
    [TestMethod]
    public void SignedZeroSurvivesPostgresJsonbNumberNormalizationWithoutChangingTheReceipt()
    {
        var request = Request() with { Outfit = new(1, [Request().Outfit.Items[0] with { BaseAngleDegrees = -0d, Softness = -0d }]) };
        var json = AccessoryPersistence.Serialize(request.Outfit).Replace(":-0", ":0", StringComparison.Ordinal);
        Assert.AreEqual(new StoredAccessoryOutfit(1, request.Outfit), AccessoryPersistence.ReadReceipt(1, request.IdempotencyKey, AccessoryPersistence.Digest(request), json));
        var positive = request with { Outfit = new(1, [request.Outfit.Items[0] with { BaseAngleDegrees = 0d, Softness = 0d }]) };
        Assert.AreEqual(AccessoryPersistence.Digest(request), AccessoryPersistence.Digest(positive));
    }
    private static void Reject(string json) => Assert.AreEqual("accessory_request_invalid",
        Assert.ThrowsExactly<AccountFault>(() => AccessoryPersistence.ParseCommit(Encoding.UTF8.GetBytes(json))).Code);
}
