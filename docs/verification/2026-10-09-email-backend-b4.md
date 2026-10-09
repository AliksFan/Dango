# 邮箱后端 B4 集成验证

日期：2026-10-09。任务分支 `codex/email-backend-design`；用户按精确数据计划授权后，B4 两个前向迁移与真实 PostgreSQL／正式 HTTPS／Hub 自动集成已完成，最终 **168／168 通过，失败／跳过 0**。客户端与人工门禁仍未完成，**不标记 B4 或完整首期已验收**。

## 范围与实现

- 正式房间准入、公共码 resolve／reset、移出／封禁／解除封禁，私密及桌宠首期仅房主，手机不能进入或完整观察桌宠房。
- 30 秒设备租约、来源／归属／控制代际、显式接管、短断线 attach、个人及有限房间快照。跨房新 ID 保持兼容观察设备的原期限；旧请求不能清新归属／新传输。
- 独立认证 Hub 与 HTTP 适配，每命令服务端核验；事务内 outbox、每收件人重新授权并在有界发送期间保持撤权 gate，允许重复刷新提示。
- B3 解散／到期清控制、终止租约、撤码；到期删除新 restrictive FK 依赖。租约身份及控制所有权增加复合 FK；公共码明确恢复代际。
- 有界连接／频率／设备／快照／预扫／批次；无效会话连接可被淘汰，维护及通知失败游标推进。所有生产 live／expiry worker 默认关闭。

具体锁序、独立到期裁定与请求回滚边界见[B4 实施细化](../planning/2026-10-09-backend-b4-execution.md)。准入预扫可能独立提交其他已经到期团子的正确下线；失败的主准入本身不能留下新归属或收据。

## 已执行

| 门禁 | 结果／证据 |
|---|---|
| Release Server 与测试源码构建 | 0 警告／0 错误，真实 PG／HTTPS／Hub 测试源码包含在构建内 |
| Server 无数据库测试 | 修复后 186／186 通过，失败／跳过 0；`artifacts/backend-b4/static/b4-server-static-receipt-fix.trx`；原候选 183 项记录保留 |
| Protocol 回归 | 9／9 通过，失败／跳过 0 |
| Client.Sync 回归 | 77／77 通过，失败／跳过 0；`artifacts/backend-b4/static/b4-client-regression.trx` |
| 模型／迁移源码 | 五个迁移与最新模型一致；真实 Npgsql metadata／生成 SQL／复合 FK／检查／唯一索引的无连接检查通过 |
| 资源工具 dry run | `-B4` 不带执行开关，准确打印既有目标与两个前向迁移范围；未启动／连接实例 |
| 源码独立审查 | 核实并修复 tracker 回滚残留、期限取时、外键锁死锁、旧租约结束、公共残留占位、失败项饥饿及观察设备迁移；源码审查不能代替真实竞争结果 |
| 修复前真实数据库反例 | 新精度用例明确失败：响应比数据库截止时间多 7 ticks；`artifacts/backend-foundation/postgres-tests/20261009-072057-9666185/backend-postgres.trx` |
| 公共房修复定向集成 | 24／24 通过，失败／跳过 0；`artifacts/backend-foundation/postgres-tests/20261009-072228-2840160/backend-postgres.trx` |
| 完整 B1–B4 集成 | 168／168 通过，executed 168，failed／error／timeout／aborted／inconclusive／notExecuted 均 0；`artifacts/backend-foundation/postgres-tests/20261009-072420-8788512/backend-postgres.trx` |
| 最终构建与格式 | Release 0 警告／0 错误；修改文件格式与 `git diff --check` 通过 |

客户端回归首次遗漏 `DANGO_SYNC_SERVER_PATH`，74 通过、3 原型网络用例因测试配置失败；指定本轮构建的独立原型服务 exe 后重跑 77 全通过。原型网络回归不证明 B4 正式多人功能。

实际前向应用 `20261009055558_RoomAccess` 与 `20261009062701_RoomAccessIntegrity`，后续重跑复用已应用结构，没有 Down／重置／还原。测试先检查独立 B4 授权开关，再创建 fixture；自定义 filter 与 B4 许可范围相交，不能重启 B0 probe 写入。详细范围见[精确数据计划](../planning/2026-10-09-backend-b4-local-data-plan.md)。

## 失败、定位与修复

