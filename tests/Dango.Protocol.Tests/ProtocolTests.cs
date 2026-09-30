using System.Text.Json;
using Dango.Protocol;

namespace Dango.Protocol.Tests;

[TestClass]
public sealed class ProtocolTests
{
    [TestMethod]
    public void SamplesMidpointAndEndWithoutClientClockOrPhysics()
    {
        var pet = Pet() with { Motion = new("m1", new(.3, .12), new(.396, .12), 100, 900, "right", 1) };
        Assert.AreEqual(.348, RoomSampling.At(pet, 500).AnchorPosition.X, 1e-9);
        Assert.AreEqual(.396, RoomSampling.At(pet, 1000).AnchorPosition.X, 1e-9);
        Assert.IsNull(RoomSampling.At(pet, 900).Motion);
        Assert.AreEqual(.3, RoomSampling.At(pet, 0).AnchorPosition.X, 1e-9);
    }

    [TestMethod]
    public void ExpiredActionNormalizesWithoutEndEvent()
    {
        var pet = Pet() with { Action = new("a1", "wave", ["pet-a"], 100, 1300, 8, 1, "wave-nod") };
        Assert.IsNotNull(RoomSampling.At(pet, 1299).Action);
        Assert.IsNull(RoomSampling.At(pet, 1300).Action);
        Assert.AreEqual(.5, RoomSampling.ActionProgress(pet.Action, 700));
    }

    [TestMethod]
    public void GoldenCommandRoundTripsAndOptionalFutureFieldsAreSafe()
    {
        const string json = """{"protocolVersion":"0.1","roomId":"p2a-room","roomEpoch":"epoch-test","connectionGeneration":1,"commandId":"command-test","commandOrdinal":1,"lastAppliedSequence":0,"type":"setMoveIntent","payload":{"direction":"left"},"futureOptional":true}""";
        var value = JsonSerializer.Deserialize<RoomCommand>(json, RoomProtocol.Json)!;
        Assert.IsNull(RoomProtocol.Validate(value));
        Assert.AreEqual("left", value.Payload.Direction);
        StringAssert.Contains(JsonSerializer.Serialize(value, RoomProtocol.Json), "\"commandOrdinal\":1");
        var spoof = JsonSerializer.Deserialize<RoomCommand>(json.Replace("\"futureOptional\":true", "\"petId\":\"pet-b\""), RoomProtocol.Json)!;
        Assert.AreEqual("forbidden", RoomProtocol.Validate(spoof));
    }

    [TestMethod]
    public void RejectsMissingRequiredFieldsUnknownVersionDirectionAndNonFinite()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<RoomCommand>("{}", RoomProtocol.Json));
        var command = new RoomCommand("0.1", "room", "epoch", 1, "c1", 1, 0, RoomProtocol.Move, new("left"));
        Assert.AreEqual("unsupported_protocol", RoomProtocol.Validate(command with { ProtocolVersion = "99" }));
        Assert.AreEqual("invalid_command", RoomProtocol.Validate(command with { Payload = new("up") }));
        Assert.IsFalse(RoomProtocol.IsPoint(new(double.NaN, .12)));
        Assert.IsFalse(RoomProtocol.IsPoint(new(1.01, .12)));
        Assert.IsFalse(RoomProtocol.IsPet(Pet() with { PhysicsPresentationPresetRef = new("unknown", 2) }));
    }

    private static PetState Pet() => new("pet-a", "participant-a", new(.3, .12), "center", null, null, PresetRef.Default, PresetRef.Default);
}
