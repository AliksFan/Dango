using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Dango.Desktop.Verification;

namespace Dango.Desktop.AppShell.Persistence;

/// <summary>
/// Flushes same-directory temporary files before replacing a target. A named mutex coordinates
/// cooperating Dango writers across processes and Windows sessions. Other applications can still
/// change a file or its directory between the comparison and replacement; this is not a
/// filesystem-wide compare-and-swap or protection against hostile directory changes.
/// </summary>
public sealed class DurableAtomicTextWriter : IAtomicTextWriter
{
    private const int MaximumSnapshotBytes = 65536;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public void Write(string path, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var fullPath = NormalizePath(path);
        var bytes = Utf8.GetBytes(content);
        using var ownership = AcquireMutex(fullPath);
        ValidateLockedPath(fullPath);
        ReplaceLocked(fullPath, bytes);
    }

    public static ExactFileSnapshot Capture(string path, int maximumBytes = MaximumSnapshotBytes)
    {
        ValidateMaximumBytes(maximumBytes);
        var fullPath = NormalizePath(path);
        using var ownership = AcquireMutex(fullPath);
        ValidateLockedPath(fullPath);
        return ReadSnapshotLocked(fullPath, maximumBytes);
    }

    public bool TryReplace(string path, ExactFileSnapshot expected, string content,
        int maximumBytes = MaximumSnapshotBytes)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(content);
        ValidateMaximumBytes(maximumBytes);
        var fullPath = NormalizePath(path);
        if (Utf8.GetByteCount(content) > maximumBytes)
            throw new InvalidDataException("Replacement exceeds the bounded snapshot size.");
        var bytes = Utf8.GetBytes(content);
        using var ownership = AcquireMutex(fullPath);
        ValidateLockedPath(fullPath);
        // Always read after acquiring ownership, including after an abandoned mutex.
        if (!expected.Matches(ReadSnapshotLocked(fullPath, maximumBytes)))
            return false;
        ReplaceLocked(fullPath, bytes);
        return true;
    }

    private static void ValidateMaximumBytes(int maximumBytes)
    {
        if (maximumBytes is < 0 or > MaximumSnapshotBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Snapshot limits must be between 0 and 65536 bytes.");
    }

    private static ExactFileSnapshot ReadSnapshotLocked(string path, int maximumBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.SequentialScan);
            if (stream.Length > maximumBytes)
                throw new InvalidDataException("Existing file exceeds the bounded snapshot size.");
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1)
                throw new InvalidDataException("Existing file changed while reading its bounded snapshot.");
            return ExactFileSnapshot.FromBytes(bytes);
        }
        catch (FileNotFoundException) { return ExactFileSnapshot.Missing; }
        catch (DirectoryNotFoundException) { return ExactFileSnapshot.Missing; }
    }

    private static void ReplaceLocked(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        ValidateLockedPath(path);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var ownsTemporaryFile = false;
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporaryFile = true;
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
            ownsTemporaryFile = false;
        }
        finally
        {
            if (ownsTemporaryFile)
                File.Delete(temporaryPath);
        }
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateWindowsSegments(path);
        var fullPath = Path.GetFullPath(path);
        if (string.IsNullOrEmpty(Path.GetFileName(fullPath)))
            throw new ArgumentException("A target file name is required.", nameof(path));
        ValidateWindowsSegments(fullPath);
        EnsureNoReparsePoints(fullPath);
        return ExpandExistingPath(fullPath);
    }

    private static void ValidateWindowsSegments(string path)
    {
        if (path.StartsWith("\\\\?\\", StringComparison.Ordinal) || path.StartsWith("\\\\.\\", StringComparison.Ordinal))
            throw new ArgumentException("Device and extended device paths are not accepted.", nameof(path));
        var rootLength = Path.GetPathRoot(path)?.Length ?? 0;
        foreach (var segment in path[rootLength..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
                continue;
            if (segment.EndsWith('.') || segment.EndsWith(' ') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Windows path aliases and alternate data streams are not accepted.", nameof(path));
            var stem = segment.Split('.')[0].TrimEnd(' ');
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                    stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
                throw new ArgumentException("Windows device names are not accepted.", nameof(path));
        }
    }

    private static void ValidateLockedPath(string path)
    {
        EnsureNoReparsePoints(path);
        if (!string.Equals(path, ExpandExistingPath(path), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The target path alias changed while acquiring the persistence lock.");
    }

    private static void EnsureNoReparsePoints(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Persistence through a reparse point is not accepted.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static string ExpandExistingPath(string path)
    {
        var suffix = new Stack<string>();
        var existing = path;
        while (true)
        {
            var buffer = new StringBuilder(32768);
            var length = GetLongPathNameW(existing, buffer, (uint)buffer.Capacity);
            if (length > 0)
            {
                if (length >= buffer.Capacity)
                    throw new PathTooLongException("The expanded persistence path is too long.");
                var expanded = buffer.ToString();
                while (suffix.TryPop(out var segment))
                    expanded = Path.Combine(expanded, segment);
                return expanded;
            }
            var error = Marshal.GetLastPInvokeError();
            if (error is not (2 or 3))
                throw new IOException("Cannot resolve the persistence path.", new Win32Exception(error));
            var parent = Path.GetDirectoryName(existing);
            if (parent is null)
                throw new DirectoryNotFoundException("The persistence path root does not exist.");
            suffix.Push(Path.GetFileName(existing));
            existing = parent;
        }
    }

    private static MutexOwnership AcquireMutex(string path)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        return new MutexOwnership($"Global\\Dango.DurableText.{key}");
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, uint bufferLength);

    private sealed class MutexOwnership : IDisposable
    {
        private readonly Mutex mutex;

        public MutexOwnership(string name)
        {
            mutex = new Mutex(initiallyOwned: false, name);
            try
            {
                try
                {
                    if (!mutex.WaitOne(TimeSpan.FromSeconds(10)))
                        throw new IOException("Timed out waiting for the Dango persistence writer.");
                }
                catch (AbandonedMutexException)
                {
                    // Windows has granted ownership; callers must reread before replacing.
                }
            }
            catch
            {
                mutex.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
