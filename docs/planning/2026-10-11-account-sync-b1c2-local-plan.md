# B1c2 请求生命周期与完整桌面跨账号候选

日期：2026-10-11。状态：诊断候选离线门禁完成，本阶段真实资源尚未批准或执行。新C#60、旧helper276、SQL元数据151、Windows账号装配／UIA11及PowerShell新增511＋旧2350检查通过；固定正式入口在旧grant开启／新grant关闭时精确1个NotExecuted，止于资源初始化前。冻结前修正外层TRX遗留旧方法名，并增加14项精确入口与结果关联检查，正式业务和资源预算不变。构建与失败保留见[准备记录](../verification/2026-10-11-account-sync-b1c2-preparation.md)。冻结提交、清单和摘要以最终精确授权消息为准。前次[B1c1一次执行](../verification/2026-10-11-account-sync-b1c1-failed.md)整体失败且授权已消耗；其冻结清单、二进制、原始证据和失败回执保持原样。本阶段不能补写旧请求的取消原因，也不能借旧授权重跑或补SQL。

## 定位与修复边界

B1c1的唯一正式用例及两段真实桌面旅程通过，但外层合同拒绝一条未捕获响应的B账号读取。旧记录不能区分限频等待、发送、正文读取、超时或注销取消，因此仍未通过完整门禁。B1c2只补充真实生命周期证据及其可失败验证，保持每次登录比较完整配置、保持账号PetId、选择来源后统一两边、登录期间同步及后一次服务端成功提交为准的业务规则。

HttpClient的handler取消令牌包含20秒超时，不能单凭它取消就归因于注销。测试候选在生产同步操作外采用只观察的decorator，采集HttpClient之前的ReadAsync原令牌，绑定同一coordinator及精确请求；生产AppHost依赖仅增加默认关闭的观察注入入口，不主动Stop、手动Pump、替换响应或改变请求／保存行为。比较基线属于创建同步器前的choice读取，不伪造原同步操作身份；窗口绑定已确认保存PATCH的真实同步器。

新版证据独立Version2。请求登记与真实Send调用分别记录；保存单调时钟频率、发送前后时标、响应头状态、正文验证阶段、真正原Task的终态，以及原操作／handler／fixture／正文期限取消事实。保存有限错误类别，禁止保存异常正文、Cookie、密码、令牌、请求秘密或密钥。terminal observer观察实际Task的Canceled／Faulted／RanToCompletion并在导出前排空，不能用finally或IsCompleted猜测。生命周期事件最多4096条；仅新版fault证据的C#写入和PowerShell读取上限同步为1MiB，其余证据仍为131072字节，超限明确失败且不截断。业务与收尾完成时先保存原样fault trace，再执行生命周期合同；合同拒绝仍保留轨迹，Journey的Passed只在合同通过后写入。此分支的Completed表示业务与收尾完成，不表示合同验收通过；更早业务失败分支的原样生命周期文件仍没有导出能力，现有资源及桌面证据保留，不能声称所有失败均有完整轨迹。

每段B最终UIA注销在调用前绑定本次client、User、Pet、Session和实际coordinator，保存调用、正常Stopped状态、原操作取消与同会话成功Logout终态。仅当这些事实共同证明正常注销导致的非关键B桌面GET `/api/v1/me/pet`取消时，允许该段最多一条响应未完整捕获；原始null保留，不能计作成功HTTP。held、基线、身份核对、PATCH、注册和Logout仍要求完整成功；未知终态、Faulted、非2xx、错误身份、缺失原令牌链、fixture／正文期限／超时竞争或窗口之外取消一律拒绝。

600ms门禁用相邻真实Send调用的前／后单调时标核算保守下界，未发送的reservation保留而无Send时标。此证据证明handler调用开始间隔，不扩大为socket发包时刻。B1c1旧记录缺少这些字段，必须被新版拒绝，不能转换追认。

## 独立入口与预算

