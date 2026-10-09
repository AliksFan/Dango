# B5B 房间管理客户端精确本地集成计划

日期：2026-10-09。状态：**用户在本计划和具体数据授权问题展示后回复“继续”，按该精确目标执行本轮真实集成；首轮 4／4 通过，零失败／跳过**。依据[B5 交互增量](2026-10-09-backend-b5-interaction-proposal.md)及根 `AGENTS.md`，本计划验证正式账号会话绑定的房间管理 HTTP 客户端；本轮授权来自上述直接回复，不继承 B1–B4、B5A 批准，不扩大到其他对象或任务。

## 精确环境

只启动当前用户 LocalApplicationData 下 `Dango/BackendDevelopment/run20261008b0/pgdata` 的既有独立 PostgreSQL 18，连接 `state.json` 记录的本机端口、`dango_backend_b0` 库、`dango_b0_owner` 角色；复用既有 `credential.dpapi`、`b1test-state.json` 和受当前用户保护的 `b1tests-keys`。仓库不记录个人端口、绝对开发机路径或凭据。

runner 在启动前核对运行名、数据库、角色、状态版本、记录端口、数据目录、恢复代际、既有文件与目录及 reparse point，并要求实例当前停止；只监听 `127.0.0.1`。fixture 连接后再核对实际数据库、角色、数据目录、PostgreSQL 18 和 `listen_addresses`，拒绝待应用迁移；B5B 还核对既有 key ring 及文件中恢复代际与传入值一致。检查失败即停止，不生成替代配置或选择别的环境。

**无迁移、初始化、新建库、重置、备份、数据库还原、数据删除或永久到期操作。** 业务“恢复房间”仅改变本轮新造房间的可恢复状态，保留全部记录，不是数据库还原。正式宿主按已有合同保护并维护既有 Data Protection key ring 的正常生命周期，不替换凭据或生成恢复代际。

## 四项用例和允许写入

仅运行 `RoomClientHttpsIntegrationTests`：

| 用例 | 新账号 / 会话 / 共同房 | 真实操作与断言 |
|---|---|---|
| 本人目录及权限 | 2 / 2 / 4 | 本人 3 间、另一个账号 1 间；limit=1 遍历 active/all/dissolved，不重复、不循环、不暴露其他房间；第四间被额度拒绝。两个房型更新后仍保持类型；未知 kind 字段、创建桌宠类型和非法容量由真实 HTTP 拒绝。修改两个房间、解散其中一个；另一个账号的读、改、解散、恢复越权均被拒绝，其持久图保持原样；默认桌宠房不可解散 |
| 独立客户端版本冲突及注销 | 1 / 3 / 1 | 两客户端读取同版本，先提交者成功，过期版本明确冲突。注销一个会话后旧适配器在发请求前拒绝；同身份重新登录也不能复活旧绑定，独立第二会话仍可读。原账号 Pet、配置及默认房保持原样 |
| 四种提交响应丢失 | 1 / 1 / 1 | Create / Update / Dissolve / Restore 各在真实服务器成功响应后丢失一次；客户端报告结果未知且不自动重试。调用方明确携原键原参数重放，得到原回执并重读；恢复后再次重放旧操作仍得到历史版本，不能据此展示当前状态。同键改参数均冲突，只有四条管理回执，无重复对象或改写 |
| 解散、公共码与归属 | 3 / 3 / 2 | 房主新造公共房、第三个账号新造独立私密房作保留对照。仅为房主与本轮访客，通过真实 HTTP 生成该公共房一个码，读取各自快照并进入，生成两个租约。客户端解散清除这两个 Pet 的归属和控制、递增版本、撤销这两个租约与码；旧码解析拒绝。客户端恢复房间不迁回 Pet、不复活租约或码；第三个账号的完整持久图保持原样 |

每轮完整执行新造 **7 个随机 `.invalid` 邮箱账号**及各自邀请、预注册、邮箱挑战、未投递队列、唯一账号 Pet、默认桌宠房、房主席位和初始归属；产生 **9 个非记住会话**，其中一个注销。额外新造 **8 间共同房**及房主席位；仅上述 17 次成功管理操作追加管理 audit 与未发布 domain event。失败的额度、越权、无效输入、过期版本和改参数请求不追加管理回执。没有 Pet 外观更新或真实账号操作。

第四项额外新造 **1 个公共码凭据与 2 个设备租约**，码重置和两次入房各有自身回执。读取这两个 Pet 的个人快照会执行既有、精确到自身对象的生命周期对账：初始无租约桌宠归属标睡眠，各产生一条 `offline` audit/event；入房随后只修改这两只 Pet 及它们的来源默认房和目标公共房，各产生来源 `left` event 和目标 `entered` 回执/event。完整成功轮次预计合计 **22 条 room audit、24 条未发布 room domain event**；注册自身的认证记录按已有合同保留。解散只撤销该新房的一个码、两个租约，并清除仍归属该房的两个 Pet；恢复不新增码、租约或席位。测试使用固定、未推进的本轮时间，避免把后台到期当作解散效果。

所有业务读取与断言按本轮持有的 user/Pet/room/request/credential/lease ID 限定；不读已有账号业务数据，不运行全库 worker 或扫到期任务。若失败后按同范围重跑，会新增另一批随机虚构记录；不清理、覆盖或重置之前留下的数据。

