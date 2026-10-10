# B1b1 双客户端同步异常与宿主恢复通过

日期：2026-10-11。按用户对[精确计划](../planning/2026-10-11-account-sync-b1b1-local-plan.md)的批准执行一次，唯一运行器退出 0，输出 `accountsync_run_one_journey_passed_data_retained`。本轮真实旅程及保存证据的独立复核通过；不表示完整 Desktop、门廊或主观验收通过。

## 冻结与实际执行

批准候选提交为 `0233eee2636dc78af8dcbc79d25632aff2e9e1b3`。固定清单 `artifacts/account-sync-b1b1-approved.json` 的 SHA256 为 `566C4F6F385FC0FC9B95A401A8CD03673000A10048869E4DD9F3FBED5B921658`，冻结 512 份源码／候选输入及三份实际 PostgreSQL 工具，零缺失。根运行器核对与独立冻结复核均通过，后者 18/18，审计 SHA256 `66E8B85CD70779831558984F9F4C88F7EFA4482A6F02E659459D126C5531D83B`。执行期间未修改、重建或 restore 候选。

唯一真实用例 `RealHttpsCoordinatorsHandleConcurrencyLostResponsesAndHostRecovery` 的 TRX 结果为 1 Passed，零 Failed／NotExecuted，用例耗时 45.7969435 秒。正式 HTTPS 请求共 67 次；实际业务账本恰好 17 次，Configuration 8 次尝试、6 次成功，其他操作符合固定计划。迁移为零，历史仍为既有七项。

| 实际病例 | 配置尝试／成功 | 最终版本 |
| --- | --- | --- |
| 两正式协调器同时从同一版本提交，正式服务屏障证明两条不同数据库连接同时在途，只有一个提交成功 | 2／1 | 2 |
| 成功提交后丢失响应，真实 GET 核对收敛，回执为 TargetSatisfied | 1／1 | 3 |
| 丢响应后另一会话提交较新配置，旧意图不复活 | 2／2 | 5 |
| 已计数但未转发的请求只用原版本、原完整配置重试 | 2／1 | 6 |
| 同 origin／证书／密钥／代际重建宿主；断线期间保存 138、139，只上传最新整份配置 | 1／1 | 7 |

最终配置 eyeSize139，账号 PetId 保持不变；昵称为“异常同步团子”，profile_version2。两个会话都真实注销；唯一待发送邮件保留 Pending、attempts0，没有发信。两设备合计 12 份原档及回执，运行器逐份核对原始字节、完整配置、身份、版本、结果与时间关系。

## 数据保全与收尾

运行器保存并比较前后 22 表完整摘要多重集。全部旧行保留，包括已有昵称和四张配饰表；仅新增本轮一个虚构注册图、团子、默认桌宠房和两个会话。前后原文分别为 846853／847731 UTF8 字节，均通过大小预检、1 MiB 硬限及完整 EOF。

资源账本恰好六项，两个宿主先后停止和 Dispose，LifecycleTasksSettled 为 true；协调器、Dispatcher、故障控制及初始化任务均已收尾，临时 CNG 私钥实际确认消失。唯一固定十工具序列完成；PG、CMD 桥、工具进程和句柄、双流／reader 任务分别清理确认，pidfile 和记录端口监听消失。固定 start 工具按批准专用合同关闭私有输出流，不将它伪称为完整 EOF。

保留全部数据、原凭据、恢复代际、全部新旧服务密钥及本轮证据。原独立数据环境及全部历史失败／通过回执未重置、删除或还原。本轮额度已消耗，不能继续使用清单执行或补跑。

## 保存证据

证据保留于精确计划指定的 `run20261011accountsyncb1b1` 目录。公开记录仅含非秘密投影与摘要，不提交完整数据库原文、凭据或配置原档。

独立只读审计 269/269，blockers为空，重新计算 40 份保存证据的 SHA256、12 对原档／回执原始字节及完整配置摘要，并逐项归因 8 次 PATCH、比较 22 表旧多重集与精确新增图、复核资源及工具收尾。审计投影为 `artifacts/account-sync-b1b1-independent-saved-evidence/5e007728e6bd4c048004df29c93644c2/independent-saved-evidence.json`，SHA256 `EC45F2EA4ABD53E6E663CFA0C715C08D33B1E9C43C570CCE3D0921174508B311`，审计未执行真实资源操作。首份审计投影把空blockers序列化成[null]，保留原投影，仅用CreateNew补存正确空数组；没有修改正式证据或重跑旅程。

独立 JSON 对600ms间隔、恢复期间第一次保存138、pending迁移零没有单独时间线或计数字段；这三项依赖冻结且通过的固定旅程／初始化断言，不将它们说成另一次独立运行时探针通过。

| 回执 | SHA256 |
| --- | --- |
| resource-receipt.json | `BD04B73E806B5B1487A17C6DDBE9A61795AB32B4B525D28927E01EB35ACE2074` |
| account-sync-b1b1.trx | `C41322DDE18EDD922B9356FB96BA13A47DE4ACD0DB3C69EBD4DE75A41494A73A` |
| before.json | `BB7F4EA5BA71F6C9AAEBD765BF2C9CFEACDE0FFB63BE4A4A6B3D2FDE95B9DD07` |
| after.json | `60D089D17ABDCE94DBA2C7773EE1082836AD2902BCCD3869117DDA946C3463FB` |
| journey-evidence.json | `10C179CBA0C3DF351699087DF089C1D418792C055FFB0D481F75F58479BAAF80` |
| configuration-sync-evidence.json | `DBD29F2B7E8D77F8E0ABDA6D0D279376398E72120BE407CFE2F3C5F2C444877A` |
| fault-evidence.json | `C14CADCF9893137B589F9FB57105AAD41450CB63D0DBE38A236C63D74B4B0271` |
| scoped-graph-evidence.json | `411FE59514E52BE8FE58778E012ECA6AD68202041E54B3B93C14C386664662D9` |

## 五项要求与剩余门禁

正确性由实际完整配置、未知请求／响应和 12 原档核对验证；一致性由真实重叠 CAS、较新提交胜出、固定版本重试与零回传验证；安全性由固定虚构图、逐次正式会话／归属检查及旧图保全验证；可靠性由 Host 停止／重建、文件保留与任务／资源收尾验证；扩展性由限频、预算、有界输出和短锁验证。本轮只覆盖两客户端及 Host 生命周期，不能据此声称公开容量、OS 强杀、数据库崩溃或灾备还原通过。

完整 Desktop 定时器／关闭浮层后的正式 HTTP 同步、注销／换号迟到任务、门廊正式 CRUD／原生输入，以及界面效果、手感和流畅度仍有独立门禁。B1b1 使用实际 WPF 文件适配器，但没有完整 AppHost／可见 HWND 或系统 WinCred。未合并 main、推送、部署、更新日常安装或替换生产 DLL。
