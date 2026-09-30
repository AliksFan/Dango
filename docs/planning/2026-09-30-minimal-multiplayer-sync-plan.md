# Phase 2A 最小多人同步实施计划

> 状态：用户已授权 M0–M6，实现和同机真实双 WPF 验证已完成；目标自动门禁已运行，历史 DLL 失败与缺失的比较产物已单独记录；用户已确认同机小样人工验收通过，M7 双机及其人工确认待安排。实际计数和交付状态以[实施记录](../verification/2026-09-30-minimal-multiplayer-sync.md)为准。
>
> 日期：2026-09-30
>
> 依据：用户要求“据此制定具体实施计划”；沿用 [Phase 2A 详细设计](2026-09-24-minimal-multiplayer-sync-design.md)，本轮完成任务拆分和验收定义。

## 1. 交付目标与当前起点

完成一套本地真实 ASP.NET Core/SignalR 服务、一个内存房间和两份真实 Windows 客户端。两个虚构参与者分别控制自己的团子，支持左移、右移、停止、非接触招呼，以及完整快照、乱序处理、重连和服务重启后的明确重置。

本批的可见成果是可从项目启动的隔离多人小样。先在一台电脑运行服务和两个客户端；第二台电脑及局域网配置到双机人工验收时再安排。本批不含拖动、贴贴、共享碰撞、数据库、手机客户端、容量认证或日常安装交付，完整 Phase 2 的剩余门禁继续保留。

2026-09-30 只读复核结果：

- 当前 Worktree 为独立 detached HEAD，基线 `89bacb56f46d366bf30d4634899eae4d8aa3bb3d`，与本地 `main` 同点。
- 当前已有修改是上轮设计稿和 README 导航；没有 Server、Protocol 或网络客户端实现。
- 另一外观 Worktree 仍有未提交修改，涉及 `Dango.slnx`、产品/阶段文档和桌面渲染文件。它们不是本批实现依赖，不能整体复制或覆盖。
- SDK 由 `global.json` 锁定到 .NET 10.0.300 feature band；现有测试采用 MSTest。本次只读取配置，没有据此声称本机 SDK、native DLL 或测试可用。
- `DangoPhysicsWorld` 当前 ABI 只接收方向、跳跃、下压和拖动输入，没有“把共享锚点设为服务端坐标”的现成方法。`DesktopPhysicsScene.Step` 使用本地 `CoreX/CoreY` 计算显示边界，不能原样当网络模式的位置来源。

以上是制定计划时的只读起点；随后用户授权在新任务中实施。实现结果与文件范围见实施记录，原有外观修改未引入本任务。

## 2. 实现模块与文件所有权

下列路径定义各模块所有权；目前已按此建立实现，运行方式见实施记录。

| 目标 | 文件或目录 | 职责与限制 |
| --- | --- | --- |
| 协议 | 新增 `src/Dango.Protocol/` | `net10.0`；DTO、稳定枚举、校验、移动/动作时间采样约定及黄金 JSON；不引用 WPF、Physics、Server 或 SignalR 类型 |
| 服务端 | 新增 `src/Dango.Server/` | Web SDK；`Sessions/`、`Rooms/`、`Realtime/`、`Storage/`、`Diagnostics/` 内部模块；一个应用实例，无 EF/数据库包 |
| 客户端同步 | 新增 `src/Dango.Client.Sync/` | `net10.0`；`RoomReplica`、`CommandDispatcher`、`RoomConnection`、`ServerClockEstimator`；传输适配与状态归约分别可测，不引用 WPF/native |
| 联网桌面表现 | 新增 `src/Dango.Desktop/Multiplayer/` | `NetworkScenePresenter`、`NetworkInputRouter`、`NetworkPetPresentation`、`WavePresentationSampler`；把规范锚点映射到 WPF，复用身体/眼帽绘制和 authored fallback |
| 真实桌面入口 | 新增 `eng/tools/Dango.MultiplayerSample/` | 独立 WPF Window 小样，客户端 A/B 和本地会话启动器；独立设置、实例标识、连接状态和退出入口 |
| 验证 | 新增 `tests/Dango.Protocol.Tests/`、`tests/Dango.Server.Tests/`、`tests/Dango.Client.Sync.Tests/`；扩展 `tests/Dango.Desktop.Tests/Multiplayer/` | 协议、纯运行时、真实 Kestrel/SignalR、Replica、真实 native/WPF 和隔离测试分别记录 |
| 脚本与说明 | 新增 `eng/run-multiplayer-review.ps1`、实施期验证记录 | 解析输出和启动隔离会话，记录本批进程/端口；不把票据放进命令行或日志 |
| 接线 | `Dango.slnx`、Desktop 与对应测试 `.csproj` | 单一集成人修改；新增引用与资源，不批量升级已有依赖 |

