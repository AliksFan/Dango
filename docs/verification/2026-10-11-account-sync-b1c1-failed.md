# B1c1 一次真实执行失败及保存证据复核

日期：2026-10-11。结论：B1c1 整体失败，未完成本阶段验收。正式测试进程退出0，唯一正式TRX结果为Passed，两段完整桌面旅程记录为Passed；外层runner退出1，故障证据校验抛出`accountsync_b1c_fault_request_rejected`，`TestsPassed=false`、`FaultsValidated=false`。不能把测试用例通过写成整体通过，也不能回改冻结清单、原门禁或原回执。

## 冻结范围与授权消耗

本次使用冻结HEAD `ce66ba60b7c28bf233a4a9587e79c79a5333b288`，清单`artifacts/account-sync-b1c1-approved.json`，SHA256 `F056344207D4C5C526CBBF52E946BAD41F811EC17098711BDECA17603314A7B3`；执行前539项源码／候选摘要核对一致。使用既有PG18 `127.0.0.1:63237`、库`dango_backend_b0`、角色`dango_b0_owner`及`run20261008b0\pgdata`，没有迁移或连接其他实例。

一次精确授权已执行并消耗，未退款。保存业务账本的序列1–21连续、Owner一致，操作计数为Invite／Start／Confirm／Complete各2、Login4、Profile2、Configuration3、Logout4，合计21；HTTP账本共66条，低于128硬上限。旅程耗时80.0959354秒。两次未转发的A本机UIA注销意图另行记账，不能冒充正式服务器Logout，也不增加或退款正式业务预算。

本次没有失败后重试、补执行SQL、重置、删除或还原。诊断修复和离线检查继续在已授权开发范围内进行；再次真实执行或追加scoped查询需要新的精确授权。本记录不表示B1c2已经实现。不改变main合并、推送、部署、安装或生产DLL替换的授权状态。

## 精确通过与整体失败

正式FQN为`Dango.AccountSyncReview.Tests.AccountSyncB1cJourneyTests.FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccount`。TRX结果／定义／执行条目各恰好1个，testId与executionId关联一致，CodeBase位于冻结`account-sync-b1c1-candidate`，计数为total1／executed1／passed1／failed0／notExecuted0。

保存旅程记录为late-get与late-patch均Passed：一个真实Formal HTTPS Host、两个先后完整Desktop AppHost、8个桌面transport及12个client；实际native健康帧、独立UIA、生产保存和timer流程均有本轮证据。四次登录对应四个不同会话，最终actor查询记录每账号2个会话、2个已撤销；最终A昵称版本2、米色111配置版本2，B昵称版本2、蓝色130配置版本3，恢复代际一致。该actor读数不替代最终scoped SQL的全部逐行关系谓词。

关键响应记录均成功：A held GET为Sequence17、版本1；B比较基线25在旧pump结束后，PATCH29为1→2。A held PATCH为47、1→2；B比较基线53在旧pump结束后，PATCH57为2→3。两次A独立清理注销35／63和B正式注销38／66均返回200；A retired主client3／8没有后续请求。两个hold均记录实际成功响应、caller取消已观察、明确释放并结束，`CleanupRelease=false`。

直接拒绝行是Sequence64：Client11、desktop／B、`GET /api/v1/me/pet`，B第二会话`8426a267-9270-4810-ad45-d9ef4af46b42`、PetReadOrdinal7、`PreviousPumpSettled=true`、`Retired=false`，`ResponseStatus=null`且`ResponseVersion=null`。其余65条响应状态均为200。冻结模块`eng/Dango.AccountSyncB1cPostgres.psm1`第696–698行要求每条响应状态为200–299整数，因此该行首先触发拒绝；第733–734行还要求pet响应版本为正整数。原回执的`ArchivesValidated=true`、`ScopeValidated=true`、`LogoutIntentsValidated=true`与`FaultsValidated=false`保持原值；其中ScopeValidated不能解读为最终scoped SQL通过。

Sequence64的终态未知。保存请求结构没有逐请求Outcome、TaskStatus、异常种类、实际发送时间或注销窗口绑定。源码先追加null响应记录，再等待限频、发送HTTPS、读取响应，完整捕获后才写入状态／版本；因此null不能区分尚未实际发送、发送取消、响应读取取消或其他失败。B最终UIA注销与timer停止是合理定位方向，但保存证据不足以确认该条实际因注销取消，不能补写取消flag或按已知序号豁免。后续若修订合同，应先补充实际请求生命周期及精确注销窗口证据，再保留held／baseline／PATCH／身份／Logout成功和全部原任务排空门禁。

