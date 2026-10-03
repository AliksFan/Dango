using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dango.Protocol;
using Dango.Client.Sync;

namespace Dango.MultiplayerSample;

internal static partial class SessionFiles
{
    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();

    public static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dango.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository_required");
    }

    public static string Resolve(string repository, string name)
    {
        if (!SafeName().IsMatch(name)) throw new ArgumentException("invalid_session_name");
        var root = Path.GetFullPath(repository);
        var result = Path.GetFullPath(Path.Combine(root, "artifacts", "multiplayer-sync", name));
        var current = new DirectoryInfo(result);
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("reparse_point_not_allowed");
            if (string.Equals(current.FullName, root, StringComparison.OrdinalIgnoreCase)) break;
            current = current.Parent;
        }
        return result;
    }

    public static string Create(string repository, string name)
    {
        var result = Resolve(repository, name);
        if (Directory.Exists(result) || File.Exists(result)) throw new InvalidOperationException("session_already_exists");
        Directory.CreateDirectory(result);
        Directory.CreateDirectory(Path.Combine(result, "A"));
        Directory.CreateDirectory(Path.Combine(result, "B"));
        return result;
    }

    public static string ResolveClient(string session, string role)
    {
        if (role is not ("A" or "B")) throw new ArgumentException("invalid_role");
        var path = Path.Combine(session, role);
        var info = new DirectoryInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("invalid_client_directory");
        return info.FullName;
    }

    public static void Write(string directory, string name, object value) =>
        File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, RoomProtocol.Json));

    public static object Fingerprint(string path) => new
    {
        file = Path.GetFileName(path),
        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
        size = new FileInfo(path).Length,
    };
}

internal sealed record SampleCommand(string Id, string Type, string? Direction = null, long? ServerTimeMs = null);
internal sealed record SampleReport(string Id, bool Success, string Code, int Pid, string State,
    RoomSnapshot? Snapshot, long SampledAtServerTimeMs, long RenderedFrames, int FallbackPets, bool Visible,
    RoomConnectionDiagnostics Diagnostics);
