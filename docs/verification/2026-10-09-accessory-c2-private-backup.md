# C2 当前穿搭正式账号私有保存：实现与验证

日期：2026-10-09。状态：正式服务端、客户端和 Windows 入口的候选源码已接通，统一自动门禁及真实 PostgreSQL／本机 HTTPS 验证通过；**Windows 人工和双机未验收，C2 未标记已验收**。

## 范围与来源

用户明确“继续”，沿用[已确认业务规则](../planning/2026-10-09-accessory-account-backup.md)及[C2 实施范围](../planning/2026-10-09-accessory-c2-execution.md)。在独立 `codex/accessory-account-c2` 整合首批 `8eb9351` 和冻结 B5A `d14b22d`；没有在原 `codex/accessory-account-sync` 或正在实施的 B5 树改写共享文件。

- EF 实体／模型／迁移、资源配置、数据 fixture／runner 和统一验证由父任务负责；新增前向迁移 `20261009124814_AccessoryAccountBackup`，用户按精确计划授权后已应用到既有隔离测试库。
- HTTP 作者提交 `0b9fa6f`、`44930f1`，Server 作者提交 `3706627`，Windows 作者提交 `649ab9a` 顺序整合为 feature 提交；Windows 整合提交为 `19013fc`，之后的资源门禁及格式修订随本记录提交。
- 主要文件：`src/Dango.Server/Accessories`、`Hosting/AccessoryEndpoints.cs`、`Persistence/AccessoryAccountModel.cs`／实体／迁移；`src/Dango.Client.Sync/Accessories/FormalAccountAccessoryTransport.cs` 和 `Accounts/FormalAccountClient.cs`；`src/Dango.Desktop/AppShell` 的配饰账号控制器／面板及账号生命周期 partial；对应反例和 `eng/Verify-AccessoryC2.ps1`。

## 已实现行为

服务端新增四张独立表：全局规范化图片、本人图片引用、团子当前穿搭及幂等回执。图片必须实际上传并完成 PNG 验证／规范化，才能获得本人引用；同图物理去重、知道哈希或内容审核结论均不授予其他账号私有下载权。当前穿搭、引用转换和回执在同一事务提交，遵循既有账号锁顺序并在提交前重查会话和暂存租约。同键原请求重放原结果，异请求和版本冲突明确拒绝；缺少回执只能说明结果未知。

正式 Cookie／fresh CSRF API：上传 `POST /api/v1/me/pet/accessory-assets`、本人图片 `GET /api/v1/me/pet/accessory-assets/{assetId}`、当前穿搭 `GET/PUT /api/v1/me/pet/accessories`、回执核对 `GET /api/v1/me/pet/accessories/commits/{key}`。未保存时 GET 返回 204。身份来自当前会话，完整 JSON／PNG 均有预算；上传内存并发 2、排队 15 秒、读 body 30 秒，PNG 32 MiB、清单 32 KiB。

私有图片目录必须以 `DANGO_ACCESSORY_BLOB_DIRECTORY` 显式配置为仓库／密钥目录之外、已存在且无链接的绝对目录，不挂静态文件服务。未配置时配饰 API 返回不可用。暂存引用 24 小时，最多 16 个有效非当前引用；当前组合仍最多 8 件。替换组合立即终止该本人退役 current 引用的租约，不删除另一账号引用或物理图片。

客户端共用正式账号 owner 的 gate、Cookie、fresh CSRF 和登录代次，完整校验网络结果。认证失败、明确拒绝、冲突及未知结果分别处理；派发后的超时／会话退休不伪装为未提交。旧 owner Dispose 后等待 Drain，完成迟到的凭据清理前不能创建访问相同凭据槽的新 owner。

Windows 登录只读取账号基线；本机与账号来源明确选择，配饰版本和身体外观版本独立。本机 Apply／Delete 实际保存成功才自动排队，读取基线期间的本机提交等真实版本读到后再处理；关闭账号面板仍继续备份。云端失败保留本机；未知结果阻止刷新／重试／恢复，先核对原键；冲突需要重读后明确选择。恢复完整校验后一次提交，保留未佩戴库，并排除恢复再次上传和旧账号跨会话提交。

私有备份不自动申请审核。当前没有正式管理员角色、审核申请 HTTP、房间投影／撤销通知、举报或持久 GC；首批人工工具仍为本地原型，不能当作正式管理员服务。

## 实际自动门禁

最终执行 `eng/Verify-AccessoryC2.ps1`，使用已核验隔离 native module；所有统计来自本次实际 TRX，不叠加作者或早期运行。

| 门禁 | 通过／执行 | 失败／跳过 |
|---|---|---|
| 完整 solution Release 构建 | 0 warning / 0 error | 无 |
| Protocol | 19/19 | 0/0 |
| AccessoryAssets | 32/32 | 0/0 |
| AccessoryModeration | 22/22 | 0/0 |
| 全 Client.Sync | 193/193 | 0/0 |
| Server 非 PostgreSQL | 219/219 | 0/0 |
| Desktop 配饰／账号相关组及 UnifiedLiveHomeTests | 111/111 | 0/0 |
| .NET 去重总计 | **596/596** | **0/0** |
| runner 进程归属纯反例 | 10/10 | 不操作实际资源 |
| 另行授权的真实 C2 PostgreSQL／HTTPS | **27/27** | **0/0** |

全 Client.Sync 包括既有匿名、内存服务器的实际 WebSocket 回归；Windows 包括实盘本机提交／恢复和正式 transport＋受控 HTTP handler 的关闭面板后 Apply→PUT、503 保留本机等流程。上述通过不等于正式网络账号、数据库持久化、WinCred 实写或双机验收。

