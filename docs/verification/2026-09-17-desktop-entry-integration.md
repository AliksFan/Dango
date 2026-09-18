# 第一批正式桌面入口整合：实施与验证

日期：2026-09-17。用户在审阅[具体设计](../planning/2026-09-17-desktop-entry-integration-design.md)与[实施计划](../planning/2026-09-17-desktop-entry-integration-plan.md)后回复“确认设计”，并在交付后明确“目前这部分人工验收通过”。第一批正式桌面入口整合已获用户人工验收认可；该确认不代表完整单人正式版或整个 Phase 1 已验收，专项证据边界见第 5 节。

## 1. 实施范围与保护

- 实施分支：`codex/desktop-entry-integration`；从 `72199ef93febaec88280fb2bd3c27c2fe594f3ca` 新建独立工作树。规划树的 Phase 0 代码未用作实施起点。
- 普通入口默认一只团子，使用已认可身体与新眼帽，默认 Wallpaper、键盘关闭/方向键、交互开启、叶芽帽。三种既有帽型与稳定偏好可保存。
- `Product/DeveloperReview` 与附件启用分离；普通界面保留恢复团子、显示/隐藏、设置、帮助和诊断；多体、骨架、物理参数及验收入口只在 review 显示。
- runtime 构造不标定；先建立托盘和可用控制入口，再初始化。标定 world 释放后才建立唯一 live world；缺/错库进入可查询的 Unavailable，显式重试。不更改身体物理、眼帽数值或 native 源码。
- Schema 4 增加 `interactionEnabled/hatPresetId/introSeen`。AppShell 统一捕获保存，离散变更及时保存、窗口几何 300 ms 合并、退出刷出。首次说明只在概览呈现后记录。
- Schema 1/2/3 保留有效偏好并先备份后原子保存。Product 强制运行骨架关闭，同时保留旧骨架兼容值。未知帽型局部回叶芽；损坏/未来版本禁止常规覆盖，显式恢复必须先备份。保存失败可见、可重试或不保存退出；重试不会重放宿主命令。
- 原开发树的 1 处 CMake 差异、25 个历史实验文件和两份 DLL 均未改变。28 项保护指纹核对一致，证据为 `artifacts/desktop-entry/implementation/protected-baseline.json`。

点击/抚摸、自主生活、真实上下岛、低动效、颜色选择、身体坐标恢复、完整档案和联网均未纳入本批。

## 2. 主要文件

| 范围 | 文件 |
|---|---|
| 应用启动与统一保存 | `AppShell/DangoApplicationHost.cs`、`ApplicationUiMode.cs`、`ApplicationCommandController.cs` |
| 控制中心与配置保护 | `AppShell/ControlCenter/ControlCenterWindow.xaml`、`.xaml.cs`、`ControlCenterWindowSettingsStore.cs`、`ApplicationSettingsSession.cs` |
| 托盘保存失败通知 | `AppShell/Tray/DangoTrayIcon.cs` |
| 单一场景与产品帽型 | `Rendering/Dango/StackedDangoSceneRuntime.cs`、`DangoHatPresets.cs`、`AttachmentReviewPanel.cs`、`DesktopHost/DesktopHostCoordinator.cs` |
| 包 | `eng/publish-desktop-entry.ps1` |
| 新增验证 | `ProductEntryRuntimeTests`、`ApplicationSettingsSessionTests`、`ProductApplicationHostTests`、`ProductControlCenterTests`、`ProductControlCenterLayoutTests` |

桌面代码路径相对于 `src/Dango.Desktop/`，测试相对于 `tests/Dango.Desktop.Tests/`。原 Schema 测试调整为版本 4；旧 DLL 导出测试未改、未跳过。

## 3. 实际门禁

最终证据根：`artifacts/desktop-entry/verification/`；构建及命令日志位于 `artifacts/desktop-entry/implementation/`。

| 检查 | 实际结果 | 证据 |
|---|---|---|
| E0 单次隔离基线构建 | 成功，0 warning / 0 error | `artifacts/desktop-entry/baseline/build/` |
| E0 基线测试 | Core 37/37、Desktop 303/304、Physics 常规 32/32 | `baseline/test-core/core.trx`、`baseline/test-desktop/desktop.trx`、`baseline/test-physics/physics.trx` |
| 最终 Release solution build | 成功，0 warning / 0 error | `implementation/final-build.log` |
| Core | 37/37，通过 | `verification/results/core.trx` |
| Physics 常规 | 32/32，通过；明确排除 `DangoPhysicsStabilityTests` | `verification/results/physics.trx` |
| Desktop 完整套件 | **322/323**，无跳过；仅历史旧 DLL 导出失败 | `verification/results/desktop.trx` |
| 身体等价回归 | 单/双/三体每例 900 步，新眼帽/旧表现/直接模块一致 | 完整 Desktop TRX 内 `NewOldAndDirectPhysicsAgreeForNineHundredSteps` |
| 新增入口/设置/UI | 共 19 个执行用例通过，包含真实模块缺失→恢复、坏库、初始化幂等、两退出路径、迁移/备份失败/保护、保存重试、Product/review、编译 XAML | 完整 Desktop TRX；无真实桌面操作的 UI 用例使用虚构 host/settings |
| 离屏界面 | 三页 × 940×620 / 820×560 内容区域，共 6 张 PNG；页面与退出入口边界断言通过，并人工查看输出 | `verification/desktop-evidence/desktop-entry-ui/` |
| `git diff --check` | 通过 | 当前差异检查 |
| 全仓 `dotnet format --verify-no-changes --no-restore` | **未通过**：5 个既有文件、25 处格式/编码问题；本次修改文件无报告项 | `verification/format-report.json`、`format-baseline-comparison.json` |

