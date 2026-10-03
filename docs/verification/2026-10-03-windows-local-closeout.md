# Windows 本地体验收口候选验证

> 日期：2026-10-03
> 状态：同屏 150% 与 100% 两轮实包自动检查和候选整体人工验收均通过，Windows 恢复原 150% 缩放；后续另获“执行”授权的[日常安装更新及 B→A→B 检查](2026-10-03-windows-local-delivery.md)已完成，进程正常退出。自动差异与其他未验证项保留；不代表完整收口门禁全通过、main 合并或推送。

## 范围与候选

按[用户确认计划](../planning/2026-10-03-windows-local-closeout.md)，仅收口既有单机首页与安装闭环：旧可读 Schema 4 主设置、独立窗口预设 sidecar、显式可恢复工具迁移、受信原生发布预检，以及隔离 Product 实包验收。A/D 编辑、S/空格、W 空门廊和进入正式房间前桌面隐藏均保留；登录、正式房间、数据库、联网与跨屏不在本批。

实施工作树 `codex/windows-local-closeout` 从 `codex/team-integration@0bdf5c1` 建立。安装代理的 `e8c4942`/`7ebd129` 分别整合为本地 `21b759c`/`3706b7e`；应用增量 `65783be2c5a3d38c2655bd8705369c9de76b70b7` 是本次包的干净源码提交。

包：`artifacts/desktop-entry/windows-local-closeout-20261003-01/package`，`win-x64` self-contained，`sourceDirty=false`。清单 SHA-256：`3BC62071F9F189DFB5BC4C082F6CBB3C5367FFB7B9C600265B7980AA741F8580`；契约为 `dango-desktop-settings-v4-preset-sidecar-v1`。旧 A 为只读日常已装包 `782b1c3b85cb-c798eb8da10d`，仅复制到独立安装根或加载其设置程序集，不启动旧 A、不写日常设置。

环境：Windows 11 26100、SDK 10.0.303；受信模块 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`。受保护旧 DLL 指纹保持 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`，未编译或替换。

## 静态和集成门禁

- Release 全方案构建：0 警告、0 错误。发布成功，包自带运行时与许可；原生和五源契约发布前后校验通过。
- 安装器最终 PowerShell 5.1 与 pwsh 各 53/53；主树证据 `artifacts/windows-local-closeout/install-final-ps51`、`install-final-pwsh`。覆盖篡改/契约换标签、旧工具拒绝、迁移/中断恢复、原子替换有界重试、状态/双文件/快捷方式保留及同实例设置域隔离。
- 原生预检两宿主各 4/4；证据 `native-final-ps51`、`native-final-pwsh`。缺模块、错 SHA、受保护输入缺失拒绝，受信模块 x64/ABI 1/七导出通过；失败不创建 package 输出。
- 实际旧程序集设置 reader/writer：3/3 预设通过；证据 `actual-old-settings-final/verification.json`，旧程序集 SHA `9024C6D7FC463384841A9E1FAC9BBDB85BDF503F933688120BFCFB682AC271D5`。虚构 B 保存→真实 A 读取/改帽型/保存→B 恢复偏好与独立预设，旧 A 不启动。
- 新 settings/sidecar 重解析点测试先在原入口 2/2 失败；修复后 LocalInstallAcceptance 5/5。安装代理五项 isolation 红测全部失败后，修复为 53/53，证据保留在代理树 `artifacts/local-install-tests/isolation-*`。
- 最终受影响 Desktop 专项：96 通过、0 失败、2 跳过，总 98；完整 Desktop：644 通过、1 失败、2 跳过，总 647，约 6 分 6 秒自然完成，无 timeout/aborted。两者分别 `affected-release-final/affected.trx`、`desktop-full-final/desktop-full.trx`。

这些目录均位于本工作树 `artifacts/windows-local-closeout/`（特别注明的代理证据除外），被 Git 忽略。

## 红灯与环境边界

完整 Desktop 唯一失败：`DangoInterBodyCollisionTests.SharedSelfWorldExportsAreAvailable`，受保护旧 DLL 缺 `dango_box2d_self_bodies_create`。没有删测、跳测或替换 DLL 求绿。

