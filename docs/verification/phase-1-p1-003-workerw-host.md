# P1-003 WorkerW/Shell 壁纸层宿主验证记录

> 状态：候选在 Windows 11 Build 26100 真实验证失败；Overlay 回退路线不受影响
>
> 验收对象：基于 Windows Shell 观测行为的 WorkerW 候选，不代表正式接口保证或最终宿主决策
>
> 实现提交：`e45d226`；Windows 11 兼容修复：`ae775d6`
>
> 日期：2026-09-02

## 1. 范围

本任务使用与 P1-002 相同的静态占位团子和悬浮岛，新增：

- 独立 `DesktopHost/WorkerW` 适配器；
- 集中的 Progman/WorkerW 发现、子窗口挂载、句柄有效性和脱离封装；
- Shell 句柄丢失、重建、重复通知、恢复超时和安全隐藏状态机；
- Overlay 与 WorkerW 的受控切换，目标宿主未确认时恢复原 Overlay；
- 控制中心内的 P1-003 图形化清单、本地草稿、诊断和 Markdown 导出。

不包含正式动画、性能结论、P1-004、P1-006 完整系统恢复加固或最终宿主选择。应用仍默认启动已通过 P1-002 人工验收的 Overlay；用户必须在控制中心显式选择 WorkerW。

## 2. Shell 假设与安全边界

WorkerW 不是面向应用公开承诺的正式宿主接口。本候选只记录当前系统上的可观测行为：

1. 向 Progman 发送实验性消息，请求 Shell 准备相应桌面层；
2. 只按窗口类名、父子关系、Explorer 进程、可见性和尺寸查找桌面层，不读取窗口标题或桌面内容；
3. 挂载前后验证句柄，句柄失效后进入有界恢复；
4. 恢复窗口为 8 秒且最多 8 次尝试，重复失效通知不会重启窗口；
5. 恢复超时或次数耗尽后脱离父窗口并安全隐藏，托盘和控制中心仍应可退出；
6. 初次切换未得到可见 WorkerW 确认时，宿主协调器恢复原 Overlay。

这些行为必须在实际 Explorer 和 Windows 版本上验收，不能由单元测试提升为真实 Shell 通过。

### 2.1 Windows 11 Build 26100 兼容修复

首个候选 `e45d226` 只实现了旧式“包含 `SHELLDLL_DefView` 的顶层窗口之后寻找 WorkerW”。用户实际点击 WorkerW 后切换失败并安全回滚到 Overlay。只读诊断确认当前系统结构为：

- Progman 为 1536×960，包含直接子 `SHELLDLL_DefView`；
- 同一 Progman 下另有一个可见的 1536×960 直接子 WorkerW；
- 多个顶层 WorkerW 均为不可见的 136×38 或无效尺寸窗口；
- Progman 之后的顶层 WorkerW 查找结果为零。

修复后优先选择与 Shell 宿主同 Explorer 进程、可见且尺寸至少达到宿主 75% 的直接子 WorkerW；没有该结构时再使用旧式顶层后继 WorkerW。不同进程、不可见或过小候选会被拒绝。初次发现失败时，控制中心现在显示安全的具体原因，不再只显示 `InvalidOperationException`。

### 2.2 可见性失败与停止点

兼容修复候选 `ae775d6` 能发现并挂载目标：控制中心诊断显示 `Attached`、父句柄有效、恢复尝试为 0、桌面显示为 True 且最近错误为空；用户仍无法在桌面看到团子。进一步只读和可恢复探针确认：

- Dango 场景窗口确实是 WorkerW 的可见子窗口，未被 DWM cloak，矩形为 416×256；
- 窗口保留 WPF 透明窗口的 `WS_EX_LAYERED`，单独使用 `PrintWindow` 能取得完整团子与岛，证明场景已渲染；
- 场景位于 WorkerW 时不可见；临时放到 `SHELLDLL_DefView` 内、桌面图标列表下方仍不可见；
- 临时放到桌面图标层上方仍不可见；恢复 WPF 原生 `WS_POPUP` 样式后仍不可见。

因此失败点不是场景、句柄发现或状态机，而是当前 WPF 透明窗口与该 Shell 子层组合没有形成可见桌面合成结果。继续尝试原生子窗口、窗口区域、输入代理或其他渲染宿主会改变已批准 Spike 的技术边界，按停止点不在 P1-003 内继续堆叠未公开假设。

## 3. 先失败后实现

