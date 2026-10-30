# 配饰标题栏与图片工具修复

日期：2026-10-27。分支 `codex/accessory-editor-rig`，HEAD `51caf0e`；本批未提交、合并、推送或发布。用户要求移除原生标题条和可见标题、三个窗口按钮参照首页，并反馈原有图片上橡皮／去背景／填充显示“操作未完成”；明确“去背景”改称“魔棒”。

## 复现与根因

原生 `AccessoryUnifiedEditorSession`／`AccessoryBrowserProjectAdapter.CreateOpenProject` 以可空的 `OriginalWidth/OriginalHeight` 创建图片图层，传给网页的 `width_original/height_original` 为 null。`editor.js` 直接载入 PNG 的快捷路径填写尺寸，但 schema2 工程载入路径没有补齐。三个原有 miniPaint 光栅工具将 null 赋给临时 canvas 的尺寸，形成 0×0；导出的 `data:,` 在 `image.decode()` 产生 EncodingError，原子动作中止后显示“操作未完成”。

修复前：用同样的合法原生开图形状，三个工具均复现 EncodingError；橡皮反例检查目标 alpha 仍为255而失败。已有小图片快捷路径不复现，说明此前验证未覆盖该原生开图差异。

修复位于拥有解码图片的网页导入边界：`editor.js` 在源尺寸缺失时从解码 PNG 的 naturalWidth/naturalHeight 补齐；保留图层显示大小／位置，不把缩放后的显示尺寸当源像素尺寸，不改变参数、格式版本或资源预算。没有通过隐藏失败提示制造成功。

## 界面改动与范围

- `AccessoryUnifiedEditorWindow.cs` 去掉原生标题栏和可见标题，用首页的窗口按钮模板提供最小化、最大化／还原、关闭；保留普通 resize、模态草稿与浏览器释放边界。
- 首页 `ProductWindowButton` 原样提取到 `Resources/ProductWindowChromeResources.xaml`，首页只换资源引用，无行为重做或新主题。
- `AccessoryEditorBrowserHost.cs` 仅在编辑器显式启用右上132×38 DIP缺口，解决 WebView2 HWND 不能被普通 WPF 覆盖的问题；网页顶部工具栏同步留位。不增加一整行标题栏，其他 host 默认不裁剪。
- 工具标签、悬停／辅助名称与用法改为“魔棒 W”；保留其当前按颜色去除像素的手动行为、容差和连续区域参数。
- 沿用桌面规范 v0.1（2026-10-10）第2–6节及用户明确页面差异；未修改物理解算、正式保存数据或日常安装。

## 已执行验证

| 范围 | 结果 |
| --- | --- |
| `raster-tools.test.mjs` | **6/6**，橡皮／魔棒／填充×原始／缩放图层；核对真实输出像素、原始尺寸、单步撤销／重做和工程重开，修复前同路径能失败 |
| `canvas-layout.test.mjs`＋`unified-skeleton.test.mjs` | **22/22**；参数／布局与统一编辑历史保持；补充顶部工具不占原生窗口按钮区域的反例后，布局6项再次通过 |
| 原生窗口／native host／打包浏览器过滤集 | **18/18，0 skipped**；共享按钮样式／无可见标题、DPI与resize几何、实际 OS HWND region、打包资源桥接及原生开图后三个光栅工具 |
| Native raster 三项 | 实际 `CreateOpenProject` 生成 null 源尺寸，隐藏 WebView2 经 DOM 输入事件执行工具，核对 PNG 像素、撤销和重开；全程根窗口无 WS_VISIBLE |
| Native region | 无 WS_VISIBLE 的真实 HwndSource/HwndHost；从 OS GetWindowRgn/PtInRegion 核对缺口、保留区域、resize与取消。未启动可见窗口或全局输入 |
| 资源与构建 | build／verify 通过，6项资源精确清单及来源检查；独立 `artifacts/accessory-chrome-fix` 构建成功 |

原生命令保留 `-p:WarningsNotAsErrors=NU1903`；既有 SignalR.Common 10.0.11 告警未解决，没有改变依赖审计策略。日志／profiles 作为本机忽略 artifacts 保留。

当前用户主动打开的旧测试实例未强制关闭或覆盖，以保留未保存草稿；新二进制在独立构建目录。原生按钮实际前台点击、WebView2缺口的视觉呈现、拖动／最大化／还原、DPI矩阵和人工观感仍待确认；后台通过不代替这些验收。无新增前台窗口、正式库操作或数据删除。

后续窗口状态：用户随后明确同意“直接重开，放弃未保存草稿”，旧实例已由修复版替换，且用户反馈“已正常打开”。该反馈确认正常打开，不等同于上述所有前台行为或工具路径均已验收。本轮图层层级调整没有再次关闭窗口、丢弃草稿或操作正式存储。
