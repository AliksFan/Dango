# B1c2 完整桌面跨账号生命周期真实验证

日期：2026-10-11。结论：用户批准后的唯一一次冻结执行通过，正式TRX及外层runner均通过；本阶段技术门禁完成。B1c1的原失败结果保持不变。未重跑、追加数据库查询或补写旧证据，未合入main、推送、部署、更新安装或替换生产DLL。完整B5及主观验收尚未通过。

## 冻结范围与执行

执行提交为`44a4ca1c15df3f69e4e9d96bb729700dc79deb4f`，已包含`main@a4acdebd8567be3761271f5b03cd45c06d2a4e6d`及最新子Agent规则。固定清单为`artifacts/account-sync-b1c2-approved.json`，SHA256为`8AF08481865049615ECAE220314AD5DB18C9B9177F6173882E25751E4D9AB0DC`。547项输入、3个PostgreSQL工具和正式Review bundle的209项摘要在执行及保存证据复核时一致，缺失输入0。冻结准备、离线门禁和中间失败保留见[准备记录](2026-10-11-account-sync-b1c2-preparation.md)。

仅使用已批准的既有PG18实例、`127.0.0.1:63237`、库`dango_backend_b0`及角色`dango_b0_owner`。零迁移，七项原历史保持一致。一个正式本机HTTPS Host、一张短期证书、两个顺序完整Desktop AppHost；12个client、8个桌面transport，native为`B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E`。没有导入系统信任、操作WinCred、发送邮件或启用worker。

唯一正式FQN为`Dango.AccountSyncReview.Tests.AccountSyncB1c2JourneyTests.FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccountWithLifecycleEvidence`。TRX精确1个Passed，结果／定义／entry的testId与executionId一致，CodeBase指向冻结Review DLL。外层返回`accountsync_run_one_journey_passed_data_retained`，回执`Failure=null`、`TestsPassed=true`，配置原档、故障生命周期、精确范围、注销意图及最终scoped门禁均通过。

## 真实结果

| 门禁 | 本轮证据 |
| --- | --- |
| 业务预算 | Invite／Start／Confirm／Complete各2，Login4、Profile2、Configuration3、Logout4，连续21次不可退款reservation |
| HTTP | 66次登记／尝试；64次实际Send调用，64个原Task为RanToCompletion、2个为Canceled，零Faulted |
| 限频 | 逐次真实Send前／后单调时标重算，保守间隔下界最小600.6748ms；仅证明handler调用间隔，不声称socket发包时刻 |
| 期限 | 正式旅程57.7343739秒，低于业务10分钟及唯一测试进程300秒上限 |
| 跨账号 | late-get、late-patch均Passed；B比较基线前旧pump已结束；两个A retired主client停止后请求数均为0 |
| 配置原档 | 5份original、4份receipt；A已提交的迟到PATCH原档保留且无receipt，未污染B或重放给B |
| 最终状态 | A昵称版本2、米色111配置版本2；B昵称版本2、蓝色130配置版本3；各2个会话均撤销 |
| 生命周期 | Version2，460条事件；原Task、observer及已公开任务排空，两个精确注销窗口通过 |

本轮未捕获响应的请求为Sequence36／Client6和Sequence64／Client11，均为B桌面非关键GET `/api/v1/me/pet`，分别对应late-get和late-patch最终正常注销窗口。两条实际Send均未开始，原Task终态为Canceled，原操作令牌、真实coordinator、同会话Logout、正常Stopped状态及窗口绑定共同证明注销取消；没有fixture、正文期限或未知取消来源竞争。原始ResponseStatus／ResponseVersion仍为null，不计入成功HTTP。其余64条响应完整成功；关键held响应、基线、身份读取、PATCH、注册和Logout仍严格要求成功。

B1c1的Sequence64缺少逐请求终态与取消来源，仍保留为原失败；本轮新证据不能追认旧请求。

## 数据与最终关系

before／after完整快照均保留原文。根使用保存快照重新比较22表旧摘要多重集合，旧行及每个重复摘要的次数全部保留。invitations、registrations、challenges、emailJobs、users、pets、rooms、memberships、petStates各新增2行，sessions新增4行，另外12表不变；四张配饰表不变。新增UserId恰好为本轮两个虚构账号，数据库目标、角色、版本、数据目录、监听范围及迁移历史前后一致。

本轮最终双账号scoped SQL已执行并通过，不能只用actor读数代替：两个账号的完整注册图、恢复代际、昵称与版本、账号PetId、默认桌宠房、membership／pet state、完整配置及版本、未发送邮件任务均匹配；四个不同会话逐行绑定所属User／Pet／恢复代际并已撤销。每账号users／validUsers／namedUsers／completeGraphs／pendingUnsentEmails为1，sessions／validSessions／revokedSessions／exactSessions为2。

## 资源收尾与保留

固定十工具全部按唯一顺序执行：version、status、start、before-size、before-full、vstest、after-size、after-full、scoped、stop。status退出3表示执行前实例停止，其余退出0。所有工具身份、进程退出／Dispose、reader结束及输出资源收尾确认；九工具双流EOF，start按PostgresStartIgnored合同丢弃输出，不能声称十工具均捕获EOF。

八条资源事件顺序完整：TLSCreated、HostStarted、两对DesktopStarted／DesktopDisposed、HostDisposed、TLSDisposed。两个桌面的HWND、UIA、pump、HTTP、timer、runtime、首页driver、coordinator、账号操作及内存secrets收尾通过；Formal Host已停止／Dispose，InFlightRequests为0，生命周期任务结束；本次临时CNG证书私钥清理确认。

本次PostgreSQL、桥进程、工具进程及输出资源清理通过。根复核时postmaster.pid不存在，PG记录端口与本次HTTPS端口均无listener。全部新旧数据、原凭据、服务密钥、恢复代际及证据保留；没有重置、删除持久数据或还原。保存证据复核没有读取原state／keys、重新启动资源或追加SQL。

## 证据与边界

原始证据目录：`C:\Users\fanh\AppData\Local\Dango\BackendDevelopment\run20261011accountsyncb1c2`。正式TRX SHA256为`CF429077B7C30031AD38F3062D903D9484C2C1D6C9B6E94371FABD6E1C579DE9`。根保存证据复核为`artifacts/account-sync-b1c2-candidate/root-saved-evidence-review.json`，SHA256为`341103F55913DCB7D840156A5D6AECC8BB31DC9E93C3ED1F41095A5A0BA43DC7`；它包含逐表旧行保全、十工具收尾、两个取消请求、限频重算及最终scoped关系。冻结摘要和全部离线失败保留，不覆盖旧阶段工件。

正确性验证正常与取消终态；一致性验证迟到响应、配置版本、原档及会话逐行关系；安全性验证精确身份／session／coordinator／窗口和固定资源；可靠性验证旧行保全、任务排空与本次资源收尾；扩展性验证21业务、128 HTTP、600ms及输出／事件／期限硬限。以上不等于容量或备份还原演练通过。

生产内部DisposeAfterPumpAsync未公开Task的观察边界仍保留。下一技术门禁为门廊场景内创建、修改、解散、恢复、成员／公共码与准入／离房等现有能力的正式服务端闭环；原门廊fake服务实窗检查及B772原生按键通过，不能替代该闭环。主观手感、界面效果与流畅度由用户随后验收。本轮一次真实资源授权已执行完毕，不扩大后续资源或部署权限。
