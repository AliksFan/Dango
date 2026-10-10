# B5C 正式房间连接精确本地集成计划

日期：2026-10-09；2026-10-10 更新。状态：**用户在本精确计划及对应授权问题后回复“批准”；已按原范围执行，第三轮六项全部通过，零失败／跳过**。前两轮失败证据和虚构记录保留，未清理或重置。依据已批准 B5 居中浮层及 B5C 连接／恢复增量。B1–B4、B5A、B5B 的批准不能替代本轮授权。

## 精确环境与拒绝边界

仅启动当前用户 LocalApplicationData 下 `Dango/BackendDevelopment/run20261008b0/pgdata` 的既有独立 PostgreSQL 18，使用 `state.json` 记录的本机端口、`dango_backend_b0` 库及 `dango_b0_owner` 角色。复用已有 `credential.dpapi`、`b1test-state.json` 与受当前用户保护的 `b1tests-keys`；不生成替代凭据或恢复代际。

runner 核验路径、reparse point、状态版本、运行名、数据库、角色、端口、既有 key ring 和恢复代际文件，且实例必须已停止。启动仅监听 `127.0.0.1`；fixture 再核对实际数据库、角色、数据目录、PostgreSQL 18、`listen_addresses` 及零待应用迁移。失败就停止，不改连其他环境。

**不迁移、初始化、新建库、重置、删除、永久到期、备份或数据库还原；不开全库 email/live/outbox/expiry worker。** 唯一 dispatcher 调用只接受本轮一条明确 event ID。既有 Data Protection key ring 仅按正式宿主正常生命周期维护，不替换。

## 六项真实验收与数据上界

仅运行 `FormalRoomSessionHttpsIntegrationTests`；每项一个正式 loopback HTTPS fixture。业务请求使用真实 `FormalAccountClient`、`IRoomAccessClient`、`FormalRoomTransport` 和 `FormalRoomSession`，经过正式 HTTP／Hub 后持久化到 PostgreSQL。观察 wrapper 只记录和转发，不假造服务结果。

| 用例 | 新账号／会话／共同房／租约／码 | room audit／domain event | 操作与反例 |
|---|---|---|---|
| 两成员与权限 | 2／2／1／2／1 | 6／8 | 新公共房、一次码重置、码解析、两 Pet 入房；两正式 Hub Attach、Heartbeat、共享快照确认两真实成员。缺 CSRF、错误 Origin negotiate 被拒绝；访客进入房主默认桌宠房及借自身租约读该房被拒绝，归属图不变。服务接入仍 AwaitingPresentation，不能伪称场景就绪 |
| 双会话控制及离房 | 1／2／0／2／0 | 6／6 | 不同实例进入同一默认桌宠房，控制／观察不同；明确接管成功，旧 CAS 及观察者 Hub Leave 拒绝。观察者精确 EndLease 保留控制者；控制者明确 Leave 清归属与租约 |
| 暂停恢复及到期 | 1／1／1／2／0 | 6／7 | Suspend 不发 Leave／EndLease；20 秒后按 Personal→新 Hub→Attach→Shared→Personal 恢复同租约，不 Enter／夺权。实际替换 transport 后旧 Heartbeat 拒绝。再次 Suspend，推进到 Attach 后精确 30 秒：默认桌宠房保留归属并睡眠，旧 Attach 拒绝；随后明确进本人新公共房，到期清归属且不能复活 |
| 未知 Enter 与历史回执 | 1／2／2／3／0 | 5／7 | A Enter 第一私人房在正式服务器提交 200 后丢失响应；保留原键，不能用新变更覆盖 pending。同账号会话 B 明确进入第二私人房，A 旧租约撤销并产生观察替换租约。A 原键重放后依据当前私人快照采用新房观察租约；历史回执、同键改参数、晚到旧 EndLease 不能改回归属 |
| 呈现失败补偿 | 1／1／1／2／0 | 6／7 | 当前 token 精确 HTTP EndLease，真实成功响应丢失一次；RetryPending 保持原键及 lease ID，只存在一次业务补偿。明确再次 Enter 得到新租约；旧 token 的就绪与失败回调，以及 Suspend／Resume 前旧连接代际 token，都不能结束已采用的新租约 |
| 真通知重发与账号隔离 | 2／3／1／2／1 | 8／10 | 两账号入公共房、Attach，房主真实修改房名。仅 dispatch 该 update request 对应的一条 event ID；一次 send-before-publish 故障，第二次重发同 ID 并发布，不新增事件。真 Hub 通知合并触发有界读取且不发业务变更。房主注销、重登录：旧绑定发送前拒绝，新绑定只读当前事实且不自动 Enter，访客归属保留 |