依赖方向为：Server → Protocol，Client.Sync → Protocol，Desktop → Client.Sync/既有渲染，MultiplayerSample → Desktop。服务端和纯协议测试不能依赖 Windows 桌面运行时或 native DLL。

现有 `StackedDangoSceneRuntime`、`DesktopPhysicsScene` 和日常 `App.xaml.cs` 默认保持本地路径。联网小样单独组合状态源和表现适配，不强行把全产品宿主改成网络框架。首批验收采用两个普通 Window；Wallpaper 联网整合留作后续宿主增量，不把 Window 小样称为日常入口已联网。

实施开始时按工作流先确认或创建 `phase/2-shared-room-sync` 阶段分支，再从核实的阶段基线建立任务分支 `codex/p2a-minimal-multiplayer-sync`。先确认分支未被其他 Worktree 占用，承接并保留现有设计/计划修改。若并行编码，Server、Client.Sync 可在协议稳定后分开 Worktree；Protocol、解决方案和桌面接线各只有一个写入负责人，按“Protocol → Server/Client → Desktop”顺序集成。

## 3. 实施前明确的协议细节

以下是设计既定边界内的实现细化，不增加新的用户功能。协议黄金样例、文档和测试必须同时采用这些规则。

### 3.1 身份、订阅和加入竞态

- `joinTicket`、`resumeToken` 是临时 bearer 凭证，保持原设计的 epoch 内稳定规则。`clientInstanceId` 只是恢复绑定信息，不能抵御真实凭证泄露；越权测试使用错误/他人身份字段和无效凭证，不能声称“窃取有效凭证仍无法冒用”。
- Join/Resume 使用身份级异步互斥，覆盖预备绑定、加入接收组、房间队列内确认代际/取得快照、返回与失败撤销。不同身份互不持有对方的锁，RoomRuntime 不等待 Hub 网络调用。
- 房间队列内的代际登记是切换生效点；公开事件只发送给登记中的当前连接。失败撤销、旧 `OnDisconnected` 和延迟移组都携带代际，不能清掉已成功的新会话。
- 同一传输连接重复 Join/Resume 保持代际；新的传输连接替换旧连接。客户端串行执行连接恢复，并为每次连接尝试和同步请求分配本地 fence，旧回调一律失效。
- Join/Resume 的快照和 `nextCommandOrdinal` 在同一房间队列步骤读取；响应中的两者不能来自不同时间点。

### 3.2 命令生命周期、重试与旧意图

