# B5C Windows 人工验收环境记录

日期：2026-10-10。用户已批准[精确人工计划](../planning/2026-10-10-backend-b5c-windows-manual-plan.md)。本记录区分准备工具、真实启动、用户交互和资源收尾；人工结果尚待用户操作。

## 准备工具与静态门禁

`eng/tools/Dango.BackendManualReview` 是本阶段专用 Windows 验收助手，不是正式部署入口。它只接受已批准计划开关及固定 PostgreSQL 18 二进制目录；由自身编译位置识别源码树，核验既有独立数据库目标、恢复代际、既有密钥及候选模块。新人工目录原子创建，拒绝既有目录及 reparse 重定向。未批准、目标不符、原实例或同模式 Desktop 已运行时先拒绝。

正式宿主新增可选的请求边界 hook，位于认证、Origin／CSRF、授权之后、正式 endpoints 之前，默认调用行为不变。助手注册单例预算和额外 Hub filter，保留正式 filter；明确关闭 Email、Expiry、Live、Outbox workers。每项业务写入前限制本轮已捕获对象、请求摘要及次数；失败不退款，相同键改参数拒绝，登录无幂等键故逐次消耗。宿主不能重启清空预算。

邮箱维护确认只读取已捕获本轮预注册及唯一有效注册挑战，核验邮箱、邀请、恢复代际、generation、期限和未消费状态，然后走正常系统 TLS、Origin 和 fresh CSRF 的正式 HTTP。确认 proof 仅缓存于内存；未知响应先核对原对象，保留原 proof。注册已提交而客户端取消时，登录前限定只读核对 Completed 对象并恢复相同身份；不补账号或提高配额。

资源工具对本次临时证书、两个 WinCred 槽及候选进程持有明确归属。PFX 用 `DefaultKeySet` 重载以满足 Schannel；不使用 `PersistKeySet`。Root 只导入公钥，删除需核验原不存在及本次 Add 的证据。进程 Start 成功立刻保留 capability，再观察 PID／时间／路径；观察失败阻止声称已退出。PostgreSQL 只有本次成功控制调用且创建时间处于启动窗口、实际参数与目标均匹配，才取得停止权。

首轮工具静态门禁 **124／124 通过，零失败／跳过**，构建零警告／错误，`git diff --check` 通过。结果：`artifacts/backend-b5c/manual-static/manual-static-start-gate.trx`。组成是预算／HTTP／Hub 51 项、资源 33 项、目标／进程证明 18 项、挑战确认 22 项。这些测试使用内存／fake 资源；没有通过测试启动真实数据库、导入系统证书或读写真实 WinCred。

失败证据保留于预算及资源隔离工作树：缺 guard 的三个反例、旧注册取消恢复的两个反例、缺资源保护的初始23项反例、启动后身份观察遗漏及未重载服务私钥两个反例均确实失败，再由修复使相应门禁通过。不能把这些静态反例称为真实进程强杀恢复或完整系统验收。

## 收尾设计

等待所有本轮候选真正退出，仍运行或归属不明时停止收尾并提示人工处理，不盲目 kill。宿主 Stop／Dispose 分别有界，释放后独立 Dispose 本次临时 native server key；随后只停止本次 PostgreSQL、清除专用两槽和本次 Root 信任，保留数据与受保护文件。

宿主释放后、PostgreSQL 停止前按本轮 invitation／pending／user／pet／rooms 读取实际落库数量、邮件 Attempts 为零及无事件 dispatch 的证据。数据库证据未确认要保留验收失败；资源全部清理后仍可关闭助手，不为补证据自动重启或写数据。非秘密回执以独立锁、CreateNew 临时文件及同目录替换写入，防止 UI 刷新和后台流程互相截断。

## 当前阶段

原计划已实际尝试：用户核对系统提示并确认公钥信任，既有 PostgreSQL 成功启动。维护目标检查使用默认 provider options，错误查询默认 `__EFMigrationsHistory`；正式迁移表是 `dango.__ef_migrations_history`。现有结构不需要变更，此次在签发邀请之前停止，没有本轮账号／挑战／邮件／房间／会话或 Desktop 候选启动。

