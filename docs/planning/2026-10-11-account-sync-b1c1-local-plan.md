# B1c1 完整桌面跨账号迟到响应候选

日期：2026-10-11。状态：候选实现与离线门禁完成，真实资源操作尚未批准。新C#108展开、旧纯helper168展开、SQL元数据110展开及八组PowerShell共2350检查全部通过；固定正式入口在旧grant开启／新grant关闭时精确1个NotExecuted。源级审查发现的预留账本冲突已修正，编译失败输出保留，最终两组Release零警告／错误。冻结提交、候选清单和摘要在最终精确授权消息中提供，详情见[候选准备记录](../verification/2026-10-11-account-sync-b1c1-preparation.md)。B1a2正常主流程、B1b1正式协调器异常及[完整桌面无数据库门禁](../verification/2026-10-11-account-sync-desktop-handoff-passed.md)沿用原证据，不重跑其已消耗额度。

用户随后要求先吸收最新main再继续原目标；本分支已合入`main@a4acdeb`，保留账号质量原则与技术验收责任，并采用最新子Agent模型使用规则。配饰预览所需native在本轮隔离重建，固定SHA256为`B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E`；生产DLL保持不变。合入回归及中文输入法按键定位见[独立记录](../verification/2026-10-11-backend-latest-main-absorption.md)。旧B1b1及完整桌面证据不被改写，也不作为新native或合入后全部行为通过的替代。

## 新增门禁

唯一固定用例 `Dango.AccountSyncReview.Tests.AccountSyncB1cJourneyTests.FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccount`，同一正式Host内依次启动两份完整Desktop AppHost，使用两份隔离本机文件。两个虚构账号各注册一次，A初始米色103、B初始蓝色103；使用四个正式登录会话。

1. A首次登录先通过独立UIA保存昵称“旧账号团子”，建立相同配置的同步基线。A真实timer GET已获得正式HTTPS返回的米色103／版本1，夹具仅保留该实际响应。通过独立UIA本机注销、取消未发出的排队注销、清理记住登录材料并离线保存118。B登录并首次设置昵称“新账号团子”，在A原pump未结束时保持配置未就绪；释放A实际响应后才读取B的实际比较基线。明确选择B蓝色103，真实Apply保存125，唯一配置提交使B版本到2。两次Profile提交是A／B各一次，第二段不再改昵称。
2. 新AppHost以A米色103／版本1登录，真实Apply保存111，正式PATCH已成功提交为版本2后保留实际ACK。同样注销、离线保存118、登录B；释放A响应后选择B现存蓝色125／版本2，再真实保存130，使B版本到3。A原档保留，晚到ACK不能形成已确认回执、变成B基线或重放给B。

两例使用实际native健康物理帧／Rendering、真实HWND、独立线程UIA、生产SavedToDisk和DispatcherTimer、真实文件／journal；不反射改变业务状态、不手动Pump。检查完整七字段、原始字节、User／Pet／Session、版本、请求顺序、来源未选／已选状态、首页昵称与停止A主transport后零新请求。主transport以实例ID及retired事件标识，独立清理transport不能混入该计数或被当成主请求复活。

保留响应必须来自正式HTTPS，不合成成功体或伪造数据库提交。每例仅一次armed，绑定本次A会话的精确GET或PATCH；获得实际成功且完整响应后才进入held，PATCH核对米色111／版本2的实际ACK。只对该请求分离caller取消，登记caller取消与fixture lifetime终止；其他请求继续尊重caller取消。hold仍受fixture lifetime、响应硬期限和总旅程期限约束；异常结束先停止controller并释放hold，再分别排空自有窗口、账号操作、pump、UIA、HTTP和Host任务。未暴露的内部异步Dispose任务仍如实记录观察限制。

A两次UIA注销都排在保留请求之后并被取消，不能算成服务器撤销。每例注销前仅在内存独立复制本次A Cookie到新的CookieContainer，不共享引用、不记录或落盘；本例B保存已收敛且A原pump结束后，通过独立受控FormalClient公开ReadIdentityAsync恰好核对一次User／Pet，再真实Logout恰好一次，不以controller内部身份代替查询。B最终通过UIA正式注销。服务器四个实际会话必须逐一撤销。两次未发送的UIA注销意图独立持久记账、硬上限2，不退款，也不冒充已接收业务操作。

## 精确资源草案