新stage为`AccountSyncB1c2`，独立grant为`DANGO_BACKEND_ACCOUNTSYNC_B1C2_APPROVED`，唯一固定FQN为`Dango.AccountSyncReview.Tests.AccountSyncB1c2JourneyTests.FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccountWithLifecycleEvidence`。新候选输出`artifacts/account-sync-b1c2-candidate`；旧B1c1及其他历史grant不能读取或初始化本阶段资源。

两段仍是late-get与late-patch：两个虚构账号各注册一次，四次正式登录，两个先后完整Desktop AppHost，一个正式HTTPS Host。实际A响应由HTTPS产生，hold只保留精确成功响应；A退出后B不能在旧pump结束前取得比较基线，迟到ACK不能确认A原档、污染B或重放给B。真实native固定为`B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E`，不替换生产DLL。

| 正式业务尝试 | 上限 |
| --- | ---: |
| Invite／Start／Confirm／Complete | 各2 |
| Login／Profile／Configuration／Logout | 4／2／3／4 |
| 合计 | 21 |

21次是不可退款的正式请求reservation预算。两次未转发的A本机注销意图单独记账，硬上限2。HTTP尝试最多128次、实际Send开始间隔至少600ms；业务期限10分钟，唯一vstest进程300秒。最多两个账号注册图、两只账号团子、两个默认桌宠房和四个不同会话；12个client、8个桌面transport，不增加业务额度。A最终昵称版本2、米色111配置版本2，B昵称版本2、蓝色130配置版本3；5份original和4份receipt，A迟到ACK原档无receipt。

## 待批准的精确资源

仅既有`C:\Users\fanh\AppData\Local\Dango\BackendDevelopment\run20261008b0\pgdata`，PG18 `127.0.0.1:63237`，库`dango_backend_b0`、角色`dango_b0_owner`。零迁移，保持原七项历史；不连接其他实例。新证据目录`C:\Users\fanh\AppData\Local\Dango\BackendDevelopment\run20261011accountsyncb1c2`须在执行前不存在并采用原受保护ACL；保留原凭据、恢复代际与全部新旧服务密钥。

固定十工具顺序为version、status、start、before-size、before-full、vstest、after-size、after-full、双账号scoped、stop。完整快照先做大小预检和1MiB硬限，比较22表旧摘要多重集合；九张注册图各新增2、sessions新增4，其余12表及四张配饰表不变。最终scoped绑定精确Owner／恢复代际／角色、A与B User／Pet／默认Room、完整配置及版本、昵称版本与四会话逐行归属和revoked；actor读数不能替代该SQL门禁。

一个本机HTTPS监听、一张短期证书、一个正式Host及两个顺序Desktop；不导入系统信任、不操作WinCred、不发邮件或启用worker。结束只停止本次实例及自有资源，清理本次临时CNG私钥，保留全部数据与证据。失败不重跑、不补SQL、不重置、删除或还原。

## 离线门禁和交付

离线正反例覆盖完整成功、已证实注销取消、原Task终态／observer排空、精确窗口与身份、关键请求仍严格成功、取消原因竞争、限频时标、stage／grant隔离及不可退款预算。正常注销取消是可验证的预期结果；未知错误不被包装为成功。共享工具只增加本阶段精确分派，并运行受影响旧合同回归。

独立复杂审查、隔离Release构建、真实普通临时文件反例与SQL元数据检查通过后，创建本地提交并冻结源码／二进制清单、独立批准摘要和固定入口。只有最终具体候选获得用户的新一次精确批准后才执行真实资源；准备操作不读取原state／keys或连接数据库。

正确性检查原终态和异常输入；一致性保留实际提交、晚到响应、原档与会话关系；安全性逐次绑定身份、固定目标、独立grant及无秘密证据；可靠性检查旧行保全、任务排空和本次资源收尾；扩展性保持请求、操作、输出、对象及期限硬限，不引入业务外架构。

生产内部`DisposeAfterPumpAsync`未公开Task的观察边界保留。门廊正式CRUD及主观手感／界面验收为后续独立门禁。本阶段不声称服务器已可部署、容量／灾备通过，也不授权main合并、推送、部署、安装或生产DLL替换。
