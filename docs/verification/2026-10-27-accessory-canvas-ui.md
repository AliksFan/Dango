# 配饰编辑器画布布局调整

日期：2026-10-27。分支 `codex/accessory-editor-rig`，HEAD `51caf0e`，全部为未提交工作树修改。用户已确认本版布局并授权本地实现；没有合并、推送、安装、发布或新的前台屏幕授权。

## 采用的设计

依据[最新 UI 合同](../planning/2026-10-26-accessory-editor-ui-contract.md) UI-03／05／08、[Reference image omitted from public source: redistribution permission is not recorded.]和桌面规范 v0.1（2026-10-10）第 2–6 节：

- 画布铺满工作区，左侧图片／骨架／视图工具为 32px 图标，保留名称／键位提示、焦点和选中态。
- 悬停／键盘聚焦工具时，左下单行显示用法；移开恢复当前工具用法。名称、真实参数值／单位和错误继续用文字。
- 右上紧凑调色盘，右下更大的图层／骨架页签面板；两块向右独立收起，只留恢复图标，露出被遮挡画布且不改查看姿态。
- 右侧工具／参数无滚动；较短窗口整套图层数值或骨架参数移到顶部同一功能组，保留原节点／事件与统一历史，不复制第二套输入。顶部功能组整组换行，短窗口左工具可排两列。
- 对象列表分页；16 层、12 组均可达，骨架页可直接选所有组。图层创建／可见性／删除／排序继续由 miniPaint 和既有命令负责。
- 原生默认 1120×760 DIP，最小 800×640 DIP；外边距 8 DIP，底部动作 32px 图标，状态及失败仍可达，Enter 不确认整个模态。

保留暖白／棕色语义资源、20px 细线图标及同组 8px 间距。用户批准的页面差异是编辑器图标优先、右侧无滚动及整组移位，未迁移其它页面主题／控件。

## 文件和替代关系

- `eng/accessory-editor/editor-layout.js`、`canvas-layout.css`：本版布局、图标、使用提示、页签、分页、响应式整组移位；原 `unified-layout.css` 已替代移除。
- `editor.js`／`entry.js`：现有编辑器入口装配，继续使用原 miniPaint 图片工具、调色、图层与历史；本轮没有独立样板入口。
- `skeleton-editor.js`：输出视图／对象选择事件、接入页签，原编辑命令与历史不变。
- `AccessoryUnifiedEditorWindow.cs` 及窗口测试：原生最小尺寸、紧凑底栏、失败换行和键盘提交边界。
- `canvas-layout.test.mjs`、`unified-skeleton.test.mjs`：新布局实际行为和既有编辑流程门禁。生成资源从维护源重建，未手改 bundle。

## 实际验证

所有浏览器为已有 Edge headless；原生测试为 unshown WPF／无 WS_VISIBLE 的 WebView2，未 Show／Activate 或使用全局输入。

| 门禁 | 结果与范围 |
| --- | --- |
| `node build.mjs`／`node verify.mjs` | 6 个资源精确清单、散列、来源许可名单、local-only CSP 通过 |
| `node --test canvas-layout.test.mjs unified-skeleton.test.mjs` | **22/22**：6 项新布局与16项原编辑集成；真实画笔／填色像素、连接点拖动、统一历史、裁剪／扩展、资源和异步边界 |
| 最后间距与反馈布局调整后的 `canvas-layout.test.mjs` | **6/6** 重跑；三种调色页无裁切、面板收起露出实际工作区、悬停／焦点提示、窄宽分组、16层／12组可达、整组参数编辑／撤销，以及真实拒绝命令的错误不盖住使用提示 |
| `node web-geometry.test.mjs` | 通过；schema2、原图恢复／镜像／旋转／裁剪、编辑文字与工具边界 |
| 原生 `AccessoryUnifiedEditorWindowTests`＋`AccessoryEditorBackgroundTests` | **10/10**；窗口最小值、纯图标辅助名称、底栏／长错误可达，以及实际打包桥接往返与隔离保存重开 |
| 布局资源打包到原生后 `AccessoryEditorBackgroundTests` | **5/5** 重跑；包含在上述10项内，不重复累加。其后仅错误文字／CSS反馈位置调整由上述网页6项覆盖，正式构建再更新资源 |

原生命令使用 `--artifacts-path artifacts/accessory-unified -p:WarningsNotAsErrors=NU1903`。保留既有 SignalR.Common 10.0.11 的 NU1903 告警；未升级依赖或修改项目审计设置。

截图为实际打包网页编辑器、虚构枝叶图层：`artifacts/accessory-unified-ui/editor-wide-final.png`（1280×850）、`editor-minimum-final.png`（784×548 网页区域探针）、`editor-parameters-final.png`（窄宽整组参数展开）。截图已逐张检查；网页区域探针不冒称原生整窗／DPI 验收。测试输出同在忽略的 `artifacts/accessory-unified-ui/`；原生 TRX 位于测试项目忽略的 `TestResults/`。

本轮发现并修复：miniPaint 工具实际为 `span role=button`，提示监听须包含此类工具；折叠过渡需等待实际工作区露出而非只检查属性；颜色通道默认最小行高会裁掉底部 Alpha，已压缩其布局且对每个调色页加无裁切反例。图片输入坐标通过真实画布变换验证，避免把视口坐标当输出像素。

尚未验证本版 Windows 前台呈现、持续手感、输入法、DPI／高对比／减少动画矩阵和用户审美；原有连接／佩戴人工验收继续待完成，S7／产品未标记通过。测试进程／端口结束核对后记录在下方；忽略的截图、日志及独立测试数据保留。

收尾：普通桌面项目构建成功，产物内6项网页资源与最终源包散列一致；资源 verify、`git diff --check` 和新增文本机器路径／冲突标记扫描通过。按本工作树路径核对，本轮残留测试进程与监听均为0；没有打开新的前台窗口，未触及其他用户进程。Git仍为上述分支／HEAD，没有本批提交或后续发布操作。