- 用户输入先进入未编号的本地意图槽；同方向重复输入合并。只有准备发送时才分配 `commandId` 和连续 `commandOrdinal`，客户端正常路径最多一个未完成调用，避免队列中大量过时按键意图。
- 服务端仍按“身份/epoch/代际 → 幂等 → ordinal → 业务裁定”顺序处理。业务接受、no-op 和业务拒绝均消费 ordinal 并缓存结果；网络包格式、身份或代际检查失败不消费。结果明确保留原接受/拒绝结论，重复的原拒绝不能被解释成成功。
- 资源准入先于 ordinal 消费：预留命令结果槽和必要事件槽失败时，只返回 `rate_limited`（带 retryAfter），不消费 ordinal。`CommandResult` 补充 `ordinalConsumed`、`nextCommandOrdinal` 和重复结果的 `originalOutcome`，让客户端能区分“未进入裁定”与“已裁定拒绝”；旧缓存结果中的 next 只作历史信息，恢复以当前 Resume 屏障为准。
- 断线立即清空未发送意图、停止续约并清除按键保持状态。成功 Resume 后，先用返回的 `nextCommandOrdinal` 处理可能已发送命令，再开放新的用户输入。
- 若 pending 的 ordinal 小于服务端 next，说明它已被消费：用原 ID/ordinal 查询式重试取得原回执，不能执行第二次。若 pending 的 ordinal 等于 next，说明在新代际生效及快照屏障前未消费：放弃旧意图，下一次新的用户动作使用服务端 next 和新的 ID。旧传输的迟到调用因旧代际被拒绝。
- pending 大于 next 属于本地账本错误，重新同步并停止发送；不能猜测跳号。客户端重启丢失本地 pending 时，只从服务端 next 继续，不追发历史按键。
- 缓存保留期内的重复返回原结果；过期旧 ordinal 返回 `resync_required` 且不再执行。高水位在 epoch 内不回退；过期命令不得换用新 ordinal“恢复执行”。幂等承诺限定在明示的重试窗口内。

### 3.3 快照和最后一条事件

- 同一连接代际也可能有迟到快照，因此增加本地 `syncAttemptId`。最多一个生效中的快照请求；仅当前 attempt、当前 epoch 且 `lastSequence` 不低于请求前已应用水位的结果可原子替换 Replica。
- `10,12,11` 到达后进入 Resyncing，12 不得先应用；应用快照后丢弃已覆盖事件并只消费连续后缀。未知事件类型、重复序号但内容/ID 矛盾属于协议错误，不能静默吞掉。
- `GetRoomTime` 返回只读 `TimeSample { protocolVersion, roomId, roomEpoch, serverTimeMs, lastSequence }`，时间和水位在房间队列中一起读取。两个客户端都低频检查水位；即使最后一条广播丢失、没有下一条制造 gap，也能发现自身陈旧并取快照。
- 发命令的一端还会核对 `eventSequence`：在回执等待上限内未收到事件或覆盖它的快照，立即取快照。只依靠发送方的回执不能证明另一端也收到事件。
- outbound 队列在新命令准入时预留可用容量；如果不能保留确认事件，就拒绝本次新写入且不消费 ordinal，不制造状态已改而事件未登记的窗口。发送中途失败则使受影响连接重新同步；后续低频水位检查是最终收敛保障。

### 3.4 时间、移动和动作

- 客户端记录单调发送/接收时间 t0/t1，以 `(t0+t1)/2` 对应返回的服务端时间估算偏移；保留最近有效样本，优先最低 RTT。高 RTT 样本不改变时钟，墙钟不参与计算。
- 每次显示采样时间在同一 epoch 内不倒退；小偏差有界校正，过大偏差或系统长时间挂起后先重同步再显示，禁止高速补算旧帧。新 epoch 重建时钟与动作时间线。
- 断线策略选择原设计中的“当前已确认有限移动段自然结束，禁止续约”，不另外生成立即停止事件；旧断线回调不能影响新代际的续约。
- 左右移动使用服务端解析段，方向切换/Stop 都先采样旧段到当前服务端时刻。到期、边界和招呼结束属于已确认时间函数，不单独增加事件序号。
- 招呼首批采用一个无接触、无肢体的短促眼部/点头表现，命名 `wave`；不新增手脚或移动落点。正常表现与降低动画/native fallback 都消费同一 actionId、开始/结束时间和种子，晚加入只显示剩余段。

## 4. 初始参数与冻结办法

下表是便于写测试和原型调试的**候选起始值**，不是已实测结论。M0 记录配置版本；M3/M6 实测后在同一配置/黄金样例/测试中同步冻结。语义不变量不随数值调节放宽。

