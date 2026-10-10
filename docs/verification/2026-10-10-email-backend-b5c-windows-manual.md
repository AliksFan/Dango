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

新增离线 metadata SQL 反例 `DatabaseValidationUsesTheFormalDangoMigrationHistorySchema`：旧实现1／1真实失败，`manual-history-schema-before.trx` 保留。维护上下文改为正式共用 `DangoDatabaseOptions.Create`；修后完整工具 **125／125** 通过，`manual-history-schema-fixed-r2.trx`。新增非秘密启动阶段／稳定错误码回执。下一次新目录及新证书按[R2 精确计划](../planning/2026-10-10-backend-b5c-windows-manual-r2-plan.md)准备，尚未获批或启动。

本阶段不改变正式多人移动／动作／场景协议，不能关闭完整 B5。没有 main 合并、推送、部署、日常安装或生产 DLL 替换。
