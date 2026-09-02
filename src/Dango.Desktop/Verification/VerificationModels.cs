using System.IO;

namespace Dango.Desktop.Verification;

public enum ManualVerificationOutcome
{
    Unverified,
    Passed,
    Failed,
    UnableToRun,
}

public sealed record VerificationItemDefinition(
    string Id,
    string Category,
    string Title,
    string Instructions,
    string Expected,
    bool IsRequired = true);

public sealed record VerificationDefinition(
    int SchemaVersion,
    string Id,
    string Title,
    IReadOnlyList<VerificationItemDefinition> Items)
{
    public const int CurrentSchemaVersion = 1;

    public static VerificationDefinition CreateP1002Overlay() =>
        new(
            CurrentSchemaVersion,
            "p1-002-overlay",
            "P1-002 透明覆盖层人工验收",
            [
                Item("startup-single-instance", "启动与恢复", "连续启动两次", "保持 Dango 运行，再次启动同一程序。", "只存在一个覆盖层、一个控制中心和一个托盘图标。"),
                Item("transparent-click-through", "输入与焦点", "透明区域单击穿透", "在窗口矩形内、团子和岛之外单击桌面或下层应用。", "点击到达下层目标，覆盖层不抢焦点。"),
                Item("transparent-desktop-actions", "输入与焦点", "透明区域桌面操作", "在透明区域右键、双击并拖出桌面框选。", "下层桌面保持正常行为。"),
                Item("dango-hit", "输入与焦点", "团子区域命中", "单击占位团子。", "团子区域接收输入，透明区规则不改变。"),
                Item("island-hit", "输入与焦点", "悬浮岛区域命中", "单击悬浮岛。", "岛作为可见场景区域参与命中。"),
                Item("drag-release", "拖动与取消", "拖动并释放", "从团子内部开始拖动并释放。", "仅团子被移动，释放后不再捕获鼠标。"),
                Item("drag-escape", "拖动与取消", "Esc 取消拖动", "拖动中按 Esc。", "拖动立即取消，鼠标捕获释放。"),
                Item("drag-lost-capture", "拖动与取消", "失去捕获取消", "拖动中切换到其他窗口或造成捕获丢失。", "拖动取消，其他窗口可正常操作。"),
                Item("focus-startup", "输入与焦点", "启动不抢焦点", "启动后直接在其他应用输入。", "覆盖层不主动抢占焦点。"),
                Item("focus-after-interaction", "输入与焦点", "交互后不持续抢焦点", "与团子交互后继续操作其他应用。", "其他应用可以立即继续正常接收输入。"),
                Item("desktop-icons", "桌面与系统 UI", "桌面图标和框选", "操作桌面图标单击、双击、右键和框选。", "团子和岛之外不被覆盖层普遍阻断。"),
                Item("system-ui", "桌面与系统 UI", "任务栏与开始菜单", "操作任务栏、开始菜单和普通窗口。", "系统 UI 与普通窗口行为正常。"),
                Item("fullscreen-layer", "全屏与窗口层级", "全屏应用层级", "打开并退出一个全屏应用。", "记录覆盖层层级，且不造成无法恢复的干扰。"),
                Item("tray-visibility", "托盘与退出", "托盘显示与隐藏", "执行隐藏覆盖层，再执行显示覆盖层。", "显示状态正确，且不创建第二个窗口。"),
                Item("tray-control-center", "托盘与退出", "打开控制中心", "使用托盘菜单和双击托盘图标分别打开控制中心。", "两种入口都激活同一个普通任务栏窗口。"),
                Item("diagnostics-privacy", "托盘与退出", "诊断摘要隐私", "打开诊断页并检查显示与导出字段。", "只显示 Dango、系统、显示和 DPI 摘要，不包含桌面内容或用户信息。"),
                Item("tray-exit", "托盘与退出", "安全退出", "执行退出 Dango。", "覆盖层、控制中心和托盘图标消失，进程退出。"),
                Item("repeat-lifecycle", "启动与恢复", "重复生命周期", "重复启动、隐藏、显示、打开控制中心和退出 3 次。", "无窗口、托盘或进程残留。"),
                Item("dpi-100", "DPI 与显示器", "1080p 100%", "在 1080p、100% 缩放环境执行关键操作。", "控制中心不截断，覆盖层行为有明确记录。"),
                Item("dpi-high", "DPI 与显示器", "较高 DPI", "在例如 1440p、150% 缩放环境执行关键操作。", "控制中心不截断，覆盖层行为有明确记录。")
            ]);

    public static VerificationDefinition CreateP1003WorkerW() =>
        new(
            CurrentSchemaVersion,
            "p1-003-workerw",
            "P1-003 WorkerW/Shell 壁纸层人工验收",
            [
                Item("workerw-attached", "Shell 挂载", "位于桌面图标之后", "切换到 WorkerW 宿主并观察桌面。", "团子和岛位于桌面图标之后，不显示为普通窗口或置顶层。"),
                Item("transparent-click-through", "输入与焦点", "透明区域单击穿透", "在 WorkerW 场景矩形内、团子和岛之外单击桌面。", "点击到达桌面目标，WorkerW 不持续抢焦点。"),
                Item("transparent-desktop-actions", "输入与焦点", "透明区域桌面操作", "在透明区域右键、双击并拖出桌面框选。", "桌面空白区域保持正常行为。"),
                Item("desktop-icons", "桌面操作", "桌面图标和框选", "操作桌面图标单击、双击、右键和框选。", "桌面图标与空白区域操作正常，WorkerW 不阻断系统行为。"),
                Item("system-ui", "桌面操作", "任务栏与开始菜单", "操作任务栏、开始菜单和普通窗口。", "系统 UI 与普通窗口行为正常。"),
                Item("dango-hit", "输入与焦点", "团子区域命中", "单击占位团子。", "团子区域接收输入，透明区规则不改变。"),
                Item("island-hit", "输入与焦点", "悬浮岛区域命中", "单击悬浮岛并继续操作桌面。", "岛参与命中，随后桌面仍可正常操作。"),
                Item("drag-release", "拖动与取消", "拖动并释放", "从团子内部开始拖动并释放。", "仅团子被移动，释放后不再捕获鼠标。"),
                Item("drag-escape", "拖动与取消", "Esc 取消拖动", "拖动中按 Esc。", "拖动立即取消，鼠标捕获释放。"),
                Item("drag-lost-capture", "拖动与取消", "失去捕获取消", "拖动中切换到其他窗口或造成捕获丢失。", "拖动取消，其他窗口可正常操作。"),
                Item("focus", "输入与焦点", "不持续抢焦点", "与团子交互后立即操作其他应用。", "其他应用可以继续正常接收输入。"),
                Item("fullscreen-layer", "桌面操作", "全屏应用层级", "打开并退出一个全屏应用。", "WorkerW 保持在桌面背景层，不覆盖全屏应用。"),
                Item("explorer-restart", "Shell 恢复", "Explorer 正常重启", "保持 Dango 运行并正常重启 Windows Explorer。", "宿主检测旧句柄失效，重新挂载到新 WorkerW，控制中心和托盘仍可用。"),
                Item("safe-recovery-failure", "Shell 恢复", "恢复失败安全隐藏", "在诊断中观察无法重新发现 WorkerW 的状态，或记录当前环境无法执行。", "Dango 有界重试后安全隐藏，不置顶、不无限重试且仍可退出。"),
                Item("visibility", "生命周期", "隐藏与恢复", "隐藏桌面团子，再重新显示。", "隐藏时不残留场景；显示时重新校验并挂载有效 WorkerW。"),
                Item("single-instance", "生命周期", "再次启动", "保持 Dango 运行，再次启动同一程序。", "只保留一个宿主、一个控制中心和一个托盘图标。"),
                Item("tray-control-center", "生命周期", "托盘打开控制中心", "使用托盘菜单和双击托盘图标分别打开控制中心。", "两种入口都激活同一个普通任务栏窗口。"),
                Item("tray-exit", "生命周期", "安全退出", "从托盘或控制中心退出 Dango。", "WorkerW 子窗口、控制中心、托盘和进程全部退出。"),
                Item("repeat-lifecycle", "生命周期", "重复生命周期", "重复启动、切换宿主、隐藏、显示和退出 3 次。", "无窗口、托盘、计时器或进程残留。"),
                Item("diagnostics-privacy", "诊断", "诊断摘要隐私", "打开诊断页并检查字段。", "只显示 Dango、系统、句柄状态和显示摘要，不包含窗口标题、桌面内容或用户输入。"),
                Item("dpi-100", "DPI 与显示器", "1080p 100%", "在 1080p、100% 缩放环境执行关键操作。", "WorkerW 场景和控制中心无不可恢复的布局问题。"),
                Item("dpi-high", "DPI 与显示器", "较高 DPI", "在较高 DPI 环境执行关键操作。", "WorkerW 场景坐标与控制中心保持可用。")
            ]);

    private static VerificationItemDefinition Item(
        string id,
        string category,
        string title,
        string instructions,
        string expected) =>
        new(id, category, title, instructions, expected);
}

