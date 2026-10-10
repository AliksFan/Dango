# B1a 登录同步候选与离线验证

状态：候选和离线检查完成；正式 PostgreSQL／HTTPS／完整可见客户端旅程尚未执行；用户主观验收尚未进行。

执行目标与精确额度见 [B1a 计划](../planning/2026-10-10-account-sync-b1a-local-plan.md)。A5 七项真实验证已通过且额度耗尽，本记录不重跑 A5，也不把旧批准移用到 B1a。

## 实现

正式 Desktop 增加内部 immutable 账号依赖接线，完整 `DangoApplicationHost` 复用正式 controller、真实文件 journal、保存事件与两秒 timer。只接受绝对文件路径、HTTPS 127.0.0.1 根 origin；未增加 CLI、设置或环境开关，原日常构造仍采用正式来源和 Windows 秘密存储。

新独立 Windows 测试项目、一次性 owner、短锁额度与 durable audit 固定19次业务尝试、512 HTTP、十分钟和原数据库63237，失败不退款。请求纯 allowlist 被实际 client handler 与 server middleware 共用。唯一旅程操作真实 HWND／UIA；三次完整 AppHost 先后启动、注销／离线改／登录选择、昵称修改和独立客户端顺序提交。native Ready、实际成功帧推进及首页帧驱动均为通过条件。

三份选择和三份同步 archive 逐份匹配原始本机字节、整份规范配置、账号身份、云端内容／版本与选择回执。运行器预留先于秘密及资源读取，核验冻结源码／候选、严格目标／进程归属和原22表完整保护，以19账本／10资源／单TRX／最终注册图联合判断。原 Profile A/D 工具仅扩展明确 B1a stage 及两个输出契约。

## 根实际离线验证

| 检查 | 实际结果 | 证据 |
| --- | --- | --- |
| 新预算、请求 policy、archive 契约 | 97 Passed、0 Failed、0 Skipped；Release 零编译警告／错误 | `artifacts/account-sync-b1a-csharp-offline/results/offline-account-sync-final.trx` |
| 正式完整宿主接线 | 7 Passed、0 Failed、0 Skipped；未 Start／未展示窗口／未启 native | `artifacts/host-account-binding/20261010-final/host-account-binding-final.trx` |
| 新固定计划／冻结输入／22表／账本／资源／TRX／跨模块／类型与格式 | 380/380 | `artifacts/account-sync-b1a-offline/0f7f5557b8f84627a08c9da5600a103c/offline-results.json` |
| 新运行器正式函数 AST 提取后实际 mock 调用 | 40/40，真实资源操作0 | `artifacts/account-sync-b1a-runner-offline-20261010/result-8ce954a0215c45ecbf42957f28c07c10.json` |
| 既有 Profile process／owner 边界回归 | 237/237 | `artifacts/account-profile-offline/c549e7e0cbd547f89106e03f35ef77ca/offline-results.json` |
| 既有22表／配饰保护回归 | 105/105 | `artifacts/protected-graph-offline/756f7ee937b547f7b86ca4b2e2dc6c19/offline-results.json` |
| 既有 target diagnostic 回归 | 96/96 | `artifacts/diagnostic-offline/a58b5c2834ab44998ed44e9b44e61a13/offline-results.json` |
| 删除全部继承 DANGO_BACKEND／PG 环境后，仅调用正式唯一旅程 | 1 NotExecuted、0 Passed、0 Failed；owner 首操作拒绝真实资源 | `artifacts/account-sync-b1a-csharp-offline/approval-closed/approval-closed.trx` |

新380项证据 SHA256 `3BAD0CBDC1488549FE78A7A6229ED5775AD83238D8D5A7175F842D6C9052C46C`。新40项证据 SHA256 `9886DDBA0110AC1586507B3568D9A3840EAA036AE0066AAC4FE7A4B8D60290F2`；正式运行器 SHA256 `78A0AFE9CC33E6F83E6271410F7972026FFEA81638DDC4C19DF98B2E26639337`。

宿主接线五种隔离错误实现已实际红灯，随后正式7/7：忽略 origin、忽略 client factory、丢保存 hook、丢连续同步 factory、错误 journal 分区。证据 `artifacts/host-account-binding/20261010-wiring/final-evidence.json`，SHA256 `C0E24302FB9D48520D7F0B7858219D346DCCF5532FF0C80CEDA1315263C0912B`。吞掉 audit 异常的隔离预算错误实现实际1 Failed；跳过 Journey 证据校验的 artifact 运行器实际拒绝断言失败并 exit1，之后正式40/40。均未降低正式门禁。

## 已纠正的问题

独立审查定位成功提交后调用方取消导致归属捕获丢失、主按钮辅助名称读取错误、仅存在实体却未确认物理帧健康、只数 archive，以及健康失败跳过收尾。修为独立有界成功响应捕获、实际 UIA Name、推进帧／驱动归属检查、精确六份存档与始终尝试各项收尾。

离线反例真实暴露23项 PowerShell 类型漏洞；完整 schema 反例暴露54项字段缺口。修复严格数字／布尔类型、Ordinal 操作与键、完整账本／Facts 字段集合和时间校验后，原反例及380项均通过。正式 C# owner 同样固定63237。

首轮相对 ArtifactsPath 生成项目内输出，改绝对候选目录前完整移存本轮生成输出至 `artifacts/build-path-correction/`，未删除源文件或业务数据。一次 archive 样本眼距50超出正式12–34范围，15项在样本构造时失败；改为合法24后97项通过。一次 host 测试 no-restore 使用新输出目录缺少 assets，正常还原现有依赖后7项通过；没有新增或升级依赖。

## 授权及未验证项

新资源目录尚未预留，未解密原 DPAPI 凭据、读取原密钥内容、连接／启动 PostgreSQL、生成 B1a 证书、启动 HTTPS 或调用完整可见 AppHost.Start。默认 review 只核验候选文件，缺失输入0。独立只读计划范围审查通过。

CNG DefaultKeySet 不保证一定得到预期用户 CNG 命名 key；实际断言在 Invite 前拒绝不匹配运行时。Dispose 的系统 key 删除可能吞异常，因此保留实际 `CngKey.Exists` 消失门禁，不能仅靠 Dispose 推定成功。此 native 行为仍须唯一真实旅程验证。原 key 目录 ACL 重申和可能 DataProtection 正常轮换已在计划准确声明。

下一步仅请求 [精确 B1a 计划](../planning/2026-10-10-account-sync-b1a-local-plan.md) 资源批准。B1b 异常恢复、多设备同时冲突、门廊业务、用户主观验收及生产部署准备仍分开。未合并 main、push、部署、更新日常安装或替换生产 DLL。
