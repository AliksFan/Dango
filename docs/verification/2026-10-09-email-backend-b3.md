# 邮箱后端 B3 房间管理候选验证

日期：2026-10-09。分支：`codex/email-backend-design`，承接 B2 `8c7bb5a`。状态：B3 候选实现已集成，静态门禁通过；真实 PostgreSQL／HTTPS 及人工尚未验收。数据库新增迁移和永久清理等待[精确数据计划](../planning/2026-10-09-backend-b3-local-data-plan.md)授权。

## 范围与证据

- 正式房间 API：创建、本人详情／分页、版本化名称／容量更新、明确确认解散、168 小时内恢复。默认桌宠房容量 2 且不能解散，共同房容量 1–12、最多 3 个有效房间。模板标识持久保存，不开放布局编辑或全网目录。
- 管理事务：email→User→Session→owner quota→Room→有序 Pet state，锁后重新读事实，提交前重验会话／恢复期限；精确版本冲突、配额／席位冲突均拒绝。解散仅清仍实际归属目标房间的团子，恢复不迁回；public owner marker 不保留座位。
- 原操作确认：随机 requestId 在同账号全部操作间唯一，规范请求摘要、原 roomId／version／operation 随审计持久保存。相同重试重放原确认，更换参数／操作拒绝，重试不能绕过会话撤销；返回确认不代表当前房间状态。
- 维护：逐房事务、每批至多 50、期限／ID 游标、失败回滚后继续并推进游标。删除成员／房间与残余归属，保留身份、审计及 outbox；生产 worker 默认关闭，开启后先等待维护周期。测试只限定新生成的目标 ID。
- 前向迁移 `20261009031410_RoomManagement`：审计、持久 outbox、模板列／回填／检查与到期索引。outbox 行表示待投递意图；B4 尚未实现正式 Hub、公共码、租约、正式准入或 outbox 投递。B5 UI 尚未接入。

## 已执行门禁

| 项目 | 实际结果与边界 |
|---|---|
| Release build | Server.Tests（含全部 B3 PG／HTTPS 测试源码）0 警告、0 错误；不是集成通过 |
| Server 非数据库 | 153/153，0 跳过；配置、政策、真实 Npgsql 查询翻译、2 项可控 worker 周期反例及既有账号／管道回归；查询翻译测试在 ConnectionOpening 前拦截，不建立网络连接 |
| Protocol | 9/9，0 跳过（Release） |
| Client.Sync | 77/77，0 跳过（Release）；含既有原型真实 WebSocket 回归，不能代表 B3 正式 HTTP 或多人功能 |
| 模型／SQL | 模型测试通过，EF has-pending-model-changes 报无差异；生成 B1→B3 幂等 SQL，仅源码操作、未应用 |
| 格式／脚本 | 本批代码 whitespace、PowerShell AST 和 diff 检查通过；-B3 工具只 dry-run，无执行开关；错误运行名／同时指定 B1、B2 或初始化的 4 项输入均拒绝 |
| 未获授权的 B3 guard | 52 项均在 fixture 前拒绝，0 通过／52 未执行；B1 flag 刻意打开且连接参数为非法虚构数据库，若绕过 B3 guard 即会失败。该结果只证明拒绝入口，不能计为 52 项 PG 通过 |
| 人工／环境 | 正式 PostgreSQL／HTTPS、生产清理 worker、真实 SMTP、多客户端、备份还原、OS 强杀／断电与人工产品行为均未验证 |

测试证据保留在忽略目录：`artifacts/backend-b3/static-tests/b3-static-final.trx`、`protocol-tests/b3-protocol.trx`、`client-tests/b3-client-regression-configured.trx`、`unauthorized-guards/b3-unauthorized-guards.trx`；源码生成 SQL 为 `artifacts/backend-b3/room-management-idempotent.sql`。计划的 PG 回归 105 项（其中 B3 52），已发现并编译，尚未在授权目标执行。

## 修复与保留的失败记录

1. 独立只读审查发现旧 -B1 入口会迁移全部 pending，可能顺带 B3；已指定各阶段精确迁移，并让 foundation 初始化逐项检查授权。子进程阶段许可先设为 0，再按本次选择打开，不能继承其他运行的许可。尚未在 PG 上执行这些新保护路径。
2. 新模板约束会使既有 foundation 桌宠 seed 在到达原断言前失败；两处 INSERT 已显式 desktop-pet-default，注册默认房同样显式写模板。迁移回填旧桌宠房，模型快照已核对一致。
3. helper 现在明确要求 EF 事务，避免 advisory lock／audit 在事务外悄然失效。expiry 测试 seed 同步事务边界，worker 避免启动立即清理；2 项可控时钟测试证明初次等待／取消零 scope，首次 tick 只尝试一次且失败后等待下一周期，不能触达数据库。
4. 首次 Client.Sync 回归漏传既有 RealNetworkAcceptanceTests 必需的 DANGO_SYNC_SERVER_PATH，74 通过／3 环境配置失败，记录保留为 `client-tests/b3-client-regression.trx`。只补当前最终构建 Server.exe 的运行环境，未修改测试或生产行为；最终 77/77 通过，三个短命原型服务正常退出。
5. 首次格式检查指出新迁移源文件 CRLF 与仓库 LF 规则不符；仅规范本次迁移文件行尾，后续检查通过。

## 五项必查与未证明项

| 要求 | 本批已执行证据 | 真实集成仍需证明 |
|---|---|---|
| 正确性 | Unicode／容量／版本／类型／期限政策、严格 DTO 配置、模型约束与模板 SQL、查询可翻译 | 正常 CRUD、错误 HTTP 输入、模板回填及真实约束执行 |
| 一致性 | 明确事务 guard、统一锁边界、唯一审计／outbox 索引与幂等合同 | 独立连接配额／同版本／恢复到期竞争、每步故障回滚、审计／outbox 恰好一份 |
| 安全性 | HTTP 复用身份／归属、CSRF／Origin／严格 JSON；B3 guard 早于数据库 fixture；维护默认关闭 | 跨账号读写、撤销／过期会话、真实 HTTPS 与精确目标数据库保护 |
| 可靠性 | 持久审计／outbox 不设房间 FK，候选逐房回滚／有界重试，不用已发送替代待投递 | 新宿主重放、维护重跑、真实备份还原、安全代际失效、OS 进程中断恢复 |
| 扩展性 | owner/status/id、recover_until/id、actor/request、outbox 索引；分页默认20最大50、维护批50；未加分布式依赖 | 实际数据量／容量／长时负载、查询计划和生产运维 |

## 数据、进程与交付

本轮未连接、启动或修改 B0 独立实例；未应用迁移、写入种子或删除房间。只读检查时该实例无 postmaster.pid、记录端口无监听；数据／密钥保留。用户既有 `postgresql-x64-18` 服务保持 Running，没有连接、配置或停止操作。

正式 HTTPS／SMTP／到期 worker 未启动。本次 Client.Sync 原型回归启动的进程已退出。受保护 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll` SHA-256 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`，本批没有 Desktop／native 更改。

仅任务分支本地提交；本任务不改 main、不推送、不部署、不更新安装、不清理 Worktree。当前 main 已由其他工作推进到 `f10121b`，本批保留其现状，不能沿用旧 B2 的 main hash 断言。独立管理／到期 Worktree 与旧客户端草稿保留。B3 尚不能标为已验收，下一门禁是获批后的本机真实数据测试。
