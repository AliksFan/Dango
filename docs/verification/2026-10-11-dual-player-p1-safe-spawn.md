# 双人 P1：安全出生隔离恢复验证

日期：2026-10-11。范围按[已批准计划](../planning/2026-10-11-dual-player-p1-safe-spawn-plan.md) S0–S4 与[编码前冻结合同](../planning/2026-10-11-dual-player-p1-safe-spawn-contract.md)。**Windows 隔离恢复验证通过；P1 整体仍不准入，P2 停止。** 不将新安全出生结果写成原完整 checkpoint／历史重放预算通过。

## 1. 来源、文件与授权

主实施分支 `codex/ordinary-room-physics-admission`。本地 main `a4acdeb` 已经通过 `844e944` 合入，且为本轮 HEAD 祖先；其中子 Agent 规则已实际遵守：扫描用 `gpt-6-luna`，复杂实现和审查用 `gpt-6.1-sol`／`high`。第一次吸收旧 main 的 `6135c03` 保留，不把它冒充最新合入。

合同提交 `d7e7386` 在编码前冻结。纯语义来源 `cdafe21` 集成为 `3a0da26`；native 工具来源 `fdab3b6` 集成为 `2856b5c`；审查修正来源 `981a70a` 集成为 `3517750`。两位实现 owner 使用独立工作树，根拥有组合验证脚本和报告。子树保留，未删除或归档。

修改仅包含 `eng/tools/Dango.SafeSpawnAdmission.Core/`、`eng/tools/Dango.SafeSpawnAdmission/`、新的语义测试和必要测试项目引用、对应计划／报告。工具不进入生产启动链；本轮没有修改正式 Server、Protocol、Client、Desktop 入口、solver、authority ABI 或参数，也未接家具、皮球和手套。

## 2. 纯语义与构建

- Core 和 console Release 构建均为零警告、零错误；独立 C probe 使用仓库原 native 源码及原 pinned Box2D 构建，不生成替换 DLL。
- 静态收尾：7份相关文档259个相对链接全部可解析，PowerShell语法零错误，`git diff --check`通过。
- 根集成 `SafeSpawnRecoverySemanticsTests`＋`AuthorityAdmissionTests`：49／49，通过、零失败／跳过。证据为 `artifacts/p1-safe-spawn/semantics-authority.log` 和 `test-results/safe-spawn-semantics-authority.trx`。native 工具后续修正不影响这组已验 Core／authority 源码，未无理由重跑。
- 覆盖严格 codec／64KiB、成员生命周期、owner／epoch／revision／Tick 条件写、最后合法记录保留、ready／输入 fence、350ms watchdog、30秒容错、控制及观察租约、Dispose 前最新事实复核、发布前事实竞态。
- Core 审查发现旧绑定未撤销、同成员有效控制租约歧义、重复 receipt 可复活 Jump。三项先复现失败后修正；原失败只有子 Agent console 工具输出 `c1a698`，没有持久 TRX／日志，不能称作磁盘证据。根49项TRX是修后集成证据。ReceivedAt 去重仅可控 clock fixture，不宣称正式 P2 ordinal／幂等账本已实现。

## 3. 最终真实 native 正常场景

最终证据目录 `artifacts/p1-safe-spawn/final-r2/`，外部 runner exit0。正常 suite **43／43**，零跳过：managed owner Create／Dispose **33／33**、Reset **0**、Add **54**、Remove **2**、实际固定步 **6651**，native allocation **0→0**。

一／两体出生各完整600固定步；Tick0检查零继承速度、支撑和冷却，首步保留原重力。推挤、堆叠、空中分别组合重连、接管、内存留存暂停，九项均续跑120步，与相同有效 neutral 输入的对照比较全部 raw＋authority 字段的 float bit／二进制编码，一致且不重建 world／实体／epoch。旧持续意图先通过 Core 实际接收，再经转换清除，native 惯性继续。

实际 Remove 下层后，幸存实体与原轮廓保留并自然下落。新成员先在实际 raw 帧预检：容量或空间不足时零 Add／Step／Reset；原槽位真正重入时 Add 新实体，旧实体与速度保留。候选第二体失败、取消、异常及最新成员变化均整组回收，未发布部分就绪；跨线程 Read／Dispose 在 native 调用前拒绝。

独立 native probe 正常 **93 checks／0失败**，两个 world均释放、allocation0→0；直接读取全部节点初始线／角速度。真实 publication 故障保留完整最后安全帧及节点状态，下一步拒绝；真实 Add 可在成员数量已经改变后报 `-422`，只保留最后安全发布组并停止，不声称物理回滚。

## 4. 跨进程恢复和预算

每项 producer 确实退出后，另一个实际 PID 的 consumer 只读取虚构领域元数据与独立处置 receipt。receipt 的 metadata SHA、producer PID、source epoch 与记录／producer／consumer全部关联核对。可信 receipt 仅是测试 fixture，不是生产认证或可靠持久故障事实。

| 处置 | 原世界前缀步 | 元数据 bytes | 校验到首健康共同帧 | 结果 |
|---|---:|---:|---:|---|
| 确认丢失 | 120 | 601 | 109.151 ms | 新epoch、整组发布、当前挑战ready通过 |
| 确认丢失 | 1200 | 602 | 93.061 ms | 同上 |
| 合法释放 | 120 | 601 | 110.481 ms | 原30秒／无控制和观察租约最终复核、实际Dispose后新epoch |
| 合法释放 | 1200 | 602 | 104.847 ms | 同上 |
| 已知故障 | 120 | 601 | 不启动出生 | blocked、零Create／发布／ready |
| 未知处置 | 120 | 601 | 不启动出生 | 同上 |

