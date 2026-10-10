# B1a2：正式客户端登录与捏脸同步正常旅程

状态：已获用户批准并执行一次；唯一正式旅程1 Passed，但最终 scoped SQL 因中文参数编码失败，整体运行 Failed，额度已消耗。不能重跑或补查，原候选、清单和失败证据保留。见[实际结果](../verification/2026-10-10-account-sync-b1a2-scoped-query-failure.md)。下文为已执行计划的历史精确范围，其原批准 SHA256 以保存清单为准；当前文档不再用作执行授权。原 B1a 与 A5 额度也均耗尽。旧联合草案其余场景仍未授权。

## 1. 本轮交付与目标

从真实 `DangoApplicationHost.Start()`、Windows HWND 与 UI Automation 完成邀请注册、首次昵称、每次登录比较及连续捏脸同步。使用正式服务端、HTTPS、PostgreSQL、真实捏脸文件和正式两秒同步计时器；第二客户端使用独立 Cookie 和内存秘密存储。

本轮验证正常主流程。两连接同时 CAS、提交后丢响应、服务重启、跨账号旧队列隔离列入后续 B1b；门廊真实房间管理另设计划。配饰随后按已确认规则接入。通过 B1a2 不表示这些场景已通过，也不表示可直接部署生产。

原 B1a 失败事实见[导航失败与收尾](../verification/2026-10-10-account-sync-b1a-navigation-failure.md)。本轮业务旅程与最大额度保持不变，修复的是测试导航前置状态与异步页面等待。换装最初如何打开仍未知；不改正式产品页面行为或直接切换业务状态来使测试通过。

## 2. 精确环境