仅使用既有 `C:\Users\fanh\AppData\Local\Dango\BackendDevelopment\run20261008b0\pgdata`、`127.0.0.1:63237`、库 `dango_backend_b0`、角色 `dango_b0_owner`。零迁移，保持原七项历史。新证据目录拟为同级 `run20261011accountsyncb1c1`，正式执行前必须不存在，且采用原受保护ACL；原state、DPAPI凭据、恢复代际及全部新旧服务密钥保留。

| 正式业务尝试 | 上限 |
| --- | ---: |
| Invite／Start／Confirm／Complete | 各2 |
| Login／Profile／Configuration／Logout | 4／2／3／4 |
| 合计 | 21 |

注册setup走正式服务／HTTP，不重复桌面注册流程。最多两个虚构`.invalid`邮箱注册图、两只账号团子、两个默认桌宠房和四会话。A最终昵称版本2、米色111配置版本2；B最终昵称版本2、蓝色130配置版本3。预计5份配置original、4份receipt，A迟到ACK的original无receipt。原22表旧行完整摘要多重集保留；九类注册图各新增2、sessions新增4，其他12表不增加，四张配饰表保持不变。固定双账号scoped必须绑定精确角色、Owner、恢复代际、A／B User／Pet／默认Room、昵称及profileVersion2、配置版本2／3和整份七字段JSONB；四个非空不同SessionId须逐一绑定所属User／Pet／恢复代际和非空revoked_at，每账号恰好两个，不以集合数量代替逐行绑定。

21是正式RequestPolicy到达前reservation的不可退款业务预算；未到达该边界的两次排队注销由上述独立意图账本核对。最多128次HTTP覆盖所有主／注册／清理client，开始间隔至少600ms；业务10分钟、唯一vstest进程300秒。一个本机HTTPS端口、一个FormalHost、一张短期证书、两个先后Desktop AppHost；不导入系统信任、不操作WinCred、不发邮件或启用worker。transport对象与会话分开计数，预计每Desktop至多4个transport（包括注销与本机清理产生的无身份实例），另两个注册及两个A清理client；对象上限不得扩展业务／HTTP预算。

固定十工具序列：version、status、start、before-size、before-full、vstest、after-size、after-full、双账号scoped、stop。完整快照先做大小预检和1MiB硬限；保留有界双流EOF、工具身份／进程／句柄、PG／桥进程／pidfile／端口及证书CNG私钥收尾。结束仅停止本次资源并清理本次临时CNG私钥，保留全部新旧数据和证据，失败不补跑、不补SQL、不重置、删除或还原，也不连接其他实例。

## 实现与离线检查

新增独立B1c1 owner、预算、双账号HTTPS fixture、迟到响应控制、完整Desktop rig、唯一旅程和证据合同；复用已审查的纯RequestPolicy／ArchiveContract／AppearanceNavigator及生命周期能力。PowerShell新增独立B1c1计划／执行／双账号SQL／合同，成熟共享process helper只新增精确stage和输出合同，旧合同及授权不得扩大。

已验证未批准入口在任何资源读取／初始化前拒绝、历史批准不能授权本阶段、两账号与四会话证据归属反例、预算不退款、迟到响应控制、原任务排空、双账号SQL列／查询结构及旧PowerShell合同回归。注销意图只追加到runner预留的既有空账本，拒绝文件缺失、非空首写、畸形旧记录和重复写入，不重建、覆盖或截断；新增8个真实普通临时文件反例。使用新隔离构建输出，不覆盖B1b1或当前桌面通过候选；离线源级与保存证据独立审查后本地提交并冻结。真实执行只在最终冻结计划获得用户明确批准后进行。

观察边界：生产controller内部`DisposeAfterPumpAsync`未公开Task，证据明确`InternalDisposeTaskObservable=false`；不把公开pump、HTTP和自有生命周期任务排空扩大表述为直接await该内部Task。真实双账号完整桌面、正式服务及PostgreSQL结果仍待上述一次新授权，当前不能写成通过。

正确性检查两条完整流程和边界输入；一致性检查真实提交／晚到回包与零重放；安全性检查每次正式会话及归属、独立grant／进程owner和固定目标；可靠性检查原档、旧行保全及自身资源收尾；扩展性保持串行pump、限频和任务／请求／输出硬限。不以本轮结论推断容量、灾备或多人可玩。门廊正式CRUD与主观验收为后续独立门禁；未授权main合并、推送、部署、安装或生产DLL替换。