| 参数 | 候选值 | 断言或调整依据 |
| --- | --- | --- |
| 规范房间 | `[0,1]²`；x 可行走区 `[0.08,0.92]`，groundY `0.12`；初始 x `0.30/0.70` | 不含本机尺寸；两只团子按稳定 ID 绘制，交叉允许重叠、不产生接触结果 |
| 移速/有限段 | `0.12` 房间单位/秒；最长 `800 ms` | 终点受边界限制；无续约时最迟段结束即停止 |
| 续约 | 持续输入时每 `250 ms` 最多一次，方向改变/Stop 立即排入 | 对系统键盘重复事件去重；Stop 不排在积压移动之后 |
| 招呼 | `1200 ms`，固定一个表现版本 | 起止状态/参与者一致；动作期拒绝移动，Stop 为 no-op |
| 每身份业务限频 | token bucket `10/s`、burst `20` | 足够正常续约和 Stop；新业务拒绝也记录 ordinal；认证前另设有界连接/报文限制 |
| 有界资源 | 命令报文 `4 KiB`；每身份待处理 `64`、房间队列 `128`、outbound `256`、客户端事件 buffer `64` | 超限稳定拒绝/重新同步；不可无限等待或扩容内存 |
| 重试与幂等 | 同一逻辑调用重试最多 `30 s`；结果保留至少 `120 s`、每身份 `2048` 条 | 未过期记录不能为新写入提前淘汰；空间不足拒绝新命令；过期旧 ordinal 不执行 |
| 时间与水位 | 加入时 `5` 次采样，之后每 `2 s`；有效 RTT 上限 `500 ms` | 队列/网络不对称会降低估算精度，真实延迟单独测量；无有效答复 `6 s` 转离线/重同步 |
| RPC/恢复 | 单次超时 `2 s`；恢复退避 `0.5/1/2/5 s`，最大 `5 s` | 超时结果未知，不当作服务端拒绝；用原命令 ID 重试或 Resume 屏障判断 |
| 回执对应事件 | 最多等 `1 s` | 未覆盖即取快照；被动客户端靠水位检查 |
| 比较与校正 | 规范锚点数学测试误差 `≤1e-6`；小修正 `≤150 ms` 收敛 | 纠偏仅改变呈现，Replica 原子更新；差距 `>0.05` 房间单位直接重绑，不拉长追赶 |

初始自动网络条件为 RTT `0/100/300 ms`，叠加受控抖动、指定事件/回执丢弃和断线；单元测试用虚拟时钟，真实进程测试用实际时间。此矩阵只验证两参与者的恢复正确性。不能把候选超时或测试注入值写成真实 P95/公网延迟承诺。

## 5. 任务顺序与逐步完成条件

下表保留任务定义；当前进度以实施记录为准。每步先有可编译的失败用例，再实现使其通过；编译失败、缺 DLL、测试未发现均不算有效红例。

