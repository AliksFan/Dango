using System.Diagnostics;
using System.Text;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;

namespace Dango.Desktop.Tests.Furniture;

[TestClass]
public sealed class FurnitureLayoutStoreTests
{
    private static readonly Guid Instance = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Operation = Guid.Parse("20000000-0000-4000-8000-000000000002");

    [TestMethod]
    public void MissingLayoutDoesNotCreateFileAndTwoRealCommitsRoundTrip()
    {
        var path = NewPath();
        var store = new FurnitureLayoutStore();
        var missing = store.Load(path);
        Assert.AreEqual(FurnitureLayoutLoadState.Missing, missing.State);
        Assert.AreEqual(FurnitureLayout.Empty, missing.Layout);
        Assert.IsFalse(File.Exists(path));
        using var lease = store.AcquireLease(path);
        using (var first = lease.Stage(Candidate(), 0))
        {
            Assert.IsFalse(File.Exists(path)); // Staging never publishes the layout.
            Assert.AreEqual(FurnitureCommitState.Committed, first.Commit().State);
            Assert.AreEqual(FurnitureCommitState.Committed, first.Commit().State); // No second replacement.
        }
        Assert.AreEqual(Candidate(), new FurnitureLayoutStore().Load(path).Layout);
        var empty = new FurnitureLayout(2, Guid.Parse("30000000-0000-4000-8000-000000000003"), null);
        using (var second = lease.Stage(empty, 1)) Assert.AreEqual(FurnitureCommitState.Committed, second.Commit().State);
        Assert.AreEqual(empty, new FurnitureLayoutStore().Load(path).Layout);
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, ".furniture-*.tmp").Length);
    }

    [TestMethod]
    [DataRow("invalid-json")]
    [DataRow("{\"schemaVersion\":100}")]
    [DataRow("{}")]
    public void CorruptOrFutureLayoutIsPreservedAndCannotBeStaged(string content)
    {
        var path = NewPath();
        Write(path, Encoding.UTF8.GetBytes(content));
        var bytes = File.ReadAllBytes(path);
        var store = new FurnitureLayoutStore();
        using var lease = store.AcquireLease(path);
        Assert.AreEqual(FurnitureLayoutLoadState.Protected, lease.Baseline.State);
        Assert.IsNull(lease.Baseline.Layout);
        Assert.IsFalse(lease.Baseline.IsWritable);
        Assert.ThrowsExactly<InvalidDataException>(() => lease.Stage(Candidate(), 0));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void OverBudgetAndLockedFileStayProtectedInsteadOfBecomingEmpty()
    {
        var path = NewPath();
        Write(path, new byte[FurnitureLayoutSerializer.MaximumBytes + 1]);
        var store = new FurnitureLayoutStore();
        Assert.AreEqual(FurnitureLayoutLoadState.Protected, store.Load(path).State);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var load = store.Load(path);
        Assert.AreEqual(FurnitureLayoutLoadState.TransientReadFailure, load.State);
        Assert.IsNull(load.Layout);
        Assert.IsFalse(load.IsWritable);
    }

    [TestMethod]
    public void LeaseCompetesAndReReadsTheLatestFileOnAcquisition()
    {
        var path = NewPath();
        var store = new FurnitureLayoutStore();
        Assert.AreEqual(FurnitureLayoutLoadState.Missing, store.Load(path).State);
        Write(path, FurnitureLayoutSerializer.Serialize(Candidate()));
        using var owner = store.AcquireLease(path);
        Assert.AreEqual(Candidate(), owner.Baseline.Layout);
        Assert.ThrowsExactly<IOException>(() => store.AcquireLease(path));
        Assert.ThrowsExactly<InvalidOperationException>(() => owner.Stage(Candidate(), 0));
    }

    [TestMethod]
    public async Task AnotherProcessCannotOpenTheOwnedLeaseAndLockFileIsReusable()
    {
        var path = NewPath();
        var store = new FurnitureLayoutStore();
        using (var owner = store.AcquireLease(path))
        {
            const string script = "try { $held = [IO.File]::Open($env:DANGO_FURNITURE_TEST_LOCK, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None); $held.Dispose(); [Console]::WriteLine('ACQUIRED') } catch { [Console]::WriteLine('BLOCKED') }";
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            start.Environment["DANGO_FURNITURE_TEST_LOCK"] = path + ".lock";
            using var child = Process.Start(start)!;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await child.WaitForExitAsync(deadline.Token); }
            catch { if (!child.HasExited) child.Kill(entireProcessTree: true); throw; }
            Assert.AreEqual(0, child.ExitCode, await child.StandardError.ReadToEndAsync());
            Assert.AreEqual("BLOCKED", (await child.StandardOutput.ReadToEndAsync()).Trim());
        }
        Assert.IsTrue(File.Exists(path + ".lock"));
        using var next = store.AcquireLease(path);
        Assert.AreEqual(FurnitureLayoutLoadState.Missing, next.Baseline.State);
    }

    [TestMethod]
    public void StagingFailureOrCorruptedTemporaryDoesNotPublishAnything()
    {
        foreach (var corrupt in new[] { false, true })
        {
            var path = NewPath();
            Write(path, FurnitureLayoutSerializer.Serialize(Candidate()));
            var bytes = File.ReadAllBytes(path);
            var writer = new ControlledWriter { StageFailure = !corrupt, CorruptStage = corrupt };
            using var lease = new FurnitureLayoutStore(writer).AcquireLease(path);
            var updated = Candidate() with { LayoutVersion = 2, LastOperationId = Guid.NewGuid() };
            if (corrupt) Assert.ThrowsExactly<InvalidDataException>(() => lease.Stage(updated, 1));
            else Assert.ThrowsExactly<IOException>(() => lease.Stage(updated, 1));
            Assert.AreEqual(0, writer.CommitCalls);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
            Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, ".furniture-*.tmp").Length);
        }
    }

    [TestMethod]
    public void UnconfirmedStageCanBeDisposedAndConcurrentStageCannotStart()
    {
        var path = NewPath();
        using var lease = new FurnitureLayoutStore().AcquireLease(path);
        using (var stage = lease.Stage(Candidate(), 0))
            Assert.ThrowsExactly<InvalidOperationException>(() => lease.Stage(Candidate(), 0));
        Assert.IsFalse(File.Exists(path));
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, ".furniture-*.tmp").Length);
        using var retry = lease.Stage(Candidate(), 0);
        Assert.AreEqual(FurnitureCommitState.Committed, retry.Commit().State);
    }

    [TestMethod]
    public void ExternalByteChangeBeforeCommitCannotBeOverwrittenEvenWithSameVersion()
    {
        var path = NewPath();
        Write(path, FurnitureLayoutSerializer.Serialize(Candidate()));
        var writer = new ControlledWriter();
        using var lease = new FurnitureLayoutStore(writer).AcquireLease(path);
        using var stage = lease.Stage(Candidate() with { LayoutVersion = 2, LastOperationId = Guid.NewGuid() }, 1);
        var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(File.ReadAllBytes(path)) + " ");
        File.WriteAllBytes(path, changed);
        Assert.AreEqual(FurnitureCommitState.Unknown, stage.Commit().State);
        Assert.AreEqual(0, writer.CommitCalls);
        CollectionAssert.AreEqual(changed, File.ReadAllBytes(path));
        stage.Dispose();
        Assert.ThrowsExactly<InvalidOperationException>(() => lease.Stage(Candidate(), 0));
    }

    [TestMethod]
    public void CorruptionOfTemporaryAfterStagingCannotReplaceTheOldLayout()
    {
        var path = NewPath();
        Write(path, FurnitureLayoutSerializer.Serialize(Candidate()));
        var bytes = File.ReadAllBytes(path);
        var writer = new ControlledWriter();
        using var lease = new FurnitureLayoutStore(writer).AcquireLease(path);
        using var stage = lease.Stage(Candidate() with { LayoutVersion = 2, LastOperationId = Guid.NewGuid() }, 1);
        File.WriteAllText(writer.StagedPath!, "bad-temp");
        Assert.AreEqual(FurnitureCommitState.NotCommitted, stage.Commit().State);
        Assert.AreEqual(0, writer.CommitCalls);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ThrowBeforeCommitIsNotCommittedButThrowAfterCommitIsCommitted(bool after)
    {
        var path = NewPath();
        Write(path, FurnitureLayoutSerializer.Serialize(Candidate()));
        var bytes = File.ReadAllBytes(path);
        var updated = Candidate() with { LayoutVersion = 2, LastOperationId = Guid.NewGuid(), Platform = null };
        var writer = new ControlledWriter { CommitAction = (temporary, target, existed) =>
        {
            if (after) new FurnitureAtomicFileWriter().Commit(temporary, target, existed);
            throw new IOException("simulated commit exception");
        } };
        using var lease = new FurnitureLayoutStore(writer).AcquireLease(path);
        using var stage = lease.Stage(updated, 1);
        Assert.AreEqual(after ? FurnitureCommitState.Committed : FurnitureCommitState.NotCommitted, stage.Commit().State);
        if (after) Assert.AreEqual(updated, new FurnitureLayoutStore().Load(path).Layout);
        else CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    [DataRow("removed")]
    [DataRow("third-content")]
    [DataRow("unreadable")]
    public void CommitExceptionWithMissingThirdOrUnreadableTargetIsUnknown(string mode)
    {
        var path = NewPath();
        Write(path, FurnitureLayoutSerializer.Serialize(Candidate()));
        FileStream? targetLock = null;
        var writer = new ControlledWriter { CommitAction = (_, target, _) =>
        {
            if (mode == "removed") File.Delete(target); // Only this test's own synthetic target.
            else if (mode == "third-content") File.WriteAllText(target, "third-content");
            else targetLock = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            throw new IOException("simulated uncertain commit");
        } };
        try
        {
            using var lease = new FurnitureLayoutStore(writer).AcquireLease(path);
            using (var stage = lease.Stage(Candidate() with { LayoutVersion = 2, LastOperationId = Guid.NewGuid() }, 1))
                Assert.AreEqual(FurnitureCommitState.Unknown, stage.Commit().State);
            Assert.ThrowsExactly<InvalidOperationException>(() => lease.Stage(Candidate(), 0));
        }
        finally { targetLock?.Dispose(); }
    }

    [TestMethod]
    public void FailedFirstCommitWithNoFileIsNotCommittedAndCanRetry()
    {
        var path = NewPath();
        var writer = new ControlledWriter { CommitAction = (_, _, _) => throw new IOException("before initial move") };
        using var lease = new FurnitureLayoutStore(writer).AcquireLease(path);
        using (var stage = lease.Stage(Candidate(), 0)) Assert.AreEqual(FurnitureCommitState.NotCommitted, stage.Commit().State);
        Assert.IsFalse(File.Exists(path));
        writer.CommitAction = null;
        using var retry = lease.Stage(Candidate(), 0);
        Assert.AreEqual(FurnitureCommitState.Committed, retry.Commit().State);
    }

    [TestMethod]
    public void ResidualOtherOperationTemporaryIsNeverLoadedOrCleaned()
    {
        var path = NewPath();
        var other = Path.Combine(Path.GetDirectoryName(path)!, ".furniture-unrelated.tmp");
        Write(other, FurnitureLayoutSerializer.Serialize(Candidate()));
        using (var lease = new FurnitureLayoutStore().AcquireLease(path))
        using (var stage = lease.Stage(Candidate(), 0)) { }
        Assert.IsFalse(File.Exists(path));
        Assert.IsTrue(File.Exists(other));
        Assert.AreEqual(FurnitureLayoutLoadState.Missing, new FurnitureLayoutStore().Load(path).State);
    }

    private static FurnitureLayout Candidate() => new(1, Operation, new(Instance, LocalFurnitureSceneGeometry.DefaultAnchor));
    private static string NewPath() => Path.Combine(TestRepository.Root, "artifacts", "test-runs", "live-furniture-store",
        Guid.NewGuid().ToString("N"), "furniture-layout.json");
    private static void Write(string path, byte[] bytes)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }

    private sealed class ControlledWriter : IFurnitureFileWriter
    {
        public bool StageFailure { get; init; }
        public bool CorruptStage { get; init; }
        public Action<string, string, bool>? CommitAction { get; set; }
        public string? StagedPath { get; private set; }
        public int CommitCalls { get; private set; }

        public void WriteStaged(string stagedPath, byte[] bytes)
        {
            StagedPath = stagedPath;
            if (StageFailure) throw new IOException("simulated staging failure");
            new FurnitureAtomicFileWriter().WriteStaged(stagedPath, CorruptStage ? Encoding.UTF8.GetBytes("bad-stage") : bytes);
        }

        public void Commit(string stagedPath, string targetPath, bool targetExisted)
        {
            CommitCalls++;
            if (CommitAction is { } action) action(stagedPath, targetPath, targetExisted);
            else new FurnitureAtomicFileWriter().Commit(stagedPath, targetPath, targetExisted);
        }
    }
}
