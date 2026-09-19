# 已验收三体能力统一到默认模块

## 授权与计划

用户已分别确认单/双体和三体人工通过，并接受自然滑落；本次授权只包含统一模块入口/容量配置及补齐三体 API、构建和使用文档，同时验证统一前后轨迹一致。保留正式桌面宿主与正式 DLL；不提交、合并或推送。

1. `DangoPhysicsDefaults.MaximumBodyCount` 与 native 默认最大容量统一为 3；调用方可显式配置 1/2/3，超容量明确拒绝。参数版本 1 与 ABI 1 保持，因为参数值和 ABI 数据布局不变。
2. 当前 CMake 仅保留统一模块构建入口 `dango_physics_module`；移除三体预览独立 target 配置，但其已验收 DLL/证据完整保留作为只读基线。现有历史兼容验证 target 不承担默认模块入口。
3. 评审窗口的数量是场景参数；增加 `--physics DLL [2|3]` 统一命令（单体使用无 WPF 样例），旧实验命令保留为兼容入口。样例展示三体、移除/添加、重置与错误处理。
4. 新产物使用 `artifacts/native-spike/physics-module-unified-build`，不覆盖 `physics-module-build` 或 `physics-three-preview-build`。
5. 先行默认容量测试在旧实现中失败；统一前后 1/2/3 体逐帧状态/接触/错误比较，误差 0，包含动作、移除/再添加、重置。运行容量边界、生命周期、原三对碰撞/三种布局专项和模块回归。
6. 更新模块说明、目录 README 和构建说明，写清当前能力、版本兼容、所有权/线程、错误、三体多支撑规则及实际验证边界；自然滑落历史失败保持，不能写成原门槛已通过。

基线：分支 `feat/p1-004-box2d-soft-body`，HEAD `ad33146`。正式 DLL SHA-256 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`，已验收三体 SHA-256 `B2C4D4AD7DC983B9C2C3B0179344ABCF4226EB4A0A28F0E5F6DF53BA16A91094`。

## 结果

两项已完成：统一默认模块入口与容量配置；补齐三体 API、构建和使用说明。物理参数及求解算法未修改。

### 自动验证

- 默认容量先行测试真实失败：期望 3，旧实现实际为 2；`artifacts/p2d-physics-module/unified-red/red.trx`。
- 统一验证共 33/33 通过，`artifacts/p2d-physics-module/unified-tests/unified.trx`，约 2 分 15 秒实际耗时。
- 新增 6 组 1/2/3 体对照各 900 帧，共 5,400 帧，与已验收三体 DLL 的状态、ID、接触计数、错误码完全一致，误差 0；包含动作、移除/再添加和重置，均无 native 错误。
- 原三体专项 12 项在统一 DLL 上通过，包含三对实际碰撞、三种布局的跳跃/拖放、任意 slot 生命周期以及与已验收双体模块对照；原模块 10 项和显式容量 2 拒绝第三体也通过。
- 创建容量 1/2/3、默认 3、第四体拒绝均通过。未提供数量时默认容量 3，不会自动创建三只；场景自行 Add。
- 统一 DLL 的 `--verify-three-window` 通过，三种布局 PNG 位于 `artifacts/p2d-physics-module/unified-window-check/`；实际 WPF 移除/重置按钮与配色、帽子层级检查通过。
- 无 WPF 样例分别运行数量 1/2/3，均完成移除、再添加、重置，输出 `error=0`；替换实体 ID 分别为 2/3/4。
- solution、评审工具、样例 Release 构建均 0 警告、0 错误；三处格式检查、文档相对链接和 `git diff --check` 通过。

此前用户明确接受自然滑落，因此叠放长测的 FirstSlip 保留为观察值，移除了“必须不滑落”的断言；数值安全/实际接触门槛保留。原 3 项失败 TRX 不变，本轮没有重跑十分钟长测，不能声称原“不滑落”门槛通过。全量 Desktop 两项既有 C0 宿主三体红灯未在本轮修复或复跑，正式宿主不在这两项授权范围内。

### 产物及交付状态

- 统一模块：`artifacts/native-spike/physics-module-unified-build/Release/dango_physics_module.dll`，SHA-256 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`。
- 已验收三体对照：SHA-256 `B2C4D4AD7DC983B9C2C3B0179344ABCF4226EB4A0A28F0E5F6DF53BA16A91094`，原目录和文件保持。
- 旧双体对照：SHA-256 `464430D8B30F9C43FCAEB8739B1B3CF2065B3003B8BD2DF571557EBC2CBAD800`，原目录和文件保持。
- 正式桌面 DLL：SHA-256 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`，未替换。
- 既有三体人工验收记录保留；此轮验证的是统一产物的自动一致性和 WPF 渲染/控制链，没有新增用户人工确认。先前预览进程 PID 25976 检查时仍运行，未关闭或重启；本轮未留下新的窗口/服务/端口，测试证据保留。
- HEAD 仍为 `ad33146`，在原 Worktree 完成；未提交、合并、推送或部署。历史对照 DLL 损伤继续按原封装验证记录保留，本次未覆盖任何对照产物。

### 当前入口

构建和 API 见[物理模块说明](phase-1-physics-module.md)，无 WPF 可运行用法见[样例 README](../eng/tools/Dango.Physics.Sample/README.md)。

```powershell
& artifacts/p2d-physics-module/unified-review-bin/Dango.JointReview.exe --physics artifacts/native-spike/physics-module-unified-build/Release/dango_physics_module.dll 3
```
