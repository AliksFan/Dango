# 登录同步、昵称与门廊修订验证

日期：2026-10-10。状态：核心与 Desktop 接线已整合，真实窗口 UIA 已通过；独立昵称数据库验证 A／A2／A3／A4 均已批准执行并失败收尾。D1已获精确批准并完成一次元数据诊断，当前仅额外配饰迁移历史不匹配；零业务写入、资源收尾通过。准备A5精确六历史及22表保全候选；新真实数据库业务验证和主观验收尚未通过。

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

正式 options factory 的 `IHistoryRepository.GetCreateScript()` 离线元数据检查 **1/1**，`profile-history-metadata.trx`：历史表是 `dango.__ef_migrations_history`，列为带引号的 `"MigrationId"` 和 `"ProductVersion"`。业务实体的 snake_case 规则不作用于该历史表；新运行器历史查询必须使用真实列名。

Profile 专用保护已整合。固定 7 个用例共享一次 owner；邀请、注册、登录、直接 Update 和 HTTP 发送均先预留、持久记录尝试，再进入正式边界。每个用例与全局的 67 次变更尝试、22 次 HTTP 请求、7 个夹具／28 行资源回执有固定限额，短锁不包住真正的并发业务。每个夹具在种子前复核原 state／keys／epoch 和实际数据库角色、监听、端口；外来请求 URI 拒绝转发，但坏 CSRF／Origin／JSON 保持原样进入正式服务。失败记录不输出 security stamp。owner 或回执失败后停止后续调用，正常 Dispose 与无法确认的清理分别记录。

整合后新增 13 项无数据库反例随非 PostgreSQL 的 Server 门禁执行，当前 **209/209、零跳过**，`server-profile-boundary-nondatabase.trx`。独立清空 Profile 与 B1 批准后，精确 Profile 类 **7 项全部 skipped/inconclusive、零 failed**，`profile-unapproved-gate.trx`；初始化未通过时 cleanup 不再重新访问 owner。这是禁止未批准执行的证据，不能称 7 项真实数据库业务已通过。

后续真实验证拆为 A-only 昵称迁移与固定 7 项测试、以及另行准备的联合 UIA／双客户端／门廊验证。前一段批准不会开放后一段写入。全量草案仍为未批准规划，测试 owner 异常即停止，不声明崩溃续跑已实现。

独立 A-only 运行器整合为 `377b557`，根 `eng/test-account-profile-postgres-boundary.ps1` **66/66**、旧阶段隔离 **2/2**；本轮非秘密离线回执为 `artifacts/account-profile-offline/93e4532dccba41d4a34390389b5adce2/offline-results.json`，`ExecutedDatabase=false`。覆盖固定 digest、文件及旁依赖变更拒绝、精确目标、进程超时及归属、不可退款的尝试、证书销毁回执、TRX 精确七项、只读 JSON framing、旧图保留和本轮图／零发信证据。代理故障副本去掉批准摘要校验后，仅针对清单与DLL同时变更的反例失败（65/66），没有修改根正式实现或启动数据库。

[A-only 精确计划](../planning/2026-10-10-account-profile-postgres-a-plan.md)已获用户批准并执行一次。原固定清单 `artifacts/account-profile-a-approved.json` 冻结 294 份输入，SHA256 `195596BA3F9D334EF71EEA33F75844C88DE492276FA8FD19AD33FD03D7AFE21A`，执行前与输入匹配；此后修复源码变动不再使用它执行。已创建受保护证据目录、在内存读取批准的 DPAPI 凭据并启动精确 PostgreSQL；因工具身份守卫失败，尚未运行 SQL 探针、迁移、种子、夹具或七项真实测试。业务尝试账本与资源账本均为空。原失败回执保留，根补充完整 Windows CMD 桥链证据后精确停止本次实例，PG／CMD／pidfile／端口已退出。见[首次失败与收尾](2026-10-10-account-profile-a-startup-failure.md)。原一次性额度已消耗，不能自动重跑；此固定输入不授权联合 UIA、房间业务写入或系统凭据操作。

启动归属修复已整合，根最终离线 **111/111**、真实冷版本工具生命周期 smoke 通过；测试 owner 的 A2 固定目录已构建，关闭批准后七项全部跳过。独立审查没有发现新的误停止路径；CMD 桥先于 PG 消失时保守报告未确认的限制已记录。第二次[A2 精确计划](../planning/2026-10-10-account-profile-postgres-a2-plan.md)及清单 SHA256 `D38FF1D2381AAE9806A6CD541935EA8E29BB552F9ED13CF1F4CE1F00208AD94F`、294 输入已获用户批准并执行一次。工具身份和完整 Windows 启动链已真实通过，但 pg_ctl 退出后继承管道不 EOF，报 `profile_run_tool_output_incomplete`；尚未 SQL／迁移／种子／七项测试，两账本为零。运行器自动精确停止实例，四工具／PG 清理确认、pidfile／端口消失，原数据和回执保留。见[A2 输出失败与自动收尾](2026-10-10-account-profile-a2-startup-pipe-failure.md)。A2 一次额度已消耗，修复后第三次真实验证需新的固定目录／清单及精确批准。离线及工具结果不代表新的昵称数据库业务、联合同步或门廊技术验证通过。

