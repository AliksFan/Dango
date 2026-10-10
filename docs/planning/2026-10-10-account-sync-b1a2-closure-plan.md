# B1a2 closure：现存虚构账号的一次只读核对

准备日期：2026-10-10；2026-10-11获用户批准后按冻结输入执行一次并通过，额度已消耗。实际只读查询1、业务写入0、迁移0，目标与八项业务检查匹配，资源完整收尾，独立保存证据审计30/30通过，见[实际记录](../verification/2026-10-11-account-sync-b1a2-closure-passed.md)。以下保留原精确范围，当前状态更新不能使原清单重新可执行。批准前根PowerShell1132/1132、实际SQL元数据47/47及Release零警告／错误，见[候选记录](../verification/2026-10-11-account-sync-b1a2-closure-candidate.md)。原B1a2正常旅程通过而整体Failed的回执保留，见[原实际结果](../verification/2026-10-10-account-sync-b1a2-scoped-query-failure.md)。

## 精确目标与额度

- 工作树 `C:\Users\fanh\.codex\worktrees\backend-b5-windows\Dango`，分支 `codex/backend-b5-windows`。
- 唯一既有独立实例：`C:\Users\fanh\AppData\Local\Dango\BackendDevelopment\run20261008b0\pgdata`，PostgreSQL18，监听 `127.0.0.1:63237`；数据库 `dango_backend_b0`、角色 `dango_b0_owner`。
- 新stage `AccountSyncB1a2Closure`，唯一证据目录 `C:\Users\fanh\AppData\Local\Dango\BackendDevelopment\run20261010accountsyncb1a2closure`。原子CreateNew预留且设当前SID专属ACL，任何失败都消耗本次额度；目录存在即拒绝，不复用、清空或删除。
- 固定审批清单 `artifacts/account-sync-b1a2-closure-approved.json`、独立SHA256。执行前冻结运行器、SQL、共享工具、正式模型／迁移、离线元数据测试及其项目输入、此计划、三份实际PostgreSQL工具二进制和生成SQL摘要。批准后不改输入；旧stage、清单、修改过的输入或无摘要均拒绝。
- 最多5个工具，仅固定顺序 version、status、start、scoped、stop；**只读查询最多1次，业务写入0、迁移0、测试子进程0**。失败不能补查询，只允许按实际归属使用剩余stop。
- PostgreSQL／pg_ctl／psql为 `C:\Program Files\PostgreSQL\18\bin` 的固定文件；每次工具限时30秒，start／stop限时40秒。启动／停止只作用于本轮证明归属的实例和CMD桥，不连接或停止其他服务。

固定虚构身份不可由调用方改变：

| 项目 | 值 |
| --- | --- |
| SourceOwnerId | `4fd0debb-576b-4410-a8da-f8f434a2e390` |
| UserId | `c0179320-d30f-421f-a43a-7fd83f72999b` |
| PetId | `aac9ef69-694d-49bf-940d-3fc0d5992cbd` |
| DefaultRoomId | `c317c4f6-07a6-4cf2-8bde-8ca5e9fc4d8f` |
| RecoveryEpoch | `67559361-c6b5-487d-b4da-948bb049000a` |

## 只读查询与通过条件

唯一 `psql -X -w -q -A -t -v ON_ERROR_STOP=1` 连接固定目标，事务 `BEGIN READ ONLY`／`COMMIT`。SQL从正式scoped getter生成，绑定账号、团子、默认房、原SourceOwner及代际，添加同一query的数据库／角色／server_version_num／data_directory／listen_addresses／迁移历史。所有命令行SQL为ASCII，昵称用PostgreSQL `U&'\65B0\540C\6B65\56E2\5B50'`，保持 `PGCLIENTENCODING=UTF8`；运输界限16384字符，拒绝非ASCII与NUL。

完整EOF、工具退出、输出流显式关闭分别验证。stdout和stderr分别最多131072字符及131072 UTF8字节，不公开原始错误中的秘密。专用固定scoped模式返回两流原文；先CreateNew保存为 `target-facts.json` 与 `query-stderr.txt`，再校验退出、JSON、实际目标及业务事实。严格拒绝重复字段、错大小写、缺失／额外字段、错误JSON类型、浮点假整数、越界、截断或多份JSON。失败保存可得证据，不能补查。

目标元数据必须为上面的数据库、角色、数据目录和本机监听，版本18；历史恰好为以下7项：

1. `20261008074701_InitialAccountRoomFoundation`
2. `20261008085521_AccountLifecycle`
3. `20261009031410_RoomManagement`
4. `20261009055558_RoomAccess`
5. `20261009062701_RoomAccessIntegrity`
6. `20261009124814_AccessoryAccountBackup`
7. `20261010055529_AccountProfile`

业务八项计数必须为 users=1、validUsers=1、namedUsers=1、completeGraphs=1、pendingUnsentEmails=1、sessions=4、validSessions=4、revokedSessions=4。它们同时证明虚构邮箱确认及账号可用、昵称“新同步团子”／profile_version3、绑定的团子configuration_version6／eyeSize128、绑定的默认桌宠房和注册图、唯一未发邮件、四个正确代际会话全部撤销。SQL不输出密码、令牌、security stamp、确认proof、用户邮箱或私钥。

## 凭据、持久数据与收尾

批准清单校验和原子预留之后，才只读原 `state.json` 与DPAPI凭据，严格核对原目标并在子进程内使用，不输出连接串或秘密。读取有界，解密原始字节及时清零；沿用原凭据，不生成账号、凭据或密钥。

只检查原state／credential的SID权限，不重申原ACL，不读取或改变keys内容，不启动FormalHost、AppHost、HTTPS、证书、SMTP、WinCred、系统信任或后台worker。PostgreSQL启动／停止本身可产生正常实例日志及恢复文件；这里的“只读”指业务查询和数据变更额度0，不声称数据库文件系统完全不写。

实例归属以PID、创建时间、实际原生路径、数据目录、固定参数及CMD启动链证明。结束仅停止本次归属资源，分别检查PG、CMD、工具、Dispose、输出资源、pidfile与端口；全部真值才允许ClosurePassed=true。归属不明或收尾未确认则失败并报告残留，不按进程名停止。保留全部旧数据、新证据、原凭据、密钥和代际，不删除、重置、还原或补写。

## 五项约束与后续边界

正确性用实际生成SQL与正式EF元数据、Unicode精确解码和反例验证；一致性用只读事务、固定身份和不可退款的单次查询；安全性用固定目标、ASCII／完整输入冻结、凭据保护和严格JSON；可靠性用先保存事实、过程归属、完整EOF及独立资源收尾；扩展性用查询／进程／时间／字节硬上限。本计划不证明生产备份还原、服务重启、多设备并发或容量。

真实执行仍需本计划的新精确批准。原因是原B1a2一次性额度已消耗，其计划明示“不能补测试、补SQL或换数据库”，且 [AGENTS.md](../../AGENTS.md) 第4节要求“数据库、对象存储、端口、进程和云资源操作必须精确到环境和目标”。此次批准只开放上述一次只读核对，不开放后续B1b／门廊业务，也不授权main合并、push、部署、日常安装、生产DLL替换、持久资源删除或备份还原。
