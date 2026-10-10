# 昵称 A-only 第二次精确验证计划

日期：2026-10-10。状态：候选检查与输入冻结已完成，未批准、未执行。启动归属修复、离线反例、独立审查及真实冷工具 smoke 已完成；请求本段精确批准前，根再次核对清单及当前输入。不得因首次 A 已批准而自动执行 A2。

## 首次结果及本次差别

首次 A 在启动资源守卫处失败，SQL 探针、迁移和七项测试尚未开始，业务尝试和夹具资源账本均为空。根保留原失败回执，补充严格 Windows CMD 桥链证据后精确停止实例，PG／CMD／pidfile／端口均已退出。[首次失败与收尾记录](../verification/2026-10-10-account-profile-a-startup-failure.md)记录准确证据。原 A 一次性额度已消耗；其目录、固定清单及所有数据不能删除、退款或复用。

A2 只修复运行器资源守卫：在启动前完成原生查询代码加载，保留已启动进程句柄，立即以 Win32 查询核对真实 executable／PID／创建时间；分别记录初始及最终查询错误，退出后查询失败不抹掉同一句柄此前的验证。Windows postmaster 必须通过精确的 CMD 桥归属于本次 pg_ctl；使用真实工具退出时间约束桥的创建，不能把观察时间当作父实例证明。启动后失败时有界观察迟到资源，缺乏完整证据则报告清理未确认，不能用瞬时无 pidfile／监听宣称成功。UTC 时间比较保留类型和亚秒精度。昵称业务、数据库目标、唯一迁移、七项测试和全部额度不扩大。

唯一新证据目录固定为当前用户 LocalAppData 下 `Dango/BackendDevelopment/run20261010profilea2`，新固定清单为本工作树 `artifacts/account-profile-a2-approved.json`。旧 A 目录与清单保持不变。新清单冻结 294 份输入，准确 SHA256 为 `D38FF1D2381AAE9806A6CD541935EA8E29BB552F9ED13CF1F4CE1F00208AD94F`；根实查与当前输入一致。不得重写清单来代替此批准摘要。

候选验证：最终离线反例 **111/111**（旧模块 72/104 的失败证据保留），真实冷 `pg_ctl --version` 初始 native 身份查询和退出清理通过；Release 构建零警告／错误；关闭批准后七项真实 Profile 测试全部跳过，旧阶段隔离 2/2。新证据目录尚未创建，A2 未读取真实凭据、启动实例、连接数据库或执行迁移／业务测试。这些结果不代表七项真实业务已通过。完整证据见[首次失败及修复记录](../verification/2026-10-10-account-profile-a-startup-failure.md)。

## 唯一环境及入口

- 仅使用既有 `Dango/BackendDevelopment/run20261008b0/pgdata` 独立 PostgreSQL 18，固定安装为 ProgramFiles 下 `PostgreSQL/18/bin`。
- 唯一数据库 `dango_backend_b0`，唯一角色 `dango_b0_owner`；端口仅从原 `state.json` 读取，只监听 `127.0.0.1`。启动前核验目标停止、pidfile 不存在、记录端口无监听；不得连接其他服务。
- 沿用原 `credential.dpapi`、`b1tests-keys`、`b1test-state.json` 恢复代际。凭据仅在内存与精确子进程环境中；不输出、持久化或换密钥。
- 只允许既有历史精确五项：`20261008074701_InitialAccountRoomFoundation`、`20261008085521_AccountLifecycle`、`20261009031410_RoomManagement`、`20261009055558_RoomAccess`、`20261009062701_RoomAccessIntegrity`。唯一前向目标 `20261010055529_AccountProfile`。历史不同立即拒绝，不补其他迁移，不降级。
- 入口仍为 `eng/invoke-account-profile-postgres-tests.ps1`，模块为 `eng/Dango.AccountProfilePostgres.psm1`；C# owner 只接受新的固定证据目录。
- 只执行 `FullyQualifiedName~Dango.Server.Tests.Accounts.AccountProfilePostgresTests` 一次、恰七项实际 Passed；真实执行不 build/restore，不运行其他测试类、workers 或维护任务。

