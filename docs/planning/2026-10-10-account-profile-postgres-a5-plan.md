# 昵称 A5 精确数据库验证计划

日期：2026-10-10。状态：用户在精确批准卡后要求“继续”，已按本计划执行一次、退出0；七项真实测试全部Passed，22表旧数据保全和资源收尾通过。见[A5实际证据](../verification/2026-10-10-account-profile-a5-postgres-passed.md)。A／A2／A3／A4／D1／A5额度均已消耗，不续跑或重试；下文额度和批准前证据保留为历史，不授权再次执行。

## 当前事实与候选范围

[D1诊断](../verification/2026-10-10-account-profile-d1-target-diagnostic.md)已真实完成一次，只读元数据确认当前数据库、角色、PG18、原目录、127.0.0.1监听均匹配；历史比旧计划多 `20261009124814_AccessoryAccountBackup`，昵称迁移尚未应用。A5只接受下面六项精确历史，不接受任意额外迁移：

1. 20261008074701_InitialAccountRoomFoundation
2. 20261008085521_AccountLifecycle
3. 20261009031410_RoomManagement
4. 20261009055558_RoomAccess
5. 20261009062701_RoomAccessIntegrity
6. 20261009124814_AccessoryAccountBackup

该配饰迁移来自本地独立C2分支，源和历史授权已只读核实；本轮保持它的四张表及数据，仅前向应用 `20261010055529_AccountProfile` 并执行原七个昵称用例一次。配饰产品代码／同步接入另属后续范围。当前root的正式单迁移入口验证唯一已知pending为Profile，并指定该目标；EF不会对当前assembly外的已应用迁移生成Up／Down。当前模型与snapshot独立检查仍需通过，不以指定target跳过EF报警代替模型验证。

## 精确环境与一次性额度

- 仅原LocalAppData下 `Dango/BackendDevelopment/run20261008b0/pgdata`，数据库 `dango_backend_b0`、角色 `dango_b0_owner`、原state记录端口及仅127.0.0.1监听。使用原DPAPI凭据、b1tests-keys和恢复代际，在内存处理秘密；不创建新密钥或改变恢复代际。
- 新证据目录为同级 `run20261010profilea5`。原生原子目录预留、当前SID独占ACL及durable CreateNew一次性owner／账本。C# owner只认精确A5目录和本次活宿主；失败也消耗，不退款、重放、续跑或复用旧目录。
- 新bundled PowerShell宿主执行固定runner。真实运行不build／restore，仅已冻结Release程序集。显式执行开关、外部准确摘要及全部输入匹配是必需门禁；reservation前、迁移前、业务测试前再次复核。
- 最多13个工具：版本、状态、启动、三次同源大小预检、三次完整只读图、唯一昵称迁移、一次七项vstest、一次scoped只读图、停止。各工具／阶段至多一次，失败不补查询。

## 旧图保护与证据顺序

完整图包含原18张业务表和四张配饰表，共22张。四张新增保护对象为 accessory_blobs、accessory_asset_references、accessory_current_outfits、accessory_commit_receipts；每行完整 `to_jsonb` 在PG内计算不可恢复SHA256，保留有序摘要多重集和总count。它们不在允许新增业务图白名单内，所以修改、删除或新增任一配饰记录均失败。既有异常配饰测试行作为不透明记录保护，不自动修复或规范化。

`counts`／`digests`必须恰22个正确大小写的键；count为非负整数，每项digest必须为小写64位hex数组且长度等于count。缺表／缺键／额外键／类型异常不能表示保护成功。三个有效完整快照均先校验结构并durable CreateNew保存，再比较目标及旧图，失败时保留已取得的合规证据。

users摘要只排除本次新增的nickname／profile_version两字段，它们另行逐旧账号核验：nickname仍NULL、profile_version=1，账号／Pet／房间身份和其他原字段保留。迁移后22表count与原图完全一致；业务测试后仅固定九个注册图和十个新会话增量允许，其他表及原记录保全，配饰四表零增删改。历史须从精确六项变为六项加唯一Profile。

