# 第二批第一小步：点击与抚摸实施计划

日期：2026-09-17；实施更新：2026-09-18。用户已确认[轻量交互设计](2026-09-17-light-interaction-design.md)及本计划，并于 09-18 明确授权恢复缺失代码后继续。**L0–L4 已完成，L5 自动回归完成（保留历史失败），用户已确认本批整体人工验收通过；专项矩阵仍未逐项验证。** 下文实施前快照保留作历史依据；当前结果见 §7 与[验证记录](../verification/2026-09-17-light-interaction.md)。

## 1. 实施起点与保护

当前规划工作树 `e0ac/Dango` 为 detached HEAD `06103556e17ac1dd3f4ac9227324a471ea4edd23`，只有本批规划文档。实际第一批工作树 `desktop-entry-integration` 仍在 `codex/desktop-entry-integration` / `72199ef93febaec88280fb2bd3c27c2fe594f3ca`；本轮再次核实，第一批代码/文档仍是未提交修改，新增文件仍未跟踪。以下路径除本批规划文档外，均相对实际第一批工作树。

实施时采用独立 `codex/light-interaction` 分支/worktree（若已存在，先检查，不覆盖）。起点必须包含第一批当前成果，不能只从 `72199ef` 或本规划树开始补造。L0 应当：

1. 再查源树 HEAD、索引、tracked diff、全部未跟踪路径；生成“第一批继承清单”，记录必要文件相对路径和 SHA-256。只读源树，不替源树提交、暂存、stash 或清理。
2. 从核实的 HEAD 建立新实施树，仅将清单中的第一批必要源码、测试、文档和脚本差异复制到新树。复制前后检查源文件指纹，逐项核验目标字节；源发生变化时重核，不拿混合快照继续。新树同名现有文件先比较，不能覆盖未知修改。
3. 将本批已确认设计和本计划带入实施树；保存导入后的基线清单与差异，再开始本批修改。交付时相对这份基线报告本批增量，第一批成果单独列为继承内容，不计作本批新实现。
4. 不整树复制 `.git`、`bin/obj`、旧日志、用户配置或历史实验。测试需要的真实 native 夹具只读复制到新的隔离位置，保持库字节及既有测试预期路径；不启动 native 重建脚本。

受保护的 `p1-004-box2d-soft-body` 历史实验、预览、证据与生产 DLL 不动。旧 `dango_box2d.dll` SHA-256 为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`，统一模块为 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`；实施前实测核对，任何差异先定位。本计划不授权合并 main、推送、部署、发布或删除旧资源。

## 2. 增量与门禁

按 L0→L1→L2→L3→L4→L5 顺序，一个实现者负责共享 runtime 和宿主接线；测试审查可并行只读。每步先定义能失败的检查，再实现并运行受影响验证，不并行改同一生命周期链路。

