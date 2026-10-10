# 昵称数据库 D1 只读目标诊断计划

日期：2026-10-10。状态：候选、有效离线红绿、原回归、独立审查及36份输入冻结均完成，等待本段精确批准；尚未批准或执行，不得启动、连接数据库或读取 DPAPI 凭据。A／A2／A3／A4 的一次性额度已消耗，旧清单和证据保留。

## 目的与固定目标

[A4](../verification/2026-10-10-account-profile-a4-target-failure.md) 已完整读取 JSON 并正常收尾，但目标／历史核验拒绝；旧顺序未保存被拒绝的实际字段，具体差异尚未确定。D1 只收集确定后续方案所需的元数据，不迁移、不执行业务测试，也不放宽原目标门禁。

- 唯一既有实例：LocalAppData 下 `Dango/BackendDevelopment/run20261008b0/pgdata`，数据库 `dango_backend_b0`，角色 `dango_b0_owner`；仅 PostgreSQL 18 正式安装的三份精确工具和旁依赖。
- 使用原 `state.json` 记录端口，仅监听 `127.0.0.1`。冻结该文件摘要，审批准备仅读取哈希，不解包凭据或恢复代际。执行时先核 state 的版本、目标和端口，拒绝既有 pidfile／监听，版本和状态检查通过后才在内存读取原 CurrentUser DPAPI 凭据。
- 唯一新证据目录：同级 `run20261010profiled1`。原生 CreateDirectory 原子预留，当前 SID 独占 ACL，reservation／owner／query-attempt／回执均 durable CreateNew。即使未查询或失败也消耗，不自动重试，不借用七项业务测试的 owner 和账本。
- 使用新的 bundled `pwsh -NoLogo -NoProfile` 宿主。默认仅预览；真实执行必须显式开关、外部准确摘要及全部输入匹配。在 reservation 前、启动前、唯一 SQL 前各重新核验。

## 唯一只读查询及输出

恰一次 `BEGIN READ ONLY`，经 `psql -X -w -q -A -t -v ON_ERROR_STOP=1` 返回以下七个唯一字段：

| 字段 | 来源／用途 |
| --- | --- |
| database | `current_database()`，核验目标库 |
| role | `current_user`，核验连接角色 |
| version | `server_version_num` 整数，核验 PG18 |
| dataDirectory | `data_directory`，核验目标实例 |
| listenAddresses | `listen_addresses`，核验仅本机监听 |
| migrations | `dango.__ef_migrations_history` 的 MigrationId 排序数组 |
| profileColumnsPresent | `information_schema.columns` 中 users 的 nickname／profile_version 两列是否都存在，只返回布尔值 |

不查询账号、团子、房间记录、ID、昵称内容或业务行摘要。迁移历史表和列元数据是唯一表读取。固定 SQL 无外部插值；不接受自定义 SQL 或查询重试。结构验证要求七唯一键、正确类型、有限字符串、最多32个迁移名称及规范格式；重复键／不可信输出拒绝。普通完整 EOF 合同的 stdout／stderr 各硬限131072字符，严格 UTF8 解码；D1 无权使用 A4 的1MiB完整业务图合同。

先 durable 保存 `target-facts.json`，再计算并保存 `target-matches.json` 的六项匹配。目标／历史不匹配是本次诊断的有效结果，不会触发业务写入或自动迁移。另记录昵称迁移是否存在及两列元数据；该事实不授权接受不同历史。后续方案必须依据实际字段，不能推断七项业务已通过。

## 精确资源与额度

最多五个工具进程：postgres 版本、pg_ctl 状态、pg_ctl 启动、单 psql、pg_ctl 停止；各最多一次。只读查询额度1，业务写入尝试0，迁移0，业务测试0。允许 PostgreSQL 启停必要的实例日志／控制文件及本次证据文件写入，业务数据保留。

无 dotnet／vstest／服务端／客户端启动，无测试证书、HTTPS、SMTP、邮件投递、WinCred 或系统信任操作。无重置、删除、还原、备份／崩溃演练或其他 PostgreSQL 服务操作。未授权 main 合并、push、部署、日常安装、生产 DLL 替换。

工具使用已验证的原生 executable／句柄／PID／创建退出时间和完整 `pg_ctl → CMD → PG` 桥链。启动证明成立才允许停止；finally 再核同一归属，只停止本次实例。工具进程退出、读取 EOF、reader 显式关闭和输出资源分别门禁；pending、sticky 关闭失败或无法证明归属均报告未确认，不盲目 kill，不宣称成功。原始失败不被收尾覆盖。

最终报告查询是否到达、事实是否保存、六项比较、PG／CMD／工具／pidfile／记录端口退出及数据／keys／代际保留。原凭据、密钥、全部失败证据和历史清单不修改、覆盖或删除。

