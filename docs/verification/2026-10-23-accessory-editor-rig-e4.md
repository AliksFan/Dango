# 配饰统一编辑器 E4 接线、退役与宿主验证

日期：2026-10-23。用户明确授权进入 E4，并分别授权约 5 分钟窗口测试及约 2 分钟交接复测。本轮从 `codex/accessory-editor-rig@a49e2ac` 继续，依据 [实施计划 E4](../planning/2026-10-23-accessory-editor-and-rig-implementation-plan.md) 和开发流程第 13 节；承接 [E2／E3 记录](2026-10-23-accessory-editor-rig-e2-e3.md)。结果是本地实现与下述验证通过，尚未标记阶段已验收。

## 生产接线与退役

首页／编辑／门廊保持 `BeltBodyRuntime` 提供成功帧、`BeltBodyAppearancePresenter` 消费；房间／桌宠保持 `StackedDangoSceneRuntime` 推进 `AccessorySceneController`。共享工程、rig 和求解合同，各自的线程、world、帧 owner 与生命周期继续独立。宿主绘制、配置刷新和重复消费没有新增推进时钟。

| 对象 | 本轮结果 |
|---|---|
| 旧简单图片窗口与 Surface | 删除 `AccessoryImageEditorWindow.cs`；正式产品入口只打开统一编辑器 |
| 旧结果注入及外层参数分区 | 删除 `AccessoryEditorPanel.ImageEditor`、旧结果转换分支及 Connection／Rotation 分区残名；保留图库、名称／层次等条目操作、身体支点工具、整套应用／取消；运动和形变参数由统一编辑结果返回 |
| 旧整图形变字段 | 删除 `AccessoryItem.Softness`、`AllowFolding`、`MaxDeformationPercent` 及旧校验；生产配置只支持刚性／单链。新的 `Rig.Softness` 与网格 `AllowFolding` 分属骨架控制及纹理覆盖合同，继续保留 |
| 旧场求解及专属测试 | 旧 V4 参考求解、应变倍率、旧折叠开关、旧帧冻结散列与旧材质性能门禁退出当前测试；没有替换旧散列宣称等价 |
| 当前格式入口保护 | 隔离验收目录检查扩展到 `accessories-v2.json` 与 `accessory-projects`；拒绝清单名上的目录及资产目录名上的文件／重解析点 |
| 存档 | Schema 1／未知／损坏格式继续按 E1 保护；未转换、清空或删除用户库 |

检索 `src`、`tests`、`eng` 的 C#／项目文件，旧窗口／Surface、`OpenImageEditor`、旧 `ImageEditor` 注入、旧参数分区和 V4 参考求解器已无消费者。旧设计及验证记录保留历史语境。

## 验证结果

证据目录：忽略的 `artifacts/accessory-editor-rig-e4-evidence/`。测试素材、条目、配置目录均为隔离虚构数据。

| 验证 | 实际结果 |
|---|---|
| Release 集成选择集 `e4-final.trx` | **139／139 通过**，0 失败、0 跳过。覆盖当前工程／存储／删除、统一编辑器、隐藏 WebView、单链与共享连接／旋转、栅格透明与缓存、Belt 编辑预览、房间入口和当前格式保护 |
| 真实产品模块无窗口检查 | `AccessoryProductChainTests` 消费随包 `dango_belt_room.dll`，100 次实际身体推进；与裸身体逐帧比较，覆盖刚性＋单链、真实移动、Stop／Start、位置重宿主、两个只读 Canvas 消费、配置刷新和草稿先生效再取消恢复 |
| 独立二进制轨迹 | `e4-product-reference.trx` 与 `e4-product-candidate.trx` 各 1／1 通过。两个测试进程、不同 Desktop DLL、相同实际房间 native DLL；**930 帧完整输出摘要一致**。fixture 为一件刚性、两件单链，包含节点／参数、网格反向覆盖标志、UV／顶点／索引、根部、身体／附件／步数与真实输入输出 |
| 实际桌宠 HWND | `e4-live-hosts.trx` 的 5 项桌宠检查通过：前后层与旧帽替换、超出身体的纹理可见、原生显示区域、脱下移除、命中穿透及只读消费。此夹具使用离屏真实 HWND，不构成人工前台观感验收 |
| 正式产品交接 | `e4-product-handoff-final.trx` **1／1 通过**：实际首页 → 普通房间 → 门廊 → 桌宠入口 → 门廊／首页；三件单链配置持续生效，实体和 owner 对应，返回时首页 owner 恢复、房间 owner 释放，保存值不变 |
| 本地编辑器资源 | `node verify.mjs` 通过：6 项资源散列、精确清单、许可、来源／依赖白名单及本地 CSP |
| 最后编译 | Release 测试项目编译 0 错误；保留既有 NU1903 的 3 条告警 |

