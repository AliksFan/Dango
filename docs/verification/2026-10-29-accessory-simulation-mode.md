# 配饰编辑器中央预览模式

日期：2026-10-29；分支 `codex/accessory-editor-rig`，HEAD `c91e5f4` 上的既有工作区。用户已批准按右上工具及佩戴参数方案实施，并要求修改后重新打开控制中心。本轮保留原有未提交修改和独立虚构配饰库；没有迁移或修改默认用户库。

## 实现范围

- 右上预览 Tab 将中央画布切为真实团子物理空间，仅呈现当前配饰；左侧与顶部图片工具收起。团子默认直接拖动，右上仅保留选挂点、平移、缩放；再次点击当前工具返回直接拖动。不提供播放、暂停、跳跃、循环或复位按钮。
- `AccessorySimulationView` 复用正式 `BeltBodyRuntime`、`BeltBodyAppearancePresenter` 与配饰求解路径，使用独立且按需创建的临时 owner；WPF 直接呈现，不传输放大的小 PNG。`AccessoryEditorBrowserHost` 按 DIP／物理像素裁出中央 WebView HWND 区域，保留标题栏按钮区域。退出、隐藏、故障清输入并暂停，关闭释放。
- 选挂点用真实配饰虚影对齐身体映射，点击提交，Esc 取消并保留编辑器。相机独立于图片视图；返回调色盘恢复编辑视角、选择和右下页面。最小内容尺寸 784×548 时，右上和右下基础／细调参数保持可达、无裁切及右侧滚动。
- `AccessoryWearParameters` 严格校验 13 项佩戴字段。`wear-history.js` 将佩戴参数和选挂点纳入原 miniPaint 历史，不导出像素；原生选挂点只合并锚点，避免旧 IPC 快照回退刚调的其他参数。确认返回完整外层草稿，外层应用才保存；取消、迟到快照和失败保持原事务边界。
- `AccessoryUnifiedEditorSession`、原生 Window 与 HomeView 工厂完成产品接线；旧小图定时器、逐帧消息和播放器命令退出生产入口。旧动态预览类型暂保留给原有测试，不作为当前产品入口，避免扩大清理范围。决策见 [ADR 0009](../adr/0009-accessory-editor-simulation-mode.md)，现行界面合同见 [第 14 节](../planning/2026-10-26-accessory-editor-ui-contract.md#14-2026-10-29-中央预览模式当前)。

## 实际验证

- 最终原生筛选 **72/72 通过、0 跳过**，结果：`artifacts/accessory-simulation-mode/tests/simulation-final-green.trx`。包括 8 项真实 native 场景测试、严格佩戴协议／保存取消测试、原生窗口布局与原项目回归。
- 真实产品 Window／WebView2 集成窗口始终完全离屏且不激活：检查正式身体像素、持续消费物理帧、仅一件当前配饰、实际 native 最大行程及佩戴状态、选点虚影／提交／撤销、Esc 路由不关窗口、切回停止 owner。仅使该夹具 renderer 崩溃后，检查清拖动输入、停止 owner、关闭中央 cutout。没有使用全局键鼠或读取真实剪贴板。
- 最终浏览器回归 **43/43 通过**：模式与最小布局、参数小数、统一历史及竞态、原画布与骨架联动、像素撤销原子性、剪贴板消息边界等；独立 `transport-tests.mjs` 通过。所有 headless 浏览器及本地测试服务器由夹具关闭。
- Web build／verify 通过：6 个交付资源、精确清单与 SHA256、本地 CSP、依赖允许列表；`git diff --check` 无空白错误。

### 回归失败及根因

首轮原生筛选有 7 项隐藏浏览器用例失败。单独重跑排除并行负载；独立 probe 在 Runtime `156.0.4314.50` 中观测到 `loaded=true, decoded=false, visibility=hidden`：图片已加载，但 `Image.decode()` 在隐藏 controller 中等待呈现而不完成。

`editor.js` 的工程加载及受控 image undo action 改为先注册 load／error 再赋 src，保留 PNG 头、尺寸、像素及字节预算。损坏图像必须拒绝并保留旧工程；缺省原始尺寸的橡皮／填充／魔棒删除、保存重开及撤销继续检查真实像素。受控 override 和 vendor 镜像保持一致。

另修正两类测试前提：旧魔棒用例按直接消除操作，现更新为选区后在画布发送 Delete；原 image undo 故障注入依赖旧 decode 方法，现从实际 image source 加载边界注入 error／阻塞，继续断言完整工程不变、撤销互斥和未完成快照拒绝。视图 transport 测试等待真实 Ready 条件，替代不稳定的固定 30ms。上述失败均在修复后重新运行通过，没有延长产品等待或跳过断言。

## 规范与验收边界

遵循桌面界面规范 v0.1（2026-10-10）第 2–5 节：暖白语义资源、细棕强调、必要参数文字、紧凑工具图标及名称提示、独立收起轨道和稳定对齐。中央模拟、左工具收起、三个右上工具及无播放器是用户明确页面差异。正式产品装配、生命周期及旧入口退役遵循开发工作流第 13 节。

前台真实指针手感、动态观感、不同显示器／DPI 和人工验收仍未完成；离屏集成和 headless 检查仅证明实际覆盖行为。当前交付是本地实现供验收；未提交、合并、推送、安装或发布。测试构建保留已有 NU1903 依赖告警，未扩大为依赖升级。

## 本地交付

最终独立 Debug 构建成功，0 警告、0 错误，位于忽略的 `artifacts/accessory-simulation-mode/bin/Dango.Desktop/debug/`。打包内 6 项 Web 资源 SHA256 与源码 manifest 逐项一致；`Dango.Desktop.dll` SHA256 为 `8843c3265d460e353e8cecad28da183ffe84c5e4d19ba6ab47515c7b84a4b216`。

启动前未发现旧版 Dango 测试进程；没有停止其他进程。已沿用原带标记的虚构验收数据目录启动新版 PID **29636**，确认持续存活、`Responding=True`、主窗口标题 **Dango 控制中心**。未自动进入配饰编辑器，没有操作用户键鼠。当前控制中心与虚构验收数据保留供用户验收；本轮没有新增配饰样例或删除测试数据。
