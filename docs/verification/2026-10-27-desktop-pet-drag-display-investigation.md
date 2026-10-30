# 桌宠拖动显示缺块排查

后续状态：本页保留首轮排查历史。两处裁切的修复、自动验证和用户人工复验已完成，见[修复记录](2026-10-27-desktop-pet-drag-display-fix.md)；随后按用户授权完成[本地 main 整合](2026-10-27-desktop-pet-drag-display-main-integration.md)，正式包未替换。

日期：2026-10-27。基线为本地 `main@d929a8ee85d050c45edc7bfb7c93b043aed35729`，排查保存在独立 `codex/desktop-pet-drag-display` 分支。用户报告拖动时偶尔出现“蘑菇头”“平头”和配饰部分消失。本轮未确认根因、未修改生产代码，也未将问题标记为修复或验收通过。

用户在一次屏幕检查后选择“先保留后台排查结果”，不继续前台测试。原工作区的既有 `AGENTS.md` 修改保留。

## 已核实结果

- 当前正式产品通过 `ProductPhysicsComposition` 创建 Belt 房间运行时。`BeltRoomSceneAdapter.Present` 正常生成的身体帧使用 `ContinuousBelt` 与 `FallbackReason.None`；没有证据支持将固定回退椭圆认定为本次根因。
- `DesktopPetDangoWindow` 固定透明窗口覆盖可用区域，内部移动绘制内容。`UpdateNativeRegion` 同时包含身体路径和配饰当前网格的包围框；`SetWindowRgn` 不仅影响命中，也会裁剪画面。关闭 WPF 的 `ClipToBounds` 不会取消这一原生裁剪。
- 原有配饰宿主集成夹具使用旧 `CreateProduct()` 物理模型。本轮仅为新增用例增加正式 Belt 装配选项，原有测试合同保留。
- 新增后台用例覆盖 24 个自然推进的拖动采样，要求实际物理步数增加、手势持续、进入真实物理拖动、身体离地超过 20 world units、无轮廓错误或回退，以及前后两层配饰均保留可用画面。
- 用例从真实 WPF 视觉树渲染 alpha，与同一时刻 HWND 的 `GetWindowRgn` 区域逐像素比较。检查 alpha 至少 250、向内腐蚀一像素后的区域，容忍量为 `max(2, 采样数 / 10000)`。当前用例通过；没有复现同帧主体内部被错误区域裁切。采样前不显式重新应用窗口帧，以免补正正常回调链中的问题。

涉及源码：[透明宿主](../../src/Dango.Desktop/AppShell/Rooms/DesktopPetDangoWindow.cs)、[原生区域](../../src/Dango.Desktop/DesktopHost/Win32/NativeMethods.DesktopPet.cs)、[Belt 呈现](../../src/Dango.Desktop/Physics/Presentation/BeltRoomSceneAdapter.cs)、[回归用例](../../tests/Dango.Desktop.Tests/AppShell/DesktopPetAccessoryIntegrationTests.cs)。

## 待验证假设

同步更新的原生裁剪区域可能与异步呈现的 WPF 图像不属于同一帧，移动时形成局部缺块。这与用户描述相容，但当前没有取得实际合成画面的连续帧证据，不能据此修改生产裁剪机制或宣称已定位。

同帧离屏渲染只验证当前视觉树与当前区域一致，不能证明桌面合成时序、半透明边缘、窗口范围外像素或真实鼠标观感正常。对隔离 HWND 的 `PrintWindow` 返回全黑，不能用作有效显示证据。首次屏幕检查仅进入隔离桌宠场景；无标题透明窗口未被自动化工具列出，没有取得拖动症状帧。

[Windows 分层窗口文档](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows)说明，零 alpha 区域可让鼠标穿透。这可作为后续评估裁剪与命中职责的参考；本轮没有采用替代机制。

## 验证状态

| 项目 | 实际结果 |
|---|---|
| 新增 Belt 连续拖动与同帧 alpha 覆盖检查 | 1/1 通过，零跳过；`artifacts/drag-display/drag-coverage-reviewed.trx` |
| 旧配饰宿主集成用例 | 5 项未通过。首次未改测试的基线因缺少旧 `dango_physics_module.dll` 初始化失败；补充历史包中的测试输出后仍未得到健康呈现。此夹具问题未扩大纳入生产修复 |
| Release 测试构建 | 诊断构建成功；使用 `-p:WarningsNotAsErrors=NU1903` 保留并降为警告的现有 SignalR 10.0.11 审计项。首次标准构建因该项失败，标准构建门禁未关闭；未升级依赖或修改项目警告策略 |
| 变更 C# 空白格式与差异空白检查 | 通过；格式命令加载工作区有警告 |
| 实际桌面连续显示与人工复现 | 未验证；用户选择保留后台结果后停止前台检查 |

运行日志、隔离宿主探针和虚构测试数据保留在忽略目录 `artifacts/drag-display/` 及配饰测试的虚构数据目录。后台窗口位于屏幕外、不激活、不操作全局鼠标；用实际物理步数增长确认测试期间仍在推进。

## 交付边界

沿用桌面界面规范 v0.1 第 1、2、6 节的范围与真实验收边界；没有页面、主题或交互规则变更。生产代码、日常安装与真实存档未更改；没有合并 main、推送或部署。所有本轮前台和后台测试进程已结束，没有启动服务或端口，虚构测试数据保留。

后续需在另获屏幕使用授权后，对同一隔离宿主捕获连续显示表现，并将实际缺块帧与对应物理状态、视觉树和原生区域关联；只有复现并核实原因后再实施修复。
