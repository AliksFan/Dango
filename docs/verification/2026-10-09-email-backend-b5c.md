# B5C 正式房间连接候选验证

日期：2026-10-09。依据用户“持续推进直到需要人工协助／验收”及[B5C 增量](../planning/2026-10-09-backend-b5c-session-execution.md)，在 `codex/backend-b5-windows` 从 `8ea00a9` 推进。代码候选 `f2f23c3`，静态、离屏及独立审查完成；[六项真实数据计划](../planning/2026-10-09-backend-b5c-local-data-plan.md)已准备，**尚未获得本轮数据执行授权，未执行真实集成**。完整 B5 未验收。

## 本次实现

- `FormalAccountClient.RoomAccess`、正式 HTTP／SignalR transport：同一受控账号 Cookie、固定 HTTPS origin、正常产品 TLS、逐 unsafe HTTP fresh CSRF，含 negotiate 和 LongPolling POST／DELETE；绑定前后校验、禁止重定向与自动变更重试，快照／消息有界。
- `FormalRoomSession`：本进程实例、30 秒租约与约 10 秒心跳、控制／观察、明确入房／接管／离房／精确结束租约；回执和当前事实分开，未知操作原键／正文保留，确定回执后读取失败必须先对账。
- 断线只核对并 Attach 本实例仍有效的租约，不自动 Enter、接管或复活旧租约。真实事件类型只触发合并读取；旧通知、连接、呈现令牌、到期回调和补偿均隔离。
- Windows 居中 `OnlineRoomPanelView` 与单 owner controller，目录／详情路由、公共码解析与明确入房、房主重置确认、离房确认；关闭只 Suspend，不发 Leave／EndLease，秘密展示清除。账号切换释放原绑定，房间拒绝不会误清账号。
- 当前只有正式连接、角色、归属与成员快照；Windows 没有调用 `ConfirmPresentationReady`。服务端没有 Move／Action 或批准的坐标映射，没有把旧原型或本地可动单人画面接成正式多人玩法。本地单人和原管理入口保留。

## 实际验证

| 门禁 | 结果与证据 |
|---|---|
| Client 静态 | **352／352**，零失败／跳过：既有244、新transport76、新session32。最终 TRX：`artifacts/backend-b5c/static-client/client-b5c-final.trx`；显式排除3项旧 `RealNetworkAcceptanceTests` |
| Desktop STA／受影响回归 | 去重 **104 项全部通过**。首组60、保留回归44；最后 session 修复后5项 Windows 生命周期复验通过，属于前60的重复，不累加。TRX：`artifacts/backend-b5c/static-desktop/desktop-b5c.trx`、`desktop-b5c-preservation.trx`、`desktop-b5c-final-lifecycle.trx` |
| Server 无数据库 | **186／186**，零失败／跳过；`artifacts/backend-b5c/static-server/server-static-b5c.trx`，筛选 `TestCategory!=PostgresIntegration` |
| Release | 集成 Desktop／测试及最后 Server.Tests 构建通过；Server.Tests 最终零警告／错误，编译实际六项真实测试但没有执行 |
| 离屏 WPF | 1024×640、336×640、336×360 下6页共 **18状态**：横向居中误差0，无横向滚动，低高度可滚动到操作。图在 `artifacts/backend-b5c/visual/`；检查了宽状态、窄入房确认和低高度公共码底部。全部是标明虚构数据的离屏布局检查 |
| runner／fixture guard | PowerShell AST无错误；代理四类资源前拒绝及根六个筛选逃逸拒绝通过。B5C dry-run仅展示目标，不解密凭据或启动／连接；`--list-tests`发现精确六项。独立批准、固定目标、互斥阶段、正向FQN AND收窄和每fixture资源回执代码已编译 |
| 真实 HTTPS／Hub／PG | **未执行**。假handler、编译、list-tests或未批准跳过都不是通过；需本轮精确计划另行批准 |

SignalR 静态反例经过其 LongPolling runtime 和受控 HTTP handler，覆盖握手、七个正式方法及通知；没有真实监听或服务端。代理首次未筛选客户端全量测试的3项旧真实网络验收因缺少 `DANGO_SYNC_SERVER_PATH` 在服务启动前失败；随后使用明确静态筛选，未把这3项记作通过。

本地房保留回归使用之前已验证且存在于忽略测试输出的隔离物理模块，SHA256 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`；本次没有复制或修改该模块，也未替换生产库。

## 独立审查与可失败反例

1. 连接额度拒绝与会话失效的共同服务端码不能据此注销账号；修前两个反例失败，修后仅 `authentication_required` 触发失效清理。
2. 确定回执后当前读取失败不能再次新提交，增加 `ReconciliationRequired` 门；未知入房重试成功必须消耗原确认页，回到当前状态。
3. 补齐真实 `expired`、`removed`、`ban_set`、`ban_removed` 通知，四个反例证明触发读事实，不按通知直接改权限。
4. 延迟快照扣完整请求耗时：初始／最终响应耗尽期限不授予连接／呈现资格，部分耗时的 watchdog 和确认门使用同一剩余期限。
5. 旧连接慢 Stop 时立即撤销就绪；Suspend、Reset、并发／重复 Dispose 等待既已分离的连接完整释放。独立跟踪cleanup、取消时worker快照和共享Dispose完成任务，防止遗漏或等待新Resume的无限心跳。

最后期限和清理两个原始反例在修前 **2／2失败**，修后全部通过；代理保留 before TRX 于隔离工作树 `TestResults/b5c-session-rtt-drain-before.trx`。发现问题的独立代理复核最终源码，无剩余阻断；根再跑完整Client352及5项Windows生命周期，未降低断言。

## 五项质量及未验证项

| 项目 | 本次证据与限制 |
|---|---|
| 正确性 | DTO／身份／房间／租约／版本／有限坐标／边界，控制与观察、回执与当前态、连接与场景就绪区分；真实服务结果待六项测试 |
| 一致性 | 串行变更、代际、固定原键、CAS、先对账、精确补偿、重复通知和drain反例；数据库实际竞争／回执仍待验证 |
| 安全性 | origin／TLS／Cookie／fresh CSRF、旧账号隔离、稳定错误白名单、准入拒绝保留账号、秘密仅内存；真实跨账号／Hub鉴权待验收 |
| 可靠性 | 断线／暂停恢复、保守期限、旧回调与慢清理；原请求仅当前进程会话保存。进程崩溃恢复、服务强杀、备份还原和长期运行未验证 |
| 扩展性 | 最多8设备／12成员、64KiB响应／单帧、合并通知与限频心跳、单owner；没有默认全库worker或新分布式依赖，生产吞吐未验证 |

真实WebSocket、长期worker、WinCred实写、SMTP投递、Windows人工、正式移动／动作与多人呈现、备份还原均未验证。六项B5C测试通过也不能关闭这些门禁。

## Git与资源

B5C代码和测试准备均为本地提交，`main`仍 `f10121ba7acd0d0df79b9a31acf6f481949230b3`。未合并、推送、部署、更新日常安装、创建发布标签或删除分支／worktree。

本轮未启动隔离PG、真实HTTPS fixture、SMTP或正式Hub；只有静态测试和已退出的离屏harness。只读检查原隔离PG无监听、无 `postmaster.pid`；原数据、凭据、key ring和恢复代际保留。根工作树fixture／预览进程为0；不改停其他任务或现有系统PG服务。受保护 `dango_box2d.dll` SHA256仍 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。
