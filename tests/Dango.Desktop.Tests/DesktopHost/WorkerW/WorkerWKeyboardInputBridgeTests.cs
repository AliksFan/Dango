using Dango.Core.Animation;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.WorkerW;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class WorkerWKeyboardInputBridgeTests
{
    [TestMethod]
    public void SettingsDefaultToDisabledArrowKeys()
    {
        var settings = KeyboardControlSettings.CreateDefault();
        Assert.IsFalse(settings.IsEnabled);
        Assert.AreEqual(KeyboardControlScheme.ArrowKeys, settings.Scheme);
    }

    [TestMethod]
    public void SelectedSchemeMapsOnlyItsFourKeys()
    {
        Assert.IsTrue(KeyboardControlKeyPolicy.TryMap(0x25, KeyboardControlScheme.ArrowKeys, out var left));
        Assert.AreEqual(DirectionalControlKey.Left, left);
        Assert.IsFalse(KeyboardControlKeyPolicy.TryMap(0x41, KeyboardControlScheme.ArrowKeys, out _));
        Assert.IsTrue(KeyboardControlKeyPolicy.TryMap(0x57, KeyboardControlScheme.Wasd, out var jump));
        Assert.AreEqual(DirectionalControlKey.Jump, jump);
        Assert.IsFalse(KeyboardControlKeyPolicy.TryMap(0x26, KeyboardControlScheme.Wasd, out _));
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
    public void DesktopFocusRequiresKnownDesktopHandleOrDescendant()
    {
        var target = new WorkerWDesktopTarget((nint)10, (nint)11, (nint)12, (nint)13, true);
        Assert.IsTrue(WorkerWDesktopFocusPolicy.IsAllowed((nint)10, target, (_, _) => false));
        Assert.IsTrue(WorkerWDesktopFocusPolicy.IsAllowed((nint)99, target, (parent, child) => parent == (nint)13 && child == (nint)99));
        Assert.IsFalse(WorkerWDesktopFocusPolicy.IsAllowed((nint)20, target, (_, _) => false));
    }
}