上述选择集与分工验证有重叠，不把子 Agent 的 8 项、32 项重复累加。独立参考文件为新 Schema 2，不覆盖旧 Schema 1 证据。参考 Desktop SHA256 为 `DC32FB8AC9F3BE630E2C122BF9110D9A2B11124819BC842345FE3A530CCC2AB1`，候选为 `8E3118A5E080CA88B460B79E7F3A20E9801E76FBA980066B2DFDDE7153A57EA9`；相同 native SHA256 为 `2CEDB643D0250FFBF152DFBAA76C9240A65CAD775546ED59CEBC92997C5C5EE8`。

独立轨迹实际向右移动约 165.12 DIP、向左约 142.45 DIP；跳跃 66 帧离地、29 帧上升、37 帧下降且落地，351 帧包含非恒等步态。它证明本次退役未改变已接通的产品输出，不证明人工效果或实时帧率。

## 失败、定位与修正

- 新房间对照最初 124／125 通过；失败来自两个独立 world 的 `OwnerEpoch/Generation` 不同。夹具仅归零这两个身份值，完整物理和呈现字段继续严格比较；随后当前选择集通过。
- 新清单目录反例先复现“应拒绝 `accessories-v2.json` 目录却未抛错”，见 `e4-startup-schema-before-fix.trx`；扩展原有入口保护后通过，不关闭格式保护或改写数据。
- 旧桌宠夹具转用当前产品模块后，5 项在旧矩形身体 guard 预期处失败，见 `e4-desktop-pet-window.trx`。核对生产 guard 使用真实连续身体包络，按 native bounds／core／呈现偏移迁移预期，保留原 1 像素容差和图片不可扩张命中范围断言，5 项复跑通过。
- 首次交接复测 5 项通过、1 项失败；夹具调用了不支持的“普通房间直接切桌宠”路由。产品桌宠有独立门廊入口，未更改产品规则绕过限制；改走正式入口并编译后，重新获得屏幕授权，交接复测通过。
- 独立轨迹初次仍指向旧模块名，未找到 `dango_physics_module.dll`；迁到正式房间模块后，在新文件中录制并验证 Schema 2。缺模块运行不计为通过。

NU1903 是既有 SignalR.Common 10.0.11 问题。本轮最终构建／测试保持审计开启，仅用命令级 `WarningsNotAsErrors=NU1903`；未修改依赖或仓库警告策略。子 Agent 曾作关闭审计的失败诊断编译，该结果不作为通过证据。

## 测试职责迁移与后续

旧编辑窗口的视图缩放、主题、数值缓冲及 PNG Surface 夹具退役，现有 UnifiedWindow／Session／Background 与网页几何测试负责新窗口合同。外层名称输入、草稿、依赖动作、图库、独立删除和固定操作区门禁继续保留；图库滚动改为真实条目溢出。需要显示窗口的外层焦点与布局矩阵没有在本轮全量重跑。

旧材质倍率、应变、形变上限及“不翻面”不作为单链合同；现有 RigMechanism／RigRuntimeContract 负责固定骨长、极端参数、零柔软度、重力和根部激励。共享的连接行程、角阻尼、回正、像素采样、透明接缝、反向覆盖和缓存仍有反例；新 rig 的性能测量归 RigRaster／Mechanism 和 E5 实际场景，不沿用旧 16ms 门槛。

桌面规范采用 v0.1 第 2–6 节。本轮没有新增页面布局；实际宿主测试验证呈现和生命周期，不能替代修正后编辑器的前台风格、完整绘图工具、输入焦点／滚轮、DPI／高对比／减少动画、8 件真实负载、正式发布包和用户人工验收。这些继续在 E5 完成，性能要求仍按获批目标另行确认。

窗口测试结束已关闭本轮窗口，检查未发现本轮 testhost、Dango 或对应 WebView2 残留。未新增服务、端口或持久外部资源；隔离测试目录和两个分工 Worktree 保留。本轮只有本地提交，无 main 合并、远端推送、部署、安装或发布标签。
