# 邮箱后端 B2 配置关联与本地存档验证

日期：2026-10-09。分支：`codex/email-backend-design`，承接 B1 `066d10f`。状态：实现候选可审查，最终已执行门禁零失败；17 项因宿主条件未执行，人工尚未验收。B3 房间管理、B4 正式准入／Hub 与 B5 登录／配置选择界面仍未实现。

## 范围与结果

- Protocol／Server：严格七字段外观 Schema 3；正式本人 Pet GET／版本化 PATCH，仅更新配置和版本，账号 Pet 身份及完整房间状态不变。所有外部数据有类型、字段、版本、大小与有限数值校验；损坏持久配置不会返回默认值或被 PATCH 隐式修复。
- Client.Sync：固定 HTTPS 来源及 User／Pet 身份、独立 Cookie、禁止重定向、鲜活 CSRF 与有界响应。预览只读取；采用来源前持久归档本地原字节、云端原配置／版本及选择意图。原档与结果 receipt 分离，读取档案不自动重放请求。
- Desktop：真实外观文件 repository、dispatcher 草稿保护、原字节条件替换及保存成功后内存更新；保留既有 UTF-8／16／32 BOM 兼容。独立 `local-rooms.json` 保存普通房／桌宠房各自本地 ID、水平位置和有限相机设置，健康宿主定期及隐藏／切换／退出时保存；损坏、未知版本及外部修改保护原文件，写失败保留待保存状态。
- 宿主释放：修复终止释放后 Home 仍绑定已 Dispose runtime 的实际崩溃；普通隐藏仍可复用，终止释放清理绑定、订阅和过期回调。已处理退出保存决定后 Dispose 不二次写盘。

位置不上传、不保存按键／速度／跳跃／拖动／窗口内容，加载重新落地；越界位置回安全入场点。配饰继续独立，B2 不上传配饰／图片／物理数据。不新增数据库迁移或依赖包；Desktop 仅新增对既有 Client.Sync 项目的引用。

## 实际门禁

| 门禁 | 实际结果 | 证据边界 |
|---|---|---|
| 构建／格式 | Server.Tests 与 Desktop.Tests Release 0 警告、0 错误；改动范围 whitespace、PowerShell 语法、diff 检查通过 | 不是人工或性能验收 |
| Protocol | **9/9**，0 跳过（Debug） | 严格合同及原协议回归 |
| Server 非数据库 | **108/108**，0 跳过（Release） | 策略、配置、既有账号与管道回归 |
| PostgreSQL／HTTPS／受控 SMTP | **60/60**，0 跳过 | B0 8、B1 34、B2 18；真实数据库独立连接、真实 Kestrel HTTPS、本机受控收信，未发真实邮件 |
| 真实 HTTPS 客户端／选择／归档 | **1/1**，0 跳过；最终修复后复验同项 | 真服务接受首次 PATCH 后由受控 handler 丢弃响应；同版本重试、409 后 GET 只确认 TargetSatisfied；本地侧为明确 probe，真实 WPF 文件 repository 另测 |
| Client.Sync | **77/77**，0 跳过（Release） | 最终全量，包含 12 个新增不确定结果反例与两个 journal 错误码案例，不累加此前重复复跑 |
| Desktop 受影响静态 | **222 通过、2 未执行、0 失败，总计 224** | 含真实文件／dispatcher、相机持久化、宿主释放／退出防护；两个符号链接案例缺少系统权限 |
| Desktop Native 受影响组 | **14 通过、15 未执行、0 失败，总计 29** | 原生模块／WPF 实际宿主与场景；15 项前台相机输入未验证，不能算整组通过 |
| 桌宠房补强重启项 | **1/1**，0 跳过 | 实际拖动、松开、Hide 保存、Dispose 后第二新宿主加载；补强原组的预置位置加载项，不重复累计为新功能总数 |
| 跨进程条件写入补充 | 两个独立 dotnet 进程，20 轮每轮恰好一个替换成功 | 与同进程 12 writer 竞争互补，不计入 MSTest 项数；不代表非合作外部软件 CAS |
| 人工／外部系统 | **未验证** | 退出弹窗、Windows 产品观感、B5 登录／选择 UI、真实邮件投递、多客户端、灾备还原 |

主要最终 TRX 均在忽略的 `artifacts/`，保留失败记录，不提交测试存档或认证信息：

- `backend-b2/protocol-delivery/b2-protocol.trx`
- `backend-b2/server-unit/b2-server-unit.trx`
- `backend-foundation/postgres-tests/20261008-102512-4885017/backend-postgres.trx`（60 项）
- `backend-foundation/postgres-tests/20261009-014608-0978170/backend-postgres.trx`（首次 HTTPS 跨层 1 项）
- `backend-foundation/postgres-tests/20261009-024307-3391387/backend-postgres.trx`（结果判断修复后 HTTPS 跨层同项复验）
- `backend-b2/client-delivery/b2-client.trx`
- `backend-b2/desktop-static-bom-delivery/b2-desktop-static.trx`
- `backend-b2/desktop-native-after-release-fix/b2-desktop-native.trx`
- `backend-b2/desktop-pet-cold-restart-final/b2-desktop-pet-cold-restart.trx`

普通房 Native 项实际移动→Hide 保存→Dispose→新宿主读同房 ID／位置／手动相机；桌宠补强项实际拖动后保存→新宿主加载，并核对普通房检查点没有串写。首帧失败和 Dispose 保留旧检查点完整字节，越界安全落地也通过。新宿主均在同一 testhost 进程中创建，不能称为 Dango OS 进程强杀／冷启动或断电证明；桌宠拖动通过场景 driver 的真实物理路径，不替代 OS 鼠标 hook 输入验收。

## 跳过项与保留的失败证据