| 步骤 | 文件/模块 | 预期行为与验证 | 依赖、停止条件 |
|---|---|---|---|
| L0 保全基线 | 新实施树、隔离 artifacts、Desktop/Core/Physics 测试 | 第一批继承清单与源树保护指纹一致；必要库可解析；一次隔离 Release build 与基线测试，记录原失败集合 | 缺夹具、新增失败或源树变动时先定位。当前树不具备实施基线，不直接编码 |
| L1 输入判定 | 新增 `DesktopHost/Interaction/LightInteractionController.cs` 及同目录测试（名称建议） | 注入单调时间、DIP 坐标和命中结果；Down 锁实体/会话/Shift，Move/Up 区分点击、拖动、抚摸；覆盖设计 A1–A3、A6 的判定层 | L0；先纯托管，不调用 native 或窗口。3 DIP 转拖动不等待 250 ms；Up 漏 Move 不误点击 |
| L2 短反馈与取消 | `Rendering/Dango/StackedDangoSceneRuntime.cs`、必要的 `Physics/Presentation/Attachments/AttachmentSceneController.cs`；新反馈测试及现有附件集成测试 | 按实体/代际提交 Blink 或 SetEyes；600 ms 成功步到期 Release；300/1000 ms 共用门；Stop 前取消、故障清理、重置失效。覆盖 A4–A7 | L1 契约固定；不通过 SelectEntity 路由，不新增计时循环，不改眼帽数学/native。反馈不得改变物理快照 |
| L3 Window 最小完整链路 | `DesktopHost/Window/DangoSceneWindow.xaml.cs`，必要的 Product 能力接线 | 点击/抚摸使用 L1/L2；原拖动在跨阈值后继续。验证正常 Up 的 LostCapture、转拖动时 InputCancelled 重入、失焦/最小化/Esc | L1/L2；Product-only 开关由现有应用模式显式传入，不用附件存在推断。捕获失败必须结束物理抓取 |
| L4 Wallpaper 与生命周期补齐 | `WorkerWWindow.xaml.cs`、`WorkerWInputProxy.cs`、`DesktopDangoSceneOverlayWindow.cs`；必要的宿主协调器取消接线；帮助文字 | 覆盖 WPF 直连、图标下 proxy、图标上 overlay；统一 Down/Move/Up/Cancel 与像素→DIP；桌面前台与 Esc 在键盘控制关闭时也有效。覆盖 A6/A8，DeveloperReview 保留原入口 | L3；正常 Up 与异常取消不可共用含糊结束回调。图标优先、空白穿透、不激活行为不变 |
| L5 回归及项目窗口验收 | Desktop 测试、专属验证记录；项目正常 Product 输出 | 全部 A1–A8，原身体/眼帽回归，两宿主真实操作及用户观感验收；逐项记录未验证与遗留问题 | L4；自动/实时/人工分别报告。默认不发布、不压缩打包，不用 review 或 smoke 代替普通入口 |

不新增数据库、服务器、网络协议、依赖或付费资源。只记录必要的测试证据，不保存真实鼠标轨迹。第一批设置 Schema 和保存流程保持不变。

## 3. 两个窄接口的责任

**输入判定器**只保留一个按压会话：实体/有效代际、会话号、按下点/时间、Shift 分支、最大位移、上一采样点和是否已消费。输出只需表达无操作、开始/移动/结束物理拖动、请求点击/抚摸或取消。它不保存轨迹列表，不直接改眼睛，不依赖全局键盘钩子。反馈结束时间不属于鼠标会话；正常 Up 终结捕获后，已经触发的反馈继续按时完成。

**反馈协调**由共享 runtime 所有，在其 UI owner thread 上接收有效实体/会话，仅消费既有成功固定步。共用现有每实体附件 token/sequence，记录当前本批通道、期限、冷却及有效代际；不每手势注册 token，不建立通用动作队列。按实体提交前和到期释放前都校验身份，不写 SelectedEntity。Product 接线与 DeveloperReview 隔离；附件失效时拒绝反馈但不阻止原身体拖动。

取消顺序严格遵守设计：健康态先 Release 本批通道再暂停；候选转拖动先终结旧会话/捕获，再调用原 BeginPhysicalDrag，待同步清输入回调结束后复核并重新捕获。重复或过期 LostCapture 不回撤刚触发的反馈。故障后不调用会抛健康检查异常的公共 SubmitAttachment；清本地状态，沿用显式恢复。取消/拒绝不补发、不重试、不重置帽子/眼动。

转拖动重捕获由宿主负责：现有 `BeginPhysicalDrag → SelectEntity → ClearPhysicalInput → InputCancelled` 会使 Window 释放捕获，并使 WorkerW 取消 proxy/overlay 捕获。这正是已确认设计采用上述顺序的原因；L3/L4 必须验证整段同步重入、宿主/按键条件复核和失败回收，不能仅测试判定器发出了“开始拖动”。

## 4. 必须能失败的检查与现有落点

具体数值和完整场景以设计 A1–A8 为准，下面说明检查如何落地，不另立阈值。

