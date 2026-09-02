# P1-003 WorkerW/Shell 壁纸层宿主验证记录

> 状态：Windows 11 WorkerW 发现缺陷已修复并通过自动门禁，新候选待人工复验
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

兼容修复候选 `ae775d6` 尚未执行 GUI 复验。以下项目对新候选均保持**未验证**：

- WorkerW 是否成功位于桌面图标之后；
- 透明区域、桌面图标、右键、双击、框选、任务栏、开始菜单和普通窗口；
- 团子/岛命中、拖动释放、`Esc`、失去捕获和焦点；
- 全屏层级；
- Explorer 正常重启后的句柄失效检测、重新发现和挂载；
- 恢复失败时的安全隐藏、托盘可用性和安全退出；
- 重复切换、隐藏、显示、再次启动和退出；
- 1080p 100% 与较高 DPI。

人工验收入口：运行候选后，从托盘菜单或双击托盘图标打开“Dango 控制中心”，进入“桌面体验”，选择“WorkerW/Shell 壁纸层”，再进入“实验与验收”。控制中心提供 22 项同口径清单；失败必须填写最小复现，无法执行必须填写原因，完成后导出 Markdown 报告。

## 6. 分层结论

- **静态门禁**：通过；格式、构建、67 项测试、self-contained 发布和 smoke test 均有真实输出。
- **集成门禁**：本任务没有网络、数据库或服务端集成，不适用，不记为通过。
- **实时行为**：旧候选切换失败并安全回滚；新候选修复后的 WorkerW 挂载未验证。smoke test 没有创建桌面宿主。
- **人工验收**：新候选未验证；留给用户通过图形化入口复验。
- **交付状态**：初始实现提交 `e45d226`，Windows 11 兼容修复提交 `ae775d6`；验证文档随后单独提交。未合并、未推送、未部署，未修改现有快捷方式。
- **下一任务**：P1-004 未开始且未获授权。只有 P1-003 真实与人工结果明确后，才形成 C3 宿主可行性检查点结论。

目前只能称为“P1-003 实现与自动门禁完成”，不能称为“WorkerW 候选通过”或“最终宿主已选定”。