## 批准前证据与固定清单

最终模块 SHA256 为 `F32B1B9582C90B54AA1F42EA0B379DEEE683464146C46333710309F516AFECE8`，runner 为 `E652A8E1A73C6861716F8DC1BBD3D55A66B09CDF42A5CAD23F25E1FD67C43924`，新测试脚本为 `AD0393A140050CB62F16891698E98DF52EAE867FDE147637B5D2072204AEA73D`。三文件语法及 diff 检查通过，独立只读审查没有发现具体阻碍；审查没有执行真实入口或资源操作。

同一最终96项脚本对旧85ED模块／新runner为 **6/96**，对新F32模块／同一runner为 **96/96**。六项仅审查新runner的AST所以两边均通过，新增90项模块行为／AST均在旧版本失败。覆盖零业务预算、七唯一字段／异常类型／重复键、六个独立目标差异、只读SQL、固定合同、审批清单篡改、独立reservation、先保存后比较及分别收尾。首轮 **5/96、95/96** 的唯一绿灯失败来自测试变量 `$matches` 与 PowerShell 自动变量 `$Matches` 冲突；修正为 `$comparisons` 后正式红绿通过，原源码和回执保留，正式模块没有因此修改或放宽。

独立证据索引在隔离测试工作区 `artifacts/account-profile-target-diagnostic-final/6c03e95e71be4a5fa0da2003db32c2e5/final-evidence-index.json`，SHA256 为 `82FA991FC349DDF74BE7F30658FD7DC37A8C6EC433CC15D9B8BBA866A0BA33C8`，包含红绿回执、准确源码快照及复现命令。原237项在新模块回归 **237/237**。所有测试均为fake／纯数据／AST，无真实数据库或凭据读取。

根复制最终模块和测试后也各执行一次：新 **96/96**，回执 `artifacts/diagnostic-offline/e31f056b49a44902b8f6ff9022fd2983/offline-results.json`、SHA256 `37586BD2CBA2975D8F613E825F71FF8D42C10F7C7C136CF97CAD1DFC91B6B568`；原 **237/237**，回执 `artifacts/account-profile-offline/ab139cbbb82b469ca5055807b6736a90/offline-results.json`、SHA256 `B7A9FDD3E9A97DD028E6FAB57410C228977353887A85E8BD6A43A263AD0EEE39`。二者 `ExecutedDatabase=false`，新测试 `RunnerEntryExecuted=false`。日志分别在 `artifacts/account-profile-d1-root-diagnostic/93326a2897ca467eb3e73d6140189ab3/` 和 `artifacts/account-profile-d1-root-regression/80a20188659a4938b0df777bfc3fc6cf/`。

固定清单 `artifacts/account-profile-d1-approved.json` 以 CreateNew 冻结 **36** 份输入：D1模块／runner、state.json摘要、三个PG工具及全部旁DLL、bundled pwsh；缺失0。准确 SHA256 为 `472EE72B5EDA17AC49B4B90F5522475E9596B175AF67894E4DEEC304F52F33E8`。根和独立审查分别复核全部36份匹配，集合差异／哈希不匹配均0。state仅读取哈希，不解包凭据；清单不包含密码、连接串或keys。预览记录 `artifacts/account-profile-d1-preapproval.json` 为 `Executed=false`、`Approved=false`、`DatabaseConnected=false`、`CredentialRead=false`；D1目录不存在、pidfile不存在、原记录端口无监听，A4记录PG／CMD／工具均无残留。旧四份清单及A4原回执／源码快照未覆盖。

真实执行必须在新宿主调用 `eng/invoke-account-profile-target-diagnostic.ps1 -ExecuteApprovedPlan -ApprovalManifest artifacts/account-profile-d1-approved.json -ApprovalManifestSha256 472EE72B5EDA17AC49B4B90F5522475E9596B175AF67894E4DEEC304F52F33E8`。本段只冻结计划，不表示查询或诊断已执行。

## 五项设计检查

1. 正确性：先验证结构并保存事实，再比较目标；六个单项 mismatch 均能暴露，格式错误不能伪装正常诊断。
2. 一致性：原子一次性 reservation、durable 查询意图、只读事务和输入复核；失败不退额度或重放查询。
3. 安全性：精确本机目标、最小元数据、严格外部输出、SID ACL、DPAPI仅内存和秘密字节清零；回执不记录环境或原异常正文。
4. 可靠性：保留事实及所有旧证据，原生归属证明后收尾，进程和输出资源独立确认；备份／还原仍未验证、不在本段。
5. 扩展性：输入枚举最多1024项、迁移最多32项、字符串和输出有界；诊断不随业务记录量增长，也不新增后台任务。