| ID | 目标、范围和交付 | 依赖与验证 | 停止条件 |
| --- | --- | --- | --- |
| M0 基线与适配探针 | 核实 SDK/native 指纹、旧失败、分支/Worktree；建立验证目录；检查官方 SignalR 客户端包兼容与许可证；做最小“真实 native 局部轮廓 + 外部规范锚点”测试探针 | 现有 Core/Physics/Desktop 受影响回归；证明本地 CoreX 改变不会带动网络锚点，fallback 可维持同一动作采样 | 无可用已验收模块、必须改 native ABI 才能实现、或无法保留独立眼帽/真实软体时，停止桌面分支并记录具体缺口；协议工作可继续 |
| M1 协议和失败样例 | 新建 Protocol 与 Protocol.Tests；冻结 DTO/错误码/坐标/TimeSample；移动和动作解析函数；黄金 JSON 和不合法样例 | 依据设计 §7–9 和计划 §3–4；测试拒绝非法值、版本、他人控制字段；结束点解析有独立算式真值 | 协议包含 WPF/Box2D/服务端内部类型，或只能靠反序列化默认值“通过”时不进入 M2 |
| M2 权威内存房间 | 新建 Server 的纯房间模块、会话注册表、单写者队列、幂等/ordinal/限频、有界发件箱、内存快照边界 | M1；虚拟时钟下测试并发、状态冲突、有限移动、缓存过期、提交与事件原子性；此步不需要启动监听服务 | 事件/序号分叉、旧命令重复执行、无界缓存或客户端自报位置能改状态即阻断 |
| M3 真实网络纵切 | 接入 Kestrel/Hub；实现 Client.Sync 最小 Join→快照→移动→Stop→招呼→双端收敛；真实 WebSocket 两连接 | M1/M2；真实服务子进程和两个 SignalR 客户端，不用 TestServer 代替；正常路径中一端只能控制自己的团子 | 无真实网络证据、授权在 Hub 被绕过或只有单连接可用时不进入桌面接线 |
| M4 恢复与异常 | 完成 Replica、CommandDispatcher、时钟、代际/fence、重连、尾事件水位修复、服务重启提示 | M3；乱序、加入竞态、旧断线回调、缓存边界、旧意图、失回执、同代际快照反退和新 epoch 测试 | 旧 Move 恢复后生效、末尾事件永久丢失、快照回退、旧凭证可进入新 epoch 均阻断 |
| M5 桌面联网呈现 | 新建 Desktop/Multiplayer 与 WPF Sample；两只真实团子、所有者输入、服务端时间招呼、每实体 native/fallback；隔离设置/实例 | M0/M4；WPF/native 测试验证锚点不由物理回写、随机散步关闭、独立眼帽和 fallback；焦点/隐藏/退出回归 | 用本地 SelectedEntity 控制远端团子、重复随机行为、保留物理跨团子碰撞或 fallback 失去共享语义即阻断 |
| M6 同机真实验证 | 完成本地会话启动器和 PowerShell 包装入口；从最终构建启动服务+A+B；执行故障矩阵、记录实测、冻结参数 | M5；三进程身份/二进制指纹可核对；两份实际 WPF 客户端同 epoch/序号/动作终点；运行后精确报告残留 | 只测无界面客户端、运行旧 EXE、客户端碰到日常设置/互斥键，或证据含凭证时不开放验收 |
| M7 双机与人工验收 | 到验收时安排第二台 Windows；服务器仍可在 A 同机；执行不同视口/DPI、真实断网/恢复和观感清单 | M6；冻结受控 LAN/WSS、监听范围和凭证交付；用户确认双方确实处于同一房间 | 设备未安排即“待双机验收”；不能据同机成功宣布 Phase 2A 已验收 |

协议 M1 稳定后，M2 服务端与 M4 中不依赖真实网络的 Replica 测试可并行；M3 集成由同一负责人收口。M0 的表现探针可以并行进行；M5 必须等恢复协议通过，避免在窗口中掩盖状态错误。

## 6. 桌面适配与项目入口的具体安排

### 6.1 不把本地物理位置当共享位置

M0 先验证采用当前统一模块的容量 1 世界，每只网络团子拥有独立的本地表现世界和眼帽历史。该模块返回相对 core 的轮廓；网络适配只使用局部轮廓、局部附件和诊断，最终 WPF 边界由 Replica 规范锚点决定。

首轮 native 世界使用中性输入稳定局部软体；行走位移来自服务端移动段，细微步态/招呼由已确认时间线驱动。不给本地 core 施加无限水平驱动，不把零速 native 诊断误当房间停止，也不逐帧重建 world。身体、眼睛和帽子的最终变换必须一致，落地锚点固定；不修改现有物理参数或 DLL。

两只团子的 native 世界相互独立，避免本地接触挤压改变共享语义。它们在规范位置交叉时按稳定 petId 层序显示允许重叠，不伪装成已实现的碰撞/贴贴。单一 world 故障只让该实体切换 authored fallback，不能冻结另一实体或 Replica 的网络时间线。

网络步态不能直接套用当前读取 `CoreX` 位移的单机控制器；新增窄适配从确认移动段计算位移进度，保留贴地、整体新增位移不超过现有 `2 DIP` 上限及眼帽同变换。若现有 helper 不支持注入此输入，新增网络局部采样器，不顺手重构整个单机 runtime。

