using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;
using Dango.Physics;

namespace Dango.JointReview;

/// <summary>Isolated real-file/native gates. These never claim hardware or human acceptance.</summary>
internal static class FurnitureLayoutVerification
{
    internal static void Run(string module, string evidenceRoot)
    {
        evidenceRoot = Path.GetFullPath(evidenceRoot);
        if (Directory.Exists(evidenceRoot) || File.Exists(evidenceRoot)) throw new IOException("验证必须使用新的独占目录。");
        Directory.CreateDirectory(evidenceRoot);
        File.WriteAllText(Path.Combine(evidenceRoot, "owner.json"), "{\"purpose\":\"live-furniture-verification\",\"schema\":1}");
        var completed = new List<string>();
        var watch = Stopwatch.StartNew();
        var commitTimes = new List<double>();
        void Check(string name, Action test)
        {
            try { test(); completed.Add(name); }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(evidenceRoot, "verification-failure.json"), JsonSerializer.Serialize(new
                { Case = name, Error = error.Message, Type = error.GetType().Name, PreviouslyCompleted = completed }, new JsonSerializerOptions { WriteIndented = true }));
                throw;
            }
        }
        var windowChecks = FurnitureLayoutWindowVerification.Run();

        Check("50 real scene lifecycles and 150 persisted native operations", () =>
        {
            var data = Path.Combine(evidenceRoot, "cycles");
            for (var cycle = 0; cycle < 50; cycle++)
            {
                using var session = new FurnitureLayoutSceneSession(module, data);
                Require(session.Layout.Platform is null, "Empty saved layout must survive each restart.");
                Require(session.Layout.LayoutVersion == cycle * 3, "Saved version differs after process-equivalent reload.");
                var entity = session.Entity;
                var generation = session.WorldGeneration;
                DangoFurnitureFrame? before = null;
                session.BeforeNativeApply = () => before = session.VerificationWorld.ReadFurnitureFrame();
                session.AfterNativeApply = () => AssertNoBodyReset(before!, session.VerificationWorld.ReadFurnitureFrame());
                session.PreviewPlatform(0, 1.375);
                var initial = session.VerificationWorld.ReadFurnitureFrame();
                Require(session.CanConfirm, "Initial platform preview must be valid.");
                var unchanged = session.VerificationWorld.ReadFurnitureFrame();
                Require(JsonSerializer.Serialize(initial) == JsonSerializer.Serialize(unchanged), "Preview or Probe mutated the world.");
                Pump(session, session.ConfirmAsync());
                Require(session.Layout.Platform is not null && session.Layout.LayoutVersion == cycle * 3 + 1, session.Status);
                var saved = File.ReadAllBytes(FurnitureLayoutStore.GetLayoutPath(data));
                var original = session.VerificationWorld.ReadFurnitureFrame();
                session.PreviewPlatform(.5, 1.5);
                Require(JsonSerializer.Serialize(original) == JsonSerializer.Serialize(session.VerificationWorld.ReadFurnitureFrame()), "Move preview changed collision.");
                session.CancelPreview();
                Require(saved.AsSpan().SequenceEqual(File.ReadAllBytes(FurnitureLayoutStore.GetLayoutPath(data))), "Cancel wrote the file.");
                session.PreviewPlatform(-7, 1);
                Require(!session.CanConfirm && session.Layout.Platform is not null, "Invalid geometry was confirmable.");
                session.CancelPreview();
                var move = new FurnitureEditOperation(Guid.NewGuid(), session.Layout.LayoutVersion, FurnitureEditKind.Move,
                    session.Layout.Platform!.InstanceId, session.Layout.Platform with { Anchor = new(.5, 1.5) });
                Pump(session, session.SubmitAsync(move));
                var receipt = session.VerificationWorld.ReadFurnitureFrame();
                var savedMove = File.ReadAllBytes(FurnitureLayoutStore.GetLayoutPath(data));
                session.SubmitAsync(move).GetAwaiter().GetResult();
                Require(JsonSerializer.Serialize(receipt) == JsonSerializer.Serialize(session.VerificationWorld.ReadFurnitureFrame()) &&
                    savedMove.AsSpan().SequenceEqual(File.ReadAllBytes(FurnitureLayoutStore.GetLayoutPath(data))), "Duplicate operation reapplied or rewrote.");
                ExpectFailure(() => session.SubmitAsync(move with { Candidate = move.Candidate! with { Anchor = new(1, 2) } }));
                Pump(session, session.DeleteAsync());
                Require(session.Layout.Platform is null && session.Layout.LayoutVersion == cycle * 3 + 3, "Delete did not persist empty layout.");
                Require(session.Entity == entity && session.WorldGeneration == generation, "Furniture recreated the player or world.");
                Require(FurnitureLayoutSerializer.Deserialize(File.ReadAllBytes(FurnitureLayoutStore.GetLayoutPath(data))) == session.Layout, "File differs from applied layout.");
                commitTimes.AddRange(session.CommitBoundaryMilliseconds);
            }
            Require(FurnitureLayoutSceneSession.OwnedWorldCount == 0, "World ownership leaked across 50 closes.");
        });

        Check("Staging continues physics; latest body shape rejects an old valid preview", () =>
        {
            using var ready = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var writer = new FaultWriter { AfterStage = () => { ready.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Staging gate timeout."); } };
            using var session = Create("latest-shape", writer);
            session.PreviewPlatform(0, 1.375);
            Require(session.CanConfirm, "Initial preview must be valid.");
            var task = session.ConfirmAsync();
            Exception? fixtureFailure = null;
            try
            {
                Require(ready.Wait(TimeSpan.FromSeconds(5)), "Stage never started.");
                var firstStep = session.PhysicsStep;
                var body = session.LastFrame!.Bodies.Single();
                var target = new System.Windows.Point(body.CoreX, body.CoreY);
                session.Tick(0, false, false, true, target.X, target.Y);
                for (var step = 0; step < 600; step++)
                {
                    target = FurnitureLayoutProjection.LimitGloveTarget(target, new(0, 1.8), FurnitureLayoutSceneSession.FixedStep.TotalSeconds);
                    session.Tick(0, false, false, true, target.X, target.Y);
                }
                Require(session.PhysicsStep > firstStep + 500, "Staging stopped the physics owner: " + session.Status);
                var current = session.VerificationWorld.ReadFurnitureFrame();
                var probe = session.VerificationWorld.ProbeStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0,
                    new(0, 1.2, 3.4, .35), current.SceneRevision);
                Require(!probe.Allowed && probe.Rejection == DangoStaticPlatformChangeRejection.Penetration,
                    "Body did not enter the staged target fixture: " + JsonSerializer.Serialize(new { probe, current.Physics, session.Status }));
            }
            catch (Exception error) { fixtureFailure = error; }
            finally
            {
                release.Set();
                try { Pump(session, task); }
                catch (Exception error) { fixtureFailure ??= error; }
            }
            if (fixtureFailure is not null) ExceptionDispatchInfo.Capture(fixtureFailure).Throw();
            Require(session.Layout == FurnitureLayout.Empty && session.PersistedLayout == FurnitureLayout.Empty && session.HasPreview,
                "Rejection changed real layout or erased the preview.");
            Require(!File.Exists(FurnitureLayoutStore.GetLayoutPath(Path.Combine(evidenceRoot, "latest-shape"))), "Rejected latest shape was saved.");
            Require(!session.CanConfirm, "The intersecting body unexpectedly became confirmable.");
            var rejectedStatus = session.Status;
            var highest = session.VerificationWorld.ReadFurnitureFrame().BodyDiagnostics.Single().Nodes.Take(16).MaxBy(node => node.Y)!;
            var away = new System.Windows.Point(highest.X, highest.Y);
            session.Tick(0, false, false, true, away.X, away.Y);
            for (var step = 0; step < 180; step++)
            {
                away = FurnitureLayoutProjection.LimitGloveTarget(away, new(4, 1.8), FurnitureLayoutSceneSession.FixedStep.TotalSeconds);
                session.Tick(0, false, false, true, away.X, away.Y);
            }
            Require(session.CanConfirm && session.Status != rejectedStatus,
                "A now-valid preview retained the stale collision rejection message.");
        });

        Check("Delete while supported preserves the live body and naturally releases it", () =>
        {
            using var session = Create("supported-delete");
            session.PreviewPlatform(0, 1.375); Pump(session, session.ConfirmAsync());
            var highest = session.VerificationWorld.ReadFurnitureFrame().BodyDiagnostics.Single().Nodes.Take(16).MaxBy(node => node.Y)!;
            var target = new System.Windows.Point(highest.X, highest.Y);
            session.Tick(0, false, false, true, target.X, target.Y);
            foreach (var waypoint in new[] { new System.Windows.Point(-5, 3.8), new System.Windows.Point(0, 3.8) })
                for (var step = 0; step < 240; step++)
                {
                    target = FurnitureLayoutProjection.LimitGloveTarget(target, waypoint, FurnitureLayoutSceneSession.FixedStep.TotalSeconds);
                    var body = session.Tick(0, false, false, true, target.X, target.Y);
                    Require(session.CanStep && body.MaximumSpeed <= 12 + 1e-5 && body.SignedArea >= .42,
                        "Supported-delete hand positioning exceeded the real window limits: " + session.Status);
                }
            for (var step = 0; step < 300; step++) session.Tick(0, false, false, false, 0, 0);
            var supported = session.VerificationWorld.ReadFurnitureFrame();
            Require(supported.BodyDiagnostics.Single().PlatformContact.IsSupported, "The fixture never rested on the real platform.");
            DangoFurnitureFrame? before = null, deleted = null;
            session.BeforeNativeApply = () => before = session.VerificationWorld.ReadFurnitureFrame();
            session.AfterNativeApply = () =>
            {
                deleted = session.VerificationWorld.ReadFurnitureFrame();
                AssertNoBodyReset(before!, deleted);
                Require(!deleted.BodyDiagnostics.Single().PlatformContact.HasPlatform && !deleted.BodyDiagnostics.Single().IsGrounded,
                    "Deletion retained platform support or injected floor support.");
            };
            Pump(session, session.DeleteAsync());
            Require(deleted is not null && session.Layout.Platform is null && session.State == FurnitureSceneState.Synced,
                "Supported deletion failed to apply and save.");
            for (var step = 0; step < 40; step++) session.Tick(0, false, false, false, 0, 0);
            Require(session.LastFrame!.Bodies.Single().CoreY < deleted!.Physics.Bodies.Single().CoreY - .1,
                "The body did not fall naturally after deletion.");
        });

        Check("File exception before replacement preserves old world and preview", () =>
        {
            using var session = Create("before-file", new() { BeforeCommit = (_, _) => throw new IOException("fixture before commit") });
            session.PreviewPlatform(0, 1.375); Pump(session, session.ConfirmAsync());
            Require(session.State == FurnitureSceneState.Synced && session.Layout.Platform is null && session.HasPreview, session.Status);
            var saveFailure = session.Status;
            Require(session.CanConfirm && session.Status == saveFailure, "A valid preview erased a real file failure message.");
        });
        Check("File exception after replacement still applies confirmed candidate", () =>
        {
            using var session = Create("after-file", new() { AfterCommit = (_, _) => throw new IOException("fixture after commit") });
            session.PreviewPlatform(0, 1.375); Pump(session, session.ConfirmAsync());
            Require(session.State == FurnitureSceneState.Synced && session.Layout == session.PersistedLayout && session.Layout.Platform is not null, session.Status);
        });
        Check("Saved-not-applied recovery applies once without rewriting or version increment", () =>
        {
            var data = Path.Combine(evidenceRoot, "before-native");
            using var session = new FurnitureLayoutSceneSession(module, data);
            session.BeforeNativeApply = () => throw new IOException("fixture before native");
            session.PreviewPlatform(0, 1.375); Pump(session, session.ConfirmAsync());
            Require(session.State == FurnitureSceneState.PersistedNotApplied && session.CanRecover && !session.CanEdit && session.CanStep, session.Status);
            var bytes = File.ReadAllBytes(FurnitureLayoutStore.GetLayoutPath(data));
            var savedVersion = session.PersistedLayout.LayoutVersion;
            session.BeforeNativeApply = null;
            Pump(session, session.RecoverAsync());
            Require(session.State == FurnitureSceneState.Synced && session.Layout == session.PersistedLayout && session.Layout.LayoutVersion == savedVersion, session.Status);
            Require(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(FurnitureLayoutStore.GetLayoutPath(data))), "Recovery rewrote the saved file.");
        });
        Check("Native exception after apply is identified from the real receipt", () =>
        {
            using var session = Create("after-native");
            session.AfterNativeApply = () => throw new IOException("fixture after native");
            session.PreviewPlatform(0, 1.375); Pump(session, session.ConfirmAsync());
            Require(session.State == FurnitureSceneState.Synced && session.Layout.Platform is not null && !session.CanRecover, session.Status);
        });
        Check("Unknown target bytes stay protected; candidate recovery never rewrites", () =>
        {
            var data = Path.Combine(evidenceRoot, "unknown-file");
            var writer = new FaultWriter { BeforeCommit = (_, target) => { File.WriteAllText(target, "third-party fixture"); throw new IOException("fixture third value"); } };
            using var session = new FurnitureLayoutSceneSession(module, data, new(writer));
            session.PreviewPlatform(0, 1.375); Pump(session, session.ConfirmAsync());
            Require(session.State == FurnitureSceneState.CommitUnknown && session.CanStep && !session.CanEdit, session.Status);
            Pump(session, session.RecoverAsync());
            Require(session.State == FurnitureSceneState.CommitUnknown, "Unreadable/invalid target was misclassified as missing.");
            var path = FurnitureLayoutStore.GetLayoutPath(data);
            File.WriteAllBytes(path, writer.StagedBytes!); // Explicit fixture resolution inside owned evidence only.
            Pump(session, session.RecoverAsync());
            Require(session.State == FurnitureSceneState.Synced && session.Layout.LayoutVersion == 1, session.Status);
            Require(writer.StagedBytes!.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "Recovery changed candidate bytes.");
        });
        Check("Unexpected writer exception after replacement cannot claim not committed", () =>
        {
            using var session = Create("unexpected-writer", new() { AfterCommit = (_, _) => throw new Exception("fixture unexpected commit return") });
            session.PreviewPlatform(0, 1.375); Pump(session, session.ConfirmAsync());
            Require(session.State == FurnitureSceneState.PersistedNotApplied && session.PersistedLayout.LayoutVersion == 1 && session.CanRecover, session.Status);
            Pump(session, session.RecoverAsync());
            Require(session.State == FurnitureSceneState.Synced && session.Layout.Platform is not null, session.Status);
        });
        Check("Runtime outcome mismatch stops steps and does not overwrite the saved candidate", () =>
        {
            using var session = Create("unknown-native");
            session.AfterNativeApply = () => session.VerificationWorld.ClearStaticPlatform();
            session.PreviewPlatform(0, 1.375); Pump(session, session.ConfirmAsync());
            Require(session.State == FurnitureSceneState.RuntimeUnknown && !session.CanStep && !session.CanEdit && !session.CanRecover, session.Status);
            var step = session.PhysicsStep; session.Tick(1, false, true, false, 0, 0);
            Require(session.PhysicsStep == step, "Unknown runtime continued stepping.");
            Require(session.PersistedLayout.LayoutVersion == 1 && session.Layout == FurnitureLayout.Empty, "Unknown runtime pretended to apply or roll back the file.");
        });
        Check("Bad existing file can play empty but remains byte-for-byte protected", () =>
        {
            var data = Path.Combine(evidenceRoot, "bad-existing"); Directory.CreateDirectory(data);
            var path = FurnitureLayoutStore.GetLayoutPath(data); File.WriteAllText(path, "invalid fixture");
            var bytes = File.ReadAllBytes(path);
            using var session = new FurnitureLayoutSceneSession(module, data);
            session.Tick(1, false, false, false, 0, 0);
            Require(session.State == FurnitureSceneState.Protected && !session.CanEdit && session.CanStep, "Bad file silently became writable empty layout.");
            session.PreviewPlatform(0, 1.375); session.ConfirmAsync().GetAwaiter().GetResult();
            Require(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "Protected file was overwritten.");
        });
        Check("Second live scene cannot overwrite a newly saved version", () =>
        {
            var data = Path.Combine(evidenceRoot, "two-scenes");
            using var first = new FurnitureLayoutSceneSession(module, data);
            using var second = new FurnitureLayoutSceneSession(module, data);
            first.PreviewPlatform(0, 1.375); second.PreviewPlatform(.5, 1.5);
            Pump(first, first.ConfirmAsync());
            var bytes = File.ReadAllBytes(FurnitureLayoutStore.GetLayoutPath(data));
            Pump(second, second.ConfirmAsync());
            Require(second.Layout == FurnitureLayout.Empty && second.VerificationWorld.ReadFurnitureFrame().Platform is null &&
                bytes.AsSpan().SequenceEqual(File.ReadAllBytes(FurnitureLayoutStore.GetLayoutPath(data))), "Stale scene overwrote or moved its world.");
        });
        Check("A new operating-system process reloads and applies the saved platform", () =>
        {
            var data = Path.Combine(evidenceRoot, "process-reload");
            var executable = Environment.ProcessPath ?? throw new IOException("Missing acceptance executable path.");
            ProcessGateResult RunChild(string resultName)
            {
                var resultPath = Path.Combine(evidenceRoot, resultName);
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "--furniture-layout-process-gate", module, data, resultPath }) start.ArgumentList.Add(arg);
                using var child = Process.Start(start) ?? throw new IOException("Cannot start the isolated reload fixture.");
                if (!child.WaitForExit(20_000))
                { child.Kill(); child.WaitForExit(); throw new IOException("Isolated reload fixture timed out."); }
                Require(child.ExitCode == 0 && File.Exists(resultPath), "Isolated reload fixture failed.");
                return JsonSerializer.Deserialize<ProcessGateResult>(File.ReadAllBytes(resultPath))!;
            }
            var first = RunChild("process-first.json");
            var second = RunChild("process-restarted.json");
            Require(first.ProcessId != second.ProcessId && first.VersionBefore == 0 && first.VersionAfter == 1 &&
                second.VersionBefore == 1 && second.VersionAfter == 2 && first.AnchorAfter == second.AnchorBefore &&
                second.LoadedNativePlatform && first.OwnedWorldCount == 0 && second.OwnedWorldCount == 0,
                "The restarted process did not reload the exact saved layout into its new native world.");
        });
        Require(FurnitureLayoutSceneSession.OwnedWorldCount == 0, "World owners leaked.");
        File.WriteAllText(Path.Combine(evidenceRoot, "verification-summary.json"), JsonSerializer.Serialize(new
        {
            Passed = true, Cases = completed, ElapsedSeconds = watch.Elapsed.TotalSeconds,
            CandidateSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))),
            RealSceneLifecycles = 50, RealAppliedAndSavedOperations = 150, OwnedWorldCount = FurnitureLayoutSceneSession.OwnedWorldCount,
            PureWindowChecks = windowChecks,
            RealReloadProcesses = 2,
            CommitBoundaryMaximumMilliseconds = commitTimes.Max(),
            CommitBoundaryP95Milliseconds = commitTimes.Order().ElementAt((int)Math.Floor((commitTimes.Count - 1) * .95)),
            EvidenceKind = "Real native and files under software-driven isolated fixtures; no hardware/DPI/multiplayer/human claim",
        }, new JsonSerializerOptions { WriteIndented = true }));

        FurnitureLayoutSceneSession Create(string name, FaultWriter? writer = null) =>
            new(module, Path.Combine(evidenceRoot, name), writer is null ? null : new FurnitureLayoutStore(writer));
    }

    internal static void RunProcessGate(string module, string data, string resultPath)
    {
        if (File.Exists(resultPath)) throw new IOException("Reload evidence must be a fresh file.");
        long before, after;
        FurnitureAnchor? beforeAnchor, afterAnchor;
        bool loadedNative;
        using (var session = new FurnitureLayoutSceneSession(module, data))
        {
            Require(session.State == FurnitureSceneState.Synced, session.Status);
            before = session.Layout.LayoutVersion; beforeAnchor = session.Layout.Platform?.Anchor;
            var actual = session.VerificationWorld.ReadFurnitureFrame();
            loadedNative = beforeAnchor is { } anchor && actual.Platform is { } platform && actual.PlatformInstanceId != 0 &&
                Math.Abs(platform.CenterX - anchor.X) <= 1e-6 &&
                Math.Abs(platform.CenterY - (anchor.Y - LocalFurnitureSceneGeometry.PlatformHeight / 2)) <= 1e-6;
            session.PreviewPlatform(before == 0 ? 0 : .5, before == 0 ? 1.375 : 1.5);
            Pump(session, session.ConfirmAsync());
            Require(session.State == FurnitureSceneState.Synced && session.Layout.LayoutVersion == before + 1 &&
                session.Layout == session.PersistedLayout, session.Status);
            after = session.Layout.LayoutVersion; afterAnchor = session.Layout.Platform!.Anchor;
        }
        using var output = new FileStream(resultPath, FileMode.CreateNew);
        JsonSerializer.Serialize(output, new ProcessGateResult(Environment.ProcessId, before, after, beforeAnchor, afterAnchor,
            loadedNative, FurnitureLayoutSceneSession.OwnedWorldCount));
    }

    private sealed record ProcessGateResult(int ProcessId, long VersionBefore, long VersionAfter,
        FurnitureAnchor? AnchorBefore, FurnitureAnchor? AnchorAfter, bool LoadedNativePlatform, int OwnedWorldCount);

    private static void Pump(FurnitureLayoutSceneSession session, Task task)
    {
        var timeout = Stopwatch.StartNew();
        while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(12))
        { session.Tick(0, false, false, false, 0, 0); Thread.Sleep(1); }
        Require(task.IsCompleted, "Pending furniture request never finished."); task.GetAwaiter().GetResult();
    }
    private static void AssertNoBodyReset(DangoFurnitureFrame before, DangoFurnitureFrame after)
    {
        Require(before.WorldGeneration == after.WorldGeneration && before.PhysicsStep == after.PhysicsStep &&
            after.SceneRevision == before.SceneRevision + 1, "Apply stepped or recreated the world.");
        var a = before.BodyDiagnostics.Single(); var b = after.BodyDiagnostics.Single();
        Require(a.EntityId == b.EntityId && a.Nodes.SequenceEqual(b.Nodes) && a.SecondsSinceJump == b.SecondsSinceJump && a.DragActive == b.DragActive,
            "Apply changed raw body IDs, positions, velocities, control or jump cooldown.");
        var left = before.Physics.Bodies.Single(); var right = after.Physics.Bodies.Single();
        Require(left.SelectedDragNode == right.SelectedDragNode && left.Contour.SequenceEqual(right.Contour),
            "Apply changed the selected grab node or published contour.");
    }
    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidDataException(reason); }
    private static void ExpectFailure(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidDataException("Invalid retry was accepted."); }

    private sealed class FaultWriter : IFurnitureFileWriter
    {
        private readonly FurnitureAtomicFileWriter real = new();
        internal Action? AfterStage;
        internal Action<string, string>? BeforeCommit, AfterCommit;
        internal byte[]? StagedBytes;
        public void WriteStaged(string path, byte[] bytes) { StagedBytes = bytes.ToArray(); real.WriteStaged(path, bytes); AfterStage?.Invoke(); }
        public void Commit(string staged, string target, bool existed)
        { BeforeCommit?.Invoke(staged, target); real.Commit(staged, target, existed); AfterCommit?.Invoke(staged, target); }
    }
}
