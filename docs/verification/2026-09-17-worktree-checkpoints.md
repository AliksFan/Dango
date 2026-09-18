# 2026-09-17 工作树整理与本地检查点

> 2026-09-17 后续入口整合：本独立分支已将新眼帽接入普通入口，用户已确认第一批人工验收通过，见[第一批记录](2026-09-17-desktop-entry-integration.md)。下文未切换或未启用普通入口的表述保留为此前隔离预览及检查点阶段的历史，不代表本分支当前入口状态；原开发树与专项证据边界不变。

## 范围与保护

用户同意梳理既有改动并将已验收部分整理为本地提交。本轮没有新增产品行为，未改动任何原有源代码、测试断言或二进制字节；只分组暂存、验证、提交并校正文档状态。

起点为 `feat/p1-004-box2d-soft-body` / `ad33146`，39 个已跟踪文件修改、169 个未跟踪文件，共 208 个文件；暂存及冲突均为 0。全部文件的原始副本、SHA-256、状态和 binary diff 已保存在本机忽略目录 `artifacts/worktree-review/20260917-161228/`。它是本机恢复与审计材料，不作为 Git 已交付内容。

`main` 保持 `0610355` 且干净；其他 5 个旧工作树未改动。未合并、推送、部署、创建发布标签、删除工作树或清理旧证据。当前没有配置远端。

## 提交边界

| 本地提交 | 范围 | 边界 |
|---|---|---|
| `f18bc79` | native 单/双/三体模块、基础约束测试、无 WPF 托管 API/测试/示例，ABI 13 加载器、清单与原有正式 DLL，共 32 文件 | 原生算法和 DLL 原样保存；只暂存 CMake 中对应的模块和基础测试 target，未纳入联合求解实验 target |
| `c709fb7` | 桌面物理消费、眼帽表现层、两种宿主、预览工具、连续添加占位修复和相关测试，共 108 文件 | 普通入口不启用新眼帽机制；JointReview 的旧候选入口仍要求显式提供历史诊断程序，不进入当前运行路径 |
| 本文所在文档提交 | 当前状态与验收索引、模块说明、原有设计/调研/失败历史、工作树留存清单 | 文档入库不代表草案获批、失败候选通过或新功能实施；保留历史门槛、数值与证据损伤记录 |

早期“未提交”“待人工验收”等说明保留为当时记录；当前 Git 状态以本文为准，功能验收以各文档最新有日期记录为准。搁置的[单人动作草案](../phase-1-single-player-interaction-design.md)与[J3R 设计复核](../phase-1-dango-j3r-design-review.md)只归档，不恢复实施。

## 本轮实际验证

每批代码先导出暂存树，再在独立目录构建和测试，避免使用未暂存源文件补齐依赖。没有在旧人工预览或既有 native 构建目录中重建。

| 检查 | 实际结果 | 证据位置（上述本机证据根目录内） |
|---|---|---|
| 第一批 native 模块构建 | 成功；仅新目录输出，未调用覆盖正式 DLL 的构建脚本 | `group-1-cmake-configure.log`、`group-1-native-build.log` |
| native 约束及弹簧程序 | 两程序退出 0；约束程序含 0/0.5/1 三档各 36,000 帧；弹簧四种冷热/约束组合通过 | 对应程序位于 `group-1-native/Release/`；实际输出已核对 |
| 第一批导出树 .NET | Core 36/36、Desktop 125/125、Physics 32/32；无 WPF 示例构建成功 | `group-1-test.log`、`group-1-results/`、`group-1-sample-build.log` |
| 第二批导出树 .NET | Core 37/37、Desktop **303/304**；已包含连续添加占位修复 | `group-2-core-test.log`、`group-2-desktop-test.log`、`group-2-results/` |
| 评审工具与脚本 | JointReview Release 构建 0 warning / 0 error；两个启动脚本与 native 构建脚本语法通过 | `group-2-review-build.log`，脚本解析实际输出已核对 |
| 提交检查 | 差异空白检查、暂存树一致性、原有代码/测试/二进制哈希保护、文档链接与本机路径检查 | `baseline.json`、分组清单与最终核对结果 |

Physics 常规运行显式排除 `DangoPhysicsStabilityTests`，不声称完成长测。第一批针对本次独立构建的 native 模块，比较使用保留的历史 DLL；这些 fixtures 没有入库，测试不是无需 native 前置的纯净 checkout 自足门禁。尤其[原模块记录](phase-1-physics-module.md)中关于旧对照 DLL 被覆盖的证据损伤仍保留，本轮没有恢复或重新认证原始对照产物。

Desktop 唯一失败为 `SharedSelfWorldExportsAreAvailable`：旧 `dango_box2d.dll` 不含 `dango_box2d_self_bodies_create`。该失败与此前一致，测试未移除、跳过或放宽，也未通过替换 DLL 消除。上述本地提交不是全绿发布基线。

本轮未启动新的桌面预览，未重复人工视觉验收或完整 DPI/故障矩阵。眼帽双宿主人工通过来自用户在本日对已有预览的明确确认，见[原记录](phase-1-eye-hat-motion.md)。本轮自动测试不增加该人工结论的覆盖范围。

## 仍留在原工作树的内容

文档整理后保留 **1 个已跟踪修改和 25 个未跟踪源码文件**，全部为既有实验；不是遗漏的当前宿主依赖。

| 路径或文件组 | 数量 | 保留原因 |
|---|---:|---|
| `eng/native/box2d/CMakeLists.txt` 剩余差异 | 1 | 仅额外声明下述实验 target；原文件完整内容未改写，已验收 target 已单独提交 |
| `eng/native/box2d/dango_joint_*` | 11 | 联合接触、位置/速度候选、fixtures、诊断及预览；包含仍未通过的求解路线 |
| 同目录 `dango_contact_friction_evaluation.h`、`dango_dynamic_review_protocol.h`、`dango_ground_contact_evaluation.h`、`dango_ground_contact_tests.c`、`dango_node_edge_evaluation.h`、`dango_pair_ground_evaluation.h`、`dango_self_collision_tests.c`、`dango_self_sleep_tests.c`、`dango_stack_evaluation.h` | 9 | 历史接触、休眠、堆叠与诊断实验，保留原失败证据和来源 |
| `eng/tools/Dango.ShapeReview/` 的项目文件及 4 个 C# 文件 | 5 | 历史形状/动态评审工具；README 作为历史导航归档，工具源码继续保留在原工作树 |

没有清理未跟踪文件或忽略目录。若以后恢复这些实验，应按各自原有设计和停止点重新界定范围，不能因本轮代码检查点而推定获准推进。

## 当前功能与交付边界

- 当前仍为 Phase 1 / P1-004，完整 Phase 1 未验收，Phase 2 多人同步未开始。
- 已认可的眼帽视觉参数保持；正式默认未切换，独立 BounceHat、完整组合预算和降低额外动效仍未完成。
- 第二种 DPI、长期稳定性、完整故障矩阵维持未验证；旧预览进程未停止，原有设置、验收草稿及证据目录保留。
- 本轮未启动服务器、占用业务端口、创建数据库或写入种子数据。测试输出仅在本机忽略目录中留存。
- 正式旧 DLL SHA-256：`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。
- 既有统一模块 SHA-256：`00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`。
