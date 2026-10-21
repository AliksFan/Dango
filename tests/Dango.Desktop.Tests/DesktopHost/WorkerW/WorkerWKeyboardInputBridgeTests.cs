using Dango.Core.Animation;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.WorkerW;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class WorkerWKeyboardInputBridgeTests
{
    [TestMethod]
    public void SettingsDefaultToDisabledApprovedControls()
    {
        var settings = KeyboardControlSettings.CreateDefault();
        Assert.IsFalse(settings.IsEnabled);
        Assert.AreEqual(KeyboardControlScheme.Wasd, settings.Scheme);
        Assert.ThrowsExactly<InvalidDataException>(() => KeyboardControlSettingsValidator.Validate(
            new KeyboardControlSettings(true, KeyboardControlScheme.ArrowKeys)));
    }

    [TestMethod]
    public void ApprovedMovementKeysMapWToStretchAndIgnoreArrows()
    {
        Assert.IsTrue(KeyboardControlKeyPolicy.TryMap(0x41, KeyboardControlScheme.Wasd, out var left));
        Assert.AreEqual(DirectionalControlKey.Left, left);
        Assert.IsTrue(KeyboardControlKeyPolicy.TryMap(0x44, KeyboardControlScheme.Wasd, out var right));
        Assert.AreEqual(DirectionalControlKey.Right, right);
        Assert.IsTrue(KeyboardControlKeyPolicy.TryMap(0x53, KeyboardControlScheme.Wasd, out var squash));
        Assert.AreEqual(DirectionalControlKey.Squash, squash);
        Assert.IsTrue(KeyboardControlKeyPolicy.TryMap(0x20, KeyboardControlScheme.Wasd, out var jump));
        Assert.AreEqual(DirectionalControlKey.Jump, jump);
        Assert.IsTrue(KeyboardControlKeyPolicy.TryMap(0x57, KeyboardControlScheme.Wasd, out var stretch));
        Assert.AreEqual(DirectionalControlKey.Stretch, stretch);
        foreach (var arrow in new[] { 0x25, 0x26, 0x27, 0x28 })
            Assert.IsFalse(KeyboardControlKeyPolicy.TryMap(arrow, KeyboardControlScheme.Wasd, out _));
        Assert.IsFalse(KeyboardControlKeyPolicy.TryMap(0x25, KeyboardControlScheme.ArrowKeys, out _),
            "A legacy preference must not reactivate arrow-key controls.");
    }

    [TestMethod]
    public void HeldStateCoalescesRepeatAndClearsAtomically()
    {
        var state = new DirectionalControlHeldState();
        state.Set(DirectionalControlKey.Jump, true);
        state.Set(DirectionalControlKey.Jump, true);
        Assert.AreEqual(new DirectionalControlIntent(false, false, true, false), state.Snapshot);
        state.Clear();
        Assert.AreEqual(DirectionalControlIntent.None, state.Snapshot);
    }

    [TestMethod]
    public void VerticalKeysCancelAsAPairAndReleaseWithoutLosingHorizontalInput()
    {
        var state = new DirectionalControlHeldState();
        state.Set(DirectionalControlKey.Left, true);
        state.Set(DirectionalControlKey.Stretch, true);
        state.Set(DirectionalControlKey.Stretch, true);
        Assert.IsTrue(state.Snapshot.Stretch);
        Assert.IsFalse(state.Snapshot.Jump);
        Assert.AreEqual(1, state.Snapshot.VerticalDeformationAxis);
        state.Set(DirectionalControlKey.Squash, true);
        Assert.AreEqual(0, state.Snapshot.VerticalDeformationAxis);
        Assert.IsTrue(state.Snapshot.Left);
        state.Set(DirectionalControlKey.Stretch, false);
        Assert.AreEqual(-1, state.Snapshot.VerticalDeformationAxis);
        state.Set(DirectionalControlKey.Squash, false);
        Assert.AreEqual(0, state.Snapshot.VerticalDeformationAxis);
        Assert.IsTrue(state.Snapshot.Left);
        state.Clear();
        Assert.AreEqual(DirectionalControlIntent.None, state.Snapshot);
    }

    [TestMethod]
    public void StretchCancellationSuppressesHeldRepeatUntilPhysicalRelease()
    {
        var state = new DirectionalControlHeldState();
        state.Set(DirectionalControlKey.Stretch, true);
        state.ClearUntilRelease();
        state.Set(DirectionalControlKey.Stretch, true);
        Assert.AreEqual(DirectionalControlIntent.None, state.Snapshot);
        state.Set(DirectionalControlKey.Stretch, false);
        state.Set(DirectionalControlKey.Stretch, true);
        Assert.AreEqual(1, state.Snapshot.VerticalDeformationAxis);
        state.Clear();
        state.SuppressUntilRelease(DirectionalControlKey.Stretch);
        state.Set(DirectionalControlKey.Stretch, true);
        Assert.AreEqual(DirectionalControlIntent.None, state.Snapshot);
        state.Set(DirectionalControlKey.Stretch, false);
        state.Set(DirectionalControlKey.Stretch, true);
        Assert.AreEqual(1, state.Snapshot.VerticalDeformationAxis);
    }

    [TestMethod]
    public void WallpaperNeverCapturesExplorerSpaceBeforeRoomGameplayIsActive()
    {
        Assert.IsFalse(WorkerWKeyboardCapturePolicy.ShouldActivate(
            settingsEnabled: true, hostReady: true, interactionEnabled: true,
            roomGameplayInputActive: false, explorerDesktopForeground: true));
        Assert.IsFalse(WorkerWKeyboardCapturePolicy.ShouldActivate(
            settingsEnabled: true, hostReady: true, interactionEnabled: true,
            roomGameplayInputActive: true, explorerDesktopForeground: false));
        Assert.IsTrue(WorkerWKeyboardCapturePolicy.ShouldActivate(
            settingsEnabled: true, hostReady: true, interactionEnabled: true,
            roomGameplayInputActive: true, explorerDesktopForeground: true));
    }

    [TestMethod]
    public void EnabledPreferenceDoesNotInstallWallpaperHookWithoutRoomGameplay()
    {
        var target = new WorkerWDesktopTarget((nint)10, (nint)11, 0, 0, false);
        using var bridge = new WorkerWKeyboardInputBridge(target);
        bridge.ApplySettings(new KeyboardControlSettings(true, KeyboardControlScheme.Wasd));

        bridge.Refresh(hostReady: true, interactionEnabled: true, roomGameplayInputActive: false);

        Assert.AreEqual("Inactive", bridge.State);
        Assert.AreEqual(DirectionalControlIntent.None, bridge.CurrentIntent);
    }

    [TestMethod]
    public void DesktopFocusRequiresKnownDesktopHandleOrDescendant()
    {
        var target = new WorkerWDesktopTarget((nint)10, (nint)11, (nint)12, (nint)13, true);
        Assert.IsTrue(WorkerWDesktopFocusPolicy.IsAllowed((nint)10, target, (_, _) => false));
        Assert.IsTrue(WorkerWDesktopFocusPolicy.IsAllowed((nint)99, target, (parent, child) => parent == (nint)13 && child == (nint)99));
        Assert.IsFalse(WorkerWDesktopFocusPolicy.IsAllowed((nint)20, target, (_, _) => false));
    }
}
