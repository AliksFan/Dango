# 邮箱后端 B1 账号闭环验证

日期：2026-10-08。分支：`codex/email-backend-design`，承接 B0 `94eb81b`。状态：B1 自动门禁通过，候选待审阅；真实邮件送达、Windows 客户端和完整首期尚未验收。

## 范围与交付

- `Accounts/`：受控维护邀请、先验证后注册、继续秘密与完成幂等、Identity 密码、持久会话、锁定、找回和改密。注册同事务创建账号、唯一 Pet、默认桌宠房、房主席位和归属，任一步失败整体回滚；完成不发 Cookie。
- `Hosting/` 与显式入口：正式 HTTPS loopback 模式、迁移 readiness、框架 CSRF／Origin、严格 JSON／请求上限、每请求会话校验、受控邀请维护命令。普通启动不改库，正式端口不映射匿名原型 Hub，原管道合同保持。
- `Email/`：限时 Data Protection 证明，MailKitLite 4.18.1 严格 TLS SMTP、持久队列，每批 10 项、最多 5 次实际尝试、3 分钟租约、有限退避和脱敏失败状态。未配置 SMTP 保留待发状态；正文／原始证明不入数据库。
- `Persistence/`：前向迁移 `20261008085521_AccountLifecycle`，五个账号生命周期表及禁用／恢复锁定字段、外键／检查／唯一索引；原 B0 迁移与 probe 保留。
- 工具、反例和[操作说明](../server-email-operations-v0.1.md)。共享合同、邮件、会话密码和测试通过独立 Worktree 分工，再按提交顺序集成本任务分支。

没有实现 B2 客户端配置选择、本地新存档、B3 房间管理、B4 正式准入／Hub 或 B5 登录界面。配饰继续独立，B1 注册只接受现有外观 Schema 3。

## 实际验证

| 门禁 | 最终结果 | 证据边界 |
|---|---|---|
| 构建／静态 | Server／Server.Tests Release 0 警告、0 错误；改动范围格式、差异检查通过 | 不等于产品人工验收 |
| 模型／迁移 | 幂等 SQL 生成成功、无模型漂移、正式前向迁移明确执行，重跑保留已有数据 | 未执行回滚或灾备还原 |
| Server 基础门禁 | **102/102**，零跳过 | 66 项新增输入／证明／邮件政策／会话／配置门禁及原 36 项；含独立真实 TLS 握手反例，不能当账号端到端证据 |
| PostgreSQL＋HTTPS＋SMTP | **42/42**，零跳过 | 34 项 B1 与原 B0 8 项；真实 PostgreSQL 独立连接、真实 Kestrel HTTPS、真实 MailKit→本机受控接收器→内存证明→API→DB→响应 |
| Client.Sync | **30/30**，零跳过 | 使用本轮 Release server executable，原型真实 WebSocket 回归；不是正式持久 Hub／双 Windows M7 |
| Protocol | **6/6**，零跳过 | 既有协议回归 |
| 人工／部署 | 未进行真实收件箱投递或 Windows 账号 UI 验收；未部署 | 本机 API 与受控 SMTP 不代表真实邮箱送达 |

去重共 **180 项通过**（比 B0 增加 100 项），重复复跑不重复计数。静态 TRX 位于忽略的 `artifacts/backend-b1/static-tests/`；最终数据库 TRX 位于 `artifacts/backend-foundation/postgres-tests/20261008-092559-2661338/`，同目录保留此前失败及修复后运行证据。幂等 SQL 位于 `artifacts/backend-b1/account-lifecycle.sql`。

## 能失败的主要反例与修复

