# 邮箱后端 B0 基础实现验证

日期：2026-10-08。任务分支：`codex/email-backend-design`，起点 `1c47930`。范围依[首期设计](../planning/2026-10-08-email-backend-design-proposal.md)、[实施计划](../planning/2026-10-08-email-backend-implementation-plan.md)及[ADR 0006](../adr/0006-email-accounts-and-persistent-room-state.md)。B0 自动基础门禁通过，候选待审阅；完整首期与 Phase 3 未验收。

## 实现范围

- 根级规则新增正确性、一致性、安全性、可靠性和扩展性五项必查，设计和计划按其列出能失败的门禁。
- 正式配置拒绝缺项、错误数据库／非 loopback 目标、不安全 HTTPS 来源及仓库内／联接点密钥目录；连接参数有界、UTC、输出和 JSON 序列化脱敏。此配置尚未接正式运行入口。
- 集中 Identity EF／PostgreSQL 模型，账号、主要团子、默认／共同房、成员和唯一归属；唯一、外键、检查约束、乐观并发字段和必要索引。初始 migration、Designer 与 Snapshot 已生成，无模型漂移。
- 独立实例工具仅执行明确计划，创建时拒绝既有目标，复用时拒绝数据目录联接点；当前用户 ACL、DPAPI 保存数据库凭据，临时明文输入已移除。只停止本次实例，不删除数据或操作现有 PostgreSQL 服务。
- 数据库约束测试先核对命名库、PostgreSQL 18 和物理 data_directory，再迁移和写虚构数据。多数测试事务回滚；一套虚构初始化对象保留，独立连接读取并在第二次进程启动后核对固定 IDs。

原 `Program.cs`、匿名管道启动合同、内存同步原型、Desktop、Client.Sync 和 Protocol 源码未修改。正式 HTTP／Hub、账号注册登录、邮件发送、房间 CRUD、配置选择和 Windows 在线入口属于后续批次。

## 实际验证

| 层次 | 结果 | 证据边界 |
|---|---|---|
| 构建／静态 | Server 与 Server.Tests Release 0 警告、0 错误；新增范围格式检查、差异检查通过 | 不代替数据库与交互 |
| 迁移生成 | 幂等 PostgreSQL SQL 生成成功，`has-pending-model-changes` 无漂移 | 没有回滚或删除迁移 |
| Server | 36/36，零跳过，含新增配置／模型 12 项及原型回归 | 原型仍是 Phase 2A 的边界 |
| PostgreSQL | 最终 8/8，零跳过；独立实例停止后重新启动，同库再次 8/8 | 真实迁移重跑、唯一／外键／容量／生命周期／无房／有限坐标／Schema 顶层检查与独立读库；非正式注册接口验收 |
| Client.Sync | 补齐本轮最终服务器路径后 30/30，零跳过 | 真正原型 WebSocket 恢复回归，非正式持久 Hub／双 Windows M7 |
| Protocol | 6/6，零跳过 | 现有协议未改 |
| 人工／部署 | 未进行本批人工功能验收，未部署 | 无产品登录或房间管理 UI 可验收 |

去重共 **80 项通过**；重复重启运行不重复计数。TRX 与迁移 SQL 位于忽略的 `artifacts/backend-foundation/`；工具为每次数据库运行建立独立结果目录，不覆盖后续证据。

## 已核实并修正的问题

1. `timestamptz + INTERVAL '7 days'` 受会话时区／夏令时影响，改为固定 `168 hours` 并固定连接 UTC；实际 `America/New_York` 跨夏令时写入 168 小时成功，167 小时失败。模型、迁移和测试一致。
2. Windows 的 PostgreSQL 后台进程可继承 pg_ctl 输出管道，导致启动工具等待 EOF；改为进程退出后有界读取，记录后台输出已分离。首次阻塞实例经精确 PID／路径核验后停止，继续同一目录，未重新初始化或删除数据。
3. 首次真实 PostgreSQL 测试 6 通过、1 失败：引用父记录的 `ON DELETE RESTRICT` 返回 `23001`，测试误期待外键写入的 `23503`。实际删除被拒绝，核实后改用驱动 `RestrictViolation` 常量，未弱化数据库约束；最终 8/8。
4. Client.Sync 首次 27 通过、3 失败，原因是实网门禁要求显式 `DANGO_SYNC_SERVER_PATH`；指向本轮 Release apphost 后全组 30/30。未跳过实网用例或修改其源码。
5. 工具失败清理补充启动尝试标记、PID／可执行文件／数据路径／创建时间核验；真实测试失败后的实例已停止。身份无法核验时保留并明确报告残留，不猜测停止其他进程。

## 环境与保留数据

用户明确批准本批 `run20261008b0` 精确目录／库的初始化、迁移和虚构约束写入，未批准备份还原。实例使用已安装 PostgreSQL 18.6，IPv4 loopback、动态端口、独立 `dango_backend_b0` 和 `dango_b0_owner`。数据位于当前用户 LocalApplicationData 的 `Dango/BackendDevelopment/run20261008b0/pgdata`，不提交开发机绝对路径、个人端口或凭据。

实际端口、PID／停止状态与物理目标记在仓库外状态及本轮工具输出；`state.json` 保存非敏感目标，`credential.dpapi` 保存当前用户加密凭据，`foundation-probe.json` 仅保存虚构 IDs。保留初始 Schema／migration history 及一套虚构 User／Pet／默认房／成员／归属记录，多数反例数据已事务回滚。数据库、日志与加密凭据未删除；测试实例已停止，用户原 PostgreSQL 服务继续运行。未启动 Docker、SMTP、对象存储或正式 Dango 服务。

复跑需先确认精确环境和数据授权，再使用[本地工具](../../eng/invoke-backend-postgres-tests.ps1)；不带 `ExecuteApprovedPlan` 只展示计划。复用不带 `Initialize`，工具绝不自动重置已存在的目录。示例不含机器专属二进制路径：

```powershell
.\eng\invoke-backend-postgres-tests.ps1 -RunName run20261008b0 -PostgresBinDirectory <已核验的PostgreSQL18二进制目录>
```

## 尚未验证与交付边界

账号初始化事务故障、同邮箱注册并发、完整配置七字段／范围、房间额度和最后名额并发、越权 API／广播、真实 SMTP、长期运行、容量、备份还原及恢复锁定尚未验证。固定对象唯一只证明“最多一个”，不能据此声称正式活动账号初始化永不缺对象。恢复锁定与专用恢复挑战是后续设计要求，B0 未实现。

当前成果仅位于本地隔离任务分支；提交状态以本轮最终报告为准。无 main 合并、远端推送、部署、日常安装更新、发布标签或 DLL 替换；完整 Windows／Phase 2A／M7 和 Phase 3 的人工验收保持开放。
