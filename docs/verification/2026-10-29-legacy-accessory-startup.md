# 旧配饰库阻断首页初始化修复

## 后续本地 main 整合与启动（2026-10-29）

用户随后明确要求“合入main并打开”。核对双方工作区干净、main仍为原基线后，本地 main 从 `fd92bd5` 快进至修复及验证记录提交 `1e672a9`；本节状态随后作为文档提交补入 main。下方原修复轮次的授权和未交付状态保留为历史记录。

合入后从 main 重新构建正式 Desktop，0警告、0错误；使用正常入口和默认日常数据根启动，PID为10128，控制中心响应正常。只读 UI Automation 确认第1／第2栏、捏脸和换装均已启用，旧“团子资料不可读”保护提示消失，现显示预期的旧配饰库未载入提示。没有点击控件、操作全局输入或执行手工数据迁移／恢复；实际鼠标交互和人工观感仍待用户确认。

应用保留运行供用户使用；未推送远端、部署或更新日常安装，未删除分支、工作区或数据。

## 原修复轮次记录

日期：2026-10-29。用户在启动当前 main 后遇到“团子资料不可读或无法识别；原件已保护（InvalidDataException）”，随后授权开分支修复。本轮仅修复和本地验证，不合入 main、不推送、不更新安装或操作日常存档。

## 范围与根因

- 基线为 `main@fd92bd54debd5136357559acea627bc4923e8db2`，修复分支为 `codex/legacy-accessory-startup-fix`，代码提交为 `9229e05924262e5970fbafe519c13b7d6763dcea`；独立工作区保留其他分支既有修改。
- 仅有旧 `accessories.json` 时，`AccessorySettingsStore.Load` 按已批准规则返回 `LegacyNotLoaded`；`DangoRosterStore.Initialize` 却只接受 `Valid`，因此终止团子初始化，首页栏位、外观和换装入口随保护状态禁用。
- 继续遵循[配饰设计第7节](../planning/2026-10-23-accessory-editor-and-rig-design.md#7-数据格式与提交)的旧库处理：只识别已知 schema1 信封，不载入旧条目、图片或柔性效果，不转换旧库，旧文件和图片保留；独立新草稿经用户应用后按现有 roster 提交边界保存。

## 修复

`DangoRosterStore` 接受已知旧库的 `LegacyNotLoaded` 状态，以空配饰配置继续初始化，同时保留有效外观、设置及原有身份规则。首页现有反馈区显示“发现旧版配饰库，本版未载入”，重新读取时根据初始化来源继续提示。旧来源仍记录原始哈希并在提交前检查稳定性，不重新导入已存在的权威 roster。

未知版本、非法 JSON、异常根字段、重复字段及非数组条目信封继续阻止初始化。新 `accessories-v2.json` 存在时优先读取；损坏或未来版本的新库继续保护，不回退到旧库或空库覆盖。

界面沿用桌面规范 v0.1（2026-10-10）第4.2节的真实状态反馈及原反馈区；没有修改 XAML、主题、布局或焦点规则，没有新增页面差异或视觉例外。正式接入遵循开发工作流第13节，使用原存储与宿主装配，不增加实验入口。

## 正式发布合同

发布入口改用同数据代的 `dango-desktop-settings-v4-roster-v2-startup-v1`，保留原 `roster-v2` 及六个更早合同的源码哈希。新合同覆盖本次 Store 修复，以及基线 main 已合入的控制中心显示设置绑定；后者只有两行接线变化，不改变持久格式。同代双向切换、目录实例身份、原生包门禁和跨代降级保护均保持。

## 实际验证

| 层次 | 结果与证据 |
|---|---|
| 修复前反例 | 新增6个用例中3失败、3通过。旧库两种启动组合及正式首页宿主用例均失败，原码复现保护状态；`artifacts/legacy-accessory-startup/tests/legacy-startup-red.trx` |
| 定向回归 | 最终98通过、0失败、0跳过；覆盖 roster 存储／升级、正式宿主、配饰存储和首页栏位。新用例核对旧文件及图片字节保留、外观保留、创建／改名／保存／重读，以及旧信封和新库拒绝边界；`artifacts/legacy-accessory-startup/tests/legacy-startup-final.trx` |
| 正式宿主 | 无窗口创建真实 `DangoApplicationHost`／首页，检查栏位、捏脸及换装按钮可用，通过原绑定编辑器保存外观与配饰并重新读取；不是前台点击或人工观感验收 |
| 发布合同 | Windows PowerShell 5.1 fixture最终7/7；包括17份当前源码、逐份篡改与缺失拒绝、原合同冻结、同代切换和跨代降级前零写入。首次检查5/7，补齐基线既有 Host 绑定哈希后复测；两次证据分别保留于 `artifacts/legacy-accessory-startup/settings-contract/` 和 `settings-contract-final/` |
| 旧包 | 原 `accessory-editor-main-ae4d420` 候选的499文件仍通过当前 `Test-DangoPackage`，未改写或安装该包 |
| 正式候选 | `eng/publish-desktop-entry.ps1 -RunName legacy-accessory-startup-9229e05` 成功；499文件完整核对通过，源码提交为 `9229e05`、`sourceDirty=false`、win-x64自包含。`--smoke-test` 无窗口退出码0；`artifacts/legacy-accessory-startup/package-verification.json` |
| 静态 | `git diff --check`、三个改动 PowerShell 文件的语法检查通过。受影响文件的 `dotnet format` 仍报告既有格式／导入顺序问题；Store 的同项问题已在未修改的 main 上对照复现，未扩大范围整理 |

定向命令：

```powershell
dotnet test tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter 'FullyQualifiedName~DangoRosterStoreTests|FullyQualifiedName~DangoRosterUpgradeTests|FullyQualifiedName~DangoRosterHostTests|FullyQualifiedName~AccessoryStorageTests|FullyQualifiedName~DangoSlotHomeTests' --logger 'trx;LogFileName=legacy-startup-final.trx' --results-directory artifacts/legacy-accessory-startup/tests --verbosity minimal
```

常规测试还原首先被既有 SignalR 依赖 `NU1903` 阻断；沿用已有命令级 `NuGetAudit=false` 例外运行定向验证和发布，没有升级依赖或更改项目审计策略。未运行全量 Desktop／安装门禁；不将历史格式、依赖或安装夹具问题改判为通过。

## 交付与未验证项

候选目录为 `artifacts/desktop-entry/legacy-accessory-startup-9229e05/package/`；manifest SHA256 为 `1CC835B5005152069D88872E92A1D2DF19FA697920F63B11355FE6E5FFE2138C`。

本轮使用虚构临时存档验证，没有修改、复制迁移、重置或恢复日常存档。无新增应用窗口、全局输入占用、服务或监听端口；无窗口 smoke 进程正常退出。测试夹具按原有边界清理，构建缓存和候选包保留。本轮最终查询无运行中的 `Dango.Desktop.exe`；此前 main 窗口未由本轮代码操作关闭。

实现和后台／包验证完成，真实桌面点击、人工观感和日常存档实际升级未验证；未将阶段标记为已验收。未合 main、推送、部署或更新日常安装。