三个完整图各先用同一JSON表达式小输出测量，限定1–1048574 UTF8字节，为psql CRLF预留两字节；完整正文仍独立限制1048576字符和UTF8字节，stderr及其他工具各131072字符。超限／解码／I/O／取消／未EOF都失败，不自适应扩大预算。结构审查确认Profile源仅改变users两列和两个check；本段不新增通用schema审计或读取库外配饰图片。

## 唯一业务与资源预算

固定类 `Dango.Server.Tests.Accounts.AccountProfilePostgresTests` 恰七项一次：并发版本、允许重名及身份保留、after_profile回滚、before_commit回滚、跨账号及撤销再检、到期precommit回滚、HTTPS资料接口权限／CSRF／Origin／异常输入。恰七项实际Passed且零跳过才表示业务通过。

| 操作 | 最大变更尝试 |
| --- | ---: |
| Invite／Start／Confirm／Complete | 各9 |
| Login | 10 |
| DirectProfileUpdate | 11 |
| HttpProfilePatch | 8 |
| SessionRevoke／HttpLogout | 各1 |
| 合计 | 67 |

case额度依次7、14、6、6、13、6、15；拒绝、回滚、重放均消耗。每调用前先durable Flush账本，短锁不包住真实并发事务。HTTPS case恰22请求；不因失败退次数。

最多九个虚构 `profilea-{OwnerId:N}-{random:N}@example.invalid` 账号及各自邀请、预注册、挑战、未投递EmailJob、Pet、默认桌宠房、房主席位和PetRoomState；十新会话，其中两撤销。十九资料修改尝试预期五次提交成功、四个账号有昵称。核对九个完整同归属图、十个同代际会话、五次资料版本增量，以及九封Pending／Attempts=0／无AcceptedAt或Lease的邮件。

七夹具各生成一份短期测试证书，HTTPS case仅一个本机临时监听，精确pin。七个完整夹具生命周期、恰28条资源账本；逐夹具DisposeAsync后确认native资源。无系统信任导入、WinCred、SMTP或dispatcher，不发邮件。该段不启动桌面客户端或联合UIA，不执行房间生命周期／配饰API业务。

## 收尾、验证与固定输入

完整原生工具身份和 `pg_ctl → CMD → PG` 桥链仍必需；finally再核相同归属后只停止本次PG。进程退出与EOF／reader显式关闭／输出资源分别门禁，无法证明归属或pending／sticky关闭失败均报告未确认，不盲目kill。旧失败码及原始证据不被收尾掩盖。

结束检查PG、CMD、工具、pidfile和记录端口退出；保留原数据、凭据、keys／代际、配饰库外文件，以及所有历史和A5目录、虚构数据、TRX、账本、清单及日志。无删除、重置、还原、备份／崩溃演练或其他服务操作。未授权main合并、push、部署、日常安装、生产DLL替换。

最终模块SHA256 `3903F20CF47B1B459C9F647C2AEE13BE6EECF54AE49A65CA0F68B429DC1C38AC`，runner `C99B314F971744975353ACC9743E4FDECD2D83A2D007D6506072EF30B148E090`，owner源 `AC2631CC38A892443ED45A650EF4C3033CC5785DB44B522EDD14BD9416A54B79`。语法及diff检查通过；独立源码审查未发现具体阻断，未触及真实资源。D1执行源码已在其结果目录CreateNew快照，不随本次修订改变。

最终新脚本 `eng/test-account-profile-protected-graph-boundary.ps1` SHA256 `B2F2221A30F6C8C5F7E1AC93E6CCAE5CC662878697D24BCB69EC2437A2641EBA`，同一脚本旧F32模块／569B runner **9/105**，新3903模块／C99 runner **105/105**；96项新增边界在旧候选失败。覆盖精确六历史、22图键／异常值、四配饰增删改、完整只读SQL、三个快照结构／保存顺序及五个新增冻结源分别改变时拒绝。独立证据索引在隔离测试工作区 `artifacts/account-profile-a5-boundary/7e86b0ae0b994e0ab14ddd32a7932fb9/final-evidence-index.json`，SHA256 `63D5C1FA1FC003F4234A736C6C9B1C5BBDEA726D972B950AF40B05C6B8CE63CA`；旧／新源码、准确回执、命令及日志保留。根整合后新脚本执行一次 **105/105**，回执 `artifacts/protected-graph-offline/69f9dbc3943d4a0eab8c2400606f5895/offline-results.json` SHA256 `08970C1C17078D43D4876879B920D042B5829AF25597A7D7D3063A773EC68ADA`，`ExecutedDatabase=false`、`RunnerEntryExecuted=false`。

