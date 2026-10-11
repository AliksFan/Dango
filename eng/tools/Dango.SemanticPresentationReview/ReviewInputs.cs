using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Physics;
using Dango.Physics.Presentation;

namespace Dango.SemanticPresentationReview;

internal sealed class ReviewInputs
{
    internal const string ModuleHash = "59EC861EE748BA93E661D058EEBD01D3E8A04ABF9D24F0376BACB4D98015B6DC";
    private static readonly Dictionary<string, string> TraceHashes = new()
    {
        ["push"] = "7D4178BAE57C38090924D012E479FD701DB558695BC4B038ED5A922F777FEFD8",
        ["stack"] = "3EF3C24E6FB81A96E32FC0620A1EDA727494094122550D501CBB62B896A224DD",
        ["withdraw"] = "02F6E209E095185B86CD36DA1F597368D08DE9BE67F0BF51E5E661B064BBE8F4",
    };
    private readonly Dictionary<string, string> files = [];
    internal string Library { get; }
    internal string Theme { get; }
    internal Dictionary<string, IReadOnlyList<SemanticPresentationSnapshot>> Traces { get; } = [];
    internal Dictionary<string, string> BeforeHashes { get; } = [];

    internal ReviewInputs(string library, string traceDirectory, string theme)
    {
        Library = Path.GetFullPath(library); Theme = Path.GetFullPath(theme);
        Pin("module", Library, ModuleHash);
        Pin("theme", Theme, Hash(Theme));
        foreach (var (scenario, expected) in TraceHashes)
        {
            var path = Path.Combine(traceDirectory, scenario + "-60hz.json");
            Pin(scenario, path, expected);
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.GetProperty("scenario").GetString() != scenario || root.GetProperty("moduleSha256").GetString() != ModuleHash ||
                !root.GetProperty("overallGatePassed").GetBoolean() || !root.GetProperty("completedFullTrajectory").GetBoolean() ||
                root.GetProperty("requiredFixedSteps").GetInt32() != 600 || root.GetProperty("stateRate").GetInt32() != 20)
                throw new InvalidDataException("trace-profile");
            var source = root.GetProperty("authoritativeSemanticTrace").Deserialize<DangoAuthorityFrame[]>()
                ?? throw new InvalidDataException("trace-empty");
            if (source.Length != 600 || source.Where((frame, index) => !frame.Succeeded || frame.Tick != (ulong)index + 1).Any())
                throw new InvalidDataException("trace-sequence");
            Traces.Add(scenario, Array.AsReadOnly(source.Select(frame => new SemanticPresentationSnapshot("semantic-fixture", (long)frame.Tick,
                frame.Bodies.Select(body => new SemanticBodyState(body.EntityId, body.CoreX, body.CoreY, body.CoreVelocityX, body.CoreVelocityY,
                    body.FloorGrounded, body.SupportEntityId)),
                frame.Pairs.Select(pair => new SemanticPairState(pair.EntityA, pair.EntityB, pair.NormalX, pair.NormalY, pair.Gap, pair.MinimumProjectedWidth)),
                sceneRevision: frame.SceneRevision)).ToArray()));
        }
    }

    private void Pin(string name, string path, string expected)
    {
        if (Hash(path) != expected) throw new InvalidDataException("input-hash:" + name);
        files.Add(name, path); BeforeHashes.Add(name, expected);
    }

    internal Dictionary<string, string> Recheck()
    {
        var result = files.ToDictionary(pair => pair.Key, pair => Hash(pair.Value));
        if (result.Any(pair => pair.Value != BeforeHashes[pair.Key])) throw new InvalidDataException("input-changed");
        return result;
    }

    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