- 工作树：`C:\Users\fanh\.codex\worktrees\backend-b5-windows\Dango`，分支 `codex/backend-b5-windows`。
- 原独立 PostgreSQL 18 数据目录：`C:\Users\fanh\AppData\Local\Dango\BackendDevelopment\run20261008b0\pgdata`。
- 唯一数据库 `dango_backend_b0`；角色 `dango_b0_owner`；监听 `127.0.0.1:63237`。若已有监听、pidfile、目标或归属不匹配，拒绝继续。
- 新证据目录：`C:\Users\fanh\AppData\Local\Dango\BackendDevelopment\run20261010accountsyncb1a2`。原子 CreateNew 预留一次，SID 专属 ACL；任何失败都消耗本轮，既有目录不得复用、清空或重置。
- 沿用原 `credential.dpapi`、`b1test-state.json` 与 `b1tests-keys`。只在预留之后解密原数据库凭据，在子进程内使用，不输出密码、连接串、Cookie、邮箱确认 proof 或私钥。
- 正式 `FormalHost` 会重申原 `b1tests-keys` 的当前 SID ACL；DataProtection 可在该原目录正常生成或轮换密钥。结束保留原目录与所有密钥，不宣称密钥目录只读，不删除或恢复旧密钥。
- 固定候选：`artifacts/account-sync-b1a2-candidate/bin/Dango.AccountSyncReview.Tests/release`。唯一原生模块为 `artifacts/local-room/account-profile-sync-20261010/bin/Dango.Desktop/release/dango_physics_module.dll`，SHA256 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`。
- 运行前固定清单与独立 SHA256 必须一致；核验源码、运行器、模块、候选依赖及原生模块。运行期间再次核验，批准后不 build、restore 或改候选。
- 固定 stage 为 `AccountSyncB1a2`，批准入口只接受 `DANGO_BACKEND_ACCOUNTSYNC_B1A2_APPROVED`，旧 B1a／Profile／其他阶段批准无效。固定清单为 `artifacts/account-sync-b1a2-approved.json`，唯一 TRX 为 `account-sync-b1a2.trx`。
- 新 before 快照保护执行前数据库中全部现存行，包括失败 B1a 已留下的注册图与会话；不清理或修改旧 B1a 数据以方便测试。最终比对仍保护 22 表完整旧行多重集。

数据库历史必须恰好为以下七项，pending 为零；本轮迁移操作为零：

1. `20261008074701_InitialAccountRoomFoundation`
2. `20261008085521_AccountLifecycle`
3. `20261009031410_RoomManagement`
4. `20261009055558_RoomAccess`
5. `20261009062701_RoomAccessIntegrity`
6. `20261009124814_AccessoryAccountBackup`
7. `20261010055529_AccountProfile`

## 3. 一次性额度

仅新增一个 `accountsync-{OwnerId:N}@example.invalid` 虚构账号及其邀请、注册、挑战、未发邮件、团子、默认桌宠房、成员与团子房间状态，各一条；四个会话。注册创建默认房属于注册图，不执行房间管理、进入、控制或公共码业务。

| 操作 | 最大尝试数 |
| --- | ---: |
| Invite | 1 |
| Start | 1 |
| Confirm | 1 |
| Complete | 1 |
| Login | 4 |
| Profile PATCH | 2 |
| Configuration PATCH | 5 |
| Logout | 4 |
| 总计 | 19 |

每次写入先短锁扣额度，再追加并 flush 持久账本；业务不在该锁内执行。失败、取消、未知响应不退款；写账本失败、父进程丢失、耗尽额度或时间超限永久关闭业务入口。

HTTP 总数最多 512，所有请求先计数；最小间隔 600 ms，遵守正式服务端 120/min 限流。业务预算最多十分钟，唯一 vstest 子进程另有更短的 300 秒上限。超限失败且不补跑。每次成功响应归属捕获独立限时 20 秒、16 KiB；调用方取消不能丢掉已成功提交的账号或会话归属。

最多十个工具进程，固定顺序：PostgreSQL version、status、start、before-size、before-full、一次 vstest、after-size、after-full、一次 scoped SQL、stop。中途失败只能使用剩余额度停止本轮已证明归属的实例，不能补测试、补 SQL 或换数据库。C# owner 的目标／迁移只读检查、仅本轮挑战确认读取和本轮最终注册图读取，以及正式 HTTP 轮询，在固定唯一旅程内执行。

仅启动一个原 PostgreSQL 实例、一个仅绑定本机临时端口的 HTTPS Host、三个先后完整 AppHost 生命周期；不同时运行两个可见完整客户端。独立第二客户端只使用正式 HTTP 客户端。

仅生成一张临时 RSA 2048、SAN=127.0.0.1、server-auth、非 CA 证书。私有 PFX 仅在内存且及时清零；客户端只钉扎该公钥，不导入系统信任、不调用 WinCred。Schannel 临时用户私钥创建和 Dispose 后通过实际 `CngKey.Exists` 核验。若当前运行时未提供预期的 CNG 用户命名 key，在 Invite 前拒绝，不换证书方案补跑。

不启动邮件、房间过期、房间 live 或 outbox worker；不连接 SMTP、不发真实或测试邮件。

## 4. 唯一正常旅程

1. 本机真实捏脸保存 eyeSize=103。UI 邀请注册并确认，首次登录。空白昵称由 UI 拒绝且零 Profile 写入；设置“同步团子”，首页按钮显示昵称。首次本机与云端相同，无选择、零配置 PATCH。
2. 独立客户端登录，同账号 PetId 保持。UI 保存 eyeSize=111，自动上传至 configuration_version=2，独立客户端和真实文件一致。
3. UI 预览草稿 118，不保存；第二客户端成功提交 116，版本=3。草稿保留，落盘仍111，草稿零回传。UI 取消后自动应用116。
4. 完整关闭第一个 AppHost。第二客户端提交123，版本=4；第二个 AppHost 记住登录恢复，出现本机116／云端123的选择。选择云端，零 PATCH，真实文件变123。
5. UI 注销；离线 UI 保存132。显式重新登录，选择本机，成功提交版本=5。再次注销并离线保存138，再登录选择云端132，零 PATCH。
6. 第二客户端在线成功提交128，版本=6。首页关闭账号浮层期间自动跟随128，无来源弹窗、零回传。UI 修改昵称为“新同步团子”。
7. 完整关闭第二个 AppHost，第三个 AppHost 记住恢复相同配置；不弹选择，零 PATCH，首页昵称恢复。UI 注销，独立客户端注销，四个会话均撤销。

每个 AppHost 启动、关键截图与退出前都检查原生场景 Ready、实际成功物理帧持续推进、首页持有唯一帧驱动、无物理或配饰故障。截图只渲染本轮窗口，不读取其他应用或桌面内容。

每次捏脸操作先检查账号浮层、设置页、门廊及团子离开等阻挡状态。当前若是换装，使用真实 UIA 收起按钮并等待首页入口可用；若已是捏脸则复用。Face、Eyes、Range、Apply、Cancel 与 Close 分步执行，各自等待实际页面／草稿／已保存状态；UIA 同时要求控件已启用且不在屏幕外。只写入本轮 owned 窗口的非秘密页面状态证据，每个 AppHost 最多 128 份 `navigation-{index}-{sequence}.json`，无键盘输入、密码、Cookie、确认 proof 或任务外窗口内容。原生场景和最终同步断言不变。

## 5. 能失败的通过条件

- 唯一正式旅程 TRX：1 Passed、0 Failed、0 NotExecuted；不是仅构建成功。
- 19 条尝试账本逐项身份、序号、操作及累计 Used 准确；根记录和 Facts 字段集合、大小写、数字与布尔类型严格校验。
- 10 条资源账本顺序恰好为 TLSCreated、HostStarted、三个 DesktopStarted／DesktopDisposed 对、HostDisposed、TLSDisposed。确认实际 HWND 销毁、配置 Pump 和唯一 UIA worker 排空、请求 inFlight=0 和 native key 消失。每个 DesktopDisposed 必须含严格布尔 `UiaWorkerSettled=true`，缺失、假 true 字符串或 false 均拒绝。
- 恰好三份登录选择 archive 与三份连续同步 archive；逐份核对真实原始本机字节、规范配置、云端内容与版本、身份、Choice、Outcome、ObservedCloudVersion、无失败代码及时间关系。不以“文件可读”代替正确性。
- 新账号最终昵称“新同步团子”、profile_version=3；配置128、configuration_version=6；账号 PetId 始终不变；默认注册图完整、四个会话均撤销、唯一邮件 Pending 且 attempts=0、恢复代际一致。
- 前后完整 JSON 快照先保存再比较；字符和 UTF8 双上限 1 MiB、完整 EOF。22 张旧业务表所有原始行 SHA256 多重集完整保全，包括旧用户昵称与资料版本、四张配饰表全字段。只允许本轮一个注册图及四会话增量，其余表零增量。
- 数据库、CMD 桥接进程、十个工具、pidfile、端口和输出资源分别证明已收尾。任一条件不满足都失败，保留部分证据，不退款、不补查、不重跑。

## 6. 五项设计约束与证据边界

| 维度 | 本轮证据 | 后续仍需验证 |
| --- | --- | --- |
| 正确性 | 首次昵称校验、相同／差异登录、两种来源选择、真实字节和版本 | 更广异常输入已有静态测试；真实异常旅程留 B1b |
| 一致性 | UI 草稿保留、保存上传、顺序远端提交后自动跟随、身份不变 | 同时 CAS、提交后丢响应、旧跨账号队列 |
| 安全性 | 精确目标／新账号绑定、请求 allowlist、内存 capability、独立 Cookie、凭据不出日志 | 跨账号正式授权真实负例留后续 |
| 可靠性 | AppHost 关闭／重开、真实文件／archive、失败关闭预算、资源归属与收尾 | 服务重启／进程崩溃、生产备份还原另行计划 |
| 扩展性 | 两秒轮询、512 HTTP／十分钟／限频、有界响应与快照 | 实际多用户负载、公开部署容量 |

## 7. 结束与授权边界

正常结束由助手自动完成窗口及宿主 Dispose。旅程任一异常首先关闭业务预算，阻止收尾期间排队请求继续发送。即使健康检查失败，仍分别尝试关闭宿主、排空 Pump、确认 HWND、排空唯一 UIA worker 与记录真实收尾结果。UIA worker 超时后保留归属，拒绝启动第二个 worker，关闭 HWND 后有界等待实际结束并观察任务异常；未结束不能报告收尾成功。不把超时杀死测试树冒充 fixture 已 Dispose。

只停止本次证明 PID、创建时间、原生路径及目标完全归属的 PostgreSQL 与 CMD 桥；保留全部数据库数据、证据、密钥、代际文件和本轮应用文件。归属不明时报告残留并求助，不按进程名盲目结束。

执行本轮资源仍须独立精确批准，来自 `AGENTS.md` 第4节“数据库、对象存储、端口、进程和云资源操作必须精确到环境和目标”。未授权 main 合并、push、部署、日常安装、生产 DLL 替换、持久资源删除、数据库重置或备份还原。B1a2 通过后继续 B1b 与门廊技术验证，用户主要验收界面效果、手感和流畅度。
