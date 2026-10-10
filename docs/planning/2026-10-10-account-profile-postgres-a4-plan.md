# 昵称 A-only 第四次精确验证计划

日期：2026-10-10。状态：候选已完成修复、离线反例、实际私有管道检查、独立审查、构建及 294 份输入冻结，等待本段精确批准；尚未批准或执行真实数据库验证。A、A2、A3 的一次性额度均已消耗，不退款、不续跑、不复用目录。

## 前次结果与候选修复

[A3](../verification/2026-10-10-account-profile-a3-read-failure.md) 已真实通过启动工具身份、完整 CMD 桥链及私有输出流关闭，在首个只读 psql 探针处失败；未取得数据库基线、未执行昵称迁移或业务测试，两账本为空。PG、CMD、五个工具、pidfile、记录端口已退出，原数据、keys／代际及所有失败回执保留。旧实现把任意 reader fault 统一误报为超限，没有实际错误类别和长度证据，因此本次不能把真实根因写成已经确认的输出越界。

A4 保留逐行不可恢复摘要、旧图比对、原生进程身份和完整 EOF，只补准确的读取诊断及明确的证据输出预算：

- 每条流记录非秘密的已观察字符、上限、完成／取消及可信错误类别；真正越界采用内部类型标记，I/O、关闭、解码、取消和其他失败不再伪装成超限。不记录 stdout／stderr 正文或任意异常原文。
- 仅完整固定数据库证据请求使用 `PostgresEvidenceJson`：精确 PostgreSQL 18 `psql.exe`、固定 Plan／端口、工作目录、全部参数、同源完整 SQL、仅本次密码和 `PGCLIENTENCODING=UTF8` 环境、无树杀；启动前验证，不符合即拒绝。stdout 字符及增量 UTF8 字节均硬限 1048576，stderr 仍硬限 131072 字符。其他工具保持原 131072 字符和完整 EOF。不得自适应扩大预算或忽略读取故障。
- psql 明确严格 UTF8 解码。before／migration／after 的每次完整快照前，新增一次小输出只读测量，使用同一 JSON 表达式的 `octet_length(convert_to(...,'UTF8'))`；结果只有 `utf8Octets` 整数，须在 1–1048574 范围，为实际 psql CRLF 预留两字节。测量超限就中止，不读正文、不自动改上限。正文仍独立限额，测量后数据改变不免除实际读取门禁。
- 取消、`Wait` 与 Task fault 的竞态、stdin 关闭失败继续失败并记录稳定分类；已观察字符包含触发越界的完整块。证据上限与 EOF、认证、目标、迁移和业务断言分别检查。
- 完整 EOF 模式的成功返回还须两个读取 Task 已结束、两条同步 reader 显式关闭；`OutputResourcesCleanupConfirmed` 与工具进程 `CleanupConfirmed` 分别记录。失败保留关闭前诊断与原始错误；Task 仍 pending 时不强关正在读取的流、不推定已清理。runner 聚合 `ToolOutputResourcesCleanupConfirmed` 并独立拒绝未确认的收尾。
- 已加载的原生类型缺少本次 capture overload 时，在创建工具之前拒绝并要求新 PowerShell 宿主。reader 关闭失败保留 sticky 失败状态，不通过第二次无效 Dispose 伪造关闭确认。

这些修复是候选；离线通过不证明 A3 的实际原因，也不证明新的数据库业务通过。

## 批准前技术证据

最终模块 SHA256 `85ED3F252E66422A40C3CE5D46A06891FEE6ED786C6FF6E67CEF8F25B350140A`，runner SHA256 `569B40393E0D6B4C5FFAA3C82F55DA64B735DA0861BBECE2509D8A0DA1F904E3`。语法及独立只读审查通过；根最终离线反例 **237/237**、零失败，回执 `artifacts/account-profile-offline/b946e25a18f345ca8ec0e65f02143870/offline-results.json`，SHA256 `B7A9FDD3E9A97DD028E6FAB57410C228977353887A85E8BD6A43A263AD0EEE39`，`ExecutedDatabase=false`。