`AuthoredPoseSampler` 已有安全轮廓回退，但没有现成 `wave` 动作。M5 必须补 `WavePresentationSampler`：按同一动作实例采样短招呼，主表现和 fallback 共用相位，结束回 idle。不能仅在 UI 显示“招呼成功”或在回执到达时各自启动本地动画。

### 6.2 输入与显示隔离

- Sample 使用独立普通 Window，明确显示“客户端 A/B”、连接状态和自己的团子标识；可见按钮为左、右、停、招呼。键盘仅在窗口拥有焦点时使用，不安装全局 hook。
- 输入只能映射为自己的 `setMoveIntent/startWave`，不开放拖动、跳跃、下压、抚摸、选择另一只团子控制或随机自主生活。
- 丢焦点、最小化、关闭或本地隐藏时清除按键/续约并尽力发送 Stop；通信故障仍由有限段兜底。恢复可见后先校验时间和状态，不补算隐藏期间的 native 帧。
- 网络收件和 Replica 继续受各自有界生命周期管理；UI Dispatcher 只获取不可变帧，不让网络回调直接访问 UI/native world。
- A/B 使用各自的虚构设置目录、clientInstanceId 和互斥标识。拒绝已有会话目录、日常设置/安装目录及路径穿越；只保存无凭证的显示设置和诊断。

### 6.3 服务启动、凭证与退出

本地启动器先创建唯一会话目录并记录非敏感 manifest，启动一个 Server 子进程，监听回环动态端口；确认已就绪后再启动 A/B。服务端生成的临时票据通过定向匿名进程管道在启动器和对应客户端间传递，保存在内存，不放入环境变量、命令行、JSON 配置、控制台日志或证据文件。管道异常按未加入处理，不降低为公开取票 API。

同机开发允许回环 WS，必须显式校验实际绑定仍为回环。M7 双机使用受信任 HTTPS/WSS 和明确局域网地址；证书、凭证输入和防火墙配置在验收准备时单独安排，不关闭证书验证。普通局域网不能沿用回环明文模式。

候选 Window 通过自己的关闭/退出入口释放连接和 native；服务端收到本会话的正常停止信号后释放监听。若退出超时，报告精确 PID/端口和异常，不结束其他 Dango 实例。保留无凭证测试证据，不把停止进程当作删除目录或数据库。用户正在观看的小样可继续保留运行，并如实记录。

## 7. 验收矩阵与错误实现反例

表中类别必须有真实发现并执行的测试数，零用例、跳过或环境不可用都不能计为通过。S 类是未来真实运行/人工证据，其余是自动门禁。

