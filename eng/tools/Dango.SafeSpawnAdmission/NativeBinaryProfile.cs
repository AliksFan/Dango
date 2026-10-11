using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dango.Physics;

namespace Dango.SafeSpawnAdmission;

// Admission-tool provenance gate only. This does not alter production loading
// or accept an arbitrary expected module hash from the command line.
internal static class NativeBinaryProfile
{
    internal const string FrozenDll = "59EC861EE748BA93E661D058EEBD01D3E8A04ABF9D24F0376BACB4D98015B6DC";
    internal const string Box2DCommit = "8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3";
    internal const string ArchiveHash = "FF94DDF7D75FFA6171502FDC7A7F336594560278978305CFC56C72268BC66721";
    internal const string LicenseHash = "68A3E676D7E94093B102D5CBA0D4E04AF812040D6F230C3DB67A6664574E43D2";
    internal static readonly string[] Exports =
    [
        "dango_physics_get_abi_version", "dango_physics_get_allocated_bytes",
        "dango_physics_get_authority_abi_version", "dango_physics_get_authority_capabilities", "dango_physics_get_authority_frame_size",
        "dango_physics_get_prescribed_abi_version", "dango_physics_get_prescribed_step_size", "dango_physics_get_prescribed_trace_size",
        "dango_physics_get_presentation_target_size", "dango_physics_get_round_ball_abi_version", "dango_physics_get_round_ball_frame_size",
        "dango_physics_get_round_ball_gravity_scale", "dango_physics_world_add", "dango_physics_world_add_round_ball",
        "dango_physics_world_apply_presentation_targets", "dango_physics_world_bind_prescribed_epoch",
        "dango_physics_world_clear_horizontal_bounds", "dango_physics_world_clear_static_platform",
        "dango_physics_world_create", "dango_physics_world_destroy", "dango_physics_world_enable_authority",
        "dango_physics_world_export_checkpoint", "dango_physics_world_get_authority_frame", "dango_physics_world_get_round_ball_frame",
        "dango_physics_world_get_static_platform_contact", "dango_physics_world_import_checkpoint", "dango_physics_world_remove",
        "dango_physics_world_remove_round_ball", "dango_physics_world_reset", "dango_physics_world_set_horizontal_bounds",
        "dango_physics_world_set_preview_jump_speed_scale", "dango_physics_world_set_static_platform", "dango_physics_world_step",
        "dango_physics_world_step_prescribed", "dango_physics_world_translate_horizontal"
    ];
    private static readonly string[] Fields = ["Version", "Platform", "SourceHash", "ModuleHash", "NativeProbeHash", "Box2DCommit",
        "Box2DArchiveHash", "Box2DLicenseHash", "BuildConfiguration", "AbiVersion", "Compiler", "Exports"];

    internal sealed record Profile(string Platform, string SourceHash, string ModuleHash, string BuildRecordHash);
    internal static string Platform => RuntimeInformation.ProcessArchitecture == Architecture.X64
        ? OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsLinux() ? "linux-x64" : "unsupported"
        : "unsupported";
    internal static string Root => Path.GetFullPath("../../..", Path.GetFullPath("../../..", AppContext.BaseDirectory));
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static string SourceHash()
    {
        var root = Root;
        var files = new[] { "eng/tools/Dango.SafeSpawnAdmission", "eng/tools/Dango.SafeSpawnAdmission.Core", "src/Dango.Physics", "eng/native/box2d" }
            .SelectMany(part => Directory.EnumerateFiles(Path.Combine(root, part), "*", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
            .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/')));
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static Profile Validate(string module, string probe, string? buildRecord)
    {
        var sourceHash = SourceHash();
        var moduleHash = Hash(File.ReadAllBytes(module));
        if (Platform == "win-x64")
        {
            Require(buildRecord is null, "Windows does not accept a Linux build profile.");
            Require(moduleHash == FrozenDll, "Wrong frozen R1 binary.");
            return new(Platform, sourceHash, moduleHash, "");
        }
        Require(Platform == "linux-x64", "Only actual Windows x64 or Linux x64 processes are supported.");
        Require(!string.IsNullOrEmpty(buildRecord), "Linux requires --linux-build-record.");
        var bytes = File.ReadAllBytes(buildRecord!);
        ValidateRecord(bytes, sourceHash, moduleHash, Hash(File.ReadAllBytes(probe)));
        using var dependency = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "eng/native/box2d/dependency.json")));
        var dep = dependency.RootElement;
        Require(dep.GetProperty("commit").GetString() == Box2DCommit && dep.GetProperty("sourceArchive").GetProperty("sha256").GetString() == ArchiveHash &&
            dep.GetProperty("license").GetProperty("sha256").GetString() == LicenseHash, "Fixed Box2D dependency differs.");
        Require(Hash(File.ReadAllBytes(Path.Combine(Root, "eng/native/box2d/LICENSE.box2d.txt"))) == LicenseHash, "Actual Box2D license differs.");
        ValidateElf(File.ReadAllBytes(module), executable: false);
        ValidateElf(File.ReadAllBytes(probe), executable: true);
        return new(Platform, sourceHash, moduleHash, Hash(bytes));
    }

