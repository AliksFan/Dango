# 三体人工预览增量

后续统一状态：本页保留三体预览的历史实施与人工验收。已验收能力现已归并到默认模块，当前构建/使用入口见[模块说明](phase-1-physics-module.md)及[统一验证](phase-1-physics-module-unification.md)。下文预览 target 命令只对应当时版本，不应在当前源码重跑；已验收 DLL 继续保留为对照。

## 当前授权

2026-09-12 用户明确接受当前叠放会自然滑落的表现，并要求开始三体测试，主要由用户人工操作。此增量实现三体实验窗口所需的最小模块扩展；此前三项“不滑落”长测失败记录保留，不改写为测试通过，也不再作为用户当前人工试用的阻断条件。

## 实施约束与设计

- 指定 `p1-004-box2d-soft-body` Worktree，HEAD `ad33146`，保留既有修改。不提交或替换正式 DLL。
- 同一源码构建独立 `dango_physics_three_preview` target，编译容量为 3；默认 `dango_physics_module` 仍为容量 2，已验收二进制保持原位。
- 默认 .NET 创建容量仍为 2；仅显式 capacity=3 进入预览 native 的三体世界。第四只必须被拒绝。
- 世界持有三个稳定 slot/ID 和各自真实 rig，只有一个 Box2D world、每小步一次积分；移除任意 slot 不重建其余实体。空 slot 可再次加入新 ID。
- 单体施力、16 节点、960 Hz、显示样条、3% 容差、跳跃/拖动参数不变。
- 每个上体选取首个符合原支撑条件的稳定 slot 作为本次软体支撑；多支撑时不把一次跳跃反作用重复施加给多个实体。反作用仍作用于被选支撑体的实际接触位置，方向竖直。此规则属于三体预览，待人工观察。
- 只有两个存活 rig 时保持一次原双体接触调用；三个存活 rig 时按 slot 顺序求解三对接触并复核全体最终间隙。最多 8 次全对扫描，每对保留原 96 次局部上限、3% 容差及 1e-5 数值判据；失败锁存，不提高容差继续。三体全对复核避免后处理一对又破坏前一对却静默成功。
- 预览复用原视觉组件，第三只独立配色，全部帽子仍在所有身体上方。新增显式 `--three-collision` 入口；保留原双体入口。
- 预览提供并排、三层落下、两下托一上场景；鼠标选择/拖动，A/D、W、S，暂停/重置、移除选中实体与恢复三体。自然滑落允许；异常仍显示停止及最后安全帧。

## 验证与交付

1. 先增加缺少第三体时必失败的测试，验证真实三体创建和第四体拒绝。
2. 只在新的隔离构建目录构建预览 target；旧 DLL 不覆盖。
3. 对比新 target 的单/双体轨迹与已验收模块，每帧状态完全一致；覆盖静置、运动、跳跃、拖放和重置。
4. 三体有界冒烟覆盖三组布局和三对实际接触、移除/再添加、拖放、重置、错误锁存。人工预览以无错误的可操作场景启动，不宣称完整三体稳定性已验收。
5. Release 构建、受影响测试、格式、文档链接与 diff 检查；实际启动窗口并记录进程、加载 DLL 和日志状态。

人工验收：2026-09-12 用户明确回复“三体通过”，当前三体实验窗口人工验收通过。对应下方记录的三体预览 DLL 版本；自然滑落表现已获接受。此结论不扩大到四体、正式宿主接入或整个 Phase 1。

## 构建与操作

使用仓库已固定的 Box2D 源目录及 Visual Studio CMake，新建隔离目录，不使用历史对照构建目录：

```powershell
& $cmake -S eng/native/box2d -B artifacts/native-spike/physics-three-preview-build -A x64 "-DBOX2D_SOURCE_DIR=$box2d"
& $cmake --build artifacts/native-spike/physics-three-preview-build --config Release --target dango_physics_three_preview
dotnet build eng/tools/Dango.JointReview/Dango.JointReview.csproj -c Release -o artifacts/p2d-physics-module/three-review-bin
& artifacts/p2d-physics-module/three-review-bin/Dango.JointReview.exe --three-collision artifacts/native-spike/physics-three-preview-build/Release/dango_physics_three_preview.dll
```

`$cmake` 和 `$box2d` 按[模块构建说明](phase-1-physics-module.md)配置。三体窗口默认并排；鼠标点选/拖动，A/D 移动、W 跳跃、S 下压；切换布局后重置。支持移除选中团子直至空世界，重置恢复三只；不隐式创建额外实体。

## 实际验证

- 先行三项添加/移除测试在旧模块容量限制下全部失败，证据：`artifacts/p2d-physics-module/three-preview-red/red.trx`。
- 初次新 native 生命周期测试发生访问异常：三体移除一个 slot 后仍有两体参与接触，原抗推循环访问了已移除 rig。已加入存活过滤，保留首次中止记录 `three-preview-first/three.trx`；不是通过跳过移除测试规避。
- 修正后 `DangoPhysicsThreePreviewTests` 12/12 通过，证据：`artifacts/p2d-physics-module/three-preview-slot-fix/three.trx`。覆盖移除 slot 0/1/2、再添加新 ID、第四体拒绝、空世界、重复释放；三对实际碰撞各 600 帧；三种布局各 600 帧含跳跃、拖放、重置；1/2 体三组各 900 帧与已验收模块状态和接触计数完全一致。
- 原模块测试 10/10 通过，证据：`artifacts/p2d-physics-module/three-preview-regression/regression.trx`。
- `--verify-three-window` 通过：真实三体帧渲染三种布局、三种配色、帽子高于全部身体；实际移除按钮逐只移除至空世界、实际重置按钮恢复三体。三张 PNG 保存在 `artifacts/p2d-physics-module/three-window-check/`；已查看“两下托一上”渲染图。
- 新 native `/W4 /WX` 构建通过，solution/评审工具 Release 0 警告、0 错误。格式、文档链接和 `git diff --check` 通过。此轮不重复此前 10 分钟长测或全量 Desktop 既有 C0 三体宿主门禁。

## DLL 与人工现场

- 三体预览 SHA-256：`B2C4D4AD7DC983B9C2C3B0179344ABCF4226EB4A0A28F0E5F6DF53BA16A91094`。
- 已验收双体模块仍为 `464430D8B30F9C43FCAEB8739B1B3CF2065B3003B8BD2DF571557EBC2CBAD800`。
- 正式 DLL 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。
- 真实窗口已启动：PID 25976，标题“Dango 三体人工测试｜实验”，响应正常，进程确实加载新隔离三体 DLL，stderr 为空。窗口保留给用户操作；PID 仅为本次检查时状态，不可后续盲目复用。
- 三体人工验收已由用户独立确认通过，依据为本轮直接反馈“三体通过”。多重接触压力、长时间极端拖动仍可能触发有限求解预算的错误锁存，必须如实记录，不吞错。
- 未创建提交、合并、推送或部署；没有替换正式 DLL，也没有另建 Worktree。前次对照 DLL 损伤继续按[原记录](verification/phase-1-physics-module.md)保留。
