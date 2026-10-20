# 2026-10-20 木平台 -707 有界诊断

状态：**已后台复现，物理问题后置，未修复**。用户优先完成门廊／配饰接入；本轮不调整数学、门限、步态或回退旧内核，不修改卸载界面逻辑。

基线 `81a4a07010858e81e3a20010b6b243156f4f93f3`，独立分支 `codex/belt-platform-diagnosis`。原始证据及可复制最小复现放在本 worktree 的 ignored `artifacts/belt-platform-diagnosis/`，包括 `investigation.md`、`BeltPlatformDiagnosisTests.cs`、`first-failure-x6-jump20.json`、两份 TRX 和摘要清单。证据须连同本 worktree 保留；Git 提交只包含本记录。

只读来源：`codex/belt-room-f7` worktree 中 `artifacts/belt-room-f7-collection-r3/review-data-95fe378/rooms/b19649ef-f5d0-4808-852a-b8a986c12688/furniture-layout.json` 是已声明的虚构人工验收存档。木平台 top-center anchor=(1715.6051461521163,126.65960390330395)，尺寸272×21；resume.json 的地面X=1513.9602355957031。该检查时数据根未发现 appearance.json，因此复现**明确使用默认三参数100／50／50**，不声称绑定用户当前应用的外观参数。初始 world x=6m 只取接近该恢复位置的独立有界案例，不声称逐输入重放用户操作。

实际 DLL SHA256：`DB4F3494E8F6FF64E783845903647D7916DC2F584715C6B9539D3AB2789A9085`。从x6m先稳定180tick，再按原布局几何插入平台，向右输入并在第20tick跳跃；第63tick（跳后43tick，约0.717模拟秒）首次失败。已接受tick243保持，attempt244返回 **-707／UNCERTAIN**，底层 **-605**，diagnostics 的 thetaDepth=20、timeDepth=16、reason=3。

首失败 JSON 保存输入、完整前后集合、身体快照及 diagnostics。BodySnapshot、Curve、Scaffold、Stats、Items逐字段序列化比较完全不变，tick也未前进；原生拒绝并回滚有效。本轮3条原生失败保留：x5／jump90／step115，x5／jump120／step160，x6／jump20／step63；第一轮起点0持续向右的5种跳跃时机未失败，只证明这些被测序列。

定位到 `eng/native/box2d/dango_belt_room.c` 的 `room_certificate`：静态／扫掠证书在theta20和time16后仍未决，触发reason3；集合平台 `collection_body_platform_position` 调用同一证书。它属于连续几何／接触求解问题，尚未证明是一个可局部修复的应用逻辑bug。`FailLocalHandoff` 后续尝试卸载已故障世界，`EnsureHealthy`拒绝后家具状态文案覆盖先前driver报告，可以解释用户看到的卸载提示；不是该物理首因。

复现命令见证据 `reproduce.ps1`：在**独立临时检出**复制最小测试，指定冻结DLL，运行定向 dotnet test；当前预期 FAIL 并保存原生首失败。该失败未纳入正常测试集，未将其转写为成功验收。

本轮无窗口、焦点、全局输入；未修改或关闭用户程序，未写用户存档；没有服务、端口、数据库操作，测试进程已退出。生产代码零变更，只有本地诊断记录提交；未合main、推送、部署。非默认三参数、完整EXE实时操作及人工观感未验证，平台物理缺陷保持OPEN。