根原237项首轮 **236/237**：唯一失败是虚构清单固定12项的旧断言；新增五个明确源后实际17，正好暴露保护范围增加。保留原B69脚本快照及回执 `artifacts/account-profile-offline/96fc898a56774cae979b18b6ebcef4b2/offline-results.json` SHA256 `969378E0272B55C32E90765137B95F8F25D5AD09FE8117E5E60698E7356CA64B`。将该测试更新为固定17且要求五源全部存在，正式实现未为此修改或放宽；最终脚本SHA256 `1ED8A009E5A2830F569925E1AD256E44660BD2E6790525FE0CC6F625FF5A537A`。修订后一次回归 **237/237**，回执 `artifacts/account-profile-offline/c4f56953588444439f0dea392aa88975/offline-results.json` SHA256 `B7A9FDD3E9A97DD028E6FAB57410C228977353887A85E8BD6A43A263AD0EEE39`，`ExecutedDatabase=false`。两轮日志及原脚本在 `artifacts/account-profile-a5-root-regression/` 各独立目录保留。

Release测试构建零错误；显式关闭B1／Profile批准后，owner边界／Profile模型／正式单迁移检查 **20 Passed**，真实Profile用例恰 **7 NotExecuted**、零失败。TRX `artifacts/account-profile-a5-static/997f68072de14e3db29d25f3df82bbd4/a5-closed-gate.trx` SHA256 `4F91D336DAD2BA66678E6CA19DAB850249661FB459529DF018E2444250A06114`。关闭门禁不证明七项真实业务通过。

新清单 `artifacts/account-profile-a5-approved.json` 以CreateNew冻结 **299** 份输入、零缺失；准确SHA256为 `32A7489C276F328AB9A6419CD80A93D3F58F910F08A0F5091FFB1224A05C414F`。包含原Release执行依赖、原八项明确输入，以及正式迁移入口／单迁移helper／数据库options／DbContext／ModelSnapshot五个源文件。根和独立清单复核299份全部匹配；独立原始键集合精确一致、无重复键或非法摘要，确认六历史／唯一Profile／七用例／67预算。预览 `artifacts/account-profile-a5-preapproval.json` 为Executed=false／Approved=false／DatabaseConnected=false／CredentialRead=false；A5目录不存在、pidfile不存在、原记录端口无监听，D1记录进程均已退出。旧五份清单、原回执和源码快照不覆盖或执行。

实际执行命令为新bundled宿主运行 `eng/invoke-account-profile-postgres-tests.ps1 -ExecuteApprovedPlan -ApprovalManifest artifacts/account-profile-a5-approved.json -ApprovalManifestSha256 32A7489C276F328AB9A6419CD80A93D3F58F910F08A0F5091FFB1224A05C414F`；已调用一次并完成，不再调用。

## 五项检查

1. 正确性：精确六历史、22图结构与完整旧记录保全；业务反例可失败，跳过不冒充真实通过。
2. 一致性：单次durable owner／预算、并发事务及版本冲突、故障回滚；配饰数据只读且完整多重集比较。
3. 安全性：固定本机目标、身份及数据归属、HTTPS pin、CSRF／Origin／输入边界；秘密只进内存及精确子进程环境。
4. 可靠性：先保留合规快照再比较，单次失败保留数据及证据，原生归属后收尾，进程和输出资源独立确认；备份还原仍不在本段。
5. 扩展性：图测量与字符／字节双硬限、固定用例／请求／进程额度；额外数据超过预算明确失败，不自动扩大或引入后台任务。