完整成功批次新造 **8 个随机 `.invalid` 邮箱账号、11 个非记住会话**（其中一个注销），各自一条邀请、预注册、邮箱挑战、未投递邮件队列、唯一 Pet、默认桌宠房、房主席位与初始归属；额外 **6 间共同房及房主席位**，共 **14 间房、14 个房主席位、13 条设备租约历史、2 个公共码凭据**。被撤销的租约仍计入 13 条历史记录，不删除。

按现有 append 合同，完整成功批次预计 **37 条 room audit、45 条 room domain event**，测试逐场景断言精确数量。37 条 audit 为 25 次成功业务变更和 12 次精确 `offline` 生命周期对账；另有 8 条跨房来源 `left` event。第六项仅将本轮一条 event 的 `PublishedAt` 更新为已发布，成功后预计 **1 条已发布、44 条未发布**。准备时这是静态推导；2026-10-10 第三轮通过后已由该批次 ID 限定 SQL 断言核验，不能当成三轮或全库累计计数。偏差先核实原因，不放宽上界或擅自扩大授权。

只按本轮持有的 user/Pet/room/request/session/lease/credential/event ID 查询和验证，不读已有账号业务图。只有第三项显式推进其 fixture 服务时钟，其余固定本轮时间；推进前 Suspend 并等待本 session 心跳结束。快照／Attach 的既有生命周期写入仅作用于本轮自己的 Pet；公共入房的目标成员也全部来自本轮。失败或按同范围重跑会新增另一批随机虚构记录，不清理旧批次；重跑前明确报告已留下的数据。

## 传输、安全与本轮证据边界

首轮正式 Hub 采用 **真实 SignalR LongPolling over HTTPS**。不把这一证据写成 WSS、移动／动作同步或多人可玩场景验收。正式协议没有 Move／Action，测试不确认任何真实场景就绪。

每 fixture 创建一个临时 IPv4 loopback HTTPS 端口与临时证书。账号和 Hub 共用 product-controlled CookieContainer；仅测试 seam 对该 fixture 精确证书固定，不安装系统信任、不修改产品 TLS 校验。unsafe HTTP 每次取当前 CSRF，Hub negotiate／LongPolling POST／DELETE 同样遵守。失应注入仅在真实服务器成功响应到达后丢弃该响应，不能伪造服务成功。

密码、邀请／继续证明、Cookie、CSRF 和公共码只在进程内存；TRX、观察日志和资源回执不记录秘密。注册通过真实客户端开始、完成和登录；确认读取本轮挑战并调用真实受保护 HTTP，邮箱队列不投递。安全存储为内存实现，不访问 WinCred。持久图先哈希再比较，防止失败输出认证材料。

## 独立授权与执行方式

runner 专用 `-B5C` 拒绝与 B1／B2／B3／B4／B5A／B5B／初始化／新建库混用，只接受 `run20261008b0`。子进程先将全部阶段批准设 `0`，只开启 `DANGO_BACKEND_B5C_TEST_APPROVED=1`；fixture 在 DB 连接、端口探测、证书和 key ring 访问前检查专用批准。旧阶段排除 B5C 分类。

强制筛选同时匹配 `PostgresIntegration`、`BackendB5CIntegration` 与新类完整名。可选 `TestFilter` 仅为一个正向 `FullyQualifiedName=...` 或 `FullyQualifiedName~...`，最长 512 字符、值限英文数字／下划线／点；禁止括号、组合、否定和转义，只能 AND 收窄。未带 `ExecuteApprovedPlan` 仅展示目标，不解密凭据、启动或连接。

完整候选静态门禁通过且**本精确计划另行获批**后才可执行：

```powershell
./eng/invoke-backend-postgres-tests.ps1 -RunName run20261008b0 `
  -PostgresBinDirectory '<已核验 PostgreSQL 18 bin 目录>' -B5C -ExecuteApprovedPlan
