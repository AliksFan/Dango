# B1c2 生命周期候选准备

后续状态：本页保留执行前离线准备快照；用户批准后的唯一一次真实执行已通过，见[完整验证记录](2026-10-11-account-sync-b1c2-passed.md)。

日期：2026-10-11。结论：离线候选门禁通过，正式完整桌面／HTTPS／PostgreSQL旅程仍须本阶段新一次精确授权。未读取原state／keys、连接数据库、执行正式旅程或启动本阶段HTTPS；未推送、合入main、部署、更新日常安装或替换生产DLL。B1c1的原失败、数据、密钥、清单与二进制保持原样。

## 修复与独立审查

新增8份B1c2 C#源、独立PowerShell module／runner／boundary及SQL元数据检查；复用B1c纯budget、意图账本与hold能力。AppHost只增加integration-only的可选观察依赖，普通构造默认关闭。新stage、grant、固定FQN、run目录及输出隔离，历史批准不能初始化新资源。

handler取消令牌含HttpClient超时，新decorator采集之前的原Read令牌，并关联实际coordinator、成功保存、UIA注销和同会话成功Logout。记录真实原Task终态、原响应状态与正文阶段、单调Send前后时标及observer排空；未知null不被改成成功或取消。仅严格证实的每段至多一条非关键B桌面读取取消可容纳，保留原始null。关键读取、held、PATCH、注册、身份核对、Logout、归属、旧pump、retired与全部预算约束保留。

独立审查核实并修正observer锁内释放取消注册死锁、CTS LIFO回调被移除、原Update含保护GET＋PATCH、异步terminal观察次序、GUID排序会话与旅程顺序、choice基线尚无sync身份等问题。原令牌使用真实IsCancellationRequested与一次性采集，不猜测取消；正文期限观察独立timer，普通caller取消不会误记超时。成功保存的真实OperationId／coordinator绑定窗口，关闭窗口本身先要求原Logout Task成功和observer排空。

raw fault先保存再验证，Journey Passed只在验证成功后写；fault Completed不能解读为合同通过。仅该文件写／读上限为1MiB，最多4096事件；其他证据131072字节。更早业务失败时仍不导出独立原样生命周期文件，这一观察边界保留。

## 实际结果

| 门禁 | 结果 |
| --- | --- |
| 新C#生命周期、真实Task／CTS LIFO／timeout／fixture取消反例 | 60 Passed |
| 原B1c Boundary／hold与旧helper | 108＋168＝276 Passed |
| 实际getter SQL元数据 | B1c2 41＋B1c 41＋B1b 22＋原Scoped 47＝151 Passed；不连接DB |
| Windows账号装配与UIA | HostAccountBinding7＋AccountWindowUia4＝11 Passed |
| 新grant隔离 | 唯一正式FQN精确1个NotExecuted，新grant0／B1c1与B1b1旧grant1 |
| 新PowerShell | 511／511，含冻结前新增的14项TRX检查 |
| 旧PowerShell | Profile237、protected graph105、diagnostic96、B1a416、runner40、closure238、B1b761、B1c457＝2350／2350 |

Review、最终Server和Desktop均Release零警告／错误。TRX结果／定义／唯一execution关联和实际展开分组由根读回；拒绝门禁以唯一UnitTestResult及Require堆栈为准，不用汇总notExecuted计数推断。三组构建输出和普通fake transport／UIA不代替本阶段正式服务／数据库结果。

新native仍为`B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E`。最新`main@a4acdeb`包含在本分支，模型分工遵循AGENTS：复杂实现与独立审查使用gpt-6.1-sol/high，主Agent保持原模型。

## 失败保留与证据链

初次PowerShell490检查为488通过／2失败，异常Phase.RequestSequence越界导致错误码不符。修复为先验证边界并严格拒绝，未放宽原错误输入。证据：`artifacts/account-sync-b1c2-preparation/offline-root-39760848b9e549b9b677978bc1b727d3`及`artifacts/account-sync-b1c2-offline/6c5bc9058247411bbc73e4a99b2bb79b`。