四次成功 consumer 的首帧 Tick1、Ready／Published均true，随后各完整600步，world Create／Dispose1／1、Reset0。首健康完整状态 digest均为 `7FD4AC7B3597F73E563AB26945CBFF910B13670860FBEE4692E519761F91C075`，不依赖两种历史长度。601／602 bytes 的差异只来自成功 Tick 数字位数，不含任何逐帧物理历史。全部满足65536 bytes／5秒预算；计时包含codec、claim、计划、Create／Enable／Bounds／整组Add、Tick0、首步、全组校验和发布，不含构建、进程启动与后续600步。

fault producer 的同一个独立 native world实际运行120步、publication fault／`-422`、最后安全 Tick120，随后释放；不是健康 world 与无关故障 world拼接。该 probe **203 checks／0失败**、创建释放1／1、allocation0→0，冷 consumer在native创建前拒绝。

lost producer验证实际owner释放和进程终止，不宣称生产服务非正常崩溃恢复。完整checkpoint Export／Import继续unsupported。

## 5. 错误变体与审查修正

**17／17** 个变体实际 exit1，且指定 oracle判红，资源门仍通过、无异常冒充成功：`healthyReset`、`oldMove`、`oldJump`、`halfReady`、`skipSpawnOverlap`、`skipSpawnBounds`、`skipSpawnGround`、`oldEpoch`、`noReason`、`faultAutoRebirth`，以及Core fixture的 `observerIgnored`、`earlyRelease`、`badRecordAsNew`、`onlyPetId`、`oldWriter`、`unknownFaultAsLost`、`staleReady`。Core反例明确只证明隔离语义。

审查修正与证据保留：

- 出生规划原来允许左右恰好相切、非法bounds和第三体；先跑 `review-evidence/planner-before-fix.*` 四红／exit1，再修正为严格间隙、有限有序边界与容量门；`planner-after-fix.*`全绿／exit0。
- 实际帧原来只验缩放后的显示轮廓，缺raw地面门。真实冻结 DLL Add成功后，左右／地面三项均为显示轮廓合法而raw越界，旧Healthy漏检。`containment-before-fix.*`三红／exit1，修后`containment-after-fix.*`全绿／exit0。使用raw `Core+Contour`及既有Box2D float `.005 p` slop，径向节点半径已在raw contour中，不重复添加、不改容差。
- Tick0失败原来只记录红灯却继续Step／Commit，现共用拒绝门先Abort，再停止并Dispose。真实私有候选读取结果显式注入只读DTO速度／支撑／冷却污染，检查零Step／epoch／ready／发布、最后合法记录保留及整组释放。这三项是fixture污染反例，不称solver轨迹；没有修前false-publication实验日志。`review-final-containment.*`保存修后11项检查。
- 根第一轮`final/`保留：正常suite通过，但runner把空检查列表展开为null，误判正确oldMove反例缺一个检查名而停止。先用现有JSON复现null组合，再只修脚本空集合规范化；没有放宽实际门。最终`final-r2/`完整重跑通过。

## 6. 来源链与资源

最终 manifest保存67个来源文件的相对名／大小／SHA、103个证据文件清单、所有实际PID／exit／CPU／墙钟与各断言。runner在前后复核源码、tool／probe／DLL未变化。30个managed进程全部已退出，17个预期失败、其余13个exit0；独立C子进程的实际exit／资源也核对。各进程墙钟合计约103.991秒，不把它当恢复计时。

| 最终来源 | SHA256 |
|---|---|
| 来源内容（含根runner） | `EF008071303EEAA9ABFCC53DF854F98342F417BB961DFAC9FB0E5380096C1C13` |
| 实际ABI参数bytes | `7257CF6852ED6EF932485F02610E3C87D8B9BE99D21EBF3626A22D0305245506` |
| console DLL | `B92B18E0A267790227F2CC2558667ABB6B32FD36F6ECEB5F6A286FF73D6EE95C` |
| 根独立probe EXE | `3893A0F82CAAE95D10DB9E3A2B6F5A317B9DD8242C61AE8150CADB88DB4701C1` |
| 冻结R1 DLL | `59EC861EE748BA93E661D058EEBD01D3E8A04ABF9D24F0376BACB4D98015B6DC` |

根在当前树重建独立probe，其build路径与子树不同，EXE hash不同；最终只认上述根binary与本轮实际来源链。A／authority／日常／B replay／R1五项受保护产物均再次匹配计划§8，不覆盖旧manifest／证据。

收尾进程扫描未发现本轮SafeSpawn console／probe存活；工具未监听端口、未启动后台服务、未操作数据库或对象存储。WSL只读列表仍只有Stopped的docker-desktop，未启动Linux／Docker／WSL。旧家具验收窗口与其他任务资源未操作；ignored证据／构建产物和三个任务工作树保留。

## 7. 仍未验证与交付状态

Linux同源native构建／运行、真实语义呈现窗口／多客户端、正式账号／Hub／数据库事务／持久fault和lease事实、真正服务重启、用户可见恢复提示及人工双机均**未验证**。DTO／clock／store／处置receipt只是虚构fixture，不代表正式认证、CAS事务或故障持久化。

本轮完成S0–S4隔离实现与自动验证，未标记P1整体验收。继续前需要可用Linux环境及明确的真实窗口验收增量；P2正式接线仍停止，不自动占用前台补观察门。后续真实窗口、多客户端和双机分别保留原门禁。

交付仅本地任务分支源码／报告提交；没有合回main、推送、安装、部署、启动新外部资源或替换日常DLL。
