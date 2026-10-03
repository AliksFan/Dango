# Phase 2A 最小多人同步详细设计

> 状态：2026-09-30 用户已批准按具体实施计划在独立任务实施 M0–M6；已完成同机双客户端验证，且用户确认同机小样人工验收通过，详见[实施记录](../verification/2026-09-30-minimal-multiplayer-sync.md)。M7 双机及 Phase 2A 整体仍待验收；M7 排期按后续[UI 前置记录](2026-09-30-ui-before-dual-machine-note.md)调整。
>
> 日期：2026-09-24
>
> 所属阶段：Phase 2 — 共享房间同步原型的第一个纵切（Phase 2A）

## 1. 结论

Phase 2A 使用一套本地运行的真实 ASP.NET Core + SignalR 服务，连接两份真实 Windows 客户端进程，证明两只团子观察并操作的是同一个服务端权威房间。

本批只同步四种用户意图：左移、右移、停止和一次非接触“招呼”动作。服务端绑定临时身份、串行裁定命令、分配房间事件序号，并向客户端提供完整快照。客户端只按服务端确认的移动段和动作实例显示，不上传逐帧动画、Box2D 节点或本地桌面信息。

房间和临时身份只保存在服务进程内。本批不建立数据库、不创建迁移、不启动云资源；服务端状态一旦丢失，就生成新的 `roomEpoch` 并明确重置房间，不能称为持久恢复。

开发阶段可以在同一台电脑运行服务器和两个客户端。最终人工验收再安排第二台 Windows 电脑，推荐拓扑是电脑 A 运行服务器和客户端 A、电脑 B 只运行客户端 B；不需要第二台服务器。

## 2. 本批与完整 Phase 2 的关系

[分阶段开发计划](../development-plan-v0.1.md)中的完整 Phase 2 还包含拖动、自动贴贴、模拟延迟观感、初步容量与带宽实测等内容。Phase 2A 只先建立多人协议骨架和最小可见纵切，完成后也只能称为“Phase 2A 完成”，不能把未实现项目改写为已完成。

### 2.1 本批目标

- 两只固定的虚构团子进入同一个临时房间，分别由两个临时参与者控制。
- 两个客户端观察相同的团子集合、规范位置、朝向、移动段、招呼动作实例、`roomEpoch` 和连续事件序号。
- 服务端而非客户端裁定身份、边界、动作冲突、时间、顺序、幂等和重连快照。
- 正确处理加入竞态、重复命令、重复事件、乱序、断线重连、旧连接回调和服务重启。
- 将共享语义与本地 WPF/Box2D 表现隔离，为未来独立移动客户端保留协议边界。

### 2.2 明确不包含

- 拖动、抚摸、贴贴、共享碰撞、堆叠、挤压或多人软体物理裁定。
- 正式账号、好友、情侣、正式房间管理、角色权限或生产级认证。
- PostgreSQL、EF Core、数据库迁移、持久快照、备份或服务重启后的状态恢复。
- Redis、消息队列、多实例、Caddy、Docker、云主机、公网入口或付费资源。
- 客户端预测回滚、无限事件日志或断线期间的增量事件补放。
- Android、iOS、Steam 账号接入或手机—电脑实机互通验收。
- 200 人在线、50–100 个活跃房间、6/12 人房间或任何容量承诺。
- 基础外观分支的合并、帽子 DIY、完整捏脸或日常安装更新。

## 3. 当前仓库事实与既有约束

当前解决方案只有 `Dango.Core`、`Dango.Physics`、`Dango.Desktop` 和对应测试/小样项目，还没有 Server、Protocol、SignalR Hub、权威房间运行时或客户端 Replica。

现有 `SceneSnapshot.SnapshotVersion` 表示本地单机场景结构，不是联网房间的状态版本，不能复用为本设计的 `snapshotSchemaVersion`、`roomEpoch` 或 `lastSequence`。现有自主移动由客户端 `Random.Shared` 产生；联网模式必须禁用它对共享位置和动作的影响。现有多体 Box2D 世界仍是本机表现与输入场景，不是多人权威状态。

本设计沿用以下已批准原则：

