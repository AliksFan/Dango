using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Furniture;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.JointReview;

/// <summary>Drives the actual product host and its existing frame owner; never steps a test world.</summary>
internal static class OrdinaryRoomFurnitureCollectionVerification
{
    private const string CandidateHash = "BA6E92D57CAFBD5CC806F709890AA1E8ED784F97CF42A09F4168EE08065E97A0";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed class Evidence
    {
        public int Schema { get; init; } = 1;
        public bool Passed { get; set; }
        public string Phase { get; set; } = "initializing";
        public string? Failure { get; set; }
        public string CandidateSha256 { get; init; } = CandidateHash;
        public int ProcessId { get; init; } = Environment.ProcessId;
        public int CompletedRoomRoundTrips { get; set; }
        public int CompletedEditRoundTrips { get; set; }
        public int SavedOperations { get; set; }
        public int FurnitureCount { get; set; }
        public int PlatformCount { get; set; }
        public int BallCount { get; set; }
        public bool LiveBallRenderVerified { get; set; }
        public bool ReentryRestoredBallAnchors { get; set; }
        public bool HiddenRoomDidNotStep { get; set; }
        public bool HidePreservedEveryNativeId { get; set; }
        public ulong WorldGeneration { get; set; }
        public int EntityId { get; set; }
        public long LayoutVersion { get; set; }
        public string? LayoutSha256 { get; set; }
        public string RuntimeState { get; set; } = "uninitialized";
        public bool WindowVisible { get; set; }
        public bool WindowMinimized { get; set; }
        public double RequiredActiveSeconds { get; init; }
        public double ActiveSeconds { get; set; }
        public double ExcludedSeconds { get; set; }
        public double WallSeconds { get; set; }
        public long CompositionCallbacks { get; set; }
        public double CompositionCallbackRate { get; set; }
        public ulong ObservedPhysicsSteps { get; set; }
        public HostFrameDiagnostics? Scheduler { get; set; }
        public List<double> CommitBoundaryMilliseconds { get; } = [];
        public double CommitBoundaryMaximumMilliseconds { get; set; }
        public double CommitBoundaryP95Milliseconds { get; set; }
        public List<ResourceSample> Resources { get; } = [];
        public int RestartProcesses { get; set; }
        public bool RestartLoadedIdenticalFile { get; set; }
        public bool ReleasedFrameOwner { get; set; }
        public bool ReleasedNativeWorld { get; set; }
        public bool ClosedOwnedWindows { get; set; }
        public string EvidenceKind { get; init; } = "Software-driven actual DangoApplicationHost / LocalRoomWindow, native physics, files and independent OS processes";
        public string CompositionMeaning { get; init; } = "Distinct WPF Rendering callbacks during eligible visibility; not compositor Present telemetry and not a 59 FPS acceptance";
        public string HardwareInputAndHumanAcceptance { get; init; } = "Not verified by this tool";
        public string DpiAccessibilityAndExisting59FpsGate { get; init; } = "Separate gates; not verified by this tool";
    }

    private sealed record ResourceSample(double WallSeconds, long PrivateBytes, long WorkingSetBytes,
        int Handles, int Threads, int OwnedVisibleWindows);
    private sealed record RestartResult(int ProcessId, string Mode, long LayoutVersion, Guid? OperationId,
        IReadOnlyList<FurnitureItemInstance> Items, string LayoutSha256, bool LoadedNativeCollection, bool ReleasedFrameOwner,
        bool ReleasedNativeWorld, bool ClosedOwnedWindows);