两项跳过仍是前台焦点条件不足：`ClosingSizeMenuReturnsKeyboardFocusToTheDangoStage(False)`、`RealMouseSizeSelectionRestoresTheHomeStageFocus`；不计通过。

首次全量留证 `full-release-01`：Desktop 635/10 fail/2 skip；其中 8 项因本工作树没有 hardcoded artifacts 模块，复制同指纹已批准模块后恢复；帽子重试测试仍假设单文件写入，按批准的双文件语义补为 sidecar 失败→sidecar→main 路径断言，同时保留一次宿主命令断言，复跑通过；最后一项为上述旧 DLL 红灯。Core 37/37、Protocol 6/6、Server 24/24 通过；Client.Sync 27/30，三项真实网络用例缺本轮专用 `DANGO_SYNC_SERVER_PATH`，本批不启动网络样本补门禁。

首次 Physics 2/45（43 fail）主要缺隔离原生路径。明确指定受信模块后，常规范围 `physics-regular-02/physics-regular.trx` 为 20 pass/13 fail/0 skip，总 33；13 项仍缺历史 collision-ratio、二体或三体实验对照库，未擅自重编译。长稳不以缺库首次失败推为物理结论，正确环境下的长稳尚未执行。本轮不能声称完整自动门禁全绿。

主树最终 PS5.1 首次直接调用测试入口时复现参数默认表达式中 `$PSScriptRoot` 为空；改为脚本体解析后，不传额外仓库参数即可 53/53。此前安装代理用显式参数通过，两份证据口径分开。

## 实时、人工与交付

真实 A→B→A 安装：PowerShell 5.1 与 pwsh 各 9/9。证据在安装代理工作树 `artifacts/windows-real-install/real-ps51-01` 和 `real-pwsh-01`，含 `a-active`、`tools-migrated`、`b-staged`、`b-active`、`a-rolled-back` 独立 JSON/哈希快照。两组使用真实 x64/self-contained EXE 和程序集，未启动旧 A，版本、双虚构设置、marker、隔离 Rollback.lnk 及工具备份完整；迁移不改应用 state，StageOnly 不改 current，回退保持新管理器。A 清单 SHA 为 `C798EB8DA10D4CB93F0F7E4D24B2B44875384FE1A74210706A5A4BC6E977D64D`。

主代理随后接管 `real-ps51-01`，显式 Activate B；`real-pwsh-01` 仍留 A current/B previous 且未启动。后续启动应用正常写入这份虚构设置，不再把启动后的 settings/state 与启动前安装演练快照混为同一不变性结论。

真实 B 窗口：`actual-product-window-02/verification.json` 为 7/7，退出 PID 30228，`ownedProcessStillRunning=false`。通过本次独立验收快捷方式冷启动、加载包内精确模块、编辑/预览边界、native Ready 与桌面房间门控、当前显示器三档尺寸、同设置域重复启动、跨设置域/运行中更新回退拒绝、关闭隐藏及已安装管理器恢复、正常退出。检索只限本会话、本 EXE、精确虚构设置域的所属 PID；没有操作其他应用窗口。

首轮窗口 DPI 144（150%），两档横向物理像素实测 960×540、1280×720，纵向为 540×960：沿用 360×640 DIP 内容最小尺寸及工作区限制，并非声称纵向在所有缩放下一定为 480×800。三档均与既有 DPI/工作区公式相符，误差门禁为 2 px。

首轮 `actual-product-window-01` 为 6 pass/1 fail，正常退出验证被 Win32 MessageBox 不支持 InvokePattern 阻断；此时保留 PID 28704。核对 PID、程序路径、对话框标题/类和 IDYES 子控件后正常确认退出，未强杀。验证脚本改为同样受控的异步 native 按钮点击，第二轮完整通过；首轮失败证据没有删除或改写。