- [.NET 10、ASP.NET Core 模块化单体、SignalR + JSON](../adr/0002-server-foundation-and-scaling.md)是首版服务端基础；Hub 只负责连接、分组和分发。
- 每个房间同一时刻只有一个权威运行实例；事件 ID、幂等键、快照版本和重连协议必须在扩容前成立。
- [动画同步](../adr/0003-animation-assets-and-rendering-validation.md)传递动作语义、服务端时间和变体种子，不传当前动画帧。
- [Box2D 软体](../adr/0005-box2d-visual-soft-body-presentation.md)只负责视觉表现，不能回写 Core 或服务端事实；不同客户端不要求逐节点一致。
- [产品设计](../product-design-v0.1.md)中的窗口标题、桌面文件、原始输入和其他本地环境信息不得进入协议或日志。

另一个外观 Worktree 仍有未提交、未合入当前 `main` 的修改。本设计不复制或依赖其中的类型。用户已表示现有三款帽子目前足够，但这不改变本批协议边界；Phase 2A 只携带当前实施基线允许的版本化外观引用。

## 4. 已批准技术与本设计新增取舍

| 类别 | 状态 | 内容 |
| --- | --- | --- |
| 服务端技术 | 已由 ADR 批准 | .NET 10、ASP.NET Core 模块化单体、SignalR、JSON |
| 权威边界 | 已由 ADR/基线批准 | 服务端裁定权限、共享状态、事件顺序、快照和恢复；不逐帧同步 |
| Phase 2A 运行方式 | 纳入本批计划 | 本地真实 Kestrel/SignalR 服务；一台电脑可同时运行服务端与两个客户端进程 |
| Phase 2A 状态存储 | 纳入本批计划 | 一个进程内临时房间；服务重启后新 epoch、明确重置 |
| 首批行为 | 用户已选、本设计细化 | 左移、右移、停止、非接触招呼 |
| 移动模型 | 纳入本批计划 | 服务端生成有限、可解析的移动段；客户端按服务端时间采样 |
| 重连模型 | 纳入本批计划 | 同 epoch 每次重连都取完整快照；不做事件日志回放 |
| 动作冲突 | 纳入本批计划 | 招呼只能在静止空闲时开始，持续期间不接受移动 |

本设计没有替换既有 ADR，因此不新增 ADR。若以后改用原始 WebSocket、不同服务端技术、多实例权威或服务端软体物理，必须另行决策。

## 5. 运行拓扑与模块边界

```text
Windows 客户端 A ─┐
                  ├─ SignalR/JSON ─ ASP.NET Core 服务 ─ 单房间串行运行时 ─ 内存状态
Windows 客户端 B ─┘
```

服务端可以和客户端 A 在同一台电脑运行。客户端 B 在开发时也可以是同机第二进程；人工双机验收时才移到第二台电脑。第二台电脑只需要客户端，不需要数据库或第二个服务实例。

现有日常入口仍保持单实例。同机集成运行必须使用显式的测试配置，为 A/B 分配不同的 `clientInstanceId`、设置目录和单实例键，且不得读写用户的日常设置；它不是绕过正常入口单实例约束的发布功能。

建议的职责边界如下；具体项目名在设计批准后的实施计划中确定：

- **平台中立协议层**：JSON DTO、字段语义、枚举、版本和黄金样例；不得引用 WPF、Win32、Box2D 或 EF 类型。
- **SignalR 适配层**：连接、Hub 调用、组成员和消息分发；不得直接修改房间状态。
- **房间应用/领域层**：身份绑定、命令验证、幂等、限频、动作冲突、状态归一化、`sequence` 和快照。
- **进程内运行时**：每个 `roomId` 一个 single-reader 队列和一个有序 outbound 队列。
- **桌面网络层**：连接生命周期、时间偏移估计、命令队列和 Replica 状态机。
- **桌面表现适配层**：把 Replica 的规范锚点、移动段和动作实例映射到现有 WPF/Box2D 或 authored fallback。

C# 共享类型可以减少当前 Windows/服务端重复代码，但不能成为只有 .NET 才能解释的隐式协议。字段语义和黄金 JSON 仍须足够让未来移动客户端独立实现。

## 6. 临时身份与连接所有权

### 6.1 临时身份

服务启动时创建一个临时房间和两个虚构参与者/团子槽位，并为每个槽位生成独立的 `joinTicket`。自动测试从 fixture 注入虚构票据；人工运行通过不入 Git、不写日志的本地启动材料传递。

首次加入后，服务端返回并绑定：

- `participantId`：临时参与者身份；
- `petId`：该参与者唯一可控制的团子；
- `resumeToken`：同一 `roomEpoch` 内恢复身份的临时令牌；
- `connectionGeneration`：该参与者当前有效连接代际；
- `nextCommandOrdinal`：该参与者在当前 epoch 中下一个新逻辑命令必须使用的 ordinal。

