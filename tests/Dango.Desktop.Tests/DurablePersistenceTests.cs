using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Dango.Desktop.AppShell.Persistence;

namespace Dango.Desktop.Tests;

[TestClass]
public sealed class DurablePersistenceTests
{
    private string directory = null!;
    private string path = null!;

    [TestInitialize]
    public void CreateExclusiveDirectory()
    {
        directory = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "durable-persistence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "fictional-room.json");
    }

    [TestMethod]
    public void MissingAndExistingEmptySnapshotsRemainDistinct()
    {
        var writer = new DurableAtomicTextWriter();
        var missing = DurableAtomicTextWriter.Capture(path);
        Assert.IsFalse(missing.Exists);
        Assert.AreEqual(0, missing.Bytes.Length);

        File.WriteAllBytes(path, []);
        var empty = DurableAtomicTextWriter.Capture(path, maximumBytes: 0);
        Assert.IsTrue(empty.Exists);
        Assert.AreEqual(0, empty.Bytes.Length);
        Assert.IsFalse(writer.TryReplace(path, missing, "fictional replacement"));
        Assert.IsTrue(writer.TryReplace(path, empty, "fictional replacement"));
        Assert.AreEqual("fictional replacement", File.ReadAllText(path));
    }

    [TestMethod]
    public void SnapshotDefensivelyCopiesInputAndPublicMemory()
    {
        byte[] original = [0x41, 0x42];
        var snapshot = ExactFileSnapshot.FromBytes(original);
        original[0] = 0x58;
        Assert.IsTrue(MemoryMarshal.TryGetArray(snapshot.Bytes, out var exposed));
        exposed.Array![exposed.Offset] = 0x59;

        CollectionAssert.AreEqual(new byte[] { 0x41, 0x42 }, snapshot.Bytes.ToArray());
        Assert.IsTrue(snapshot.Exists);
        Assert.IsFalse(ExactFileSnapshot.Missing.Exists);
    }