| 检查组 | 测试落点 | 必需负例和实际观察 |
|---|---|---|
| 判定与捕获 | 新 `DesktopHost/Interaction/LightInteractionControllerTests.cs`，参考既有 `DesktopInteractionControllerTests` | 3 DIP、250 ms、16 DIP、8 DIP、600 ms 的小于/等于/大于；移出再返回、只 Up 越阈值、中途 Shift、折返微抖、重复 Up、正常/异常失捕获；断言反馈次数、实体及零多余抓取 |
| 眼睛输出与限频 | 新反馈测试，复用 `EyeMotionTests`、`AttachmentSceneIntegrationTests` 夹具 | 直接检查双眼实际附件帧，不能只看 Accepted。600 ms 自动释放与 200 ms 恢复、两种冷却的交叉请求、Busy/Stopped、无 UI 帧时钟、暂停/失败步不推进 |
| 身份、取消与物理保持 | `AttachmentSceneIntegrationTests`、`ProductEntryRuntimeTests` 或相邻 runtime 测试 | 命令接纳未消费和播放中分别取消；重置后旧 Up/Release 无效；窄附件接口对非 selected 虚构实体不改选择、不污染另一只。用相同物理输入序列比较有/无反馈的真实快照，帽子与 gaze 不被释放 |
| 宿主接线 | `DesktopHostModuleIntegrationTests`、WorkerW 对应用例，必要的 STA 测试 | 四条输入路径相同事件得到相同结果；像素/DIP 换算、捕获失败、Begin→InputCancelled 重入、键盘关闭时 Esc、Product/review 隔离。源码/XAML 检查只作补充，不替代行为断言 |

故障注入使用虚构状态/隔离夹具或既有可注入边界，不破坏源 DLL、真实 Explorer 或日常配置。新检查应能拦住：按下即眨眼、拖完补点击、永久眯眼、正常 Up 误取消、旧代际污染、隐藏后续播和第二个物理时钟。

## 5. 构建与验证环境

使用一个新的 `artifacts/light-interaction/<run-name>/` 根，日志、构建、测试结果和证据分开；已有目录不清理或覆盖。L0 解析本机路径并保存在忽略的证据中，源码/文档不写个人绝对路径。

Desktop 的 `DangoPhysicsModulePath` 由现有 csproj 复制统一模块到输出，旧 DLL 继续使用仓库原字节。除统一模块外，既有 Physics 回归还需历史三体、双体与 collision-ratio 对照夹具；依照 `PhysicsTestLibraries`、`DangoPhysicsThreePreviewTests`、`DangoPhysicsEquivalenceTests` 的已存在路径逐项核对。必要夹具未齐时标明未验证，不能用假库、重建库或跳过后宣称通过。

以下是实施时的命令类别，**本轮未运行**；均从实施树根执行，`$lightBuildRoot/$lightResultsRoot/$lightModulePath` 由 L0 解析为本次隔离路径：

```powershell
dotnet build .\Dango.slnx -c Release --artifacts-path $lightBuildRoot "-p:DangoPhysicsModulePath=$lightModulePath"
dotnet test .\tests\Dango.Desktop.Tests\Dango.Desktop.Tests.csproj -c Release --no-build --artifacts-path $lightBuildRoot --results-directory "$lightResultsRoot/desktop" --logger 'trx;LogFileName=desktop.trx'
dotnet test .\tests\Dango.Core.Tests\Dango.Core.Tests.csproj -c Release --no-build --artifacts-path $lightBuildRoot --results-directory "$lightResultsRoot/core" --logger 'trx;LogFileName=core.trx'
dotnet test .\tests\Dango.Physics.Tests\Dango.Physics.Tests.csproj -c Release --no-build --artifacts-path $lightBuildRoot --filter 'FullyQualifiedName!~DangoPhysicsStabilityTests' --results-directory "$lightResultsRoot/physics" --logger 'trx;LogFileName=physics.trx'
dotnet format .\Dango.slnx --verify-no-changes --no-restore
git diff --check
```