前次助手回执为 `stopped`、ResourceCleanupComplete=true、ServerKeyDisposed=true、无 Problems，InvitationId为空，预算与候选进程均为空。随后根实查本次 PostgreSQL PID退出、两个记录端口无监听、pidfile消失、该 thumbprint 在 CurrentUser Root 不存在，并仅对已核验路径／创建时间且资源已清理的助手请求正常关闭，进程实际退出。受保护文件及原库保留。本次验证了环境准备失败的资源收尾，未验证真实账号或端到端客户端 TLS／WinCred 使用。

新增离线 metadata SQL 反例 `DatabaseValidationUsesTheFormalDangoMigrationHistorySchema`：旧实现1／1真实失败，`manual-history-schema-before.trx` 保留。维护上下文改为正式共用 `DangoDatabaseOptions.Create`；修后完整工具 **125／125** 通过，`manual-history-schema-fixed-r2.trx`。新增非秘密启动阶段／稳定错误码回执。

### R2 真实启动与当前验收点

用户明确回复“确认”，批准[R2 精确计划](../planning/2026-10-10-backend-b5c-windows-manual-r2-plan.md)后执行一次准备。开始前核验新目录和既有独立实例 pidfile 均不存在、工作树干净、工具静态125／125、受保护生产 DLL 和隔离候选模块 hash 与既有记录一致。没有覆盖前次目录。

R2 助手回执为 ready／StartupPhase=ready、StartupFailure为空、无 Problems；正式历史检查通过，已签发一份本轮邀请。首次候选进程具有可核验 PID、创建时间与固定 release 路径，IdentityVerified=true、LaunchNumber=1；助手、PostgreSQL 与真实 Dango 控制中心进程存活且可响应。此时注册开始／完成、登录、共同房、Enter、码重置、配置和 Other 预算均为0，本轮账号注册尚未开始。

在正常系统证书校验下请求本轮 HTTPS `/health/ready` 返回200及 `status=ready`；未使用证书 pin、跳过验证或正式业务请求代替用户操作。TLS 公钥信任及专用凭据槽仅用于此 R2 origin；其准确值位于仓库外受保护的非秘密回执，不写成本机部署配置。

独立代理对当前启动快照只读复核通过：owner／runtime／TLS 回执归属一致；候选父进程、参数及时间与 launcher 对应；PostgreSQL 精确 `-D`、`-h`、`-p` 与 pidfile 对应，两项监听仅在 loopback 并由本轮进程持有。R2 目录无 reparse、当前用户独占受保护 ACL；公钥文件／回执／当前用户 Root 的唯一条目摘要一致，无 private key，SAN 仅 loopback、EKU 仅 server-auth、非 CA、3天且有效。代理没有读取秘密或业务数据，没有操作资源或 UI。

当前环境保持运行，交给用户按助手中的顺序操作邮箱账号、配置来源、房间管理、租约与进程重开。没有代用户设置密码、注册、登录、创建房间或判定体验通过。结束需用户从托盘退出候选，再点击助手“结束验收”，随后核验本轮资源收尾；目前尚未收尾，不能把运行中的资源称为已退出或把 readiness 称为人工验收通过。

本阶段不改变正式多人移动／动作／场景协议，不能关闭完整 B5。没有 main 合并、推送、部署、日常安装或生产 DLL 替换。

### R2 后续实际收尾

账号／门廊修订续作时只读复核：runtime-receipt 已为 stopped，ResourceCleanupComplete=true、ServerKeyDisposed=true、Problems为空、EvidenceFailure为空。原助手26912、PostgreSQL34716、第二次候选29704均不存在，记录的63237／63939无监听，独立 pgdata 的 pidfile和本轮90A3DB8A94F85DC7F0FBD45641C16D6C39B3593C公钥信任不存在。助手回执证明两槽清理；本轮复核没有重新读出任何凭据内容。

落库回执：邀请／预注册／挑战／未发送邮件／账号／团子各1，房间2（默认房与共同房各1），会话2、历史租约1、Code0、Audit3、Events4，邮件Attempts0。预算已用注册开始1、完成1、登录2、创建1、Enter1、配置0、码重置0、Other2。原库、批次文件和隔离本地存档保留；这表明有限真实路径发生过，不代表未执行的原验收步骤或后续修订通过。以上“环境保持运行”描述仅为先前 ready 快照，现已收尾。
