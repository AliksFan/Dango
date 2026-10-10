# 昵称、每次登录比较、会话同步与门廊的精确本地验证计划

日期：2026-10-10。状态：**设计草案，尚未获得本轮数据及资源操作授权，尚未执行**。

本计划覆盖已确认的昵称、捏脸登录比较、会话同步和门廊房间入口迁移。配饰同步仍是随后阶段。助手完成能够自动执行的真实 PostgreSQL、HTTPS、客户端与 UIA 技术验证；用户验收界面效果、手感和流畅度。

## 1. 只读核查所得事实

- 当前 `FormalTestFixture` 不迁移、不初始化、不清库。它使用正式 `DangoDatabaseOptions.Create`，检查编译程序集的全部迁移已经应用，再创建夹具。服务默认不启动 Email、Expiry、Live、Outbox workers。
- `AccountProfilePostgresTests` 当前有 7 个实际用例，其中事务失败方法有两项 DataRow。精确过滤并执行一次会创建 9 个虚构账号及各自注册图、执行 10 次登录、19 次昵称修改调用和 2 次会话撤销；不得顺带运行其他 PostgreSQL 类或自动重跑。
- 当前 Profile 类还有本轮独立批准开关，但其夹具仍使用 B1 目标入口。仅设置旧环境变量不能替代本计划的目标、次数和资源核验。
- 旧 R2 `ManualReviewBudget` 仅允许 1 个账号、3 次登录、2 次配置写入，没有 profile 路由。它将整个变更执行包在串行 gate 中；配置以 expectedVersion 作为 replay key，相同摘要重复不再计数。此实现不能用于本轮的真正 CAS 并发和逐次尝试计数。
- 旧 R2 资源状态和收尾由父任务统一核对。本计划要求它完整退出后才能启动本轮；不继承它的 PID、资源所有权、剩余额度或授权。

## 2. 精确环境、迁移与保留范围

本轮仅使用当前用户 LocalApplicationData 下 `Dango/BackendDevelopment/run20261008b0/pgdata` 的既有独立 PostgreSQL 18；数据库 `dango_backend_b0`、角色 `dango_b0_owner`，端口从原 `state.json` 读取并与实例实际值核对。仅监听 `127.0.0.1`，原 `credential.dpapi`、`b1tests-keys`、`b1test-state.json` 恢复代际沿用。

新建且只使用受当前用户 ACL 保护的 `Dango/BackendDevelopment/run20261010accountsync`。遇到目录已存在、reparse、已有 postmaster、目标端口被占用或候选验收模式正在运行，先拒绝；不能换目录、换库或改端口绕过。

迁移前用正式 options factory 查询 `dango.__ef_migrations_history`。允许的既有历史为以下五项，允许的 pending 集合只能是最后一项：

1. `20261008074701_InitialAccountRoomFoundation`
2. `20261008085521_AccountLifecycle`
3. `20261009031410_RoomManagement`
4. `20261009055558_RoomAccess`
5. `20261009062701_RoomAccessIntegrity`
6. **本轮唯一前向目标：`20261010055529_AccountProfile`**

使用 `IMigrator.MigrateAsync` 的精确目标，不调用无目标的全量迁移。历史或 pending 与预期不同即停止并报告，不退迁移、不补迁移。迁移仅增 nickname、profile_version 及约束；旧账号 nickname 为 null、profile_version 为 1，原 ID、账号安全字段、Pet、房间及关系保留。迁移前后记录非秘密结构证据；不得输出密码、security stamp、令牌或连接串。

本轮 PostgreSQL 最多启动一次，结束只停止此轮证明归属的实例。没有新建库、初始化、重置、删除、备份还原或系统 PostgreSQL 服务操作。

## 3. 数据和尝试次数预算

所有数值都是上限。未执行不消耗；进入调用或受控请求转发边界后，失败、取消、超时、未知结果和相同内容重复都消耗一次，不能退款。注册的同一请求重放不能创建第二份身份；重放仍另扣尝试数。数据库已存在的旧记录不得用于扩大本轮图。

| 项目 | Profile 夹具阶段 | UIA 与双客户端阶段 | 本轮合计上限 |
| --- | ---: | ---: | ---: |
| 邀请、预注册、挑战、未投递邮件，各类新增记录 | 各 9 | 各 2 | 各 11 |
| 注册 Start / Confirm / Complete 尝试，各操作 | 各 9 | 各 2 | 各 11 |
| 账号、Pet、默认桌宠房、房主席位、PetRoomState，各类新增图 | 各 9 | 各 2 | 各 11 |
| 登录尝试 | 10 | 8 | 18 |
| 新会话记录 | 10 | 最多 7 | 17 |
| 昵称 Update / PATCH 尝试 | 19 | 3 | 22 |
| 配置 PATCH 尝试 | 0 | 13 | 13 |
| 共同房 Create 尝试 / 新增共同房 | 0 | 2 | 2 |
| 共同房 Rename / Dissolve / Restore 尝试，各操作 | 0 | 各 2 | 各 2 |
| Enter 尝试 / 新增历史租约 | 0 | 4 | 4 |
| 公共码 Reset 尝试 / 新增凭据记录 | 0 | 2 | 2 |
| TakeControl / Leave / EndLease 尝试 | 0 | 2 / 2 / 4 | 2 / 2 / 4 |
| 会话 Logout / Revoke 尝试 | 2 | 最多 7 | 9 |

