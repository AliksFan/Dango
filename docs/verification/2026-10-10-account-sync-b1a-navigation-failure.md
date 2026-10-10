# B1a 真实导航失败与收尾

日期：2026-10-10。用户批准后按固定计划执行一次，退出码 1，错误 `accountsync_run_tests_failed_no_retry`。唯一 TRX 为 **1 Failed、0 Passed**。本轮额度已消耗，不能重跑、补 SQL 或补查数据库。

## 实际范围

执行提交 `212424bb118f9b01b05b7b130560c600d64e37c1`，固定清单 `artifacts/account-sync-b1a-approved.json`，SHA256 `6E6FCF13A1B108B9C08A4FD90208D4EBDF66E46F8AF3E73FD43270CE3AED2D50`；219 项输入执行前由根与独立代理逐项复核，缺失 0，全部匹配。执行时没有修改源码或重建候选。

证据位于原独立开发目录的 `run20261010accountsyncb1a`。OwnerId 为 `dd930dc9-361f-42a0-bd40-39a5aa84561d`。以下结论只读取已生成的 `resource-receipt.json`、`account-sync-b1a.trx`、`attempts.jsonl`、`resources.jsonl`、`before.json` 与本轮窗口截图，没有恢复实例或补查数据库。

已执行邀请注册、首次登录昵称、首页昵称断言，以及独立第二客户端登录和读取 eyeSize=103、configuration_version=1。首次 UI 保存 111 尚未发生，UI Automation 查找 `FaceButton` 失败。堆栈为 `UiaClient.Id → InvokeId → PreviewEyeSize → SaveEyeSize`，旅程首次 `SaveEyeSize(111)`。

实际尝试账本 7 条：Invite、Start、Confirm、Complete、Profile 各 1，Login 2，Configuration 和 Logout 都是 0。注册图已有写入；最终数据库注册图、会话状态及行计数没有取得，不能从尝试账本推定数据库最终结果。

## 可确认的导航缺陷

`first-login.png` 显示当时换装编辑器已经打开；编辑时首页 Launchers 被 Collapsed，所以 `FaceButton` 不在当时可用的 UIA 树中。WPF 默认会以 FrameworkElement.Name 生成 UIA AutomationId，源码的 x:Name 本来有效，不能将此失败归为缺少显式 AutomationId。

旧 helper 假定首页入口始终可达，并把 Face、Eyes、Range 三个动作放在同一 UIA 操作中，没有等待编辑器与参数页的实际状态。旧 CloseEditor 也只处理捏脸关闭按钮，无法关闭换装。这是确定的测试导航缺陷。

换装最初如何打开仍未知。已有证据不能证明是产品自动切页、键盘输入或其他原因。新导航 helper 将记录非秘密页面状态，使用真实 UIA 关闭当前编辑器并逐步等待；不直接修改正式业务状态，不增加产品 AutomationId，也不以技术复现冒充新的数据库旅程通过。

## 收尾证据

实际使用 7 个工具：version、status、start、before-size、before-full、唯一 vstest、stop。回执确认全部工具自然结束、ProcessDispose，以及双输出流关闭、读取任务收尾。

6 条资源记录依次为 TLSCreated、HostStarted、DesktopStarted、DesktopDisposed、HostDisposed、TLSDisposed。只有 1 个完整 AppHost 和 1 个 HTTPS Host 实际启动。DesktopDisposed 的 Disposed、HwndClosed、ConfigurationPumpDrained 均为 true；HostDisposed 的 Stopped、Disposed 为 true，InFlightRequests=0；TLSDisposed 的 NativeKeyIdentityCaptured、NativeCleanupConfirmed 为 true，NativeProbeFailed=false。

运行器确认 PostgreSQL、CMD 桥和工具输出资源收尾；pidfile 不存在，记录端口不再监听。回执中的 `FixtureNativeCleanupConfirmed=false` 来自完整十条成功资源摘要未生成，不能据此报告原生密钥清理失败；原始 TLSDisposed 记录实际清理确认 true。

before 含恰好 22 张旧业务表，计数与摘要多重集基数一致。after 未产生，**旧 22 表最终保全未验证**。原数据、证据、应用文件、密钥与恢复代际保留，无迁移、SMTP、系统信任或 WinCred 操作。

独立审计投影：`artifacts/account-sync-b1a-result/c6abf489c10a4920a1c84b4d901a02e2/independent-audit.json`，SHA256 `959D5C46C2B7815FC010A42415A4DCFC233B6CAD31516F635769065E5D12206C`。审计前后源文件 SHA256 不变。

后续先完成纯前端反例、导航修复、离线回归和新固定候选，再请求 B1a2 精确资源批准。保留 B1a 已批准清单、候选与执行证据；B1a2 使用新的 stage、grant、输出目录和一次性证据目录。未合并 main、push、部署、更新日常安装或替换生产 DLL。
