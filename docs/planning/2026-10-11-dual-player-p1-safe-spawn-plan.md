# 双人 P1：安全出生恢复首轮实施计划

日期：2026-10-11。状态：**用户再次明确“吸收main再继续原目标”，继续P0–P1隔离实施；编码前先冻结本轮合同。** 用户已批准[世界丢失／释放后的安全出生规则](../adr/proposals/2026-10-11-world-loss-safe-spawn.md)。本轮仍不启动P2或正式持久接线，不宣称恢复、P1或正式多人已通过。

## 1. 当前基线与范围

- 分支 `codex/ordinary-room-physics-admission`；先以 `6135c03`吸收本地main `44a8775`，用户提醒后以 `844e944`继续吸收最新main `a4acdeb`。R1源码仍来自 `5a3848e`。
- main实际合入仅涉及 `AGENTS.md`、`docs/desktop-ui-design-v0.1.md`：界面图标／短文字规则，以及子Agent模型选择规则。主Agent保持当前模型，资料扫描子Agent用 `gpt-6-luna`，复杂实现和审查子Agent用 `gpt-6.1-sol`／`high`；没有生产代码变化。
- [R1](../verification/2026-10-11-dual-player-p1-r1.md)的九条呈现轨迹及原行为回归已有隔离证据；Linux、真实窗口／多客户端、恢复接线及人工双机仍未验证，P1 不准入、P2 停止。
- 首轮只做无家具、最多两团子的隔离恢复合同与真实 native 出生验证。使用虚构身份、可控领域事实与时钟；不修改正式 Server、Protocol、Client 或 Desktop 入口，不接家具、球和手套。

当前 `RoomRuntime`、`RoomSession`、`InMemoryRoomSnapshotStore` 与正式后端来源 `237db114cad57270868c6018cf4171a49b3f8773` 中同名文件一致，但仍是匿名样例；内存 store 明确不提供持久化或重启恢复。正式账号／租约／数据库模块位于该来源的 `Rooms/Access`、`Persistence`，当前树未包含；该来源也尚无玩法 `PresenceGeneration`／`RuntimeRevision`。首轮不能把旧样例改名当成正式恢复。

## 2. 恢复分支与状态机

以下状态名为隔离工具内部名称，不增加玩家协议。

| 触发／状态 | 首轮合同 |
|---|---|
| `Live`：普通重连、隐藏、有效租约重绑、同房接管 | 保留原 world、实体与 PlayEpoch；清除失效输入／重建 ready 绑定，不改身体位置、速度或支撑 |
| `PausedRetained`：暂停但 world 健康且在内存 | 唤醒继续原状态；业务时钟与固定步时钟分开，不把停顿时间补成无界步进 |
| 全房合法休眠 | 独立最新领域事实确认控制及观察租约均不再有效，遵守原30秒容错；最终Dispose转换点再次核对owner与事实revision／有效租约，完成释放后才进入 `Released` |
| `Released` 唤醒，或进程重启后确认原 world 丢失 | `ValidateMetadata` → `PrepareCandidate` → `HealthyCommonFrame` → `AwaitReady`；全组成功才发布新 PlayEpoch 和安全出生原因 |
| 当前绑定 ready 成功 | 当前身份、租约、控制／成员代际、epoch、版本与挑战重新核对后开放输入；观察 ready 不授予控制 |
| 坏记录、未知版本、授权事实不完整、出生不可行 | `RecoveryBlocked`，保留最后合法记录及原因，无部分身体／部分 epoch 对外发布 |
| native 求解故障 | `FaultLatched`，保留最后安全帧并停止；不能转入自动安全出生 |

350 ms watchdog 仅清持续意图，不能释放 world，也不能清除物理惯性。UI 失焦、渲染订阅归零、单次传输断线均不构成释放事实。30 秒边界由领域 fixture 提供租约到期与容错判断，不能在物理 owner 中再凭断线时间猜一套资格；精确等号行为沿用正式基线并在合同中冻结。

新 PlayEpoch 由恢复 owner 创建，客户端不得选择。创建候选时可以持有内部待发布 epoch，失败不得将其作为当前就绪 epoch。旧输入、排队数据、旧 ready 和旧健康帧在新绑定前全部失效；恢复原因必须与首个可发布共同帧同属一次转换。首轮只验证结构化原因存在且正确，实际中文提示与用户可见性留给 P4／真实窗口验收。

## 3. 元数据与所有权合同

拟新增版本化 `RecoveryRecord`，**不保存身体位置、节点、速度、支撑、持续输入或 solver 内部状态**。原 `ExportCheckpoint`／`ImportCheckpoint` 和 complete-checkpoint capability 继续 unsupported；公开 pose 冷恢复与无限历史重放的失败证据保留。

