using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.RoundBallAdmission;

var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = true
};
try
{
    if (args.Length == 2 && args[0] == "--write-synthetic-example")
    {
        WriteNew(args[1], ProbeSelfTests.SyntheticInput());
        Console.WriteLine("Wrote test-only synthetic geometry example; no native state was captured.");
        return 0;
    }
    var selfTest = args.Length is 1 or 3 && args[0] == "--self-test";
    var external = args.Length is 2 or 4 && args[0] == "--frames";
    if ((!selfTest && !external) || (selfTest && args.Length == 3 && args[1] != "--output") ||
        (external && args.Length == 4 && args[2] != "--output"))
    {
        Console.Error.WriteLine("Usage: --self-test [--output NEW.json] | --frames INPUT.json [--output NEW.json] | --write-synthetic-example NEW.json");
        Console.Error.WriteLine("Pure data only. No WPF, native world/Step, real DPI/keyboard or no-ball regression acceptance.");
        return 2;
    }
    var output = selfTest && args.Length == 3 ? args[2] : external && args.Length == 4 ? args[3] : null;
    if (output is not null && (File.Exists(output) || Directory.Exists(output)))
        throw new IOException("Output must be a new path; existing evidence is never overwritten.");
    ProbeInput input;
    string? inputHash = null;
    if (selfTest) input = ProbeSelfTests.SyntheticInput();
    else
    {
        var info = new FileInfo(args[1]);
        if (info.Length > 4 * 1024 * 1024) throw new InvalidDataException("Input exceeds the 4 MiB data-fixture limit.");
        var bytes = File.ReadAllBytes(args[1]);
        using var document = JsonDocument.Parse(bytes);
        RequirePublicBodyFields(document.RootElement);
        input = JsonSerializer.Deserialize<ProbeInput>(bytes, json) ?? throw new InvalidDataException("Input was null.");
        inputHash = Convert.ToHexString(SHA256.HashData(bytes));
    }
    var report = ProbeVerifier.Verify(input, inputHash, selfTest);
    if (output is null) Console.WriteLine(JsonSerializer.Serialize(report, json));
    else
    {
        WriteNew(output, report);
        Console.WriteLine($"Projection contract: {report.Passed} passed, {report.Failed} failed; source={report.SourceKind}.");
        Console.WriteLine("Native dynamics/I8, baseline DLL verification and real HWND/DPI remain unverified.");
    }
    return report.Failed == 0 ? 0 : 1;
}
catch (Exception error) when (error is IOException or JsonException or InvalidDataException or ArgumentException or KeyNotFoundException or InvalidOperationException)
{
    Console.Error.WriteLine("Probe input/output error: " + error.Message);
    return 2;
}

void WriteNew<T>(string path, T value)
{
    var fullPath = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    JsonSerializer.Serialize(stream, value, json);
}

static void RequirePublicBodyFields(JsonElement root)
{
    // Existing DTO constructor fields are all required here; omitted fields cannot
    // silently become zero and masquerade as a complete public native frame.
    foreach (var frame in root.GetProperty("frames").EnumerateArray())
    {
        var body = frame.GetProperty("body");
        foreach (var name in new[] { "entityId", "contour", "coreX", "coreY", "signedArea", "centroidX", "centroidY",
            "maximumSpeed", "groundContactCount", "isGrounded", "selectedDragNode", "pointCount" })
            _ = body.GetProperty(name);
        _ = body.GetProperty("entityId").GetProperty("value");
        _ = frame.GetProperty("sceneRevision");
        _ = frame.GetProperty("successfulStep");
        _ = frame.GetProperty("ballSourceKind");
        var ball = frame.GetProperty("ball");
        foreach (var name in new[] { "entityId", "centerQX", "centerQY", "radiusQ", "angleRadians", "velocityQX",
            "velocityQY", "angularVelocity", "isSleeping" })
            _ = ball.GetProperty(name);
    }
}
