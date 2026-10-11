# 双人玩法 P0：隔离准入冻结合同

> 2026-10-11现行平台顺序：[Windows先行修订](2026-10-11-dual-player-windows-first-plan.md)已由用户批准。Linux移至部署准备前独立门禁；下文历史“Linux缺口停止P2”由此替代。Windows物理／呈现／恢复和正式后端条件仍需满足，全部数值门槛保留。

日期：2026-10-11。用户在已交付设计／计划后指示“继续”，本轮批准设计及 P0–P1 隔离实施、构建、虚构夹具和本地提交。P2–P6、正式服务、数据库操作、main 整合、推送、安装与部署未授权。

依据[设计](2026-10-10-formal-ordinary-room-dual-player-design.md)、[计划](2026-10-10-formal-ordinary-room-dual-player-plan.md)和[ADR](../adr/proposals/2026-10-10-authoritative-room-physics.md)。本合同在新增实现和失败验证前冻结；失败只记录，不事后放宽。

## 1. 来源与文件所有权

实施分支 `codex/ordinary-room-physics-admission` 从设计提交 `af55f9ce6dbed24e5621f28307cfa2cd57eb4efa` 创建。P1 只依赖该提交内的物理／呈现；正式后端联合基线到 P2 前再选，当前不导入后端活跃分支。

| 冻结来源 | SHA256／版本 |
|---|---|
| Box2D 官方源码 | 3.1.1，commit `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3` |
| Box2D source archive | `FF94DDF7D75FFA6171502FDC7A7F336594560278978305CFC56C72268BC66721` |
| MIT LICENSE | `68A3E676D7E94093B102D5CBA0D4E04AF812040D6F230C3DB67A6664574E43D2` |
| 原 `dango_collision_review.c` | `A89366157E13A1CE2502789A17A540C68062A5684E38C783DB5FDD555B1539E3` |
| 原 `dango_box2d_bridge.c` | `EB2782E3E813B246093018A34B509CCA6D2F34791C061E1705C821491769A678` |
| 原 module header | `E20C1B40D8DFE2F9CDAD77A2D215E89207B476990E6CBA5B8EC0801BF0531242` |
| 原参数文件 | `5C507A89A03979F90B91BD037EE9F3125614F00BFA71991A53ADC59DCF382E7C` |
| 只读源码重建 baseline module | `BA6E92D57CAFBD5CC806F709890AA1E8ED784F97CF42A09F4168EE08065E97A0` |
| 受保护日常 DLL | `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A` |

Native owner 修改 collision module、可选 ABI、managed world binding 和 Authority tests；presentation owner 修改独立 semantic adapter／测试；根负责合同、隔离 headless host、调度与最终验证。Box2D 缓存及源码只读，不引用其 private headers，不补丁依赖，不关闭 warm starting，不改原团子参数。构建直接调用 CMake 至 `artifacts/ordinary-room-physics-admission`，不运行会覆盖日常 DLL 的 `build-box2d.ps1`。

## 2. 坐标、形状与时间

原参数保持16节点、4 substeps、最小面积 `.42`、最大速度 `12 p/s`、GroundHeight `-.72 p`。本批不接新圆形模型、家具、球或手套。普通房逻辑宽2160；在地面以上使用正 y 高度：