| 数据 | 候选内容与校验 |
|---|---|
| 记录身份与顺序 | RecordVersion、RoomId、RuntimeRevision、RecordRevision、来源 PlayEpoch／最后成功 Tick；整数合法、有界、无溢出 |
| 配置 | 模板／GeometryId 与版本、模拟版本、参数 hash；只能选服务端已知配置，不接受客户端出生坐标或配置 |
| 成员 | 最多两项 PetId＋PresenceGeneration，身份唯一、代际合法；native entityId 只属于当前 world 的映射 |
| 编码 | 规范字段顺序与完整性校验；总字节上限沿用 64 KiB，未知版本／额外非法字段／截断／非有限数拒绝，不自动修复 |
| 最新领域事实 | 与记录分开输入的不可变 RoomId、当前合法成员及其 PresenceGeneration、当前所有权、控制／观察租约与事实 revision；缺失与“确实无记录”分开 |
| 运行处置事实 | 区分可安全重建的已确认world丢失、正常释放、故障锁存与未知；故障或未知不可因进程重启转成自动出生。首轮验证可信虚构事实，正式故障事实持久化／原子性留P3 |

完整性校验只检测记录损坏，不充当业务授权。恢复成员取最新合法事实；按 RoomId＋PetId＋PresenceGeneration 对照记录，旧成员／旧代际不得复活。确属新成员或真正重入的当前合法成员可按现行出生规则创建；坏记录不能用“所有成员均不匹配”绕过校验。人数超出两体先拒绝，不能截断成两人。零成员只保留合法领域状态，不发布一个假可玩空房。

内存 `ConditionalRecoveryStoreFixture` 仅用于合同验证：claim 原子递增 RuntimeRevision 并捕获最后合法成功记录；新记录成功前旧记录保留。写入同时核对 owner、epoch、RecordRevision 和同 epoch 内单调 Tick；新 epoch 的 Tick 可重新起算，不能直接跨 epoch 比较 Tick。模拟旧 writer、倒序完成、失败与取消，失败不改变最后成功记录。真实事务、持久 claim、并发数据库连接与故障恢复仍属 P3，不能以该 fixture 证明。

候选建好到发布之间，重新核对最新领域事实 revision／成员与当前所有权；有变更则拒绝发布、释放候选，等待显式的新尝试。首轮不自动循环重试或无限补偿，不把旧授权快照封存到一次长时间恢复中。

## 4. 出生规划与 native 边界

沿用 [P0 坐标合同](2026-10-11-dual-player-p0-contract.md)与原参数，首批出生 anchor 为 `(-1.4, 0)`／`(1.4, 0)`，native 左右界 `-13.2`／`13.2`；逻辑映射 `x = 1080 + 80 × nativeCoreX`、`height = 60 × (nativeCoreY + .72)`。首轮不新增可调出生点，成员按规范 PetId 顺序稳定分配已有槽位；单体使用首槽。故意缩小合法出生区只作为拒绝夹具。

原 Add 的实际初始 coreY 为 `anchorY + DropHeight`，当前 DropHeight 为 `.42 p`；不能把 anchor 当 core，也不能使用旧归属位置 `0,0` 猜出生。全组先按原径向上界及节点碰撞半径构造保守占用包络，检查有限值、落在批准出生区／左右界／地面以上、容量和互不重叠。具体包络公式及浮点边界在 S0 冻结，不调团子尺寸、弹性、重力或旧容差让出生通过。

唯一 owner 线程按 `Create(capacity: 2)` → `EnableAuthority` → `SetHorizontalBounds` → 整组 `Add` → 读取 Tick0 authority frame → neutral 固定步 → 全组验证执行。现有接口足够，本轮不增加 native ABI。Add 对无家具双体未检查成对出生重叠或左右出生越界，因此 Add 成功不能代替出生门禁。

“零速度／零支撑”指创建时没有继承旧 world 的运动、冷却、支撑和输入：Tick0 authority 的 core velocity 为零、SupportEntityId 为空；节点由原 native 初始化创建为零速度，必要时用独立 probe 通过 Box2D 公开 getter 核对，不增加生产导出。首个健康 neutral 步自然产生重力速度，不清零该步速度，不将其误判为恢复失败。全组健康帧必须同 Tick、同 scene revision、身份齐全、finite、无 native error，满足原面积／最大速度／交叉／锐刺和接触／边界门。

候选未通过前不发布共同帧、就绪或新绑定。第二个 Add、配置、步进、整组验证或提交前复核失败均由同一 owner 释放整个候选，保留合法恢复记录；不得保留第一个已成功身体作为可玩结果。world 已丢失时不得伪造旧句柄存活；world 健康时不得调用 Reset／Create／Translate 掩盖恢复错误。

