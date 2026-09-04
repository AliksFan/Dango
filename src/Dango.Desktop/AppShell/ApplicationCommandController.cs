using Dango.Desktop.DesktopHost;

namespace Dango.Desktop.AppShell;

public sealed record DesktopHostSnapshot(
    string Mode,
    bool IsVisible,
    bool IsInteractionEnabled,
    string VerificationStatus,
    string? LastError);

public interface IDesktopHostControl
{
    DesktopHostSnapshot GetSnapshot();

    void SetVisibility(bool isVisible);

    void SetInteractionEnabled(bool isEnabled);

    string CreateDiagnosticSummary();
}

public enum CommandApplicationState
{
    Applied,
    Failed,
}

public sealed record HostCommandResult(
    CommandApplicationState State,
    DesktopHostSnapshot Snapshot,
    string? Error);

public sealed class ApplicationCommandController
{
    private readonly IDesktopHostControl host;

    public ApplicationCommandController(IDesktopHostControl host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public DesktopHostSnapshot GetSnapshot() => host.GetSnapshot();

    public string CreateDiagnosticSummary() => host.CreateDiagnosticSummary();

    public DesktopLayeringSettings GetLayeringSettings() =>
        host is IDesktopLayeringControl layeringControl
            ? layeringControl.GetLayeringSettings()
            : DesktopLayeringSettings.CreateDefault();

    public HostCommandResult ApplyLayeringSettings(DesktopLayeringSettings settings)
    {
        if (host is not IDesktopLayeringControl layeringControl)
        {
            return new HostCommandResult(
                CommandApplicationState.Failed,
                host.GetSnapshot(),
                "当前宿主控制器不支持对象分层设置。");
        }

        var before = layeringControl.GetLayeringSettings();
        try
        {
            layeringControl.ApplyLayeringSettings(settings);
            return new HostCommandResult(CommandApplicationState.Applied, host.GetSnapshot(), null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try
            {
                layeringControl.ApplyLayeringSettings(before);
            }
            catch (Exception rollbackException) when (rollbackException is not OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Unable to roll back object layering: {rollbackException.GetType().Name}");
            }

            return new HostCommandResult(
                CommandApplicationState.Failed,
                host.GetSnapshot(),
                $"对象层级无法应用：{SafeError(exception)}");
        }
    }

    public KeyboardControlSettings GetKeyboardControlSettings() =>
        host is IKeyboardControl keyboardControl
            ? keyboardControl.GetKeyboardControlSettings()
            : KeyboardControlSettings.CreateDefault();

    public HostCommandResult ApplyKeyboardControlSettings(KeyboardControlSettings settings)
    {
        if (host is not IKeyboardControl keyboardControl)
        {
            return new HostCommandResult(
                CommandApplicationState.Failed,
                host.GetSnapshot(),
                "当前宿主控制器不支持键盘控制设置。");
        }

        var before = keyboardControl.GetKeyboardControlSettings();
        try
        {
            keyboardControl.ApplyKeyboardControlSettings(settings);
            var after = keyboardControl.GetKeyboardControlSettings();
            return after == settings
                ? new HostCommandResult(CommandApplicationState.Applied, host.GetSnapshot(), null)
                : new HostCommandResult(CommandApplicationState.Failed, host.GetSnapshot(), "键盘控制设置未确认。");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try
            {
                keyboardControl.ApplyKeyboardControlSettings(before);
            }
            catch (Exception rollbackException) when (rollbackException is not OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine($"Unable to roll back keyboard control: {rollbackException.GetType().Name}");
            }

            return new HostCommandResult(
                CommandApplicationState.Failed,
                host.GetSnapshot(),
                $"键盘控制无法应用：{SafeError(exception)}");
        }
    }

    public HostCommandResult SetVisibility(bool isVisible) =>
        Apply(
            snapshot => snapshot.IsVisible,
            host.SetVisibility,
            isVisible,
            "桌面显示");

    public HostCommandResult SetInteractionEnabled(bool isEnabled) =>
        Apply(
            snapshot => snapshot.IsInteractionEnabled,
            host.SetInteractionEnabled,
            isEnabled,
            "交互模式");

    public HostCommandResult SetHostMode(DesktopDisplayMode mode)
    {
        if (host is not IDesktopDisplayModeControl modeControl)
        {
            return new HostCommandResult(
                CommandApplicationState.Failed,
                host.GetSnapshot(),
                "当前宿主控制器不支持候选切换。");
        }

        try
        {
            modeControl.SetMode(mode);
            var snapshot = host.GetSnapshot();
            if (string.Equals(snapshot.Mode, mode.ToString(), StringComparison.Ordinal))
            {
                return new HostCommandResult(CommandApplicationState.Applied, snapshot, null);
            }

            return new HostCommandResult(
                CommandApplicationState.Failed,
                snapshot,
                "宿主模式未确认，仍保留当前观测状态。");
        }
        catch (DesktopHostSwitchException exception)
        {
            return new HostCommandResult(
                CommandApplicationState.Failed,
                host.GetSnapshot(),
                $"宿主模式无法切换：{exception.SafeReason}");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new HostCommandResult(
                CommandApplicationState.Failed,
                host.GetSnapshot(),
                $"宿主模式无法切换：{SafeError(exception)}");
        }
    }

    private HostCommandResult Apply(
        Func<DesktopHostSnapshot, bool> readValue,
        Action<bool> applyValue,
        bool requestedValue,
        string commandName)
    {
        var before = host.GetSnapshot();
        try
        {
            applyValue(requestedValue);
            var after = host.GetSnapshot();
            if (readValue(after) == requestedValue)
            {
                return new HostCommandResult(CommandApplicationState.Applied, after, null);
            }

            return new HostCommandResult(
                CommandApplicationState.Failed,
                after,
                $"{commandName}未确认，仍保留宿主观测状态。");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try
            {
                applyValue(readValue(before));
            }
            catch (Exception rollbackException) when (rollbackException is not OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Unable to roll back {commandName}: {rollbackException.GetType().Name}");
            }

            var observed = host.GetSnapshot();
            return new HostCommandResult(
                CommandApplicationState.Failed,
                observed,
                $"{commandName}无法应用：{SafeError(exception)}");
        }
    }

    private static string SafeError(Exception exception) =>
        $"宿主返回 {exception.GetType().Name}，未记录可能包含隐私的异常正文。";
}