各增量先跑命名到具体类的受影响用例；L5 在最后一次代码变更后跑完整 Desktop、Core 与 Physics 常规，保留 TRX/命令/退出码。无新失败或改动时不无理由反复跑全套。长测明确排除，不视为通过。

第一批记录为 Release build 成功、Core 37/37、Physics 常规 32/32、Desktop 322/323。预期保留的历史失败为 `SharedSelfWorldExportsAreAvailable`；本批新增用例必须通过，出现额外失败应定位而非降门槛。全仓 format 的 5 个既有文件问题仍需区分；其中 `DangoSceneWindow.xaml.cs`、`DesktopDangoSceneOverlayWindow.cs` 可能落入本批修改范围，不再声称“修改文件无旧问题”，应逐项比对前后、避免整文件顺手格式化和新增问题。

## 6. 项目启动、验收和交付

L5 自动结果可审阅后，按设计 §7 从本次项目构建输出打开普通 Product 窗口；不默认 ZIP，不以 `--eye-hat-review` 或 `--physics-host-review` 作为本批人工入口，因为新手势只接 Product。`--smoke-test` 也不能证明交互。

启动前检查普通单实例与既有进程：已有正式实例时不能误把第二次启动唤醒旧进程当新候选成功，也不能批量结束进程。优先沿用有效隔离 Windows 测试环境；若需替换用户正在运行的正式实例或更改日常设置，再明确具体目标。记录本次精确可执行路径、PID、模块指纹和设置归属；不增加新的产品模式/实例隔离框架来绕过环境问题。

人工依次覆盖 Window、Wallpaper 图标上/下层级的点击/抚摸、原拖动/方向/眼帽，以及失焦、Esc、隐藏恢复、切宿主、重置和两种 DPI。真实桌面输入、焦点、图标/任务栏兼容必须实际操作；受控自动故障测试和真实 WorkerW 故障结果分开，设备或环境不足标未验证。

新增一份本批验证记录，分列：静态门禁、集成/自动结果、实时操作、用户人工认可、Git 状态与残留资源。交付差异同时标明第一批继承清单和本批新增修改；保留源树和受保护库指纹。人工窗口交付时明确是否仍驻留；仅处理本次明确目标，不清旧日志/备份/预览。

完成本小步只表示点击、抚摸及实际回归范围通过；不启动自主陪伴、真实上下岛或 Phase 2，不把第一批人工认可复用成本批认可。出现身体轨迹改变、认可眼帽被改、旧输入残留、捕获不释放或普通入口不可恢复时，停止交付并定位，不通过换 DLL、删测试或自动重建场景掩盖。

## 7. 实施状态（2026-09-18）

当前实施树为 `codex/light-interaction` / `72199ef93febaec88280fb2bd3c27c2fe594f3ca`。L0 只读保全并导入第一批 33 个必要变更文件，清单、字节副本、库指纹与基线测试保存在 `artifacts/light-interaction/implementation-20260917/baseline/`；源树保持原状。

L1 输入判定、L2 短反馈、L3 Window 和 L4 WorkerW 三入口、帮助文字均已实现。09-18 发现 runtime/Window/反馈测试与先前记录不一致，用户确认按方案恢复后，从先前工具补丁记录恢复缺失内容，再完成 L4。恢复后使用新的 `artifacts/light-interaction/recovery-20260918/` 证据，不复用旧通过日志冒充当前结果。

L5：32 项本批受影响测试通过，Release solution build 0 warnings/errors；完整回归与格式检查见验证记录。09-18 用户授权结束旧试运行实例后，精确结束旧 PID 10584，已从项目最终输出启动普通 Product 候选 PID 6008，核对窗口及加载模块并保留驻留。用户随后回复“没问题，验收通过”，本批点击与抚摸整体人工验收通过；Window/Wallpaper 全矩阵、第二 DPI、故障专项与长测仍未逐项验证。用户随后授权整理并本地提交：继承的第一批以 `59a77d7` 保存，本批增量与验收记录随当前提交保存。未合并、推送、部署、打包或清理历史成果；不启动下一批功能。
