using System.Windows;
using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Physics;

namespace Dango.Desktop.Tests.AppShell;

// These contracts use fabricated hook input and install no operating-system hook or application window.
[TestClass]
[DoNotParallelize]
public sealed class DesktopPetPointerGestureBridgeTests
{
    private const int MouseMove = 0x0200;
    private const int LeftButtonDown = 0x0201;
    private const int LeftButtonUp = 0x0202;

    [STATestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void NoHookCanStartWithoutBothItsOwnActiveGestureAndCapture(bool active, bool capture)
    {
        using var rig = new BridgeFixture { OwnGestureActive = active, OwnCapture = capture };
        Assert.IsFalse(rig.Bridge.Start());
        Assert.IsFalse(rig.Bridge.IsListening);
        Assert.AreEqual(0, rig.Platform.InstallCount);
        Assert.HasCount(0, rig.Moves);
        Assert.HasCount(0, rig.Releases);
    }

    [STATestMethod]
    public void OnlyOwnActiveMoveAndReleaseAreReadAndTheHookChainAlwaysContinues()
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        Assert.AreEqual((nint)987, rig.Platform.Emit(LeftButtonDown, new(10, 20), timestamp: 10));
        Assert.AreEqual((nint)987, rig.Platform.Emit(MouseMove, new(11, 21), timestamp: 11, code: -1));
        Assert.AreEqual(0, rig.Platform.ReadCount);
        Assert.AreEqual((nint)987, rig.Platform.Emit(MouseMove, new(12, 22), timestamp: 12));
        Assert.HasCount(0, rig.Moves, "The native callback must not mutate physics or visuals.");
        DrainInput();
        CollectionAssert.AreEqual(new[] { (new Point(12, 22), false) }, rig.Moves);
        rig.Bridge.Stop();
        Assert.AreEqual((nint)987, rig.Platform.Emit(MouseMove, new(99, 99), timestamp: 13));
        Assert.AreEqual((nint)987, rig.Platform.Emit(LeftButtonUp, new(99, 99), timestamp: 14));
        DrainInput();
        Assert.AreEqual(1, rig.Platform.ReadCount, "A stopped hook delegate must not inspect unrelated input.");
        Assert.AreEqual(5, rig.Platform.NextCount);
        Assert.HasCount(1, rig.Moves);
        Assert.HasCount(0, rig.Releases);
    }