Profile 阶段的 19 次修改包括独立连接竞争 2、重名及连续修改 4、事务失败 2、跨账号及撤销重查 2、提交前到期 1，以及 HTTPS 缺 CSRF／外来 Origin／伪造字段／3 项坏 JSON／非法昵称／合法昵称共 8。合法成功、业务拒绝、HTTP 边界拒绝分开记录，不能把回滚误记为成功提交。

UIA 的 8 次登录为账号 A 首次、A 独立第二客户端、A 相同配置显式重登、A 差异采用本机、A 差异采用云端、B 一次明确错误密码、B 正确登录、最后切回 A。错误密码不创建会话；记住登录恢复只恢复已捕获会话，不新建登录次数或新会话。

UIA 的 3 次 profile PATCH 为 A 首次起昵称、B 使用与 A 相同昵称、A 改昵称。空白、控制字符等客户端校验必须在本机失败，证明没有发出 PATCH；真实服务端异常输入由 Profile 阶段覆盖。

UIA 的 13 次配置尝试上限逐项冻结：

1. 差异登录采用整份本机配置一次。
2. UI 成功保存后的自动上传一次。
3. UI 关闭期间第二客户端修改一次，供记住登录恢复比较。
4. UI 打开期间第二客户端修改一次，验证主动轮询应用。
5. 草稿打开期间第二客户端修改一次。
6. 草稿明确应用并成功落盘后上传一次。
7. 两个独立连接以同 expectedVersion 竞争，合计两次。
8. 服务端已提交后丢响应的 PATCH 一次；仅当正式客户端确实发出同笔重试时，再允许同摘要、同 expectedVersion 一次。
9. 服务不可用期间保存，恢复后提交一次。
10. 在 fresh session 核验前受控暂停的旧 A 请求，注销后释放并被拒绝一次。
11. 切到 B 后独立成功保存并上传一次。

上述编号 7 与 8 各最多两次，其余各一次，共 13。不为了凑次数伪造 GET、版本或服务端内容；丢响应后 GET 已确认提交就不重发。

HTTP、Hub 和直接 service 调用共用相应业务次数。另设整个运行的 HTTP 请求总上限 2048、最长 60 分钟；只允许捕获会话的 Hub 连接，最多 16 次连接／Attach，Heartbeat 最多 128 次且限频，快照读取计入总请求数。Heartbeat 延长租约属于写入，不能列为无限免费读取。按正式服务的每分钟 120 请求限制安排操作节奏，不关闭或提高正式限流。到达上限即停止该路径，不自动重试、重启清额度或切换目标。

邮件 Attempts 必须为零，EmailJobs 保持 Pending；无 SMTP 接收器、真实邮件或 dispatcher。审计与事件由正式业务追加保留，限定到本轮捕获的房间／请求图，回执核对审计最多 64、事件最多 128、PublishedAt 均为空。上述图最多 13 房间、17 会话、4 历史租约、2 公共凭据；若实际越界即验证失败并停止，不修数据掩盖。

## 4. 新运行器与 guard 的实施要求

在执行授权前可以实现并离线测试新运行器；**不能修改正在运行或旧 R2 的 guard 及回执**。新运行器必须通过下列反例后，才成为可审批候选：

