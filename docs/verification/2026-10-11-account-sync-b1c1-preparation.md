# B1c1完整桌面跨账号候选准备

日期：2026-10-11。结论：候选离线门禁通过，固定正式入口仍需本阶段独立精确授权。未读取原state／keys、连接数据库、启动正式HTTPS或执行正式旅程；未推送、部署、更新日常安装或替换生产DLL。

## 实现与失败保留

新增独立owner／预算／双账号HTTPS fixture、实际响应hold、完整Desktop rig、唯一正式旅程、注销意图账本、证据合同与双账号scoped SQL；共享process helper只增加精确stage及有界输出分派。两份普通本地文件、真实SavedToDisk／timer／UIA、正式HTTP和最终PG约束属于待批准的真实门禁，不能以离线检查代替。

独立源码审查发现runner已预创建注销意图账本，首条C#却再次CreateNew，导致真实旅程必然中断。修正为同一existing handle核验后SeekEnd／Write／Flush(true)，不创建、覆盖或截断。首写要求文件为空，次写验证唯一首条、owner／User／Pet、不同client／Session；新增8展开普通临时文件正反例并比较拒绝前后的字节。

初次Review构建13处错误为两个文件缺少Pets命名空间、可空Choice访问和断言expected／actual次序。窄修保留Choice缺失即失败、所有业务断言与warnings-as-errors。失败输出位于`artifacts/account-sync-b1c1-preparation/csharp-82a23162e5e84f41ad8077d9c143e7f0`，保留Exit1及EOF／收尾；修正后使用新的证据目录复验，没有降低门禁或重新执行已批准真实旅程。

## 实际离线结果

| 门禁 | 精确结果 |
| --- | --- |
| B1c C# | Boundary86＋ResponseHold22＝108 Passed |
| 旧纯helper | RequestPolicy63、Archive15、B1b lifecycle8／fault12／evidence15／appearance11、两代Budget各22，共168 Passed |
| SQL元数据 | B1c41、原Scoped47、B1b22，共110 Passed；不打开数据库连接 |
| 独立grant | 固定完整正式FQN精确1个NotExecuted；新grant0、旧B1b1 grant1；原因为缺本阶段独立批准 |
| B1c PowerShell | 457／457 |
| 旧PowerShell | Profile237、protected graph105、diagnostic96、B1a416、runner40、closure238、B1b761；共1893／1893 |

PowerShell证据位于`artifacts/account-sync-b1c1-preparation/offline-073283d3ab4c4f8e88bdc6d37adbb571`；B1c实际getter工件为`artifacts/account-sync-b1c1-offline/33b538e0411d499bba0dde30b452b94a/actual-b1c-scoped-sql.json`，采用固定虚构scope。SQL包含双账号完整七字段JSONB、昵称版本、12个精确UUID、四会话所属User／Pet／epoch／stamp与revoked约束，模型与迁移snapshot列一致。

最终C#证据位于`artifacts/account-sync-b1c1-preparation/csharp-corrected-758fbc1b218d4b4d89f9ca0cb6c3bb0d`。Review与Server两份Release均零警告／错误；正式固定候选输出为`artifacts/account-sync-b1c1-candidate`。根实际读回四TRX每条结果、唯一execution、测试定义及组数量；十份B1c源文件相对构建前摘要全部不变。八个PowerShell和六个C#工具共14份回执均exit0、身份确认、双流EOF、reader settled及过程／输出资源收尾。root首次读回脚本误用EOF字段名的无效中间记录单独保留为`root-readback-invalid.json`，按实际`EofCompleted`字段修正后完整14工具读回为`root-readback.json`，未改原回执。

原SQL负例保留其真实旧工件；当前Fixed／Closure由当前getter重新生成，并记录来源摘要。B1b既有SQL工件的两个生成器摘要与当前源一致后复用。新B1c工件与其五项PS／metadata源摘要均由根和独立审查读回，不用手写SQL代替实际getter结果。

最后暂存检查发现B1c模块EOF多余空行，仅删除一个末尾LF字节，保留前后摘要与格式变更记录。随后在`artifacts/account-sync-b1c1-preparation/final-ps-64345c6b19634e3daec0be6dd959ee36`重新执行本阶段离线457检查，全部通过；最终五项源摘要均与当前文件一致。其实际SQL工件`artifacts/account-sync-b1c1-offline/1eb8824cf3d04d49ac969f7c8cdc863b/actual-b1c-scoped-sql.json`与已通过110元数据回归的原工件逐字节摘要一致；新增工具exit0、双流EOF与收尾确认，未重复无变化的旧阶段回归或真实旅程。

新native固定为`B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E`；候选实际DLL与该摘要一致。最新main合入和IME真实原生按键门禁见[独立记录](2026-10-11-backend-latest-main-absorption.md)，旧BFDD完整桌面证据不能替代本轮正式B772双账号旅程。

## 五项检查与未验证项

正确性由可失败输入／版本／配置及账本反例验证；一致性由不可退款预算、迟到原响应／取消／提交证据合同验证；安全性由独立grant第一调用、固定目标、精确身份与scope反例验证；可靠性由原档合同、原Task排空和进程EOF／收尾验证；扩展性由21业务／128HTTP／600ms间隔、任务／资源／输出期限验证。以上均为候选离线证据，不声称已执行正式并发、数据库恢复、容量或灾备测试。

生产controller内部`DisposeAfterPumpAsync`未公开Task，证据明确`InternalDisposeTaskObservable=false`；公开原pump、HTTP、fixture操作、清理账号与Host生命周期任务有排空检查，不能声称直接await全部内部Dispose Task。

独立复杂审查已读回修复后的源码、四TRX结果／定义／唯一execution、准确组数量、候选程序集路径、六工具回执、十项源摘要及实际native，未发现未解决阻断。正式拒绝TRX的汇总`notExecuted`为0；精确1项拒绝来自唯一`UnitTestResult outcome=NotExecuted`、独立授权错误信息及止于Require的堆栈，不以汇总计数推断。

复杂实现和独立审查遵循最新AGENTS的`gpt-6.1-sol/high`；资料／证据盘点使用`gpt-6-luna`，主Agent保持当前模型。正式一次测试范围以[精确计划](../planning/2026-10-11-account-sync-b1c1-local-plan.md)、冻结源码／二进制清单及最终授权摘要为准。门廊正式CRUD与主观手感／界面验收仍为后续独立门禁。