    // Pure record validation is also exercised on Windows without pretending to
    // be a Linux process or bypassing the real platform gate above.
    internal static void ValidateRecord(byte[] bytes, string sourceHash, string moduleHash, string probeHash)
    {
        using var document = JsonDocument.Parse(bytes);
        var record = document.RootElement;
        Require(record.ValueKind == JsonValueKind.Object, "Build record must be an object.");
        var names = record.EnumerateObject().Select(p => p.Name).ToArray();
        Require(names.Length == Fields.Length && names.Distinct(StringComparer.Ordinal).Count() == names.Length &&
            Fields.All(f => names.Contains(f, StringComparer.Ordinal)), "Build record has missing, unknown or duplicate fields.");
        Require(record.GetProperty("Version").ValueKind == JsonValueKind.Number && record.GetProperty("Version").TryGetInt32(out var version) && version == 1,
            "Unsupported build record version.");
        Require(record.GetProperty("AbiVersion").ValueKind == JsonValueKind.Number && record.GetProperty("AbiVersion").TryGetInt32(out var abi) && abi == 1,
            "Build record ABI must be 1.");
        string Text(string field)
        {
            var value = record.GetProperty(field);
            Require(value.ValueKind == JsonValueKind.String, $"{field} must be a string.");
            return value.GetString()!;
        }
        Require(Text("Platform") == "linux-x64", "Build record platform must be linux-x64.");
        Require(Text("BuildConfiguration") == "Release", "Build record must describe Release.");
        Require(!string.IsNullOrWhiteSpace(Text("Compiler")), "Build record compiler is missing.");
        foreach (var field in new[] { "SourceHash", "ModuleHash", "NativeProbeHash", "Box2DArchiveHash", "Box2DLicenseHash" })
            Require(Text(field).Length == 64 && Text(field).All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F'), $"{field} must be uppercase SHA256.");
        Require(Text("SourceHash") == sourceHash, "Current source hash differs.");
        Require(Text("ModuleHash") == moduleHash, "Actual module hash differs.");
        Require(Text("NativeProbeHash") == probeHash, "Actual native probe hash differs.");
        Require(Text("Box2DCommit") == Box2DCommit && Text("Box2DArchiveHash") == ArchiveHash && Text("Box2DLicenseHash") == LicenseHash,
            "Build record fixed Box2D source differs.");
        var exports = record.GetProperty("Exports");
        Require(exports.ValueKind == JsonValueKind.Array && exports.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String), "Exports must be strings.");
        Require(exports.EnumerateArray().Select(v => v.GetString()).SequenceEqual(Exports), "Build record export list differs.");
    }

    internal static void ValidateElf(byte[] bytes, bool executable)
    {
        Require(bytes.Length >= 64 && bytes[0] == 0x7f && bytes[1] == 'E' && bytes[2] == 'L' && bytes[3] == 'F' &&
            bytes[4] == 2 && bytes[5] == 1 && bytes[6] == 1 && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(18, 2)) == 62 &&
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20, 4)) == 1, "Native binary must be little-endian ELF x86-64.");
        var type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(16, 2));
        Require(executable ? type is 2 or 3 : type == 3, "ELF module/probe type differs.");
    }

    internal static void ValidateLoader(string module, bool allExports)
    {
        // Exercise the existing real ABI/export loader before any Create. Its
        // IDisposable releases only the library; no native world is allocated.
        var api = typeof(DangoPhysicsWorld).GetNestedType("NativeApi", BindingFlags.NonPublic)!;
        try
        {
            using var loaded = (IDisposable)api.GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [module])!;
        }
        catch (TargetInvocationException error) { throw new InvalidOperationException("Actual production native loader rejected the binary.", error.InnerException); }
        if (!allExports) return;
        var handle = NativeLibrary.Load(module);
        try { foreach (var export in Exports) Require(NativeLibrary.TryGetExport(handle, export, out _), $"Actual module export missing: {export}"); }
        finally { NativeLibrary.Free(handle); }
    }
    private static void Require(bool pass, string error) { if (!pass) throw new InvalidOperationException(error); }
}