public sealed record VerificationEnvironmentSnapshot(
    string ApplicationVersion,
    string BuildIdentity,
    string HostMode,
    string OsBuild,
    string DisplaySummary);

public sealed class VerificationItemResult
{
    public VerificationItemResult(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ItemId = itemId;
    }

    public string ItemId { get; init; }

    public ManualVerificationOutcome Outcome { get; set; }

    public string Notes { get; set; } = string.Empty;
}

public sealed class VerificationSession
{
    public int SchemaVersion { get; init; }

    public required string DefinitionId { get; init; }

    public required string SessionId { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public required VerificationEnvironmentSnapshot Environment { get; init; }

    public required List<VerificationItemResult> Results { get; init; }

    public static VerificationSession Create(
        VerificationDefinition definition,
        VerificationEnvironmentSnapshot environment,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(environment);

        return new VerificationSession
        {
            SchemaVersion = definition.SchemaVersion,
            DefinitionId = definition.Id,
            SessionId = Guid.NewGuid().ToString("N"),
            StartedAt = startedAt,
            Environment = environment,
            Results = definition.Items.Select(item => new VerificationItemResult(item.Id)).ToList(),
        };
    }
}

public static class VerificationSessionValidator
{
    private const int MaximumNotesLength = 2000;

    public static void Validate(VerificationDefinition definition, VerificationSession session)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(session);

