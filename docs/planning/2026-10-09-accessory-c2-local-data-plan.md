# C2 私有穿搭精确本地数据验证计划

日期：2026-10-09。状态：已准备，**尚未获得本计划的数据库操作授权，未执行迁移或真实集成测试**。本次编码沿用[已确认设计](2026-10-09-accessory-account-backup.md)及[C2 实施范围](2026-10-09-accessory-c2-execution.md)。B1–B5A 的既有批准不扩大到新配饰迁移和写入。

## 精确资源与操作

只使用当前用户 LocalApplicationData 下 `Dango/BackendDevelopment/run20261008b0/pgdata` 的既有独立 PostgreSQL 18 实例、`dango_backend_b0` 库和 `dango_b0_owner` 角色。复用受当前用户保护的 `credential.dpapi`、`state.json` 的原本机端口、`b1test-state.json` 的原恢复代际及 `b1tests-keys`。不初始化、新建库、重置、恢复、删除或改停系统现有 PostgreSQL 服务。

执行前验证现有路径／文件无 reparse point，运行名、数据库、角色、版本、记录端口相符，原数据和密钥目录存在且实例停止；启动仅监听 `127.0.0.1`。连接后核对数据库、当前角色、data_directory、PostgreSQL 18 和 loopback 监听。任何不匹配立即拒绝，不生成替代凭据、恢复代际或目标。

只前向应用 `20261009124814_AccessoryAccountBackup`；已应用则保留，不执行 Down。维护入口只接受这个迁移是唯一 pending 迁移或已应用且无 pending；不能顺便应用其他新增迁移。这张迁移只新增 `accessory_blobs`、`accessory_asset_references`、`accessory_current_outfits`、`accessory_commit_receipts` 及其索引／约束，不改写现有业务记录。普通服务启动不自动迁移。

在同一隔离 run 目录下创建或复用 `accessory-c2-blobs` 私有图片目录；不得挂静态文件服务。目录无链接并位于仓库、密钥目录及数据库 pgdata 之外，继承既有受当前用户限制的 run 目录权限。所有实际上传使用本次测试生成的合成 PNG，最多 64 个新物理内容；规范化按哈希去重，共用旧物理内容时只读核验，不删除、改坏或替换该内容。

## 允许的写入与门禁

只执行源码中 `Dango.Server.Tests.Accessories.AccessoryAccountPostgresTests`，同时要求 `PostgresIntegration` 和 `AccessoryC2Integration` 两个分类。当前准备 **27 个执行案例（19 个方法含数据行）**，预计新造至多 31 个虚构账号／团子／默认房、37 个会话和 37 个随机合成 PNG 内容（含一次受控回滚后孤儿文件）。每次完整运行的授权上限为 40 个随机 `.invalid` 账号及对应邀请／预注册／挑战／未投递邮件任务／团子／默认桌宠房／房主席位／初始归属、40 次登录会话和 64 个图片内容。配饰本人引用、当前穿搭和幂等回执仅涉及这些新造团子。失败后同范围重跑可能追加虚构数据，全部保留。

预注册及确认使用既有服务／真实受 CSRF 保护的 API 和本次新造挑战，不启用 SMTP 或真实发信。密码、邀请证明、继续秘密、Cookie 和观察用 CSRF 只在进程内存中传递，不输出、写入普通文件或 TRX。正式客户端测试使用内存 secret store，不访问 Windows 凭据管理器。

允许的配饰验证：

- 实际 PNG 上传和服务器 source→canonical 映射；本人下载／提交与跨账号知道哈希仍拒绝；两账号各自实际上传同图后共用一个物理内容、保持独立引用。
- 完整穿搭和全部参数往返；不影响外观配置、账号身份及默认房。当前图片超过上传租约仍有效；替换当前组合只终止该本人旧 current 引用，不删除另一账号引用或图片。
- 同版本并发和同幂等键并发；同键原请求重放保留原版本，异请求拒绝，缺回执保持未知；提交成功响应丢失后以原键核对，不把查不到当未提交。
- 事务内 checkpoint 在写引用、写穿搭／回执、提交前受控抛错或推进虚构时钟，验证完整回滚及最后时刻会话检查。可以留下本次未引用的完整图片文件，不做回收。
- 暂存 16 个有效非 current 引用的边界；过期暂存拒绝、current 替换取消旧租约。时间测试使用正式 `remembered=true` 登录的有效七日会话穿过 24 小时图片租约，不延长到超过现有会话策略的期限。
- 只对用例自己刚创建并明确持有 ID 的 user/session 修改撤销、到期、停用、恢复锁、锁定、security stamp 或 epoch 测试条件；不能批量修改其他账号。只有该新造 Pet 的 current outfit、receipt 或本人引用可以写入故意损坏的测试状态，保留其虚构数据。不能篡改全局 blob 元数据或物理文件。
- 实际 HTTPS Cookie／CSRF、严格 JSON、独立穿搭版本；真实 FormalAccountClient 经相同登录会话接配饰 HTTP transport。重建正式 HTTPS host 时复用同一数据库／恢复代际／key ring／图片目录，读取已保存结果，不用新的内存服务冒充持久化。

所有正式后台邮件、房间到期、房间 live/outbox worker 关闭；不执行 Hub、入房／离房、公共码、房间管理、审核、举报或 GC。临时 HTTPS 仅使用 fixture 的 IPv4 loopback 随机端口和临时证书，客户端只信任该 fixture 的精确证书，不安装系统信任或绕过生产 TLS。

## 执行隔离与收尾

runner `-C2` 拒绝与 B1/B2/B3/B4/B5A、Initialize、CreateDatabase 混用，只接受 `run20261008b0`。子进程将旧阶段测试批准全部清零，仅启用 `DANGO_BACKEND_C2_TEST_APPROVED=1`；fixture 在连接、探测端口、创建证书或访问 key ring 前检查该专用批准。额外筛选只允许一个受限 `FullyQualifiedName= / ~` 条件，可以缩小本类，不能逃逸阶段范围。

不带 `-ExecuteApprovedPlan` 只输出操作计划，不解密凭据、启动实例、连接数据库、创建图片目录或迁移。精确计划获批且最终 ServerTests Release 构建完成后才执行：

```powershell
./eng/invoke-backend-postgres-tests.ps1 -RunName run20261008b0 `
  -PostgresBinDirectory '<已核验 PostgreSQL 18 bin 目录>' -C2 -ExecuteApprovedPlan
```

证据保存在忽略目录 `artifacts/accessory-c2/postgres-tests/<时间>/`。必须报告总数／通过／失败／跳过，静态通过或未授权跳过不能算集成通过。fixture 释放本次 host 和证书；runner 仅在 PID、启动时间、程序和数据目录匹配自己启动的实例时停止，无法证明归属则报告残留，不停止其他服务。

结束后检查本次 PostgreSQL PID、记录端口、postmaster.pid、测试宿主及临时 HTTPS 监听退出；原 pgdata、key ring、恢复代际、本次虚构记录、图片和日志全部保留。停止服务不等于删除数据。本计划不授权备份还原、持久 GC、资源删除、main 合并、推送、安装、部署或正式 DLL 替换；Windows 人工、系统凭据实写、正式审核／房间显示及双机验收仍独立未验证。
