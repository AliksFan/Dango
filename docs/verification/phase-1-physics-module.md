# Phase 1 可复用物理模块实施验证

> 2026-09-17 整理说明：本文的 1/2 体容量和“不含宿主”描述是原封装轮次边界；后续已统一 1/2/3 体并完成独立宿主接入验收，见[当前模块说明](../phase-1-physics-module.md)。历史失败及对照产物损伤记录原样保留。

> 2026-09-12：模块实现、自动门禁与真实实验窗口启动检查完成，用户已明确确认当前模块人工验收通过。新增长测及三体结论单列于[稳定性补充验证](phase-1-physics-module-stability.md)。正式 DLL 未替换；未提交、合并、推送或部署。

## 范围与实现

- 新增无 WPF 的 `Dango.Physics` 项目、版本 1 默认配置和纯数据世界/实体/输入/状态 API。
- 新增独立 `dango_physics_module` native target，容量明确为 2；实现空世界、稳定 ID、真实 rig 添加/移除、固定步、重置、最后安全帧、错误诊断和按 world 释放。
- `dango_collision_review.c` 不再直接包含 `dango_box2d_bridge.c`；模块与正式 bridge 编译同一 self rig/施力/约束原语。
- 新增 `DangoPhysicsPresentationAdapter`，并把 `Dango.JointReview --collision` 改为模块第一消费者。正式桌面 `Box2DSelfPhysicsPresentationWorld` 和正式 DLL 加载路径不变。
- 新增无 WPF、可编译运行的 `Dango.Physics.Sample` 与中文模块说明。

## 先行失败

新增 API/生命周期测试先指向正式 `dango_box2d.dll`。该 DLL 缺少 `dango_physics_get_abi_version`，世界创建、生命周期和多世界 3 项真实失败；默认配置与可识别缺失/旧库错误 2 项通过，命令退出 1。没有删除或跳过红灯。

逐帧等价测试首次在 frame 0 失败。原因是测试直接使用 `1f/60f`，而真实窗口使用 `TimeSpan.FromSeconds(1d/60)` 后按 tick 舍入再转 float。测试改为真实调用值后通过；数值门槛仍为按位相等，没有放宽。

## 自动验证

### native 与模块

- 新隔离目录 `artifacts/native-spike/physics-module-build` 的 `dango_physics_module`、`dango_collision_review`、`dango_box2d` 与 `dango_self_constraints_tests` 均以 Release、C17、MSVC `/W4 /WX` 构建通过。
- 三档各 36,000 帧 self 回归通过，末段漂移保持 `0.000464803219 / 0.00214219093 / 0.00114684191`。
- `Dango.Physics.Tests` 10/10：默认版本、空世界、容量、添加/移除、过期 ID、重置、重复释放、同时多世界、跨线程拒绝、缺失/旧 native 识别，以及四组 1800 帧等价。
- 逐帧组：1 体分离、2 体分离、2 体侧碰、2 体头顶支撑；覆盖静置、左右、下压、跳跃、拖放/释放。每帧每体 42 个状态槽位按位相等，最大误差 0。
- 最小样例真实运行 120 帧：`bodies=2; contacts=0; error=0`。

### 已验收行为回归

- 封装前后以下 JSON 文件 SHA-256 完全一致：
  - `collision/verification.json`：`103CBCC5E80CF237C31C2690D73899E9C246B74B7A5F10D1ADC31D8D61A457FE`
  - `cushion/cushion.json`：`94DB794817F0426CE0CABD31F8F4CB3866DBC4CB43F7A3772B92C76E5CF7CBB0`
  - `support/support.json`：`D473A8C0F0D77E307EDB34470C4804F7CFF5796085704FA4F3ACEC9F360DF24E`
- 故意关闭 3%/展开及抗推/借力能力的两组负例均退出 1。
- 显示适配与原单体/runtime 相关测试 27/27；Desktop 除既有 `DangoInterBodyCollisionTests` 外 184/184；Core 36/36。
- `Dango.slnx` Release 构建 0 警告、0 错误；solution、评审工具和样例格式门禁通过；文档相对链接与 `git diff --check` 通过。

全量 Desktop 186 项中保留两项交接前 C0 红灯：正式 DLL 缺少三体 plural ABI，以及旧 `StackedDangoSceneRuntime.AddDango()` 不增长到三体。它们与本轮“只支持并验收 1/2 体、不替换正式 DLL”边界冲突，因此没有越权修改；全量命令结果为 184 通过、2 失败。

## DLL 与证据状态

- 正式 DLL 保持 SHA-256 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。
- 最终隔离模块 SHA-256 `464430D8B30F9C43FCAEB8739B1B3CF2065B3003B8BD2DF571557EBC2CBAD800`。
- 封装前证据在 `artifacts/p2d-physics-module/before-*`，封装后证据在 `after-*`；旧 P2D-007C 证据未覆盖。

已知证据损伤：初次验证 C 源拆分时误在历史 `collision-ratio-build` 目录执行重建，覆盖了交接指定的对照 DLL 文件。覆盖前已核对其 SHA 为 `97D787...A8A6E` 并完成本轮全部三组封装前验证；之后在现有 artifact、其他 Worktree 和本机相关工作区未找到同 SHA 副本。重建文件不能冒充原二进制，当前该路径 SHA 为 `B00A4E6D...B5DD`。正式 DLL 与封装前 JSON 证据未受影响；封装前后三份 JSON 逐字节一致，另外 7,200 帧模块/源基线逐位一致。此项仍作为交付风险保留，不写成已恢复。

## 实时、人工与交付状态

- 新模块实验窗口 PID 24256 已从 `artifacts/p2d-physics-module/review-bin` 真实启动，窗口响应、标题正确、错误日志为空，并确认进程实际加载隔离 `dango_physics_module.dll`。窗口保留供用户操作；进程存活不等于外形/输入手感人工验收。
- 用户人工手感验收：2026-09-12 明确回复“人工验收通过”；适用于此前交付的单/双体实验窗口，不沿用到新增长测或三体。
- 稳定性补测：持续碰撞、抽走支撑、拖放/重置通过，居中及左右 5% 长期叠放失败；详见[补充记录](phase-1-physics-module-stability.md)。三体动态稳定性因容量为 2 尚无法执行；全部 DPI/硬件、正式桌面宿主接入和多人同步仍未验证。
- Git：仍在 `feat/p1-004-box2d-soft-body`、HEAD `ad33146d499ffb3f197cc68b2a30c5ccb9182ab4`；保留原有 dirty 内容并新增本轮文件，无提交、合并、推送、发布或部署。
