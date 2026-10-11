using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Dango.SafeSpawnAdmission;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class NativeBinaryProfileTests
{
    private static readonly string Source = new('A', 64), Module = new('B', 64), Probe = new('C', 64);
    private static Dictionary<string, object> Record() => new(StringComparer.Ordinal)
    {
        ["Version"] = 1, ["Platform"] = "linux-x64", ["SourceHash"] = Source, ["ModuleHash"] = Module,
        ["NativeProbeHash"] = Probe, ["Box2DCommit"] = NativeBinaryProfile.Box2DCommit,
        ["Box2DArchiveHash"] = NativeBinaryProfile.ArchiveHash, ["Box2DLicenseHash"] = NativeBinaryProfile.LicenseHash,
        ["BuildConfiguration"] = "Release", ["AbiVersion"] = 1, ["Compiler"] = "gcc 12.2.0",
        ["Exports"] = NativeBinaryProfile.Exports.ToArray()
    };
    private static void Validate(Dictionary<string, object> record) =>
        NativeBinaryProfile.ValidateRecord(JsonSerializer.SerializeToUtf8Bytes(record), Source, Module, Probe);

    [TestMethod]
    public void ExactRecordAcceptsIndependentCurrentHashes() => Validate(Record());

    [TestMethod]
    [DataRow("Version", 2)]
    [DataRow("Version", "1")]
    [DataRow("AbiVersion", 2)]
    [DataRow("AbiVersion", "1")]
    [DataRow("Platform", "win-x64")]
    [DataRow("Platform", "linux-arm64")]
    [DataRow("BuildConfiguration", "Debug")]
    [DataRow("Compiler", " ")]
    [DataRow("Box2DCommit", "v3.1.1")]
    [DataRow("SourceHash", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
    [DataRow("ModuleHash", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBA")]
    [DataRow("NativeProbeHash", "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCA")]
    [DataRow("Box2DArchiveHash", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [DataRow("Box2DLicenseHash", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [DataRow("SourceHash", 1)]
    [DataRow("ModuleHash", true)]
    [DataRow("NativeProbeHash", "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
    public void ChangedContractFieldFails(string field, object value)
    {
        var record = Record(); record[field] = value;
        Assert.ThrowsExactly<InvalidOperationException>(() => Validate(record));
    }

    [TestMethod]
    public void MissingUnknownAndDuplicateFieldsFail()
    {
        var missing = Record(); missing.Remove("Compiler");
        Assert.ThrowsExactly<InvalidOperationException>(() => Validate(missing));
        var unknown = Record(); unknown["AllowedModuleHash"] = Module;
        Assert.ThrowsExactly<InvalidOperationException>(() => Validate(unknown));
        var encoded = JsonSerializer.Serialize(Record());
        var duplicate = encoded.Insert(1, "\"Version\":1,");
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeBinaryProfile.ValidateRecord(Encoding.UTF8.GetBytes(duplicate), Source, Module, Probe));
    }

    [TestMethod]
    public void ExportMissingDuplicateOrderAndTypeFail()
    {
        foreach (var exports in new object[] { NativeBinaryProfile.Exports.Skip(1).ToArray(),
            NativeBinaryProfile.Exports.Append(NativeBinaryProfile.Exports[0]).ToArray(),
            NativeBinaryProfile.Exports.Reverse().ToArray(), new object[] { 1 }, "dango_physics_get_abi_version" })
        {
            var record = Record(); record["Exports"] = exports;
            Assert.ThrowsExactly<InvalidOperationException>(() => Validate(record));
        }
    }

    private static byte[] Elf(ushort type = 3)
    {
        var bytes = new byte[64]; bytes[0] = 0x7f; bytes[1] = (byte)'E'; bytes[2] = (byte)'L'; bytes[3] = (byte)'F';
        bytes[4] = 2; bytes[5] = 1; bytes[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16, 2), type);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18, 2), 62);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), 1);
        return bytes;
    }
    [TestMethod]
    public void ElfGateRequiresActualLittleEndianX64AndModuleType()
    {
        NativeBinaryProfile.ValidateElf(Elf(), executable: false);
        NativeBinaryProfile.ValidateElf(Elf(), executable: true); // PIE probe
        NativeBinaryProfile.ValidateElf(Elf(2), executable: true);
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeBinaryProfile.ValidateElf(Elf(2), executable: false));
        foreach (var index in new[] { 0, 4, 5, 6, 18, 20 })
        {
            var broken = Elf(); broken[index] = 0;
            Assert.ThrowsExactly<InvalidOperationException>(() => NativeBinaryProfile.ValidateElf(broken, executable: false));
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeBinaryProfile.ValidateElf(new byte[63], executable: false));
    }
}