格式问题对应 `VerificationModels.cs`、`HatBodyFollowTests.cs`、`DangoSceneWindow.xaml.cs`、`DesktopDangoSceneOverlayWindow.cs`、`WorkerWWindow.xaml.cs`。逐文件 Git blob 与 `72199ef` 一致；没有为全绿顺手改动这些文件。

Desktop 唯一失败仍为 `DangoInterBodyCollisionTests.SharedSelfWorldExportsAreAvailable`，旧 `dango_box2d.dll` 缺少 `dango_box2d_self_bodies_create`。本批没有删除/跳过测试或替换库。后续须独立核实该契约，不能把内部候选称全绿正式发布。

基线与最终测试均使用真实的四份必要 native 夹具副本：统一模块、历史三体库、历史双体模块和 collision-ratio 对照库。逐份与原树 SHA-256 一致；旧对照产物既有证据损伤没有因复制/测试而被重新认证。不使用 fake DLL，也未重建 native。

## 4. 内部候选包

以下保留本批已完成的打包验证及产物记录。用户于 2026-09-17 明确后续验收直接从项目打开窗口，不再默认提供整个压缩包；仅在需要分发时按需打包。

- 包目录：`artifacts/desktop-entry/candidate-20260917-01/package/`；入口：`Dango.Desktop.exe`，无参数。
- ZIP：`artifacts/desktop-entry/candidate-20260917-01.zip`，76,669,540 bytes。
- ZIP SHA-256：`62A87D6A2787BBBE5A0A3BA7A1F9402DF773AF6B3345E6042D949BD2BA8386F3`。
- 包清单：`package/package-manifest.json`，478 个包文件逐项路径、大小和 SHA-256 一致；清单自身不递归列入。包含基线提交与 `sourceDirty=true`，明确为未提交工作树产生的内部候选。
- 清单 SHA-256：`7877240416F0C0444A4F90CCD54627482115DD5CC711EA5068A351C723434020`。
- 两库字节及许可证/依赖清单与输入一致；包中无 PDB、个人设置；清单路径均为相对路径。
- 新库 SHA-256：`00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`。
- 旧库 SHA-256：`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。
- 缺输入、错误哈希、非法运行名和已有输出的负例被拒绝。已有候选目录拒绝前后的清单和完整包树哈希一致。
- 包内 `--smoke-test` 隐藏启动后自然退出，exit 0。仅证明 Dispatcher 启动/退出，不代表普通无参数场景或新模块在包内真实加载通过。

模块输入来自已验收的本地产物，不是纯净 Git checkout 自足交付；本批未解决 native 从零重建与 CI 产物托管。脚本只发布到全新目录，不自动启动、停止或清理。

## 5. 人工验收与专项证据边界

2026-09-17，用户反馈：“以后不用给我整个压缩包，直接在项目里打开窗口不就行了，目前这部分人工验收通过”。据此记录**第一批正式桌面入口整合人工验收通过**。后续默认从项目启动对应验收窗口，不再要求用户解压整包；本次确认后不重新启动已验收窗口，也不自动开始互动/自主/岛批次。

此前自动验证阶段没有在日常 Windows 用户中无参数启动候选，没有迁移或覆盖用户日常设置，没有关闭旧预览。此为当时执行记录；本次用户确认不改写为代理曾完成真实桌面操作。

用户未提供[计划第 5 节](../planning/2026-09-17-desktop-entry-integration-plan.md#5-普通入口人工清单)的逐项结果，以下专项仍标为**未验证**，不从本批整体人工认可推定通过：

1. 包内模块实际加载，以及 Window/Wallpaper 两宿主输入、焦点、穿透、桌面图标与任务栏的完整专项矩阵。
2. 普通单实例再次启动、退出及键盘捕获/托盘/窗口资源回收的逐项检查。
3. 第二种 DPI/分辨率、Explorer 与 WorkerW 故障恢复、长期运行及完整系统/硬件矩阵。
4. 普通入口实际保存→退出→重启恢复的逐项证据、真实无写权限及各项迁移故障场景。现有自动 store/编译壳层证据继续保留。

第 3 节的旧 DLL 导出失败与全仓格式基线问题仍未消除，不能据本次人工确认标记全绿发布或整个 Phase 1 完成。

## 6. Git 与运行状态

本批保留为独立分支未提交修改，未合并 main、未推送、未部署或创建发布标签。当前规划树同步补记设计确认、第一批人工验收通过及项目内启动的交付方式；原开发树与历史预览保持原样。

此前实施验证未启动服务器、业务端口、数据库或新预览；包 smoke 已自然退出，当时测试进程结束后无本批桌面应用驻留。本次仅同步验收文档，未重新启动或停止程序，未重新检查进程状态。native fixtures、备份、测试日志/图片和候选包保留在忽略的 `artifacts/` 下，未删除旧证据。
