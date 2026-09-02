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

    public HostCommandResult SetHostMode(DesktopHostMode mode)
    {
        if (host is not IDesktopHostModeControl modeControl)
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
