# 门廊 CRUD C1r2 修正与准备记录

日期：2026-10-11。状态：离线验证和候选准备完成，真实 C1r2 未执行，等待新的精确资源批准。范围见[精确计划](../planning/2026-10-11-porch-crud-c1r2-local-plan.md)。原 C1 失败、资源退出与未验证数据库项见[原失败记录](2026-10-11-porch-crud-c1-failed.md)，不追认为通过。

## 根因与修正

独立完整 DangoApplicationHost／B772 native／内存 HTTP handler 在原路径复现按 A 无法到达建房牌：Stage 已聚焦、无表单打开，但事件 Key 与 ImeProcessedKey 均为 ImeProcessed，Handled=false，方向指令 Left=false。X 从 559.99 到 328.14 属于闲逛变化，不能算玩家控制。生产修正仅为 gameplay Stage 禁用 IME，并在 EditorPanel／FaceEditorTools／AccessoryEditorTools／PorchOperations 明确保留 IME；账号输入在 Stage 外保持启用。没有修改房间 CRUD、身份或云同步规则。

IME 修正后 A 已成为 Handled=true／Left=true；旧“距离中心 160 小于 35”仍超时，最低 X=250.42 已落在建房牌的业务互动范围内。R2 按用户原目标改为生产 NearbyDoorId 确认真实目标，W 必须实际打开目标表单；没有改物理坐标／投影或增加远距离调用。每次行走新增方向指令投递断言及全过程前台 HWND 检查，保留位移>50、新健康帧、同一运行时／团子及后续业务结果要求，闲逛不能满足门禁。

房间排空检查改为在 Host.Dispose 前捕获原房间控制器及其 SemaphoreSlim，释放后检查同一原对象。离线故意保持原操作锁定时必须为 false，释放后才能为 true；宿主清空引用不能伪造排空。原内部 DisposeTask 仍明确不可观察，不冒称 await。

新 Stage 为 PorchCrudC1r2，独立 grant、一次性 `run20261011porchcrudc1r2` 和独立候选目录；原 C1／B1c2 grant、旧 C1 Stage 与旧批准清单拒绝。复用 C1 工具类型与函数名以避免重复代码，历史 C1 实现保存在原冻结提交，旧候选／清单／执行证据未覆盖。

## 实际验证

离线宿主全部使用内存 HTTP handler，未打开 PostgreSQL／HTTPS listener，未读取原凭据／密钥／状态，不操作 WinCred。正式 CRUD 端到端与最终数据库结果仍待新的 C1r2。

| 检查 | 结果 | 证据 |
|---|---|---|
| 最终 Review／Desktop／Server 三个 Release 构建 | 全部成功，均 0 warning／0 error | `artifacts/porch-crud-c1-r2-candidate/`、`porch-crud-c1-r2-desktop-regression/`、`porch-crud-c1-r2-server-regression/` |
| 实际完整宿主三目标行走／互动、IME 上下文、原操作排空和 C# 边界 | 8 Passed／0 Failed／0 NotExecuted | `artifacts/porch-crud-c1-r2-offline/offline-host-r2-final.trx` |
| 新固定计划与 PowerShell 正反例 | 51 通过，DatabaseOpened／OriginalStateRead=false | `eng/test-porch-crud-c1-boundary.ps1`；`artifacts/porch-crud-c1-r2-candidate/scoped-sql-review.json` |
| getter SQL 与 EF Model／migration snapshot 字段 | 2 Passed | `artifacts/porch-crud-c1-r2-offline/c1r2-sql-metadata.trx` |
| 受影响门廊／房间／账号／首页输入目标回归 | 70 Passed／1 NotExecuted／0 Failed | `artifacts/porch-crud-c1-r2-offline/c1r2-desktop-input-regression.trx` |
| 上述前台限制项的独立进程窄复测 | 1 Passed | `artifacts/porch-crud-c1-r2-offline/c1r2-native-input-standalone.trx` |
| 共享 Profile 的 B1c2 离线回归 | 511 通过，UsesRealBackendResources=false | `artifacts/account-sync-b1c2-offline/b96185c09b1c4ce9b06482ea3978226c/offline-results.json` |
| 未批准正式入口 | 1 NotExecuted／0 Passed，独立 grant 前拒绝 | `artifacts/porch-crud-c1-r2-offline/c1r2-unapproved-entry.trx` |
| 旧 C1 清单用于新外层入口 | `porch_c1_approval_invalid`；在预留和原状态读取前拒绝 | 当前入口批准检查顺序及独立实际拒绝检查 |

三目标离线旅程以真实 A／D 行走到建房牌、旧门册和内存目录确认的房门，W 打开实际控件；目录为空／当前目录重新读取和房门 GET 使用 FormalAccountClient 的受控 handler。它证明客户端、输入和门廊旅程，不能代表正式后端越权、CRUD 持久化或旧数据保全。

IME 文本检查覆盖属性启用和中文字符串；未将 UIA／直接赋值写成真实中文候选词键入已验证。受影响桌面组的原 NotExecuted 来自 Windows 拒绝自有 HWND 前台激活，无向其他窗口注入；窄复测通过不改写原记录。

## 保留的失败和构建记录

初次离线宿主 1 Failed／1 Passed，受控目录响应漏了严格模板字段，尚未进入行走；纠正 handler 后得到真正行走反例 1 Failed／1 Passed。IME 修正但仍用中心 ±35 的一轮同样 1 Failed／1 Passed，已收录实际按键投递与位置证据。首次使用业务互动范围的一轮 3 Passed，三目标扩展与最终新 grant 两轮均为 8 Passed。全部 TRX 保留，不将原失败或 Skip 覆盖成通过。

一次相对 ArtifactsPath 在七个项目内生成目录，转为绝对路径时导致重复 Assembly 特性编译失败。仅将本次七个生成目录移至经工作区绝对路径校验的 `artifacts/porch-crud-c1-relative-build-preserved`，没有删除或处理用户文件；后续全部构建使用统一绝对 artifact 路径并通过。

## 五项检查和阶段边界

- 正确性：原生按键投递、三个实际目标、IME 文本上下文及生命周期有真实离线反例／复测；正式写后读回待 C1r2。
- 一致性：未放宽版本、请求号、审计／outbox、旧摘要多重集合或不可退还预算；并发、丢响应、重试、中断仍属 C2。
- 安全性：新独立 grant／目录／Stage，旧清单拒绝，方向输入全过程限定自有前台 HWND；正式 B 越权拒绝待执行。
- 可靠性：原操作锁定反例阻止假排空；历史 C1 退出证据保留，新的正式资源合同尚未执行。备份还原／运维未验证。
- 扩展性：维持 21 业务尝试、128 HTTP／600ms、300秒进程、10工具、6资源事件、1MiB快照、目录页长4；不据此宣称负载能力。

遵循桌面规范 v0.1（2026-10-10）第 3、4.2、6 节，仍为门廊场景内功能、真实结果及明确确认。本轮仅修正输入上下文，未改变视觉主题／图标／滚动。主观手感、效果与流畅度尚未验收。

下一步本地提交、生成并核验一次性冻结清单，然后申请 C1r2 一次真实旅程。原 C1 批准已消耗，本轮未重新启动该 PostgreSQL。未合 main、推送、部署、更新日常安装或替换生产 DLL。