实施前先加入 WorkerW 状态机、宿主协调器、窗口配置和验收定义测试。首次定向执行因 `WorkerWRecoveryStateMachine`、`DesktopHostCoordinator`、`DesktopHostMode` 和 `CreateP1003WorkerW` 尚不存在而编译失败；补充实现后相关测试转为通过。

自动测试覆盖：

- 旧 Shell 句柄丢失后挂载到新句柄；
- 重复失效通知幂等；
- 恢复超时和尝试耗尽进入安全隐藏；
- 停止状态不可重新进入恢复；
- WorkerW 激活失败时恢复可见 Overlay；
- 两宿主切换保留已确认的显示和交互状态；
- WorkerW 窗口不置顶、不进入任务栏并复用相同占位场景契约；
- P1-003 导出不能自行声明自动通过。
- Windows 11 Progman 直接子 WorkerW 与旧式顶层后继 WorkerW 均能被确定性选择；不同进程、不可见和过小窗口被拒绝。
- 宿主切换错误只显示受控原因，不暴露异常正文。

## 4. 自动门禁

| 门禁 | 实际结果 |
|---|---|
| `dotnet format .\Dango.slnx --verify-no-changes --no-restore` | 通过 |
| `dotnet build .\Dango.slnx -c Release --no-restore` | 通过，0 警告、0 错误 |
| `dotnet test .\Dango.slnx -c Release --no-build --no-restore` | 通过，Core 19/19、Desktop 48/48，共 67/67 |
| `dotnet publish`，`win-x64` self-contained | 通过 |
| 发布物 `--smoke-test` | 退出码 0；只证明 App/Dispatcher 可启动退出，不证明 WorkerW 挂载 |
| `git diff --check` | 通过 |

候选发布物：

- 本地目录：`artifacts/publish/Dango.Desktop-ae775d6-win-x64/`
- 产品版本：`1.0.0+ae775d699a8ad50ee11fd5cbf285415554c52d89`
- `Dango.Desktop.exe` SHA-256：`BF6DF8333EB2683C751CA644DBCB80813A1124C4AC4015805BE77A9835E64443`

该目录是未提交的本地构建产物。P1-002 已验收发布物及现有桌面快捷方式未被替换。

## 5. 真实 Windows 与人工验收

首个候选 `e45d226` 已取得真实失败证据：在 Windows 11 Build 26100 点击 WorkerW 后没有切换，控制中心显示宿主返回 `InvalidOperationException`；根因是该候选无法发现 Progman 的直接子 WorkerW。安全回滚正常，桌面仍保留 Overlay。

兼容修复候选 `ae775d6` 已由用户执行 GUI 复验。最低可见性失败，因此 P1-003 WorkerW/Shell 候选判定为**失败**，其余依赖可见宿主的项目不再继续执行：

- WorkerW 句柄发现与挂载状态：通过自动及运行时观测，但桌面可见性失败；
- 用户人工可见性：失败，多次确认“不可见”；
- 透明区域、桌面图标、输入、拖动、焦点和全屏层级：因最低可见性失败而停止，未验证；
- Explorer 重启恢复、重复生命周期、1080p 100% 与较高 DPI：因候选已失败而停止，未验证。

诊断期间只修改当前 Dango 进程的窗口父级和样式；没有修改系统设置或 Explorer。该进程应通过控制中心正常退出，以回收包括内部 IME 窗口在内的全部临时状态。

## 6. 分层结论

- **静态门禁**：通过；格式、构建、67 项测试、self-contained 发布和 smoke test 均有真实输出。
- **集成门禁**：本任务没有网络、数据库或服务端集成，不适用，不记为通过。
- **实时行为**：失败；新候选能进入 Attached，但在四种受控层级/样式探针下均没有可见桌面结果。
- **人工验收**：失败；用户多次确认没有实际看到 Dango。
- **交付状态**：初始实现提交 `e45d226`，Windows 11 兼容修复提交 `ae775d6`；验证文档随后单独提交。未合并、未推送、未部署，未修改现有快捷方式。
- **C3 宿主可行性**：P1-002 Overlay 已通过，P1-003 WorkerW/Shell 候选失败；“至少一个候选满足最低要求”的进入条件已由 Overlay 满足，但最终采用 Overlay 仍需用户确认。
- **下一任务**：P1-004 未开始且未获授权。若继续，应以已通过人工验收的 Overlay 作为当前宿主，不把 WorkerW 失败掩盖为成功。

P1-003 到此按停止点结束：候选失败，保留实现、自动测试和真实失败证据，不继续扩展 Shell 假设，也不把失败候选用于 P1-004。
