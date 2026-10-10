# 昵称 A-only 首次执行、失败与精确收尾

日期：2026-10-10。状态：首次精确执行已消耗，业务验证未开始；启动的本次资源已停止，全部持久数据和原证据保留。

## 批准与执行

用户明确批准[A-only 精确计划](../planning/2026-10-10-account-profile-postgres-a-plan.md)及清单 `artifacts/account-profile-a-approved.json`，SHA256 `195596BA3F9D334EF71EEA33F75844C88DE492276FA8FD19AD33FD03D7AFE21A`。执行前逐份冻结输入匹配。运行器创建该计划的受保护 `run20261010profilea` 目录；批准的 DPAPI 凭据仅在内存及精确子进程环境中使用，未打印或写入证据。

首次执行在 `pg_ctl start` 后返回 `profile_run_tool_identity_unconfirmed`，外层报告 `profile_run_cleanup_unconfirmed_no_retry`。原 `resource-receipt.json` 必须保留原样，不改写成成功。OwnerId 为 `5fa6487a-04c2-45a8-b1fd-287e9ee3d486`。SQL 探针、唯一迁移、种子、证书夹具、HTTPS 和七项测试均未开始；`attempts.jsonl`、`resources.jsonl` 为零行，业务变更尝试为零。本次全套 reservation 已消耗，`RetryAllowed=false`。

## 已确认的缺陷

工具身份检查依赖刚启动进程的 `MainModule.FileName`；值为空时不会进入仅处理异常的回退分支，因而拒绝已启动的工具。离线空值反例复现了这条失败路径；原运行未记录底层 Win32 错误，不能断言当时究竟为空还是其他模块枚举故障。