- 独立的新批准入口在读取 credential、连接数据库、探测端口、创建证书或目录之前检查。冻结 run 名、源版本、候选路径与 SHA256、原目标、迁移目标和本文预算，不接受客户端自行提高次数。
- 新 stage fixture 使用正式目标 factory，逐次核对 PostgreSQL 18、实际 data_directory、database、role、listen_addresses 和恢复代际；只允许本轮清单。Profile 类精确过滤一次，不让旧 B1 开关授权其他类。
- 直接 service 调用也必须在调用前预留次数。Profile 专用注册 helper 先捕获生成的 .invalid 邮箱、邀请 ID、request/completion key；每次 Invite、Start、Confirm、Complete、Login、Update、Revoke 预留并记账。不能仅给 HTTPS 加 middleware，而使夹具 service 种子写入无界。
- 分离「尝试计数」与「业务重放摘要」。每次到边界均生成运行器自己的 attempt ID 并扣数；房间 request ID 的摘要仍用于识别同笔业务。配置和昵称 CAS 的 expectedVersion 不是幂等键，同版本不同内容允许进入正式服务的竞争和冲突处理。
- 短 gate 只保护预留、记账和成功 ID 捕获，不包住整个正式执行。指定的两个竞争请求允许同时使用独立 Npgsql 连接，并记录不同 backend PID；不通过移除数据库锁或修改服务事务制造并发。
- 预留先持久落盘并 Flush(true)，然后调用／转发；成功捕获即使客户端断线也用非客户端取消令牌完成。账本、固定 manifest 和非秘密回执在受保护目录；摘要密钥用当前用户 DPAPI 保护。响应丢失、服务重开、运行器恢复均不清次数。账本损坏、回执 IO 失败、目标漂移或归属不明立即关闭业务写入口。
- 正式服务重开复用同 ledger、origin、TLS 材料、keys、epoch 和 captured IDs，禁止重新签发邀请、创建新身份或选择新资源。运行器自身意外退出后不能开一个全新运行绕过已消费额度。
- 安全反例必须到正式边界：Profile 阶段的匿名、跨账号、缺 CSRF、坏 Origin、坏 JSON 应得到正式服务的对应错误。guard 只限定预先冻结的反例清单，不能先返回自身 403／400，再把它冒充服务端授权或输入验证证据。
- 新 UI guard 仅放行本轮两个 .invalid 邮箱、已捕获会话／Pet／房间／租约／instance ID。A 的配置和房间写入不能指向 B；B 仅允许使用本轮公共码 Resolve／Enter A 的公共房。不能把拥有本轮任意账号等同于拥有本轮所有房间。
- HTTP 和 Hub 的 Create、Rename、Dissolve、Restore、Enter、CodeReset、Control、Leave、End 共享同类额度；重复 request ID 仍扣尝试。额外业务路由、全库维护、密码重置／发信、moderation、动作／移动等拒绝。

离线 guard 反例至少覆盖：额度最后一次的并发竞争；相同 payload 重复仍耗尽；同 CAS 版本不同 payload 到达正式执行；丢响应仍 capture；落盘失败不转发；账本重载不退款；旧会话不能读写新身份；HTTP／Hub 不能各领一份额度；中途失败不能变更固定目标。

## 5. 资源及 TLS / 凭据边界

Profile 阶段有 7 个夹具，最多生成 7 份短期、不导入系统信任的测试证书；只 1 个夹具启动真实 HTTPS，绑定临时 loopback 端口并用精确证书 pin，结束逐份 Dispose 临时 native key。其余夹具只执行正常服务与数据库逻辑。

UIA 阶段另生成唯一一份 TLS 证书：SAN 仅 `127.0.0.1`、仅 server-auth、非 CA、最多 3 天。PFX、DPAPI 密码及回执只写本轮受保护目录；当前用户 Root 仅导入此份公钥。Windows 若弹出系统确认，用户核对本轮名称／指纹后操作；助手承担其余技术操作。

选择一个专用两槽均为空的 loopback HTTPS origin，写入固定 manifest。仅允许该 origin SHA256 对应的 WinCred `/session`、`/registration` 两槽；不得枚举、解析或覆写其他凭据。第二客户端使用独立的内存 secret store 与 CookieContainer，不复制 UI 槽，也不导出明文 session。

UI 正式宿主最多启动两次：首次启动，以及一次 Stop／Dispose 后在同 origin 重建。两次共用同预算，不重新 seed。Profile HTTPS 与 UI 宿主分别计数，整个运行最多三个 HTTPS 启动。每个宿主明确禁用 Email、Expiry、Live、Outbox workers，并实查 hosted services 集合。

Windows 候选从已整合提交构建到本工作树 `artifacts/local-room/account-profile-sync-20261010` 下的独立目录。只使用验收设置、已核对的候选 native module 及固定 origin，最多四次候选进程启动；记录 PID、创建时间、可执行路径和 SHA256。生产 DLL、日常安装、用户日常设置均不变。

## 6. 技术验收顺序和能够失败的证据

### A. Profile PostgreSQL / HTTPS

精确过滤 `AccountProfilePostgresTests` 一次，7 个用例全部要求实际 Passed，不接受 Inconclusive／Skipped。独立连接同版本竞争只有一笔成功、另一笔 conflict；相同昵称不同 UserId 成功；资料变更不改 UserId／PetId／账号安全 stamp／默认房图；两处提交前异常真实回滚；跨账号、撤销、到期、CSRF、Origin 和坏 JSON 真正拒绝。TRX、资源回执和本轮数据库 scoped counts 一起核对。

### B. UIA：登录、昵称、捏脸

