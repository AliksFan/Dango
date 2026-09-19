# 第二批第二小步：安静待机实施计划

日期：2026-09-18。依据已获用户确认的[自然眨眼与偶尔张望设计](2026-09-18-quiet-idle-design.md)。**Q0–Q4 已实施，Q5 已完成自动回归及项目候选交付，用户确认整体人工验收通过；专项未验证项和历史门禁见[验证记录](../verification/2026-09-18-quiet-idle.md)，不宣称全门禁完成。不增加第三个功能，不改变已确认参数。**

## 1. 起点、隔离与保护

本轮复核：工作树 `e0ac/Dango`，分支 `codex/light-interaction`，HEAD `85d239eb626d37c8746125c18fb151774cb5d26c`；开始时只有本批设计文档未跟踪。`59a77d7` 与 `85d239e` 已完整保存第一批正式入口和第二批第一小步，不再从旧 dirty 源树搬运内容。

实施获准后，在当前工作树从上述 HEAD 创建 `codex/quiet-idle`，保留这两份规划文档；先确认分支是否已有、是否被其他工作树占用，不能覆盖未知分支或修改。无并行代码任务时无需另建一份目录；存在并行写入时再使用独立工作树。一个实现者负责共享 runtime、附件和输入生命周期，不并行修改这条链路。

保护范围：旧 `desktop-entry-integration`、`p1-004-box2d-soft-body`、历史实验/预览/证据、所有 native/DLL 和已验收物理参数不动。旧生产库与统一模块 SHA-256 应分别为：

- `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`
- `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`

使用全新 `artifacts/quiet-idle/<run-name>/` 保存日志、TRX、指纹和验证输出；不覆盖上一批结果。已有 native 测试夹具只读复用，缺失时定位来源，不自动重建或替换。

## 2. 实施顺序与门禁

下表步骤编号 Q0–Q5 为实施增量；设计中的 Q1–Q10 为验收检查编号，二者分开引用。

| 步骤 | 修改边界与工作 | 必须获得的结果 |
|---|---|---|
| Q0 起点验证 | 分支/状态/库指纹；隔离 Release solution build、现有测试；新增真实静置/落地序列的只读稳定资格测试 | 复核上一批失败集合；真实 `IsGrounded`、核心速度≤0.15 的连续 500 ms 可达，不靠硬设稳定。新增异常先定位，不改阈值、DLL 或测试预期掩盖 |
| Q1 纯调度 | 建议新增 `Rendering/Dango/QuietIdleController.cs` 及同域测试，接收成功步、稳定/手动/可见条件、实体代际和可注入随机源 | 先红后绿覆盖设计 Q1/Q4：两计时器、端点、同时到期、500 ms 间隔、拒绝后重抽、无排队、无每帧随机、旧代际无效 |
| Q2 附件与 runtime | `AttachmentSceneController.cs`、`StackedDangoSceneRuntime.cs`；新增真实帧测试 | 每实体独立低优先级 token/序号；双眼眨眼、±0.4 张望、1 秒后仅释放自主 Gaze；10/20/40 ms 步长下满足设计 Q2/Q3/Q6/Q7/Q8 |
| Q3 手动接管 | `LightInteractionSession.cs`、runtime 手动反馈/方向/拖动边界，补现有 session 测试 | 有效 Down 立即取消自主动作并登记占用；正常 Up/取消/捕获失败均幂等释放；手动冷却不变；旧会话不能解锁新会话；真实 Begin→InputCancelled→重捕获仍通过 |
| Q4 两宿主与帮助 | Window、WorkerW 现有宿主和必要的协调器窄接线；控制中心帮助一句 | 可见后台待机、前台边沿取消一次、不可见停止；四条手动入口一致；Review 禁用；错误和显式恢复入口可用。设计 Q5/Q8/Q9 的编译后行为测试通过 |
| Q5 全量回归与交付 | 最终源码构建/测试、格式差异、保护指纹、专属验证记录；普通项目输出 | 原 32 项交互用例及新用例通过；全套无新增失败；真实两宿主与用户人工验收分别记录，未做专项保留未验证 |

Q1–Q4 可先写具体失败断言再实现，不将“缺类型编译失败”当作唯一红灯证据。保持每步可复验；出现稳定条件不可达、跳跃眼动被压住、手动命令遭自主占用等问题时先定位，不带问题进入窗口交付。

## 3. 三个窄接口的责任

### 调度器

