# B1b1：真实 HTTPS 双客户端同步异常与宿主恢复

状态：候选实现与离线检查完成；本轮真实验证未批准、未执行，最终冻结清单及SHA256单独提供。B1a2及只读closure均已执行一次，旧额度不能用于此计划。本轮只验证正式同步协调器、正式文件适配器和真实 HTTPS／PostgreSQL；完整 Desktop 定时器、换号迟到任务、门廊与主观体验仍分别保留验证门禁。离线证据见[候选记录](../verification/2026-10-11-account-sync-b1b1-candidate.md)。

## 精确目标与冻结

- 工作树 `C:\Users\fanh\.codex\worktrees\backend-b5-windows\Dango`，分支 `codex/backend-b5-windows`。
- 唯一既有独立实例：`C:\Users\fanh\AppData\Local\Dango\BackendDevelopment\run20261008b0\pgdata`；PostgreSQL18，`127.0.0.1:63237`，数据库 `dango_backend_b0`、角色 `dango_b0_owner`。
- 新stage `AccountSyncB1b1`；唯一新证据目录 `C:\Users\fanh\AppData\Local\Dango\BackendDevelopment\run20261011accountsyncb1b1`。原子CreateNew预留并设当前SID专属ACL，已存在即拒绝，不删除、清空或复用。
- 独立入口 `eng/invoke-account-sync-b1b1-tests.ps1`，审批清单 `artifacts/account-sync-b1b1-approved.json` 与独立SHA256。冻结本计划、运行器／工具／测试源码、正式模型／迁移／身份／传输／同步／文件与archive输入、候选全部运行依赖及三份实际PostgreSQL工具。批准后不修改、构建或restore。
- 固定候选 `artifacts/account-sync-b1b1-candidate/bin/Dango.AccountSyncReview.Tests/release`；仅执行 `Dango.AccountSyncReview.Tests.AccountSyncB1bJourneyTests.RealHttpsCoordinatorsHandleConcurrencyLostResponsesAndHostRecovery` 一次。
- 历史必须恰好为原七项，pending为零：InitialAccountRoomFoundation、AccountLifecycle、RoomManagement、RoomAccess、RoomAccessIntegrity、AccessoryAccountBackup、AccountProfile。正式完整migration ID由固定plan／owner校验。迁移操作为零。

只有清单验证和原子预留完成后才读取原state、DPAPI凭据及恢复代际。数据库密码只在批准子进程内使用，不输出、提交或持久复制。沿用原 `b1tests-keys`：FormalHost会重申SID ACL，DataProtection可能正常生成或轮换密钥；结束保留全部新旧密钥，不能称keys只读。原数据、恢复代际、所有历史证据均保留。

## 一次性资源与业务额度

仅新增一个 `accountsyncb1b-{OwnerId:N}@example.invalid` 虚构账号及完整注册图、团子、默认桌宠房和两个会话。默认房属于注册事务；不执行房间管理／进入／控制或配饰业务。

| 操作 | 最大尝试数 |
| --- | ---: |
| Invite／Start／Confirm／Complete | 各1 |
| Login | 2 |
| Profile PATCH | 1 |
| Configuration PATCH | 8 |
| Logout | 2 |
| 总计 | 17 |

每次业务操作先不可退款扣额度、追加并flush持久账本，再执行；包括本轮故障截住而未转发的PATCH、冲突、取消和丢响应。短锁不包住实际业务。账本失败、父进程失去归属、额度／时间超限或显式Abort永久关闭业务，不能续跑。

所有HTTP先计数，总数最多128，最小间隔600ms；业务期限十分钟，唯一vstest进程最多300秒。成功注册／登录归属捕获最多20秒／16KiB。故障等待、Host停启、文件适配器Dispatcher与协调器任务均有独立界限，不能留下后台业务继续写入。

PostgreSQL只启动一次。HTTPS仅监听一个本机临时端口，顺序创建两个FormalHost；第二Host保持同origin、证书／私钥和RecoveryEpoch。两个客户端各有独立CookieContainer／内存秘密存储，使用正式HTTP客户端、两个正式同步协调器、独立本机配置文件与真实 `PetConfigurationFileJournal`。不启动完整AppHost／可见HWND或UIA；不导入系统信任、不调用WinCred。

仅生成一张RSA2048、SAN=127.0.0.1、server-auth、非CA短期证书；PFX仅内存并清零。客户端固定钉扎该证书，临时CNG用户私钥在Dispose后实际确认不存在。现运行时若不能取得预期CNG命名key，在邀请前拒绝，不换方案补跑。

最多十个工具，固定version、status、start、before-size、before-full、一次vstest、after-size、after-full、一次scoped、stop。中途失败仅可使用剩余额度停止本轮已证明归属的PG／CMD；不补查询、测试、迁移或诊断。不启动邮件、房间过期／live／outbox worker、SMTP接收器或真实发信。

