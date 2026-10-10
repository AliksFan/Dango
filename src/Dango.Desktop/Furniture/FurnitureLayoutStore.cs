using System.IO;
using System.Text.Json;
using Dango.Core.Furniture;

namespace Dango.Desktop.Furniture;

public enum FurnitureLayoutLoadState { Missing, Valid, Protected, TransientReadFailure }
public enum FurnitureCommitState { Committed, NotCommitted, Unknown }

public sealed class FurnitureLayoutLoadResult
{
    internal FurnitureLayoutLoadResult(FurnitureLayoutLoadState state, FurnitureLayout? layout,
        byte[]? bytes = null, string? warning = null)
    { State = state; Layout = layout; Bytes = bytes; Warning = warning; }

    public FurnitureLayoutLoadState State { get; }
    public FurnitureLayout? Layout { get; }
    public string? Warning { get; }
    public bool IsWritable => State is FurnitureLayoutLoadState.Missing or FurnitureLayoutLoadState.Valid;
    public byte[]? CopyRawBytes() => Bytes?.ToArray();
    internal byte[]? Bytes { get; }
}

public sealed record FurnitureCommitResult(FurnitureCommitState State, string? Warning = null);

/// <summary>Separates slow staging from the final replacement at the physical step boundary.</summary>
public interface IFurnitureFileWriter
{
    void WriteStaged(string stagedPath, byte[] bytes);
    void Commit(string stagedPath, string targetPath, bool targetExisted);
}

public sealed class FurnitureAtomicFileWriter : IFurnitureFileWriter
{
    public void WriteStaged(string stagedPath, byte[] bytes)
    {
        using var output = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
    }

    public void Commit(string stagedPath, string targetPath, bool targetExisted)
    {
        if (targetExisted) File.Replace(stagedPath, targetPath, destinationBackupFileName: null);
        else File.Move(stagedPath, targetPath, overwrite: false);
    }
}

public sealed class FurnitureLayoutStore
{
    private readonly IFurnitureFileWriter writer;
    public FurnitureLayoutStore(IFurnitureFileWriter? writer = null) => this.writer = writer ?? new FurnitureAtomicFileWriter();

    public FurnitureLayoutLoadResult Load(string path)
    {
        path = NormalizePath(path);
        try
        {
            EnsureRegularFile(path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is < 1 or > FurnitureLayoutSerializer.MaximumBytes)
                return new(FurnitureLayoutLoadState.Protected, null, warning: "布局文件大小无效；原件已保护。");
            var bytes = new byte[checked((int)input.Length)];
            input.ReadExactly(bytes);
            // Replacing an open file cannot make this snapshot partially parsed.
            var layout = FurnitureLayoutSerializer.Deserialize(bytes);
            return new(FurnitureLayoutLoadState.Valid, layout, bytes);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { return new(FurnitureLayoutLoadState.Missing, FurnitureLayout.Empty); }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
        { return new(FurnitureLayoutLoadState.Protected, null, warning: $"布局无法识别；原件已保护（{error.GetType().Name}）。"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return new(FurnitureLayoutLoadState.TransientReadFailure, null, warning: $"布局暂时无法读取；原件已保护（{error.GetType().Name}）。"); }
    }

    public FurnitureLayoutLease AcquireLease(string path)
    {
        path = NormalizePath(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        EnsurePlainDirectory(directory);
        var lockPath = path + ".lock";
        try { EnsureRegularFile(lockPath); }
        catch (FileNotFoundException) { }
        var handle = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { return new(this, writer, path, handle, Load(path)); }
        catch { handle.Dispose(); throw; }
    }

    public static string GetLayoutPath(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        return Path.Combine(Path.GetFullPath(dataRoot), "furniture-layout.json");
    }

    internal static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    internal static void EnsureRegularFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("布局及租约文件不能是目录或链接。");
    }

    private static void EnsurePlainDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("布局目录不能是链接。");
    }
}

public sealed class FurnitureLayoutLease : IDisposable
{
    private readonly FurnitureLayoutStore store;
    private readonly IFurnitureFileWriter writer;
    private readonly FileStream handle;
    private FurnitureLayoutStage? pending;
    private bool disposed;
    private bool unknown;

    internal FurnitureLayoutLease(FurnitureLayoutStore store, IFurnitureFileWriter writer, string path,
        FileStream handle, FurnitureLayoutLoadResult baseline)
    { this.store = store; this.writer = writer; Path = path; this.handle = handle; Baseline = baseline; }

