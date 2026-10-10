# 昵称 A-only 第三次精确验证计划

日期：2026-10-10。状态：用户以“可以，执行A3吧”精确批准后已执行一次；启动专用输出契约通过，在首个只读 psql 探针的读取任务异常处失败。未取得 SQL 基线、未进入迁移或七项业务测试；运行器自动停止本次实例，根追加核验无进程、pidfile 或端口残留。完整 EOF 模式的独立读资源关闭未记录。数据与原回执保留，本次额度已消耗，不重跑、不退款、不复用目录。见[A3 读取失败及收尾](../verification/2026-10-10-account-profile-a3-read-failure.md)。A、A2 也均已消耗各自一次额度。

## 前两次结果及本次差别

A 首次在工具身份守卫处失败，后由根补充严格 Windows 桥链证据完成精确收尾。A2 的工具身份和完整 `pg_ctl → CMD → PostgreSQL` 链已真实通过，但 pg_ctl 退出后其 CMD 后代持有输出写端，EOF 门禁错误；运行器自动核实并停止实例。两次均未进入 SQL、迁移、种子或七项业务测试，账本均为空，数据和所有原回执保留。详见[A2 输出管道失败及收尾](../verification/2026-10-10-account-profile-a2-startup-pipe-failure.md)。

A3 仅新增精确 pg_ctl start 的输出契约：仍使用原 .NET Start 句柄、清除继承的 DANGO／PG 环境并重定向到本次私有管道；不创建输出 reader Task、不使用控制输出文本。控制进程退出后记录实际退出码与 ExitTime，显式关闭未读的两个 reader；关闭失败就拒绝返回成功。管道本身有界，异常输出塞满只会导致现有超时与失败收尾，不把未完整文本当业务结果。其他工具继续要求完整、有界的 EOF 输出，不能放宽 psql、正式迁移、版本检查或测试结果。

专用模式在启动前只允许固定 Plan 派生的 pg_ctl executable、工作目录、完整十项 start 参数、本次日志／data 路径和本机 host／记录端口，不接受额外环境、树杀、错误目标或其他命令。真正启动仍要求工具退出码 0、原生身份及严格 PG／CMD／控制实例证明；进入迁移前仍须正式只读目标与历史核验。

新的唯一证据目录为当前用户 LocalAppData 下 `Dango/BackendDevelopment/run20261010profilea3`；C# owner 只接受该目录。A、A2 目录、清单和回执均保留，不能借用。新固定清单为本工作树 `artifacts/account-profile-a3-approved.json`，CreateNew 冻结 294 份输入、零缺失，准确 SHA256 为 `5EA60013B4BF06F70F4E4F39293DCBA809F1A7D7D0C8F18613F347BAE296360D`；根已通过 `Assert-DangoProfileReviewInputs` 核对全部输入，不能重写清单代替此摘要。

批准前验证：离线反例 **138/138**，覆盖控制进程退出但继承管道仍开放的 EOF 失败、固定 startup 零 reader Task 与显式关流、非零退出码、17 项启动前误用拒绝、身份未知和关流失败拒绝、其他工具的完整 EOF 门禁；回执 `artifacts/account-profile-offline/b3314001e2b74b1fbc1a28bd25c8e430/offline-results.json`，SHA256 `53BC92FBAA163ED676C146E5B327EE6E7B09606F9DB50134F258B9BCFB1ABD92`，实际命令与正式源码快照另存于同目录。实际 Windows 私有匿名管道 smoke 在独立 writer 仍有效时，未读 reader 的 Dispose 3.4619 ms 返回，全部 owned handles 关闭，未启动进程或数据库、未读取凭据；回执 `artifacts/account-profile-private-pipe/4fbbb043ba7144af9045b8b09b61c549/private-pipe-smoke.json`。独立只读审查未发现具体阻碍。Release 构建零警告／错误，关闭 Profile／B1 批准后七项全部 skipped/inconclusive、零 failed，证据 `artifacts/test-runs/account-sync-nickname-porch/profile-a3-unapproved-gate.trx`；这只是未批准执行门禁，不代表业务通过。

## 唯一目标与执行