## 原档与数据库保全

独立只读核对5份originals及4份receipts：base64原始字节SHA256、解码后的完整七字段、local／cloud规范配置SHA256、Origin／User／Pet、版本及receipt字段均与本轮清单一致。B两次选择及两次保存各有回执；A已提交的迟到PATCH原档保留，没有确认回执。两条logout-intents序列1／2绑定Owner、A身份、Client3／8和两个不同A会话，`Forwarded=false`。

仅使用本轮保存的before／after完整快照比较22表旧摘要多重集合，每表旧摘要及重复次数全部保留。invitations、registrations、challenges、emailJobs、users、pets、rooms、memberships、petStates各新增2行，sessions新增4行，其余12表行数与摘要不变；四张配件表不变。database／role／version／dataDirectory／listenAddresses／migrations前后一致，新增UserId恰好为本次A／B，二者profile版本均为2。

最终双账号scoped工具未执行，没有scoped结果证据。尚未独立验证该查询要求的完整注册图、stamp、房间关系及四会话逐行归属谓词。保存的范围输入与actor读数不能补作该门禁结果。

## 资源收尾与观察边界

固定十工具序列在故障校验处停止，实际9个工具为version、status、start、before-size、before-full、vstest、after-size、after-full、stop；status退出3表示原实例停止，其余退出0。九工具身份、进程退出／Dispose、reader结束及输出资源清理均确认。八工具双流达到EOF；start按`PostgresStartIgnored`契约主动丢弃输出，`OutputComplete=false`、`OutputDiscarded=true`，不能声称九工具均完成EOF读取。

resources.jsonl共有8条同Owner、连续序列资源事件：TLSCreated、HostStarted、DesktopStarted／Disposed两对、HostDisposed、TLSDisposed。两个桌面均Failures空，HWND关闭、UIA／pump／HTTP／账号操作排空、timer与Host timer停止、runtime停止、首页driver释放、coordinator停止、内存secrets释放及CleanupPassed均为true。Formal Host停止并Dispose、InFlightRequests0、生命周期任务已结束；临时TLS本机私钥身份已捕获并确认清理，NativeProbeFailed=false。未导入系统信任、未操作Windows凭据。

外层回执确认本次PostgreSQL、桥进程、工具进程及输出资源清理完成，postmaster.pid与记录端口listener均不存在。`DataRetained=true`、`KeysAndEpochRetained=true`，保留原数据库、本轮新增数据、服务密钥与恢复代际；原始state／凭据／密钥未为复核而读取。

生产controller内部`DisposeAfterPumpAsync`未公开Task，本轮明确`InternalDisposeTaskObservable=false`。已观察的原pump、HTTP、UIA、账号操作和Host任务排空不等于直接await该内部Task。HTTP开始间隔600ms由冻结源码的单调时钟节流实现，但保存请求没有逐次实际开始时间，无法从本轮trace独立重算间隔。HTTP结束证据只记录整体排空，没有Sequence64的逐请求终态。

## 证据出处

本轮原始证据目录：`C:\Users\fanh\AppData\Local\Dango\BackendDevelopment\run20261011accountsyncb1c1`。

- `account-sync-b1c1.trx`、`journey-evidence.json`：唯一正式用例、两段旅程、actor终值、计数与耗时。
- `attempts.jsonl`、`attempt-summary.json`、`logout-intents.jsonl`：不可退款reservation及未转发本机退出意图。
- `fault-evidence.json`、两份`late-*/desktop-evidence.json`：关键响应、Sequence64未知响应、phase／transition、hold及桌面清理。
- `configuration-sync-evidence.json`、两份`late-*/accounts/.../*.originals.json`及`*.receipt.json`：5份原档／4份回执与摘要交叉核对。
- `before-size.json`、`before.json`、`after-size.json`、`after.json`及对应空stderr：完整快照、22表旧摘要多重集合及新增行边界。
- `scoped-inputs-evidence.json`：拟查询的双账号精确范围输入，非scoped查询结果。
- `resources.jsonl`、`fixture-resource-summary.json`、`resource-receipt.json`：8资源事件、9工具、原校验结果及收尾／数据保留。
- `approved-inputs.json`、`input-hashes.json`与冻结清单：已批准输入和候选一致性。来源文档见[原精确计划](../planning/2026-10-11-account-sync-b1c1-local-plan.md)和[离线准备记录](2026-10-11-account-sync-b1c1-preparation.md)。

本次独立复核只读取已结束运行的非秘密保存证据和冻结源码；文档更新未执行测试、真实资源、数据库连接或补充查询，未修改原证据，也未提交。