存活world的成员变动另测：移除旧生命周期并清旧支撑，幸存身体不重建；新合法成员先用当前已取得的raw帧找可用安全槽，空间预检失败保持当前健康world，不能挪走幸存团子或额外Step读位置。native Add后的publication失败可能已修改world并锁存，不承诺事务回滚，须进入FaultLatched并保留最后安全发布帧，禁止Reset掩盖；不复活已移除的旧生命周期。真正重入使用新代际；同房接管仍用旧代际和原身体。丢失world则当前合法成员整组重新出生。

## 5. 可交付增量与文件 owner

使用现有隔离工作树，根负责合同、工具集成与报告；子任务可只读审查，若并行写代码必须另行分配独立文件与工作树。候选文件名可在合同冻结时调整，职责不扩展。

| 增量 | 目标文件／模块 | 预期交付与验证 | 依赖／停止点 |
|---|---|---|---|
| S0 冻结合同 | 新 `docs/planning/*p1-safe-spawn-contract.md` | 固定 record schema、状态／原因、槽位包络、预算、失败夹具、源码与 binary hash；在编码前本地提交 | 只复用已批准产品规则；发现必须改变规则或 ABI 时停止相应实现 |
| S1 纯恢复语义 | 新 `eng/tools/Dango.SafeSpawnAdmission/` 下 Contracts、RecoveryStateMachine、RecordValidator、ConditionalRecoveryStoreFixture；独立测试 | 可控事实／时钟覆盖健康保留、实际丢失、30 秒容错、成员代际、owner／epoch／顺序 fence；无 native、网络或 DB 依赖的部分先失败再实现 | fixture 不冒充正式权限或持久化；不改旧匿名 Server |
| S2 出生与 owner | 同工具 SpawnPlanner、NativeRecoveryOwner；引用已有 `Dango.Physics` | 真实候选 DLL 全组出生／阻止半成功；Tick0 与健康帧分别校验；创建／读取／释放同线程，取消／异常回收 | 复用 R1 候选及原 API／参数；任何正常门失败保留证据并停止扩展 |
| S3 组合与反例 | 同工具 Program、ScenarioVerifier／Mutations；新恢复测试文件于现有 `tests/Dango.Physics.Tests/` | 真实进程丢失 world 后从虚构 record 重建；健康 world 与不中断对照；至少各类破坏变体外部 verifier 判红 | 跨进程只传虚构元数据和证据文件，不传物理 checkpoint，不启动正式服务 |
| S4 收口 | 工具 README／调用脚本、新 `docs/verification/*p1-safe-spawn.md`，必要计划状态同步 | 保存正常与预期失败日志、manifest、source／DLL／参数 hash、进程退出及 native allocation；必要原能力回归；本地提交 | 只得出隔离恢复结果；Linux、UI／多客户端和人工仍分别开放，P2 不自动启动 |

工具使用 .NET10 与现有测试依赖，不新增第三方依赖，不进入生产启动链。通过独立 csproj 构建；如 tests 引用工具程序集，其可测语义与入口分开，不能为了测试把工具接入正式 Server。证据使用新 ignored `artifacts/p1-safe-spawn/`，不覆盖旧 manifest 或受保护候选。

## 6. 能失败的验收矩阵

冻结后所有正常项必须完整通过、零跳过；错误变体由独立 verifier 判失败并实际进程 exit1，不能只断言候选返回某个状态字符串。