UIAutomation 只证明以上应用行为，不代替用户观感、键盘手感或真实托盘菜单点击。首轮重新打开的 B 人工验收窗口 PID 记录在原 `manual-handoff.json`；其后已正常退出，不改写原证据。第二缩放自动检查见下节；第二分辨率、跨屏及热插拔未验证。

2026-10-03，用户对首轮已打开的 B 候选明确回复“验收通过”。记录为 150% 当前屏幕条件下的单机候选整体人工确认，不推定每个专项矩阵逐项完成；旧 DLL 导出失败、两项前台焦点跳过、历史实验对照库缺失和旧 A 未实际冷启动继续保留。没有因此补写自动通过、完成全量阶段门禁，或取得更新日常安装、合并、推送的授权。

### 同屏第二缩放：100%

用户手动将 Windows 缩放从 150% 切到 100% 后，原验收进程 PID 4900 仍报告旧 DPI 144；主代理核验精确 EXE、虚构设置域及所属退出确认框后正常退出该进程，无强杀。随后经同一独立安装根冷启动相同 B 包，不改变进程 DPI 感知、发送伪造 DPI 消息或改动系统显示设置来求通过。

实际显示条件为主显示器 1920×1200、工作区 1920×1152；新实包窗口的 DPI 为 96（100%）。`artifacts/windows-local-closeout/second-scale-100-01/verification.json` 为 7 pass/0 fail，PID 11532 正常退出，`ownedProcessStillRunning=false`。包清单仍为上述相同 SHA，已安装管理工具与受审源码哈希一致。

| 预设 | 首轮 150% / DPI 144 实测 | 第二轮 100% / DPI 96 实测 |
| --- | --- | --- |
| 1080p 横向 | 960×540 | 960×540 |
| 1440p 横向 | 1280×720 | 1280×720 |
| 纵向 | 540×960 | 480×800 |

两份原始单轮 JSON 均保留，固定的 `secondScale=not-verified` 不改写：单轮脚本本身不比较两个真实环境。组合记录 `second-scale-100-01/display-comparison.json` 将相同包、DPI 144 和 DPI 96 两次独立运行关联为“同屏第二缩放自动验证通过”；这不是第二分辨率、跨屏 DPI、热插拔或运行中动态缩放验收。

随后同一 B 包在 100% 下重新打开，供用户检查三档布局、编辑面板及键盘手感；PID 8144、DPI 96、初始 960×540，所属记录见新目录 `manual-handoff.json`。这是有意保留的人工验收窗口，不是测试退出残留；handoff 和组合 JSON 中的 pending 是当时快照，不覆盖为之后的结果。

2026-10-03，用户对这份 100% 窗口另行明确回复“验收通过”。本次记录为第二真实缩放条件下的候选整体人工确认；不依赖首轮确认、不推定每个专项矩阵逐项通过。新增 `second-scale-100-01/manual-acceptance.json` 记录该确认及后续正常退出 PID 8144、`ownedProcessStillRunning=false`，无强杀。两轮原始测试及交付快照均保留。Windows 恢复 150% 仍待用户操作；既有旧 DLL 红灯、两项焦点跳过、历史实验对照库缺失、旧 A 未冷启动及其他延期项不因此关闭，也不取得日常安装更新、合并或推送授权。

用户随后要求继续下一步，主代理只读实测当前主显示器 DPI 144（150%），确认已经恢复原缩放；Dango 产品进程数为 0。新增 `second-scale-100-01/display-restoration.json` 记录恢复事实，不改写此前 pending 快照，不更改 Windows 设置或启动日常安装。日常安装迁移、更新及真实回退验收仍须单独授权。

截至上述候选验收，日常安装、真实用户设置与桌面快捷方式未改。用户随后单独明确授权“执行”日常迁移、更新与真实回退验收；现已完成，具体状态、设置保留及原始诊断失败见[日常交付记录](2026-10-03-windows-local-delivery.md)。`main` 和共享整合分支未变；本批只创建本地阶段提交，未推送、部署或删除工作树。所有失败、工具备份和测试产物保留；无本批服务、监听端口或数据库，全部本次产品进程已正常退出。
