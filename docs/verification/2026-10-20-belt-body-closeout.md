# 新身体候选收尾与 F7 设计交接

日期：2026-10-20。分支 `codex/face-body-belt-integration`，调查起点 `555d40b`，产品代码 `0c9c3ba`。用户授权“收尾并开始F7的设计与计划”；本轮完成有界存储调查、状态核对和 F7 设计／计划草案，未实施 F7。

## 1. 收尾结论与验收范围

F1～F6 的隔离首页／捏脸自动工作已完成，当前候选使用新身体、真正实时三参数和默认启用的可见形变休眠 v2。保留[原实施记录](2026-10-19-belt-body-implementation.md)与[独立休眠合同](2026-10-19-belt-visible-deformation-sleep-v2.md)的通过、失败和设备覆盖边界。

| 项目 | 本轮核对结果 |
|---|---|
| 活动窗口 WASD／Space | 用户在新版候选实际体验后明确“通过”；本项人工验收已记录，受影响回归 240/240 |
| 圆形滑块／短窗口布局 | 已按截图反馈修订，沿用 13/13 布局证据；本轮未新增视觉结论 |
| 连续外圈／实时改参／休眠 | 沿用最终联合 230/230、binding 9/9、EXE 两进程保存恢复及真实 CPU 对照；本轮未重跑数学／CPU 长测 |
| 整体人工范围 | 最近一次“通过”记录为活动窗口输入确认；未逐项确认的捏脸保存／恢复、休眠体验和设备专项不补造验收 |
| 迁移保存间歇失败 | **仍开放：本轮未复现，异常根因未知**，见第 2 节；不因多轮绿灯关闭 |
| 房间／多人 | 新内核尚未接入；F7 只有待批准草案，不标实施或验收通过 |

这里的“收尾”表示证据和待办已经交接，不表示整个阶段已验收或所有风险消除。当前候选保留可用，整体验收状态仍按实际覆盖记录。

## 2. 迁移保存失败调查

### 2.1 历史证据与可支持的判断

已定位数据来源工作树的 `body-migration-investigation/body-migration-group-3.trx`：`LegacySchemaOnlyMigratesOnExplicitSave` 的 Schema1 用例在 2026-10-19 报 `TrySave=false`，发生在 writer 诊断加入前，没有具体异常类型／HResult／堆栈。本轮将原 TRX 精确复制到新证据目录，未覆盖旧日志。

该用例失败前已验证黑色身体、默认眼睛、迁移待保存以及取消后仍待保存。Schema1 不进入旧身体模型的显示确认门；结合 `AppearanceSettingsSession.TrySave` 的分支，支持将该次失败收窄到 `store.Save` 抛出并被会话捕获的异常。**这不是具体根因**，不能认定文件占用、扫描器、权限或硬件问题。

检查了 `AppearanceSettingsStore`、`AppearanceSettingsSession`、`AtomicTextFileWriter` 和原用例路径：每次测试目录和 writer 临时名均为 GUID；未找到共享 session／writer、跨用例目录删除或已有锁定测试未释放句柄的证据。writer 仍按原逻辑建同目录临时文件、写入并 Move，未增加重试或放松断言。

现有 `AppearanceSaveDiagnostics` 记录 writer 最终异常；建目录、写入、Move、清理都在其观察范围，但清理异常可能遮蔽首个异常。它没有独立临时内容验证／最终重新读取阶段收据；最终读取仍由原断言覆盖。因此原 TRX 不能反推出异常发生阶段。

### 2.2 本轮实际验证与停止点

使用独立 Release 输出，保留既有验收进程与标准输出；不操作真实存档。定向 baseline 包括 AppearanceSettings、BodySettingsMigration、DangoAppearanceConfiguration、DangoBodyColorPolicy 和 SoftBodyTuning，**129/129 通过**；原 119 项数据集再运行 10 轮，**1190/1190 通过**。数据门不创建玩法窗口，不注入鼠标键盘。

