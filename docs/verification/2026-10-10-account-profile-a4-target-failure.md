# 昵称 A4 目标核验失败与自动收尾

日期：2026-10-10。状态：用户批准的精确 A4 已执行一次并结束；未执行昵称迁移或七项业务测试。一次性额度已消耗，原数据和证据保留，不续跑或重试。

## 实际执行与阶段

执行前核对分支 `codex/backend-b5-windows`、HEAD `9e1e2800c19e586fd1068885b9965cb5f0c0f929` 及干净工作树。根与独立审查均确认 `artifacts/account-profile-a4-approved.json` 的 294 份输入匹配、零缺失，SHA256 为 `1A4189A44CD8D75BDF656B94032B54BA5D13EA710F0DFB7EB3AA70EECBF5F51E`。A4 目录不存在，既有实例停止。随后在新的 bundled PowerShell 宿主仅执行批准的 runner 一次；退出码 1，错误为 `profile_run_database_target_rejected`。

OwnerId 为 `45ded8e0-0204-43e6-9420-8bf95a8ae491`，证据目录为 LocalAppData 下 `Dango/BackendDevelopment/run20261010profilea4`。启动通过 `pg_ctl 39728 → CMD 30832 → PostgreSQL 29340` 的完整原生身份和桥链核验。启动工具退出码 0、零 reader Task，双输出流关闭确认。

大小预检返回 `utf8Octets=831577`。完整只读 JSON 返回成功，stdout 实际字符及 UTF8 字节均为 831579；两字节差额为 psql CRLF。它在 1048576 双硬限内，stderr 长度 0，两条流 EOF 完成、读取失败类别 `None`，同步 reader 显式关闭、独立输出资源清理确认。因此 A4 的完整读取与收尾修复具有真实证据；不能据此反推 A3 的具体故障类别。

失败发生在完整 JSON 返回后的六项目标／历史核验：数据库、角色、PG18 版本、数据目录、监听地址及精确五项迁移历史。旧 runner 先核验再写 `before.json`，本次实际字段未保存，所以当前证据不能指出哪项不匹配。禁止猜测原因、放宽门禁或补查数据库。

`FixturesAttempted=false`，`before.json`、迁移证据和 TRX 均不存在，业务尝试与夹具资源账本均为零字节。唯一昵称迁移和七项业务测试未进入；没有测试证书、HTTPS、SMTP、WinCred 或信任操作。这些工具结果不表示昵称业务通过。

## 收尾与证据保全

六个工具为 postgres 版本、pg_ctl 状态、启动、大小 psql、完整证据 psql 和 pg_ctl 停止；最终退出码依次为 0、3、0、0、0、0。六工具均确认进程退出、Process Dispose、双流关闭及独立输出资源收尾。运行器再核启动归属后只停止本次实例。根另查 PG、CMD 与六工具 PID 均消失，pidfile 不存在，记录端口监听为零。

原 `resource-receipt.json` SHA256 为 `D0770F58F5904A8A5DADE135E57F9BFDD652FC622702577DB4972984158A40C9`，未修改。根的非秘密投影为 `artifacts/account-profile-a4-failure/0c2fc38bb0d04e4687d7dc49845277ca/root-summary.json`，SHA256 为 `831BCEC2FA4DAD04A8BF4E8746B48372EB7F7EE9FE49B6FF171187B60FBA82F1`；独立只读审查通过。投影不保存 SQL 正文、任意异常原文或认证材料。

同目录 `source-snapshots/` 以 CreateNew 保留执行时模块、runner 及 command-and-inputs 记录；模块 SHA256 为 `85ED3F252E66422A40C3CE5D46A06891FEE6ED786C6FF6E67CEF8F25B350140A`，runner SHA256 为 `569B40393E0D6B4C5FFAA3C82F55DA64B735DA0861BBECE2509D8A0DA1F904E3`。后续候选源码变化不修改这些快照或原清单。执行结束后未再查询数据库。

原数据库、凭据、keys／恢复代际及 A、A2、A3、A4 的清单、日志、reservation、账本和回执全部保留。未删除、重置、还原、合并 main、push、部署、安装或替换生产 DLL。

## 后续诊断

另准备 [D1 只读精确计划](../planning/2026-10-10-account-profile-target-d1-plan.md)，使用新证据目录和独立输入清单，仅查询系统／迁移元数据并先保存实际字段，再比较。D1 不是 A4 重试，也不允许迁移或业务测试；必须获得该新计划的精确批准后才启动既有实例。