Windows PostgreSQL 18 通过受限 CMD 启动 postmaster。[官方源码](https://github.com/postgres/postgres/blob/REL_18_STABLE/src/bin/pg_ctl/pg_ctl.c#L506)与实际链一致：本次 `pg_ctl 12792 → cmd 32784 → postgres 7848`。原失败收尾要求直接父进程为 pg_ctl，拒绝了正确的桥链。离线合法 CMD 桥反例也被旧实现拒绝。

原失败回执于 `07:21:02.5399166Z` 写出，CMD 创建于 `07:21:02.6401110Z`，PostgreSQL 创建于 `07:21:02.6711630Z`。早回执内 pidfile／监听为 false 是瞬时快照，不能证明不会稍后启动。补充收尾首次尝试把回执写出时间作为创建上界，被自身守卫拒绝，未停止进程；核对精确时间后纠正该上界。

补充脚本另遇 PowerShell JSON 日期转换陷阱：`ConvertFrom-Json` 已将 ISO UTC 字段解析为 `DateTime`；再次用 `DateTimeOffset.Parse` 让该对象经本地字符串解释，会丢失 UTC 标识及亚秒。逐项时间输出确认了八小时时差，修为保留类型并 `ToUniversalTime()`；没有放宽其他归属条件。

## 补充收尾证据

根独占收尾，检查 OwnerId、固定工具记录、未复用工具 PID、PG／CMD 精确可执行文件与完整命令、两级 parent、创建时间顺序、启动后十秒窗口、pidfile 四字段和唯一 loopback 监听，十七项全部满足；随即再次核对两个进程实例的创建时间与命令，再执行仅此数据目录的 `pg_ctl stop -m fast -w -t 20`，退出码 0。没有按进程名停止，没有树杀，没有操作其他服务。

新增 `root-cleanup-before.json` 和 `root-cleanup-after.json` 与原回执并存于该受保护证据目录。后者记录 `07:31:09.4454083Z`：原 PostgreSQL 和 CMD 均退出，pidfile 不存在、原端口无监听，原失败回执 SHA256 未变，`PostgreSqlCleanupConfirmed=true`。此补充结果确认资源收尾，不表示七项业务测试通过。

原数据、凭据、业务 keys、恢复代际、原清单、reservation、空账本、失败回执、启动日志和补充回执全部保留。未执行系统信任、WinCred、发邮件、删除、重置、还原或数据库业务写入。未合并 main、push、部署、更新日常安装或替换生产 DLL。

## 后续

修复运行器工具身份查询、严格 CMD 桥链和有界迟到资源观察，补充能拒绝错误实现的离线反例并独立审查。第二次执行使用全新固定目录 `run20261010profilea2` 与全新清单，原 A 目录和清单不能复用；新的精确计划经用户批准前不得再次启动真实实例。

新增反例在旧模块快照下为 72/104，32 项失败，其中七项直接调用旧有接口暴露错误，其余包含缺少新增安全接口；不能把所有失败都称为同一个已复现业务缺陷。准确命令、旧模块 SHA256 及完整检查回执位于 `artifacts/account-profile-offline/fe6f3e914bdd4194855915563046795f/`，`ExecutedDatabase=false`。

代理实现整合至根后，真实冷进程工具检查另复现 Win32 错误 31：模块在 Start 后才 Add-Type，短命工具退出后查询映像失败。根仅运行 `pg_ctl --version`，未启动或连接实例、未读凭据；失败回执为 `artifacts/account-profile-a2-native-smoke/6b14db57a58343ffa8208595dbafb830/native-tool-smoke.json`。根改为启动前加载原生代码、启动后先核身份再启动输出读取；重新冷进程执行同一版本命令，退出码 0，初始查询 0、身份验证通过、真实 ExitTime 和退出清理确认。成功回执为 `artifacts/account-profile-a2-native-smoke/0367684b0da34db1b8863740a5d09434/native-tool-smoke.json`。退出后最终查询仍返回 31，不抹掉原保留句柄此前的验证；初始／最终错误分别记录，避免失败诊断被覆盖。此 smoke 证明短命工具路径，不代表 PostgreSQL 启动桥或七项业务已真实通过。

最终根实现及入口已独立只读审查，离线反例 **111/111**，回执 `artifacts/account-profile-offline/a76bfbf91a464e0a8024d77e4a2a583b/offline-results.json`，SHA256 `36F91E3A8EA50201D16244220DAD3DE1A4FCE20F8FC3865011583A37FADAD53B`。同目录 `verification-command.txt` 保留精确命令及模块／入口／测试脚本 SHA256。覆盖 native handle 的 image／PID／FILETIME、未知或错误身份不得 kill、初始错误恢复／最终 31 不丢早已验证身份、UTC typed／字符串亚秒、精确 CMD 桥与控制进程真实退出时间、防 PID 复用、额外 shell 指令、迟到资源和缺桥不得确认无残留。fake 的加载与 reader 顺序检查不能替代真实冷工具 smoke。测试修订曾纠正两份无效变异：错父误用合法桥 PID、反斜杠替换未命中正斜杠；随后使用确实改变输入的变异，不降低守卫。

测试 owner 的唯一目录改为 A2 后，Server.Tests Release 构建零警告／错误。根清空 Profile 和 B1 批准，七项精确测试全部 skipped/inconclusive、零 failed，回执 `artifacts/test-runs/account-sync-nickname-porch/profile-a2-unapproved-gate.trx`；这是未批准不得运行的门禁证据，不是真实业务成功。旧阶段隔离 2/2 通过。生产 DLL 与 main 实查仍为既有 SHA256 与 `f10121b`。

[A2 精确计划](../planning/2026-10-10-account-profile-postgres-a2-plan.md)及全新 `artifacts/account-profile-a2-approved.json` 已准备。新清单 SHA256 `D38FF1D2381AAE9806A6CD541935EA8E29BB552F9ED13CF1F4CE1F00208AD94F`、294 份冻结输入，根再次核对匹配。旧 A 清单 digest 仍保持原值，原目录及回执保留；新 A2 目录尚未创建，没有 A2 凭据读取、实例启动、数据库连接、迁移或业务写入。再次真实执行需要新的精确批准。
