# 自身捏脸拆分验证与本地 main 整合

日期：2026-10-06。用户先确认当前捏脸／首页布局观感并授权合并，随后明确选择“只合捏脸，先拆分依赖并重新验证”。本记录以该范围为准；原候选包含的普通房结果不属于此次交付。

## 来源与拆分范围

- 合并前 main：`e2f4037ea2e56a222d41a5cd3085b21cee220565`。原成果分支 `codex/appearance-face-design`：`d18ff6942427dd314df50aac14dc4ea3cfc43d1c`；最终界面实现为 `da6b8b2`，设计归档为 `d18ff69`。原分支、工作树、设置、评审窗口和历史 artifacts 保留。
- 原分支通过 `4eb06a7` 纳入普通房 `88c0231`，所以没有整体合并或把其历史带入 main。从 main 创建 `codex/face-only-main-integration`，提取捏脸提交的文件并在共享 App／Host／Porch／Runtime 上按 main 基线适配。
- 纳入身体／眼睛 RGB、四个连续眼睛滑块、旧 Windows 默认长相校准、严格 Schema 3、旧文件兼容与保护、整套草稿／取消／默认／应用和保存失败重试；保留已确认的矢量图标、键帽、三侧入口、镜像面板、纵向滚动和团子右下保存区、三态接地位置和跳跃阴影。
- 保留 main 既有 Home↔E1 Porch、Window／Wallpaper 与图标层覆盖宿主；同一 Session／Runtime／Entity、唯一帧 driver，Product 门廊仍无普通房入口。颜色和眼睛请求消费真实帧，不另建团子、动画循环或物理模拟。
- 排除 LocalRoom 类与 Host partial、普通房门／托盘入口、房内视角与滚轮、WorkerW 滚轮桥／对应 Win32 helper、普通房 review 参数／mutex、房间 suspend／resume／首帧确认，以及普通房专用中性几何校准。没有 Native 源码、DLL、依赖、服务端、数据库、账号、多人协议、安装器或发布路径增量。
- 独立只读复核确认已验收 XAML、外观控件、Store／Session、眼睛映射和 solver 保持原成果；共享差异仅保留捏脸接线。当前替代 F4 为既有宿主与门廊往返，普通房 F4 留在独立任务。计划中的历史普通房证据不能用于声明此次房间验收。

## 本轮实际验证

Release 桌面与测试构建均为 **0 警告、0 错误**。构建使用显式候选模块输入，隔离产物位于本拆分工作树的 `artifacts/face-only-integration/build/`；未改变默认 Native 来源。`--smoke-test` 退出码为 0，只证明 WPF 初始化／退出，不替代普通启动或人工观感。该 smoke 在最后的键盘焦点修复前执行；修复后的最终构建和受影响测试另列如下。

最终四组 TRX 的 testId 去重后共 **370 个不同测试，370 通过、0 失败、0 未执行／跳过**；没有重复计入初次 29 项宿主检查、前轮重复结果或失败批次。证据保留在拆分工作树的 `artifacts/face-only-integration/results/`，不复制为 main 的历史运行产物。

| 组别 | 结果 | TRX 文件 |
|---|---|---|
| Store／Session、眼睛合同／求解／像素／Native、附件与 Gait 回归 | 193/193 | `face-only-geometry-store.trx` |
| 横竖布局、图标、快捷键、焦点、隔离入口与设置路径保护 | 96/96 | `face-only-layout-entry-accepted.trx` |
| 原生跳跃、阴影像素、头顶／入口净距及门廊往返 | 10/10 | `face-only-jump-shadow.trx` |
| Product／Host、真实 Window↔WorkerW、Home↔Porch、旧模块及唯一帧 driver | 71/71 | `face-only-host-porch-accepted.trx` |

几何组包括 1,575 组合网格和连续映射、旧默认冻结／动态参考、眼帽隔离；这不是完整连续域证明。跳跃组包含 24 条真实 Native 模式轨迹、实际阴影像素和 720 步门廊往返。新增产品测试检查六项外观、实体身份、成功物理 step、唯一帧 owner 与门廊 `DoorCount=0`／无房间／无入房或房间输入权限；真实 WorkerW 和图标层交接也检查同一运行时身份。

