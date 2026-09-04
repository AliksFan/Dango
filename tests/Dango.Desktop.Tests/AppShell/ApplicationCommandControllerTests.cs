using Dango.Desktop.AppShell;
using Dango.Desktop.DesktopHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ApplicationCommandControllerTests
{
    [TestMethod]
    public void VisibilityIsAppliedOnlyAfterTheHostConfirmsIt()
    {
        var host = new FakeDesktopHost(isVisible: true, isInteractionEnabled: true);
        var controller = new ApplicationCommandController(host);

        var result = controller.SetVisibility(isVisible: false);

        Assert.AreEqual(CommandApplicationState.Applied, result.State);
        Assert.IsFalse(result.Snapshot.IsVisible);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void UnconfirmedCommandReturnsFailureAndKeepsObservedState()
    {
        var host = new FakeDesktopHost(isVisible: false, isInteractionEnabled: true)
        {
            IgnoreVisibilityRequests = true,
        };
        var controller = new ApplicationCommandController(host);

        var result = controller.SetVisibility(isVisible: true);

        Assert.AreEqual(CommandApplicationState.Failed, result.State);
        Assert.IsFalse(result.Snapshot.IsVisible);
        StringAssert.Contains(result.Error, "未确认");
    }

    [TestMethod]
    public void HostExceptionIsReportedWithoutInventingAStateChange()
    {
        var host = new FakeDesktopHost(isVisible: true, isInteractionEnabled: true)
        {
            ThrowOnInteractionRequest = true,
        };
        var controller = new ApplicationCommandController(host);

        var result = controller.SetInteractionEnabled(isEnabled: false);

        Assert.AreEqual(CommandApplicationState.Failed, result.State);
        Assert.IsTrue(result.Snapshot.IsInteractionEnabled);
        StringAssert.Contains(result.Error, "无法应用");
    }

    [TestMethod]
    public void PartialHostChangeIsRolledBackAfterAnException()
    {
        var host = new FakeDesktopHost(isVisible: true, isInteractionEnabled: true)
        {
            ThrowAfterNextInteractionChange = true,
        };
        var controller = new ApplicationCommandController(host);

        var result = controller.SetInteractionEnabled(isEnabled: false);

        Assert.AreEqual(CommandApplicationState.Failed, result.State);
        Assert.IsTrue(result.Snapshot.IsInteractionEnabled, "失败命令必须恢复原有交互状态。");
    }

    [TestMethod]
    public void HostModeIsAppliedOnlyAfterCoordinatorConfirmation()
    {
        var host = new FakeModeHost();
        var controller = new ApplicationCommandController(host);

        var result = controller.SetHostMode(DesktopDisplayMode.Window);

        Assert.AreEqual(CommandApplicationState.Applied, result.State);
        Assert.AreEqual("Window", result.Snapshot.Mode);
    }

    [TestMethod]
    public void UnsupportedHostModeSwitchReturnsFailure()
    {
        var controller = new ApplicationCommandController(
            new FakeDesktopHost(isVisible: true, isInteractionEnabled: true));

        var result = controller.SetHostMode(DesktopDisplayMode.Window);

        Assert.AreEqual(CommandApplicationState.Failed, result.State);
        Assert.AreEqual("Wallpaper", result.Snapshot.Mode);
        StringAssert.Contains(result.Error, "不支持");
    }

    [TestMethod]
    public void SafeHostSwitchReasonIsShownWithoutAnExceptionBody()
    {
        var controller = new ApplicationCommandController(new FailingModeHost());

        var result = controller.SetHostMode(DesktopDisplayMode.Window);

        Assert.AreEqual(CommandApplicationState.Failed, result.State);
        StringAssert.Contains(result.Error, "未发现兼容的 WorkerW 桌面层");
        Assert.IsFalse(result.Error.Contains("InvalidOperationException", StringComparison.Ordinal));
    }

    [TestMethod]
    public void KeyboardSettingsAreAppliedOnlyAfterHostConfirmation()
    {
        var host = new FakeKeyboardHost();
        var controller = new ApplicationCommandController(host);
        var requested = new KeyboardControlSettings(true, KeyboardControlScheme.Wasd);

        var result = controller.ApplyKeyboardControlSettings(requested);

        Assert.AreEqual(CommandApplicationState.Applied, result.State);
        Assert.AreEqual(requested, controller.GetKeyboardControlSettings());
    }

    private sealed class FakeDesktopHost : IDesktopHostControl
    {
        private DesktopHostSnapshot snapshot;

        public FakeDesktopHost(bool isVisible, bool isInteractionEnabled)
        {
            snapshot = new DesktopHostSnapshot(
                "Wallpaper",
                isVisible,
                isInteractionEnabled,
                "尚未完成人工验收",
                null);
        }

        public bool IgnoreVisibilityRequests { get; init; }

        public bool ThrowOnInteractionRequest { get; init; }

        public bool ThrowAfterNextInteractionChange { get; set; }

        public DesktopHostSnapshot GetSnapshot() => snapshot;

        public void SetVisibility(bool isVisible)
        {
            if (!IgnoreVisibilityRequests)
            {
                snapshot = snapshot with { IsVisible = isVisible };
            }
        }

        public void SetInteractionEnabled(bool isEnabled)
        {
            if (ThrowOnInteractionRequest)
            {
                throw new InvalidOperationException("simulated host failure");
            }

            snapshot = snapshot with { IsInteractionEnabled = isEnabled };
            if (ThrowAfterNextInteractionChange)
            {
                ThrowAfterNextInteractionChange = false;
                throw new InvalidOperationException("simulated partial host failure");
            }
        }

        public string CreateDiagnosticSummary() => "safe diagnostics";
    }

    private sealed class FakeModeHost : IDesktopHostControl, IDesktopDisplayModeControl
    {
        private DesktopHostSnapshot snapshot = new("Wallpaper", true, true, "测试", null);

        public DesktopHostSnapshot GetSnapshot() => snapshot;

        public void SetMode(DesktopDisplayMode mode) => snapshot = snapshot with { Mode = mode.ToString() };

        public void SetVisibility(bool isVisible) => snapshot = snapshot with { IsVisible = isVisible };

        public void SetInteractionEnabled(bool isEnabled) =>
            snapshot = snapshot with { IsInteractionEnabled = isEnabled };

        public string CreateDiagnosticSummary() => snapshot.Mode;
    }

    private sealed class FailingModeHost : IDesktopHostControl, IDesktopDisplayModeControl
    {
        public DesktopHostSnapshot GetSnapshot() => new("Wallpaper", true, true, "测试", null);

        public void SetMode(DesktopDisplayMode mode) =>
            throw new DesktopHostSwitchException("未发现兼容的 WorkerW 桌面层。");

        public void SetVisibility(bool isVisible)
        {
        }

        public void SetInteractionEnabled(bool isEnabled)
        {
        }

        public string CreateDiagnosticSummary() => "safe diagnostics";
    }

    private sealed class FakeKeyboardHost : IDesktopHostControl, IKeyboardControl
    {
        private KeyboardControlSettings settings = KeyboardControlSettings.CreateDefault();

        public DesktopHostSnapshot GetSnapshot() => new("WorkerW", true, true, "测试", null);

        public void SetVisibility(bool isVisible)
        {
        }

        public void SetInteractionEnabled(bool isEnabled)
        {
        }

        public string CreateDiagnosticSummary() => "safe diagnostics";

        public KeyboardControlSettings GetKeyboardControlSettings() => settings;

        public void ApplyKeyboardControlSettings(KeyboardControlSettings value) => settings = value;
    }
}