    [TestMethod]
    public void CaptureKeepsExactInvalidUtf8AndBomBytesAndStaleSnapshotCannotOverwrite()
    {
        byte[] original = [0xEF, 0xBB, 0xBF, 0xFF, 0x41];
        File.WriteAllBytes(path, original);
        var expected = DurableAtomicTextWriter.Capture(path);
        CollectionAssert.AreEqual(original, expected.Bytes.ToArray());
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, 0xFE, 0x41]);

        Assert.IsFalse(new DurableAtomicTextWriter().TryReplace(path, expected, "replacement"));

        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF, 0xFE, 0x41 }, File.ReadAllBytes(path));
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
    }

    [TestMethod]
    public void MissingSnapshotCreatesFileButDoesNotReplaceAFileCreatedAfterCapture()
    {
        var missing = DurableAtomicTextWriter.Capture(path);
        var writer = new DurableAtomicTextWriter();
        Assert.IsTrue(writer.TryReplace(path, missing, "first fictional value"));
        Assert.IsFalse(writer.TryReplace(path, missing, "second fictional value"));
        Assert.AreEqual("first fictional value", File.ReadAllText(path));
    }

    [TestMethod]
    public void DeletedFileCannotMatchAnExistingSnapshot()
    {
        File.WriteAllText(path, "original");
        var expected = DurableAtomicTextWriter.Capture(path);
        File.Delete(path);

        Assert.IsFalse(new DurableAtomicTextWriter().TryReplace(path, expected, "replacement"));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void CaptureAndReplacementRejectExceededByteLimitsWithoutChangingFile()
    {
        File.WriteAllBytes(path, new byte[65536]);
        Assert.AreEqual(65536, DurableAtomicTextWriter.Capture(path).Bytes.Length);
        var expected = DurableAtomicTextWriter.Capture(path);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DurableAtomicTextWriter.Capture(path, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DurableAtomicTextWriter.Capture(path, 65537));
        Assert.ThrowsExactly<InvalidDataException>(() => DurableAtomicTextWriter.Capture(path, 65535));
        Assert.ThrowsExactly<InvalidDataException>(() => new DurableAtomicTextWriter().TryReplace(
            path, expected, new string('中', 21846)));
        CollectionAssert.AreEqual(new byte[65536], File.ReadAllBytes(path));

        File.WriteAllBytes(path, new byte[65537]);
        Assert.ThrowsExactly<InvalidDataException>(() => DurableAtomicTextWriter.Capture(path));
    }

    [TestMethod]
    public void BlockedReplacementPreservesOldFileAndUnownedTempThenRetrySucceeds()
    {
        const string old = "fictional old value";
        File.WriteAllText(path, old);
        var expected = DurableAtomicTextWriter.Capture(path);
        var unownedTemporaryPath = Path.Combine(directory, ".fictional-room.json.unowned.tmp");
        File.WriteAllText(unownedTemporaryPath, "unrelated sentinel");
        var writer = new DurableAtomicTextWriter();

        using (var blockingReader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Assert.Throws<Exception>(() => writer.TryReplace(path, expected, "new fictional value"));
            Assert.IsTrue(error is IOException or UnauthorizedAccessException);
            Assert.AreEqual(old, File.ReadAllText(path));
            CollectionAssert.AreEqual(new[] { unownedTemporaryPath }, Directory.GetFiles(directory, "*.tmp"));
        }

        Assert.IsTrue(writer.TryReplace(path, expected, "new fictional value"));
        Assert.AreEqual("new fictional value", File.ReadAllText(path));
        Assert.AreEqual("unrelated sentinel", File.ReadAllText(unownedTemporaryPath));
        CollectionAssert.AreEqual(new[] { unownedTemporaryPath }, Directory.GetFiles(directory, "*.tmp"));
    }

    [TestMethod]
    public void WriteUsesBomlessUtf8AndLeavesNoOwnedTemporaryFiles()
    {
        var writer = new DurableAtomicTextWriter();
        var nested = Path.Combine(directory, "new", "room.json");
        writer.Write(nested, "虚构房间");
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("虚构房间"), File.ReadAllBytes(nested));
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public void UnconditionalArchiveWriteMayExceedTheSnapshotLimit()
    {
        var archive = new string('A', 90000);
        new DurableAtomicTextWriter().Write(path, archive);

        Assert.AreEqual(archive, File.ReadAllText(path));
        Assert.ThrowsExactly<InvalidDataException>(() => DurableAtomicTextWriter.Capture(path));
    }

    [TestMethod]
    public void CaptureOfUnreadableExistingFileDoesNotPretendItIsMissing()
    {
        File.WriteAllText(path, "fictional original");
        using var blockingStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var error = Assert.Throws<Exception>(() => DurableAtomicTextWriter.Capture(path));
        Assert.IsTrue(error is IOException or UnauthorizedAccessException);
    }

    [TestMethod]
    public void AbandonedMutexIsAcquiredAndCurrentBytesAreReadAgain()
    {
        File.WriteAllText(path, "original");
        var expected = DurableAtomicTextWriter.Capture(path);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        using var abandoned = new Mutex(initiallyOwned: false, $"Global\\Dango.DurableText.{key}");
        var owner = new Thread(() => abandoned.WaitOne());
        owner.Start();
        Assert.IsTrue(owner.Join(TimeSpan.FromSeconds(5)));
        File.WriteAllText(path, "fictional change after abandoned owner");

        Assert.IsFalse(new DurableAtomicTextWriter().TryReplace(path, expected, "must not overwrite"));
        Assert.AreEqual("fictional change after abandoned owner", File.ReadAllText(path));
        Assert.AreEqual("fictional change after abandoned owner", Encoding.UTF8.GetString(DurableAtomicTextWriter.Capture(path).Bytes.Span));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReparseTargetsAndAncestorDirectoriesAreRejected(bool targetIsLink)
    {
        var actualDirectory = Path.Combine(directory, "actual");
        Directory.CreateDirectory(actualDirectory);
        var actualPath = Path.Combine(actualDirectory, "room.json");
        File.WriteAllText(actualPath, "fictional original");
        var linkPath = Path.Combine(directory, "link");
        try
        {
            if (targetIsLink)
                File.CreateSymbolicLink(linkPath, actualPath);
            else
                Directory.CreateSymbolicLink(linkPath, actualDirectory);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Inconclusive("This host does not permit creating an exclusive test symbolic link.");
        }
        catch (IOException exception) when (exception.HResult == unchecked((int)0x80070522))
        {
            Assert.Inconclusive("This host lacks the privilege required to create an exclusive test symbolic link.");
        }
        var alias = targetIsLink ? linkPath : Path.Combine(linkPath, "room.json");
        var writer = new DurableAtomicTextWriter();

        Assert.ThrowsExactly<IOException>(() => DurableAtomicTextWriter.Capture(alias));
        Assert.ThrowsExactly<IOException>(() => writer.Write(alias, "must not overwrite"));
        Assert.ThrowsExactly<IOException>(() => writer.TryReplace(alias, ExactFileSnapshot.Missing, "must not overwrite"));
        Assert.AreEqual("fictional original", File.ReadAllText(actualPath));
    }

    [TestMethod]
    public void DirectoryJunctionAncestorIsRejectedAndOriginalBytesRemainIntact()
    {
        var actualDirectory = Path.Combine(directory, "actual");
        Directory.CreateDirectory(actualDirectory);
        var actualPath = Path.Combine(actualDirectory, "room.json");
        var original = Encoding.UTF8.GetBytes("fictional junction original");
        File.WriteAllBytes(actualPath, original);
        var junctionPath = Path.Combine(directory, "junction");
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junctionPath);
        startInfo.ArgumentList.Add(actualDirectory);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Cannot create the test junction process.");
        Assert.IsTrue(process.WaitForExit(10000), "The exclusive test junction creation did not complete.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.AreEqual(0, process.ExitCode, $"Junction creation failed: {output} {error}");
        Assert.IsTrue((File.GetAttributes(junctionPath) & FileAttributes.ReparsePoint) != 0);
        var alias = Path.Combine(junctionPath, "room.json");
        var writer = new DurableAtomicTextWriter();

        Assert.ThrowsExactly<IOException>(() => DurableAtomicTextWriter.Capture(alias));
        Assert.ThrowsExactly<IOException>(() => writer.Write(alias, "must not overwrite"));
        Assert.ThrowsExactly<IOException>(() => writer.TryReplace(alias, ExactFileSnapshot.Missing, "must not overwrite"));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(actualPath));
    }

    [TestMethod]
    public async Task ConcurrentWriterInstancesAllowExactlyOneConditionalReplacement()
    {
        File.WriteAllText(path, "original");
        var expected = DurableAtomicTextWriter.Capture(path);
        using var start = new ManualResetEventSlim(false);
        var alternateCanonicalPath = Path.Combine(directory, ".", "fictional-room.json");
        var replacements = Enumerable.Range(0, 12).Select(index => Task.Run(() =>
        {
            start.Wait();
            return new DurableAtomicTextWriter().TryReplace(
                index % 2 == 0 ? path : alternateCanonicalPath, expected, $"fictional winner {index}");
        })).ToArray();
        start.Set();

        var results = await Task.WhenAll(replacements);

        Assert.AreEqual(1, results.Count(replaced => replaced));
        StringAssert.StartsWith(File.ReadAllText(path), "fictional winner ");
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
    }

    [TestMethod]
    [DataRow("fictional-room.json.")]
    [DataRow("fictional-room.json ")]
    [DataRow("parent.\\room.json")]
    [DataRow("parent \\room.json")]
    [DataRow("fictional-room.json:stream")]
    [DataRow("NUL")]
    public void WindowsAmbiguousPathsAreRejectedBeforeAnyWrite(string relative)
    {
        var alias = Path.Combine(directory, relative);
        var writer = new DurableAtomicTextWriter();

        Assert.ThrowsExactly<ArgumentException>(() => DurableAtomicTextWriter.Capture(alias));
        Assert.ThrowsExactly<ArgumentException>(() => writer.Write(alias, "value"));
        Assert.ThrowsExactly<ArgumentException>(() => writer.TryReplace(alias, ExactFileSnapshot.Missing, "value"));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(directory).Length);
    }
}
