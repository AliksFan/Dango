# B5A Windows 账号与配置候选验证

日期：2026-10-09。用户选择“居中浮层”，按[执行计划](../planning/2026-10-09-backend-b5-account-execution.md)开始正式 Windows 接入。当前完成账号／配置候选、自动局部门禁，以及用户另行明确批准后的五项真实账号客户端集成；系统凭据实写、窗口人工与房间接入仍未验收，不标记完整 B5 已验收。

## 实现范围

- `Client.Sync/Accounts/`：正式邮箱账号、邀请注册／继续、找回请求、登录／记住恢复／退出，同 Cookie 的本人 Pet 适配。固定 HTTPS 根地址、正常生产 TLS 校验、禁止重定向、新 CSRF、有界严格 JSON、取消与身份绑定。没有新增依赖。
- `AccountPanelView`、`DesktopAccountController`：居中暖色浮层，账号与明确本机／账号配置选择。发送受理、SMTP 接受、已验证、发送失败与提交未知分开；Completed 恢复后提示首次密码并转标准登录，不再次采集或提交配置。
- `WindowsAccountSecretStore`：按规范化 origin 的摘要与 session／registration 两槽限定 Windows 当前用户凭据。默认登录只在内存，显式记住才保存，密码不保存；长度限制与 native 写入缓冲清零。当前没有访问或实写系统凭据，不能将内存替身测试算作 Windows 凭据验收。
- ProductHome／host：右上登录、门廊独立“在线账号”；未配置 `DANGO_SERVER_ORIGIN` 时说明真实不可用原因，仍可继续本地。背景控件／Tab 与游戏快捷键隔离，Esc 可从密码框关闭；关闭／隐藏／卸载清秘密并取消请求。正式房间目录／Hub／租约尚未接入。
- 使用既有 B2 repository／journal／choice；读取不写，选卡不写，确认才保存原件并应用配置。冲突、未知结果或完成记录保存失败不能显示成功；账号 Pet 身份保持不变，退出保留已应用的本机外观。

## 实际验证

| 门禁 | 结果与证据 |
|---|---|
| Client 无真实网络 | 134／134，通过且零跳过；`artifacts/backend-b5a/client/b5a-client-static.trx`。包含 60 个账号反例及既有 74 个静态回归。3 个旧 `RealNetworkAcceptanceTests` 未执行 |
| Desktop 受影响组 | 96／96，通过且零跳过；`artifacts/backend-b5a/desktop/b5a-desktop-affected-native.trx`。包含首页、布局、外观存档、本地配置 repository，以及有限本地房 native／WPF 自动检查 |
| 最终账号／浮层补充组 | 27／27，通过且零跳过；`artifacts/backend-b5a/desktop/b5a-account-boundaries-final.trx`。与上组去重为 **102** 个 Desktop 用例；补充邮件未验证不能完成、明确拒绝后的显式继续信息清理、居中／高对比与表单按键隔离 |
| Release | 集成后 Desktop 和 ServerTests 均构建成功，0 警告、0 错误；新 Server 集成用例编译不能算真实执行 |
| 数据 runner | 不带执行开关的 B5A dry-run 只展示原隔离目标；PowerShell AST 语法及三个筛选逃逸拒绝检查通过。未解密数据凭据、启动实例或连接数据库 |
| B5A 真实集成 | 用户按精确计划回复“允许”后执行正式客户端→本机 HTTPS→既有隔离 PostgreSQL，**5／5 通过、零失败／跳过**。证据 `artifacts/backend-b5a/postgres-tests/20261009-123107-5324222/backend-postgres.trx`；本轮无迁移、SMTP 或 Windows 凭据管理器操作 |
| 格式与保护文件 | `git diff --check` 通过。受保护 `dango_box2d.dll` SHA256 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A` |
| WPF 离屏视觉 | 实际 ProductHome／AccountPanel 的 1024×640 和 336×640 登录、注册、配置图均居中；水平误差 0 DIP、垂直误差不超过 0.33 DIP，无横向溢出。336×360 长表单真实滚到底，操作可见。只证明离屏布局 |

Desktop 初次受影响测试缺少隔离 native 模块，两个房间运行时检查失败、两个实时组未验证；随后核对既有隔离模块 SHA256 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`，通过构建属性复制到本次测试输出，96 项全部通过，未替换受保护库。原门廊测试把一切非返回按钮视作房门，已精确允许新账号入口，并增加“打开账号表单仍无准入”的断言，保留不能伪进入房间的原门禁。

## 实际复现并修复的生命周期缺陷

存储删除失败后，Logout 仍等待服务端；用户关闭再打开浮层，旧 UI 代际的错误曾被丢弃，新 Open 排队后可能从仍有效的记住 Cookie 恢复登录。

