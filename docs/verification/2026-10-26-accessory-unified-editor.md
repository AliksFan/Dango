# 配饰统一编辑器与双向骨架实现记录

日期：2026-10-26–27。工作分支 `codex/accessory-editor-rig`，起点与当前 HEAD `51caf0e7644ce89829e610a5ea50df2de41a898e`；本批修改尚未提交。

依据：[最新实施计划](../planning/2026-10-26-accessory-skeleton-implementation-plan.md)、[UI 合同](../planning/2026-10-26-accessory-editor-ui-contract.md)、[机制设计](../planning/2026-10-26-accessory-skeleton-mechanism-and-editing-design.md)、[ADR 0008](../adr/0008-accessory-shared-joints-and-unified-project.md)。用户明确要求实际 miniPaint 一体实现、左工具／右上图层／右下骨架属性，以及连接两侧物理互相影响；不再把独立 S0 验收当作产品接入前置。

状态：**统一产品源与两个实际消费者已接线，相关自动门禁通过；正式 Windows 前台、实时观感、最大负载与人工验收未完成。S7／产品未标记已验收。**

2026-10-27 后续：用户已批准新的画布主体／图标布局；右上调色盘、右下图层／骨架页签、独立向右收起与无滚动整组移位已实施，见[本版 UI 实现与验证](2026-10-27-accessory-canvas-ui.md)。下文是此前统一接线批次的历史证据，不作为新版布局说明。

## 已实现范围和责任

| 范围 | 维护源与实际职责 |
| --- | --- |
| 网页编辑 | `eng/accessory-editor/skeleton-commands.js`、`skeleton-input.js`、`skeleton-editor.js`：组／链／挂接、同点身份双向编辑、上下文与取消；复用 miniPaint `State.do_action`，图片和骨架共用历史 |
| 图像和布局 | `canvas-operations.js`、`restore-editor.js`、`unified-layout.css`、`editor.js`、`index.html`：原子整图变换、擦除恢复、透明扩展、左工具与右上下独立面板；打包入口不再引入旧 `geometry-editor.js` |
| 原生合同 | `Accessories/Editing/AccessorySkeletonConfiguration.cs`、`AccessoryEditorProject.cs`、`AccessoryBrowserProjectAdapter.cs`、`AccessoryEditorBridge.cs`、`AccessoryUnifiedEditorSession.cs`：独立校验、完整工程、版本与分块传输 |
| 编辑窗口／外层 | `AccessoryUnifiedEditorWindow.cs`：全宽浏览器和固定操作区；`AccessoryEditorPanel.cs`：名称内、佩戴外、连接／整体摆动默认折叠、四个叠放动作及草稿边界 |
| 物理与蒙皮 | `Accessories/Rig/AccessorySkeletonSolver.cs`、`AccessorySkeletonSkin.cs`：R/J 联合为共享粒子，距离／角度约束双向影响；弯曲、伸缩和组重力独立；有序多片蒙皮 |
| 真实消费者 | `AccessoryMotionSolver.cs`、`AccessorySceneController.cs`、`AccessoryHostBinding.cs`、`BeltBodyAppearancePresenter.cs`、`StackedDangoSceneRuntime.Accessories.cs`、`ProductHomeView.Accessories.cs`：每件一次 owner 推进，多片纹理及透明次序，Refresh 不推进 |
| 保存／试摆 | `AccessorySettingsSession.cs`、`AccessorySettingsStore.cs`、`AccessoryEditDraft.cs`：按不可变 item 修订取工程；未暂存的模态试摆工程可被两个消费者解析；取消恢复原草稿，外层应用才提交 |

原生源短路径相对于 `src/Dango.Desktop/`。旧单链类型／内部测试保留为兼容合同，不作为新版制作 UI 或缺工程时的静默回退。独立 S0 仅保留开发夹具与历史记录。

网页 schema/API 2、原生工程 version 2／`minipaint-v2`、桥 protocol 2、清单 schema 3、骨架 version 1。旧清单 v2／工程 v1 保留并写保护，无真实数据迁移或删除。