        if (session.SchemaVersion != definition.SchemaVersion ||
            session.SchemaVersion != VerificationDefinition.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"不支持的验收记录版本：{session.SchemaVersion}。");
        }

        if (!string.Equals(session.DefinitionId, definition.Id, StringComparison.Ordinal))
        {
            throw new InvalidDataException("验收记录与验收定义不匹配。");
        }

        if (string.IsNullOrWhiteSpace(session.SessionId))
        {
            throw new InvalidDataException("验收记录缺少会话标识。");
        }

        ValidateEnvironment(session.Environment);

        var expectedIds = definition.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var resultIds = session.Results.Select(result => result.ItemId).ToArray();
        if (resultIds.Length != expectedIds.Count ||
            resultIds.Distinct(StringComparer.Ordinal).Count() != resultIds.Length ||
            resultIds.Any(id => !expectedIds.Contains(id)))
        {
            throw new InvalidDataException("验收记录包含缺失、重复或未知项目。");
        }

        foreach (var result in session.Results)
        {
            if (!Enum.IsDefined(result.Outcome))
            {
                throw new InvalidDataException($"验收项目 {result.ItemId} 使用未知结果。");
            }

            if (result.Notes.Length > MaximumNotesLength)
            {
                throw new InvalidDataException($"验收项目 {result.ItemId} 的备注超过 {MaximumNotesLength} 字符。");
            }

            if (result.Outcome == ManualVerificationOutcome.UnableToRun &&
                string.IsNullOrWhiteSpace(result.Notes))
            {
                throw new InvalidDataException($"验收项目 {result.ItemId} 标记为无法执行时必须填写原因。");
            }

            if (result.Outcome == ManualVerificationOutcome.Failed &&
                string.IsNullOrWhiteSpace(result.Notes))
            {
                throw new InvalidDataException($"验收项目 {result.ItemId} 标记为人工失败时必须填写最小复现。");
            }
        }
    }

    private static void ValidateEnvironment(VerificationEnvironmentSnapshot environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (string.IsNullOrWhiteSpace(environment.ApplicationVersion) ||
            string.IsNullOrWhiteSpace(environment.BuildIdentity) ||
            string.IsNullOrWhiteSpace(environment.HostMode) ||
            string.IsNullOrWhiteSpace(environment.OsBuild) ||
            string.IsNullOrWhiteSpace(environment.DisplaySummary))
        {
            throw new InvalidDataException("验收记录缺少必要的本地环境摘要。");
        }
    }
}
