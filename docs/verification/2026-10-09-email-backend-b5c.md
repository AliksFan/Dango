# B5C 正式房间连接候选验证

日期：2026-10-09；2026-10-10 更新。依据用户“持续推进直到需要人工协助／验收”及[B5C 增量](../planning/2026-10-09-backend-b5c-session-execution.md)，在 `codex/backend-b5-windows` 从 `8ea00a9` 推进。用户对[六项精确数据计划](../planning/2026-10-09-backend-b5c-local-data-plan.md)明确回复“批准”；真实执行发现并修复 Hub 错误映射缺陷，调整测试请求节奏后，**第三轮六项全部通过，零失败／跳过**。完整 B5 未验收。

## 本次实现

- `FormalAccountClient.RoomAccess`、正式 HTTP／SignalR transport：同一受控账号 Cookie、固定 HTTPS origin、正常产品 TLS、逐 unsafe HTTP fresh CSRF，含 negotiate 和 LongPolling POST／DELETE；绑定前后校验、禁止重定向与自动变更重试，快照／消息有界。
- `FormalRoomSession`：本进程实例、30 秒租约与约 10 秒心跳、控制／观察、明确入房／接管／离房／精确结束租约；回执和当前事实分开，未知操作原键／正文保留，确定回执后读取失败必须先对账。
- 断线只核对并 Attach 本实例仍有效的租约，不自动 Enter、接管或复活旧租约。真实事件类型只触发合并读取；旧通知、连接、呈现令牌、到期回调和补偿均隔离。
- Windows 居中 `OnlineRoomPanelView` 与单 owner controller，目录／详情路由、公共码解析与明确入房、房主重置确认、离房确认；关闭只 Suspend，不发 Leave／EndLease，秘密展示清除。账号切换释放原绑定，房间拒绝不会误清账号。
- 当前只有正式连接、角色、归属与成员快照；Windows 没有调用 `ConfirmPresentationReady`。服务端没有 Move／Action 或批准的坐标映射，没有把旧原型或本地可动单人画面接成正式多人玩法。本地单人和原管理入口保留。

## 实际验证

| 门禁 | 结果与证据 |
|---|---|
| Client 静态 | 初始352／352；真实集成后的错误包装修复增加12项反例，最终 **364／364**，零失败／跳过。最终 TRX：`artifacts/backend-b5c/static-client/client-b5c-hub-error-fixed.trx`；显式排除3项旧 `RealNetworkAcceptanceTests` |
| Desktop STA／受影响回归 | 去重 **104 项全部通过**。首组60、保留回归44；session修复后5项、Hub错误映射修复后5项Windows生命周期复验均通过，属于前60的重复，不累加。TRX：`artifacts/backend-b5c/static-desktop/desktop-b5c.trx`、`desktop-b5c-preservation.trx`、`desktop-b5c-final-lifecycle.trx`、`desktop-b5c-hub-error-fixed.trx` |
| Server 无数据库 | **186／186**，零失败／跳过；`artifacts/backend-b5c/static-server/server-static-b5c.trx`，筛选 `TestCategory!=PostgresIntegration` |
| Release | 集成 Desktop／测试构建通过；错误映射及测试节奏修复后 Server.Tests 最终零警告／错误，随后执行实际六项真实测试 |
| 离屏 WPF | 1024×640、336×640、336×360 下6页共 **18状态**：横向居中误差0，无横向滚动，低高度可滚动到操作。图在 `artifacts/backend-b5c/visual/`；检查了宽状态、窄入房确认和低高度公共码底部。全部是标明虚构数据的离屏布局检查 |
| runner／fixture guard | PowerShell AST无错误；代理四类资源前拒绝及根六个筛选逃逸拒绝通过。B5C dry-run仅展示目标，不解密凭据或启动／连接；`--list-tests`发现精确六项。独立批准、固定目标、互斥阶段、正向FQN AND收窄和每fixture资源回执代码已编译 |
| 真实 HTTPS／Hub／PG | 原范围三轮分别4／6、5／6、最终 **6／6**；均零跳过。最终 TRX：`artifacts/backend-b5c/postgres-tests/20261010-014147-3870623/backend-postgres.trx`，同目录实际六份 fixture 与资源回执；传输为真实 HTTPS LongPolling |

SignalR 静态反例经过其 LongPolling runtime 和受控 HTTP handler，覆盖握手、七个正式方法及通知；没有真实监听或服务端。代理首次未筛选客户端全量测试的3项旧真实网络验收因缺少 `DANGO_SYNC_SERVER_PATH` 在服务启动前失败；随后使用明确静态筛选，未把这3项记作通过。