只操作本轮 candidate PID／创建时间／路径对应的 UIA 树，使用 Value／Invoke 等模式和必要的截图；不采集无关窗口或秘密输入值。

1. 正常界面完成 A 注册、邮箱确认、登录与起昵称；首页账号按钮显示已保存昵称。没有昵称时不开放账号共同房能力。
2. 注册采用本机已保存捏脸，首次相同登录不出现来源选择，配置 PATCH 差额为零；后续相同显式重登和记住恢复也各证明零选择、零 PATCH。
3. 不同显式登录分别选择整份本机和整份云端；云端选择零配置 PATCH。本机、云端原始配置均有 archive，账号 PetId 不变。
4. 关闭 UI、让第二客户端改变云端，再重开记住登录；确实再次比较并出现差异选择，选择云端后继续同步。不能仅检查账号 Cookie 是否恢复。
5. 实际操作捏脸并应用／保存；落盘成功后自动提交，数据库版本增加，独立客户端读取一致。远端后一次成功提交由 UI 轮询应用，没有新登录来源弹窗，也没有回传 PATCH。
6. 草稿不上传、不被远端覆盖；实际选择应用或取消后继续同步。两连接 CAS、提交后丢响应、服务关闭期间保存／同 origin 重开分别核对版本、文件、archive 与状态；服务失败不能显示同步成功。
7. 暂停一个旧 A 写入，在注销后释放；fresh session 拒绝，响应不得发布到 B。B 错误密码失败、正确登录后可用重复昵称和自己的配置；切回 A 时重新比较且不重放 A 旧队列。
8. A 改昵称后首页按钮实际更新；客户端关闭后重开仍读到服务端 nickname 与 profile_version。每项有操作前后技术状态、请求差额及实际数据库读取，截图只作为界面证据。

### C. UIA：门廊全部房间功能

首页和其他入口均不存在“我的房间”按钮或其残留导航；从门廊中的团子互动进入正式房间管理。未登录仍能本地单人，在线入口引导登录；起昵称及捏脸确认完成后按正常权限开放。

账号 A 从门廊依次创建一个私人共同房和一个公共共同房；每个实际 Rename、查看列表／详情、Dissolve、查看已解散列表、Restore 一次。数据库和刷新后的界面都呈现相应名称、状态与版本；刷新和重开能找回同两个房间，不因导航创建重复房间。

四次 Enter 的目标序列冻结为：A 私人房、A 公共房、A 第二客户端观察同公共房、B 通过本轮公共码进入公共房。A 用 UI 操作接管／返回自己的桌宠房／结束本设备租约，并用独立客户端造成一次观察者状态；最多 2 Control、2 Leave、4 End 各有实际结果。顺序须在执行前细化到 request IDs 和租约归属，不为耗尽全部额度追加操作。

公共房最多两次 CodeReset；UI 从门廊读取／复制和确认入房。只在本轮进程内保管临时代码，Resolve 请求限定本轮代码摘要，不读取系统剪贴板。旧码在重置后拒绝、最后一次有效码可供 B 进入；真实计数只允许一个成功访客身份。失败也耗尝试，不能生成第三个代码或第五个 Enter。

B 的正常目录只含自己的默认房。预先冻结一次 B 读取 A 私人共同房的负例探测，guard 仅对这个 GET 放行到正式授权边界，要求实际服务拒绝且不泄露详情；不放行跨账号业务写入，也不把 guard 自身拒绝冒充正式权限证据。

此验证检查正式管理、租约、数据归属和迁移后的可操作入口；多人动作／位置画面尚未接入，仍按 B5 当前阶段记录，不把单客户端目录或状态面板作为完整多人画面验收。

## 7. 结束、失败与报告

先通过本轮候选 UI／托盘的 Exit 正常退出，UIA 可以操作就由助手执行。只在工具实际受阻时请求用户协助，并给出具体阻碍。核对每个候选 PID／创建时间／路径确实退出；不按进程名盲目 kill。

有界 Stop／Dispose 本次全部宿主与证书 native key；在停止数据库前读取本轮 scoped actual counts、identity/version、邮件 Attempts 和无 dispatch 证据。清理本 origin 的两个 WinCred 槽、仅本份公钥信任，最后停止唯一证明归属的 PostgreSQL。保留全部业务数据、受保护文件、ledger、回执、TRX 和非秘密截图；停止不等于删除数据。

无法证明归属、资源消失、证书清理或数据库边界时报告失败和残留，不换目标、退款、清数据或补跑。报告区分静态门禁、真实 PostgreSQL／HTTPS、UIA 行为、用户主观验收及 Git 状态；此轮不授权 main 合并、push、部署、日常安装、生产 DLL 替换、持久数据删除或备份还原。备份／还原及数据库进程崩溃恢复仍按独立阶段取得实际证据。