    internal static void Run(string module, string dataRoot, string outputRoot, double activeSeconds)
    {
        Require(double.IsFinite(activeSeconds) && activeSeconds >= 0 && activeSeconds <= 3600,
            "Active observation seconds must be finite and within 0..3600.");
        module = Path.GetFullPath(module); dataRoot = Path.GetFullPath(dataRoot); outputRoot = Path.GetFullPath(outputRoot);
        Require(Hash(module) == CandidateHash, "Ordinary-room verification requires the frozen Furniture ABI candidate.");
        Require(!Directory.Exists(outputRoot), "Use a fresh evidence directory; existing evidence is preserved.");
        PrepareOwnedData(dataRoot);
        var layoutPath = FurnitureLayoutStore.GetLayoutPath(dataRoot, FurnitureLayoutContract.OrdinaryRoom);
        Require(!File.Exists(layoutPath), "Full verification requires a fresh owned room layout.");
        Directory.CreateDirectory(outputRoot);
        var evidence = new Evidence { RequiredActiveSeconds = activeSeconds };
        var watch = Stopwatch.StartNew();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        ProductFixture? product = null;
        Exception? failure = null;
        try
        {
            evidence.Phase = "independent-process-restart";
            Progress();
            var restartData = Path.Combine(dataRoot, "restart-case");
            PrepareOwnedData(restartData);
            var first = Child("save", restartData, "restart-save.json");
            var second = Child("load", restartData, "restart-load.json");
            Require(first.ProcessId != second.ProcessId && first.LayoutVersion == 30 &&
                first.LayoutVersion == second.LayoutVersion && first.OperationId == second.OperationId &&
                first.Items.SequenceEqual(second.Items) && first.Items.Count == 30 && first.LayoutSha256 == second.LayoutSha256 &&
                first.LoadedNativeCollection && second.LoadedNativeCollection && first.ReleasedNativeWorld &&
                second.ReleasedNativeWorld && first.ReleasedFrameOwner && second.ReleasedFrameOwner &&
                first.ClosedOwnedWindows && second.ClosedOwnedWindows,
                "Independent restarted product process did not restore the identical file, thirty GUIDs and native collection.");
            evidence.RestartProcesses = 2; evidence.RestartLoadedIdenticalFile = true;

            evidence.Phase = "actual-product-round-trips";
            product = new ProductFixture(module, dataRoot);
            product.EnterFromPorchFurniture();
            var session = product.Furniture;
            evidence.WorldGeneration = session.WorldGeneration;
            evidence.EntityId = product.Runtime.EntityIds.Single().Value;
            var initialWorld = product.World;
            FillCollection(product, () => evidence.SavedOperations++);
            evidence.FurnitureCount = session.Layout.Items.Count;
            evidence.PlatformCount = session.Layout.Items.Count(item => item.Kind == FurnitureItemKind.StaticPlatform);
            evidence.BallCount = session.Layout.Items.Count(item => item.Kind == FurnitureItemKind.RoundBall);
            Require(evidence.FurnitureCount == 30 && evidence.PlatformCount == 24 && evidence.BallCount == 6,
                "Actual product did not create the complete thirty-item mixed collection.");
            CheckApplied();
            var atCapacity = JsonSerializer.Serialize(session.NativeFrame);
            session.PreviewNewFurniture(FurnitureItemKind.RoundBall, new(1440, 132));
            Require(!session.HasPreview && !session.CanConfirm && session.Layout.Items.Count == 30 &&
                atCapacity == JsonSerializer.Serialize(session.NativeFrame), "The actual coordinator admitted a thirty-first preview or changed the native world at capacity.");
            var movingPlatformId = session.Layout.Items.First(item => item.Kind == FurnitureItemKind.StaticPlatform).InstanceId;
            VerifyLiveBallAndResetEntry();
            evidence.Resources.Add(Sample(watch.Elapsed.TotalSeconds, product));

            for (var cycle = 0; cycle < 50; cycle++)
            {
                var beforeStep = session.PhysicsStep;
                var beforeCenter = product.Runtime.CurrentSelectedCenter;
                Require(product.Window.EndFurnitureEditing() && product.Window.BeginFurnitureEditing(),
                    "Actual ordinary-room editor toggle failed.");
                Require(beforeStep == session.PhysicsStep && beforeCenter == product.Runtime.CurrentSelectedCenter,
                    "Editor controls stepped or moved the existing pet.");
                CheckIdentity(); evidence.CompletedEditRoundTrips++;
                var versionBefore = session.Layout.LayoutVersion;
                var requestedAnchor = new FurnitureAnchor(cycle % 2 == 0 ? 220 : 200, 225);
                session.PreviewMoveFurniture(movingPlatformId, requestedAnchor);
                Require(session.CanConfirm, session.Status);
                Complete(session.ConfirmAsync());
                Require(session.Layout.LayoutVersion == versionBefore + 1 && session.Layout.Find(movingPlatformId)?.Anchor == requestedAnchor,
                    "Product move was ignored, duplicated, rejected or saved at a different anchor.");
                CheckApplied(); evidence.SavedOperations++;
                var bytes = File.ReadAllBytes(layoutPath);
                var saved = session.Layout;
                Require(product.Window.EndFurnitureEditing(), "Could not leave the editor after a completed commit.");
                product.Host.ReturnToPorch(); product.UntilHandoff();
                Require(product.Host.LocalPresentation == LocalRoomPresentation.Porch, product.Host.LocalRoomFailure ?? "Porch handoff failed.");
                Require(!session.IsAttached && session.NativeFrame?.Items.Count == 0,
                    "Ordinary-room furniture collection leaked into the porch.");
                Require(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(layoutPath)), "Runtime detach changed the saved layout.");
                CheckIdentity();
                product.Host.EnterLocalRoomFurniture(); product.UntilHandoff();
                Require(product.Host.LocalPresentation == LocalRoomPresentation.Window && product.Window.IsFurnitureEditing,
                    product.Host.LocalRoomFailure ?? "Furniture entry did not open the actual room editor.");
                CheckApplied();
                Require(saved == session.Layout && bytes.AsSpan().SequenceEqual(File.ReadAllBytes(layoutPath)),
                    "Reentry rewrote or changed the persisted layout.");
                evidence.CompletedRoomRoundTrips++;
                if (cycle % 10 == 9) { evidence.Resources.Add(Sample(watch.Elapsed.TotalSeconds, product)); Progress(); }
            }

            // Exercise the product hide/restore lifecycle with all thirty real colliders.
            var hiddenBytes = File.ReadAllBytes(layoutPath);
            var hiddenInstances = session.NativeFrame!.Items.Select(item => item.InstanceId).ToArray();
            product.Host.HideLocalRoom();
            Require(product.Runtime.FrameDriver is null && !product.Runtime.IsRunning, "Hidden room retained a running frame owner.");
            var hiddenStep = session.PhysicsStep;
            Pump(250);
            Require(session.PhysicsStep == hiddenStep && hiddenInstances.SequenceEqual(session.NativeFrame!.Items.Select(item => item.InstanceId)),
                "Hidden room advanced physics or changed an owned native instance.");
            evidence.HiddenRoomDidNotStep = true;
            product.Host.RestoreLocalRoom(); product.UntilHandoff();
            CheckApplied();
            Require(hiddenInstances.SequenceEqual(session.NativeFrame!.Items.Select(item => item.InstanceId)) && hiddenBytes.AsSpan().SequenceEqual(File.ReadAllBytes(layoutPath)),
                "Hidden-room restore duplicated an instance or rewrote its file.");
            evidence.HidePreservedEveryNativeId = true;
            Require(product.Window.BeginFurnitureEditing(), "Editor unavailable after hidden-room restore.");
            ExportActualRoomImages(product, outputRoot);
            evidence.CommitBoundaryMilliseconds.AddRange(session.CommitBoundaryMilliseconds);
            evidence.CommitBoundaryMaximumMilliseconds = evidence.CommitBoundaryMilliseconds.DefaultIfEmpty().Max();
            var orderedTimes = evidence.CommitBoundaryMilliseconds.Order().ToArray();
            evidence.CommitBoundaryP95Milliseconds = orderedTimes.Length == 0 ? 0 : orderedTimes[(int)Math.Floor((orderedTimes.Length - 1) * .95)];
            Require(!File.Exists(FurnitureLayoutStore.GetLayoutPath(dataRoot)), "Ordinary room wrote the independent trial layout.");

            evidence.Phase = activeSeconds > 0 ? "visible-room-observation" : "short-verification-complete";
            Progress();
            if (activeSeconds > 0) Observe();
            evidence.LayoutVersion = session.Layout.LayoutVersion;
            evidence.LayoutSha256 = Hash(layoutPath);
            evidence.Passed = true;

            void CheckIdentity()
            {
                Require(ReferenceEquals(initialWorld, product.World) && session.WorldGeneration == evidence.WorldGeneration &&
                    product.Runtime.EntityIds.Count == 1 && product.Runtime.EntityIds.Single().Value == evidence.EntityId,
                    "Product handoff recreated the world or copied/replaced the pet.");
                Require(product.Runtime.LastError is null && product.Runtime.AttachmentError is null,
                    product.Runtime.LastError ?? product.Runtime.AttachmentError ?? "Runtime fault.");
            }
            void CheckApplied()
            {
                CheckIdentity();
                Require(session.IsAttached && session.CanEdit && session.State == OrdinaryFurnitureSceneState.Synced &&
                    session.NativeFrame is { Items.Count: 30 } frame && frame.Physics.Succeeded &&
                    session.Layout == session.PersistedLayout && session.Layout.Items.Count == 30 && session.RenderItems.Count == 30 &&
                    frame.Items.Select(item => item.InstanceId).Distinct().Count() == 30 &&
                    session.RenderItems.Select(item => item.InstanceId).SequenceEqual(session.Layout.Items.Select(item => item.InstanceId)),
                    "Saved thirty-item room collection is not backed by the complete healthy native/render frames: " + session.Status);
                var loaded = new FurnitureLayoutStore(contract: FurnitureLayoutContract.OrdinaryRoom).Load(layoutPath);
                Require(loaded.IsWritable && loaded.Layout == session.Layout, "Product file and live layout disagree.");
            }
            void VerifyLiveBallAndResetEntry()
            {
                var savedBytes = File.ReadAllBytes(layoutPath);
                var savedLayout = session.Layout;
                Require(product.Driver.Key(Key.D, true, false), "Actual room driver did not accept the software-driven move.");
                try
                {
                    Until(() => session.RenderItems.Any(item => item.Kind == FurnitureItemKind.RoundBall &&
                        Math.Abs(item.Anchor.X - savedLayout.Find(item.InstanceId)!.Anchor.X) > 2), 5000,
                        "Actual player movement did not disturb a real ball; live render evidence is missing.");
                }
                finally { product.Driver.Key(Key.D, false, false); product.Driver.ClearInput(); }
                Require(session.Layout == savedLayout && savedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(layoutPath)),
                    "A moving ball rewrote its confirmed placement or layout file.");
                Require(product.Window.Scene.FurnitureItems.SequenceEqual(session.RenderItems),
                    "The actual room scene did not receive every live native render item.");
                foreach (var item in session.RenderItems.Where(item => item.Kind == FurnitureItemKind.RoundBall))
                {
                    var actual = session.NativeFrame!.Items.Single(native => native.BallState is { } ball &&
                        Math.Abs(item.Anchor.X - product.Scene.OrdinaryBallAnchor(ball).X) < 1e-4 &&
                        Math.Abs(item.Anchor.Y - product.Scene.OrdinaryBallAnchor(ball).Y) < 1e-4);
                    Require(actual.BallState!.AngleRadians == item.AngleRadians, "Rendered ball angle is not the actual native angle.");
                }
                evidence.LiveBallRenderVerified = true;
                Require(product.Window.EndFurnitureEditing(), "Could not exit furniture editing after pushing the ball.");
                product.Host.ReturnToPorch(); product.UntilHandoff();
                product.Host.EnterLocalRoomFurniture(); product.UntilHandoff();
                CheckApplied();
                Require(session.Layout == savedLayout && savedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(layoutPath)),
                    "Ball reentry changed saved GUIDs, anchors or bytes.");
                foreach (var item in session.RenderItems.Where(item => item.Kind == FurnitureItemKind.RoundBall))
                {
                    var saved = savedLayout.Find(item.InstanceId)!;
                    Require(Math.Abs(item.Anchor.X - saved.Anchor.X) < .25 && Math.Abs(item.Anchor.Y - saved.Anchor.Y) < .25,
                        "Reentry did not restore a ball to its confirmed placement.");
                }
                evidence.ReentryRestoredBallAnchors = true;
            }
            void Observe()
            {
                var observation = Stopwatch.StartNew();
                var previous = observation.Elapsed;
                var nextProgress = TimeSpan.FromSeconds(30);
                var startStep = session.PhysicsStep;
                var previousStep = startStep;
                long eligibilityEpoch = 0;
                EventHandler lifecycle = (_, _) => eligibilityEpoch++;
                DependencyPropertyChangedEventHandler visibility = (_, _) => eligibilityEpoch++;
                product.Window.Activated += lifecycle;
                product.Window.Deactivated += lifecycle;
                product.Window.StateChanged += lifecycle;
                product.Window.IsVisibleChanged += visibility;
                TimeSpan? lastRendering = null;
                long compositions = 0;
                var wasEligible = Eligible();
                EventHandler rendered = (_, args) =>
                {
                    if (args is not RenderingEventArgs rendering || !Eligible() || lastRendering == rendering.RenderingTime) return;
                    lastRendering = rendering.RenderingTime; compositions++;
                };
                CompositionTarget.Rendering += rendered;
                try
                {
                    // This timer only observes the existing product owner. It never calls Step.
                    while (evidence.ActiveSeconds < activeSeconds)
                    {
                        var epochBeforePump = eligibilityEpoch;
                        Pump(100);
                        var now = observation.Elapsed;
                        var eligible = Eligible();
                        var delta = (now - previous).TotalSeconds;
                        var currentStep = session.PhysicsStep;
                        if (eligible && wasEligible && eligibilityEpoch == epochBeforePump && currentStep > previousStep)
                            evidence.ActiveSeconds += delta;
                        else evidence.ExcludedSeconds += delta;
                        previous = now; wasEligible = eligible; previousStep = currentStep;
                        CheckIdentity();
                        Require(session.CanStep && session.State == OrdinaryFurnitureSceneState.Synced &&
                            session.NativeFrame?.Physics.Succeeded == true,
                            "Actual room became unhealthy during observation: " + session.Status);
                        evidence.CompositionCallbacks = compositions;
                        evidence.CompositionCallbackRate = evidence.ActiveSeconds > 0 ? compositions / evidence.ActiveSeconds : 0;
                        evidence.ObservedPhysicsSteps = session.PhysicsStep - startStep;
                        if (now >= nextProgress)
                        {
                            evidence.Resources.Add(Sample(watch.Elapsed.TotalSeconds, product));
                            Progress(); nextProgress += TimeSpan.FromSeconds(30);
                        }
                        Require(now.TotalSeconds <= activeSeconds + 120,
                            "Observation wall-clock limit reached; hidden/minimized time did not satisfy the active-time gate.");
                    }
                    Require(evidence.ObservedPhysicsSteps > 0 && compositions > 0,
                        "Visible observation had no genuine native steps or composition callbacks.");
                }
                finally
                {
                    CompositionTarget.Rendering -= rendered;
                    product.Window.Activated -= lifecycle;
                    product.Window.Deactivated -= lifecycle;
                    product.Window.StateChanged -= lifecycle;
                    product.Window.IsVisibleChanged -= visibility;
                }
                bool Eligible() => product.Window.IsActive && product.Window.IsVisible && product.Window.WindowState != WindowState.Minimized &&
                    product.Driver.IsReady && product.Driver.OwnsRuntime && product.Runtime.IsRunning && product.Runtime.LastError is null &&
                    product.Host.LocalPresentation == LocalRoomPresentation.Window && session.IsAttached;
            }
            RestartResult Child(string mode, string childData, string resultName)
            {
                var resultPath = Path.Combine(outputRoot, resultName);
                var errorPath = Path.Combine(outputRoot, Path.GetFileNameWithoutExtension(resultName) + ".stderr.log");
                var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("Missing review executable."))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (var argument in new[] { "--ordinary-furniture-collection-restart", mode, module, childData, resultPath })
                    start.ArgumentList.Add(argument);
                using var child = Process.Start(start) ?? throw new IOException("Could not start the isolated product restart process.");
                var errors = child.StandardError.ReadToEndAsync();
                var wait = Stopwatch.StartNew();
                while (!child.HasExited && wait.Elapsed < TimeSpan.FromSeconds(30)) Pump(50);
                if (!child.HasExited)
                {
                    child.Kill(); child.WaitForExit(); File.WriteAllText(errorPath, errors.GetAwaiter().GetResult());
                    throw new IOException("Owned restart process timed out; inspect " + Path.GetFileName(errorPath));
                }
                File.WriteAllText(errorPath, errors.GetAwaiter().GetResult());
                Require(child.ExitCode == 0 && File.Exists(resultPath), "Owned product restart process failed; inspect " + Path.GetFileName(errorPath));
                return JsonSerializer.Deserialize<RestartResult>(File.ReadAllBytes(resultPath)) ?? throw new InvalidDataException("Missing restart result.");
            }
        }
        catch (Exception error)
        {
            failure = error; evidence.Passed = false; evidence.Failure = error.Message;
        }
        finally
        {
            if (product is not null)
            {
                try { evidence.Resources.Add(Sample(watch.Elapsed.TotalSeconds, product)); }
                catch (Exception error) { FailCleanup(error); }
                try
                {
                    product.Dispose();
                    evidence.ReleasedFrameOwner = product.ReleasedFrameOwner;
                    evidence.ReleasedNativeWorld = product.ReleasedNativeWorld;
                    evidence.ClosedOwnedWindows = product.ClosedOwnedWindows;
                    Require(evidence.ReleasedFrameOwner && evidence.ReleasedNativeWorld && evidence.ClosedOwnedWindows,
                        "Product host did not release its owned frame driver, native world and windows.");
                }
                catch (Exception error) { FailCleanup(error); }
            }
            try { app.Shutdown(); }
            catch (Exception error) { FailCleanup(error); }
            evidence.WallSeconds = watch.Elapsed.TotalSeconds;
            if (evidence.Passed) evidence.Phase = "complete";
            try { Write(Path.Combine(outputRoot, "verification-summary.json"), evidence); }
            catch (Exception error) { FailCleanup(error); }
            try { Write(Path.Combine(outputRoot, "progress.json"), evidence); }
            catch (Exception error) { FailCleanup(error); }
        }
        if (failure is not null) throw new InvalidOperationException("Actual ordinary-room furniture verification failed; inspect verification-summary.json.", failure);

        void Progress()
        {
            evidence.WallSeconds = watch.Elapsed.TotalSeconds;
            if (product is not null)
            {
                evidence.RuntimeState = product.Furniture.State.ToString();
                evidence.WindowVisible = product.Window.IsVisible;
                evidence.WindowMinimized = product.Window.WindowState == WindowState.Minimized;
                evidence.Scheduler = product.Driver.FrameDiagnostics;
                evidence.LayoutVersion = product.Furniture.Layout.LayoutVersion;
            }
            Write(Path.Combine(outputRoot, "progress.json"), evidence);
        }
        void FailCleanup(Exception error)
        {
            failure ??= error; evidence.Failure ??= error.Message; evidence.Passed = false;
            Console.Error.WriteLine(error);
        }
    }

    internal static void RunRestart(string mode, string module, string dataRoot, string outputFile)
    {
        Require(mode is "save" or "load", "Restart mode must be save or load.");
        Require(Hash(module) == CandidateHash && !File.Exists(outputFile), "Restart module/evidence is not the expected fresh target.");
        RequireOwnedData(dataRoot);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var layoutPath = FurnitureLayoutStore.GetLayoutPath(dataRoot, FurnitureLayoutContract.OrdinaryRoom);
        var bytesBefore = mode == "load" ? File.ReadAllBytes(layoutPath) : null;
        var product = new ProductFixture(module, dataRoot);
        long version = 0; Guid? operation = null; IReadOnlyList<FurnitureItemInstance> items = []; var loaded = false;
        try
        {
            product.EnterFromPorchFurniture();
            var session = product.Furniture;
            if (mode == "save")
            {
                Require(session.Layout.LayoutVersion == 0 && session.Layout.Items.Count == 0, "Restart save target must be empty.");
                FillCollection(product);
            }
            else Require(bytesBefore!.AsSpan().SequenceEqual(File.ReadAllBytes(layoutPath)), "Restart load rewrote the saved file.");
            loaded = session.NativeFrame is { Items.Count: 30 } frame && frame.Items.All(item => item.InstanceId > 0) && frame.Physics.Succeeded;
            Require(loaded && session.State == OrdinaryFurnitureSceneState.Synced && session.Layout.Items.Count == 30 && session.RenderItems.Count == 30,
                "Restart product host did not genuinely apply the saved furniture.");
            Require(session.Layout.LayoutVersion == 30 && session.Layout.Items.Count(item => item.Kind == FurnitureItemKind.StaticPlatform) == 24 &&
                session.Layout.Items.Count(item => item.Kind == FurnitureItemKind.RoundBall) == 6,
                "Restart load did not preserve the exact saved version and mixed collection.");
            version = session.Layout.LayoutVersion; operation = session.Layout.LastOperationId; items = session.Layout.Items.ToArray();
        }
        finally { product.Dispose(); app.Shutdown(); }
        var result = new RestartResult(Environment.ProcessId, mode, version, operation, items, Hash(layoutPath), loaded,
            product.ReleasedFrameOwner, product.ReleasedNativeWorld, product.ClosedOwnedWindows);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
        using var output = new FileStream(outputFile, FileMode.CreateNew); JsonSerializer.Serialize(output, result, Json);
    }

    private sealed class ProductFixture : IDisposable
    {
        internal DangoApplicationHost Host { get; }
        internal StackedDangoSceneRuntime Runtime { get; }
        internal LocalRoomWindow Window => Host.LocalWindow ?? throw new InvalidOperationException("Product room was not created.");
        internal LocalRoomSceneDriver Driver => Field<LocalRoomSceneDriver>(Host, "localRoomDriver");
        internal OrdinaryRoomFurnitureSession Furniture => Runtime.Furniture ?? throw new InvalidOperationException("Missing product furniture session.");
        internal DesktopPhysicsScene Scene => Field<DesktopPhysicsScene>(Runtime, "scene");
        internal DangoPhysicsWorld World { get; }
        private readonly Window[] ownedWindows;
        internal bool ReleasedFrameOwner { get; private set; }
        internal bool ReleasedNativeWorld { get; private set; }
        internal bool ClosedOwnedWindows { get; private set; }
        internal ProductFixture(string module, string dataRoot)
        {
            var prior = Application.Current.Windows.Cast<Window>().ToHashSet();
            Host = new(settingsFile: Path.Combine(dataRoot, "settings.json"), nativeLibraryPath: module, isolatedLocalRoomReview: true);
            Runtime = Field<StackedDangoSceneRuntime>(Host, "stackedRuntime");
            try
            {
                Host.Start();
                Require(Runtime.LastError is null && Runtime.EntityIds.Count == 1, Runtime.LastError ?? "Product runtime did not initialize.");
                Pump(250);
                var home = Field<ControlCenterWindow>(Host, "controlCenterWindow").ProductHome;
                home.ResumePorch(); Pump(300);
                World = Field<DangoPhysicsWorld>(Field<DesktopPhysicsScene>(Runtime, "scene"), "world");
                ownedWindows = Application.Current.Windows.Cast<Window>().Where(window => !prior.Contains(window)).ToArray();
            }
            catch { Host.Dispose(); throw; }
        }
        internal void EnterFromPorchFurniture()
        {
            var home = Field<ControlCenterWindow>(Host, "controlCenterWindow").ProductHome;
            Require(home.PorchScene.RequestNearbyFurniture(), "Actual product porch could not request nearby ordinary-room furniture.");
            UntilHandoff();
            Require(Host.LocalPresentation == LocalRoomPresentation.Window && Window.IsFurnitureEditing && Furniture.CanEdit,
                Host.LocalRoomFailure ?? "Actual furniture room did not become ready.");
        }
        internal void UntilHandoff() => Until(() => !Host.LocalHandoffPending, 4500, "Actual product handoff timed out.");
        public void Dispose()
        {
            Host.Dispose(); Pump(40);
            ReleasedFrameOwner = Runtime.FrameDriver is null && !Runtime.IsRunning;
            ReleasedNativeWorld = Field<nint>(World, "world") == 0 && Field<bool>(World, "disposed") && World.Count == 0;
            ClosedOwnedWindows = ownedWindows.All(window => !Application.Current.Windows.Cast<Window>().Contains(window));
        }
        internal int VisibleOwnedWindows => ownedWindows.Count(window => window.IsVisible);
    }

    private static void Complete(Task task)
    {
        Until(() => task.IsCompleted, 6000, "Actual product furniture commit timed out."); task.GetAwaiter().GetResult();
    }
    private static void FillCollection(ProductFixture product, Action? saved = null)
    {
        var session = product.Furniture;
        var world = product.World;
        var generation = session.WorldGeneration;
        var entity = product.Runtime.EntityIds.Single();
        var columns = new[] { 200d, 500d, 800d, 1400d, 1700d, 1980d };
        for (var row = 0; row < 4; row++)
            foreach (var x in columns) Insert(FurnitureItemKind.StaticPlatform, new(x, 225 + row * 30));
        foreach (var x in columns)
        {
            // Placement correctly rejects the live Dango as well as furniture.
            // Real idle wandering remains enabled in the editor, so a slow setup
            // cannot assume the Dango still occupies its initial spawn. Use the
            // actual product return/entry before each ball, without pausing or
            // changing the solver, input rules, saved anchors or frame owner.
            var savedLayout = session.Layout;
            Require(product.Window.EndFurnitureEditing(), "Mixed setup could not leave the actual editor.");
            product.Host.ReturnToPorch(); product.UntilHandoff();
            product.Host.EnterLocalRoomFurniture(); product.UntilHandoff();
            Require(ReferenceEquals(session, product.Furniture) && ReferenceEquals(world, product.World) &&
                session.WorldGeneration == generation && product.Runtime.EntityIds.Single() == entity &&
                product.Window.IsFurnitureEditing && session.CanEdit && session.Layout == savedLayout &&
                session.NativeFrame is { Physics.Succeeded: true } frame && frame.Items.Count == savedLayout.Items.Count,
                "Mixed setup did not restore the same actual furniture session.");
            var center = product.Runtime.CurrentSelectedCenter;
            var contract = FurnitureLayoutContract.OrdinaryRoom;
            Require(center is { } point && point.X >= contract.SpawnProtectionLeft && point.X <= contract.SpawnProtectionRight &&
                product.Scene.GroundY - point.Y >= contract.SpawnProtectionBottom &&
                product.Scene.GroundY - point.Y <= contract.SpawnProtectionTop,
                "Mixed setup did not genuinely restore the Dango to the protected spawn.");
            foreach (var item in session.RenderItems.Where(item => item.Kind == FurnitureItemKind.RoundBall))
            {
                var anchor = savedLayout.Find(item.InstanceId)?.Anchor;
                Require(anchor is { } placed && Math.Abs(item.Anchor.X - placed.X) < .25 && Math.Abs(item.Anchor.Y - placed.Y) < .25,
                    "Mixed setup did not genuinely restore an existing ball's confirmed anchor.");
            }
            Insert(FurnitureItemKind.RoundBall, new(x, 66.1));
        }
        void Insert(FurnitureItemKind kind, FurnitureAnchor anchor)
        {
            var before = session.Layout.LayoutVersion;
            var count = session.Layout.Items.Count;
            session.PreviewNewFurniture(kind, anchor);
            var preview = session.Preview;
            if (preview is null || !session.CanConfirm)
                throw new InvalidOperationException("Actual mixed furniture insertion was rejected: " +
                    JsonSerializer.Serialize(new { kind, anchor, before, count, status = session.Status, frame = session.NativeFrame }, Json));
            Complete(session.ConfirmAsync());
            Require(session.Layout.LayoutVersion == before + 1 && session.Layout.Items.Count == count + 1 &&
                session.Layout.Find(preview!.InstanceId) == preview, "Insertion changed a GUID, anchor, count or version.");
            saved?.Invoke();
        }
    }
    private static void ExportActualRoomImages(ProductFixture product, string outputRoot)
    {
        var window = product.Window;
        var originalWidth = window.Width; var originalHeight = window.Height;
        var captures = new List<object>();
        try
        {
            foreach (var geometry in new[] { (Name: "minimum", Width: 640d, Height: 420d),
                (Name: "landscape", Width: 1200d, Height: 620d), (Name: "portrait", Width: 640d, Height: 800d),
                (Name: "default", Width: 960d, Height: 600d) })
            {
                window.Width = geometry.Width; window.Height = geometry.Height; Pump(160);
                var content = window.Content as FrameworkElement ?? throw new InvalidOperationException("Missing actual room content visual.");
                content.UpdateLayout();
                Require(content.ActualWidth > 0 && content.ActualHeight > 0 && window.IsFurnitureEditing &&
                    product.Furniture.NativeFrame?.Items.Count == 30 && product.Window.Scene.FurnitureItems.Count == 30,
                    "Actual thirty-item furniture room visual is not ready for capture.");
                var deviceDpi = VisualTreeHelper.GetDpi(content);
                foreach (var dpi in new[] { 96d, 144d })
                {
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi / 96),
                        (int)Math.Ceiling(content.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var name = $"actual-room-editor-{geometry.Name}-{dpi:0}dpi-offscreen.png";
                    using (var output = new FileStream(Path.Combine(outputRoot, name), FileMode.CreateNew)) encoder.Save(output);
                    captures.Add(new { File = name, WindowWidthDip = geometry.Width, WindowHeightDip = geometry.Height,
                        ContentWidthDip = content.ActualWidth, ContentHeightDip = content.ActualHeight,
                        RenderDpi = dpi, ActualDeviceDpiX = deviceDpi.PixelsPerInchX,
                        ActualDeviceDpiY = deviceDpi.PixelsPerInchY,
                        EvidenceKind = "Actual LocalRoomWindow Content RenderTargetBitmap; offscreen 144 DPI is not real OS 150% input/layout acceptance" });
                }
            }
        }
        finally { window.Width = originalWidth; window.Height = originalHeight; Pump(100); }
        Write(Path.Combine(outputRoot, "actual-room-images.json"), captures);
    }
    private static void Until(Func<bool> condition, int timeoutMilliseconds, string message)
    {
        var watch = Stopwatch.StartNew();
        do { Pump(30); } while (!condition() && watch.ElapsedMilliseconds < timeoutMilliseconds);
        Require(condition(), message);
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static ResourceSample Sample(double seconds, ProductFixture fixture)
    {
        using var process = Process.GetCurrentProcess(); process.Refresh();
        return new(seconds, process.PrivateMemorySize64, process.WorkingSet64, process.HandleCount, process.Threads.Count, fixture.VisibleOwnedWindows);
    }
    private static T Field<T>(object instance, string name) => (T)(instance.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) ?? throw new InvalidOperationException("Missing product diagnostic field: " + name));
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Write(string path, object value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json)); File.Move(temporary, path, overwrite: true);
    }
    private static void PrepareOwnedData(string root)
    {
        if (Directory.Exists(root))
        {
            var files = Directory.EnumerateFileSystemEntries(root).ToArray();
            Require(files.Length == 0 || File.Exists(Path.Combine(root, "owner.json")), "Refusing an unowned data directory.");
        }
        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, "owner.json");
        if (!File.Exists(marker))
            File.WriteAllText(marker, JsonSerializer.Serialize(new { purpose = "ordinary-furniture-collection-acceptance", schema = 1, owner = Guid.NewGuid() }, Json));
        RequireOwnedData(root);
    }
    private static void RequireOwnedData(string root)
    {
        using var marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "owner.json")));
        Require(marker.RootElement.GetProperty("purpose").GetString() == "ordinary-furniture-collection-acceptance",
            "Furniture verification data is not explicitly owned by this isolated review.");
    }
}