1. 输入测试先发现非数值 Schema 抛框架异常和颜色正则误接受尾部换行，修正类型判断与绝对末尾锚点后通过；重复／未知字段、无穷／范围、密码和继续秘密边界拒绝。
2. 并发完成使用独立连接与同步起点，固定完成键仅生成同一套对象；不同邮箱规范化、邀请撤销／到期、未验证、篡改／跨用途／旧 generation／一次消费及 10 分钟完成窗口均有反例。每个初始化步骤注入故障，以及提交前推进到期限，均从新连接核对没有半套账号／Pet／房间／归属，也没有误消费邀请。
3. 完成响应丢失后同键重试返回原 IDs，不修改原密码／外观，不建会话；更换继续秘密／完成键／恢复代际拒绝。数据库唯一冲突映射稳定 409，不输出 PostgreSQL 内容。
4. 真实 HTTP 检查缺 CSRF、不同 CookieContainer、无 Origin 的原生请求、外来 Origin、登录后旧 CSRF、严格 DTO、Secure／HttpOnly／SameSite Cookie。本人会话列表／撤销、旧 Cookie 重放、8／168 小时绝对期限、五次失败十五分钟锁定、改密／找回撤销其他设备、禁用／恢复锁定不可绕过均通过。
5. 初轮真实测试 **25 通过、10 失败**：九项 HTTP 卡在 Windows TLS 握手，一项跨用途测试构造缺少 reset 安全戳。独立真实 TLS 反例证明 Windows Schannel 拒绝 EphemeralKeySet，RSA 是否提前 Dispose 不改变失败；正式入口与 fixture 改用 DefaultKeySet、不使用 PersistKeySet、Dispose 释放本次 native key container，PFX 缓冲清零，仍固定验证证书且不安装信任。合法跨用途挑战由注册接口拒绝。修复后 35/35，再补充安全／恢复门禁后最终 42/42。
6. SMTP 451 真实接收器拒绝最多尝试五次并持久记失败；未配置和非目标虚构队列不伪报送达。真实 SMTP 接受后注入取消，使结果未写入而租约保留；重建 dispatcher、租约到期后重复发送，同挑战仍只消费一次。
7. 同库多次真实停止／重启保留 B0 固定 probe。B1 实际销毁／重建应用宿主后，旧有效 Cookie、会话和未过期证明仍可验证。这里证明宿主／队列重建，**未执行 B1 进程强杀或灾备还原**。
8. 审查发现用 CWD 识别仓库可能让仓库内 key path 漏过校验；改按程序真实 checkout／安装位置再次校验，纯配置反例通过。

Windows TLS 官方依据：[.NET 10.0.12 Schannel 源码](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Net.Security/src/System/Net/Security/SslStreamPal.Windows.cs#L258-L263)。新增 SMTP 依赖版本／许可证与官方资料见[操作说明](../server-email-operations-v0.1.md)。

## 五项质量与未验证项

| 要求 | 本批证据 | 尚未证明 |
|---|---|---|
| 正确性 | 严格输入、明确用途／状态／期限、SMTP 状态区分和 stable errors | Windows UI、真实邮件送达 |
| 一致性 | 数据库约束、固定锁顺序、同库事务、独立连接竞争、逐步故障回滚、固定幂等结果 | B3／B4 房间领域竞争及迁移 DDL 故障注入 |
| 安全性 | 每请求持久会话与安全戳、CSRF／Origin、本人数据边界、恢复锁定、秘密不存正文／日志，测试全为虚构数据 | 正式 Hub 命令／广播、受控恢复审批和生产 runtime 最小权限角色验收 |
| 可靠性 | 持久 key ring／挑战／会话／队列、真实宿主重建、租约恢复及重复邮件一次消费；独立 DB 正常停止／重启 | B1 OS 进程强杀、pg_dump→独立还原→旧凭据拒绝、专用账号恢复流程 |
| 扩展性 | 有索引，邮件批次 10、会话活动上限 20／列表 50、请求／超时／重试有限 | 真实容量、长时负载、历史数据保留／清理政策 |

恢复锁定守卫已实现，真实备份还原及审批解锁流程仍需精确目标和单独授权，不能以普通找回代替。当前配置适配 Windows DPAPI；其他平台尚须等效保护。首期仅 loopback，本轮不是公网部署准备度结论。

## 精确资源与 Git 状态

用户另行批准[本批数据目标](../planning/2026-10-08-backend-b1-local-data-plan.md)。仅复用当前用户 LocalApplicationData 的 `Dango/BackendDevelopment/run20261008b0/pgdata` 与 `dango_backend_b0`，不连接／更改／停止既有 PostgreSQL 服务。前向迁移前额外核对实际数据库、PostgreSQL 18 和物理 data_directory。

最终测试工具已停止本次 PostgreSQL 实例；测试宿主／SMTP 监听按 Dispose 关闭。数据、B0 probe、DPAPI 凭据、`b1tests-keys`、非秘密 `b1test-state.json` 与日志保留，未重置／删除／还原。三个分工 Worktree 保留，不擅自归档删除。

成果仅为任务分支本地提交，未合并 main、推送、部署、修改日常安装或替换 DLL；B1 候选与完整 Phase 3／M7 均未标记已验收。
