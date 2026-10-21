# 新身体分支最终合并前核验

日期：2026-10-21。分支 `codex/belt-page-completion`。结论：按[正式接线与合并收口计划](../planning/2026-10-20-production-closeout.md)完成本轮最终核验，未发现本轮范围内的合并阻断项，等待用户明确授权本地合并。未合 main、推送、安装或部署。

## 基线与变更

- 已验收行为快照为 `9e0a6440a3d803ba049c57ef47ff6c6aedd5904b`；用户回复“通过”的范围及工程证据见[W/S记录](2026-10-21-balanced-ws-control.md)。原平台角部／推球修复的验收见[接触接入记录](2026-10-21-contact-metric-integration.md)。不重新扩大人工验收范围。
- 本次正式包从干净提交 `552d9bb3567e5a256b255cf422048e4f31abebb4` 生成。验收快照之后仅更新验收文档，并移除 `BeltNativeValidation.cs` 与旧休眠记录的文件末尾多余空行；`git diff -w --ignore-blank-lines 9e0a644 HEAD -- src eng tests` 为空，没有产品行为变化。
- 本轮未增加依赖、测试或产品逻辑；不重复历史物理／家具／Windows完整矩阵。桌面规范 v0.1 沿用已验收页面的第2～6节，本次无界面、主题或布局差异。

## 本轮实际验证

证据根为 `artifacts/final-closeout-2026-10-21/`；[机器摘要](../../artifacts/final-closeout-2026-10-21/evidence-summary.json)绑定包、清单、测试及 main 保护哈希。

| 检查 | 结果与边界 |
| --- | --- |
| 正式默认发布 | `eng/publish-desktop-entry.ps1 -RunName belt-final-verification-20261021-v2` 成功，win-x64 自包含、481项文件清单；未使用外部 native 覆盖或警告例外。日志 `publish-v2.log` |
| 安装器兼容预检 | 现有 `eng/test-desktop-publish-preflight.ps1` **10/10通过**。覆盖旧模块缺失／错误哈希／ABI与导出、受保护输入、默认发布无需旧模块、native源与产物绑定、源码及产物清单篡改拒绝、完整包校验，以及改写文件清单后仍缺少room模块的拒绝。仅检查隔离副本，未运行安装操作。结果 `preflight/summary.json` |
| 包与源码身份 | `sourceCommit=552d9bb…`、`sourceDirty=false`；设置合同 `dango-desktop-settings-v4-belt-default-v1`、native合同 `dango-belt-native-v1`。12项native源码哈希全部匹配，模块导出／ABI／能力通过现有门禁 |
| 已验包／测试产物一致性 | 正式包的两个native DLL与 `artifacts/balanced-ws-control/review-app/` 和 `obj/belt-native/Release/build/Release/` 逐字节哈希一致；独立只读核验同样通过 |
| 无窗口冒烟 | 正式包仅运行 `--smoke-test`，退出码0、进程已退出；该分支在实例协调器、host、托盘和窗口创建前返回，不读写用户设置。只证明基础托管启动与ApplicationIdle退出，不证明产品物理运行、界面或真实输入。结果 `smoke.json` |
| 分支与差异 | main `788456bf52a9d3d699c31a4c00cc0ea39b0c69fd` 是包源提交祖先，无分叉；该快照45项分支独有提交、无patch等价提交。`git diff --check main...HEAD` 通过；本报告提交只更新文档，不要求重建行为相同的包 |
| main现场保护 | 原有未暂存 `AGENTS.md` 修改保留，无暂存项；index、AGENTS和受保护DLL的SHA256与核验开始时一致，分支无AGENTS／旧DLL差异。未修改main工作区 |

正式包：[package目录](../../artifacts/desktop-entry/belt-final-verification-20261021-v2/package/)。`packageType=internal-candidate`，本记录不作为完整发布验收。

| 文件 | SHA256 |
| --- | --- |
| `package-manifest.json` | `422160C93BB3D2F1AEEC82C0710A6A2897D70977FDF613AEEDB20C376CA88112` |
| `belt-native-manifest.json` | `576AF191B7882154CBDA70C51592DB630FD0A7C3A5C3A13B16DDC12F2F7F49B8` |
| `dango_belt_product.dll` | `D60212F4DDAB01B6104A440F38C19D966184B58B899192F7C8090E79A932ECF7` |
| `dango_belt_room.dll` | `2CEDB643D0250FFBF152DFBAA76C9240A65CAD775546ED59CEBC92997C5C5EE8` |
| 受保护旧Box2D DLL | `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A` |

## 既有证据与未覆盖范围

最新受影响后台检查沿用 `artifacts/balanced-ws-control/focused-final.trx`：121/121、零失败、零跳过；本轮读取并绑定TRX哈希，**没有重新运行**。此前原生与接触负控、人工通过及历史失败仍按各自记录保留。早期独立审计 `audit/audit.json` 的快照为 `0796a68`，其中两项EOF格式备注已在 `552d9bb` 解决；未覆盖或改写该历史审计。

- Schema1间歇保存异常仍为 **UNKNOWN**；此前有界调查与1319次数据执行未复现，不等于修复，不继续盲目复跑。
- 两个旧小尺寸Face预览基线失败（480×270、853×480）继续单列保留，不在本轮扩展修复。
- 既有传递依赖 `SignalR.Common 10.0.11` 的 `NU1903` 尚未解决。既有定向测试使用命令级警告例外，不能称为依赖安全通过；本轮正式发布无需该例外。
- 本轮没有前台窗口、真实全局键鼠或新增人工体验；完整Windows／DPI／多屏／59 FPS／多人验收继续属于各自阶段。
- 目录整理、历史review模式退役与规则建设留给合并后的新分支，本次未实施。

## 交付与运行状态

本轮只形成正式包、隔离负控副本、日志及本地核验文档提交。所有既有候选、失败记录与冻结物理证据保留；未写入日常存档，未启动服务或监听端口，无新增遗留测试窗口、输入占用或冒烟进程。未操作用户原有窗口和进程。正式包绑定上述源码提交，后续文档提交不改变该来源。

下一步为用户明确授权后本地整合到main；执行前仍需重新检查main现场。推送、安装、部署和清理分别处理，不由本次核验推定授权。