首次完整集成 167 项中 164 通过、3 失败，跳过 0；证据保留在 `artifacts/backend-foundation/postgres-tests/20261009-070624-4849481/backend-postgres.trx`。

- 公共码回执重放及同请求并发重放失败：.NET 时间精度为 100ns，PostgreSQL timestamp 保留微秒；JSON 回执保留第七位而实体重载截断。创建码前统一 UTC 微秒，使响应、实体和收据完全一致；继续严格比较回执，不放宽校验。新增真实 PG 用例在错误实现下测出 7 ticks 差异，修复后验证精确一致、确认重放及原码不入成功准入审计／outbox；单元用例覆盖 0／1／9 子微秒 ticks。
- 首次封禁 NOWAIT 冲突：实际 EF／Npgsql 将 `55P03` 包装成 `InvalidOperationException` 的直接 inner exception。仅增加该确定包装的映射，返回 `room_moderation_conflict`／409；其他异常保持失败。原独立连接锁竞争用例通过。
- 测试图比较改为对全部原字段的排序序列化结果作 SHA256，保持回滚比较能力，断言失败不会输出凭据密文或收据。原码安全检查直接检查实际审计／outbox，不对摘要作无效的字符串检查。

新增精度诊断最初两次停在 `email_proof_invalid` 注册夹具，第二次因 runner 的 `--no-build` 仍使用旧产物；这两次不能作为精度反例，原 TRX 保留在 `20261009-071434-8323129` 与 `20261009-071915-6679417`。之后调整为正常整秒注册／建房，再把测试时钟推进到明确的第七位 7，并在每次源码变化后先 Release 重建，得到上表有效反例与通过结果。

## 五项实际证据

| 质量 | 本轮实际通过与边界 |
|---|---|
| 正确性 | 正常／异常准入、旧来源／控制／transport、手机桌宠限制、公共码重放／期限、过期占位及实际复合 FK；响应时间与 DB 精确一致 |
| 一致性 | 独立连接最后席、迁移、准入与解散、首次封禁锁竞争；提交前故障回滚；B3 解散／恢复／限定永久清理实际处理新增依赖 |
| 安全性 | 正式 HTTPS／Hub 匿名和跨账号拒绝、真实撤销会话和旧 transport 失效、逐收件人授权／撤权与发送串行、实际审计／outbox 无原码 |
| 可靠性 | 新正式宿主读取同库持久状态、重 attach、观察设备保持原期限并以新 ID 恢复、发送成功但发布标记失败时相同事件 ID 重试；不是进程强杀或备份还原证明 |
| 扩展性 | 设备／连接／频率／快照／任务有界、失败游标推进、前向迁移和必要索引；没有生产吞吐证据 |

## 未验证与边界

- 正式 Hub 已使用真实 SignalR LongPolling 测试；正式 WebSocket 传输仍未验证。outbox 注入与独立连接竞争门禁通过，不代替真实长时间生产运行。
- 真正进程强杀、长期生产后台维护、WebSocket 正式传输、真实备份还原、真实邮箱投递、完整 Windows 多客户端和人工验收均未验证。
- B5 登录／配置选择／房间界面未实施；没有把候选代码或原型测试记作完整首期、Phase 3 或 M7 验收。
- 只验证本机有界实现，不承诺生产容量；迁移采用前向流程，不能因生成了 Down 方法就认为已有 B4 数据可安全回退。

## 环境与交付

仅启动精确计划中的既有独立 PostgreSQL；每次 runner 均在 finally 停止本次实例，最后一次已正常停止。最终检查 `postmaster.pid` 不存在、本次 PID 不存在、记录端口监听为 0，`Dango.Server.exe`／testhost 残留为 0，正式 host 和受控 SMTP 随测试宿主退出；原系统 `postgresql-x64-18` 仍 Running，未连接或操作。限定永久清理只删除新造测试房 ID 及其依赖业务行；数据库、剩余虚构数据、密钥、凭据保护文件和恢复代际文件保留。未采集旧全库逐字段前后快照，不宣称每一旧字段完全不变。

`main` 仍为 `f10121ba7acd0d0df79b9a31acf6f481949230b3`，候选保存在隔离任务分支并允许本地提交；未合并／推送／部署／更新安装。受保护 DLL SHA256 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。B2 用户既有草稿 worktree 未清理或改写。