- `logicalCenterX = 1080 + 80 * nativeCoreX`；`logicalCenterHeight = 60 * (nativeCoreY + .72)`。
- 逆映射分别为 `(x - 1080) / 80`、`height / 60 - .72`；中心与碰撞脚底分开，`0,0` 不是旧资料的合法猜测出生。
- 地面逻辑高度0。左右碰撞界24与2136，native为 `-13.2` 与 `13.2`。首批自然出生 anchor 为 `(-1.4,0)` 与 `(1.4,0)`，保留原 DropHeight；实际初始中心由首个成功步输出。堆叠测试的上层 anchor 为 `(0,1.65)`，不作为产品出生区。
- 名义60 Hz固定步沿用旧 managed adapter：`TimeSpan.FromSeconds(1d/60)`，送入 native 的 float 来自其 `TotalSeconds`。2026-10-11 .NET10实际probe确认ticks为166666、TotalSeconds为`.0166666`、float bits为`0x3C888865`；先前文字近似`.0166667`不代表实际bit值，现作事实更正，未改变adapter。旧native coldpose独立实验使用的`.0166667f`只代表其自身同构建对照，不等同managed时间轨迹。不能在新host改用另一浮点dt后称旧轨迹逐位相同。
- Tick 是成功步编号，事件单独计数。故障不推进安全 Tick；所有实体一次读自同一成功步。单调时钟调度，墙钟不决定步长。

## 3. 独立可选 ABI 与恢复停止点

保留 legacy ABI1、42 floats、3体容量和未启用路径。authority capability 另用 v1，明确 enable 后才产生安全缓存，首批只支持最多2体且无家具。只读 getter 校验 exact frame size，不在 fault 后读取污染世界；固定 buffer 280 bytes：40-byte header、3个48-byte body槽、3个32-byte pair槽，未用槽清零。

Body 输出 entityId、floorGrounded、supportEntityId、CanJump、实际当步 JumpResult、core position／velocity、support normal／gap 和 cooldown；pair 输出两方身份、normal／gap／较小法向投影全宽。不包含轮廓、节点、指针或句柄。floorGrounded 与 supported-by-Dango 分开；旧 row[39] 不能独自决定共享起跳。enable/getter不能改变原轨迹。故障读取最后安全缓存并标识错误；Add／Remove刷新场景代际并撤销旧支撑。

完整 checkpoint gate 要求原身体动态、所有节点／速度／冷却、真实接触和未来步在冻结预算内续接，不能以 Add＋落下代替。只读审计确认官方公开 API 没有 joint impulse／contact cache／sleepTime／solver graph 的完整恢复接口。因此本轮完整恢复 capability 必须保持 unsupported，导入不得修改世界；允许独立 experimental executable 测量“公开 pose／velocity 冷恢复”的不足，但不导出为生产恢复 API。

冷恢复实验先核对17点 position／velocity各轴误差 ≤ `1e-6 p`，再与同构建不中断世界比较120步；任一节点 position误差 > `1e-5 p`、velocity误差 > `1e-4 p/s`、支撑身份或跳跃分支不一致即记录失败。覆盖地面、相撞、堆叠、空中、移除下层。即使个别轨迹巧合符合数值，也不据此宣称隐藏求解器状态完整恢复。

## 4. 可失败的物理与呈现门槛

| 验证 | 预先冻结的门槛 |
|---|---|
| 单人／双体无变化对照 | baseline与candidate各1800步，移动／停止／跳跃／下压／拖动；legacy42 floats逐位一致，重复只读getter不改变结果 |
| 真实接触／支撑 | 原3%法向全宽侵入目标；原诊断浮点门限 `.031`；support gap ≤ `.008`、upward normal ≥ `.7`、upperCoreY > lower+.4、relativeVy < `.5` |
| 支撑起跳反作用 | 原冲量动量误差 ≤ `1e-5`；无凭空接触动能增加；上下层反作用真实，侧面接触不授予起跳 |
| 呈现中心 | 最终绘制中心各轴对同一采样时刻的权威插值误差 ≤ `1e-5 p`；不得用 native center／seam offset 替换 |
| 实际画出轮廓 | 原 quadratic B-spline128点 SAT；penetration ratio ≤ `.031`；权威支撑期间正 gap ≤ `.008 p` |
| 两份呈現实例 | 30／60／144 Hz，在相同采样时间中心差 ≤ `1e-5 p`，支撑身份和接地语义一致 |
| 支撑撤销 | 下一个呈现固定步清旧支撑；移除下层后上层正常落下，不移动权威中心掩盖缝隙 |
| 软体安全 | 原16点、面积／最大速度／交叉／锐刺门禁；不以 fallback 通过玩法就绪 |
| 20 Hz插值 | 常态中心重建 P99 ≤ `.02 p`、最大 ≤ `.06 p`；Jump／碰撞冲量窗口单列，不能把100ms采样延迟算空间误差 |