    [STATestMethod]
    public void MoveBurstCoalescesToLatestScreenPointAndShiftAtDispatcherBoundary()
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        for (uint i = 1; i <= 40; i++) rig.Platform.Emit(MouseMove, new(i, i + 10), timestamp: i, shift: i == 40);
        Assert.HasCount(0, rig.Moves);
        DrainInput();
        CollectionAssert.AreEqual(new[] { (new Point(40, 50), true) }, rig.Moves);
        Assert.AreEqual(40, rig.Platform.ReadCount);
        Assert.AreEqual(40, rig.Platform.NextCount);
        Assert.IsTrue(rig.Bridge.IsListening);
    }

    [STATestMethod]
    public void ReleasePreservesPendingMoveStopsBeforeSinkAndRejectsSubsequentEvents()
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        rig.Platform.Emit(MouseMove, new(10, 20), timestamp: 1);
        rig.Platform.Emit(LeftButtonUp, new(30, 40), timestamp: 2, shift: true);
        rig.Platform.Emit(MouseMove, new(90, 90), timestamp: 3);
        rig.Platform.Emit(LeftButtonUp, new(90, 90), timestamp: 4);
        Assert.HasCount(0, rig.Releases);
        DrainInput();
        CollectionAssert.AreEqual(new[] { (new Point(10, 20), false) }, rig.Moves,
            "The queued release must preserve the actual preceding move while rejecting later hook events.");
        CollectionAssert.AreEqual(new[] { (new Point(30, 40), true) }, rig.Releases);
        Assert.IsTrue(rig.ReleaseObservedInactiveBridge);
        Assert.IsFalse(rig.Bridge.IsListening);
        Assert.AreEqual(2, rig.Platform.ReadCount, "Once release is queued, later physical events cannot replace it.");
        Assert.AreEqual(4, rig.Platform.NextCount);
        Assert.AreEqual(1, rig.Platform.UninstallCount);
    }

    [STATestMethod]
    public void PendingOldGenerationCannotMoveOrReleaseAfterStopAndRestart()
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        rig.Platform.Emit(LeftButtonUp, new(10, 20), timestamp: 1);
        rig.Bridge.Stop();
        Assert.IsTrue(rig.Bridge.Start());
        DrainInput();
        Assert.HasCount(0, rig.Moves);
        Assert.HasCount(0, rig.Releases);
        Assert.AreEqual(0, rig.CancelCount);
        Assert.IsTrue(rig.Bridge.IsListening);
        rig.Platform.Emit(MouseMove, new(30, 40), timestamp: 2);
        DrainInput();
        CollectionAssert.AreEqual(new[] { (new Point(30, 40), false) }, rig.Moves);
        Assert.HasCount(0, rig.Releases);
        Assert.AreEqual(2, rig.Platform.InstallCount);
    }

    [STATestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void LosingOwnGestureOrCaptureBeforeDrainCancelsInsteadOfApplyingMove(bool active, bool capture)
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        rig.Platform.Emit(MouseMove, new(10, 20), timestamp: 1);
        rig.OwnGestureActive = active;
        rig.OwnCapture = capture;
        DrainInput();
        Assert.HasCount(0, rig.Moves);
        Assert.HasCount(0, rig.Releases);
        Assert.AreEqual(1, rig.CancelCount);
        Assert.IsFalse(rig.Bridge.IsListening);
        Assert.AreEqual(1, rig.Platform.UninstallCount);
    }

    [STATestMethod]
    public void LastMoveSinkLosingCaptureMustCancelQueuedReleaseEvenWithoutSynchronousStop()
    {
        BridgeFixture? observedRig = null;
        using var rig = new BridgeFixture(observeMove: (_, _) => observedRig!.OwnCapture = false);
        observedRig = rig;
        Assert.IsTrue(rig.Bridge.Start());
        rig.Platform.Emit(MouseMove, new(10, 20), timestamp: 1);
        rig.Platform.Emit(LeftButtonUp, new(30, 40), timestamp: 2);
        DrainInput();
        CollectionAssert.AreEqual(new[] { (new Point(10, 20), false) }, rig.Moves);
        Assert.HasCount(0, rig.Releases, "The UP cannot act on capture lost during the preceding move sink.");
        Assert.AreEqual(1, rig.CancelCount);
        Assert.IsFalse(rig.Bridge.IsListening);
        Assert.AreEqual(1, rig.Platform.UninstallCount);
    }

    [STATestMethod]
    public void ForbiddenModifierCancelsQueuedInputWithoutApplyingIt()
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        rig.Platform.Emit(MouseMove, new(10, 20), timestamp: 1, forbiddenModifier: true);
        DrainInput();
        Assert.HasCount(0, rig.Moves);
        Assert.HasCount(0, rig.Releases);
        Assert.AreEqual(1, rig.CancelCount);
        Assert.IsFalse(rig.Bridge.IsListening);
    }

    [STATestMethod]
    public void ForbiddenModifierInCoalescedBurstCannotBeErasedByLaterValidMove()
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        rig.Platform.Emit(MouseMove, new(10, 20), timestamp: 1, forbiddenModifier: true);
        rig.Platform.Emit(MouseMove, new(30, 40), timestamp: 2);
        DrainInput();
        Assert.HasCount(0, rig.Moves, "The latest valid sample cannot erase an earlier invalid gesture transition.");
        Assert.HasCount(0, rig.Releases);
        Assert.AreEqual(1, rig.CancelCount);
        Assert.IsFalse(rig.Bridge.IsListening);
        Assert.AreEqual(2, rig.Platform.ReadCount);
        Assert.AreEqual(2, rig.Platform.NextCount);
    }

    [STATestMethod]
    public void ShiftReleaseInCoalescedPetGestureCannotBeErasedByLaterShiftMove()
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start(shift: true));
        rig.Platform.Emit(MouseMove, new(10, 20), timestamp: 1, shift: false);
        rig.Platform.Emit(MouseMove, new(30, 40), timestamp: 2, shift: true);
        DrainInput();
        Assert.HasCount(0, rig.Moves, "Reapplying Shift cannot revive the pet gesture after its mandatory Shift was released.");
        Assert.HasCount(0, rig.Releases);
        Assert.AreEqual(1, rig.CancelCount);
        Assert.IsFalse(rig.Bridge.IsListening);
        Assert.AreEqual(2, rig.Platform.ReadCount);
        Assert.AreEqual(2, rig.Platform.NextCount);
    }

    [STATestMethod]
    public void ReleaseCannotOverwriteLatestUnflushedMoveAndMustReachSinkAfterIt()
    {
        var ordered = new List<(int Message, Point Point)>();
        using var rig = new BridgeFixture(
            (point, _) => ordered.Add((MouseMove, point)),
            (point, _) => ordered.Add((LeftButtonUp, point)));
        Assert.IsTrue(rig.Bridge.Start());
        rig.Platform.Emit(MouseMove, new(10, 20), timestamp: 1);
        rig.Platform.Emit(LeftButtonUp, new(30, 40), timestamp: 2);
        DrainInput();
        CollectionAssert.AreEqual(new[] { (MouseMove, new Point(10, 20)), (LeftButtonUp, new Point(30, 40)) }, ordered,
            "A physical move remains part of the gesture even when release is queued before the Dispatcher drains.");
        Assert.IsTrue(rig.ReleaseObservedInactiveBridge);
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CoalescedExcursionAndReturnCannotEraseRealControllerDragOrPetCancellation(bool shift)
    {
        var controller = new LightInteractionController();
        Assert.IsTrue(controller.TryBegin(new(new DangoEntityId(7), 3), new(0, 0), 1000, shift, out var session));
        var bounds = new LogicalRect(-100, -100, 200, 200);
        var results = new List<LightInteractionResult>();
        using var rig = new BridgeFixture(
            (point, pressedShift) => results.Add(controller.Move(session, new(point.X, point.Y), 1100, pressedShift, bounds)),
            (point, pressedShift) => results.Add(controller.End(session, new(point.X, point.Y), 1120, pressedShift, bounds)),
            () => { controller.Cancel(session); results.Add(LightInteractionResult.Cancelled); });
        Assert.IsTrue(rig.Bridge.Start(shift));
        rig.Platform.Emit(MouseMove, shift ? new(0, 9) : new(40, 0), timestamp: 1, shift: shift);
        var returned = shift ? new Point(20, 0) : new Point(0, 0);
        rig.Platform.Emit(MouseMove, returned, timestamp: 2, shift: shift);
        rig.Platform.Emit(LeftButtonUp, returned, timestamp: 3, shift: shift);
        DrainInput();
        if (shift)
        {
            CollectionAssert.Contains(results, LightInteractionResult.Cancelled,
                "Returning to a valid horizontal stroke cannot erase its earlier vertical safety violation.");
            CollectionAssert.DoesNotContain(results, LightInteractionResult.Pet);
        }
        else
        {
            CollectionAssert.Contains(results, LightInteractionResult.BeginDrag,
                "A move past the real three-world threshold must retain drag eligibility when the cursor returns to its origin.");
            CollectionAssert.DoesNotContain(results, LightInteractionResult.Click);
        }
    }

    [STATestMethod]
    public void PetBoundaryFirstHorizontalExitCannotBeErasedByFartherValidStroke()
        => AssertPetBoundaryCancellationBeforeQualification([new(-1, 60), new(60, 60)]);

    [STATestMethod]
    public void PetBoundaryShortReverseVerticalExitCannotBeReorderedAfterLaterOppositeExtreme()
        => AssertPetBoundaryCancellationBeforeQualification([new(20, 51), new(100, 60), new(20, 70)]);

    [STATestMethod]
    public void PetBoundaryFirstExitMustCancelBeforeQualifyingStrokeWhenLaterExitReplacesCoordinateExtreme()
        => AssertPetBoundaryCancellationBeforeQualification([new(-1, 60), new(60, 60), new(-2, 60)]);

    [STATestMethod]
    public void PetGuardQualifiesTheSameClampedPointAsControllerInsteadOfRejectingRawOutsideTarget()
    {
        var down = new Point(20, 60);
        var bounds = new LogicalRect(0, 0, 160, 120);
        var guard = new DesktopPetPointerGestureGuard(new(0, 0, 160, 120), 8, new Rect(0, 58, 160, 4));
        var controller = new LightInteractionController();
        Assert.IsTrue(controller.TryBegin(new(new DangoEntityId(7), 3), new(down.X, down.Y), 1000, true, out var session));
        var results = new List<LightInteractionResult>();
        LogicalPoint ControllerPoint(Point raw)
        {
            // The fake driver applies its independently specified clamp, as the real driver does after the bridge.
            // Do not ask the guard to supply the expected controller coordinate.
            return new(Math.Clamp(raw.X, 0, 160), Math.Clamp(raw.Y, 58, 62));
        }
        using var rig = new BridgeFixture(
            (raw, shift) => results.Add(controller.Move(session, ControllerPoint(raw), 1100, shift, bounds)),
            (raw, shift) => results.Add(controller.End(session, ControllerPoint(raw), 1120, shift, bounds)),
            () => { controller.Cancel(session); results.Add(LightInteractionResult.Cancelled); });
        Assert.IsTrue(rig.Bridge.Start(shift: true, origin: down, guard: guard));
        rig.Platform.Emit(MouseMove, new(60, -500), timestamp: 1, shift: true);
        rig.Platform.Emit(LeftButtonUp, new(60, -500), timestamp: 2, shift: true);
        DrainInput();
        CollectionAssert.AreEqual(new[] { (new Point(60, -500), true) }, rig.Moves,
            "The bridge retains raw screen coordinates; the driver owns the shared clamp conversion.");
        CollectionAssert.Contains(results, LightInteractionResult.Pet,
            "Raw out-of-range input that the actual driver clamps to a valid stroke must not be falsely cancelled.");
        CollectionAssert.DoesNotContain(results, LightInteractionResult.Cancelled);
        Assert.AreEqual(0, rig.CancelCount);
    }

    [STATestMethod]
    public void PetGuardMeasuresVerticalSafetyFromOriginalDownWithoutClampingTheOrigin()
    {
        using var rig = new BridgeFixture();
        var guard = new DesktopPetPointerGestureGuard(new(0, 0, 160, 120), 8, new Rect(0, 70, 160, 40));
        Assert.IsTrue(rig.Bridge.Start(shift: true, origin: new(20, 60), guard: guard));
        rig.Platform.Emit(MouseMove, new(60, 60), timestamp: 1, shift: true);
        rig.Platform.Emit(LeftButtonUp, new(60, 60), timestamp: 2, shift: true);
        DrainInput();
        Assert.HasCount(0, rig.Moves, "Clamping the sampled point to Y=70 leaves a ten-unit violation from the original down Y=60.");
        Assert.HasCount(0, rig.Releases);
        Assert.AreEqual(1, rig.CancelCount, "Clamping the original down too would incorrectly hide this safety violation.");
        Assert.IsFalse(rig.Bridge.IsListening);
    }

    [STATestMethod]
    [DataRow(100u, 101u)]
    [DataRow(4294967294u, 1u)]
    public void AlreadyObservedNativeMessageRemainsDuplicateAfterNewerHookTimestampIncludingWrap(uint earlier, uint later)
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        rig.Platform.Emit(MouseMove, new(10, 20), timestamp: earlier);
        DrainInput();
        rig.Platform.Emit(MouseMove, new(30, 40), timestamp: later);
        Assert.IsTrue(rig.Bridge.MatchesNativeMessage(MouseMove, earlier),
            "An older physical WM must remain suppressed when a newer low-level event overtakes its window dispatch.");
        Assert.IsTrue(rig.Bridge.MatchesNativeMessage(MouseMove, later));
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(LeftButtonUp, earlier));
        rig.Bridge.Stop();
        Assert.IsTrue(rig.Bridge.Start());
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(MouseMove, earlier), "The duplicate watermark belongs only to the current gesture.");
    }

    [STATestMethod]
    public void MatchingNativeMessageKeepsObservedTypeWatermarkWithinCurrentGeneration()
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        rig.Platform.Emit(MouseMove, new(10, 20), timestamp: 100);
        Assert.IsTrue(rig.Bridge.MatchesNativeMessage(MouseMove, 100),
            "Coordinate changes from a moving HWND must not prevent timestamp duplicate recognition.");
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(MouseMove, 101));
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(LeftButtonUp, 100));
        DrainInput();
        Assert.IsTrue(rig.Bridge.MatchesNativeMessage(MouseMove, 100));
        rig.Platform.Emit(LeftButtonUp, new(30, 40), timestamp: 101);
        Assert.IsTrue(rig.Bridge.MatchesNativeMessage(LeftButtonUp, 101));
        Assert.IsTrue(rig.Bridge.MatchesNativeMessage(MouseMove, 100),
            "Observing a release cannot reopen an older physical move for duplicate window dispatch.");
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(MouseMove, 102));
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(LeftButtonUp, 102));
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(LeftButtonDown, 100));
        DrainInput();
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(LeftButtonUp, 101));
        Assert.IsTrue(rig.Bridge.Start());
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(MouseMove, 100));
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(LeftButtonUp, 101));
    }

    [STATestMethod]
    public void NativeMessagesWithoutObservedLowLevelEventRemainAvailableToWindowHandler()
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(MouseMove, 100));
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(LeftButtonUp, 101));
        Assert.AreEqual(0, rig.Platform.ReadCount,
            "A fabricated SendMessage-only path is not evidence that a low-level physical event was observed.");
        rig.Platform.Emit(MouseMove, new(10, 20), timestamp: 100);
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(MouseMove, 200));
        Assert.IsFalse(rig.Bridge.MatchesNativeMessage(LeftButtonUp, 201));
    }

    [STATestMethod]
    public void FailedUnhookRemainsInactiveDoesNotReadAndRetriesBeforeAnySecondInstallation()
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        rig.Platform.UninstallSucceeds = false;
        rig.Platform.Emit(MouseMove, new(10, 20), timestamp: 1);
        rig.Bridge.Stop();
        Assert.IsFalse(rig.Bridge.IsListening);
        Assert.IsTrue(rig.Bridge.IsHookReleasePending);
        Assert.IsFalse(rig.Bridge.Start());
        Assert.AreEqual(1, rig.Platform.InstallCount, "An unreleased callback cannot be replaced by a second global hook.");
        var reads = rig.Platform.ReadCount;
        rig.Platform.Emit(MouseMove, new(90, 90), timestamp: 2);
        rig.Platform.Emit(LeftButtonUp, new(90, 90), timestamp: 3);
        DrainInput();
        Assert.AreEqual(reads, rig.Platform.ReadCount);
        Assert.HasCount(0, rig.Moves);
        Assert.HasCount(0, rig.Releases);
        Assert.AreEqual(0, rig.CancelCount);
        var failedAttempts = rig.Platform.UninstallCount;
        rig.Platform.UninstallSucceeds = true;
        PumpFor(200);
        Assert.IsTrue(rig.Platform.UninstallCount > failedAttempts, "The owned retry timer must release the retained callback.");
        Assert.IsFalse(rig.Bridge.IsHookReleasePending);
        Assert.AreEqual(1, rig.Platform.InstallCount);
        Assert.IsTrue(rig.Bridge.Start());
        Assert.AreEqual(2, rig.Platform.InstallCount);
    }

    [STATestMethod]
    public void FailedUnhookInOldBridgePreventsFreshWindowBridgeInstallingAnotherHookUntilReleased()
    {
        using var retained = new BridgeFixture();
        using var replacement = new BridgeFixture();
        Assert.IsTrue(retained.Bridge.Start());
        retained.Platform.UninstallSucceeds = false;
        retained.Bridge.Stop();
        Assert.IsTrue(retained.Bridge.IsHookReleasePending);
        Assert.IsFalse(replacement.Bridge.Start(), "Replacing the Window cannot bypass an older bridge's retained global hook.");
        Assert.AreEqual(0, replacement.Platform.InstallCount);
        retained.Platform.Emit(MouseMove, new(90, 90), timestamp: 1);
        Assert.AreEqual(0, retained.Platform.ReadCount);
        retained.Platform.UninstallSucceeds = true;
        PumpFor(200);
        Assert.IsFalse(retained.Bridge.IsHookReleasePending);
        Assert.IsTrue(replacement.Bridge.Start());
        Assert.AreEqual(1, replacement.Platform.InstallCount);
    }

    [STATestMethod]
    public void DisposalWithFailedUnhookRetainsOnlySilentRetryUntilReleaseSucceeds()
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        rig.Platform.UninstallSucceeds = false;
        rig.Bridge.Dispose();
        Assert.IsTrue(rig.Bridge.IsHookReleasePending);
        Assert.IsFalse(rig.Bridge.IsListening);
        Assert.ThrowsExactly<ObjectDisposedException>(() => rig.Bridge.Start());
        rig.Platform.Emit(MouseMove, new(10, 20), timestamp: 1);
        rig.Platform.Emit(LeftButtonUp, new(10, 20), timestamp: 2);
        Assert.AreEqual(0, rig.Platform.ReadCount);
        var failedAttempts = rig.Platform.UninstallCount;
        rig.Platform.UninstallSucceeds = true;
        PumpFor(200);
        Assert.IsTrue(rig.Platform.UninstallCount > failedAttempts);
        Assert.IsFalse(rig.Bridge.IsHookReleasePending);
        Assert.AreEqual(1, rig.Platform.InstallCount);
        Assert.HasCount(0, rig.Moves);
        Assert.HasCount(0, rig.Releases);
    }

    [STATestMethod]
    public void ReadFailureDoesNotEscapeNativeChainAndCancelsOnDispatcher()
    {
        using var rig = new BridgeFixture();
        Assert.IsTrue(rig.Bridge.Start());
        rig.Platform.ThrowOnRead = true;
        Assert.AreEqual((nint)987, rig.Platform.Emit(MouseMove, new(10, 20), timestamp: 1));
        Assert.AreEqual(0, rig.CancelCount);
        DrainInput();
        Assert.AreEqual(1, rig.CancelCount);
        Assert.IsFalse(rig.Bridge.IsListening);
        Assert.HasCount(0, rig.Moves);
        Assert.HasCount(0, rig.Releases);
        Assert.AreEqual(1, rig.Platform.NextCount);
    }

    private static void AssertPetBoundaryCancellationBeforeQualification(IReadOnlyList<Point> moves)
    {
        var down = new Point(20, 60);
        var bounds = new LogicalRect(0, 0, 160, 120);
        var controller = new LightInteractionController();
        Assert.IsTrue(controller.TryBegin(new(new DangoEntityId(7), 3), new(down.X, down.Y), 1000, true, out var session));
        var results = new List<LightInteractionResult>();
        var dispatched = new List<string>();
        using var rig = new BridgeFixture(
            (point, shift) =>
            {
                var result = controller.Move(session, new(point.X, point.Y), 1100, shift, bounds);
                dispatched.Add($"Move({point.X},{point.Y})={result}");
                results.Add(result);
            },
            (point, shift) =>
            {
                var result = controller.End(session, new(point.X, point.Y), 1120, shift, bounds);
                dispatched.Add($"Up({point.X},{point.Y})={result}");
                results.Add(result);
            },
            () =>
            {
                controller.Cancel(session);
                dispatched.Add("Cancel");
                results.Add(LightInteractionResult.Cancelled);
            });
        Assert.IsTrue(rig.Bridge.Start(shift: true, origin: down,
            guard: new DesktopPetPointerGestureGuard(new(0, 0, 160, 120), 8)));
        uint timestamp = 1;
        foreach (var point in moves) rig.Platform.Emit(MouseMove, point, timestamp: timestamp++, shift: true);
        rig.Platform.Emit(LeftButtonUp, new(60, 60), timestamp: timestamp, shift: true);
        DrainInput();
        var evidence = $"Down=(20,60); Bounds=(0,0,160,120); " +
            $"PhysicalMoves={string.Join(" -> ", moves)}; Dispatched={string.Join(" -> ", dispatched)}";
        CollectionAssert.DoesNotContain(results, LightInteractionResult.Pet,
            $"The first invalid stroke must cancel before any later qualified Pet can be consumed. {evidence}");
        CollectionAssert.Contains(results, LightInteractionResult.Cancelled,
            $"Coalescing must preserve the first boundary violation instead of deleting it. {evidence}");
        Assert.AreEqual(LightInteractionResult.Cancelled, results.First(result => result != LightInteractionResult.None),
            $"Cancellation must be the first meaningful controller outcome. {evidence}");
    }

    private static void DrainInput()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ApplicationIdle);
        Dispatcher.PushFrame(frame);
    }

    private static void PumpFor(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    private sealed class BridgeFixture : IDisposable
    {
        internal readonly FakePlatform Platform = new();
        internal readonly List<(Point Point, bool Shift)> Moves = [];
        internal readonly List<(Point Point, bool Shift)> Releases = [];
        internal readonly DesktopPetPointerGestureBridge Bridge;
        internal bool OwnGestureActive = true, OwnCapture = true;
        internal int CancelCount;
        internal bool ReleaseObservedInactiveBridge;

        internal BridgeFixture(Action<Point, bool>? observeMove = null, Action<Point, bool>? observeRelease = null,
            Action? observeCancel = null)
        {
            Bridge = new(Dispatcher.CurrentDispatcher, () => OwnGestureActive && OwnCapture,
                (point, shift) => { Moves.Add((point, shift)); observeMove?.Invoke(point, shift); },
                (point, shift) =>
                {
                    ReleaseObservedInactiveBridge = !Bridge!.IsListening;
                    Releases.Add((point, shift));
                    observeRelease?.Invoke(point, shift);
                }, () => { CancelCount++; observeCancel?.Invoke(); }, Platform);
        }

        public void Dispose()
        {
            Platform.UninstallSucceeds = true;
            Bridge.Dispose();
            if (Bridge.IsHookReleasePending) PumpFor(200);
            DrainInput();
        }
    }

    private sealed class FakePlatform : IDesktopPetPointerGesturePlatform
    {
        private NativeMethods.LowLevelMouseProcedure? callback;
        private DesktopPetPointerSample current;
        internal int InstallCount, UninstallCount, ReadCount, NextCount;
        internal bool UninstallSucceeds = true, ThrowOnRead;

        public nint Install(NativeMethods.LowLevelMouseProcedure value)
        {
            callback = value;
            return (nint)(++InstallCount);
        }

        public bool Uninstall(nint hook) { UninstallCount++; return UninstallSucceeds; }
        public nint Next(nint hook, int code, nint message, nint data) { NextCount++; return 987; }

        public DesktopPetPointerSample Read(nint data)
        {
            ReadCount++;
            if (ThrowOnRead) throw new InvalidOperationException("Fabricated input read failure.");
            return current;
        }

        internal nint Emit(int message, Point point, uint timestamp, bool shift = false, bool forbiddenModifier = false, int code = 0)
        {
            current = new(point, shift, forbiddenModifier, timestamp);
            return (callback ?? throw new AssertFailedException("The fixture must install its fake callback before emitting."))(code, message, 1);
        }
    }
}