| 编号 | 失败型断言 | 实现/测试归属 |
| --- | --- | --- |
| P01 | 非法 JSON、未知命令、伪造 actor/pet、NaN/无穷、缺字段、错误协议/快照版本失败；黄金样例支持未知可选字段 | M1，Protocol.Tests |
| P02 | 已知时间独立计算移动中点、反向起点、Stop 终点、到期 idle；客户端无法指定速度和动作参与者 | M1/M2，Protocol/Server.Tests |
| R01 | A/B 并发各发 100 轮合法意图，有状态变化时连续 sequence、唯一 eventId，最终双方状态与服务器一致 | M2/M3，Server.Tests；专用测试限频配置与正常限频用例分开 |
| R02 | 相同命令不重复执行；同 ID 不同载荷报冲突；ordinal 1 回执丢失、2 已消费后重试 1仍取原结果；原业务拒绝重试仍拒绝 | M2/M4，Server/Client.Sync.Tests |
| R03 | 缓存保留期前/后重试、队列/缓存满、速率超限都不产生重复/未记录事件；另一身份仍可正常提交 | M2/M4，Server.Tests |
| R04 | 错误票据/令牌、已绑定票据被另一实例复用、旧代际调用/旧断线/失败撤销都不能控制或删除新会话 | M2/M4，Server.Tests |
| C01 | 事件先于 Join 快照，S+1 与快照竞态，`10,12,11`、重复11和 buffer 溢出，均不提前应用12或丢连续后缀 | M4，Client.Sync.Tests + 真连接验证 |
| C02 | 同代际快照13先于12返回，旧 attempt/旧连接快照、事件和回执均不能覆盖新状态 | M4，Client.Sync.Tests |
| C03 | 丢最后一条事件且此后静默：发送方凭回执、被动方凭 TimeSample 水位都在配置上限内取快照收敛 | M4，两个真实 SignalR 连接 |
| C04 | 未发送 Move 后断线、离线松键、恢复不得自动移动；已消费 pending 只取原结果；未消费 pending 丢弃但 next ordinal 可继续 | M4，Client.Sync/Server.Tests |
| C05 | 并发 Resume 最终只有当前代际收件；客户端重启不多生团子；服务重启新 epoch、旧凭证失败、新本地票据可入 | M4，Server.Tests 真服务进程 |
| C06 | 两端墙钟偏移/跳变、异常 RTT、挂起恢复不倒播动作；相同服务端采样时刻得到相同规范状态 | M4，虚拟时间及真实时间采样 |
| V01 | 本地 CoreX/帧率/随机种子变化及 native 故障不改 Replica 锚点、动作或 sequence；2 pet 相互无物理推动 | M0/M5，Desktop.Tests 与真实 native |
| V02 | 主表现与 authored fallback 显示同一招呼的剩余时间及结束状态；无新增手脚，身体眼帽一致、贴地和新增位移有界 | M5，WPF 几何/动作测试 + 人工观感 |
| V03 | A/B 单实例和设置隔离；焦点变化/隐藏/退出不持续续约；仅本窗口键盘生效；普通单机入口受影响回归无新增失败 | M5/M6，Desktop.Tests + 实际窗口 |
| S01 | 从同一最终构建启动真实 Server+A+B，核对共享 epoch/序号/pet集合/动作ID/终点，完成重连、尾事件修复和 fallback 观察 | M6，同机真实运行 |
| S02 | 两台 Windows，不同视口/DPI、真实网络中断/恢复，用户确认“同一房间、同一次动作”，退出后进程/端口可核对 | M7，双机实时行为 + 用户验收 |

至少保留六组有效错误实现红例：重复命令生成第二次事件、按到达顺序直接应用12、忽略连接代际、接受反退快照、丢尾事件后没有水位检查、用本地 CoreX 放置网络团子。红例可使用局部测试替身或可回退的最小变异；恢复候选后重跑对应绿例，不留下故障开关或修改历史门禁。

## 8. 验证命令与证据

以下为可用验证命令模板，实际运行结果见实施记录。所有命令从仓库根执行；`$syncRunRoot` 是 `artifacts/multiplayer-sync/<唯一会话>/` 下新建的证据根，`$syncModulePath` 是核对过指纹的统一 native 模块路径。客户端真实进程测试另需将 `DANGO_SYNC_SERVER_PATH` 指向本轮最终构建的 `Dango.Server.exe`；native 测试使用 `DANGO_PHYSICS_MODULE_PATH`。路径由启动器显式校验，不提交开发机绝对路径。

```powershell
$syncBuildRoot = Join-Path $syncRunRoot 'build'
$syncResultsRoot = Join-Path $syncRunRoot 'results'

dotnet build Dango.slnx -c Release --artifacts-path $syncBuildRoot "-p:DangoPhysicsModulePath=$syncModulePath"
dotnet test tests/Dango.Protocol.Tests/Dango.Protocol.Tests.csproj -c Release --no-build --artifacts-path $syncBuildRoot --results-directory "$syncResultsRoot/protocol" --logger 'trx;LogFileName=protocol.trx'
dotnet test tests/Dango.Server.Tests/Dango.Server.Tests.csproj -c Release --no-build --artifacts-path $syncBuildRoot --results-directory "$syncResultsRoot/server" --logger 'trx;LogFileName=server.trx'
dotnet test tests/Dango.Client.Sync.Tests/Dango.Client.Sync.Tests.csproj -c Release --no-build --artifacts-path $syncBuildRoot --results-directory "$syncResultsRoot/client" --logger 'trx;LogFileName=client.trx'
dotnet test tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj -c Release --no-build --artifacts-path $syncBuildRoot --filter 'FullyQualifiedName~Multiplayer' --results-directory "$syncResultsRoot/desktop-sync" --logger 'trx;LogFileName=desktop-sync.trx'
dotnet test tests/Dango.Core.Tests/Dango.Core.Tests.csproj -c Release --no-build --artifacts-path $syncBuildRoot --results-directory "$syncResultsRoot/core" --logger 'trx;LogFileName=core.trx'
git diff --check
```

