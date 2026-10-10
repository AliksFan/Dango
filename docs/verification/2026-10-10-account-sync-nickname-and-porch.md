# 登录同步、昵称与门廊修订验证

日期：2026-10-10。状态：核心与 Desktop 接线已整合，真实窗口 UIA 已通过；正在准备独立昵称数据库验证，新真实数据库验证和主观验收尚未通过。

## 登录比较与成功保存边界

两个登录反例在修改前均失败：相同配置仍停留选择页；记住登录恢复没有展示差异选择。证据为 `artifacts/backend-account-sync/static/login-comparison-before.trx`。显式登录和恢复均读取比较，语义相同直接就绪，不改写本机或账号；差异保留双方配置并继续来源选择。修后账号边界19项通过。

新增成功落盘信号：草稿、取消、无变化保存、保存失败和云端应用均不能产生上传意图。遗漏通知的实现下两项测试中1项失败，`save-signal-before.trx`；信号在磁盘与内存确认保存后发出。

联合账号／外观门禁首次35项中一条既有旧版本保存用例失败；未改实现或放宽断言，针对该组复核2项通过，联合复核 **35/35、零跳过**。最终证据 `artifacts/backend-account-sync/static/login-save-confirmed.trx`。初次失败保留于 `login-and-save-boundaries.trx`，不将其删除或当成通过。

这些是受控 HTTP receiver／本地文件／Dispatcher 边界测试，尚未证明新的真实数据库流程、连续同步或昵称与门廊完整行为。

## 技术界面自动化可行性

已找到项目原生探针 `eng/test-local-desktop-window.ps1`、真实窗口和 Win32 输入测试基础。已在当前旧候选上按 PID、创建时间及路径核验归属，只读访问任务窗口 UIA；首页登录、门廊、捏脸、设置等按钮提供 InvokePattern。没有读取编辑框内容、系统凭据或任务外窗口，没有消耗业务写入预算。

此只读探针证明工具可用，不能代替新需求端到端界面测试。后续由助手完成已授权的真实窗口交互和技术流程，再交用户评审手感、效果及流畅度。

## 数据与交付

旧 R2 回执已为 stopped，ResourceCleanupComplete=true、ServerKeyDisposed=true、无 Problems 或 EvidenceFailure；根复核助手、PostgreSQL 和第二次候选原 PID 均不存在，两个记录端口无监听、pidfile和本轮 Root 公钥消失。对该固定origin核验两槽准确摘要后只调用CredRead存在性及CredFree，未解包或复制blob，实查两槽均不存在。按原回执保留邀请、注册、挑战、邮件、账号、团子各1，账号房间2、会话2、历史租约1、Audit3、Events4，邮件尝试为0。此收尾不表示新要求通过，也不删除原数据。

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

门廊组件、真实物理实体及多视口证据见[门廊记录](2026-10-10-porch-account-scenes.md)。24字素最长昵称窄窗口溢出已复现；修为按导航空间缩放、有界省略显示、完整Tooltip／辅助名称，代理七种真实窗口通过。根联合账号／外观／门廊／昵称布局 **61/61**，`account-porch-layout-final.trx`，零失败或跳过。

独立线程对真实 HWND 的 Windows UIA client 已在根整合执行 **4/4**，`account-window-uia-confirmed.trx`：首次null昵称校验／保存、配置来源选择、首页昵称、关闭浮层后真实两秒timer收敛及记住登录相等／差异恢复。使用实际 Invoke／Value／Selection／Toggle 模式，密码为fixture预填、HTTP与仓储受控；不是正式HTTPS／系统WinCred／真实数据库端到端。方法与边界见[账号真实UIA记录](2026-10-10-account-window-uia.md)。根截图位于 `artifacts/test-runs/5cc9425eb4ff4319b30f7e6be7705c74/account-window-uia/`，已检查来源选择与首页昵称；首页fixture标明无实时场景，不能冒充实体渲染。

旧较宽门廊回归的两项失败准确属于新增fixture：转场尚未完成即断言门廊，以及实际stage焦点缺失导致团子仍选本地门。最终增加转场等待和明确stage焦点后，87项及真实实体单项通过，随后同进程定向2项也通过；不将这两个错误归入系统前台阻碍。独立原生键盘仍因Windows拒绝前台而未验证、未注入键；不会默认交给用户，后续真实候选若仍需手动取得前台再按具体阻碍请求协助。

独立候选 `artifacts/local-room/account-profile-sync-20261010/bin/Dango.Desktop/release/` 已由现有review脚本NoLaunch构建，零警告／错误；未启动或触达新系统凭据槽。exe SHA256 `36349C85912EA6384253AF33F83BDB1026592F82A3C8EED59E9A217D0FBDB6B2`、Desktop DLL `B2D606261FBF330D76B799C75BC082E17B6FC62E3EF90A99D471A998088A7FC6`。生产和隔离物理模块hash仍为上述原值。

新增旧PostgreSQL运行器边界：对子进程明确归零Profile批准变量，所有旧阶段基线强制排除Profile类。原实现两个离线反例均失败；修后根 `eng/test-backend-postgres-stage-boundary.ps1` **2/2** 通过，未启动数据库。新迁移与虚构资料写入只可走本轮独立审批入口。

精确单迁移命令现在向正式 `IMigrator` 传入批准目标。单个 pending 与目标不符或有额外 pending 时拒绝；目标已应用则零迁移调用，避免向后执行。迁移失败继续作为失败返回。提取原无目标调用作为反例后，离线 4 项中 2 项失败；显式传目标后 **4/4**，`formal-single-migration-confirmed.trx`，原反例 `formal-single-migration-before.trx` 保留。此检查使用 recording migrator，不连接数据库；并不表示新增真实迁移已经执行。

后续真实验证拆为 A-only 昵称迁移与固定 7 项测试、以及另行准备的联合 UIA／双客户端／门廊验证。前一段批准不会开放后一段写入。全量草案仍为未批准规划，测试 owner 异常即停止，不声明崩溃续跑已实现。
