# 昵称 A5 PostgreSQL／HTTPS 验证通过

日期：2026-10-10。状态：精确A5执行一次完成；七项真实用例全部Passed、零失败／跳过，22表旧图保全及资源收尾通过。一次性额度已消耗，不续跑；原数据、密钥和证据保留。

## 执行与业务证据

用户在完整A5精确批准卡后明确回复“继续”。根按该计划，仅在新bundled PowerShell宿主执行固定runner一次，不build／restore。执行前分支 `codex/backend-b5-windows`、HEAD `b0dfce393c134c37fd31682f1b5e6c537b0b1fa0`，工作树干净；根与独立复核清单 `artifacts/account-profile-a5-approved.json` SHA256 `32A7489C276F328AB9A6419CD80A93D3F58F910F08A0F5091FFB1224A05C414F`、299输入全部匹配、零缺失。A5目录不存在，原pidfile不存在，生产DLL摘要仍为原基线。

OwnerId `72f83ab2-239a-4747-afcc-eaed0a5575a9`，原证据目录为LocalAppData下 `Dango/BackendDevelopment/run20261010profilea5`。runner退出0，输出 `profile_run_seven_tests_passed_data_retained`。核验精确六项历史后，唯一前向应用 `20261010055529_AccountProfile`；保存证据历史从六项变为七项，其余历史ID不变。

| 用例 | 实际结果 |
| --- | --- |
| 独立连接同版本并发 | 一次提交、一次冲突；Passed |
| 重名与连续修改 | 允许重名，账号／Pet／房间身份保留；Passed |
| after_profile故障 | 昵称和版本事务回滚；Passed |
| before_commit故障 | 昵称和版本事务回滚；Passed |
| 跨账号及撤销后锁内再检 | 正式服务拒绝；Passed |
| precommit会话到期 | 回滚昵称；Passed |
| 正式HTTPS资料边界 | 会话／归属／CSRF／Origin／坏JSON／异常昵称拒绝，合法修改成功；Passed |

67次尝试全部按固定预算核对：Invite／Start／Confirm／Complete各9、Login10、DirectProfileUpdate11、HttpProfilePatch8、SessionRevoke1、HttpLogout1；七case分别7、14、6、6、13、6、15。失败与回滚均计数，没有退款或重跑。scoped实测九个有效账号及完整图、十个同归属／代际会话、四个有昵称账号、五次资料版本增量、两撤销会话、九封Pending且未投递的邮件。

## 旧数据保全

三份完整图均通过22键／count／digest数组结构核验。根从已保存文件另行复核迁移前后count完全一致、全部旧行摘要保留，以及测试后仅批准的新图增量；未补查数据库。原账号nickname仍NULL、profile_version=1，原ID、安全字段、Pet、房间及关系保留。

| 配饰保护表 | 迁移前 | 迁移后 | 测试后 | 全行摘要多重集 |
| --- | ---: | ---: | ---: | --- |
| accessory_blobs | 37 | 37 | 37 | 完全一致 |
| accessory_asset_references | 38 | 38 | 38 | 完全一致 |
| accessory_current_outfits | 19 | 19 | 19 | 完全一致 |
| accessory_commit_receipts | 22 | 22 | 22 | 完全一致 |

同源大小预检依次839697、837606、844959 UTF8字节；完整输出在批准的1MiB字符／UTF8字节双硬限内、EOF完成、读取失败None。三个有效快照先保存后比较。配饰异常测试行原样保留；未调用配饰API或读取库外图片。

## 资源收尾与交付边界

实际13个工具，完整 `pg_ctl32596 → CMD6536 → PG24108` 原生桥链通过。工具进程退出、Process Dispose、双流显式关闭及输出资源清理全部确认；finally只停止本次归属已证明的PG。七夹具、28条资源账本、一HTTPS启动、七Dispose，fixture native清理全部确认。

根追加只读检查PG／CMD／13工具PID均不存在，pidfile不存在，记录端口无监听。原数据、凭据、keys／恢复代际及全部历史和A5证据保留。未导入系统信任、操作WinCred、启动SMTP或发邮件，没有删除、重置、还原或其他服务操作。

原receipt SHA256 `EC958B04D0A27677DEE4A7738B22E0754AB4B328FD7FB52DD77FBA4E0D84BCF0`，TRX `B225A2B19F14AB2C931DAF12815B57FAAD06280ECB223847FBF426538FF82DD8`，原件未改。根非秘密投影 `artifacts/account-profile-a5-result/c9983ef1dd4246a2907469885a66f69d/root-summary.json` SHA256 `3660A50C1B08B4EC9E747FC3B196FA2D62CC9F9A5C1CD9F2DAA607AA097B3FB1`，包含三图／大小／账本／scoped证据的准确摘要；同目录source-snapshots以CreateNew保留执行源和命令。

独立只读复核投影与13份原文件摘要全部一致、TRX恰7Passed、Sequence1–67与逐操作／case预算完全一致；使用独立Ordinal摘要计数多重集算法确认22表旧行及四配饰表完整保全，708个旧账号昵称NULL／资料版本1保持。28条资源账本、13工具输出及native收尾均符合契约；追加检查记录的15个PID均不存在、63237无监听、pidfile不存在。该复核没有连接PG、补查数据、读取秘密或修改原文件。

本次证明昵称服务、事务及正式HTTPS边界；真实Desktop／WinCred登录、每次登录比较、连续会话同步及门廊端到端技术验证仍属于后续联合阶段。人工验收仍由用户判断界面、手感和流畅度。未合并main、push、部署、更新日常安装或替换生产DLL；备份／还原及崩溃恢复尚未由本段验证。