`Changed {editSequence}` 在 200 ms 内合并；Preview／Confirm 头携带所捕获修订，Open 头不含编辑序列。实际编辑取消旧导出，迟到的同序列 Changed 不取消有效导出。首次成功试摆后，编辑使旧试摆失效并排队一次后台刷新；错误不无限重试，关闭撤销待排队刷新。

## 实际自动验证

环境：已有 Node v24.18.0、锁定 Playwright Core 1.56.1／已装 Edge、.NET 10、WebView2 Runtime `155.0.4283.62`。无依赖升级、系统 Runtime 安装或浏览器下载；只用虚构素材与隔离目录。

| 层次 | 命令／归属 | 最后结果与证明范围 |
| --- | --- | --- |
| 静态资源 | `node build.mjs`、`node verify.mjs` | 6 个资源精确清单、散列、构建来源／依赖许可名单、local-only CSP 与无 CSS 外部资源通过 |
| 共享命令／输入 | `node --test skeleton-commands.test.mjs skeleton-input.test.mjs` | **30/30**；同点根／J 双向编辑与多选去重、实际 pointer／keyboard、挂接候选一致性、删除原子性、取消／修订／输入域 |
| 实际 miniPaint DOM／图像 | `node --test unified-skeleton.test.mjs` | **16/16**；真实图片与骨架统一历史、图层归组／新层插入、透明片顺序、异步操作排他、首擦恢复、变换／扩展原子性、布局和滚动 |
| 传输 | `node transport-tests.mjs` | 通过；Changed 合并、导出期间内容变化、迟到请求／取消／dispose、带 editSequence 的快照头 |
| 编辑兼容／图像预算 | `node smoke.mjs`、`node web-geometry.test.mjs`、`node budget-tests.mjs` | 通过；无 pageerror、schema2 重开、可编辑文字／笔画、翻转／裁剪／恢复像素、工具边界和资源拒绝 |
| 原生相关过滤集 | 下列 `dotnet test` 过滤集 | **96/96，0 skipped**；多组校验、双向受力、伸缩端点、owner、两消费者真实多片 raster、桥／会话、模态试摆、隔离存储重开、旧格式保护 |
| 最后网页资源再打包到宿主 | `AccessoryEditorBackgroundTests` 单独重跑 | **5/5**；实际打包资源在隐藏 WebView2 中加载，多块 Open／Preview／Confirm 和连接骨架往返，隔离外层 Apply／磁盘重开 |

5 项隐藏检查已包含在 96 项过滤集内，单独重跑不重复累加通过数。网页 16 项完整最终输出为 `artifacts/accessory-unified/unified-web-tests.txt`，共享输入输出为 `shared-input-tests.txt`，图像／边界输出为 `web-geometry-tests.txt`；原生 TRX 位于测试项目忽略的 `TestResults/`。这些是本机忽略证据，不作为产品资源。

原生执行入口（仓库根）：

```powershell
dotnet test tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj --filter 'FullyQualifiedName~AccessorySkeleton|FullyQualifiedName~AccessoryBrowserProjectAdapterTests|FullyQualifiedName~AccessoryUnifiedEditorSessionTests|FullyQualifiedName~AccessoryUnifiedEditorWindowTests|FullyQualifiedName~AccessoryOuterWearingPanelTests|FullyQualifiedName~AccessoryProjectStorageTests|FullyQualifiedName~AccessoryEditorBoundaryTests|FullyQualifiedName~AccessoryModalPreviewProjectTests|FullyQualifiedName~AccessoryLivePreviewTests|FullyQualifiedName~AccessoryEditorBackgroundTests' --artifacts-path artifacts/accessory-unified -p:WarningsNotAsErrors=NU1903 --logger 'trx;LogFileName=accessory-unified.trx' --verbosity minimal
```

命令保留既有 SignalR.Common 10.0.11 的 NU1903 高严重性依赖告警为 warning；没有修改项目告警策略或依赖来隐藏它。该告警仍是仓库遗留风险。

