# B1b1 同步异常候选准备

日期：2026-10-11。[精确计划](../planning/2026-10-11-account-sync-b1b1-local-plan.md)的候选实现与离线检查完成；本轮真实验证尚未批准、未执行，最终冻结清单及SHA256单独提供。B1a2正常旅程与单次只读closure的组合证据已完成，见[closure结果](2026-10-11-account-sync-b1a2-closure-passed.md)；旧Failed回执与旧额度保持原状态。

本轮独立stage覆盖两个正式HTTPS客户端、两个正式文件adapter／journal／同步协调器，固定五个异常病例、17次业务尝试。完整Desktop定时器／跨账号迟到任务、门廊、主观验收和生产恢复仍有各自门禁。

## 已取得的准备证据

- 独立预算22/22通过；artifact副本把Configuration限额8改成9，同22项测试为19 Passed／3 Failed。证据 `artifacts/account-sync-b1b1-budget/17a46d0605024b5b8cf714593cf39a74/budget-evidence.json`，SHA256 `386127CFE4DA78446C3E900B5E70B75DE10EC5A3D9A65E9402E0B846A9660713`。没有数据库、Host、证书、凭据或实际run访问。
- 实际PowerShell scoped getter离线生成SQL，显式绑定五个虚构身份；新21项与旧47项正式EF模型／ModelSnapshot检查68/68、零跳过，`artifacts/account-sync-b1b1-sql-build/5840e1d9c17d4e9097b7661a7078fe98/results/scoped-metadata.trx`，SHA256 `01FC76729CB0752DA9DDE349B1B960BAEBF57F02058F99AC4DD150371921B4E9`。SQL artifact `artifacts/account-sync-b1b1-sql/0bc6a21977444c67a95e9886fe3752a5/sql-review.json`，SHA256 `80EB3F0024AB5793CFC12B22E69C754EAFB2D2F4C5308B50A3B25C7719543E3C`。只核模型与生成SQL，未执行PostgreSQL语法。
- 初次完整候选Release构建零warning／error，初次无资源C#134 Passed、0 Failed、旧B1a2入口1 NotExecuted；过滤器误用旧类别名，因此旧入口仍被选中后由独立grant拒绝。回执 `artifacts/account-sync-b1b1-offline-csharp/c879b218576e4376a2695cd3c69beb70/offline-csharp.trx`，SHA256 `60838791128E8EAF74F780BF1F0290A6E25F64178404D04CE77D5C402778F2BB`。这不是两项真实资源旅程通过。
- 独立B1b1批准关闭、历史批准开启，精确唯一新旅程为1 NotExecuted、0 Failed，`artifacts/account-sync-b1b1-unapproved-gate/c587a223ad4747a6b48cba0e27e1bd69/unapproved-gate.trx`，SHA256 `60550E952D1BC57AED092A5FCBA1D40254135C356745AC6976166CC3FB2BA67F`。在owner initializer之前拒绝；未触达实际state／keys／DB。初次构建日志与DLL已保留于 `artifacts/account-sync-b1b1-initial-candidate/`，后续修复需重建最终候选。

## 修复与最终候选证据

已修复archive枚举顺序、CAS合法反序、真实生命周期任务归属、失败清理互相跳过和完整配置意图绑定。fixture在初始化开始前归属旅程；保留并观察initializer真实Task，单独检查其settlement；Start／Stop／Dispose保留真实Task并硬等待20秒，重复Dispose加入同一Task。恢复Stop聚合外层90秒、finally聚合240秒；各设备Stop／Pump／Dispatcher、Host、证书和owner清理独立尝试，未settled或失败不能通过。实际PATCH完整配置与对应成功保存原档逐份匹配；原档固定12步、唯一且全局排序。

独立审查发现恢复样本149／155超过正式70..140范围；Desktop直接抛异常，协议也拒绝。已改为138／139，最终139，版本7、8次PATCH／6次成功及17次业务额度保持不变；正式schema未改变。九个旅程实际使用的样本常量由双正式合同检查，原149／155仅作为拒绝负例。最初68项SQL元数据检查和155输入记录保留为历史准备证据，不能当作最终样本通过。

