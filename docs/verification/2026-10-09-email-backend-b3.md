# 邮箱后端 B3 房间管理候选验证

日期：2026-10-09。分支：`codex/email-backend-design`，承接 B2 `8c7bb5a`。状态：B3 候选实现已集成，静态及真实 PostgreSQL／HTTPS 门禁通过；用户对[精确数据计划](../planning/2026-10-09-backend-b3-local-data-plan.md)明确回答“允许”后，应用指定迁移并执行限定测试房间到期清理。人工及后续 B4／B5 尚未验收，不将自动门禁等同于完整首期完成。

## 范围与证据

- 正式房间 API：创建、本人详情／分页、版本化名称／容量更新、明确确认解散、168 小时内恢复。默认桌宠房容量 2 且不能解散，共同房容量 1–12、最多 3 个有效房间。模板标识持久保存，不开放布局编辑或全网目录。
- 管理事务：email→User→Session→owner quota→Room→有序 Pet state，锁后重新读事实，提交前重验会话／恢复期限；精确版本冲突、配额／席位冲突均拒绝。解散仅清仍实际归属目标房间的团子，恢复不迁回；public owner marker 不保留座位。
- 原操作确认：随机 requestId 在同账号全部操作间唯一，规范请求摘要、原 roomId／version／operation 随审计持久保存。相同重试重放原确认，更换参数／操作拒绝，重试不能绕过会话撤销；返回确认不代表当前房间状态。
- 维护：逐房事务、每批至多 50、期限／ID 游标、失败回滚后继续并推进游标。删除成员／房间与残余归属，保留身份、审计及 outbox；生产 worker 默认关闭，开启后先等待维护周期。测试只限定新生成的目标 ID。
- 前向迁移 `20261009031410_RoomManagement`：审计、持久 outbox、模板列／回填／检查与到期索引。outbox 行表示待投递意图；B4 尚未实现正式 Hub、公共码、租约、正式准入或 outbox 投递。B5 UI 尚未接入。

## 已执行门禁

| 项目 | 实际结果与边界 |
|---|---|
| Release build | Server.Tests（含全部 B3 PG／HTTPS 测试源码）0 警告、0 错误；集成另列 |
| Server 非数据库 | 153/153，0 跳过；配置、政策、真实 Npgsql 查询翻译、2 项可控 worker 周期反例及既有账号／管道回归；查询翻译测试在 ConnectionOpening 前拦截，不建立网络连接 |
| Protocol | 9/9，0 跳过（Release） |
| Client.Sync | 77/77，0 跳过（Release）；含既有原型真实 WebSocket 回归，不能代表 B3 正式 HTTP 或多人功能 |
| 模型／SQL | 模型测试通过，EF has-pending-model-changes 报无差异；生成 B1→B3 幂等 SQL，获批后仅前向应用 RoomManagement；fixture 实际检查无 pending 迁移 |
| 格式／脚本 | 本批代码 whitespace、PowerShell AST 和 diff 检查通过；-B3 先 dry-run，用户允许后才执行；错误运行名／同时指定 B1、B2 或初始化的 4 项输入均拒绝 |
| PostgreSQL／HTTPS／受控 SMTP | **105/105，0 跳过、0 失败**；B1 34、B2 19、B3 52；真实 PostgreSQL 独立连接、Kestrel HTTPS、本机受控收信，未发真实邮件。独立只读代理复核 TRX counters 与逐项 outcomes，无 ErrorInfo |
| 未获授权的 B3 guard | 52 项均在 fixture 前拒绝，0 通过／52 未执行；B1 flag 刻意打开且连接参数为非法虚构数据库，若绕过 B3 guard 即会失败。该结果只证明拒绝入口，不能计为 52 项 PG 通过 |
| 人工／环境 | 持续运行的生产清理 worker、真实 SMTP、B4 多人产品客户端／Hub、备份还原、OS 强杀／断电与人工产品行为均未验证 |

静态证据保留在忽略目录：`artifacts/backend-b3/static-tests/b3-static-final.trx`、`protocol-tests/b3-protocol.trx`、`client-tests/b3-client-regression-configured.trx`、`unauthorized-guards/b3-unauthorized-guards.trx`；生成 SQL 为 `artifacts/backend-b3/room-management-idempotent.sql`。最终真实集成 TRX 为 `artifacts/backend-foundation/postgres-tests/20261009-052552-0072851/backend-postgres.trx`，测试耗时约 48 秒。

B3 52 项拆分为管理 31、到期 16、正式 HTTPS 3、真实恢复／清理 2。管理覆盖第三间配额与同版本竞争、私密长期席位／公共实际占位缩容、14 个逐步回滚反例、会话失效、七天恢复及请求重放。到期覆盖精确期限、非目标保护、每个删除步骤回滚、版本耗尽、游标越过失败目标、独立清理连接幂等及取消回滚。