## 固定五病例与能失败的条件

1. **真实重叠CAS**：两设备从同一版本分别成功本机保存；在正式服务 `before_locks` 有界屏障处证明两PATCH同时在途。两尝试只允许一成功、一冲突；只增一版本。两正式协调器最终采用胜出配置，冲突旧意图不能改用新版本重放。
2. **成功提交丢响应**：第一端保存后，正式服务已返回成功，测试运输层销毁响应并模拟丢失。只许一次PATCH，同次或后续Pump用真实GET核对并收敛，archive receipt为 `TargetSatisfied`；不得声称客户端收到ACK。
3. **丢响应后另一端更新**：第一端提交成功但丢响应，再由第二会话按实际最新版本成功提交不同配置。两PATCH、两成功；第一端随后GET采用较新版本，旧意图不能复活、回传或再次改版本。
4. **未提交的未知请求**：已计数／持久预留的第一PATCH被运输层故障拦截，未转发服务端；下一Pump只可按原version／原完整配置重试一次。两尝试、仅一成功；不能换版本重放旧意图。
5. **宿主恢复**：关闭并Dispose第一个Host，第一端依次成功本机保存eyeSize138和139，均处于正式70..140范围；Pump读取失败后保留最新本机意图。相同origin／证书／key／epoch创建第二Host；先GET当前版本，只上传最新完整配置一次。两端与真实文件收敛，最终eyeSize139、configuration_version7，PetId不变。

五病例Configuration尝试数2／1／2／2／1，成功数1／1／2／1／1，最终版本依次2／3／5／6／7。昵称固定“异常同步团子”，profile_version2。结束两个会话均真实注销；唯一邮件Pending、attempts0。

唯一TRX须1 Passed、0 Failed、0 NotExecuted。必须逐项核对真实请求配置／expectedVersion／响应状态及屏障／故障次数，不能只根据最终数值推断病例执行。两设备合计恰好12份archive，逐份核对真实原始字节、规范配置、完整账号身份、云端版本、receipt outcome及时间关系；保存非秘密审计投影，不能以文件存在代替校验。

资源账本六项顺序TLSCreated、HostStarted(Index1)、HostDisposed(Index1)、HostStarted(Index2)、HostDisposed(Index2)、TLSDisposed。fixture初始化开始前由旅程持有；Start／Stop／Dispose保留真实Task并设置硬等待界限，超时不能丢弃任务，重复Dispose等待同一Task。失败时各设备Pump／Dispatcher与Host清理独立尝试。成功要求两Host停止／Dispose、inFlight为零、每份HostDisposed的LifecycleTasksSettled为true、故障／Pump／Dispatcher任务settled、证书CNG key消失；进程退出不能代替fixture收尾。

前后22表完整JSON快照先CreateNew保存原文再验证，完整EOF、字符与UTF8字节各最多1MiB；size预检与实际输出均要通过。旧行完整SHA256多重集保全，包括旧账号昵称和四张配饰表全字段。只允许本轮完整注册图各+1、会话+2，其余表零增量。最终scoped SQL绑定本轮Owner／User／Pet／Room／epoch；所有SQL参数ASCII，昵称使用PostgreSQL Unicode literal，拒绝空白、非ASCII、NUL或越界。stdout／stderr原文均有界保存，不公开秘密。

成功还要求五维实际目标、恰好七迁移、完整本轮图／昵称／版本／未发邮件／两已撤销会话、固定账本及所有资源收尾。PG、CMD桥、工具句柄／进程、双流／reader任务、pidfile与端口分别确认。失败保存可得证据、保留数据，额度消耗，不重跑。

## 五项设计要求与剩余边界

| 维度 | 此轮可验证 | 剩余 |
| --- | --- | --- |
| 正确性 | 完整配置、未知请求／响应、真实原始字节与版本 | 更广异常输入已有离线覆盖，完整桌面接线另验 |
| 一致性 | 真实CAS重叠、丢响应后新提交、固定版本重试、无回传 | 换号迟到请求的完整Desktop重演 |
| 安全性 | 独立stage／capability／固定账号会话归属、只读保全旧图 | 更广正式跨账号授权负例及公开入口 |
| 可靠性 | Host停止／重建、文件archive、任务和资源收尾 | OS强杀、PG崩溃、备份还原与生产恢复另需精确批准 |
| 扩展性 | 两客户端、限频／有界预算与输出、短锁允许业务并发 | 多用户负载、公开部署容量 |

本轮Host重建不等于OS进程崩溃或数据库灾备通过。无main合并、push、部署、日常安装、生产DLL替换、数据重置／删除／还原。完成准备、独立审查和冻结后才请求此精确资源批准。
