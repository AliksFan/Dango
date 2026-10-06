using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class LocalRoomWallpaperPettingManualTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("ManualPhysicalInput")]
    public void PhysicalShiftStrokeReachesNativeWallpaperFeedbackAndNormalUp()
    {
        if (Environment.GetEnvironmentVariable("DANGO_MANUAL_WALLPAPER_PETTING") != "1")
            Assert.Inconclusive("Set DANGO_MANUAL_WALLPAPER_PETTING=1 only for an attended physical-input diagnostic.");

        var output = Path.Combine(TestRepository.Root, "artifacts", "local-room", "manual-petting", Guid.NewGuid().ToString("N"));
        var readiness = Path.Combine(TestRepository.Root, "artifacts", "local-room", "manual-petting-readiness.json");
        Directory.CreateDirectory(output);
        var host = new DangoApplicationHost(settingsFile: Path.Combine(output, "settings.json"),
            nativeLibraryPath: Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName),
            isolatedLocalRoomReview: true);
        WallpaperGameplayInputBridge? bridge = null;
        WallpaperWheelInputBridge? wheelBridge = null;
        Observation? observation = null;
        try
        {
            var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
            Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
            host.ShowControlCenter(); Pump(350);
            host.EnterLocalRoom(); WaitForHandoff(host);
            Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
            host.SwitchLocalRoomPresentation(LocalRoomPresentation.Wallpaper); WaitForHandoff(host);
            Assert.AreEqual(LocalRoomPresentation.Wallpaper, host.LocalPresentation, host.LocalRoomFailure);
            var wallpaper = Field<LocalRoomWallpaperHost>(host, "localRoomWallpaper");
            Assert.IsTrue(wallpaper.Scene.HasHealthyFrame);
            bridge = Field<WallpaperGameplayInputBridge>(wallpaper, "gameplayBridge");
            wheelBridge = Field<WallpaperWheelInputBridge>(wallpaper, "wheelBridge");
            observation = new(bridge, wallpaper, runtime);
            observation.Install();
            File.WriteAllText(readiness, "{\"Ready\":true}");
            TestContext.WriteLine("Physical input ready for at most 180 seconds: click blank wallpaper to establish Shell foreground, then hold Shift before pressing the body, stroke horizontally inside its body, and release the left button.");
            // Only the user supplies foreground, cursor movement and button input. No synthetic hook event or direct driver call is used.
            var watch = Stopwatch.StartNew();
            var nextStatus = TimeSpan.Zero;
            while (!observation.Succeeded && watch.Elapsed < TimeSpan.FromSeconds(180))
            {
                observation.ObserveListenerStatus();
                if (watch.Elapsed >= nextStatus)
                {
                    // Overwrite aggregate state only; never save event records, keys, coordinates or window identities.
                    File.WriteAllText(readiness, System.Text.Json.JsonSerializer.Serialize(new { Ready = true, Summary = observation.Summary() }));
                    nextStatus = watch.Elapsed + TimeSpan.FromSeconds(1);
                }
                Pump(25);
            }
        }
        finally
        {
            try
            {
                File.WriteAllText(readiness, "{\"Ready\":false}");
            }
            finally
            {
                try
                {
                    host.Dispose();
                }
                finally
                {
                    try
                    {
                        try
                        {
                            if (bridge is { IsHookReleasePending: true } or { IsListening: true }) bridge.Stop();
                        }
                        finally
                        {
                            if (wheelBridge is { IsHookReleasePending: true } or { IsListening: true }) wheelBridge.Stop();
                        }
                    }
                    finally
                    {
                        try { observation?.RestoreFields(); }
                        finally
                        {
                            if (observation is not null) TestContext.WriteLine(observation.Summary());
                            TestContext.WriteLine($"GameplayHooksReleased={bridge is null || (!bridge.IsListening && !bridge.IsHookReleasePending)}.");
                            TestContext.WriteLine($"WheelHooksReleased={wheelBridge is null || (!wheelBridge.IsListening && !wheelBridge.IsHookReleasePending)}.");
                        }
                    }
                }
            }
        }
        Assert.IsNotNull(observation);
        Assert.IsTrue(observation.Succeeded,
            "No complete physical Shift stroke started with fully open eyes, produced new feedback and a later real Soft eye frame with left openness below .4, and completed a normal Up within 180 seconds; boolean summaries distinguish each gate.");
        Assert.IsNotNull(bridge);
        Assert.IsFalse(bridge.IsListening);
        Assert.IsFalse(bridge.IsHookReleasePending, "The manual diagnostic must release its native gameplay hooks.");
        Assert.IsNotNull(wheelBridge);
        Assert.IsFalse(wheelBridge.IsListening);
        Assert.IsFalse(wheelBridge.IsHookReleasePending, "The manual diagnostic must release its native wheel hook.");
    }

    private sealed class Observation : IWallpaperGameplayInputPlatform
    {
        private static readonly FieldInfo ActionsField = RequiredField("actions");
        private static readonly FieldInfo PlatformField = RequiredField("platform");
        private readonly WallpaperGameplayInputBridge bridge;
        private readonly LocalRoomWallpaperHost wallpaper;
        private readonly StackedDangoSceneRuntime runtime;
        private readonly LocalRoomSceneDriver driver;
        private readonly WallpaperGameplayInputActions originalActions;
        private readonly IWallpaperGameplayInputPlatform originalPlatform;
        private NativeMethods.LowLevelMouseProcedure? observedMouseCallback;
        private NativeMethods.LowLevelKeyboardProcedure? observedKeyboardCallback;
        private int currentMouseMessage = -1;
        private bool currentShift, eventsSubscribed;
        private bool shiftSceneHealthy, shiftSceneBlocked, shiftContextReady;
        private DangoEntityId? lastPublishedEntity, currentAttemptEntity;
        private AttachmentPresentationFrame? lastPublishedAttachment;
        private int currentAttachmentGeneration = -1;
        private long attemptEpoch, downBaselineStep = -1, feedbackBaselineStep = -1;
        private bool currentShiftAttempt, currentAttemptPetted, currentAttemptNormalUp, currentAttemptEyeFrame;
        private bool keyboardCallbackObserved, mouseCallbackObserved, downCallbackObserved, rawDownObserved, injectedDownObserved, injectedShiftDownObserved;
        private bool listenerStoppedDuringObservation;
        private bool downObserved, shiftDownObserved, downReachedAction, downAccepted;
        private bool shiftMoveReachedAction, shiftMoveProducedFeedback, upAccepted, hitObserved, missObserved;
        private bool shiftBodyHitObserved, shiftBodyMissObserved;
        private bool feedbackExpiredBeforeUp, upPreservedActiveFeedback, upClearedActiveFeedback;
        private bool notAcceptedObserved, captureRejectedObserved, captureAcceptedObserved, forbiddenModifierObserved;
        private bool publishedFrameObserved, downStartedFullyOpen, downStartedWithoutFeedback, cleanShiftStartObserved;
        private bool sameEntityFrameObserved, sameAttachmentGenerationFrameObserved, laterThanDownFrameObserved;
        private bool laterThanFeedbackMoveFrameObserved, softEyesFrameObserved, leftEyeBelowThresholdFrameObserved;
        private bool sameStrokeEyeFrameObserved, normalUpObserved, pendingAttemptInvalidated;
        private readonly Qualifications eventQualifications = new();
        private readonly Qualifications commandQualifications = new();
        private readonly Qualifications shiftDownQualifications = new();
        private readonly HashSet<RoomInputInspectionPhase> shiftDownPhases = [];
        private readonly HashSet<RoomPointerRole> shiftDownPointerRoles = [];
        private readonly HashSet<RoomInputInspectionPhase> shiftPointerPhases = [];
        private readonly HashSet<RoomPointerRole> shiftPointerRoles = [];
        private bool shiftBudgetExpired, shiftInspectionException;
        internal bool Succeeded { get; private set; }

        internal Observation(WallpaperGameplayInputBridge bridge, LocalRoomWallpaperHost wallpaper, StackedDangoSceneRuntime runtime)
        {
            this.bridge = bridge; this.wallpaper = wallpaper; this.runtime = runtime;
            driver = Field<LocalRoomSceneDriver>(wallpaper, "driver");
            originalActions = (WallpaperGameplayInputActions)ActionsField.GetValue(bridge)!;
            originalPlatform = (IWallpaperGameplayInputPlatform)PlatformField.GetValue(bridge)!;
            Assert.IsInstanceOfType<NativeWallpaperGameplayInputPlatform>(originalPlatform,
                "The physical diagnostic must observe the original native platform.");
        }

        internal void Install()
        {
            bridge.Stop();
            Assert.IsFalse(bridge.IsHookReleasePending, "Observation cannot install over an unreleased native hook.");
            eventsSubscribed = true;
            wallpaper.Scene.FrameApplied += ObservePublishedFrame;
            wallpaper.Scene.InputInvalidated += InvalidateAttempt;
            runtime.InputCancelled += InvalidateAttempt;
            driver.PointerCancelled += InvalidateAttempt;
            var observedActions = new WallpaperGameplayInputActions(originalActions.Key, Down, Move, Up,
                point =>
                {
                    var hit = originalActions.IsPetHit(point);
                    hitObserved |= hit; missObserved |= !hit;
                    if (currentMouseMessage == 0x0201 && currentShift)
                    { shiftBodyHitObserved |= hit; shiftBodyMissObserved |= !hit; }
                    return hit;
                },
                originalActions.MenuRequested,
                () => { InvalidateAttempt(); originalActions.ClearInput(); });
            ActionsField.SetValue(bridge, observedActions);
            PlatformField.SetValue(bridge, this);
            Assert.AreSame(observedActions, ActionsField.GetValue(bridge), "The original action wrappers must actually install.");
            Assert.AreSame(this, PlatformField.GetValue(bridge), "The original native platform wrapper must actually install.");
            bridge.Start();
            Assert.IsTrue(bridge.IsListening, "The diagnostic must retain real native keyboard and mouse hooks.");
        }

        internal void RestoreFields()
        {
            try
            {
                if (!ReferenceEquals(ActionsField.GetValue(bridge), originalActions))
                    ActionsField.SetValue(bridge, originalActions);
            }
            finally
            {
                try
                {
                    if (!ReferenceEquals(PlatformField.GetValue(bridge), originalPlatform))
                        PlatformField.SetValue(bridge, originalPlatform);
                }
                finally
                {
                    if (eventsSubscribed)
                    {
                        wallpaper.Scene.FrameApplied -= ObservePublishedFrame;
                        wallpaper.Scene.InputInvalidated -= InvalidateAttempt;
                        runtime.InputCancelled -= InvalidateAttempt;
                        driver.PointerCancelled -= InvalidateAttempt;
                        eventsSubscribed = false;
                    }
                    ClearAttempt();
                    lastPublishedEntity = null;
                    lastPublishedAttachment = null;
                }
            }
        }

        private bool Down(WallpaperWheelPoint point, bool shift)
        {
            downReachedAction = true;
            ClearAttempt();
            var epoch = attemptEpoch;
            var baseline = lastPublishedAttachment;
            var entity = lastPublishedEntity;
            var feedbackWasInactive = !runtime.HasLightFeedback;
            var eyesWereFullyOpen = baseline is { Face.Left.Visible: true, Face.Right.Visible: true } &&
                baseline.Face.Left.Openness >= .98 && baseline.Face.Right.Openness >= .98;
            downStartedWithoutFeedback |= feedbackWasInactive;
            downStartedFullyOpen |= eyesWereFullyOpen;
            var cleanBaseline = eyesWereFullyOpen && feedbackWasInactive && entity is not null && baseline is not null &&
                wallpaper.Scene.HasHealthyFrame && !wallpaper.Scene.InputBlocked &&
                TryReadRuntimeFrame(out var currentEntity, out var currentAttachment) &&
                currentEntity == entity && currentAttachment.Generation == baseline.Generation &&
                currentAttachment.StepIndex == baseline.StepIndex;
            var accepted = originalActions.PointerDown(point, shift);
            downAccepted |= accepted;
            var lastStartResult = wallpaper.LastPointerStartResult;
            notAcceptedObserved |= lastStartResult == RoomPointerStartResult.NotAccepted;
            captureRejectedObserved |= lastStartResult == RoomPointerStartResult.CaptureRejected;
            captureAcceptedObserved |= lastStartResult == RoomPointerStartResult.Accepted;
            if (epoch == attemptEpoch && accepted && shift && cleanBaseline && baseline is not null &&
                TryReadRuntimeFrame(out var acceptedEntity, out var acceptedAttachment) &&
                acceptedEntity == entity && acceptedAttachment.Generation == baseline.Generation)
            {
                currentShiftAttempt = cleanShiftStartObserved = true;
                currentAttemptEntity = entity;
                currentAttachmentGeneration = baseline.Generation;
                downBaselineStep = baseline.StepIndex;
            }
            return accepted;
        }

        private bool Move(WallpaperWheelPoint point, bool shift)
        {
            shiftMoveReachedAction |= shift;
            var epoch = attemptEpoch;
            var baseline = lastPublishedAttachment;
            var feedbackWasInactive = !runtime.HasLightFeedback;
            var accepted = originalActions.PointerMove(point, shift);
            if (epoch == attemptEpoch && currentShiftAttempt && !currentAttemptPetted && !currentAttemptNormalUp &&
                accepted && shift && feedbackWasInactive && runtime.HasLightFeedback &&
                lastPublishedEntity == currentAttemptEntity && baseline is not null &&
                baseline.Generation == currentAttachmentGeneration)
            {
                currentAttemptPetted = shiftMoveProducedFeedback = true;
                feedbackBaselineStep = baseline.StepIndex;
            }
            return accepted;
        }

        private bool Up(WallpaperWheelPoint point, bool shift)
        {
            var epoch = attemptEpoch;
            var feedbackWasActive = runtime.HasLightFeedback;
            var accepted = originalActions.PointerUp(point, shift);
            upAccepted |= accepted;
            // Cancellation may return true; its event changes the epoch before this wrapper resumes.
            if (epoch == attemptEpoch && currentShiftAttempt && currentAttemptPetted && accepted)
            {
                feedbackExpiredBeforeUp |= !feedbackWasActive;
                upPreservedActiveFeedback |= feedbackWasActive && runtime.HasLightFeedback;
                upClearedActiveFeedback |= feedbackWasActive && !runtime.HasLightFeedback;
                if (!feedbackWasActive || runtime.HasLightFeedback)
                {
                    currentAttemptNormalUp = normalUpObserved = true;
                    Succeeded |= currentAttemptEyeFrame;
                    // Keep this attempt pending if Up precedes the real eye frame.
                    return accepted;
                }
            }
            InvalidateAttempt();
            return accepted;
        }

        private void ObservePublishedFrame()
        {
            if (!wallpaper.Scene.HasHealthyFrame || wallpaper.Scene.InputBlocked ||
                !TryReadRuntimeFrame(out var entity, out var attachment))
            {
                InvalidateAttempt();
                return;
            }
            publishedFrameObserved = true;
            lastPublishedEntity = entity;
            lastPublishedAttachment = attachment;
            if (!currentShiftAttempt) return;
            var sameEntity = entity == currentAttemptEntity;
            var sameGeneration = sameEntity && attachment.Generation == currentAttachmentGeneration;
            sameEntityFrameObserved |= sameEntity;
            sameAttachmentGenerationFrameObserved |= sameGeneration;
            if (!sameEntity || !sameGeneration) { InvalidateAttempt(); return; }
            if (!currentAttemptPetted) return;
            var laterThanDown = attachment.StepIndex > downBaselineStep;
            var laterThanFeedbackMove = attachment.StepIndex > feedbackBaselineStep;
            laterThanDownFrameObserved |= laterThanDown;
            laterThanFeedbackMoveFrameObserved |= laterThanFeedbackMove;
            if (!laterThanDown || !laterThanFeedbackMove) return;
            var softEyes = attachment.Face.Left.Visible && attachment.Face.Right.Visible &&
                attachment.Face.Left.Shape == EyeShape.Soft && attachment.Face.Right.Shape == EyeShape.Soft;
            var leftEyeBelowThreshold = attachment.Face.Left.Visible && attachment.Face.Left.Openness < .4;
            softEyesFrameObserved |= softEyes;
            leftEyeBelowThresholdFrameObserved |= leftEyeBelowThreshold;
            if (!softEyes || !leftEyeBelowThreshold) return;
            currentAttemptEyeFrame = sameStrokeEyeFrameObserved = true;
            Succeeded |= currentAttemptNormalUp;
        }

        private bool TryReadRuntimeFrame(out DangoEntityId entity, out AttachmentPresentationFrame attachment)
        {
            entity = default;
            attachment = null!;
            if (runtime.LastFrame is not { EntityIds.Count: 1, Bodies.Count: 1 } frame ||
                frame.Bodies[0].Attachments is not { } actualAttachment) return false;
            entity = frame.EntityIds[0];
            attachment = actualAttachment;
            return true;
        }

        private void InvalidateAttempt()
        {
            pendingAttemptInvalidated |= currentShiftAttempt;
            ClearAttempt();
            lastPublishedEntity = null;
            lastPublishedAttachment = null;
        }

        private void ClearAttempt()
        {
            attemptEpoch++;
            currentShiftAttempt = currentAttemptPetted = currentAttemptNormalUp = currentAttemptEyeFrame = false;
            currentAttemptEntity = null;
            currentAttachmentGeneration = -1;
            downBaselineStep = feedbackBaselineStep = -1;
        }

        public nint InstallKeyboard(NativeMethods.LowLevelKeyboardProcedure callback)
        {
            observedKeyboardCallback = (code, message, data) =>
            {
                keyboardCallbackObserved |= code >= 0;
                return callback(code, message, data);
            };
            return originalPlatform.InstallKeyboard(observedKeyboardCallback);
        }
        public nint InstallMouse(NativeMethods.LowLevelMouseProcedure callback)
        {
            observedMouseCallback = (code, message, data) =>
            {
                var previousMessage = currentMouseMessage;
                var previousShift = currentShift;
                currentMouseMessage = code >= 0 ? unchecked((int)message) : -1;
                currentShift = false;
                mouseCallbackObserved |= code >= 0;
                downCallbackObserved |= code >= 0 && currentMouseMessage == 0x0201;
                try { return callback(code, message, data); }
                finally { currentMouseMessage = previousMessage; currentShift = previousShift; }
            };
            return originalPlatform.InstallMouse(observedMouseCallback);
        }
        public bool UninstallKeyboard(nint hook) => originalPlatform.UninstallKeyboard(hook);
        public bool UninstallMouse(nint hook) => originalPlatform.UninstallMouse(hook);
        public nint NextKeyboard(nint hook, int code, nint message, nint data) => originalPlatform.NextKeyboard(hook, code, message, data);
        public nint NextMouse(nint hook, int code, nint message, nint data) => originalPlatform.NextMouse(hook, code, message, data);
        public WallpaperGameplayKeyData ReadKeyboard(nint data) => originalPlatform.ReadKeyboard(data);
        public WallpaperGameplayMouseData ReadMouse(nint data)
        {
            var input = originalPlatform.ReadMouse(data);
            currentShift = input.Shift && (input.Flags & 0x01) == 0;
            if (currentMouseMessage == 0x0201)
            {
                rawDownObserved = true;
                injectedDownObserved |= (input.Flags & 0x01) != 0;
                injectedShiftDownObserved |= input.Shift && (input.Flags & 0x01) != 0;
            }
            if ((input.Flags & 0x01) == 0)
            {
                forbiddenModifierObserved |= input.HasForbiddenModifier;
                if (currentMouseMessage == 0x0201)
                {
                    downObserved = true; shiftDownObserved |= input.Shift;
                    if (input.Shift)
                    {
                        shiftSceneHealthy |= wallpaper.Scene.HasHealthyFrame;
                        shiftSceneBlocked |= wallpaper.Scene.InputBlocked;
                        shiftContextReady |= wallpaper.IsVisible && wallpaper.Scene.HasHealthyFrame && !wallpaper.Scene.InputBlocked;
                    }
                }
            }
            return input;
        }
        public bool IsKeyPressed(int virtualKey) => originalPlatform.IsKeyPressed(virtualKey);
        public WallpaperWheelInputEnvironment Inspect(WallpaperWheelInputContext context, WallpaperWheelPoint? point, bool atEvent, bool allowShift)
        {
            var environment = originalPlatform.Inspect(context, point, atEvent, allowShift);
            if (point is not null && currentShift && originalPlatform is NativeWallpaperGameplayInputPlatform native)
            {
                var trace = native.LastInspectionTrace;
                shiftPointerPhases.Add(trace.Phase); shiftPointerRoles.Add(trace.Pointer.Role);
                shiftBudgetExpired |= trace.BudgetExpired; shiftInspectionException |= trace.Exception;
                if (atEvent && currentMouseMessage == 0x0201)
                { shiftDownPhases.Add(trace.Phase); shiftDownPointerRoles.Add(trace.Pointer.Role); }
            }
            if (point is not null)
            {
                (atEvent ? eventQualifications : commandQualifications).Observe(environment);
                if (atEvent && currentMouseMessage == 0x0201 && currentShift)
                    shiftDownQualifications.Observe(environment);
            }
            return environment;
        }

        internal string Summary() =>
            $"KeyboardCallbackObserved={keyboardCallbackObserved}, MouseCallbackObserved={mouseCallbackObserved}, DownCallbackObserved={downCallbackObserved}, RawDownObserved={rawDownObserved}, InjectedDownObserved={injectedDownObserved}, InjectedShiftDownObserved={injectedShiftDownObserved}, ListenerStoppedDuringObservation={listenerStoppedDuringObservation}. " +
            $"Physical pointer summary: LowLevelDownObserved={downObserved}, ShiftDownObserved={shiftDownObserved}, CommandDownReached={downReachedAction}, DownAccepted={downAccepted}, PublishedFrameObserved={publishedFrameObserved}, DownStartedFullyOpen={downStartedFullyOpen}, DownStartedWithoutFeedback={downStartedWithoutFeedback}, CleanShiftStartObserved={cleanShiftStartObserved}, ShiftMoveReachedAction={shiftMoveReachedAction}, ShiftMoveProducedRealFeedback={shiftMoveProducedFeedback}, UpAccepted={upAccepted}, NormalUpObserved={normalUpObserved}, SameEntityFrameObserved={sameEntityFrameObserved}, SameAttachmentGenerationFrameObserved={sameAttachmentGenerationFrameObserved}, LaterThanDownFrameObserved={laterThanDownFrameObserved}, LaterThanFeedbackMoveFrameObserved={laterThanFeedbackMoveFrameObserved}, SoftEyesFrameObserved={softEyesFrameObserved}, LeftEyeBelowThresholdFrameObserved={leftEyeBelowThresholdFrameObserved}, SameStrokeEyeFrameObserved={sameStrokeEyeFrameObserved}, SameStrokeSucceeded={Succeeded}, PendingAttemptInvalidated={pendingAttemptInvalidated}, FeedbackExpiredBeforeUp={feedbackExpiredBeforeUp}, UpPreservedActiveFeedback={upPreservedActiveFeedback}, UpClearedActiveFeedback={upClearedActiveFeedback}, BodyHitObserved={hitObserved}, BodyMissObserved={missObserved}, ShiftBodyHitObserved={shiftBodyHitObserved}, ShiftBodyMissObserved={shiftBodyMissObserved}, StartNotAcceptedObserved={notAcceptedObserved}, CaptureRejectedObserved={captureRejectedObserved}, CaptureAcceptedObserved={captureAcceptedObserved}, ForbiddenModifierObserved={forbiddenModifierObserved}, ShiftSceneHealthyObserved={shiftSceneHealthy}, ShiftSceneBlockedObserved={shiftSceneBlocked}, ShiftContextReadyObserved={shiftContextReady}, ShiftDownPhases={string.Join(',', shiftDownPhases.Order())}, ShiftDownPointerRoles={string.Join(',', shiftDownPointerRoles.Order())}, ShiftPointerPhases={string.Join(',', shiftPointerPhases.Order())}, ShiftPointerRoles={string.Join(',', shiftPointerRoles.Order())}, ShiftBudgetExpired={shiftBudgetExpired}, ShiftInspectionException={shiftInspectionException}. {eventQualifications.Summary("Event")} {commandQualifications.Summary("Command")} {shiftDownQualifications.Summary("ShiftDown")}";

        internal void ObserveListenerStatus() => listenerStoppedDuringObservation |= !bridge.IsListening;

        private static FieldInfo RequiredField(string name) => typeof(WallpaperGameplayInputBridge)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("Required native observation field is unavailable: " + name);
    }

    private sealed class Qualifications
    {
        private bool observed, known, unknown, foreground, nonForeground, blocking, background, nonBackground, icon, modifier;
        internal void Observe(WallpaperWheelInputEnvironment value)
        {
            observed = true; known |= value.IsKnown; unknown |= !value.IsKnown;
            foreground |= value.IsDesktopForeground; nonForeground |= !value.IsDesktopForeground;
            blocking |= value.HasFullscreenApplication; background |= value.IsPointerOnDesktop;
            nonBackground |= !value.IsPointerOnDesktop; icon |= value.IsDesktopIcon; modifier |= value.HasModifier;
        }
        internal string Summary(string phase) =>
            $"{phase}Qualifications: Observed={observed}, KnownObserved={known}, UnknownObserved={unknown}, ShellForegroundObserved={foreground}, NonShellForegroundObserved={nonForeground}, BlockingObserved={blocking}, BackgroundObserved={background}, NonBackgroundObserved={nonBackground}, IconObserved={icon}, ModifierRejectedObserved={modifier}.";
    }

    private static T Field<T>(object instance, string name) =>
        (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)
            ?? throw new AssertFailedException("Required product field is unavailable: " + name));

    private static void WaitForHandoff(DangoApplicationHost host)
    {
        var watch = Stopwatch.StartNew();
        while (host.LocalHandoffPending && watch.Elapsed < TimeSpan.FromSeconds(2)) Pump(25);
        Assert.IsFalse(host.LocalHandoffPending, "A healthy real room frame is required: " + host.LocalRoomFailure);
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
