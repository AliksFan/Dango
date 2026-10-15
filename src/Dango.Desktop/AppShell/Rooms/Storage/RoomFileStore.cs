using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Desktop.Furniture;

namespace Dango.Desktop.AppShell.Rooms.Storage;

internal enum RoomFileState { Missing, Present, Protected }
internal sealed record RoomFileSnapshot(RoomFileState State, byte[]? Bytes = null, string? Warning = null);
internal sealed record RoomDocument<T>(T? Value, RoomFileSnapshot Snapshot) where T : class
{
    internal bool Valid => Value is not null && Snapshot.State == RoomFileState.Present;
}

internal static class RoomJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };
    internal static byte[] Write<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
    internal static T Read<T>(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        CheckDuplicates(document.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, Options) ?? throw new InvalidDataException("存档内容为空。");
    }
    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("存档含重复字段。");
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckDuplicates(item);
    }
}

/// <summary>Bounded plain files, explicit roots, lock ordering and observed atomic outcomes.</summary>
internal sealed class RoomFileStore
{
    private readonly IFurnitureFileWriter writer;
    internal RoomFileStore(string root, IFurnitureFileWriter? writer = null)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        this.writer = writer ?? new FurnitureAtomicFileWriter();
    }
    internal string Root { get; }
    internal string PathFor(string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("存档路径必须相对数据目录。");
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        if (!path.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("存档路径越界。");
        Guard(path);
        return path;
    }
    private void Guard(string path)
    {
        var current = path;
        while (current.Length >= Root.Length)
        {
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("存档路径不能是链接。");
                if (current == path && (attributes & FileAttributes.Directory) != 0) throw new InvalidDataException("存档文件不能是目录。");
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
            if (string.Equals(current, Root, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current)!;
        }
    }
    internal RoomFileSnapshot Read(string relative)
    {
        try
        {
            var path = PathFor(relative);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is < 1 or > 1024 * 1024) throw new InvalidDataException("存档大小无效。");
            var bytes = new byte[(int)input.Length];
            input.ReadExactly(bytes);
            return new(RoomFileState.Present, bytes);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { return new(RoomFileState.Missing); }
        catch (Exception error) when (IsStorageError(error))
        { return new(RoomFileState.Protected, Warning: $"存档暂不可读或无法识别；原件已保护（{error.GetType().Name}）。"); }
    }
    internal RoomDocument<T> Read<T>(string relative, Action<T> validate) where T : class
    {
        var snapshot = Read(relative);
        if (snapshot.State != RoomFileState.Present) return new(null, snapshot);
        try { var value = RoomJson.Read<T>(snapshot.Bytes!); validate(value); return new(value, snapshot); }
        catch (Exception error) when (IsStorageError(error))
        { return new(null, snapshot with { State = RoomFileState.Protected, Warning = "存档格式无法识别；原件已保护。" }); }
    }
    internal IDisposable Acquire(string relative)
    {
        var path = PathFor(relative + ".lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Guard(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    internal FurnitureCommitResult Write(string relative, RoomFileSnapshot baseline, byte[] candidate, Action<byte[]> validate, bool preserveBaseline = true)
    {
        if (baseline.State == RoomFileState.Protected) return new(FurnitureCommitState.NotCommitted, "原件受保护，禁止覆盖。");
        var path = PathFor(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Guard(path);
        var temporaryRelative = Path.GetRelativePath(Root, Path.Combine(Path.GetDirectoryName(path)!, ".room-" + Guid.NewGuid().ToString("N") + ".tmp"));
        var temporary = PathFor(temporaryRelative);
        try
        {
            validate(candidate);
            writer.WriteStaged(temporary, candidate);
            var staged = Read(temporaryRelative);
            if (staged.State != RoomFileState.Present || !candidate.AsSpan().SequenceEqual(staged.Bytes))
                return new(FurnitureCommitState.NotCommitted, "暂存校验失败；原件保留。");
            validate(staged.Bytes!);
            if (preserveBaseline && baseline.State == RoomFileState.Present)
            {
                var previous = Read(relative + ".previous");
                if (previous.State == RoomFileState.Protected) return new(FurnitureCommitState.NotCommitted, "保护副本不可读，未覆盖原件。");
                if (previous.State == RoomFileState.Present) validate(previous.Bytes!);
                var protectedCopy = Write(relative + ".previous", previous, baseline.Bytes!, validate, preserveBaseline: false);
                if (protectedCopy.State != FurnitureCommitState.Committed)
                    return new(FurnitureCommitState.NotCommitted, "保护副本未能确认，原件未提交。");
            }
            if (!Same(Read(relative), baseline)) return new(FurnitureCommitState.Unknown, "存档已被外部修改或不可读，禁止继续覆盖。");
            try { writer.Commit(temporary, path, baseline.State == RoomFileState.Present); }
            catch (Exception error) when (IsStorageError(error)) { /* Always observe the target, even when replacement reported failure. */ }
            var observed = Read(relative);
            if (observed.State == RoomFileState.Present && candidate.AsSpan().SequenceEqual(observed.Bytes))
                return new(FurnitureCommitState.Committed);
            return Same(observed, baseline) ? new(FurnitureCommitState.NotCommitted, "保存未完成；原件保留。") :
                new(FurnitureCommitState.Unknown, "保存结果待核对，禁止继续覆盖。");
        }
        catch (Exception error) when (IsStorageError(error))
        { return new(FurnitureCommitState.NotCommitted, $"保存未完成；原件保留（{error.GetType().Name}）。"); }
        finally
        {
            // Only this operation's unique temporary file; persistent saves and backups are never cleaned.
            try { Guard(temporary); File.Delete(temporary); }
            catch (Exception error) when (IsStorageError(error)) { }
        }
    }
    internal static bool Same(RoomFileSnapshot first, RoomFileSnapshot second) => first.State == second.State &&
        (first.State == RoomFileState.Missing || (first.State == RoomFileState.Present && first.Bytes!.AsSpan().SequenceEqual(second.Bytes)));
    internal static bool IsStorageError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or
        JsonException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException or System.Security.SecurityException;
}