证据位于 `artifacts/appearance-closeout-2026-10-20/`：`baseline.trx`、`data-round-1.trx`～`data-round-10.trx`、对应日志、`historical-body-migration-group-3.trx`、`results-summary.json` 和 `investigation-receipt.json`。结果不是不同功能数量之和，也不能抹去旧失败。基于有界复跑和路径核查仍未复现，停止继续盲目重跑；本轮没有足以支持生产修复的具体根因，**src/tests 未改**。

下一次原用例失败必须保留原 TRX、schema、会话 `StatusMessage`、writer `LastFailure`／HResult／堆栈及该 GUID 目录。若 writer 已成功而最终读取失败，补预期／实际原字节摘要、schema 和临时文件清单；若清理异常遮蔽首因，再设计仅用于测试的逐阶段收据。不能先加保存重试，或修改原断言以获得通过。

此项为 F7 持久化／正式接入交付前的开放门禁：先获得原失败根因及针对性修复证据，或由用户明确接受该已披露风险，才能关闭相应准入；不阻止只读设计或日后另行批准的隔离物理研究。本轮不把未知根因登记为“已修复”。

## 3. F7 交接

新增[房间接入设计草案](../planning/2026-10-20-belt-room-f7-design.md)与[实施计划草案](../planning/2026-10-20-belt-room-f7-implementation-plan.md)。推荐连续外圈房间模块统一协调身体／球／平台接触、一个 room owner、完整世界快照和逐身体／接触岛休眠。该方案尚待用户批准，不代表技术准入已通过。

分为：合同与基线 → 静态接触 → 动态外物／多体 → room owner／热更 → 家具事务与可靠外来唤醒 → 普通房及地面安全检查点 → 桌宠同世界分层 → 联合与人工验收。物理门可失败；单体成功不能外推房间，虚拟多体不等于联机。

草案经独立架构／并发／数据一致性审查，补足两项合同：家具 staging 后由 owner 重新校验并串行 prepare／持久提交／apply，token 绑定 tick 和配置；FIFO 旧帧保留对应版本家具几何，显示／命中不读取 session 最新值混帧。对应调度反例已写入计划，复核无新增重大合同问题。这仅证明草案经过审查，不证明新接触数学、实现或运行验证已通过。

普通房继续稳定 roomId、最多 3 间、原 30 件家具及球锚点保存规则；桌宠仍不接家具或新增位置存档。房间实际缩放须重新标定休眠可见预算，不外推预览 420 DIP 的原测量。旧来源的保全 refs、bundle 和忽略产物继续按[交接记录](../archive/2026-10-19-body-physics-handoff.md)保留，本轮没有删除／重建源分支或 Worktree。

## 4. 保护与交付

受保护 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll` 的 SHA256 保持 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`；main 原 `AGENTS.md` 修改摘要保持 `7F2DD53E64CB4E3B32445CBD798D5ABD01AB4B283EB096F5A854674B6802A029`，未据为本轮修改。main 仍为 `788456b`。

本轮没有新建／操作前台玩法窗口，数据测试子进程已退出；既有人工验收窗口保留供用户使用。只保留 ignored 的虚构测试目录、TRX、收据和构建输出，不清理持久数据。未启动服务／端口／数据库或云资源。

文档检查覆盖本轮 7 个文件、273 个本地链接目标，均存在；差异空白检查通过。数据回归的 11 份新 TRX 合计 1319 次执行、1319 次通过、零失败／跳过，其中包含同一 119 项数据集的重复执行，不表示新增了 1319 项独立验证。

本轮交付为文档及调查证据、本地提交；未合 main、推送、安装／部署或替换生产 DLL。F7 草案待批准，产品实施未开始；完整 Windows、既有 59 FPS、其它 DPI／真实120Hz／高对比／GPU present 与多人专项继续按各自合同记录。