## HTTPS、秘密和授权隔离

fixture 仅创建临时 IPv4 loopback HTTPS 端口及临时证书；所有测试 handler 只接受该 fixture 的精确证书，没有系统信任安装或产品 TLS 绕过。客户端复用受控 CookieContainer 与 CSRF，原始密码、邀请证明、继续秘密、Cookie、公共码和防伪头只在进程内存；观察 transport 仅记录请求 ID、操作和有无防伪头。持久图先哈希再断言，避免测试失败将配置或认证材料写入 TRX。安全存储为内存实现，不访问 WinCred。

注册通过真实客户端开始、完成与登录；邮箱确认读取本轮新造挑战后调用真实受保护确认 HTTP，绕过邮件投递和浏览器。Email、room live、outbox 和永久到期 worker 关闭，没有 SMTP 接收器、真实发信、浏览器、Hub、WSS、接管、好友、配饰或在线画面验收。第 4 项入房只为真实解散约束提供限定数据，不声明正式房间场景可用。

runner 专用 `-B5B` 拒绝与 B1/B2/B3/B4/B5A/初始化/新建库混用，只接受 `run20261008b0`。子进程将 B1/B3/B4/B5A/B5B 授权先全部置 `0`，只开启 `DANGO_BACKEND_B5B_TEST_APPROVED=1`；fixture 在 DB 连接、端口探测、证书和 key ring 之前检查此专用授权，旧批准不能代替它。其他阶段将 B5B 开关置 `0`，并排除新分类。

强制筛选同时匹配 `PostgresIntegration`、`BackendB5BIntegration` 与新类完整名。可选 `-TestFilter` 只能是一个正向 `FullyQualifiedName=...` 或 `FullyQualifiedName~...`，最长 512 字符、值仅含英文数字、下划线和点，拒绝括号、组合、否定和转义；只能 AND 收窄范围。未带 `-ExecuteApprovedPlan` 时仅展示目标，不解密凭据、启动或连接。

本精确计划获批、集成后的 Server 测试项目完成 Release 构建后，才可执行：

```powershell
./eng/invoke-backend-postgres-tests.ps1 -RunName run20261008b0 `
  -PostgresBinDirectory '<已核验 PostgreSQL 18 bin 目录>' -B5B -ExecuteApprovedPlan
```

TRX 保存于忽略目录 `artifacts/backend-b5b/postgres-tests/<本次时间>/backend-postgres.trx`。未批准导致跳过不能报告为通过。

## 五项门禁和收尾

| 要求 | 本次证据及限制 |
|---|---|
| 正确性 | 客户端到正式 HTTP/JSON 到 PostgreSQL 的目录与 CRUD 结果、非法输入和状态；响应上限与畸形响应由独立静态客户端门禁覆盖 |
| 一致性 | 独立会话旧版本拒绝、四种已提交响应丢失同键恢复、同键改参数冲突、原回执与当前详情区别、事务解散与恢复 |
| 安全性 | 本人过滤、逐次越权拒绝、受控 Cookie/CSRF、旧适配器跨注销和重新登录失效、对照账号未改变、旧公共码拒绝 |
| 可靠性 | 进程内传输失应恢复、fixture 失败清理；未做服务强杀、磁盘故障、真实备份还原或长期运行 |
| 扩展性 | limit=1 有界游标且不循环、单会话绑定、无自动变更重试；未承诺生产吞吐或完整多人呈现 |

每个 fixture 使用 `await using` 停止且释放自己的 HTTPS 宿主和临时证书，构造或停止失败也尽力释放。runner `finally` 仅在 PID、启动时间、可执行文件及数据目录均证明属于本轮时停止这个独立 PostgreSQL；不能证明时报告残留，不改停既有服务。结束核对本次数据库 PID/记录端口以及全部临时 HTTPS 监听退出，报告残留进程和端口。

保留原有数据、凭据、key ring、恢复代际、本轮虚构业务记录、未投递队列、未发布事件及日志；停止实例不删除数据。本计划不授权数据库清理、备份还原、删除 worktree/分支，或 main 合并、推送、部署、日常安装、DLL 替换。Windows 人工、真实发信、WinCred 实写、Hub/租约编排和多人画面仍有独立未验证门禁。

## 本轮执行记录

集成候选 `034c827` 执行本计划的完整四项，首轮全部通过，未修改代码或重跑；没有迁移、SMTP、WinCred、worker、重置／删除／数据库还原。TRX：`artifacts/backend-b5b/postgres-tests/20261009-133624-9449573/backend-postgres.trx`，同目录 `resource-receipt.json` 记录收尾。

runner 已停止其启动的独立 PostgreSQL。随后核对该启动 PID 已退出、记录数据库端口无监听、`postmaster.pid` 不存在；本工作树的 fixture 宿主进程和监听为 0。四个 `await using` fixture 完结且所属 testhost 退出，随机 HTTPS 端口未逐一记录，不能写成逐端口验收。原数据目录、凭据、key ring 与恢复代际文件保留；本轮虚构记录也保留。未操作既有 PostgreSQL 服务。结果与未验收边界见[B5B 验证](../verification/2026-10-09-email-backend-b5b.md)。
