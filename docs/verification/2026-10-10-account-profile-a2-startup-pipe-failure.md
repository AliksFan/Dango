# 昵称 A2 输出管道失败与自动收尾

日期：2026-10-10。状态：用户精确批准的一次执行已结束，业务验证未开始；运行器自身完成归属核验和资源收尾，持久数据与原证据保留。

用户批准[A2 精确计划](../planning/2026-10-10-account-profile-postgres-a2-plan.md)及固定清单 `artifacts/account-profile-a2-approved.json`，SHA256 `D38FF1D2381AAE9806A6CD541935EA8E29BB552F9ED13CF1F4CE1F00208AD94F`。执行前根实查分支 `codex/backend-b5-windows`、HEAD `1b3fa5f`、工作树干净、294 份输入匹配，新目录不存在、原 pidfile 不存在。仅执行批准入口一次。

OwnerId 为 `594f79ca-cc80-49a9-a5e2-23304c938e0f`。运行器创建固定受保护 `run20261010profilea2`，仅在内存读取既有 DPAPI 凭据，精确启动既有本机实例。工具原生身份检查全部通过；本次 `pg_ctl 29724 → cmd 37536 → postgres 30188` 完整 Windows 桥链通过严格核验。

`pg_ctl start` 于 `07:54:12.2585645Z` 正常退出，但输出读取没有在 EOF 等待期限内结束，运行器报告 `profile_run_tool_output_incomplete`。CMD 作为长期桥进程保有从控制工具继承的 stdout／stderr 写端，控制进程退出不意味着管道已经没有写端；启动日志已写入指定文件，不需要依赖这个控制输出作业务证据。固定 startup 不应使用等待完整文本 EOF 的成功契约，增加等待不能解决实例仍运行时的继承句柄。

尚未调用只读 SQL 探针、迁移、种子或七项测试。`attempts.jsonl`、`resources.jsonl` 均为零字节，`FixturesAttempted=false`，没有测试证书或 HTTPS；不把空资源账本的 native 清理 false 当成证书泄漏。

运行器 finally 的迟到观察与完整原生桥链证据确认归属，调用 `pg_ctl stop` PID 32348，退出码 0。回执 `PostgreSqlCleanupConfirmed=true`，四个工具均 `Exited=true`、`CleanupConfirmed=true`；pidfile 和原端口监听均消失，启动日志于 `07:54:16.267` 明确记录 shutdown。根额外只读核对 pidfile 不存在、记录端口无监听。没有按进程名 kill、树杀、连接其他服务或手工补写成功回执。

原 `resource-receipt.json`、reservation、固定输入回执、空账本和启动／关闭日志全部保留于 A2 目录。A 与 A2 清单保持各自原摘要，数据库数据、凭据、keys、恢复代际全保留。`RetryAllowed=false`，本次一次额度已消耗。未删除、重置、还原、发邮件、导入信任、操作 WinCred、合并 main、push、部署、安装或替换生产 DLL。

原 `resource-receipt.json` SHA256 为 `FB3303133D45A5E232CF8B8274016F2D57582F5A030157E33ABB0175BC978933`；没有用后续修复结果覆盖或修改这份失败回执。

下一步仅修复精确 pg_ctl start 的输出契约：仍重定向到本次私有管道，但不创建 reader Task、不使用输出文本；确认原句柄退出后显式关闭未读的两个 reader。固定 start 的实际退出码、身份和 PostgreSQL 归属证据仍须满足；其他工具仍要求完整有界 EOF，参数或路径不同在 Start 前拒绝。先进行无数据库反例和独立审查，再准备新的固定 A3 目录、清单与精确计划，批准前不再次启动真实实例。

## 后续修复验证

专用 `PostgresStartIgnored` 已实现；入口只对精确 pg_ctl start 传入固定 Plan／端口，原句柄退出即记录真实 ExitCode／ExitTime，caller 的 finally 从 recorded receipt 恢复已知控制结果；两流关闭未确认就失败。其他命令仍使用 `CompleteEof`。修后离线 **138/138**，未操作真实 PostgreSQL；回执 `artifacts/account-profile-offline/b3314001e2b74b1fbc1a28bd25c8e430/offline-results.json`，SHA256 `53BC92FBAA163ED676C146E5B327EE6E7B09606F9DB50134F258B9BCFB1ABD92`，包含实际命令和正式 module／runner／testscript 快照。

A2 故障前 module 从提交 `1b3fa5f` 的精确 Git blob 原始字节 CreateNew 保留，SHA256 `E1FCF1E0374B65D34F3F0A0891D6E1064FAA0FCEF8B3018FA813F8C1D589B332` 与 A2 冻结输入一致。独立纯 fake 反例令控制进程正常退出且身份／清理通过、输出 writer 保持开放，旧实现仍准确失败为 `profile_run_tool_output_incomplete`；没有启动真实工具或读取受保护状态。证据目录 `artifacts/account-profile-before-a2/cda37e8e12f34d8d86fe9c6e2a13b201` 保留原 module、重现脚本和准确命令，`before-eof-reproduction.json` SHA256 `E94F637025448B8F6B8092A7C43ED5D8B2C37809A6D4E03273CE59E55CEA1B29`。

实际 Windows 私有匿名管道 smoke 证明另一端 writer 仍有效时，未开始任何 read Task 的 reader 可及时关闭、所有本次 handles 均释放；没有 PG、子进程或凭据操作。回执 `artifacts/account-profile-private-pipe/4fbbb043ba7144af9045b8b09b61c549/private-pipe-smoke.json`。独立只读审查通过；A3 测试 owner 构建通过，未批准门禁仍七项全跳过。新的[A3 精确计划](../planning/2026-10-10-account-profile-postgres-a3-plan.md)已准备并冻结 294 输入，尚未批准或执行；这些证据不表示昵称真实数据库业务通过，也不恢复 A2 额度。