`joinTicket` 的首次成功使用与 `clientInstanceId` 绑定。同一组合在响应丢失后可以安全重试且不会生成新参与者/团子，但不同客户端实例不能借此生成第二只团子。同一当前传输连接重复 Join/Resume 返回既有代际；只有把身份绑定到新的传输连接时才递增 `connectionGeneration`，并立即取代旧代际。

Phase 2A 的 `resumeToken` 在同一 epoch 内保持稳定，不在 Join/Resume 时轮换。Join/Resume 重试可以建立新的连接代际和取得更新的快照，但只能恢复原有参与者/团子，绝不能创建副本。这样即使成功响应丢失，客户端仍可在当前或新传输连接上用原凭证安全重试。正式认证阶段若要轮换令牌，必须另行设计可幂等恢复的轮换协议。

`joinTicket`、`resumeToken` 和 SignalR `connectionId` 都不得写入仓库、普通日志或诊断导出。`clientInstanceId` 和 SignalR `connectionId` 不是业务身份。客户端命令不得声明任意 `participantId` 或 `petId`；服务端只使用当前连接所绑定的身份。

代际切换必须在会话注册表中原子完成。服务端从房间收件人集合和 SignalR group 中移除旧连接，并向其发送 `sessionSuperseded` 后停止向它分发新事件；即使已有网络回调正在路上，旧客户端也因本地连接代际不匹配而不得应用。SignalR group 只用于传输优化，广播前仍以当前会话注册表中的连接集合为准，不能把 group 成员资格当成授权。

这只证明最小原型的身份隔离，不代表正式账号认证安全。

### 6.2 Join 与 Resume

最小调用面为：

- `JoinRoom(JoinRoomRequest)`：使用 `joinTicket` 首次加入或重试首次加入。
- `ResumeRoom(ResumeRoomRequest)`：连接中断后使用当前 `resumeToken` 恢复。
- `SendCommand(RoomCommand)`：提交共享操作意图。
- `GetSnapshot()`：发生 gap 或显式重同步时取完整快照。
- `GetRoomTime()`：取得不进入房间事件流的时间样本。
- `RoomEvent`：服务端向房间组广播确认事件。

SignalR 自动重连不会自动恢复业务会话或组成员。每次传输重连成功后，客户端仍必须调用 Resume、取得新连接代际和完整快照，之后才能回到 `Live`。

### 6.3 加入快照竞态

Join/Resume 必须按以下顺序完成：

1. 验证临时票据或恢复令牌，并为连接绑定身份和新代际。
2. 将新连接加入 SignalR 房间组。
3. 在房间 single-reader 队列内捕获 `lastSequence = S` 的完整快照。
4. 返回身份、epoch 内稳定的恢复令牌、连接代际、下一个命令 ordinal 和快照。

客户端从调用开始即进入 `Joining` 或 `Resyncing` 并缓冲事件。因此，组内事件即使早于 Join/Resume 响应到达，也不会直接改动可见状态：原子应用快照 S 后，丢弃 `sequence <= S`，再只应用连续的后续事件。任一步失败都撤销连接绑定并移出组。

## 7. 协议 v0.1

### 7.1 通用规则

- `protocolVersion` 首批固定为 `0.1`；不兼容版本 fail closed。
- JSON 字段使用稳定 camelCase 名称；ID 是不透明字符串，不能塞入本地物理实体句柄。
- 所有数字必须有限并处于协议范围；`NaN`、无穷、溢出、未知必需枚举或缺失必需字段一律拒绝。
- 接收方可忽略未知可选字段，但不能猜测未知命令、事件或快照主版本的语义。
- `roomEpoch` 标识一次内存房间生命期；任何权威状态丢失都必须使用从未复用的新值。
- `sequence` 在同一 `(roomId, roomEpoch)` 内从 1 开始严格递增且连续；快照初始 `lastSequence` 可以为 0。
- `eventId` 全局唯一；`commandId` 由客户端为一次逻辑命令生成，重试时保持不变。
- 点对点 Join/Resume/CommandResult/TimeSample 不占用房间 `sequence`，防止其他客户端看到假 gap。

