# 门廊正式房间 CRUD C1 准备记录

日期：2026-10-11。状态：候选与离线检查准备完成，真实 C1 数据库旅程未执行，等待新的精确资源批准。范围见[执行计划](../planning/2026-10-11-porch-crud-c1-local-plan.md)。

## 本轮交付

新增独立 PorchCrudC1 测试入口、预算、请求范围、正式 HTTPS fixture、实际 Desktop 宿主、HWND／PID 限定 UIA 与真实按键旅程，以及外层 PostgreSQL 一次性执行和证据验证工具。本轮没有修改生产门廊／房间 CRUD 行为。

候选使用同一 native 运行时和团子 A／D 行走、W 互动；功能直接展开在门廊。建房、修改、解散、恢复分别要求版本 1／2／3／4 的正式 HTTPS 回执、独立 GET 与 UI 读回。外来账号读／改／解散／恢复均须 404；空名称、容量 13 和取消解散均须零业务写入。最终独立 SQL 验证注册图、撤销会话、房间／成员／审计／outbox 及旧 22 表摘要多重集合保全。

批准检查必须先于一次性目录预留和原状态读取；旧阶段 grant 无效。固定 21 次不可退还业务尝试、HTTP 128 次／Send 间隔至少 600ms、十个外层工具、一个正式 HTTPS／桌面宿主、六条资源事件。失败只收尾，不补跑或补查。SQL scope 由规范身份重生成全文，不能授权任意查询。

共享 Profile 工具只新增 C1 固定计划和输出合同分支。三项旧桌面断言按用户已批准的场景内门廊规则更新：目录页长 4、表单位于门廊、导航禁用且秘密清除；没有放宽正式写入、读回或归属断言。

## 已执行验证

所有路径均为仓库内隔离 artifact，未连接 C1 PostgreSQL 或读取原开发凭据。

| 检查 | 实际结果 | 证据 |
|---|---|---|
| C1 最终 Release 构建 | 成功，0 warning／0 error | `artifacts/porch-crud-c1-candidate/` |
| C1 C# 离线边界 | 5 Passed | `artifacts/porch-crud-c1-candidate/offline/c1-offline-final.trx` |
| C1 PowerShell 正反例 | 50 通过；DatabaseOpened／OriginalStateRead 均 false | `eng/test-porch-crud-c1-boundary.ps1`；`artifacts/porch-crud-c1-candidate/scoped-sql-review.json` |
| 真实 getter SQL 与 EF Model／migration snapshot 字段核对 | 2 Passed | `artifacts/porch-crud-c1-regression/offline/c1-sql-metadata-fixed.trx` |
| 门廊／房间目标桌面回归 | 48 Passed／1 NotExecuted／0 Failed | `artifacts/porch-crud-c1-desktop-regression/offline/porch-room-regression-fixed.trx` |
| 上述未执行按键项，独立进程窄复测 | 1 Passed | `artifacts/porch-crud-c1-desktop-regression/offline/porch-native-input-standalone.trx` |
| 共享工具旧 B1c2 离线回归 | 511 通过；UsesRealBackendResources=false | `artifacts/account-sync-b1c2-offline/84313b1f4a304543a56576a41f8028b3/offline-results.json` |
| 未批准正式 C1 测试入口 | 1 NotExecuted，0 Passed；独立 grant 前拒绝 | `artifacts/porch-crud-c1-candidate/offline/c1-unapproved-entry.trx` |
| 未批准外层 ExecuteApprovedPlan | 批准清单检查处拒绝：`porch_c1_approval_invalid` | 外层入口检查顺序及离线边界 |
| Git 空白检查 | `git diff --check` 通过 | 当前 C1 源码／文档 |

离线反例包括旧 grant、预算失败不退款、越范围请求、外来成功、缺失写后读回、错误请求号／版本、HTTP 间隔、伪造行走、资源收尾遗漏、额外字段、旧数据变更和 SQL 未知字段；正例通过不能代替真实旅程。

## 失败与未执行记录

初次桌面回归为 44 Passed／5 Failed：两项缺少隔离 native，三项仍断言旧居中房间浮层／页长。补入经哈希验证的 native 并按已批准门廊规则修正后为 48 Passed／1 NotExecuted。该按键项因 Windows 拒绝自有 HWND 前台激活而未执行，没有向其他窗口注入；仅在新独立进程复测此未决项后得到 1 Passed。原 NotExecuted 不追认为 Passed，原 TRX 保留。

初次 SQL metadata 两项失败源于检查器把邮件文字中的 `.invalid` 误识别为列引用。已修正字面量解析，未知字段反例仍可失败；原失败证据保留。

native SHA256 为 `B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E`，只复制到隔离测试输出，未替换生产 DLL。已核实本地 `main` 的 `a4acdeb`（子 Agent 模型选择规则）为当前 HEAD 的祖先。

## 五项检查与下一门禁

- 正确性：控件、异常输入、版本与读回已有离线可失败检查；正式端到端结果待 C1。
- 一致性：最终房间／成员／审计／outbox 与旧数据保全合同已准备；C1 不覆盖新增并发、丢响应、原请求重试或宿主中断，后续 C2 单独执行。
- 安全性：独立 grant、固定路由／SQL／目标、账号会话归属和私有证据有界；正式 B 越权拒绝待 C1。
- 可靠性：桌面／HTTPS／PostgreSQL／桥进程／临时 CNG 密钥收尾合同已准备；内部不可观察 DisposeTask 明确标为不可观察，不冒称 await。备份还原与服务中断演练不在 C1。
- 扩展性：目录、请求、任务、时间、输出和新增对象有界；不据此宣称负载能力或完整多人可玩。

遵循桌面规范 v0.1（2026-10-10）第 3、4.2、6 节：场景内布局、真实结果及明确解散确认、实际输入与分层报告。本轮无主题、图标或滚动规范例外；主观手感、效果、流畅度尚未验收。

下一步本地提交后生成一次性批准清单并验证全部输入哈希，取得 C1 精确授权后仅运行固定旅程一次。B1c2 已完成的一次批准不能授权 C1。准备通过不等于真实闭环通过，也不等于完整 B5／公共码／成员／准入／离房／多人／运维或生产部署通过。未合 main、推送、部署、更新日常安装或替换生产 DLL。
