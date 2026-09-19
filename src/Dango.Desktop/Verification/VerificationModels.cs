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

    public static VerificationDefinition CreateEyeHatHosts()
    {
        var items = CreatePhysicsModuleHosts().Items.ToList();
        foreach (var host in new[] { "window", "wallpaper" })
        foreach (var count in new[] { 1, 2, 3 })
            items.Add(Item($"{host}-{count}-eye-hat", $"{host} · {count} 体眼帽", "眼睛面向、跳跃与帽子惯性",
                "逐只选择，左右移动急停、跳落和上下拖放；切帽，应用表情并眨眼；隐藏恢复和切换宿主。",
                "眼距随转向缩小，释放回正；跳跃眼睛上下动；帽子保留埋入位置和惯性前滑回弹，三体状态不串，不裁切。"));
        return new(CurrentSchemaVersion, "p1-eye-hat-hosts", "眼帽双宿主隔离预览验收", items);
    }

    public static VerificationDefinition CreatePhysicsModuleHosts()
    {
        var items = new List<VerificationItemDefinition>();
        foreach (var host in new[] { "Window", "Wallpaper" })
        {
            foreach (var count in new[] { 1, 2, 3 })
            {
                var prefix = $"{host.ToLowerInvariant()}-{count}";
                var category = $"{host} · {count} 体（独立验收）";
                items.Add(Item(prefix + "-control", category, "移动、下压、跳跃与手感",
                    $"切换到 {host}，使场景保留 {count} 只。分别选择每只，测试 A/D、S、W 及长按 W。",
                    "只有选中实体接收输入；起跳竖直、手感保持，长按不重复触发起跳。"));
                items.Add(Item(prefix + "-drag", category, "反复拖放、取消与重置",
                    "逐只拖放，尝试 Esc/失焦/关闭隐藏后恢复，重复拖放及重置。",
                    "命中对应实体，释放不粘鼠标；取消无残留输入，重置不恢复已移除实体。"));
                if (count > 1)
                    items.Add(Item(prefix + "-contact", category, "持续碰撞、偏心叠放与抽走支撑",
                        "持续推挤、偏心叠放，拖走或移除底部支撑；三体必须分别观察全部实体，不沿用双体结论。",
                        "无异常穿透或失控；承重和分离正常，允许已经认可的自然滑落。"));
            }
        }
        items.Add(Item("module-layers", "宿主与系统", "三体图标上下层级",
            "三体下分别切换图标上/下，逐只拖动，重叠观察帽子，再操作空白区和桌面图标。",
            "所有团子层级一致，帽子在所有身体之上；空白区穿透，图标优先规则保持。"));
        items.Add(Item("module-roundtrip", "宿主与系统", "三体宿主往返与退出",
            "三体下往返两种宿主至少三次，隐藏恢复、最小化恢复，最终从控制中心或托盘退出。",
            "实体数量与状态延续，无双重推进、重复团子、残留覆盖窗/捕获/托盘。"));
        items.Add(Item("module-dpi", "宿主与系统", "DPI 与跨屏",
            "在至少两种分辨率或缩放下，分别验证两种宿主的三体显示与拖动；有多屏时补跨屏。",
            "抓取点、轮廓与位置无明显偏移，窗口可操作；无法执行则标记未验证。"));
        items.Add(Item("module-focus", "宿主与系统", "焦点、全屏与故障信息",
            "操作普通应用、任务栏、全屏窗口；查看只读参数和诊断，故障场景按文档单独验证。",
            "不抢焦点或遮挡正常操作；参数不可写入，故障不可伪装成正常物理帧。"));
        return new(CurrentSchemaVersion, "p1-physics-module-hosts", "物理模块正式宿主接入人工验收", items);
    }

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

    public static VerificationDefinition CreateP1006WindowModes() =>
        new(
            CurrentSchemaVersion,
            "p1-006-window-modes",
            "P1-006 壁纸层与普通窗口人工验收",
            [
                Item("wallpaper-default", "启动与设置", "首次启动默认壁纸层", "使用无显示模式字段的旧设置或全新设置启动。", "团子只在壁纸层显示，且不会自动弹出普通窗口。"),
                Item("window-standard", "普通窗口", "标准窗口行为", "切换到普通窗口并检查标题栏、任务栏、移动、缩放和普通应用遮挡。", "窗口可正常操作，不置顶且不透明穿透。"),
                Item("window-input", "普通窗口", "窗口内控制与拖动", "聚焦普通窗口后执行方向控制、跳跃和拖动，再切走焦点。", "仅窗口有焦点时接收按键，拖动与动态表现正常。"),
                Item("window-hide", "生命周期", "关闭按钮只隐藏", "点击普通窗口关闭按钮，再从托盘和控制中心恢复显示。", "Dango 进程、托盘和控制中心保留，只恢复同一个场景窗口。"),
                Item("window-minimize", "生命周期", "最小化与恢复", "最小化普通窗口，等待后恢复。", "隐藏期间没有补帧或积压输入，恢复后场景继续可用。"),
                Item("roundtrip", "宿主切换", "两模式往返", "在壁纸层与普通窗口之间往返至少三次。", "始终只有一只团子，位置合理，无闪退、重放或重复进程。"),
                Item("wallpaper-desktop", "壁纸层", "桌面输入与层级", "操作桌面图标、空白区、任务栏和普通窗口，并复验对象层级。", "Dango 不普遍阻断桌面输入，层级设置只影响壁纸层。"),
                Item("explorer-wallpaper", "Shell 恢复", "壁纸层恢复", "在壁纸层正常重启 Windows Explorer，并记录失败恢复路径。", "成功时有界恢复；失败时只安全隐藏，不自动显示普通窗口。"),
                Item("explorer-window", "Shell 恢复", "窗口模式不受 Explorer 影响", "普通窗口运行时重启 Windows Explorer。", "普通窗口持续可用，壁纸层恢复流程不抢占显示。"),
                Item("dpi-monitor", "DPI 与显示器", "缩放与多显示器", "在 100% 与当前高 DPI 环境复验；有双屏时跨屏并移除副屏。", "窗口保持可操作，团子完整可见，坐标和命中不漂移。"),
                Item("settings-restart", "启动与设置", "模式与窗口布局恢复", "选择普通窗口并调整布局后退出、重启，再验证旧宿主设置迁移。", "最后确认模式与安全窗口布局恢复，旧宿主值迁移到 Wallpaper。"),
                Item("system-focus", "系统兼容", "系统 UI 与焦点", "检查 Alt+Tab、开始菜单、任务栏、最大化窗口和真正全屏应用。", "Dango 不异常抢焦点、置顶或遮挡系统操作。"),
                Item("safe-exit", "生命周期", "完整退出", "从托盘或控制中心退出 Dango。", "场景窗口、输入桥、计时器、托盘和进程全部回收。")
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
