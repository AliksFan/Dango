# 昵称 A3 读取失败与自动收尾

日期：2026-10-10。状态：用户批准的精确 A3 已执行一次并结束；未执行昵称迁移或七项业务测试。原数据库、密钥与失败证据保留，不能续跑或重试。

## 执行与已证实结果

用户明确要求“可以，执行A3吧”。根执行前实查 `codex/backend-b5-windows`、HEAD `b58b005`、工作树干净；固定清单 `artifacts/account-profile-a3-approved.json`，SHA256 `5EA60013B4BF06F70F4E4F39293DCBA809F1A7D7D0C8F18613F347BAE296360D`，294 输入全部匹配，零缺失。独立只读复核同样通过。固定 A3 目录不存在，原 pidfile 不存在、记录端口 63237 无监听；未先读取凭据。随后仅执行批准的 `eng/invoke-account-profile-postgres-tests.ps1 -ExecuteApprovedPlan` 一次，不 build／restore。

OwnerId 为 `2e2daa6c-d6c7-4093-bb00-b8b9a4a25958`，原证据目录为 LocalAppData 下 `Dango/BackendDevelopment/run20261010profilea3`。启动通过完整 `pg_ctl 24392 → CMD 13284 → PostgreSQL 29140` 原生身份与 Windows 桥链核验；控制进程实际退出码 0，`PostgresStartIgnored` 创建零个 reader Task，双流关闭确认。因此 A2 的继承输出 EOF 故障修复已经得到真实运行证据。

首个只读数据库探针为 `psql` PID 6400，采用 `-X -w -q -A -t -v ON_ERROR_STOP=1` 和单一 `-c` SQL；`-w` 禁止密码提示。运行器报 `profile_run_tool_output_bound_exceeded`，psql 最终退出码 -1，生命周期约 30 秒，收尾确认。这个退出码来自受验证的本次工具句柄收尾，不能当作自然认证或 SQL 执行失败结论。

没有 `before.json`、`migration-evidence.json` 或 TRX，`FixturesAttempted=false`，两个账本均为零字节。代码顺序要求完整只读 SQL 基线及目标／历史核验成功后才调用唯一迁移；本次未到该调用点，没有业务图或资料写入，也没有测试证书、HTTPS、邮件、WinCred 或信任操作。不能把本次工具通过或空账本写成七项昵称业务通过。

## 失败诊断边界

旧实现只要 stdout／stderr 的任一 Task `IsFaulted`，即写 `profile_run_tool_output_bound_exceeded`；回执没有记录真实异常类别或已读字符数。因此目前仅能证实读取任务异常，不能证实实际输出超过 131072 字符。

独立纯离线检查证实这一诊断缺陷：131072 字符可成功，131073 才产生底层真实超限标记；I/O、流已关闭、解码和其他异常也使 Task faulted，却被旧入口误报成同一超限码。取消、stdin 关闭和未到 EOF 也需独立规范错误。模拟证据不能反推本次真实 psql 的异常类别。独立回执 `artifacts/profile-a3-readfault/6ce74a2f6afb4146a93acf6039ff6dfb/direct-reader-report.json` 在对应 `account-live-sync` 隔离工作树；入口映射的十二场景证据保留在原聊天工作区的 `artifacts/account-profile-reader-audit/9e7e579daf15426da4c44083911f11c0/reader-fault-mapping.json`，SHA256 `A7CF903375450DC6D4F4467F123959510F7A4ACA1102D0C47727234FB3159D83`。这些测试没有真实进程、数据库或凭据操作。

完整 SQL 证据包含十八张表的全部不可恢复行摘要，以及账号 ID／资料初始化元数据，输出随旧数据量增长；仅数组约两千条摘要即可接近当前字符上限。这是已确认的输出规模问题，不代表本次实际超限原因已确认。后续候选应保留旧图算法与完整 EOF，补每流非秘密诊断和同源的小输出大小预检；仅固定完整 SQL 证据允许新的明确硬上限，不允许自动扩大或忽略读取失败。不得为诊断再次启动或连接本次实例。

## 收尾与证据保全

运行器 finally 再核完整启动链后，仅调用本次 `pg_ctl stop` PID 17500，退出码 0；`PostgreSqlCleanupConfirmed=true`，五个工具均 `Exited=true`、`CleanupConfirmed=true`，原 pidfile 和记录端口监听均不存在。根只读追加核验 PG 29140、CMD 13284、全部记录工具 PID 均不存在，端口监听数量 0。没有按进程名终止或操作其他服务。

这里的五工具 `CleanupConfirmed` 仅确认工具进程退出。只有专用 `pg_ctl start` 明确记录了双输出流关闭；其他完整 EOF 模式没有独立记录同步 reader 的显式关闭与 Task 收尾，不能据此宣称全部读取资源已清理。A4 候选补独立输出资源门禁，不修改 A3 原回执或反推其未记录的状态。

原 `resource-receipt.json` SHA256 为 `9EA1C67EDD43A19BD649E520D60C873E10762A183C350E38369D4CFE5F15D3FB`，原回执未改。根独立非秘密投影及收尾证据为 `artifacts/account-profile-a3-failure/889e55013c3043cb919d997fe35b67b9/root-summary.json`，SHA256 `105960ECB42CA2E03FF681289ADF87E2063F5778E529D4577D8B4E1F93BC1E6D`；它不含密码、SQL 输出、异常原文或可恢复认证信息。独立只读审查确认阶段和收尾字段一致。

数据库、原凭据、keys／恢复代际，以及 A、A2、A3 的清单、原回执、reservation、账本和日志全部保留。`RetryAllowed=false`，A3 一次额度已消耗。未删除、重置、还原、合并 main、push、部署、安装或替换生产 DLL。下一次真实资源／数据验证需要新的固定输入、证据目录和精确批准。