2 个 `ReparseTargetsAndAncestorDirectoriesAreRejected` 符号链接案例：宿主缺少创建测试符号链接的权限。真实目录 junction 拒绝及原文件保留案例已执行通过，但不能据此替代符号链接案例。

15 个 `RoomCameraWindowTests / NativeIntegration` 案例都在现有 `WaitReady` 前台门禁处明确 Inconclusive：精确房间 HWND 未取得 native foreground，输入准入／捕获未验证。包括手动二维平移、Follow／修饰键／左手势／native chrome 拒绝、捕获时模式切换，以及取消平移的 Mode、Zoom、InvalidFrame、Presentation、Resize、Hide、Menu、Dpi、LostCapture、LostKeyboardFocus、Deactivate、DriverStop 共 12 个分支。未绕过前台检查。TRX Counters 的 `notExecuted="0"` 与条目不一致，本报告按 15 条实际 NotExecuted 和 test runner 结果统计。

本批发现并处理：

1. 首轮客户端 3 项既有网络回归因未设置本轮 server executable 路径失败，补齐测试环境后通过，未改断言。
2. 首轮桌面 29 项因输出缺少 `dango_physics_module.dll` 报 MissingLibrary。只将既有已验 module 复制到忽略构建输出，没有替换受保护 DLL；修正构建目标后继续执行门禁。
3. Native 组暴露 `OnUnloaded → CancelAppearancePreview → SetAppearance` 访问已释放 runtime，导致 testhost 崩溃。旧实现精确复现后修复 terminal release，并通过 3 个新 managed 反例和既有真实 Native 隐藏复用项；根最终静态／Native 组已复验相关行为。
4. 审查发现首次 PATCH 可能已提交但响应丢失时，后续本地异常、第二次拒绝或 409 后读取失败会被误写为 Failed。新增 12 个反例在旧代码下 **12/12 失败**；修复保存不确定状态，后续不能确认的异常保持 OutcomeUnknown，停止不安全重试、保留原档；首次确定拒绝与可确认冲突仍保持原语义。最终客户端 **77/77** 通过。原失败 TRX 为 `backend-b2/choice-unknown-before-fix/b2-choice-unknown-before-fix.trx`。
5. 真实外观 adapter 补齐 4 个 UTF-16／32 BOM 兼容案例，验证原字节没有重写；规范色值在写回前转大写，保留共享合同对大小写的语义等价。130% 相机缩放的 IEEE 微小舍入边界纳入回归。
6. 桌宠补强测试首版误用桌宠明确禁止的方向键，断言失败如实保留；改用该宿主实际支持的拖动路径，未改变生产输入准入规则，最终保存／重载通过。

## 五项质量证据与边界

| 要求 | 本批可验证证据 | 尚未证明 |
|---|---|---|
| 正确性 | 严格 Schema／字段／有限值／版本、持久损坏拒绝、旧外观格式与 BOM 兼容、越界恢复、明确 Failed／Conflict／OutcomeUnknown | B5 UI／产品人工；17 个未执行案例 |
| 一致性 | email→User→Session→Pet 固定锁顺序、expectedVersion；独立 DB 连接同版本仅一方成功；提交前故障、过期和撤销回滚，Pet 与完整房间图不变；原档先落盘、原字节比较与合作 writer 互斥 | B3／B4 房间竞争；非合作应用、UNC／SUBST 别名及恶意目录替换的文件系统 CAS |
| 安全性 | 请求重新验证账号／会话／归属，外部 ID 字段与跨账号 session 拒绝；CSRF／Origin／严格 DTO；HTTPS 固定身份、无重定向、档案无凭据、虚构测试数据 | 正式 Hub 授权／广播、生产最小权限 DB role、真实收件箱 |
| 可靠性 | Flush(true) 后同目录发布、故障保留原文件与待存状态；丢响应不提高版本覆盖；receipt 与原档独立、重新读取不 PATCH；两个本地房间新宿主重载 | 实际断电、OS 进程强杀、设备丢失后的旧配置恢复、pg_dump→独立还原→旧安全状态失效 |
| 扩展性 | 一账号一 Pet 既有唯一索引、有界查询；配置 4 KiB、本地原字节 64 KiB、档案 128 KiB、HTTP response 16 KiB；网络 20 秒且最多 2 次同版本 PATCH；位置每 5 秒本地保存、不逐帧上传 | 真实容量、长时负载、档案保留与清理政策；未引入无证据的分布式复杂度 |

## 数据、进程与交付状态

仅复用先前批准的仓库外 `LocalApplicationData/Dango/BackendDevelopment/run20261008b0/pgdata` 与 `dango_backend_b0`，虚构 `.invalid` 邮箱／账号／团子及默认房。B2 无新增 schema；60 项中的既有 B0 回归幂等调用迁移 API 时没有待应用迁移。后续 `-B2` 工具要求既有实例／代际／密钥，不调用迁移命令，默认排除 B0 迁移组。没有重置、删除、还原或连接已有用户 PostgreSQL 服务。

测试实例与本次 HTTPS／SMTP 监听均已结束；数据库、DPAPI 凭据、B1 密钥与非秘密恢复代际以及忽略目录中的测试档案保留，未删除。用户既有 PostgreSQL 服务保持 Running。受保护 `dango_box2d.dll` SHA-256 保持 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`；仅测试输出使用既有 module `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`。

本轮只建立任务分支本地提交，`main` 保持 `1c47930`；未合并、推送、部署、更新日常安装或替换受保护 DLL。独立模块 Worktree 保留；客户端审查追加反例／修复由根接手并验证，原 Worktree 的相同未提交草稿保留，未自动清理。下一批 B3 处理房间创建额度、名称／容量更新、解散／七天恢复和到期任务；本记录不授权新数据库目标或灾备操作。