新目录 CreateNew 原子预留全套额度；目录存在即拒绝，不换目录，不续跑、退款或自动 retry。批准 digest 必须来自用户批准的准确摘要，每次迁移及测试前重新核对摘要与全部冻结输入；同步改清单与 DLL 也拒绝。

## 一次全套数据及资源额度

| 操作 | 最大变更尝试 |
| --- | ---: |
| Invite／Start／Confirm／Complete，各操作 | 各 9 |
| Login | 10 |
| DirectProfileUpdate | 11 |
| HttpProfilePatch | 8 |
| SessionRevoke／HttpLogout，各操作 | 各 1 |
| 合计 | 67 |

固定逐 case 次数为 concurrent_profile 7、duplicate_names 14、rollback_after_profile 6、rollback_before_commit 6、cross_session 13、expiry 6、https 15。逐 case 操作预算与[原 A 计划](2026-10-10-account-profile-postgres-a-plan.md)第 3 节相同。所有拒绝、回滚和重复请求均耗次数，先 durable Flush 再调用；账本失败不调用、不退次数。短锁只保护记账，不串行化业务并发事务。HTTPS case 总请求恰 22，不能额外循环扩大额度。

最大新增九个 `profilea-{OwnerId:N}-{random:N}@example.invalid` 虚构账号与各自 Pet、默认桌宠房、房主席位、PetRoomState；各九份邀请／预注册／挑战／未投递 EmailJobs；十个新会话，其中两项撤销。十九次昵称修改尝试中预期仅五次提交成功，四个账号拥有昵称。旧业务图保留，不使用旧记录扩大额度。

只启动一次精确 PostgreSQL。七个夹具各生成一份短期测试证书，仅 HTTPS case 启动一个临时本机 HTTPS 监听并精确 pin。正常资源账本恰 28 条，七个夹具各 preparing → ready → dispose_started → disposed；正式 DisposeAsync 正常返回后才确认 native key 清理。没有系统信任导入、WinCred、SMTP 接收器、真实邮件或邮件 dispatcher。

## 证据及结束

迁移前后与测试后仅通过 `BEGIN READ ONLY` 查询精确库，使用 quiet JSON framing，历史列按正式 EF 的 `"MigrationId"` 引用。比较十八张旧业务／身份表不可恢复 SHA256 摘要，旧账号仍 nickname=NULL、profile_version=1；不输出密码、stamp、配置原文或原始 rowJSON。核对九个完整注册图、十个同归属／代际会话、四个有昵称用户、五次版本增量、两项撤销及九项未投递邮件，后者均 Pending、Attempts=0、AcceptedAt／Lease 为空。

工具的稳定非秘密回执记录保留句柄查询结果、Win32 错误码、退出和创建时间。PostgreSQL 归属要求固定 executable、精确七参数、data／host／port、pidfile、控制进程身份与创建时间、CMD 精确 executable／完整命令和两级 parent／时间顺序；不能只凭路径子串、PID 或 pg_ctl 的退出码。启动失败后有界观察只能收集证据，不能证明未捕获桥链的启动能力已消失。证据不足就报告残留，不盲目 kill。

当前收尾仍要求再次读取同一活 CMD 桥；若外部原因导致桥先于 PG 退出，即使曾有有效快照也会保守报告清理未确认，由根补充精确证据后处理。此路径不自动复用已消失桥的 PID，不擅自停止无法重新核实的资源。

先完成夹具 Dispose，最后仅用 pg_ctl 停止证明归属的本次目标，核对原实例退出、pidfile 和记录端口消失。分别记录工具、PostgreSQL 与夹具 native key 清理；缺证据不写成功。保留原全部数据、A 和 A2 目录、所有原密钥／代际、新虚构数据、清单、账本、TRX、回执和日志。结束不删除、重置、还原或操作其他服务。

本段不授权后续联合 UIA、捏脸真实会话同步、门廊 CRUD、系统信任或 WinCred；不授权 main 合并、push、部署、日常安装、生产 DLL 替换、备份还原或崩溃演练。助手完成技术验证，用户主要验收手感、界面效果与流畅度。