Windows 作者的 336×360 STA 门禁覆盖 Account／ConfigurationChoice 每个配饰按钮的水平范围和纵向滚动可达，四张实际 WPF 离屏 PNG 已检查。root 相关回归重新通过，不把离屏布局当真实 DPI／前台交互或用户人工验收。

本批 C# `dotnet format whitespace --verify-no-changes`、PowerShell AST、C2 dry-run 和 `git diff --check` 通过。第一次格式检查差异已修正，之后重新执行上述最终统一门禁。日志／TRX 在 ignored `artifacts/accessory-c2/final/`，作者反例保留在各自 ignored artifacts。

## 能失败的证据与审查修订

- HTTP 实际错误包包含 `code` 和 `correlationId`；原解析有 7 项红测。明确 429／408 与派发后未知分类有 2 项红测，Drain 缺失有 1 项红测；修订后最终完整组通过。
- 服务端规范化 signed zero，避免 PostgreSQL jsonb 将 `-0` 改写为 `0` 后破坏合法回执 digest。读取回执重算完整请求摘要；合法但被改写的配方不能冒充原提交。实际 HTTPS／PostgreSQL 的 signed-zero 提交、核对和重放已通过。
- 资源复核发现迁移前未核对实际角色／监听范围：提取原判断后 9 项反例中 4 项失败。已在迁移之前读取并核对数据库、完整数据目录、PostgreSQL 18、连接配置角色及 `127.0.0.1` 监听，再检查唯一允许迁移。
- runner 原收尾在未记录启动 PID 或目录只匹配前缀时可能误停；纯反例暴露 3 个错误放行。现在必须核对非零记录 PID、postmaster 第二行完整目录、程序、命令行目录边界和启动时间；无法证明归属只报告，10 项反例通过，未执行真实 stop。
- 两位独立代理只读审查 Windows 最终候选，第三位复核精确数据计划及资源修订，未留阻断。审查范围不等于全项目验收。

## 真实集成验证与遗留风险

用户明确“授权，继续，计划，此对话永久授权”，批准按[精确本地数据计划](../planning/2026-10-09-accessory-c2-local-data-plan.md)执行；此对话内同一计划范围不重复请求授权。旧 B1–B5A 批准仍不能启动 C2 fixture。

实际执行 `eng/invoke-backend-postgres-tests.ps1 -RunName run20261008b0 -C2 -ExecuteApprovedPlan`，指定已核验 PostgreSQL 18.6 工具目录。迁移入口报告成功，只前向应用本批四表迁移。**27 个案例（19 个方法含数据行）全部执行并通过，失败／跳过均为 0**；TRX 保存在 ignored `artifacts/accessory-c2/postgres-tests/20261009-140307-4834614/backend-postgres.trx`。

已验证真实数据库和私有图片存储上的本人授权、同图跨账号独立引用、暂存租约及 current 替换；两条不同 PostgreSQL 物理连接对同版本／同键竞争作出裁定；事务内受控异常和临界虚构时钟下，穿搭／本人引用／回执完整回滚。实际 HTTPS 上，正式客户端读取 Cookie／fresh CSRF，上传 PNG、提交完整测试穿搭；handler 消费成功 PUT 响应后主动抛错，再以原键核对并幂等重放。优雅停止／释放后在同进程新建 HTTPS host 和 DbContext，沿用原数据库／key ring／恢复代际／内存 Cookie，能读取已保存结果。

该组实际测试穿搭只有部分参数取非默认值，不表示全部非默认参数组合已穷尽；外观、身份和默认房不变的模块边界由其他门禁支持，这 27 项没有专门比较其前后数据库图。丢成功响应属于受控故障注入，宿主重建属于优雅停止后新建，不能代替跨机网络故障或进程强杀。

真实 Windows 前台／其他 DPI、系统凭据实写、最大八图表现、双设备登录恢复、正式审核／多人展示、撤销广播、服务进程强杀和备份还原均未验证。回执及过期引用现在物理保留，图片孤儿／增长和引用回收仍需后续策略及授权；没有以降低门禁或删数据处理这些边界。

## Git 与资源状态

本批只在 `codex/accessory-account-c2` 创建本地提交。原 `codex/accessory-account-sync@8eb9351` 保持干净，本地 main 仍为 `f10121b`。没有 main 合并、推送、部署、更新日常安装、替换正式 DLL 或删除 worktree／持久资源。

受保护 `dango_box2d.dll` SHA256 保持 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`；隔离 `dango_physics_module.dll` 为 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`，仅复制到本次构建输出。

本轮按计划启动既有隔离 PostgreSQL、应用唯一前向迁移并创建 `accessory-c2-blobs`。没有 SMTP、WinCred、Hub 或后台房间 worker。runner 正常停止自己启动的实例；检查本次 PID、记录端口、postmaster.pid、Dango.Server／testhost 均无残留。原系统 10 个 PostgreSQL PID 保持一致，未停止其他服务。

原 key ring 文件哈希、恢复代际状态文件哈希及受保护 DLL 不变；pgdata、测试虚构记录、工作树和日志保留。图片目录保留 37 个合成 PNG 文件，共 21,339 bytes，含受控回滚后孤儿文件；没有删除图片、GC 或还原。资源前后证据保存在 ignored `artifacts/accessory-c2/resource-baseline.json`／`resource-after.json`。本批不处理真实账号或非必要桌面内容。
