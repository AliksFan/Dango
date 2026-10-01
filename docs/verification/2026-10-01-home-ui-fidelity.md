# Dango 首页实现验证记录

本记录只覆盖[首页横向交互](../planning/2026-10-01-home-ui-fidelity.md)，不代表整个 Phase 1、物理场景或多人能力通过。分支为 `codex/desktop-home-ui`，实施基线为撤回提交 `38010f4`。用户于 2026-10-01 确认本批首页与展开交互人工验收通过。

## 实际验证

- 实施前，新增 Product 入口契约测试真实失败：找不到已批准首页、捏脸、换装和编辑面板。实施后该测试通过。
- 专项曾发现 320 DIP 团子偏离中心，以及窄窗口更多选项空间不足；修正后复验，未降低初始居中、共同位移与横向并排的断言。
- 静态门禁：Desktop 构建成功，0 警告、0 错误；新增 C# 文件 whitespace 格式检查及 `git diff --check` 通过。
- 专项：30/30 通过，覆盖首页、横向布局、快速反向、Esc 焦点、减少动画、旧设置/诊断的可达性、保存失败提示以及窗口设置校验。系统允许动画时，还断言 80 ms 中间帧不是直接跳到终点。
- 实际渲染：1024、736、640、480、320 DIP 默认、捏脸、换装、更多共 20 张 WPF 渲染图已生成；已对照检查宽窗口与 320 DIP 窄窗口代表图。证据目录为 `artifacts/home-ui-2bcf9ad3fcae4757842cf21e2bbfd252/home-ui/`。这不是用户人工验收。
- 全量 Desktop 回归：558 项中 386 通过、172 失败，未跳过。报告保留于 `artifacts/home-ui-final-ee6b47c46a08430c9355d7b3694beacb/desktop-full.trx`。当前工作树和测试输出缺少 `dango_physics_module.dll`，旧 DLL 的 `dango_box2d_self_bodies_create` 缺导出失败仍存在。其余物理/渲染断言没有逐项复核根因，不宣称全量通过或全部属于历史问题；本轮不修复物理门禁。

专项命令：

```powershell
dotnet test tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj --filter 'FullyQualifiedName~ProductHomeInteractionTests|FullyQualifiedName~ProductControlCenterTests|FullyQualifiedName~ProductControlCenterLayoutTests|FullyQualifiedName~ControlCenterWindowSettingsStoreTests|FullyQualifiedName~ApplicationSettingsSessionTests'
```

## 验收启动

从仓库根运行：

```powershell
dotnet run --project src/Dango.Desktop -- --home-ui-review
```

这是同一生产首页控件的隔离验收入口，只显示首页与展开交互，不创建物理世界、托盘、网络服务或用户设置文件。可点击捏脸、换装、颜色、更多与收起，按 Esc 收起；应用和后续模块禁用。窗口关闭即退出。

## 未验证与交付边界

- 首页与横向展开的人工观感已获用户确认；正式桌面物理联动不在这次验收结论中。
- 高对比度、跨显示器 DPI、触屏和真实桌面物理联动未完成人工验证。
- 不修改原型 HTML，不更换正式物理 DLL，不覆盖日常安装，不合并 main、推送或部署。
- 验收图片及测试产物保留于被忽略的 artifacts 下；不删除此前任务的产物或设置。
- 本批在 `codex/desktop-home-ui` 建立本地提交；未合并 main、推送或部署。没有启动服务端或数据库。
- 隔离入口已实际启动，返回窗口标题“Dango · 首页交互验收”、有效窗口句柄及响应状态；随后用户确认本批视觉与交互验收通过。