逐个检查退出码再运行下一个命令。服务端集成夹具从本轮输出定位真实 `Dango.Server` 程序并验证指纹，不跑旧 bin；WebSocket 协议必须确认实际被使用，不能悄悄退成长轮询。源码或测试变化后先重新构建，再使用 `--no-build`。

M5 后追加现有受影响 Desktop 测试：ProductEntry、Gait、Attachment/Eye/Hat、ShortWander、LightInteraction、QuietIdle/IdleDoze、DesktopPhysicsScene 和 Host 生命周期。Physics 常规测试与历史长稳测试分开，历史旧 DLL 缺导出和全仓格式问题保留基线对照。相关用例无法运行时记“未验证”，不把继承失败当作新功能可跳过的理由。

M6 的拟新增人工入口：

```powershell
pwsh -NoProfile -File ./eng/run-multiplayer-review.ps1 -RunName <新的会话名称> -PhysicsModulePath <核对过的DLL路径> -Build
```

该脚本只负责前置核验、构建/定位最终产物并启动本地会话工具；客户端 A/B、端口及凭证由工具在内存中安排。双机启动指引到 M7 根据实机地址/证书形成，不在计划中编造当前可用地址。

每轮证据至少包含：分支/HEAD与差异摘要、SDK/程序集/native指纹、候选参数版本、红/绿测试计数及断言、服务与A/B的PID/实际监听地址、规范状态对照、实测RTT/重连收敛时间、已知失败/跳过项、人工结论、退出及残留。规范状态比较在同一服务端时间采样，不能比较两端任意瞬间屏幕像素来声称完全同步。日志使用白名单字段，不导出凭证或完整报文；画面仅取候选窗口本身。

## 9. 完成标准和交付状态

| 交付层 | 完成标准 | 当前状态 |
| --- | --- | --- |
| 设计/计划 | 范围、协议细化、任务依赖、失败测试、入口和停止条件可供实施 | 已形成并按授权实施 |
| 自动门禁 | P01–V03 所需用例实际执行，有对应错误实现会失败的证据 | 目标门禁已运行；最终回归和历史环境限制见实施记录 |
| 同机真实运行 | S01 和参数冻结完成 | 双 native 与 native/fallback 首轮已验证，最终构建复核见实施记录 |
| 双机实时行为 | S02 中设备/网络/状态检查完成 | 待验收时安排 |
| 同机小样人工验收 | 用户确认同机 A/B 小样体验 | 用户于 2026-09-30 确认通过；不覆盖双机 M7 |
| 双机人工验收 | 用户在 S02 确认双方处于同一房间、看到同一次动作及恢复表现 | 待 M7 安排 |
| Git 与交付 | 分支、提交、合并、安装、推送、部署逐项记录 | 任务分支本地集成/提交；未合入 main、更新安装、推送或部署 |

M0–M6 已在当前电脑完成实现与同机验证，同机小样人工验收通过。M7 的设备安排不阻碍已完成部分；第二台电脑尚未安排，交付状态为“实现与同机验证完成、同机小样人工验收通过，待双机实时行为及人工确认”，保留证据和可启动入口。

用户已批准在独立实施任务推进 M0–M6，不需要逐个重问常规技术细节。若遇到改变既定范围的选择（例如必须扩展 native ABI、切换引擎或引入数据库），先提交具体证据和修订建议。Phase 2A 验收完成后才能标记本批已验收；拖动/贴贴、持久化、手机互通及完整 Phase 2 仍分别后置。