| 门禁 | 正确结果／外部可观测证据 | 必须识别的错误实现 |
|---|---|---|
| 健康 world 连续性 | 推挤／堆叠／空中三夹具，重连、接管及内存暂停前后保持world、实体、epoch；两边施加相同有效输入（包括清旧意图），未来至少120固定步raw／semantic完整状态逐位一致，暂停墙钟不补步 | 接管／重连偷偷Reset、Create或传送；Stop瞬时清物理速度 |
| 实际丢失／合法释放 | 两个不同触发原因；旧 world 实际 Dispose／原进程退出，新 world 的 epoch 不同、旧输入／ready 失效，当前成员全组安全出生 | 把仍存活 world 当丢失；沿用旧 epoch、重放旧 Jump、伪造重启 |
| watchdog 与业务容错 | 可控clock在350ms及30秒边界前／后，观察租约仍有效、隐藏、无订阅和旧连接迟到断开均不误释放；资格检查后新observer有效、Dispose前复核阻止旧释放决定 | 350ms超时或bare disconnect触发Dispose；忽略观察设备／释放前事实变更 |
| 元数据与成员 | 无记录且确认首次可初始化；坏／截断／超限／未知版本阻止就绪；同 Pet旧代际／错房记录不复活，当前新成员合法出生 | checksum失败当无记录；仅按 PetId匹配；超人数截断；缺授权当空房 |
| claim／条件写入 | 旧writer、同epoch旧Tick／revision、旧epoch与写失败均拒绝；每次核对最后合法记录未被清空 | 所有权递增先删记录、按异步完成顺序覆盖、跨epoch误比较Tick |
| 发布前竞态 | 候选成功后领域成员／所有权变更则拒绝发布并释放；重新尝试须消费新事实 | 只在恢复开始时验权限；候选用旧成员就绪 |
| 出生与整组原子性 | 一／两成员、固定槽位，出生包络合法；第二体失败、槽位重叠、区不足、越界、非有限值、native错误均零部分发布 | Add成功即ready；绕过出生检查；只发布已成功第一体 |
| 初始状态与共同帧 | Tick0零继承；首个健康neutral帧允许真实重力速度；同Tick／scene、完整成员后才AwaitReady | 把旧速度／支撑复制入新体；强清首步速度；拼接两次帧 |
| 存活成员变更 | 撤下层后幸存实体保留并自然落下；重入新代际，新成员无空间明确失败而幸存身体不变 | 为添加而重置全房；复用旧生命周期；挪走幸存团子 |
| ready／恢复原因 | 新epoch附正确丢失／释放原因，当前挑战与健康帧绑定；即使新旧native数值EntityId相同，旧输入／帧／ready仍拒绝，观察设备不获控制 | 没原因就跳位置；仅靠数值ID断开旧绑定；fixture角色直接当授权 |
| 故障锁存 | 真实native失败保留最后安全发布Tick／状态，后续拒绝并不建新world；故障后进程退出，可信事实仍为fault／unknown时冷启动拒绝自动出生 | 失败自动重生，或重启绕过已知故障；将保留安全帧误称失败物理步回滚 |
| 资源与界限 | 生命周期、第二体构建失败、提交前取消／异常均同owner释放；native allocation回到开始值；所属进程全部退出 | 跨线程Dispose、候选泄漏、有界队列变无界 |

出生后的真实 native 轨迹按60 Hz至少600步完整验证一／两成员，neutral 后再用既有移动／跳跃／支撑夹具核对物理仍可用。保持原 `.031` 侵入比、`.008 p` 支撑正gap及原身体安全门，不把出生成功推广到家具或更大容量。原无变化轨迹、checkpoint unsupported、authority与owner门分别跑受影响回归；仅当新增代码／失败说明有影响时重跑昂贵R1 native全套。

建议沿用旧恢复预算的上限作为本轮更保守起点：元数据不超过64 KiB，预编译工具内“校验→候选创建→首个健康帧”实测不超过5秒，构建时间另列。历史长度不参与恢复输入；从不同长运行前缀丢失world得到同配置／成员时，同一新出生轨迹与新建参考一致。实测报告总耗时与native步数，不以模拟时钟代替墙钟；失败时不加预算、不改参数，给出具体瓶颈后停止。该预算将在S0冻结，不是本轮已经通过的结果。

## 7. 后续接线与准入

S0–S4通过只能证明隔离恢复语义及 Windows native 出生，不证明正式事务、真实账号授权、服务重启恢复或用户看到正确提示。P1仍需Linux能力和真实语义呈现等原开放门禁，再明确复核P2准入。

P2／P3之前重新选择并审查正式后端联合基线；PresenceGeneration的所有正式生命周期路径、RuntimeRevision claim／RecordRevision条件写入需要前向迁移与精确数据计划。正式 sleep worker、Hub attach／ready／逐次出站资格及账号绑定再接入；P4实现实际提示，P5／P6分别做正式同机与双机恢复。旧B5C预算不自动续用。

本轮不启动服务、Docker／WSL或Linux资源，不创建数据库／对象存储，不操作真实账号／存档，不占用前台观察窗口；不将当前分支合回main、推送、安装／部署或替换日常DLL。

## 8. 本轮已完成的核对

main→当前分支两次普通合并 `6135c03`／`844e944`均无冲突；最新本地main `a4acdeb`为HEAD祖先，合入差异只有前述两份规范。第二次合入保留本轮既有文档修改；没有将物理候选合入main。

核对受保护产物均匹配此前合同：A DLL `4E2B92B5CC73A6C586A7A9F8EFE96F03187AECBAC6A5975E34BFBC9BB7E32A68`；authority DLL `C7D3140079BEFD29E3E8512B5CF69376D4B342AD583A7C458EB89420A72FE07A`；日常DLL `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`；B replay EXE `B4707336E379A234F2C68B29B186342F253295AA2971475F5A0D22B29003A750`；R1候选DLL `59EC861EE748BA93E661D058EEBD01D3E8A04ABF9D24F0376BACB4D98015B6DC`。

计划准备阶段完成六份文档121个相对链接及diff静态检查；尚未执行恢复测试、物理构建、真实窗口或服务恢复。用户随后要求继续原目标，下一步按本轮冻结合同实施，最终验证以新报告的实际输出为准。