最终测试脚本 SHA256 `B69CF00D369C0D22DDAE290A10594B9AF3504296351464DF114E9AF78A17A724`。同一最终脚本对输出收尾修复前的 E2D1 模块／B27B runner 为 **211/237**，新增 26 项均失败、原 211 项仍通过。覆盖 pending 与进程退出独立性、成功双流显式关闭、sticky 关闭失败、原始错误不被收尾覆盖及 runner 独立聚合门禁；此前读取诊断 73 项反例对 A3 原始 blob 失败的证据也保留。独立 artifact 索引 `account-profile-a4-output-cleanup/29bbb6da7b46418dafacca02165f8dae/final-evidence-index.json` 的 SHA256 为 `D5B1C69A1167B77223BF19BE657EA2E08A318FFA91710B1B272FEE26D74A1B34`。首轮新反例 **236/237** 的唯一失败是 helper 缺 reader 引用却期待已关闭的不当测试预期；按保守未确认语义修正测试，原回执及说明保留，正式实现未因此修改或放宽。

根实际运行四个只属于本测试的 bundled `pwsh` 子进程，严格 UTF8 Unicode、131072 字符完整 EOF、131073 真超限及无效 UTF8 分类均通过；四项均确认进程退出、双同步 reader 显式关闭和独立输出资源收尾。回执 `artifacts/account-profile-native-reader/5754fb10285e4d6a88df95129f7a464b/native-reader-smoke.json`，SHA256 `0BC32BFE9719FE76125A233E97D450423E6D05FEE3381AD81465F33F69E3F374`。根后查四个记录 PID 均不存在。

根实际匿名管道两项通过：持有本测试 writer 时 pending reader 不关闭且收尾确认=false；只关闭该 writer 形成 EOF 后，双 reader 显式关闭并核验底层 handle／stream 关闭，收尾确认=true。回执 `artifacts/account-profile-output-resources/685c4563e8e349c089b753f3a91b1aaa/output-resource-smoke.json`，SHA256 `3B13870CACF620CD9356F0DEE62C2898A8608B1015FBC2B586BE20E43DE2F1E0`。两个目录均另以 CreateNew 保存正式 module、runner、smoke 源码和实际命令；这些检查不启动 PostgreSQL、不读凭据。

已完成的 A4 owner Release 构建零警告／错误；现 DLL 含 A4 目录而不含 A3 目录。关闭 Profile／B1 批准后恰七个实际结果均 `NotExecuted`、零 Failed，TRX 为 `artifacts/test-runs/account-sync-nickname-porch/profile-a4-unapproved-68530d686b55486d88c8876abd4420cf/profile-a4-unapproved-gate.trx`。此结果仅证明未批准时禁止执行，不能称业务通过。

## 固定目标、输入和执行

- 仅现有 LocalAppData 下 `Dango/BackendDevelopment/run20261008b0/pgdata` 独立 PostgreSQL 18；安装为 ProgramFiles 下 `PostgreSQL/18/bin`。唯一库 `dango_backend_b0`，角色 `dango_b0_owner`，端口仅从原 state 核对，监听仅 `127.0.0.1`。启动前目标停止、无 pidfile、记录端口无监听，不操作其他服务。
- 原 `credential.dpapi`、`b1tests-keys`、`b1test-state.json` 恢复代际沿用。秘密仅在内存和精确子进程环境中，不打印、写参数或普通文件。
- 既有历史须恰为 `20261008074701_InitialAccountRoomFoundation`、`20261008085521_AccountLifecycle`、`20261009031410_RoomManagement`、`20261009055558_RoomAccess`、`20261009062701_RoomAccessIntegrity` 五项。唯一前向迁移 `20261010055529_AccountProfile`；不符即中止，不补其他迁移、不降级。
- 唯一新证据目录为 LocalAppData 下 `Dango/BackendDevelopment/run20261010profilea4`；runner 与 C# owner 必须一致，目录已存在即拒绝。A、A2、A3 目录不能借用。
- 入口 `eng/invoke-account-profile-postgres-tests.ps1`；固定过滤 `FullyQualifiedName~Dango.Server.Tests.Accounts.AccountProfilePostgresTests`，仅一次、恰七项实际 Passed。真实运行不 build／restore，不执行其他类、workers 或维护任务。
- 实际入口必须在新的 bundled `pwsh -NoProfile` 宿主中执行，不复用已载入旧 `DangoProfileRunNativeV2` 类型的会话；重新 Import 不能更新已编译的同名类型。
- 新清单为本工作树 `artifacts/account-profile-a4-approved.json`。完成验证后以 CreateNew 冻结 **294** 份输入、零缺失，准确 SHA256 为 `1A4189A44CD8D75BDF656B94032B54BA5D13EA710F0DFB7EB3AA70EECBF5F51E`；根已用 `Assert-DangoProfileReviewInputs` 复核 294 份均匹配，预览 `Executed=false`，未预留或启动 A4。执行前及迁移／测试前核对用户批准的摘要及全部源码、DLL 和旁依赖，不允许同时改清单与 DLL 绕过。

