using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dango.Desktop.Accessories.Editing;

internal static class AccessoryEditorAssets
{
    internal const string SourceCommit = "a79733eb803fc97084ef0ee4faa96b031e69e1c0";
    internal const string BundleVersion = "4.14.3-dango-e0.1";
    internal const long MaximumBundleBytes = 16 * 1024 * 1024;

    internal static IReadOnlySet<string> Verify(string directory)
    {
        directory = Path.GetFullPath(directory);
        AssertNotLinked(directory);
        var manifest = Path.Combine(directory, "manifest.json");
        AssertNotLinked(manifest);
        if (new FileInfo(manifest).Length is < 1 or > 128 * 1024)
            throw new InvalidDataException("编辑器资源清单大小无效。");
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifest), new() { MaxDepth = 8 });
        var root = AccessoryEditorBridge.ExactObject(document.RootElement, ["sourceCommit", "version", "files"]);
        if (root["sourceCommit"].GetString() != SourceCommit || root["version"].GetString() != BundleVersion ||
            root["files"].ValueKind != JsonValueKind.Array || root["files"].GetArrayLength() is < 3 or > 128)
            throw new InvalidDataException("编辑器资源版本无效。");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var item in root["files"].EnumerateArray())
        {
            var fields = AccessoryEditorBridge.ExactObject(item, ["path", "sha256", "bytes"]);
            var relative = fields["path"].GetString();
            if (string.IsNullOrEmpty(relative) || relative.Length > 160 ||
                relative.Split('/').Any(part => part.Length == 0 || part is "." or "..") ||
                relative.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '/' and not '-' and not '_' and not '.') ||
                !paths.Add(relative) || !fields["bytes"].TryGetInt64(out var bytes) || bytes < 1 ||
                bytes > MaximumBundleBytes || (total += bytes) > MaximumBundleBytes)
                throw new InvalidDataException("编辑器资源引用无效。");
            var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            AssertNotLinked(path);
            using var input = File.OpenRead(path);
            if (input.Length != bytes || Convert.ToHexStringLower(SHA256.HashData(input)) != fields["sha256"].GetString())
                throw new InvalidDataException("编辑器资源校验失败；请恢复完整软件包后重试。");
        }
        if (!new[] { "index.html", "bundle.js", "theme.css", "licenses/MIT-minipaint.txt" }.All(paths.Contains))
            throw new InvalidDataException("编辑器资源不完整。");
        return paths;
    }

    private static void AssertNotLinked(string path)
    {
        for (string? cursor = path; cursor is not null; cursor = Path.GetDirectoryName(cursor))
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("编辑器资源不能经过链接。");
    }
}
