using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.DesktopHost.WorkerW;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class WallpaperWheelInputBridgeTests
{
    private static WallpaperWheelInputContext Context => new(
        new WorkerWDesktopTarget(10, 11, 12, 13, true), new LogicalRect(-1920, 0, 1920, 1080), 1, true, false);

    [STATestMethod]
    [DataRow(true, false, false, true, false)]
    [DataRow(true, true, true, true, false)]
    [DataRow(true, true, false, true, true)]
    [DataRow(false, true, false, true, false)]
    public void IneligibleEventCannotBecomeEligibleWhenEnvironmentRecoversBeforeFlush(
        bool known, bool foreground, bool fullscreen, bool desktopPointer, bool icon)
    {
        var environment = new WallpaperWheelInputEnvironment(known, foreground, fullscreen, desktopPointer, icon);
        var platform = new FakePlatform { Inspection = (_, _) => environment };
        var applied = new List<int>();
        var cleared = 0;
        using var bridge = Create(platform, () => Context, applied, () => cleared++);
        bridge.Start();
        Assert.AreEqual((nint)987, platform.Emit(120));
        environment = new(true, true, false, true, false);
        bridge.FlushPendingInput();
        Assert.HasCount(0, applied,
            "An event from an application, fullscreen, icon or unknown environment must never be accepted after the environment recovers.");
        Assert.AreEqual(1, cleared);
        Assert.AreEqual(1, platform.NextCount);
    }

    [STATestMethod]
    public void EventInspectionStopStartDiscardsOldEventWithoutPoisoningTheFreshGesture()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        var cleared = 0;
        using var bridge = Create(platform, () => Context, applied, () => cleared++);
        bridge.Start();
        platform.Inspection = (_, _) =>
        {
            bridge.Stop(); bridge.Start();
            return new(true, true, false, true, false);
        };
        platform.Emit(60);
        platform.Inspection = (_, _) => new(true, true, false, true, false);
        platform.Emit(120);
        bridge.FlushPendingInput();
        CollectionAssert.AreEqual(new[] { 120 }, applied);
        Assert.AreEqual(1, cleared, "The old event cannot clear or reject the new hook epoch.");
        Assert.AreEqual(2, platform.NextCount);
        Assert.AreEqual(1, platform.UninstallCount);
    }

    [STATestMethod]
    public void HookReadReentryCapturesEpochBeforeReadingAndPreservesTheNewQualifiedEvent()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        using var bridge = Create(platform, () => Context, applied);
        bridge.Start();
        platform.BeforeRead = () =>
        {
            platform.BeforeRead = null;
            bridge.Stop(); bridge.Start();
            platform.Emit(120);
        };
        platform.Emit(60);
        bridge.FlushPendingInput();
        CollectionAssert.AreEqual(new[] { 120 }, applied);
        Assert.AreEqual(1, platform.EventInspectionCount);
        Assert.AreEqual(2, platform.NextCount);
    }

    [STATestMethod]
    public void NestedReverseWheelDuringInspectionRejectsBothWithoutRecursiveQueriesAndKeepsHookChain()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        var cleared = 0;
        using var bridge = Create(platform, () => Context, applied, () => cleared++);
        bridge.Start();
        platform.Inspection = (_, _) =>
        {
            platform.Emit(-120);
            return new(true, true, false, true, false);
        };
        Assert.AreEqual((nint)987, platform.Emit(120));
        bridge.FlushPendingInput();
        Assert.HasCount(0, applied);
        Assert.AreEqual(1, platform.EventInspectionCount);
        Assert.AreEqual(0, platform.InspectionCount);
        Assert.AreEqual(2, platform.NextCount);
        Assert.AreEqual(1, cleared);
    }

    [STATestMethod]
    public void NestedFlushDuringInspectionCannotApplyQueuedInputInsideTheHook()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        using var bridge = Create(platform, () => Context, applied);
        bridge.Start();
        platform.Emit(60);
        var appliedInsideInspection = false;
        platform.Inspection = (_, _) =>
        {
            bridge.FlushPendingInput();
            appliedInsideInspection |= applied.Count != 0;
            return new(true, true, false, true, false);
        };
        platform.Emit(60);
        Assert.HasCount(0, applied);
        bridge.FlushPendingInput();
        Assert.IsFalse(appliedInsideInspection);
        CollectionAssert.AreEqual(new[] { 120 }, applied);
        Assert.AreEqual(2, platform.EventInspectionCount);
        Assert.AreEqual(1, platform.InspectionCount);
    }

    [STATestMethod]
    public void CommandInspectionStopStartCannotClearTheFreshEpochAfterOldBatchReturns()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        var cleared = 0;
        using var bridge = Create(platform, () => Context, applied, () => cleared++);
        bridge.Start();
        platform.Emit(60);
        platform.Inspection = (_, _) =>
        {
            bridge.Stop(); bridge.Start();
            return new(true, true, false, true, false);
        };
        bridge.FlushPendingInput();
        platform.Inspection = (_, _) => new(true, true, false, true, false);
        platform.Emit(120);
        bridge.FlushPendingInput();
        CollectionAssert.AreEqual(new[] { 120 }, applied);
        Assert.AreEqual(1, cleared);
    }

    [STATestMethod]
    public void HookQualifiesWithoutApplyingAndPreservesPartialRawDeltasAtUiBoundary()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        using var bridge = Create(platform, () => Context, applied);
        bridge.Start();
        Assert.AreEqual((nint)987, platform.Emit(60));
        Assert.AreEqual(1, platform.EventInspectionCount, "Event qualification must happen before the candidate is queued.");
        Assert.AreEqual(0, platform.InspectionCount, "Application still waits for the Dispatcher command boundary.");
        Assert.HasCount(0, applied);
        bridge.FlushPendingInput();
        platform.Emit(-60);
        bridge.FlushPendingInput();
        CollectionAssert.AreEqual(new[] { 60, -60 }, applied);
        Assert.AreEqual(2, platform.NextCount);
    }

    [STATestMethod]
    public void SamePointBurstCoalescesToOneInspectionAndRawSum()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        using var bridge = Create(platform, () => Context, applied);
        bridge.Start();
        for (var i = 0; i < 100; i++) platform.Emit(60);
        Assert.AreEqual(100, platform.EventInspectionCount, "Every event must independently qualify before coalescing.");
        Assert.AreEqual(0, platform.InspectionCount);
        bridge.FlushPendingInput();
        CollectionAssert.AreEqual(new[] { 6000 }, applied);
        Assert.AreEqual(1, platform.InspectionCount);
    }

    [STATestMethod]
    public void DirectionReversalPreservesCommandOrderAtClampedCameraBoundary()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        using var bridge = Create(platform, () => Context, applied);
        bridge.Start();
        platform.Emit(120);
        platform.Emit(-120);
        bridge.FlushPendingInput();
        CollectionAssert.AreEqual(new[] { 120, -120 }, applied,
            "A positive step at the camera boundary followed by a negative step must not cancel before the accumulator sees it.");
    }

    [STATestMethod]
    public void DispatcherTimerAppliesBurstWithoutOneDispatcherOperationPerEvent()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        using var bridge = Create(platform, () => Context, applied);
        bridge.Start();
        for (var i = 0; i < 100; i++) platform.Emit(60);
        Assert.AreEqual(0, platform.InspectionCount);
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timeout.Tick += (_, _) => { timeout.Stop(); frame.Continue = false; };
        timeout.Start();
        Dispatcher.PushFrame(frame);
        CollectionAssert.AreEqual(new[] { 6000 }, applied);
        Assert.IsTrue(platform.InspectionCount is >= 1 and <= 3,
            "A burst shares one command inspection, followed only by the short-lived 50ms eligibility checks.");
    }

    [STATestMethod]
    public void IneligibleIconPointCannotBeMergedIntoLaterBackgroundPoint()
    {
        var platform = new FakePlatform { Inspection = (_, point) => new(true, true, false, true, point.X == -100) };
        var applied = new List<int>();
        var cleared = 0;
        using var bridge = Create(platform, () => Context, applied, () => cleared++);
        bridge.Start();
        platform.Emit(60, -100);
        platform.Emit(60, -200);
        bridge.FlushPendingInput();
        Assert.HasCount(0, applied);
        Assert.AreEqual(1, cleared);
    }

    [STATestMethod]
    public void QueueOverflowDropsEntireBurstAndClearsAccumulator()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        var cleared = 0;
        using var bridge = Create(platform, () => Context, applied, () => cleared++);
        bridge.Start();
        for (var i = 0; i <= WallpaperWheelInputBridge.MaximumPendingPoints; i++) platform.Emit(120, -100 - i);
        bridge.FlushPendingInput();
        Assert.HasCount(0, applied);
        Assert.AreEqual(0, platform.InspectionCount);
        Assert.AreEqual(1, cleared);
    }

    [STATestMethod]
    public void GenerationChangesAndStopStartInvalidateQueuedCandidates()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        var current = Context;
        var cleared = 0;
        using var bridge = Create(platform, () => current, applied, () => cleared++);
        bridge.Start();
        platform.Emit(60);
        current = current with { Generation = 2 };
        bridge.FlushPendingInput();
        Assert.AreEqual(1, cleared);
        platform.Emit(60);
        bridge.Stop();
        bridge.Start();
        bridge.FlushPendingInput();
        Assert.HasCount(0, applied);
        Assert.AreEqual(1, platform.UninstallCount);
        platform.Emit(120);
        bridge.FlushPendingInput();
        CollectionAssert.AreEqual(new[] { 120 }, applied);
    }

    [STATestMethod]
    public void TargetAndHostAvailabilityAreRecheckedBeforeInspection()
    {
        WallpaperWheelInputContext?[] changes = [null, Context with { IsReady = false }, Context with { IsBlocked = true },
            Context with { Target = Context.Target with { WallpaperLayerHandle = 99 } },
            Context with { ScreenBounds = new LogicalRect(-1920, 0, 1280, 1080) }];
        foreach (var changed in changes)
        {
            var platform = new FakePlatform();
            var applied = new List<int>();
            WallpaperWheelInputContext? current = Context;
            var cleared = 0;
            using var bridge = Create(platform, () => current, applied, () => cleared++);
            bridge.Start();
            platform.Emit(120);
            current = changed;
            bridge.FlushPendingInput();
            Assert.HasCount(0, applied);
            Assert.AreEqual(0, platform.InspectionCount);
            Assert.AreEqual(1, cleared);
        }
    }

    [STATestMethod]
    public void FocusFullscreenPointerAndUnknownQueriesRejectAtCommandBoundary()
    {
        WallpaperWheelInputEnvironment[] rejected = [default, new(true, false, false, true, false),
            new(true, true, true, true, false), new(true, true, false, false, false), new(true, true, false, true, true)];
        foreach (var environment in rejected)
        {
            var platform = new FakePlatform();
            var applied = new List<int>();
            var cleared = 0;
            using var bridge = Create(platform, () => Context, applied, () => cleared++);
            bridge.Start();
            platform.Emit(60);
            platform.Inspection = (_, _) => environment;
            bridge.FlushPendingInput();
            Assert.HasCount(0, applied);
            Assert.AreEqual(1, cleared);
        }
    }

    [STATestMethod]
    public void ContextChangedDuringNativeInspectionStillRejects()
    {
        var platform = new FakePlatform();
        var current = Context;
        var applied = new List<int>();
        var cleared = 0;
        using var bridge = Create(platform, () => current, applied, () => cleared++);
        platform.Inspection = (_, _) =>
        {
            current = current with { Generation = 2 };
            return new(true, true, false, true, false);
        };
        bridge.Start();
        platform.Emit(120);
        bridge.FlushPendingInput();
        Assert.HasCount(0, applied);
        Assert.AreEqual(1, cleared);
    }

    [STATestMethod]
    public void StopStartDuringInspectionInvalidatesCurrentCommandEvenWithSameContext()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        using var bridge = Create(platform, () => Context, applied);
        bridge.Start();
        platform.Inspection = (_, _) =>
        {
            bridge.Stop();
            bridge.Start();
            return new(true, true, false, true, false);
        };
        platform.Emit(120);
        bridge.FlushPendingInput();
        Assert.HasCount(0, applied);
        Assert.AreEqual(1, platform.UninstallCount);
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StopOrDisposeDuringInspectionInvalidatesCurrentCommand(bool dispose)
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        using var bridge = Create(platform, () => Context, applied);
        bridge.Start();
        platform.Inspection = (_, _) =>
        {
            if (dispose) bridge.Dispose(); else bridge.Stop();
            return new(true, true, false, true, false);
        };
        platform.Emit(120);
        bridge.FlushPendingInput();
        Assert.HasCount(0, applied);
        Assert.AreEqual(1, platform.UninstallCount);
    }

    [STATestMethod]
    public void NativeInspectionExceptionClearsWithoutApplying()
    {
        var platform = new FakePlatform { Inspection = (_, _) => throw new InvalidOperationException() };
        var applied = new List<int>();
        var cleared = 0;
        using var bridge = Create(platform, () => Context, applied, () => cleared++);
        bridge.Start();
        platform.Emit(120);
        bridge.FlushPendingInput();
        Assert.HasCount(0, applied);
        Assert.AreEqual(1, cleared);
    }

    [STATestMethod]
    public void HookNeverConsumesNegativeCodeNonwheelInjectedRejectedOrExceptionEvents()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        var cleared = 0;
        using var bridge = Create(platform, () => Context, applied, () => cleared++);
        bridge.Start();
        Assert.AreEqual((nint)987, platform.Emit(120, code: -1));
        Assert.AreEqual((nint)987, platform.Emit(120, message: WallpaperWheelInputBridge.WmMouseHorizontalWheel));
        Assert.AreEqual((nint)987, platform.Emit(120, flags: 1));
        Assert.AreEqual((nint)987, platform.Emit(120, x: 0));
        platform.ThrowOnRead = true;
        Assert.AreEqual((nint)987, platform.Emit(120));
        bridge.FlushPendingInput();
        Assert.AreEqual(5, platform.NextCount);
        Assert.HasCount(0, applied);
        Assert.AreEqual(1, cleared);
    }

    [STATestMethod]
    public void DisposeUnhooksClearsAndLeavesOldCallbackInert()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        var cleared = 0;
        var bridge = Create(platform, () => Context, applied, () => cleared++);
        bridge.Start();
        platform.Emit(120);
        bridge.Dispose();
        bridge.Dispose();
        Assert.AreEqual((nint)987, platform.Emit(120));
        bridge.FlushPendingInput();
        Assert.HasCount(0, applied);
        Assert.AreEqual(1, platform.UninstallCount);
        Assert.AreEqual(1, cleared);
        Assert.ThrowsExactly<ObjectDisposedException>(() => bridge.Start());
    }

    [STATestMethod]
    public void StopClearCallbackCannotRestartOrRecursivelyStopHook()
    {
        var platform = new FakePlatform();
        WallpaperWheelInputBridge? bridge = null;
        var cleared = 0;
        bridge = Create(platform, () => Context, [], () =>
        {
            cleared++;
            if (cleared == 1)
            {
                bridge!.Start();
                bridge.Stop();
            }
        });
        bridge.Start();
        bridge.Stop();
        Assert.AreEqual(1, platform.InstallCount);
        Assert.AreEqual(1, platform.UninstallCount);
        Assert.AreEqual(1, cleared);
        // Do not call Start from the final disposal callback; a disposed bridge correctly rejects that call.
        bridge.Dispose();
    }

    [STATestMethod]
    public void DisposeMarksTerminalStateBeforeClearCallbackAndAllowsRecursiveDispose()
    {
        var platform = new FakePlatform();
        WallpaperWheelInputBridge? bridge = null;
        var cleared = 0;
        bridge = Create(platform, () => Context, [], () =>
        {
            cleared++;
            bridge!.Dispose();
            Assert.ThrowsExactly<ObjectDisposedException>(() => bridge.Start());
        });
        bridge.Start();
        bridge.Dispose();
        Assert.AreEqual(1, platform.InstallCount);
        Assert.AreEqual(1, platform.UninstallCount);
        Assert.AreEqual(1, cleared);
    }

    [STATestMethod]
    public void FailedHookInstallationClearsExistingAccumulator()
    {
        var platform = new FakePlatform { HookHandle = 0 };
        var cleared = 0;
        using var bridge = Create(platform, () => Context, [], () => cleared++);
        bridge.Start();
        Assert.AreEqual(1, cleared);
    }

    [STATestMethod]
    public void FailedUnhookLeavesObserverInertAndPreventsDuplicateInstallationUntilRetrySucceeds()
    {
        var platform = new FakePlatform { UninstallSucceeds = false };
        var applied = new List<int>();
        using var bridge = Create(platform, () => Context, applied);
        bridge.Start();
        platform.Emit(120);
        bridge.Stop();
        Assert.IsTrue(bridge.IsHookReleasePending);
        Assert.AreEqual((nint)987, platform.Emit(120));
        bridge.Start();
        Assert.AreEqual(1, platform.InstallCount);
        bridge.FlushPendingInput();
        Assert.HasCount(0, applied);
        platform.UninstallSucceeds = true;
        bridge.Start();
        Assert.IsFalse(bridge.IsHookReleasePending);
        Assert.AreEqual(2, platform.InstallCount);
        platform.Emit(120);
        bridge.FlushPendingInput();
        CollectionAssert.AreEqual(new[] { 120 }, applied);
    }

    [STATestMethod]
    public void ModifierAtHookOrCommandBoundaryRejectsAndClearsFractionalInput()
    {
        var platform = new FakePlatform();
        var applied = new List<int>();
        var cleared = 0;
        using var bridge = Create(platform, () => Context, applied, () => cleared++);
        bridge.Start();
        platform.Emit(60, modifier: true);
        bridge.FlushPendingInput();
        platform.Emit(60);
        platform.Inspection = (_, _) => new(true, true, false, true, false, true);
        bridge.FlushPendingInput();
        Assert.HasCount(0, applied);
        Assert.AreEqual(2, cleared);
    }

    [STATestMethod]
    [DataRow(false, false, true)]
    [DataRow(true, false, false)]
    [DataRow(true, true, true)]
    public void RecentFractionIsClearedDuringFocusPointerOrFullscreenLossWithoutAnotherWheel(
        bool foreground, bool fullscreen, bool pointerAllowed)
    {
        var clock = new ManualTimeProvider();
        var platform = new FakePlatform();
        var fraction = 0;
        var steps = 0;
        var clears = 0;
        using var bridge = new WallpaperWheelInputBridge(Dispatcher.CurrentDispatcher, () => Context,
            delta => { fraction += delta; if (fraction >= 120) { steps++; fraction -= 120; } },
            () => { fraction = 0; clears++; }, platform, clock);
        bridge.Start();
        platform.Emit(60);
        bridge.FlushPendingInput();
        Assert.AreEqual(60, fraction);
        platform.Inspection = (_, _) => new(true, foreground, fullscreen, pointerAllowed, false);
        clock.Advance(50);
        bridge.FlushPendingInput();
        Assert.AreEqual(0, fraction, "Eligibility loss must clear the parent fraction even without a second wheel event.");
        Assert.AreEqual(1, clears);
        platform.Inspection = (_, _) => new(true, true, false, true, false);
        clock.Advance(50);
        platform.Emit(60);
        bridge.FlushPendingInput();
        Assert.AreEqual(60, fraction);
        Assert.AreEqual(0, steps, "The restored host must not inherit the fraction captured before eligibility loss.");
    }

    [STATestMethod]
    public void RecentChecksStopAfter250MillisecondsAndClearParentFraction()
    {
        var clock = new ManualTimeProvider();
        var platform = new FakePlatform();
        var cleared = 0;
        using var bridge = new WallpaperWheelInputBridge(Dispatcher.CurrentDispatcher, () => Context,
            _ => { }, () => cleared++, platform, clock);
        bridge.Start();
        platform.Emit(60);
        bridge.FlushPendingInput();
        for (var i = 0; i < 4; i++) { clock.Advance(50); bridge.FlushPendingInput(); }
        Assert.AreEqual(5, platform.InspectionCount);
        clock.Advance(50);
        bridge.FlushPendingInput();
        Assert.AreEqual(1, cleared);
        clock.Advance(1000);
        bridge.FlushPendingInput();
        Assert.AreEqual(5, platform.InspectionCount, "An idle bridge must not continue surveying the desktop.");
    }

    [STATestMethod]
    public void RecentGenerationLossClearsFractionAndInvalidatesPendingCandidates()
    {
        var clock = new ManualTimeProvider();
        var platform = new FakePlatform();
        var current = Context;
        var applied = new List<int>();
        var cleared = 0;
        using var bridge = new WallpaperWheelInputBridge(Dispatcher.CurrentDispatcher, () => current,
            applied.Add, () => cleared++, platform, clock);
        bridge.Start();
        platform.Emit(60);
        bridge.FlushPendingInput();
        current = current with { Generation = 2 };
        clock.Advance(50);
        bridge.FlushPendingInput();
        Assert.AreEqual(1, cleared);
        Assert.AreEqual(1, platform.InspectionCount);
        platform.Emit(60);
        bridge.FlushPendingInput();
        CollectionAssert.AreEqual(new[] { 60, 60 }, applied);
    }

    [TestMethod]
    public void DispatcherShutdownUnhooksAndInvalidatesPendingAndFutureCallbacks()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var platform = new FakePlatform();
                var applied = new List<int>();
                var cleared = 0;
                using var bridge = Create(platform, () => Context, applied, () => cleared++);
                bridge.Start();
                platform.Emit(120);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
                Assert.AreEqual((nint)987, platform.Emit(120));
                Assert.HasCount(0, applied);
                Assert.AreEqual(1, platform.UninstallCount);
                Assert.AreEqual(1, cleared);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) throw failure;
    }

    private static WallpaperWheelInputBridge Create(FakePlatform platform, Func<WallpaperWheelInputContext?> context,
        List<int> applied, Action? clear = null) =>
        new(Dispatcher.CurrentDispatcher, context, applied.Add, clear ?? (() => { }), platform);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        internal void Advance(int milliseconds) => timestamp += TimeSpan.FromMilliseconds(milliseconds).Ticks;
    }

    private sealed class FakePlatform : IWallpaperWheelInputPlatform
    {
        private NativeMethods.LowLevelMouseProcedure? procedure;
        private WallpaperWheelHookData data;
        internal int NextCount { get; private set; }
        internal int InspectionCount { get; private set; }
        internal int EventInspectionCount { get; private set; }
        internal int InstallCount { get; private set; }
        internal int UninstallCount { get; private set; }
        internal nint HookHandle { get; init; } = 42;
        internal bool ThrowOnRead { get; set; }
        internal Action? BeforeRead { get; set; }
        internal bool UninstallSucceeds { get; set; } = true;
        internal Func<WallpaperWheelInputContext, WallpaperWheelPoint, WallpaperWheelInputEnvironment> Inspection { get; set; } =
            (_, _) => new(true, true, false, true, false);

        public nint Install(NativeMethods.LowLevelMouseProcedure value) { procedure = value; InstallCount++; return HookHandle; }
        public bool Uninstall(nint hook) { UninstallCount++; return UninstallSucceeds; }
        public nint CallNext(nint hook, int code, nint wordParameter, nint longParameter) { NextCount++; return 987; }
        public WallpaperWheelHookData Read(nint longParameter)
        {
            BeforeRead?.Invoke();
            return ThrowOnRead ? throw new InvalidOperationException() : data;
        }
        public WallpaperWheelInputEnvironment Inspect(WallpaperWheelInputContext context, WallpaperWheelPoint point)
        {
            InspectionCount++;
            return Inspection(context, point);
        }
        public WallpaperWheelInputEnvironment InspectAtEvent(WallpaperWheelInputContext context, WallpaperWheelPoint point)
        {
            EventInspectionCount++;
            return Inspection(context, point);
        }
        internal nint Emit(int delta, int x = -100, int code = 0,
            int message = WallpaperWheelInputBridge.WmMouseWheel, uint flags = 0, bool modifier = false)
        {
            data = new WallpaperWheelHookData(new WallpaperWheelPoint(x, 100), unchecked((uint)(ushort)(short)delta << 16), flags, modifier);
            return procedure!(code, message, 123);
        }
    }
}