全套额度先 CreateNew 原子预留，即使启动或测量失败也消耗，不退款、续跑或自动重试。仅本次 pg_ctl start 一次、归属证明有效才允许 stop。正常完整运行最多十三个工具进程：版本、状态、启动、三个大小预检、三个完整 SQL 快照、单迁移、单 vstest、单 scoped SQL、停止；每个只读查询一次。失败不补执行已失败查询。

## 数据与资源额度

昵称业务预算与 A／A2／A3 完全相同：

| 操作 | 最大变更尝试 |
| --- | ---: |
| Invite／Start／Confirm／Complete，各操作 | 各 9 |
| Login | 10 |
| DirectProfileUpdate | 11 |
| HttpProfilePatch | 8 |
| SessionRevoke／HttpLogout，各操作 | 各 1 |
| 合计 | 67 |

逐 case 为 concurrent_profile 7、duplicate_names 14、rollback_after_profile 6、rollback_before_commit 6、cross_session 13、expiry 6、https 15。HTTPS case 恰 22 请求；所有拒绝、回滚和重放均计数，先 durable Flush 账本再调用，不退次数。短锁不包住正式并发事务。

最多新增九个 `profilea-{OwnerId:N}-{random:N}@example.invalid` 虚构账号及各自 Pet、默认桌宠房、房主席位和 PetRoomState；各九邀请／预注册／挑战／未投递 EmailJobs，十新会话、其中两撤销。十九资料修改尝试预期仅五次提交成功，四个账号有昵称；不改变旧业务图扩大额度。

七个夹具各生成一份短期证书，仅 HTTPS case 启动一个临时本机 HTTPS 监听、精确 pin；七个完整生命周期／恰 28 条资源回执。夹具正常 DisposeAsync 后才能确认 native 清理。不导入系统信任、不操作 WinCred，不启动 SMTP 或 dispatcher、不发邮件。

## 证据与收尾

SQL 使用 `BEGIN READ ONLY`、quiet JSON framing、正式带引号的 `"MigrationId"`。三个大小结果各 CreateNew 保存；每条流的实际计数、类别、EOF 与字节硬限单独记录，不记录正文或异常原文。迁移前后及用例后比较十八张旧业务／身份表的逐行不可恢复 SHA256，旧账号仍 nickname=NULL、profile_version=1；账号和业务图身份保留。核对九个完整新增图、十个同归属／代际会话、四个有昵称账号、五次版本增量、两撤销及九 Pending／Attempts=0／AcceptedAt 和 Lease 为空的邮件。

完整 `pg_ctl → CMD → PG` executable、参数、PID、创建／退出时间及本次私有句柄证据仍必须通过。迟到观察只收集证据；不能以瞬时无监听宣称无残留。活桥先消失而无法证明归属时，报告未确认，不盲目 kill。

先 Dispose 夹具，最后仅停止证明归属的本次 PostgreSQL，再核对 PG、CMD、工具、pidfile 和端口退出；业务通过与 native 清理分开报告。原数据、凭据、keys／代际及 A／A2／A3／A4 目录、虚构数据、固定清单、原回执、TRX、账本和日志全部保留，不删除、重置、还原或操作其他服务。

工具进程退出、完整 EOF 和私有输出资源收尾分别检查；任何一条流仍 pending 或关闭未确认，都不能将本次结果报告为完整清理。A3 只有 startup 模式双流明确关闭，其余工具进程退出不补充成输出资源证据。

本段不授权联合 UIA、捏脸真实会话同步、门廊 CRUD、系统信任、WinCred、main 合并、push、部署、日常安装、生产 DLL 替换、备份还原或崩溃演练。助手负责技术验证，用户主要验收手感、界面效果和流畅度。