HTTP 真实验证创建默认容量与模板、名称规范化、详情／分页／修改／解散／恢复、跨账号 404、桌宠保护、CSRF／Origin、未知及重复 DTO 字段、空／重复查询、撤销会话、新宿主／新会话重放原版本。恢复竞争测试在实际管理服务 before_commit 持锁，推进到精确期限后由第二 PG 连接读取候选并等待同 owner 锁，恢复回滚而清理只删除指定房间，身份与非目标图保留；另一项验证期限前恢复后不会被后续清理删除，也不迁移团子。

## 修复与保留的失败记录

1. 独立只读审查发现旧 -B1 入口会迁移全部 pending，可能顺带 B3；已指定各阶段精确迁移，并让 foundation 初始化逐项检查授权。子进程阶段许可先设为 0，再按本次选择打开，不能继承其他运行的许可。本次 B3 正确目标／单项 pending 路径已实际执行；旧 -B1 面对尚未应用 B3 的拒绝路径仍只做源码审查，不回退数据库来制造反例。
2. 新模板约束会使既有 foundation 桌宠 seed 在到达原断言前失败；两处 INSERT 已显式 desktop-pet-default，注册默认房同样显式写模板。迁移回填旧桌宠房，模型快照已核对一致。
3. helper 现在明确要求 EF 事务，避免 advisory lock／audit 在事务外悄然失效。expiry 测试 seed 同步事务边界，worker 避免启动立即清理；2 项可控时钟测试证明初次等待／取消零 scope，首次 tick 只尝试一次且失败后等待下一周期，不能触达数据库。
4. 首次 Client.Sync 回归漏传既有 RealNetworkAcceptanceTests 必需的 DANGO_SYNC_SERVER_PATH，74 通过／3 环境配置失败，记录保留为 `client-tests/b3-client-regression.trx`。只补当前最终构建 Server.exe 的运行环境，未修改测试或生产行为；最终 77/77 通过，三个短命原型服务正常退出。
5. 首次格式检查指出新迁移源文件 CRLF 与仓库 LF 规则不符；仅规范本次迁移文件行尾，后续检查通过。

## 五项必查与未证明项

| 要求 | 本批已执行证据 | 尚未证明 |
|---|---|---|
| 正确性 | Unicode／容量／版本／类型／期限政策、真实 CRUD／错误 HTTP 输入、模板迁移与数据库检查、Npgsql 查询及分页实际执行 | B5 UI／产品人工；B0 foundation 约束组本次未重跑 |
| 一致性 | 独立 PG 连接配额／同版本／恢复期限竞争、逐步故障回滚、审计／outbox 恰好一份，解散仅解除实际目标归属 | B4 准入／占位竞争及共享状态同步；未采集全体旧房间的前后快照 |
| 安全性 | 跨账号读写拒绝、撤销／过期会话、CSRF／Origin／严格 DTO、真实 HTTPS；精确物理目标与单项迁移，清理只限定本次 ID | 正式 Hub 命令／广播授权、生产最小权限角色、真实 SMTP |
| 可靠性 | 新宿主／会话读取持久原确认、丢弃首次正文后重放、维护重跑幂等、逐房失败回滚且继续；身份与测试中非目标图保留 | 持续生产 worker、真实备份还原、安全代际失效、OS 进程强杀／断电恢复；outbox 尚未投递 |
| 扩展性 | owner/status/id、recover_until/id、actor/request、outbox 索引；分页默认20最大50、维护批50；未加分布式依赖 | 实际数据量／容量／长时负载、查询计划和生产运维 |

## 数据、进程与交付

仅启动并连接获批 `LocalApplicationData/Dango/BackendDevelopment/run20261008b0/pgdata` 与 `dango_backend_b0`，只应用指定迁移、写虚构数据并删除本次测试范围内到期共同房。旧房间未作为永久清理目标；未重置／还原／批量清空。迁移按授权回填既有模板，既有虚构 email job 可按回归策略推进状态；未采集所有旧房间逐行前后快照，不能声称所有旧数据完全未改。

脚本在 finally 核实本次实例路径、PID 和创建时间后正常 fast stop；结束无 postmaster.pid、记录端口无监听、启动 PID 已退出、Dango.Server 进程为 0。正式 HTTPS／受控 SMTP 测试宿主已 Dispose、测试进程退出；房间清理 worker 始终未启用。数据、DPAPI 凭据、B1 代际、既有密钥及 TRX 保留，未删除。用户既有 `postgresql-x64-18` 服务保持 Running，没有连接、配置或停止操作。

本次 Client.Sync 原型回归进程也已退出。受保护 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll` SHA-256 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`，本批没有 Desktop／native 更改。

候选代码本地提交为 `0befc48`，随后只更新本次集成结果文档；本任务不改 main、不推送、不部署、不更新安装、不清理 Worktree。当前 main 为 `f10121b`，本次数据运行前后相同，不能沿用旧 B2 的 main hash 断言。独立管理／到期 Worktree 与旧客户端草稿保留。B3 自动门禁已完成，人工未验收；下一批为已批准首期计划中的 B4 正式准入、租约与同步恢复，新增迁移／数据操作仍需精确目标授权。