只管理每实体的稳定资格、倒计时、自主动作/期限和代际。输入为已成功的固定步、物理核心样本和有限状态；输出为最多一个动作请求或释放决定。它不调用 native、不操作鼠标捕获、不读真实窗口内容、不保存轨迹，也不驱动额外物理步。

动作接纳/拒绝必须回报调度器，以正确处理“提交 Busy 也重新抽取等待”。间隔抽样仅发生在初始化、重排或到期接纳/丢弃时；方向只在准备提交张望时抽取。测试使用确定序列并断言调用次数，生产无需统计日志或持久化随机状态。

### runtime 与附件

沿用 UI owner thread。`AttachmentSceneController` 在实体创建/重置代际时注册自主 token，调用现有 `RegisterAction`，独立于原 preview token。Product 才用该入口；不修改 router 的优先级或释放规则。移除/重置时自主身份随 ledger 一起失效。

共享固定步顺序需明确验证：

1. 现有输入及生命周期取消先处理；执行原 `scene.Step`。
2. 成功后处理既有重宿主/布局变化，并同步当前实体身份；物理失败则丢弃本地自主计划，走原故障冻结路径。
3. 用当前成功帧复核稳定条件。正在张望时本步离地/移动，必须在本步附件消费前 Release；不得延迟到下一个宿主 timer。
4. 在消费前处理到期释放/调度与命令提交；调用现有 `attachments.Advance` 一次。首次消费及每个成功步只计时一次，1 秒保持的取整误差最多一步，不能在 before/after 两处各加一次 dt。
5. 处理该实体附件故障及本地状态，输出原表现帧；不因自主功能推进额外物理世界。

候选有效实体/代际和独立反馈冷却的查询保持窄化，不开放整个 ledger。`TryReact` 只在请求身份和原有门禁有效后让自主命令退出，再走原手动提交；旧目标不能借调用清除当前自主动作。自主行为不调用 `CancelLightFeedback` 给自己腾通道。

健康取消必须在附件暂停前 Release 自主占用。Release 被拒绝时锁存本批错误，禁止后续自主计划并沿用显式恢复；必要时在现有诊断/宿主错误汇总中补入该错误，不能只写私有字段导致用户无法发现。不得为此清掉所有眼帽或在失败世界继续提交。

### 会话与宿主

候选占用携带实体、runtime 代际及输入会话身份：开始、结束和取消均匹配身份；同步清输入时先使旧会话失效，再释放捕获。按设计保留 3 DIP 转拖动的完整重入过程，不用“保持原捕获”替换它。

原 `LightInteractionSession` 为四条入口共用，优先在此统一通知。proxy/overlay 的正常 Up 与异常 Cancel 已分离，除非发现覆盖缺口，不再额外重写 native 窗口或命中几何。

只有当前活动宿主提供自主行为的可见/允许交互状态。已有 `DesktopHostCoordinator` 在切换时隐藏旧宿主再配置新宿主；测试必须覆盖隐藏宿主的晚到回调不能重新打开调度，以及切换失败回滚后完整重排。使用明确的活动宿主身份或仅在活动宿主成功 Tick 前刷新状态，不让两个已构造宿主共享一个可随意写的全局布尔值。

Window 复用 Deactivated、可见性、最小化和 DPI 事件。WorkerW 在现有 timer 中保存上次桌面前台判定，变化时取消一次；后台持续期间不重复取消自主计划，也不改变原手动输入资格。Esc 沿用原局部处理；即便扩大到当前自主行为，也只能在宿主拥有相应输入资格时处理，不把用户在其他应用按 Esc 当成全局宠物指令。

## 4. 验证落点与反例

建议新增 `QuietIdleControllerTests`、`QuietIdleRuntimeTests`、`QuietIdleHostTests`，具体文件名可随实现保持简洁。