`serverTimeMs` 定义为当前 `roomEpoch` 内由服务端单调时钟产生的非负毫秒数，不是 Unix 时间，不能跨 epoch 比较。客户端以本地单调时钟和多次 `GetRoomTime` 往返样本估计偏移；客户端墙钟、时区或用户手动改时间不得参与业务裁定。采样周期、异常 RTT 舍弃规则和允许的显示误差在实施计划中根据实测冻结。

### 7.2 房间坐标

协议坐标采用版本化的规范二维空间，不使用物理像素、WPF DIP 或 Box2D 单位：

- 原点在左下，x 向右、y 向上；规范范围为 `[0, 1] × [0, 1]`。
- Phase 2A 只改变 x，团子锚点 y 固定在服务端给出的 `groundY`。
- 快照的 `coordinateSpace` 给出 `id`、`version`、`bounds`、`walkableBounds` 和 `groundY`。
- 服务端位置表示团子规范落地锚点；客户端把它映射到本机安全显示区域。
- 服务端按 `walkableBounds` 裁定移动，客户端分辨率、DPI、任务栏或显示器布局不能反向改变房间坐标。

以后加入岛、家具或体型时，可以升级坐标/碰撞规则，但不得把本地屏幕尺寸偷偷写成共享房间事实。

### 7.3 Join/Resume 请求与响应

`JoinRoomRequest` 至少包含：

```text
protocolVersion, roomId, clientInstanceId, joinTicket
```

`ResumeRoomRequest` 至少包含：

```text
protocolVersion, roomId, roomEpoch, clientInstanceId, resumeToken
```

成功响应至少包含：

```text
protocolVersion, connectionGeneration, selfParticipantId, selfPetId,
resumeToken, nextCommandOrdinal, snapshot
```

响应中的令牌属于敏感字段，诊断对象和默认序列化日志必须显式脱敏。

### 7.4 RoomCommand

```text
protocolVersion
roomId
roomEpoch
connectionGeneration
commandId
commandOrdinal
lastAppliedSequence
type
payload
```

规则：

- `commandOrdinal` 在每个 `(participantId, roomEpoch)` 内从 1 连续递增，不因传输重连或连接代际变化而重置；客户端通过单一 outbound 队列发送命令。
- 同一逻辑重试在同一 epoch 内始终保留相同 `commandId`、`commandOrdinal` 和业务载荷，包括跨连接代际重试。
- 幂等比较只比较命令 `type` + 规范化业务载荷，不把连接代际、ordinal 或 `lastAppliedSequence` 算入业务载荷。
- 通过协议/身份/epoch/当前代际检查后，服务端先查询 `(participantId, commandId)`：同载荷直接返回缓存结果，不再检查 ordinal 或推进序号；不同载荷返回 `command_id_conflict`。
- 新命令在消费 ordinal 前必须通过有界资源准入，预留结果记录和必要事件容量；资源不足返回 `rate_limited` 且 `ordinalConsumed=false`，不能在无处保存结果时消费序号。
- 只有首次见到的 `commandId` 才进入 ordinal 校验。它必须正好等于服务端返回的 `nextCommandOrdinal`，且未被其他命令 ID 占用；否则返回 `command_order_mismatch`。
- 首次见到并通过 ordinal 检查的命令，无论业务结果是接受、no-op 还是拒绝，都只消费该 ordinal 一次并缓存结果，避免迟到的旧意图越过较新的意图重新生效。
- 服务端始终根据当前权威状态裁定，不信任客户端最后看到的状态。`lastAppliedSequence` 大于服务端当前值或落后超过有界窗口时返回 `resync_required`。
- 首批命令只有 `setMoveIntent { direction: left | right | stopped }` 和 `startWave {}`。客户端不能提供速度、起点、终点、时长、动作种子、参与者或可控制团子。

### 7.5 CommandResult

Hub 调用直接返回、不进入房间序号流：

```text
protocolVersion, roomId, roomEpoch, commandId,
outcome, stateChanged, currentSequence,
ordinalConsumed, nextCommandOrdinal, originalOutcome?,
eventId?, eventSequence?, serverTimeMs, error?
```

`outcome` 为 `accepted | duplicate | rejected`：

- 新命令改变共享状态时，服务端先原子提交状态和事件，再返回 `accepted` 及事件 ID/序号。
- 合法 no-op 返回 `accepted + stateChanged=false`，不产生事件、不推进序号。
- 相同身份、相同 `commandId` 和相同业务载荷返回 `duplicate`，并指向原事件，不重复执行。
- 相同 `commandId` 配不同业务载荷返回 `command_id_conflict`。
- 拒绝、重复和 no-op 都不得推进房间序号。