- 仅既有 `Dango/BackendDevelopment/run20261008b0/pgdata` 独立 PostgreSQL 18，固定 ProgramFiles 安装下 `PostgreSQL/18/bin`。
- 唯一库 `dango_backend_b0`、角色 `dango_b0_owner`；沿用原 `state.json` 记录端口，仅监听 `127.0.0.1`。启动前目标须停止、pidfile 不存在、记录端口无监听，不操作其他服务。
- 原 `credential.dpapi`、`b1tests-keys`、`b1test-state.json` 恢复代际沿用；秘密仅在内存与精确子进程环境中，不打印、写文件或命令行。
- 历史须恰为 `20261008074701_InitialAccountRoomFoundation`、`20261008085521_AccountLifecycle`、`20261009031410_RoomManagement`、`20261009055558_RoomAccess`、`20261009062701_RoomAccessIntegrity` 五项。唯一前向迁移 `20261010055529_AccountProfile`，不符即停止，不补其他迁移、不降级。
- 入口为 `eng/invoke-account-profile-postgres-tests.ps1`。仅运行 `FullyQualifiedName~Dango.Server.Tests.Accounts.AccountProfilePostgresTests` 一次，恰七项实际 Passed；真实运行不 build/restore，不运行其他测试类、workers 或维护任务。

先 CreateNew 原子预留本次全套额度；目录存在即拒绝、不换目录。即使执行前期失败也不退款、续跑或 retry。批准 digest 来自用户审查的准确摘要，执行前及每次迁移／测试前核对该摘要和全部冻结输入；同步改 DLL 与清单也拒绝。

## 数据与资源额度

| 操作 | 最大变更尝试 |
| --- | ---: |
| Invite／Start／Confirm／Complete，各操作 | 各 9 |
| Login | 10 |
| DirectProfileUpdate | 11 |
| HttpProfilePatch | 8 |
| SessionRevoke／HttpLogout，各操作 | 各 1 |
| 合计 | 67 |

逐 case 为 concurrent_profile 7、duplicate_names 14、rollback_after_profile 6、rollback_before_commit 6、cross_session 13、expiry 6、https 15，逐操作预算与[原 A 计划](2026-10-10-account-profile-postgres-a-plan.md)第 3 节相同。HTTPS case 总请求恰 22。拒绝、回滚、重放均计数，先 durable Flush 再执行，账本失败不调用、不退次数；短锁不包住正式并发事务。

最多新增九个 `profilea-{OwnerId:N}-{random:N}@example.invalid` 虚构账号及各自 Pet、默认桌宠房、房主席位、PetRoomState；各九邀请／预注册／挑战／未投递 EmailJobs，十新会话、其中两项撤销。十九资料修改尝试中预期仅五次提交成功，四个账号有昵称；不修改旧业务图以扩大额度。

PostgreSQL 只启动一次。七个夹具各生成一份短期测试证书，仅 HTTPS case 启动一个临时本机 HTTPS 监听并精确 pin；资源回执恰 28 条、七个完整生命周期，正常 DisposeAsync 后才确认 native 清理。不导入系统信任、不操作 WinCred、不启动 SMTP／dispatcher、不发真实邮件。

## 证据与收尾

正式只读查询须 `BEGIN READ ONLY` 和 quiet JSON framing，历史列用 `"MigrationId"`。迁移前后及用例后比较十八张旧业务／身份表的不可恢复 SHA256，旧账号仍 nickname=NULL、profile_version=1；不输出密码、stamp、配置原文或 rowJSON。核对九个完整注册图、十个同归属／代际会话、四有昵称账号、五次版本增量、两撤销及九 Pending、Attempts=0、AcceptedAt／Lease 为空的邮件。

专用工具回执分别记录实际退出码、初始／最终原生查询、真实创建／退出时间、输出契约、零 reader Task、输出丢弃及显式关闭结果。严格 PG proof 仍要求固定 executable、精确七参数、data／host／port、pidfile、控制原生身份及精确 ExitTime、CMD 完整命令／两级 parent／创建时间顺序。迟到观察仅收集证据，不能凭短暂无进程或端口宣称无残留。活 CMD 桥先于 PG 消失时仍保守报告清理未确认，根另补精确证据后处理，不盲目 kill。

先 Dispose 夹具，最后仅停止证明归属的本次 PostgreSQL，核对原实例、pidfile 和记录端口退出；工具、PG、夹具 native 清理分开报告。全部原数据、凭据、keys／代际、A／A2／A3 目录、新虚构数据、清单、账本、TRX、回执和日志均保留。不删除、重置、还原、操作其他服务。

本段不授权联合 UIA、捏脸真实会话同步、门廊 CRUD、系统信任、WinCred、main 合并、push、部署、日常安装、生产 DLL 替换、备份还原或崩溃演练。助手负责技术验证，用户主要验收手感、界面效果和流畅度。