隐藏 WebView2 使用无 `WS_VISIBLE` 的 HwndSource，并断言 `IsWindowVisible=false`；没有 Show／Activate 或全局输入。它证明脚本、打包宿主和桥接，不能证明前台呈现／持续操作手感。含 Show／Activate 的其他桌面测试未在无屏幕授权时运行。

本轮发现并复现后修复的最后两项问题：

1. 原生保存重开内容一致，但骨架集合默认引用比较使 `AccessoryItem` 不等；为骨架／组补齐逐字段、节点／层顺序比较与对应 hash，保留原有重开断言，并新增字段变化反例。最终 96 项通过。
2. 840px 视口经历恢复像素操作后，左栏 `scrollLeft=8`，骨架工具左边界为 −3px；根因为沿用 miniPaint 横向换行 flex 加宽选项。改为纵向块布局并约束选项宽度，保留严格 `x >= 0`，完整图像／边界检查及 16 项通过。

旋转后重新光栅化的笔画以独立世界坐标控制点（误差 <1e-6）、一像素边缘带／无内部差异、面积和半像素质心门禁验证，不要求两次抗锯齿结果逐字节一致；双次可逆整图操作仍检查原始结果恢复。未把放宽镜像专用阈值作为修复。

## UI 规范与未验证项

采用桌面规范 v0.1（2026-10-10）第 2–6 节：宿主语义暖白／棕强调、细线图标和紧凑工具提示、参数数值／单位、固定提交区、右侧独立滚动与可中断折叠、画布缩放输入边界。页面差异是用户明确的左侧统一工具、右上图层／右下组属性；外层保留佩戴参数，模态窗口不复制原生独立参数页。窄工具列隐藏短字但保留图标、键位、辅助名称。

后台静态截图来自**实际打包的统一网页编辑器**，仅使用虚构枝叶图层／两组连接，不是另一套样板页面：`artifacts/accessory-unified/web-1280.png`、`web-840.png`、`web-528.png`。1280×850 和 840×750 已检查布局；528×320 是宿主最小外窗扣除边缘／底部后的网页空间探针，工具需要侧栏滚动，不当作原生整窗验收。

仍未验证：普通产品启动的前台操作与连续拖动、真实输入法和辅助操作、支持 DPI／高对比／减少动画矩阵、真实素材形变边缘、自然运动和佩戴观感、1 至 8 件最大负载／峰值资源／长时间运行。数值约束不承诺能量守恒；有界资源计数不等于性能通过。旧 E5 遗留不因新 S 编号消失。

此前约 5 分钟的独立 S0 屏幕授权已结束，窗口／服务器已关闭。2026-10-27 用户另行批准约 5 分钟的实际 Dango 统一编辑器试用；使用普通产品可执行文件和独立 `--data-root`，只复制先前虚构测试配置。已显示实际原生编辑窗口及 miniPaint WebView2，未占用日常数据目录。

本轮用户随后要求“先调整 UI”，并表示稍后提供调整方向；连接／佩戴验收留待 UI 调整后，没有给出通过结论。按此要求结束本轮试用，精确核对可执行路径及独立数据参数后关闭该测试进程；独立数据保留。当前等待具体 UI 方向，不自行扩展修改或沿用本轮屏幕授权继续显示。

## 交付状态

源码、生成资源、测试及本记录均在现有工作树；保留用户原有 README／设计／参考图改动，未修改 main 的 AGENTS.md。无新提交、main 合并、推送、安装、发布或正式库操作。测试使用的忽略 artifacts、独立配置／WebView2 profiles 作为证据保留，不清理真实保存。

本轮 headless 浏览器和临时 loopback 服务器均由测试 finally／after 关闭；隐藏 Runtime 通过 Dispose 释放。结束后按本工作树路径核对，未发现本轮残留进程或监听端口；未终止用户其他进程。最终资源 verify、`git diff --check` 与新增文本机器路径／冲突标记扫描通过，仅保留 Git 的既有 CRLF 规范化提示。