最终证据目录前缀 `artifacts/account-sync-b1b1-final-csharp/f47c1d51e75b47e0a000ee59eb4828af/`：

- 完整Release候选构建零warning／error；无资源C#168/168、零失败、零跳过。最终回执 `final-bounded-cleanup/offline.trx`，SHA256 `30002C066C6D0D1BABED51A373D04D22903238F72B2CD3743660C1F1283800DE`。最终测试DLL SHA256 `004EAF59D0F4AD56E6CD5C80819160C17127DF541166AFF864DFF62959B09A08`。首次最终构建的typed receipt nullable编译错误日志保留，显式拒绝缺失observed version后复验通过。finally追加仅含Step／FailureType和实际initializer／Host任务状态的TRX投影，原旅程失败时不覆盖原异常，正常路径报告故障仍失败；此修复后再次完整复验通过，之前回执全部保留。
- 新批准关闭、历史批准开启时，唯一真实旅程入口在owner initializer前拒绝，精确UnitTestResult为1 NotExecuted。最终回执 `final-bounded-cleanup/unapproved-gate.trx`，SHA256 `A4A0501C16931A7512C245F4BAFA243E07846EC65FFD47719C4BBFC2EBD477E8`。前次整理脚本误用TRX总计notExecuted（其值为0）导致投影报错；先只读原TRX逐项绑定TestId／精确方法／UnitTestResult及RequireWithFactory拒绝原因，未为投影重跑。后续仅因新增清理投影才复验，最终脚本使用逐项结果并通过，保存 `final-bounded-cleanup/final-csharp-evidence.json`；旧报错与旧回执全部保留。
- 原档排序helper副本去掉排序，15项测试为14 Passed／1 Failed；完整配置摘要副本退回仅eyeSize，10 Passed／5 Failed。`mutants.json`记录两份TRX摘要与正式源未变，正式helper15项在最终168项中全绿。
- 实际getter生成最终139 SQL，`sql-review-139.json` SHA256 `909ED462AB4DE20C9E75968F409E64B47481577ABFB2CC51044671A34B7E7253`；SQL文本SHA256 `449B2E3BE6AC89FEAAD3CEDCE32D43D83DF9760B12D2E7E20208EFAD5A6442F7`。旧155实际SQL在新增正式合同检查中1 Failed，回执 `metadata-invalid-old-input.trx`；最终新22＋旧47元数据检查69/69、零跳过，`metadata-final.trx` SHA256 `A0717185228399A6A7EB721BB2596041B6E4D89F965C7E6AD376110FEF1E29E5`。仍未执行数据库语法。

最终139 PowerShell边界761/761，证据 `artifacts/account-sync-b1b1-offline/5cff1699eba546c99cd6ee41e90d6e78/offline-results.json`，SHA256 `DA24A5E3FBFA90A3CD2BDCA1237A5A8E67C8CA2968DAC249FB4F56D19E89D842`。覆盖ordinal／NUL／控制键、CAS任意设备顺序、完整配置归因、独立工具计数、固定SQL及原始输出字符／UTF8字节上限、两流EOF与原文CRLF；原始输出使用真实helper配合纯假process，未启动真实子进程。

四个artifact错误实现分别产生94／24／1／2项失败（文化比较、跳过配置归因、覆盖原始证据、漏attempt计数），目标函数AST与最终139源码逐字相同。共享当前Profile237／B1a416／Closure238离线回归全绿；没有重复真实closure。汇总 `artifacts/account-sync-b1b1-mutants/405443c081c34b92b4ef93a125adc85c/final-review-summary.json` SHA256 `93E97500D69B2540CC7F48ADBE470FCD7D86959D349E64ED1EBE9E29ED579ACE`。四个PowerShell parser及diff检查通过；旧runner／protected graph／D1未重复执行，不能把其历史次数写成本轮证据。

另一代理已只读复核当前源码，源级blockers为空。真实执行必须先完成独立审计、本地提交、CreateNew清单与逐项冻结核对，并取得精确批准。未预留正式证据目录、启动数据库／HTTPS／证书，未读取真实凭据或keys。未合并main、push、部署、安装、替换生产DLL或删除持久数据。此轮真实结果、完整Desktop、门廊和主观验收尚未通过。