新反例真实保存虚构记住 Cookie 到内存 store，阻塞 Logout、让删除失败，再立即 Close→Open。修复前 `IsLoggedIn` 为 true，19 项中该项失败；证据 `b5a-account-boundaries-before-cleanup-fix.trx`。修复将“记住材料尚未清除”记录为跨 UI 代际的生命周期事实，并在新 Open 取得串行锁后重查。修复后断言未登录、没有新身份读取且保留清理提示；最终组通过。退出文案分别报告本机清理与服务端撤销两个结果。

补充反例覆盖 Logout 本身排队等待旧配置读取、尚未开始清理即被 Close 取消。修复前也恢复为已登录，证据 `b5a-queued-logout-before-fix.trx`；退出意图现在立即阻止记住恢复，只有已确认的清理结果才解除，未进入回调也释放旧 client。修复后最终组 27／27 通过；两项修订均经只读复核。

旧登录忽略取消后的迟到响应、完成注册丢响应后重读 Completed、清理失败后重开实际重试删除、配置显式确认及 resize 时背景仍禁用均有受控 handler／STA 反例。这些测试没有访问数据库、SMTP 或 Windows 凭据管理器。

## 五项要求的证据边界

| 要求 | 当前证据与未完成项 |
|---|---|
| 正确性 | 异常 origin／响应／输入、真实 WPF 状态和邮件语义有反例；真实注册／登录／恢复／本人 Pet HTTP 合同通过 |
| 一致性 | 请求键保存、串行等待旧操作退出、迟到响应拒绝、显式配置与 conflict 边界已测；真实 Start／Complete 提交丢响应同键恢复、完成重放不替换原密码／配置、双客户端旧版本写入冲突通过 |
| 安全性 | 凭据与 UI 状态分离、fresh CSRF、固定 origin／身份、安全 store 槽及注销两个结果已测；真实认证恢复和撤销后旧 Cookie 拒绝通过，系统凭据实写未验证 |
| 可靠性 | 关闭／取消、未知完成状态与本机保存错误不伪成功；真实服务强杀、磁盘故障、备份还原未验证 |
| 扩展性 | 请求／响应／秘密大小有界，单控制器与取消／锁边界已测；生产吞吐、长期生命周期及房间通知／心跳未验证 |

## 下一门禁与交付状态

`AccountClientHttpsIntegrationTests` 与 runner `-B5A` 按[精确数据计划](../planning/2026-10-09-backend-b5a-local-data-plan.md)执行首轮五项真实测试，仅使用原隔离库、新造 `.invalid` 账号、临时本机 HTTPS 和内存安全 store。独立 B5A 授权开关在资源操作前检查，旧阶段批准不能启动；额外筛选不能逃逸到其他阶段。测试实际核对注册后尚未登录、共享 Cookie／登录后新 CSRF、配置版本及账号／默认房身份不变、客户端重建后的恢复、旧 Cookie 拒绝、原密码和原配置保持正确。Logout 丢失成功响应时客户端仍报告撤销未知，再由独立客户端及数据库确认真实撤销；没有用数据库事实冒充客户端已知结果。

本轮真实测试无需修改实现，第一次完整运行 5／5 通过，没有追加、扩大或重复数据库用例。测试代码在 `d3a28b9`，数据操作仅由 root 执行；子代理只读支持。邮箱确认使用新造挑战和真实 CSRF HTTP，没有 SMTP、真实投递或邮件网页人工验证。

收尾证据保存在同一忽略结果目录的 `resource-receipt.json`：runner 报告本次 PostgreSQL 已停止；进一步检查本次 PID 不存在、数据库端口无监听、`postmaster.pid` 已消失，测试 fixture 宿主进程及其监听残留为 0。随机 HTTPS 端口未逐个写入 TRX，五个 fixture 的 `await using` 正常结束且测试宿主已退出。`pgdata`、原 key ring、恢复代际和虚构数据保留，既有系统 PostgreSQL 服务未操作。

Windows 系统凭据实写、实际客户端窗口人工、真实邮箱投递、正式房间／Hub 与多客户端显示、长期 worker／WebSocket、备份还原均未验收。已有 B1–B4 结果保留历史证据，不能自动转成 B5A 通过。

本地任务分支 `codex/backend-b5-windows` 整合子代理的 UI、账号与真实测试准备提交，保留父任务及其他 worktree；未合并 main、推送、安装、部署或替换正式 DLL。本次临时 PostgreSQL／HTTPS 监听已结束；此前自动检查的临时 WPF 窗口按 finally 关闭，仓库忽略目录保留 TRX、收尾记录、截图、一次性 render harness 和普通构建输出，不自动删除。受保护 DLL 的复核 SHA256 仍与上表一致，main 仍为本轮之前的基线。

视觉证据位于 `artifacts/backend-b5a/visual/`：`account-login-1024x640.png`、`account-register-336x640.png`、`account-configurationchoice-336x640.png`、`account-register-bottom-336x360.png`、`account-configurationchoice-bottom-336x360.png`。图中虚构状态／配置仅用于布局，不代表已登录或已同步。