```

TRX 位于忽略目录 `artifacts/backend-b5c/postgres-tests/<本轮时间>/backend-postgres.trx`；未批准跳过不能报告为通过。每 fixture 写 `fixture-<实际端口>-<随机ID>.json`，记录本次 testhost PID、实际 HTTPS 端口及 Start／Stop／Dispose 完结；runner 收尾生成 `resource-receipt.json` 并核查所记录的所有端口。

## 五项门禁与收尾

| 项目 | 证据与限制 |
|---|---|
| 正确性 | 正式准入、两成员快照、角色、过期结果及错误码；场景就绪仍保留外部门禁 |
| 一致性 | 独立会话 CAS、原键重放、替换观察租约、精确补偿、原回执与当前归属分离、重复 outbox 通知 |
| 安全性 | 归属拒绝、Cookie／fresh CSRF／Origin、旧 transport 和注销代际拒绝、秘密不入日志 |
| 可靠性 | 暂停／恢复顺序、未知结果重放与生命周期结束；不包含进程强杀、磁盘故障或备份还原 |
| 扩展性 | 单 owner 心跳、有限恢复、有界通知刷新、精确事件 dispatch；不承诺生产吞吐或长期运行 |

所有 session 和 Hub 连接 `await` 释放，再停止且释放各自 HTTPS fixture 和临时证书；每个实际端口写入回执。runner `finally` 只有 PID、创建时间、可执行文件和目录证明属于本轮才停止该 PostgreSQL，不改停既有服务；不能证明时报告残留。核查数据库 PID、记录端口、`postmaster.pid`、每个实际 HTTPS 端口及 fixture 完结标记，明确报告残留。

保留既有数据、凭据、key ring、恢复代际及本轮虚构记录、未投递队列、domain event 和日志。停止不是删除。本计划不授权 main 合并、推送、部署、日常安装、DLL 替换、删除 worktree／分支或任何持久资源。WSS、真实发信、WinCred 实写、Windows 人工验收、移动／动作同步、多人画面、长期 worker、备份还原仍未验证，不能关闭完整 B5。

## 准备验证记录

已将正式 transport、session 及通知白名单修复作为依赖集成后，编译 `Dango.Server.Tests` Release，零警告／错误。PowerShell parser 无错误；阶段混用、错误运行目标、非单一正向 FQN filter 和 Initialize 的四项拒绝检查均携 `ExecuteApprovedPlan`，在二进制、凭据、状态或资源访问前拒绝。Session 实现代理只读核对恢复顺序、未知 Enter 后替换观察租约和精确补偿 API，与六项测试契合。

准备时没有运行 `dotnet test`、fixture、数据库、服务器、listener、dispatch 或任何种子操作。上述准备记录保留为当时快照，不能替代下述独立批准与真实执行证据。

## 2026-10-10 批准与执行记录

用户明确回复“批准”，执行固定 `-B5C -ExecuteApprovedPlan` 的六项完整套件，没有使用旧阶段开关、迁移、初始化、重置、删除、SMTP、WinCred 或全库 worker。

| 轮次 | TRX 所在相对目录 | 实际结果 |
|---|---|---|
| 首轮 | `artifacts/backend-b5c/postgres-tests/20261010-013300-0462988/` | 4 通过／2 失败／0 跳过；真实 Hub 冲突消息被误映射为 Unavailable |
| 第二轮 | `artifacts/backend-b5c/postgres-tests/20261010-013930-2081047/` | 5 通过／1 失败／0 跳过；第三项压缩请求触发正式 HTTP 限流 |
| 第三轮 | `artifacts/backend-b5c/postgres-tests/20261010-014147-3870623/` | **6 通过／0 失败／0 跳过**；总耗时约 81 秒 |

每目录包含 `backend-postgres.trx`、实际六份 `fixture-*.json` 与 `resource-receipt.json`。首轮根因是 .NET SignalR 的固定错误包装：只对当前调用方法的完整前缀解包，再验证完整白名单错误码；未知文本不透传，HTTP、Start／Stop 规则不变。修前新增反例 17 项中 5 项失败；修后 Client 静态 364／364 通过，独立审查通过。

第二轮第三项在本人公共房 Enter 后的 Snapshot 被未改动的每分钟 120 请求全局限流拒绝。业务假时钟不能推进真实 HTTP 限流窗口。第三轮仅在桌宠到期与旧 Attach 拒绝均已核验、session／临时连接已停止后，等待真实窗口 61 秒，再执行公共房生命周期；保留 90 秒测试超时、正式限额与全部业务／数据断言。此结果不证明突发吞吐。

**第三轮成功批次**按本轮 ID 的 SQL 断言核验：8 账号及各自邀请／预注册／挑战／未投递邮件任务／Pet／默认桌宠房，11 个非记住会话（一个注销），6 个额外共同房，合计 14 房／14 房主席位，13 历史设备租约、2 公共码凭据、37 audit、45 event（1 已发布／44 未发布）；8 条邮件任务 Attempts 均为 0。前两轮失败场景没有运行到末尾全部计数，不能把三轮预计数量相乘或称为全库累计实测；前两批和原数据全部保留。

三轮回执均确认各自六份实际 fixture Started／Stopped／Disposed 完成，testhost／HTTPS 监听消失；本次 PostgreSQL 进程、数据库监听及 pidfile 无残留。原数据、凭据、key ring、恢复代际和本轮虚构业务记录／事件／日志保留。第三轮结果及逐资源清理由独立代理只读复核。真实传输仍为 HTTPS LongPolling，不是 WSS、可玩多人或完整 B5 人工验收。