最终9组PowerShell证据：`artifacts/account-sync-b1c2-preparation/offline-corrected-1c4788ba5b8f48329957ad2894446a0a`；实际SQL：`artifacts/account-sync-b1c2-offline/c3a1294db206470f9c12f99064406493/actual-b1c2-scoped-sql.json`。九工具均exit0、身份确认、双流EOF、readers结束及进程／输出资源清理。该目录另保留首次Review编译的两项MSTEST0044失败：过时DataTestMethod改为TestMethod，warnings-as-errors保持。

修正后Review构建与60／276结果：`artifacts/account-sync-b1c2-preparation/csharp-corrected-abc09efda2df47588149899fd6435cc0`。本轮SQL151结果旧110通过、新41因复制时遗留旧工件文件名断言失败；失败TRX、输出及资源回执保留。只修新metadata的工件名和提示，SQL与PowerShell逻辑不变。此前PS记录的第五项metadata源哈希因此改变，不能声称全部五项仍相同；四项实际PS源不变，最终metadata源码由后续构建及151项门禁覆盖。

最终Server／151 SQL／唯一grant拒绝／Desktop构建与11结果：`artifacts/account-sync-b1c2-preparation/remaining-corrected-979721ba866042de919dd1738615a21b`。未重复无变化的60／276或9组PS；Review bundle从此前通过目录逐字节复制，源码8项及两处seam不变。最终采用的8个C#工具均exit0、双流EOF、进程与输出资源收尾；helper不返回完整stderr正文，报告不声称已保存其完整文本。

冻结前根审查发现新入口的方法名已带WithLifecycleEvidence，但外层TRX仍要求旧名，会错误拒绝本阶段结果。仅修新B1c2 module及boundary，精确核对新方法／类、唯一结果／定义／执行条目的testId与executionId关联；新增1个正例和13个旧名、旧类、方法名、关联变更、定义缺失／重复及NotExecuted反例。最新PowerShell511项通过，当前五项源摘要全部相符，证据为`artifacts/account-sync-b1c2-preparation/trx-corrected-1a12a19fe13`。新实际getter SQL工件与此前已检查工件逐字节相同，并使用未变化的最终Server bundle重跑新SQL41项通过；两个新工具均exit0、双流EOF及资源收尾确认。原497项证据保留为窄修前历史，不能冒充当前版本验证；旧2350项、无变化C#源11项及其正式bundle沿用原通过结果。独立审查另直接核对正式入口、grant、环境、文件名、schema与生产者／消费者调用链，未发现其他确定集成阻断。

最终候选`artifacts/account-sync-b1c2-candidate/bin`从最终输出复制，820个文件逐字节摘要一致；正式入口所在Review bundle及依赖另核对来源。根结果读回记录为最后目录的`root-readback.json`。冻结提交、正式候选输入清单和摘要在最终授权消息提供，不覆盖任何旧阶段候选。

## 五项检查与未验证项

正确性由异常输入、原Task及精确阶段／窗口反例验证；一致性由实际原响应hold合同、原档和预算不退款验证；安全性由原身份／会话／sync绑定、独立grant和固定目标验证；可靠性由锁外observer、LIFO、原样失败轨迹与有界资源收尾验证；扩展性保持21业务／128尝试／600ms、对象、输出、事件及期限硬限。

以上仍为候选离线结果。实际完整双账号Desktop、正式HTTPS／PG、600ms真实轨迹、最终scoped图与四会话关系仍待[新精确计划](../planning/2026-10-11-account-sync-b1c2-local-plan.md)。生产内部DisposeAfterPumpAsync未公开Task，不能扩大为直接await全部内部任务。门廊正式CRUD及主观手感／界面验收另行推进，不据此声明可生产部署、容量或灾备通过。