2026-09-30 实施细化：`originalOutcome` 保留重复命令最初的接受/拒绝结论；`ordinalConsumed` 区分未准入和已裁定；缓存结果的 `nextCommandOrdinal` 是历史水位，重连以当前 Resume 快照屏障返回值为准。详情见 [实施计划第 3 节](2026-09-30-minimal-multiplayer-sync-plan.md#3-实施前明确的协议细节)。

稳定错误码至少包括：

```text
unsupported_protocol, invalid_ticket, invalid_resume_token,
not_joined, superseded_connection, room_epoch_mismatch,
command_order_mismatch, invalid_command, forbidden,
action_conflict, rate_limited, command_id_conflict,
resync_required, room_reset_required
```

错误详情不能包含令牌、连接串、本地路径或原始输入内容。

### 7.6 RoomEvent

```text
protocolVersion
eventId
roomId
roomEpoch
sequence
serverTimeMs
causationCommandId?
type
payload
```

首批公开事件只有：

- `pet.motion.changed`：完整替换指定团子的规范位置、朝向和可选移动段。
- `pet.action.started`：完整给出一次招呼动作实例。

事件必须是所有房间客户端都能观察的公开状态变化。私有回执、限频详情和身份令牌不得混入该序号流。

### 7.7 RoomSnapshot

```text
protocolVersion
snapshotSchemaVersion
roomId
roomEpoch
lastSequence
serverTimeMs
selfPetId
coordinateSpace
pets[]
```

`snapshotSchemaVersion` 只表示快照 JSON 结构；`(roomEpoch, lastSequence)` 才表示权威状态版本，两者不能混用。

每个 `PetState` 至少包含：

```text
petId, participantId, anchorPosition, facing,
motion?, action?, appearanceRef, physicsPresentationPresetRef
```

`appearanceRef` 和 `physicsPresentationPresetRef` 是服务端 allow-list 校验过的版本化预设引用。它们不包含 WPF 对象、Box2D 节点、弹簧、速度或未合入外观分支的私有类型。Phase 2A 可只使用当前正式基线中的默认安全引用。

服务端生成快照前，先按当前 `serverTimeMs` 归一化已经结束的移动段和动作：移动落在确认终点，过期 `motion`/`action` 变为 `null`。本批不保存或回放完整事件历史。

## 8. 权威房间运行时

### 8.1 单写者规则

一个 `roomId` 只有一个 single-reader `RoomRuntime`。所有会改变共享状态的工作都进入同一队列，并按以下顺序处理：

1. 读取房间单调时间并归一化到期状态。
2. 验证协议、房间、epoch、当前连接身份和连接代际。
3. 先查询 `(participantId, commandId)` 幂等记录；相同业务载荷直接返回已记录结果，不同载荷返回冲突。
4. 对首次见到的命令先预留结果/必要事件容量，再原子验证并消费 `commandOrdinal`；未准入不消费。
5. 验证有界载荷、限频、当前动作冲突和房间边界。
6. 根据当前权威状态计算结果；绝不使用客户端自报位置或物理结果。
7. 若状态改变，分配唯一 `eventId` 和下一个连续 `sequence`，原子应用状态并记录结果；no-op/拒绝也记录其幂等结果。
8. 把事件写入单一有序 outbound 队列；Hub 依次广播给会话注册表中的当前连接。
9. 返回 CommandResult。事件可能先于回执到达，客户端不得依赖两者的到达先后更新状态。

网络广播不在状态临界区内等待。若发送失败，客户端以后通过 gap 检测或重连快照恢复；服务端不能为了“补发”而让两个执行路径同时修改房间。

### 8.2 幂等、限频和有界内存

- 幂等记录至少覆盖本批允许的重试/恢复窗口；同 ID 同载荷返回原结果，同 ID 不同载荷稳定报错。
- 命令 ordinal、幂等缓存、每身份速率限制、消息大小、运行时队列和客户端事件缓冲都必须有硬上限。
- 超限时返回稳定错误或要求完整重同步，不能静默丢命令、无限增长或降低另一身份的正常服务能力。
- 具体速度、招呼时长、移动租约、限频、缓存容量、buffer 上限、超时和视觉平滑容差是实施前的可测参数；本设计只冻结它们必须由服务端配置、有界且能被失败型测试覆盖，不先伪造性能结论。

### 8.3 未来持久化接缝

房间应用层保留可替换的 `IRoomSnapshotStore` 边界，未来可在 PostgreSQL 中加载/保存持久快照，但领域状态、Hub DTO 和 EF 实体不得互相泄漏。

Phase 2A 只绑定内存实现，不创建 Schema 或迁移，也不在进程退出时冒充保存成功。进程重启、运行时无法恢复或内存状态损坏时：

1. 废弃旧 `roomEpoch`、票据、令牌、幂等缓存和事件缓冲。
2. 创建新 epoch、两只初始团子和 `lastSequence = 0` 的快照。
3. 客户端以 Resume 请求中的旧 `roomEpoch` 发现不匹配，收到不含新凭证的 `room_reset_required { currentRoomEpoch }`，清空旧 pending/buffer 并显示“临时房间已重置”。
4. 操作者从本地验收启动材料重新加载服务端新生成的临时 `joinTicket`，客户端再执行 Join。服务端不通过匿名错误响应发放新凭证。

这叫受控重置，不叫数据库恢复、持久恢复或高可用故障转移。

## 9. 移动与招呼规则

### 9.1 有限移动段

服务端确认 `left` 或 `right` 时，先在当前服务端时间解析已有移动段的规范位置，再生成：

```text
motionId
from
to
startedAtServerTimeMs
endsAtServerTimeMs
facing
movementProfileVersion
```

服务端选择速度、终点、最大连续移动时间和边界。客户端按服务端时间解析同一段，不逐帧上报或接收位置。

- 同方向续行必须由低频命令续约，且续约产生新的权威移动段；不能依赖无限持续的本地按键状态。
- `stopped` 由服务端按当前时间采样已有移动段、夹紧到合法边界，并广播 `motion = null` 的完整新状态。
- 反向移动先解析当前点，再从该点生成相反方向的新段。
- 到达边界或有限移动段结束后，所有客户端都能仅凭该段得到相同终点；无需结束事件。
- 连接中断时，服务端对该身份生成停止状态，或最多允许当前已确认有限段自然结束；不能因丢失 Stop 永久行走。

### 9.2 招呼动作

服务端确认 `startWave` 后生成：

```text
actionId
actionKey = wave
participants = [selfPetId]
startedAtServerTimeMs
endsAtServerTimeMs
variantSeed
presentationVersion
fallbackKey
```

招呼是非接触动作，只允许静止且没有其他共享动作时开始。移动中发起招呼、招呼中发起左/右移动均返回 `action_conflict`；招呼中 Stop 是合法 no-op。动作结束由服务端时间函数决定，不广播逐帧或高速补播结束前的帧。晚加入/重连客户端从快照中的同一动作实例和当前服务端时间直接采样；结束后的快照显示 `action = null`。

### 9.3 本地表现边界

联网时，Replica 的规范锚点、朝向、移动段和动作实例是唯一共享输入：

- 禁用 `ShortWanderController` 等会改变共享位置或动作的本地随机逻辑。
- 当前步态只能依据已确认移动段的规范位移/时间表现，不能把键盘持续时间或本地物理速度提升为事实。
- Box2D 可以围绕规范锚点产生轮廓、眼帽惯性和次级晃动，但不能把节点、碰撞或质心回写 Replica/服务端。
- 一端 native 物理失败时使用 authored fallback；两端仍必须保留相同规范位置、动作实例、起止时间和结束状态。
- 自然眨眼、呼吸和不改变共享语义的轻微晃动可以本地生成；它们不占房间序号。
- Phase 2A 不裁定团子间碰撞，本地物理不得把两只团子的权威锚点各自推开。

## 10. 客户端 Replica 状态机

```text
Disconnected -> Joining -> Live
                    ^        |
                    |        v
                 Resyncing <-+

任意状态 -> Incompatible（协议/快照版本不兼容）
任意状态 -> Faulted（不可恢复的本地校验或实现错误）
```

### 10.1 应用规则

- `Joining`/`Resyncing` 只缓冲当前尝试收到的事件，不直接改可见共享状态。
- 快照必须先完整校验，再一次性替换 Replica；不能逐个团子修改造成半新半旧状态。
- 应用快照 S 后丢弃 `sequence <= S` 的重复事件，只按 `S+1, S+2...` 连续应用。
- `sequence <= lastAppliedSequence` 是重复，忽略但不重新触发动画。
- `sequence > lastAppliedSequence + 1` 是 gap：立即进入 `Resyncing`，不提前应用较新事件，并请求完整快照。本批不做增量补发。
- buffer 超限、事件 epoch 不同、快照自相矛盾或服务端要求重同步时，清空候选状态并重新取快照。
- 每个回调捕获本地连接代际；旧连接迟到的事件、快照和回执不能覆盖新代际状态。
- 未确认命令不能修改规范共享状态；首批不做预测/回滚。可以显示不影响团子的本地输入反馈。

命令回执和公开事件允许任意先后到达。公开事件/快照驱动共享 Replica；CommandResult 只负责本地 pending、错误提示和幂等重试。若服务端已经接受命令但回执丢失，客户端以原 `commandId` 重试，不能重做一次动作。

### 10.2 断线显示

连接中断后不再接受共享命令。客户端可以把已经确认的有限移动段播放到其确认终点，把已经确认的招呼播放到结束；到期后冻结在规范状态，不继续随机散步或推测新动作。恢复后完整快照无条件覆盖本地临时显示。

## 11. 安全与隐私

- 只使用虚构参与者、团子、房间和日志样例。
- 协议不采集窗口标题、窗口内容、键盘文本、原始鼠标轨迹、桌面文件、剪贴板或其他本地隐私。
- 日志可以记录脱敏后的 `roomId`、epoch 后缀、序号、事件类型、稳定错误码和相关 ID，但不得记录票据、令牌、连接串或完整异常中的敏感字段。
- 客户端自报角色、团子、位置、时间、速度或动作完成均不是授权依据。
- 本地开发服务不得自动打开公网端口或修改防火墙。双机验收准备必须先冻结监听地址、WSS/证书或物理隔离方式、临时凭证交付和防火墙最小规则；未完成这项前置检查不得开始双机验收。不得在不受控局域网明文传递票据/令牌，也不能把验收配置称为生产部署。

## 12. 能失败的验收设计

以下是未来实施必须满足的门禁，不表示本轮已经执行。

### 12.1 协议与权威单元门禁

- 黄金 JSON 固定必需字段、枚举和值域；未知必需语义、不兼容版本、缺字段、非有限数和越界坐标必须失败。
- A 尝试声明或控制 B 的团子、复制 B 的身份、使用旧连接代际、错误 epoch 或非法 ordinal，均被拒绝，状态和 `sequence` 不变。
- 两个连接并发提交命令时，RoomRuntime 队列产生唯一、连续、无分叉的顺序；Hub 到达顺序本身不充当权威。
- no-op、拒绝和重复命令不推进序号；只有公开状态改变产生一个事件。

### 12.2 幂等门禁

- 相同身份以同一 `commandId` 和相同载荷发送两次，只产生一次状态改变、一个事件和一次序号推进。
- 模拟“服务端已提交但回执丢失”，重连后以同 ID 重试，仍不重复移动或招呼。
- ordinal 1 已提交但回执丢失、ordinal 2 已接受后，再重试 ordinal 1：必须返回原结果/`duplicate`，不能先报 `command_order_mismatch`，也不能推进序号。
- 同一 ID 配不同载荷必须得到 `command_id_conflict`，不能覆盖旧结果。

### 12.3 快照、乱序和竞态门禁

- Replica 已应用 10 时注入 `10, 12, 11`：12 不能被提前显示；重复 10/11 不能重播动作，最终状态与权威快照一致。
- 事件先于 Join 快照到达、快照夹在 S 与 S+1 之间、事件先于 CommandResult 到达时，不能丢 S+1 或产生两次效果。
- gap、buffer 超限或快照校验失败必须进入 `Resyncing`，不能“尽量猜”后续状态。
- 新连接已应用快照后，旧连接迟到的事件/回执/快照不能覆盖它。
- 新 Resume 成功并完成代际切换后，旧连接必须退出服务端当前收件人集合；已在途回调也因旧代际被客户端忽略，不能继续形成有效 RoomEvent 流。

### 12.4 移动和动作门禁

- `left/right` 的起点、终点、速度配置、朝向和有限结束时间由服务端产生；客户端伪造这些值无效。
- Move 后直接断网、不发送 Stop，团子仍在配置的有限上界内停止且不越界；迟到旧 Stop/Move 不能覆盖新代际状态。
- 移动与招呼同时提交时，只由串行权威顺序决定接受/拒绝；两客户端得到相同动作实例和稳定错误码。
- 两客户端系统墙钟相差数分钟或运行中跳变，仍得到相同规范终点、动作 ID、变体种子和结束状态。
- 两客户端使用不同本地随机种子并空置，没有服务器事件时，规范位置和共享动作保持一致。

### 12.5 重连与重启门禁

- 同一 `joinTicket + clientInstanceId` 的重复/并发 Join 只能绑定原有参与者和团子；同一传输连接重复调用保持原代际，新连接成功后只有一个最新代际有效。不同 `clientInstanceId` 复用已绑定票据必须失败。
- B 断线期间 A 移动并招呼；B Resume 后以完整快照恢复同一 epoch、同两只 `petId`、相同终点/动作实例，不生成幽灵团子或高速补播旧帧。
- SignalR 传输重连但尚未 Resume 时不能发送共享命令，也不能假设仍在旧 group。
- 服务端重启后 `roomEpoch` 必须变化；携带旧 epoch 的 Resume 得到 `room_reset_required`，相同当前 epoch 下的错误令牌只得到 `invalid_resume_token`，客户端不能把普通凭证错误误报成重启。旧事件、旧命令、旧票据/令牌均不能进入新房间；重新载入本地新票据后，新房间仍恰好两只初始团子。

### 12.6 表现降级门禁

- 一端正常使用 native 软体，另一端故意触发 native 初始化失败并使用 fallback；两端仍有相同 epoch、连续序号、规范位置、动作实例和终点。
- 本地软体参数、帧率、DPI 或视口不同，只能改变表现细节和坐标映射，不能改变房间事实。
- 旧 DLL 缺导出的既有自动门禁仍单独报告，不能通过替换、跳过或多人局部通过写成全绿。

### 12.7 真实运行与人工门禁

实施阶段先允许同机运行真实 Kestrel/SignalR 服务和两个真实桌面进程，验证完整协议路径；仅 Mock、TestServer 或内存客户端不能替代此证据。

最终人工验收另行安排两台真实 Windows 电脑：

- 电脑 A：服务器 + 客户端 A；电脑 B：客户端 B。
- 两端核对相同 `roomEpoch`、最高连续 `sequence`、团子集合、移动终点和招呼 `actionId`。
- 使用不同分辨率/DPI，确认只是映射不同，双方仍认为这是同一个房间和同一次动作。
- 实际断网、恢复和客户端重连后，没有副本、长期分叉或旧回调覆盖。

在第二台设备和用户观感验收完成前，这些项目必须标记“未验证”。同机双进程、模拟乱序或负载脚本不能替代真实双客户端门禁。

## 13. 明确保留的未验证项

即使 Phase 2A 后续全部通过，也不能据此声称以下事项已验证：

- 手机—电脑或 Windows—Steam 跨端互通；
- 拖动、贴贴、碰撞、堆叠和软体挤压的一致性；
- PostgreSQL 持久化、数据库/应用重启恢复、备份或迁移；
- 200 在线、50–100 房间、P95 延迟、带宽或长期稳定容量；
- 公网 TLS、云部署、多实例、Redis backplane 或高可用；
- 完整外观配置兼容、正式账号安全或生产运维。

## 14. 后续顺序与授权边界

1. 用户已于 2026-09-30 要求据本设计制定具体实施计划。
2. 已形成 [Phase 2A 实施计划](2026-09-30-minimal-multiplayer-sync-plan.md)，列出任务、失败型测试、候选参数和实测冻结步骤；第 3 节细化命令恢复、会话竞态、快照防回退与末尾事件收敛，第 6 节定义独立 Window 小样与真实软体呈现接线。
3. 获得实施授权后，才新增协议、服务端、Replica、测试和本地运行入口。
4. 原排期为自动门禁和同机真实运行通过后准备第二台电脑与人工验收；2026-09-30 用户调整为先完成完整可用的 Windows UI（创作编辑器后置），再安排正式 M7 双机验收，详见[排期记录](2026-09-30-ui-before-dual-machine-note.md)。M7 原有门禁不变。
5. 拖动/贴贴、手机客户端和容量验证仍分别后置。原先“本批验收后才设计数据库持久化”的顺序不再约束 UI 所需的正式账号/持久化规划；其详细设计和实施仍需独立批准，数据库也不属于本 Phase 2A 小样。

本设计最初只交付设计；用户后续已另行授权 M0–M6 本地实施、隔离分支/提交及回环监听验证。仍未授权合并到 `main`、更新安装、推送、部署、开放非回环端口、建立数据库或启动付费资源。