首轮宿主／门廊为 68 通过、3 失败，共 71，保留 `face-only-host-porch.trx`：

- 一项真实回退：门廊 TextBox 获焦时，新增首页 Esc 分支抢走输入。定位后在 `ProductHomeView.xaml.cs` 的按键入口增加门廊输入控件焦点 guard；原失败测试未改动，最终回归通过，捏脸滑块自身 Esc 路径保留。
- 两项旧模块测试缺少基线环境变量。找到真正旧模块并只读核验后，以 `DANGO_PHYSICS_BASELINE_PATH` 显式传入；没有以候选模块冒充旧模块或降低断言，两项最终通过。
- 最终静态复核又发现旧 main 的全局文本控件保护只在门廊恢复，设置诊断 TextBox 获焦时仍会被 Esc 导航截获。新增 `EscapeInFocusedSettingsDiagnosticsDoesNotNavigateHome` 在修复前实际失败，证据为 `face-only-settings-focus-before.trx`；恢复 TextBoxBase／ComboBox／PasswordBox 的全局优先后，96 项布局／入口及 71 项宿主／门廊全部通过。滑块仍走自己的 Esc 取消，未扩大新按键行为。
- 先前一个合并筛选的长跑在约九分钟后取消，没有完成结果，不计为通过，也没有依据将其宣布为死锁。随后按原筛选拆组，各组完成并保留独立 TRX。

几何／存储和阴影逻辑在键盘修复后未变化；仅归一新增 C# 文件的空白格式，Host／Porch 与布局／入口两组均在最终构建上重跑。没有机械重跑无关全量套件。受影响 C# 的 `dotnet format whitespace --verify-no-changes --no-restore --include` 退出 0，`git diff --check` 及 staged 检查通过；文档相对文件链接和 Markdown 锚点检查通过。首次空白格式检查失败保留为本轮过程事实，归一格式后通过，不由此声明全仓格式／分析器历史门禁已关闭。实际交付状态在末节补录。

## 模块、人工与环境边界

| 模块 | SHA256 | 本轮用途 |
|---|---|---|
| E1 隔离候选 `dango_physics_module.dll` | `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507` | 显式构建／Native 集成输入，复制到隔离 build |
| 真正旧基线 `dango_physics_module.dll` | `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C` | 旧模块能力反例测试，只读引用既有已验收产物 |
| 受保护跟踪库 `dango_box2d.dll` | `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A` | 没有替换，合并后再次核验 |

人工依据沿用用户对原候选最终自身捏脸／首页布局的“通过”，见[已确认设计](../planning/2026-10-06-home-and-appearance-design.md)及[原观感记录](2026-10-05-home-icons-and-ground-shadow.md)。本次保留该 UI，新增上述拆分后的自动／Native 集成证据，没有新增一次全产品人工验收。原完整候选的本地房体验不能迁移为此次普通房验收。

真实 100%／125% 系统缩放、其它显示器、完整 F5 专项、长时／全量历史差异、完整 Windows／Phase 1／Phase 2A 与双 Windows M7 均未由此次关闭。旧 DLL 缺导出及发布模块 hash 边界仍保留。换装内容、帽子候选、物理 DIY、普通房和多人外观同步独立。

本轮只启动隔离构建和测试进程，没有停止既有产品／评审实例、修改日常设置、更新安装、推送、部署、发布标签或删除分支／工作树。没有新服务、监听端口、数据库或虚构账号；测试文件与旧失败证据保留在自有 artifacts，交付后只核验自身测试进程退出。

## 实际 Git 交付

拆分验证通过，准备创建本地整合提交并对干净的 main 执行 `--ff-only`。实际来源提交、tree、快进结果与交付后状态在合并完成后追加；这一准备状态不表示已推送或已安装。