    public string Path { get; }
    public FurnitureLayoutLoadResult Baseline { get; private set; }
    public byte[]? GetBaselineBytes() => Baseline.CopyRawBytes();

    public FurnitureLayoutStage Stage(FurnitureLayout candidate, long expectedVersion)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (unknown) throw new InvalidOperationException("上次保存结果待确认，禁止继续覆盖。");
        if (pending is not null) throw new InvalidOperationException("已有布局提交正在进行。");
        if (!Baseline.IsWritable) throw new InvalidDataException("原布局受保护，禁止覆盖。");
        candidate.Validate();
        if (Baseline.Layout!.LayoutVersion != expectedVersion || expectedVersion == long.MaxValue ||
            candidate.LayoutVersion != expectedVersion + 1)
            throw new InvalidOperationException("布局保存版本冲突。");
        var bytes = FurnitureLayoutSerializer.Serialize(candidate);
        var temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!,
            ".furniture-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            writer.WriteStaged(temporary, bytes);
            var staged = store.Load(temporary);
            if (staged.State != FurnitureLayoutLoadState.Valid || !bytes.AsSpan().SequenceEqual(staged.Bytes) || staged.Layout != candidate)
                throw new InvalidDataException("临时布局重读校验失败。");
            pending = new(this, candidate, temporary, bytes);
            return pending;
        }
        catch { RemoveOwnTemporary(temporary); throw; }
    }

    internal FurnitureCommitResult Commit(FurnitureLayoutStage stage, string temporary, byte[] candidateBytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(pending, stage)) throw new InvalidOperationException("布局提交不属于当前租约。");
        var current = store.Load(Path);
        if (!SameSnapshot(current, Baseline))
        {
            unknown = true;
            return new(FurnitureCommitState.Unknown, "布局已被外部修改或暂时不可读；禁止覆盖。");
        }
        var staged = store.Load(temporary);
        if (staged.State != FurnitureLayoutLoadState.Valid || !candidateBytes.AsSpan().SequenceEqual(staged.Bytes))
            return new(FurnitureCommitState.NotCommitted, "临时布局已改变或无法读取；原布局保留。");
        string? warning = null;
        try { writer.Commit(temporary, Path, Baseline.State == FurnitureLayoutLoadState.Valid); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or InvalidOperationException or NotSupportedException)
        { warning = $"文件提交返回异常（{error.GetType().Name}），已重新确认目标内容。"; }
        var observed = store.Load(Path);
        if (observed.State == FurnitureLayoutLoadState.Valid && candidateBytes.AsSpan().SequenceEqual(observed.Bytes))
        {
            Baseline = observed;
            return new(FurnitureCommitState.Committed, warning);
        }
        if (SameSnapshot(observed, Baseline))
            return new(FurnitureCommitState.NotCommitted, warning ?? "目标内容未提交，原布局保留。");
        unknown = true;
        return new(FurnitureCommitState.Unknown, "文件保存结果待确认；禁止继续覆盖。");
    }

    internal void EndStage(FurnitureLayoutStage stage)
    {
        if (ReferenceEquals(pending, stage)) pending = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        pending?.Dispose();
        disposed = true;
        handle.Dispose();
    }

    private static bool SameSnapshot(FurnitureLayoutLoadResult current, FurnitureLayoutLoadResult expected) =>
        expected.State == FurnitureLayoutLoadState.Missing
            ? current.State == FurnitureLayoutLoadState.Missing
            : expected.State == FurnitureLayoutLoadState.Valid && current.State == FurnitureLayoutLoadState.Valid &&
                expected.Bytes!.AsSpan().SequenceEqual(current.Bytes);

    internal static void RemoveOwnTemporary(string path)
    {
        try
        {
            FurnitureLayoutStore.EnsureRegularFile(path);
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { }
    }
}

public sealed class FurnitureLayoutStage : IDisposable
{
    private readonly FurnitureLayoutLease owner;
    private readonly string temporary;
    private readonly byte[] bytes;
    private bool disposed;
    private FurnitureCommitResult? result;

    internal FurnitureLayoutStage(FurnitureLayoutLease owner, FurnitureLayout candidate, string temporary, byte[] bytes)
    { this.owner = owner; Candidate = candidate; this.temporary = temporary; this.bytes = bytes; }

    public FurnitureLayout Candidate { get; }

    public FurnitureCommitResult Commit()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return result ??= owner.Commit(this, temporary, bytes);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        FurnitureLayoutLease.RemoveOwnTemporary(temporary);
        owner.EndStage(this);
    }
}