固定 pg_ctl start 改为不创建输出 reader Task、原句柄退出后显式关闭私有输出流；关闭失败与非零退出不放行，其他工具保留完整有界 EOF。离线反例 **138/138**，原生 Windows 未读私有管道关闭 smoke 通过，独立只读审查通过。A3 owner Release 构建零警告／错误，未批准门禁七项全部跳过、零失败。证据路径及 SHA 见[A2 后续修复记录](2026-10-10-account-profile-a2-startup-pipe-failure.md)。[A3 精确计划](../planning/2026-10-10-account-profile-postgres-a3-plan.md)及固定清单 SHA256 `5EA60013B4BF06F70F4E4F39293DCBA809F1A7D7D0C8F18613F347BAE296360D`、294 输入已获用户明确批准并执行一次。真实启动和专用输出流关闭通过；首个只读 psql 探针发生读取 Task 异常，未取得基线、未迁移、未执行七项业务测试，两个账本为零。旧错误分类不能证明实际超过字符上限。运行器自动停止 PG 与五工具，根实查无 PG／CMD／工具／pidfile／端口残留；全部原数据与回执保留。本次额度已消耗，见[A3 读取失败及收尾](2026-10-10-account-profile-a3-read-failure.md)。

A3 后续修复准确区分越界、I/O、解码、关闭、取消及其他读取错误；仅固定完整数据库证据允许 1 MiB 字符／UTF8 字节双硬限。三次完整快照各有同源 size-only 只读预检，含 CRLF 超限或测量后增长均不放行。完整 EOF、同步 reader 显式关闭与工具进程退出分别检查；pending 或 sticky 关闭失败不能伪装收尾，runner 单独记录并拒绝输出清理未确认。旧原生类型缺新 overload 时在工具创建前要求新宿主。

根最终纯离线 **237/237**，回执 `artifacts/account-profile-offline/b946e25a18f345ca8ec0e65f02143870/offline-results.json`，SHA256 `B7A9FDD3E9A97DD028E6FAB57410C228977353887A85E8BD6A43A263AD0EEE39`。同一最终脚本对输出收尾修复前候选为 **211/237**，新增26项全部失败；首轮 **236/237** 的一条不当测试预期及原证据保留，未因此修改正式实现。根四个真实 owned `pwsh` 子进程和实际匿名 pipe 两项 **6/6**，双流关闭与独立资源门禁均通过，未启动数据库或读取凭据。独立只读审查通过；A4 owner 已 Release 构建，关闭批准后七项实际结果均 `NotExecuted`。这些均非七项数据库业务通过。

[A4 精确计划](../planning/2026-10-10-account-profile-postgres-a4-plan.md)已获批准并执行一次。固定清单 `artifacts/account-profile-a4-approved.json` 的294输入、SHA256 `1A4189A44CD8D75BDF656B94032B54BA5D13EA710F0DFB7EB3AA70EECBF5F51E` 在执行前根与独立审查均确认匹配。完整只读 stdout 为831579字符／UTF8字节，大小预检为831577，两字节为CRLF，均在批准硬限内、EOF及双流显式关闭通过；随后报 `profile_run_database_target_rejected`。旧顺序没有保存被拒绝的六项实际字段，具体差异未确定，不能猜测或补查。未进入唯一迁移／七项测试，账本为空；PG／CMD／六工具／pidfile／端口已退出，进程与输出资源独立清理均确认。原数据、凭据、keys／代际、原回执和执行源码快照保留，见[A4 目标失败与收尾](2026-10-10-account-profile-a4-target-failure.md)。A4 额度已消耗，四份历史清单不可复用。

[D1 只读精确计划](../planning/2026-10-10-account-profile-target-d1-plan.md)最终同脚本红6/96／绿96/96，原237项回归237/237；根整合后的96项及237项各执行一次全通过。CreateNew清单 `artifacts/account-profile-d1-approved.json` 冻结36份输入、零缺失，SHA256 `472EE72B5EDA17AC49B4B90F5522475E9596B175AF67894E4DEEC304F52F33E8`；执行前根和独立复核全部匹配。用户已批准并执行一次，实际只读查询1、业务写入0、工具5，先保存七字段再比较。当前唯一差异是第六条 `20261009124814_AccessoryAccountBackup`，其余五项目标匹配，Profile迁移不存在；来源已从独立C2本地ref核实。PG／CMD／工具／pidfile／端口及双流收尾均确认，无残留。原数据、密钥和证据保留，独立复核通过，见[D1实际诊断](2026-10-10-account-profile-d1-target-diagnostic.md)。D1额度已消耗，不能续跑。

[A5精确计划](../planning/2026-10-10-account-profile-postgres-a5-plan.md)候选完成：核验当前精确六历史，将配饰新增四表纳入完整旧图保全，共22表；只应用Profile并执行原七项昵称验证一次。三个合规快照先保存后比较。最终同脚本红9/105／绿105/105，根105项全部通过；原237项首轮因新增五冻结源的旧输入数断言出现236/237，更新固定17并要求五源存在后237/237，原失败与脚本快照保留，正式实现未为此放宽。Release静态20项通过、关闭批准后七项真实用例全部NotExecuted，独立源码审查通过。CreateNew清单 `artifacts/account-profile-a5-approved.json` 冻结299份输入、零缺失，SHA256 `32A7489C276F328AB9A6419CD80A93D3F58F910F08A0F5091FFB1224A05C414F`，根与独立清单复核全部匹配，六历史／唯一Profile／七用例／67预算均确认。A5目录不存在、未预留或执行。真实资源及业务写入必须按新A5固定清单另行批准。数据库、联合账号／连续同步／门廊真实技术验证及主观验收仍分开，未合并、push、部署、安装或替换生产 DLL。
