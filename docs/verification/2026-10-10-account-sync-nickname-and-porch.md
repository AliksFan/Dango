# 登录同步、昵称与门廊修订验证

日期：2026-10-10。状态：核心与 Desktop 接线已整合，正在完成真实窗口与独立审查；新真实数据库验证和主观验收尚未通过。

## 登录比较与成功保存边界

两个登录反例在修改前均失败：相同配置仍停留选择页；记住登录恢复没有展示差异选择。证据为 `artifacts/backend-account-sync/static/login-comparison-before.trx`。显式登录和恢复均读取比较，语义相同直接就绪，不改写本机或账号；差异保留双方配置并继续来源选择。修后账号边界19项通过。

新增成功落盘信号：草稿、取消、无变化保存、保存失败和云端应用均不能产生上传意图。遗漏通知的实现下两项测试中1项失败，`save-signal-before.trx`；信号在磁盘与内存确认保存后发出。

联合账号／外观门禁首次35项中一条既有旧版本保存用例失败；未改实现或放宽断言，针对该组复核2项通过，联合复核 **35/35、零跳过**。最终证据 `artifacts/backend-account-sync/static/login-save-confirmed.trx`。初次失败保留于 `login-and-save-boundaries.trx`，不将其删除或当成通过。

这些是受控 HTTP receiver／本地文件／Dispatcher 边界测试，尚未证明新的真实数据库流程、连续同步或昵称与门廊完整行为。

## 技术界面自动化可行性

已找到项目原生探针 `eng/test-local-desktop-window.ps1`、真实窗口和 Win32 输入测试基础。已在当前旧候选上按 PID、创建时间及路径核验归属，只读访问任务窗口 UIA；首页登录、门廊、捏脸、设置等按钮提供 InvokePattern。没有读取编辑框内容、系统凭据或任务外窗口，没有消耗业务写入预算。

此只读探针证明工具可用，不能代替新需求端到端界面测试。后续由助手完成已授权的真实窗口交互和技术流程，再交用户评审手感、效果及流畅度。

## 数据与交付

旧 R2 回执已为 stopped，ResourceCleanupComplete=true、ServerKeyDisposed=true、无 Problems 或 EvidenceFailure；根复核助手、PostgreSQL 和第二次候选原 PID 均不存在，两个记录端口无监听、pidfile和本轮 Root 公钥消失。按原回执保留邀请、注册、挑战、邮件、账号、团子各1，账号房间2、会话2、历史租约1、Audit3、Events4，邮件尝试为0。此收尾不表示新要求通过，也不删除原数据。

新昵称迁移未应用于真实数据库。未合并 main、推送、部署、更新日常安装或替换生产 DLL。生产 DLL SHA256 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`；测试输出隔离模块为既有 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`。

## 核心、昵称及 Desktop 接线

联合 Release 构建零警告、零错误。最初 no-restore 构建因当前树四项 assets 缺失失败；正常还原已有依赖后通过，没有新增或升级依赖。

- Client.Sync 静态 **403/403**，`artifacts/test-runs/account-sync-nickname-porch/sync-static-confirmed.trx`。先前排除错误类名时，3项旧真实网络测试因未配置 DANGO_SYNC_SERVER_PATH 在启动前失败；403静态项均通过，该首跑回执保留，不称真实网络通过。
- Protocol **12/12**，`protocol-combined.trx`；Server 明确排除 PostgresIntegration 后 **191/191**，`server-nondatabase-confirmed.trx`。
- 账号／捏脸会话 **45/45**，`account-desktop-lifecycle-confirmed.trx`。关闭浮层继续同步、云端应用零回传、昵称输入不中断、资料读取失败显式重试、保存昵称丢响应读取核对、退出后下次登录比较、读取期间本机变化、离线查看同步状态恢复均通过。
- 账号／外观／门廊首轮联合52项中5项失败：4项新昵称测试错误将 TextBlock 内容当 string，改为读取实际 UIA Name；1项既有排队注销暴露重新开页的比较请求使注销不可用，修复 UI 和 controller 允许注销取消忙碌读取。随后 **52/52**，首跑回执仍保留。
- 房间／在线连接／首页受影响回归 **44/44**，`room-home-regression.trx`。门廊目录读取事件直接读取，不先展开空操作区。

记住登录在首页后台恢复昵称：相同配置不打开账号页，不同配置主动打开选择页、零写入，2项通过于 `homepage-restore-integration.trx`。旧未知请求最多两次提交、完成回执有限补偿及新保存独立提交已整合；旧独立反例修后2项通过。登录比较改为先排空上一会话的 Pump 再捕获本机基线，等待可取消。独立迟到任务反例在冻结旧源码上确实覆盖新保存而失败；修后相同路径保留最新本机、展示差异选择，零Apply／零PATCH。已整合并在根执行 **1/1**，`sync-handoff-boundary-confirmed.trx`。

昵称为独立 AccountProfile，允许重名；首次 null 必设，之后可修改。前向迁移 `20261010055529_AccountProfile` 只增加可空昵称和资料版本。7项真实 PostgreSQL/HTTPS昵称用例仅编译，尚未执行。

门廊组件、真实物理实体及多视口证据见[门廊记录](2026-10-10-porch-account-scenes.md)。24字素最长昵称窄窗口溢出已复现；修为按导航空间缩放、有界省略显示、完整Tooltip／辅助名称，代理七种真实窗口通过。根联合账号／外观／门廊／昵称布局 **61/61**，`account-porch-layout-final.trx`，零失败或跳过。真实独立 UIA 流程继续推进。以上受控 receiver、文件与 Dispatcher 测试不能替代正式服务端、数据库和系统凭据端到端验证。