本地房保留回归使用之前已验证且存在于忽略测试输出的隔离物理模块，SHA256 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`；本次没有复制或修改该模块，也未替换生产库。

## 独立审查与可失败反例

1. 连接额度拒绝与会话失效的共同服务端码不能据此注销账号；修前两个反例失败，修后仅 `authentication_required` 触发失效清理。
2. 确定回执后当前读取失败不能再次新提交，增加 `ReconciliationRequired` 门；未知入房重试成功必须消耗原确认页，回到当前状态。
3. 补齐真实 `expired`、`removed`、`ban_set`、`ban_removed` 通知，四个反例证明触发读事实，不按通知直接改权限。
4. 延迟快照扣完整请求耗时：初始／最终响应耗尽期限不授予连接／呈现资格，部分耗时的 watchdog 和确认门使用同一剩余期限。
5. 旧连接慢 Stop 时立即撤销就绪；Suspend、Reset、并发／重复 Dispose 等待既已分离的连接完整释放。独立跟踪cleanup、取消时worker快照和共享Dispose完成任务，防止遗漏或等待新Resume的无限心跳。

最后期限和清理两个原始反例在修前 **2／2失败**，修后全部通过；代理保留 before TRX 于隔离工作树 `TestResults/b5c-session-rtt-drain-before.trx`。发现问题的独立代理复核最终源码，无剩余阻断；根再跑完整Client352及5项Windows生命周期，未降低断言。

2026-10-10 真实集成首轮揭示：SignalR 将服务端 `HubException(code)` 包装为 `An unexpected error occurred invoking '<当前方法>' on the server. HubException: <code>`，客户端此前只识别裸码，导致控制 CAS／旧 transport 拒绝误报 Unavailable。当前 .NET 框架格式经无数据库反射独立确证。修复仅匹配当前 hardcoded 调用方法的完整 Ordinal 前缀，解包后验证完整白名单；错误方法、未知异常类型、前后附加文本均保守 Unknown，不泄露远端异常、不扩展 HTTP 或 Start／Stop 映射。新增反例修前17项中5失败／12通过，见 `static-client/hub-error-before.trx`；修后完整静态364通过，独立复审无阻断。

第二轮剩余第三项触发未修改的正式全局 HTTP 每分钟120请求限制。测试假业务时钟不影响真实限流窗口。仅在桌宠过期与旧 Attach 拒绝、相关连接全部停止之后等待61秒，再执行公共房生命周期；90秒超时、限额和所有业务／数据断言均保留。第三轮六项完整通过，第三项约63秒，总体约81秒；这不是突发容量验收。各轮原始 TRX 和失败数据保留，未重置或删除。

成功第三批按本轮 ID 限定 SQL 断言确认8账号、11非记住会话、14房间／14房主席位、13历史租约、2公共码、37audit、45event（1已发布／44未发布）；8个未投递邮件任务 Attempts=0。失败前两批未全部走到 CountsAsync，不声明全库或三批累计计数。

## 五项质量及未验证项

| 项目 | 本次证据与限制 |
|---|---|
| 正确性 | DTO／身份／房间／租约／版本／有限坐标／边界，控制与观察、回执与当前态、连接与场景就绪区分；六项真实结果及错误包装反例通过 |
| 一致性 | 串行变更、代际、固定原键、CAS、先对账、精确补偿、重复通知和drain反例；本轮真实CAS拒绝、失应重放、替换观察租约及精确事件重发通过，不等于所有竞争专项完成 |
| 安全性 | origin／TLS／Cookie／fresh CSRF、旧账号隔离、稳定错误白名单、准入拒绝保留账号、秘密仅内存；本轮真实跨账号、CSRF／Origin与旧Hub拒绝通过 |
| 可靠性 | 断线／暂停恢复、保守期限、旧回调与慢清理；原请求仅当前进程会话保存。进程崩溃恢复、服务强杀、备份还原和长期运行未验证 |
| 扩展性 | 最多8设备／12成员、64KiB响应／单帧、合并通知与限频心跳、单owner；没有默认全库worker或新分布式依赖，生产吞吐未验证 |

真实WebSocket、长期worker、WinCred实写、SMTP投递、Windows人工、正式移动／动作与多人呈现、备份还原均未验证。六项B5C测试通过也不能关闭这些门禁。

## Git与资源

B5C代码和测试准备均为本地提交；本轮真实错误映射与测试节奏修复提交为 `9573f83`。`main`仍 `f10121ba7acd0d0df79b9a31acf6f481949230b3`。未合并、推送、部署、更新日常安装、创建发布标签或删除分支／worktree。

三轮各自六份实际 fixture 均完成停止／释放，逐端口回执确认 HTTPS监听和testhost消失；runner仅停止各轮自己启动的既有独立PG，进程、数据库监听与 `postmaster.pid` 无残留。原数据、凭据、key ring、恢复代际和三轮虚构业务记录／队列／事件／日志保留；没有迁移、SMTP、WinCred、全库worker或备份还原，不改停其他任务或现有系统PG服务。最终六项和清理回执经独立只读复核。受保护 `dango_box2d.dll` SHA256仍 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。

## 下一人工边界

隔离Windows候选在 `artifacts/local-room/backend-b5c-review-r1-20261010/bin/Dango.Desktop/release/` 构建通过，零警告／错误；未启动。只读核验目前没有适用的当前用户可信loopback证书。正常界面会访问专用WinCred槽，且默认正式CLI会开EmailWorker，不能直接沿用自动fixture启动。已形成[Windows人工精确计划](../planning/2026-10-10-backend-b5c-windows-manual-plan.md)，等待用户批准本轮临时TLS信任、专用凭据槽及有限虚构账号／房间；旧六项数据授权不能替代它。候选构建与离屏检查不算人工通过。
