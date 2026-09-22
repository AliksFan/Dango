# 已验收性能改动的本地整合与安装更新

日期：2026-09-22。用户接受候选 `8c46a0f` 的性能后，明确授权在新任务中完成阶段分支和本地 main 整合、日常安装更新及启动/模式切换/退出检查。本轮没有产品修改，没有重跑完整性能长测，也不宣布整个 Phase 1 验收完成。

## 本地整合

- 开始时 `main`、`phase/1-desktop-life-prototype` 均为 `6e3266d`，来源 `codex/performance-stability` 为 `e598ae3228166cb84061f212029d8c373372eb34`；三处工作区干净，main 是来源祖先。
- 本任务创建 `codex/performance-local-delivery`，纳入来源后，先将阶段分支快进，再将 main 快进至 `e598ae3`，无冲突；交付记录另作本地文档提交并沿相同顺序同步。
- 整合后的文件树与来源完全一致。实际产品候选 `8c46a0f8b85d7bf7765e9e4e87cee0855e25e3b3` 与来源 HEAD 的 `src/` 树相同；后续提交只涉及工具与文档。因此直接使用已实测的原包，没有重新构建或替换其二进制。

## 安装与保护检查

| 项目 | 更新前 | 更新后 |
|---|---|---|
| 日常当前版本 | `333ec9c2eeee-8b67926bf3bd` | `8c46a0f8b85d-066a12d5a250` |
| 回退版本 previous | 无 | `333ec9c2eeee-8b67926bf3bd` |
| 设置契约 | `dango-desktop-settings-v4` | 相同 |

安装根仍为 `%LOCALAPPDATA%/Programs/Dango`，设置仍为 `%LOCALAPPDATA%/Dango/settings.json`。来源包为性能工作区的 `artifacts/desktop-entry/redundant-8c46a0f/package/`；format 2、win-x64、self-contained、`sourceDirty=false`。

实际执行现有 `Test-DangoPackage`，478 个文件的清单、大小、SHA-256、许可和受保护两 DLL 均通过。五项设置源合同与 `Get-DangoSettingsSourceContract` 一致；清单 SHA-256 为 `066A12D5A250C2CE679C6036C52E4A067D908CAECD57E1AEDA12F5D95399F7E3`。现有管理工具与仓库指纹相同，桌面和回退快捷方式符合已安装入口契约，更新前无 Dango 实例。

先运行既有安装脚本的 `-StageOnly`，确认当前版本仍为旧版；再校验新版本目录和设置指纹，调用既有 `Set-DangoActiveVersion` 激活。激活后重新校验当前包与 previous 包，均通过。旧版本、管理工具备份和原工作区全部保留；此次未实际执行回退演练，回退功能沿用[此前验证](2026-09-20-local-launch-update.md)，本轮确认旧包完整、previous 指向正确且回退入口保留。

设置与受保护 DLL 的原七项指纹在更新前后全部一致。设置最终 SHA-256 为 `FB4D6BFC95AE57F52B0DC022C67F7DD435974C561D45359D2B4D213D4BE8AC96`。模式切换时由应用正常保存 Wallpaper，切回 Window 后恢复相同字节；未用备份回拷设置。受保护旧 DLL 未替换，其缺少 `dango_box2d_self_bodies_create` 导出的既有失败继续保留。

## 真实日常入口快速检查

通过实际桌面 `Dango.lnk` 启动普通产品，使用仅限目标 PID 的 UI Automation 操作控制中心；未使用隔离性能探针代替日常程序，也未采集其他应用内容。

- 冷启动：启动器退出码 0，唯一产品 PID 为 18588，路径位于新版本目录；普通窗口响应正常，场景 Ready，物理世界运行，已加载新目录内指纹一致的统一物理模块。
- 重复打开快捷方式：启动器退出码 0，仍为同一个 PID，成功打开控制中心。
- Window → Wallpaper → Window：通过产品模式单选按钮切换。Wallpaper 报告 `Attached`、父句柄有效、桌面显示开启、场景 Ready；两种宿主均显示物理 world 运行、附件/互动/待机无故障、原生错误 0。切回后恢复原 Window 模式、800 × 600 逻辑尺寸，设置指纹与更新前相同。
- 正常退出：点击控制中心“退出 Dango”并确认后，首个 PID 和所属窗口消失，无 Dango 残留；首次观察未捕获产品退出码，保留该原始记录。随后再次经快捷方式冷启动，以 PID 7216 确认原设置和 Window 场景恢复，提前持有进程句柄后经相同 UI 退出，实际产品退出码 **0**。
- 最终无 Dango 或测试探针进程、目标窗口残留；两次启动器及重复激活辅助进程均已退出。没有启动网络服务、端口或数据库，没有删除旧版本、备份或用户数据。

本轮证据位于本任务工作区 `artifacts/local-performance-update/20260922/`：`before.json`、`protection-before.json`、`staged.json`、`activated.json`、`package-validation.json`、`cold-start.json`、`repeat-start.json`、`capture-ui.json`、`wallpaper-ui.json`、`window-ui.json`、`normal-exit.json`、`restart-normal-exit.json`、`final.json`。该目录与本地 UI 检查脚本仅作为本机证据，不提交到 Git。

## 验证与交付边界

- 静态与安装集成：来源差异检查、包完整性、设置契约、工具/快捷方式、版本切换和设置保护实际通过。本轮只新增交付文档，不重复无变更的产品构建和整套测试。
- 既有自动证据沿用：分析器 75/75，Core 37/37，Desktop 494 通过 / 1 项旧 DLL 既有失败；没有把这些写成本轮重跑或全绿。
- 性能人工验收沿用用户已经作出的接受决定；本轮 UI 操作和运行诊断只证明本机日常启动、切换和退出，不是新的人工视觉验收或性能长测。
- CPU、Wallpaper 内存峰值与短暂丢时的原始失败保留；Wallpaper 活动覆盖不足、60 分钟持续运行、GPU 呈现、第二 DPI/其他环境等专项仍未验证，详见[性能验收记录](2026-09-21-performance-acceptance-near60.md)。
- 已本地整合及安装更新；未推送远端、云部署、创建发布标签或删除任何分支、Worktree、旧版本与备份。