独立 presentation 模式每60 Hz固定步接权威 centers／velocities，整组校验后对17点作统一平移与速度差补偿，保留局部相对动态；操作输入 neutral。它只能产局部轮廓，不能当 authority world 读取，也不能从本地碰撞得出玩家输入资格。两种模式不能混用。支撑／contact语义仅驱动表现，未必可行；若SAT或原软体门不通过，停止呈现准入，不传轮廓、不固定缝隙、不移动显示中心、不调整参数兜底。

2026-10-11实施澄清：轮廓和最终权威绘制中心必须属于同一固定步终点T。候选须用T−dt的整组目标驱动局部步进到T，再以T的权威中心绘制；首次样本允许按当前速度回推一步起点。将T的目标先送入、再多走一步却仍按T中心绘制属于时间错配，不能计入准入。此修正不改变表中任何参数或容差；原错误相位日志保留，修正后仍需独立通过全部门禁。动态core残差造成的绘制接触偏差也不能靠移动中心修补。

关闭真实碰撞／反作用、错误支撑身份、陈旧支撑、采用本地center等变体必须出现预期失败。完整恢复与语义呈现是独立 gate，不能互相替代。

## 5. 后续正式协议预算及生命周期合同

以下为P0冻结供P2实现，不在P1接正式Hub／身份：状态20 Hz、持有输入10 Hz；每控制者输入预算20次／秒、burst4，Stop预留；应用期限100ms，从服务器接收时刻计，持续意图350ms失效。输入队列上限64、输出共同帧队列8、最多补4步，超预算转明确暂停／恢复，不无限排队。中立玩法DTO上限4 KiB、最多2 body／1 pair，内部checkpoint候选上限64 KiB；实际完整格式仍缺能力，不能上传节点绕过。

命令出队复查代际／deadline／ordinal；queued不代表applied，Jump不补播，旧Move不越过Stop。ready绑定当前lease／instance／实际连接／归属与控制／PresenceGeneration／PlayEpoch／输入代际／几何与模拟版本／到期，确认重验。实际出站逐次核对会话、lease、房间资格和连接。玩法适用性在准入提交前复验。

PresenceGeneration在真正进入／重入／迁移产生新生命周期，同房接管／有效重绑／私密睡眠保持；控制版本变化不重建身体。RuntimeRevision持久claim同时捕获最后合法成功存档；新存档成功前保留，CAS拒旧writer和倒序tick/revision。首次无存档可安全出生，坏存档阻止就绪；恢复过滤RoomId＋PetId＋PresenceGeneration，新成员安全出生、幸存身体不重建。全房休眠按最新正式有效控制与观察租约，保留30秒业务容错，与350ms输入看门狗分开。

## 6. 平台、人工与交付

Windows环境已只读确认 .NET10.0.303、CMake3.31.6-msvc6、MSVC14.44.35207。Linux只有Stopped的docker-desktop WSL，Docker daemon不运行；不启动／安装持久资源，Linux gate当前未验证。P1 headless进程只使用虚构输入和隔离artifact，无网络端口、账号、数据库或用户存档。

需要可视实验时沿用桌面规范v0.1 §§2–6和已有绘制器，仅作为P1隔离验证窗；不改正常入口或通过静态演出宣称多人。实际窗口、600秒活动观察、人工与双机分别记录，不自动占用电脑补门禁。

P0合同冻结不表示P1通过。完整恢复、客户端呈现或Linux任何缺口均保留未通过／未验证，停止依赖它们的P2。验证报告附真实日志／hash／反例、进程及资源收尾，不以单机建模代替正式双人验收。