- **真实稳定资格**：先跑真实单体落地/静置序列，观察是否形成连续 500 ms 稳定；再用真实跳跃和拖放序列证明离地/移动会清除资格。另用虚构样本覆盖恰好 0.15、略大于阈值和缺前帧/非有限值。只记录必要断言和匿名数值，不采集桌面输入。
- **最终眼睛输出**：不能只检查请求 Accepted；左右张望用同一真实稳定物理帧作无动作对照，验证约 2 DIP、1 秒后自然回正。左右眼单独检查；眨眼沿用 130 ms 双眼包络。
- **物理和帽子保持**：相同输入、相同随机序列、相同成功步，比较启用/关闭自主行为的真实物理快照；帽子沿用既有数值容差并说明浮点积分切片原因。张望中跳跃必须检查首个成功步已恢复默认纵向眼动目标与后续真实表现，不能以自然回正慢慢掩盖一帧命令残留。
- **所有权**：真实 router 的两个 token，验证低优先级不能抢手动 Gaze，自动 Blink 不妨碍手动点击；自主旧 Release 不能清理手动 Blink/EyePose/Gaze 或帽子。每代际 token 数固定，不随动作次数增长。
- **捕获与宿主**：沿用上一批编译后 Window/WorkerW 和自有隐藏 HWND 夹具；对候选、未消费、播放中分别注入取消。前台序列 true→false→false 验证只有一次重排，后台成功步可继续等待。不能用源码字符串检查替代状态/帧断言。
- **错误/暂停**：受控 ledger 容量拒绝、附件冻结、物理失败、重复 Stop、隐藏恢复、移除/重置/切宿主旧事件都要覆盖；显式恢复创建新代际后不补播。

旧 `LightFeedbackRuntimeTests`/session 的部分稳定等待测试可能跨过自主触发时间。不要把断言放宽为“任意眼睛动画都可接受”：通过测试可注入的自主配置或确定随机序列固定场景，并保留普通 Product 默认启用自主行为的独立测试。不增加对用户可见的测试模式或命令行开关。

## 5. 构建与最终门禁

Q0 先复核环境，Q1–Q4 跑受影响测试；最后一次行为代码变更后再跑完整门禁。命令类别如下，路径变量在实施时解析到本轮全新证据目录，源码/文档不写个人绝对路径：

```powershell
dotnet build Dango.slnx -c Release --artifacts-path $quietBuildRoot "-p:DangoPhysicsModulePath=$quietModulePath"
dotnet test tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj -c Release --no-build --artifacts-path $quietBuildRoot --results-directory "$quietResultsRoot/desktop" --logger 'trx;LogFileName=desktop.trx'
dotnet test tests/Dango.Core.Tests/Dango.Core.Tests.csproj -c Release --no-build --artifacts-path $quietBuildRoot --results-directory "$quietResultsRoot/core" --logger 'trx;LogFileName=core.trx'
dotnet test tests/Dango.Physics.Tests/Dango.Physics.Tests.csproj -c Release --no-build --artifacts-path $quietBuildRoot --filter 'FullyQualifiedName!~DangoPhysicsStabilityTests' --results-directory "$quietResultsRoot/physics" --logger 'trx;LogFileName=physics.trx'
dotnet format Dango.slnx --verify-no-changes --no-restore
git diff --check
```

基线预期：Core 37/37、Physics 常规 32/32；Desktop 基线 354/355，本批新增用例全部通过，唯一可保留失败为旧 `SharedSelfWorldExportsAreAvailable` 缺 `dango_box2d_self_bodies_create`。格式检查保留已记录 5 个旧文件问题，逐项对比本批是否新增，不整仓自动格式化。保护 DLL/源码指纹一致；长测未执行时明确排除，不能写成通过。

## 6. 从项目交付与本轮状态

Q5 新增 `docs/verification/2026-09-18-quiet-idle.md`，分别记录自动结果、真实窗口、人工反馈、Git 和残留进程。自动结果可审阅后，按设计 §8 从本轮普通 Product 项目输出启动，检查精确路径、PID、实际加载模块和设置归属。旧实例是否退出以届时核对和当前授权为准，不复用上一轮针对旧 PID 的授权结束另一个实例；不新增隔离框架或默认 ZIP。

人工连续观察至少 90 秒，并实际执行操作抢占、跳跃眼动、后台陪伴、隐藏恢复与两宿主测试。第二 DPI/完整故障矩阵缺少条件时保留未验证；上一批验收不自动覆盖新待机行为。用户认可本批后再记录整体人工验收，不等同整个 Phase 1 完成。

**本轮实际完成**：从 `85d239e` 创建 `codex/quiet-idle`；Q0 基线复验、Q1 纯调度、Q2 附件/runtime、Q3 手动接管、Q4 两宿主接线与帮助已实现。Q1/Q2 保留真实断言红灯和修复后的绿灯；最终自动结果与尚未验证的专项见本批记录。用户授权后已结束上一批 PID 6008，启动本批普通 Product 候选 PID 31652；用户随后回复“通过”，确认本批整体人工验收通过。实现、测试和验收记录随本次本地提交保存。未手动改写设置、重建 DLL、合并、推送或部署；专项未验证项和历史门禁保留。
