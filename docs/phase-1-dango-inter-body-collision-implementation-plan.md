# Phase 1 团子间碰撞实施计划

> 2026-09-17 历史资料归档：下文状态、失败门槛和候选参数仅适用于本文对应路线，不代表当前统一模块或眼帽接入仍停在该步骤，也不因后续方案获认可而变为通过。当前实现与验收边界见[模块说明](phase-1-physics-module.md)和[工作树整理](verification/2026-09-17-worktree-checkpoints.md)。

> 状态：已批准，C0 已执行、C1 部分实施；触发穿心停止点
>
> 批准日期：2026-09-08
>
> 实际结果：[P2D-007C 验证记录](verification/phase-1-p2d-007c-inter-body-collision.md)。共享 self 双体试验在第 38 帧出现节点进入另一只核心附近；暂停后续实施并复核设计。
>
> 日期：2026-09-07
>
> 已批准设计：[Phase 1 团子间碰撞设计](phase-1-dango-inter-body-collision-design.md)
>
> 阶段任务：P2D-007C 新单体机制上的双体与三体碰撞
>
> 本计划在用户批准前只用于审查，不授权修改运行时代码、替换正式程序、提交、合并、推送或部署。

## 1. 目标

在现有新单体物理机制上建立一个 1～3 只团子共享的 native world，并在正式 `Wallpaper/Window` 桌面场景中完成：

- 跨团子轮廓节点真实接触；
- 双体轻重叠、局部挤压、释放和不粘连；
- 三体自然落下、承重、偏心滑落和支撑移除；
- 任意团子的鼠标拖动，主团子独占 A/D/S/W；
- 不可见左右屏幕墙及与地面分离的接触判断；
- 三色、眼睛、帽子和完整真实骨架；
- 共享 world 的异常锁存、资源释放和正式宿主生命周期。

实现不得接回旧 paired/stacked 算法，不得改变已验收单体形状、满意 16 项参数、`TopInwardForce=0.5`、输入语义、二次 B 样条渲染或异常保护。

## 2. 开始实施前的基线

每次开始或恢复本计划时先只读核对：

1. `git worktree list --porcelain` 仍把 `p1-004-box2d-soft-body` 工作树（实际目录由本机 `git worktree list` 查询） 注册到 `feat/p1-004-box2d-soft-body`；
2. HEAD 仍以 `4c0dbe78a5b490ec19571b840b27b6952aa98dec` 及当前未提交成果为基线，或记录用户随后明确批准的新基线；
3. 所有现有 modified/untracked 文件仍被保留，没有 reset、clean、强制检出或来源不明覆盖；
4. 单体默认参数表、角度硬约束、顶部内收力和快速拖动限速文档未发生未评审变化；
5. 正式 `Wallpaper/Window` 仍共同使用 `StackedDangoSceneRuntime`，且单体阶段数量按钮仍明确禁用；
6. 当前正式进程如需替换，必须重新按可执行文件完整路径核对 PID，不能沿用历史 PID；
7. `artifacts/before-fast-drag-fix-20260907/` 备份存在且可读。该备份只保留，不删除、不覆盖。

基线不一致或同一文件出现另一执行者的新修改时停止，先报告所有权冲突，不合并猜测。

## 3. 范围、排除项与文件所有权

### 3.1 预计修改范围

| 模块 | 预计文件 | 职责 |
|---|---|---|
| 设计与计划 | `docs/phase-1-dango-inter-body-collision-*.md`、`docs/development-plan-v0.1.md` | 范围、门禁、阶段状态 |
| native world | `eng/native/box2d/dango_box2d_bridge.c`、`dango_self_constraints.h`、`dango_self_constraints_tests.c` | 共享 world、1～3 rig、节点接触、地面/墙、约束、诊断、释放 |
| native 构建契约 | `eng/native/box2d/CMakeLists.txt`、`build-box2d.ps1`、`dependency.json`、`README.md` | ABI/export/hash/native 门禁 |
| 托管加载 | `src/Dango.Desktop/Physics/Native/Box2DNativeLoader.cs` | plural API、参数封送、帧与诊断 |
| 参数与表现 | `src/Dango.Desktop/Physics/Tuning/DangoSelfPhysicsParameters.cs`、`Physics/Presentation/` | 独立跨团子半径、参与者输入/输出和共享 world 适配 |
| 正式运行时 | `src/Dango.Desktop/Rendering/Dango/StackedDangoSceneRuntime.cs`、`DeformableDangoVisual.xaml.cs`、两种宿主窗口 | 数量、命中、拖动、墙映射、三色、附件和骨架 |
| 控制中心 | `ApplicationCommandController.cs`、`ControlCenterWindow.xaml(.cs)` 及必要设置/宿主协调文件 | 恢复 1～3 数量入口与只读诊断，不新增验收记录 |
| 自动测试 | `tests/Dango.Desktop.Tests/Physics/`、`Rendering/`、`DesktopHost/`、`AppShell/` | 先失败验收和回归 |
| 验证记录 | 新增 `docs/verification/phase-1-p2d-007c-inter-body-collision.md`，更新验证入口 | 实际命令、输出、实时与人工状态 |

实施时只修改完成本任务实际需要的文件。表中列出的是上限，不表示必须机械触碰全部文件。

### 3.2 明确不修改或不删除

- 不修改 `Dango.Core` 的业务实体、房间、同步、权限或权威状态。
- 不恢复或改写 `dango_box2d_paired_soft_body_*`、`dango_box2d_stacked_soft_body_*` 的行为来承载新功能。
- 不删除旧 paired/stacked 类、测试、文档或预览文件；是否清理历史入口另需明确授权。
- 不改变满意 16 项参数、顶部内收力、长度/角度门槛、渲染曲线、眼睛或帽子美术。
- 不增加核心碰撞代理、隐藏刚体壳、额外碰撞节点、顶部墙或测试专用持续外力。
- 不进入 P2D-008 性能结论，不实现账号、好友、房间或网络同步。
- 不提交、合并、推送、部署、删除 Worktree/分支/备份或创建正式发布标签。

## 4. 目标实现结构

```text
Wallpaper / Window client bounds + input
                   |
                   v
        StackedDangoSceneRuntime
        | count 1..3
        | active drag participant
        | primary keyboard control
        | render and attachment ordering
                   |
                   v
   MultiDangoSelfPhysicsPresentationWorld
        | participant frames
        | ground/wall/inter-body diagnostics
        | last valid frame and error latch
                   |
                   v
      Native plural self-world ABI
        | one Box2D world
        | ground + left/right wall
        | up to 3 complete self rigs
        | separate boundary/inter-body shapes
                   |
                   v
  current constraints + speed limiter + Box2D
```

`StackedDangoSceneRuntime` 名称可继续沿用以减少无关重命名；其正式物理成员改为新的 plural self-world。单体 `Box2DSelfPhysicsPresentationWorld` 保留为自动回归适配器，但正式桌面不并行运行一个单体 world 和一个碰撞 world。

## 5. native 与 ABI 方案

### 5.1 plural self-world

新增明确的 plural self-world C ABI，候选命名为：

- `dango_box2d_self_bodies_create`
- `dango_box2d_self_bodies_step`
- `dango_box2d_self_bodies_destroy`

最终命名可在实现时按现有导出风格微调，但必须与 loader、构建脚本和 `dependency.json` 一致，不能冒用旧 paired/stacked 入口。

共享句柄包含：

- 一个 Box2D world；
- 一个地面 body/shape；
- 左、右静态墙 shape；
- 最多三套完整 self rig；
- active count、拖动参与者、每只选定节点、接触诊断、最后有效错误状态和初始分配计数。

### 5.2 碰撞过滤与两类节点 shape

每个轮廓节点承载两类共心 circle shape：

1. **boundary shape**：保持当前节点半径，只与地面和左右墙碰撞；
2. **inter-body shape**：使用独立 `InterDangoCollisionRadius`，只与其他参与者的 inter-body shape 碰撞。

同一参与者的 inter-body shapes 使用唯一负 group index 禁止互撞；不同参与者再由 category/mask 允许接触。核心 mask 始终为零。这样不会为团子间观感改变既有地面轮廓，也不会引入核心代理。

### 5.3 动态墙与接触分类

- `step` 每帧接收有限的 `left_wall_x/right_wall_x`，要求左值小于右值且能容纳批准最小几何。
- 墙变化时只移动静态边界 shape/body，不重建 rig，不清速度。
- 地面、左墙、右墙和参与者分别使用可识别 category/user data；诊断按另一 shape 的身份统计，不能把所有 `GetContactData` 相加后推断接地。
- `Grounded` 只由地面 contact 或现有合法底部容差决定；左右墙 contact 单独返回。

### 5.4 约束和限速

- 当前每只 rig 的面积力、顶部内收力、接地外扩、径向排斥、角度屏障、凸性屏障、局部角均衡和硬约束顺序保持不变。
- 每个小步先施加每只输入和内部力，再统一 `b2World_Step`，随后对三只分别求硬约束。
- 任一 rig 约束失败导致整个 plural step 失败；托管层释放共享 world 并锁存全组最后合法帧。
- 统一限速先扫描当前 active rigs 的全部核心和节点，若全局最大速度超过 12，则对全部 active 核心和节点乘同一比例；非有限速度仍返回异常。
- native ABI 由当前 13 升至 14；loader、export 检查、依赖元数据和测试同步更新。

## 6. 托管契约

### 6.1 输入

表现层定义固定容量为三的参与者输入，至少包含：

- 稳定参与者索引与是否 active；
- 初始/权威锚点；
- 仅主团子的 horizontal、downward、jump 输入；
- 当前唯一 drag participant、drag active 和 canonical target；
- 当前宿主 canonical 左右墙位置；
- 当前物理参数和 `InterDangoCollisionRadius`。

所有数组长度、active count、参与者索引、墙范围和数值有限性在进入 native 前校验。无效输入得到受控错误，不截断、绕过或默认为主团子。

### 6.2 输出

每帧至少返回：

- 每只 active 团子的 16 个有序局部轮廓点、核心位置、面积、质心、最大速度、接地状态和固定拖动节点；
- 全组跨团子 contact 数；
- 每只地面、左墙、右墙 contact 数；
- native error code、是否锁存、是否使用最后合法帧；
- 创建/销毁后的分配差值。

输出校验继续检查点数、有限值、正面积、节点顺序、自交、角度和长度；异常不能进入 WPF。

### 6.3 单体适配与参数

- 现有单体适配器和测试继续覆盖单只行为，必要时让它委托 plural API 的 active count 1，而不是维护两份内部算法。
- `DangoSelfPhysicsParameters` 只新增一个跨团子碰撞半径字段及其合法范围；默认值先采用当前节点半径进入 C3 实测，不添加未经设计的新摩擦/形状参数。
- 参数改变在下一物理步更新 inter-body shapes；若 Box2D 不支持安全原位调整，则要求显式重新生成，不能悄悄重建并丢失状态。实现前先以官方 API/头文件确认。

## 7. 分步实施与验收

### C0 — 冻结基线并建立先失败验收

**修改范围**

- 新增 P2D-007C native/C# 测试和验证记录骨架；不改生产实现。

**必须先观察到的失败**

- 当前 native export 不存在 plural self-world；
- 当前正式运行时 `DangoCount` 固定为 1，Add/Remove 返回失败；
- 当前无法创建两只共享新 self world，也不存在跨团子或左右墙诊断；
- 当前拖动契约没有 participant identity。

先记录具体测试名、命令、失败断言和退出码。若新增测试意外通过，停止检查其是否只验证类名、模拟点或旧 stacked 路径。

**单体基线**

- 运行当前 native 构建/三档 36,000 帧；
- 运行 Desktop/Core 全量测试、格式、Release 构建和 `git diff --check`；
- 记录实际通过数和当前 native ABI/hash，不直接复制旧报告数字。

### C1 — plural native world、三套 rig 与资源生命周期

**实现**

- 从当前 self 实现提取可复用 rig 初始化、施力、约束、采样和销毁函数；
- plural world 创建 1～3 套相同 rig，不调用旧 paired/stacked create/step；
- inactive rig 无活动 shape/contact，或在 active count 变化时按明确生命周期创建/销毁；
- 完成 ABI 14、loader export 探针和重复 create/step/destroy。

**验收**

- 1、2、3 只分别返回独立 16 点合法帧；
- 三只初始锚点一起平移时，核心和全部节点同步平移且速度清零，仅发生一次初始化；
- 1↔2↔3↔1 循环至少 1,000 次，无句柄、body、shape、joint 或 Box2D byte 泄漏；
- count 越界、数组容量不足、非有限值、重复 destroy 得到受控结果，不崩溃。

**停止点**

若无法在不复制三份 self 算法的情况下共享当前约束，先重构内部纯 C rig 边界并保持单体回归；不得让旧 stacked 变成 plural 实现。

### C2 — 左右屏幕墙与边界诊断

**实现**

- 增加 boundary/inter-body 两类 node shapes 和过滤；
- 创建地面、左墙、右墙，按 step 输入更新墙位置；
- 分离 ground/left/right/inter-body contact 统计和 `Grounded`。

**验收**

- 单只无遮挡静置外形与 C0 基线在既有容差内等价；
- 左右各推 300 帧，核心与合法轮廓不穿墙、不翻面、不锁存；
- 反向离开后 120 帧内对应墙 contact 为零；
- 只贴墙时 `Grounded=false` 且 W 不触发跳跃；
- 墙位置变化后下一步生效，核心、节点速度和参与者身份不重置；
- 过窄和反向墙范围得到明确错误并保留最后合法帧。

### C3 — 双体接触、局部挤压与参数选择

**实现**

- 启用不同参与者 inter-body shape 接触；
- 只调整 `InterDangoCollisionRadius` 首轮候选，不改单体参数或渲染；
- 增加从真实 WPF 二次 B 样条测量视觉接缝的测试工具。

**验收**

- 分离时 contact 为零，靠近/落下后出现真实不同参与者节点 contact；
- 轻接触稳定窗口的视觉重叠为 `0～3 DIPs`；
- 强挤压接触侧压缩至少为径向静止长度的 3%，远侧平均变化不超过接触侧的 25%；
- 释放并完全分离后 120 帧内 contact 清零；
- 全过程逐帧通过有限值、速度、面积、自交、长度和 90°～180°；
- 记录物理间距、视觉接缝、接触侧/远侧变化和所选半径，不只记录 contact 数。

**停止点**

若节点互锁、穿心、承重需要核心代理，或碰撞半径无法兼顾无缝与不穿透，立即保存失败帧并回到设计；不得进入 C4/C5。

### C4 — 正式运行时、数量、任意团子拖动与渲染层

**实现**

- `StackedDangoSceneRuntime` 改用 plural self-world，启动 active count 1；
- 恢复 Add/Remove 到 1～3，按上方左右轻微错位生成、后加先移除；
- WPF 命中选最前身体，拖动开始时固定 participant，再固定该身体最高节点；
- A/D/S/W 只封送到 participant 0；
- 三色身体与每只附件消费各自同帧数据，所有帽子最后统一绘制；
- 两种宿主向运行时提供实际客户区和 DPI 映射，墙随正式窗口变化。

**验收**

- 初始 1，只能加到 3、减到 1；按钮启用状态、数量文本和真实 world 一致；
- 添加不会生成在现有轮廓内，移除时先取消该 participant 拖动并释放资源；
- 任意一只可命中拖动，重叠时选择最前身体，按住期间不换参与者或节点；
- 键盘只移动主团子，新增参与者只因重力和真实接触移动；
- 骨架开关展示每只真实 1 核心、16 节点、16 径向、16 外围拓扑；
- 帽子无碰撞且全局最高，眼睛/帽子跟随不因接触跑出轮廓；
- 不创建独立预览窗口或控制中心验收数据。

### C5 — 三体承重、滑落与长期稳定

**先固定统计门槛**

用户最新确认的小偏心范围为宽度的 **5%**，取代之前的 10% 必过标准。首轮原生测试固定 W 为无负载初始单体经现有径向外扩和二次 B 样条采样后的横向宽度（512 点、物理单位）；仅用初始轮廓作固定尺度，不随受压变窄重算。必过测试相邻层中心偏移为 0、±0.05W，三体先使用每层同向偏移，交错方向另列未验证。±0.10W 保留为探索，不再要求不滑落。此定义不把 5% 变成硬性支撑边界：超过范围允许自然滑落，但不强制滑落。

`stack-check` 拒绝绝对值超过 0.05 的比例，`stack-diagnostic` 仍允许到 0.10 以保留历史探索。旧 10% 失败记录不删除，也不改写为数值通过。摩擦系数、96 轮联合求解预算和接触容差 1e-5 不因范围调整而改变。

`stack-check 数量 厚度 偏移比例` 保留 36,000 帧上限、几何门禁及最终支撑必要条件；在小偏心范围内，上层一旦直接落到地面即可提前判定“不滑落”失败，输出实际帧与节点并销毁 world，不等待已经失败的场景跑满。稳定窗口及负载压缩统计仍需后续补齐，不能把仅未落地写成完整承重通过。

先运行不带通过断言的诊断采样，记录居中和偏心场景的底层/中层/顶层垂向压缩、接触对、质心投影和滑落起始帧。依据已批准行为在测试中固定门槛后再实现调优；不得在失败后追着结果降低。

**验收**

- 三只从不同高度自然下落，居中后只有相邻层形成有效承重路径；
- 稳定窗口中底层平均垂向压缩显著大于顶层，且两组接触持续存在；
- 质心投影在有效支撑区内的轻偏心可短暂稳定，超出后 180 帧内开始持续滑落并最终接地；
- 移开底层后上层失去支撑并下落，不被永久摩擦拖走；
- 1、2、3 只各 36,000 帧逐帧通过所有单体约束、跨团子接触合法性、最大速度与无锁存；
- 超速时全组速度统一按比例耗散，非有限或约束失败仍锁存整个 world。

**停止点**

如果只有高摩擦、隐藏横向力、预制位移或修改单体基线才能堆叠/滑落，停止并报告，不继续调参。

### C6 — 全量门禁、正式替换与人工验收

**自动门禁**

```powershell
pwsh -NoProfile -File .\eng\native\box2d\build-box2d.ps1 -Configuration Release
pwsh -NoProfile -File .\eng\Validate-PlaceholderAssets.ps1
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release --no-build
git diff --check
```

另行定向运行 P2D-007C、单体三档 36,000 帧、快速拖动、渲染、控制中心和两种宿主测试，并在验证记录中写明实际测试数、耗时、ABI 和 native SHA-256。

**正式替换步骤**

1. 确认所有自动门禁通过；
2. 验证现有正式 Release 备份可读；
3. 通过完整可执行路径重新解析正在运行的正式 Dango PID；
4. 只停止匹配该精确路径的进程；
5. 构建完成后再启动 `src\Dango.Desktop\bin\Release\net10.0-windows\Dango.Desktop.exe`；
6. 报告新 PID、路径、ABI、native hash、仍运行的相关进程和保留的备份。

找不到精确进程时不按名称广泛结束进程。正式构建失败时不启动半替换输出。

**实时与人工验收**

- 先由 Agent 在 Wallpaper/Window 检查启动、隐藏/恢复、宿主切换、数量、拖动、墙、接触、异常诊断和进程响应；
- 再请用户按已批准设计的 10 项桌面观察清单验收观感；
- 用户确认前，状态保持“自动门禁通过/等待人工验收”，不能宣称 P2D-007C 完成。

## 8. 验证证据分层

最终报告必须分别列出：

- **静态门禁**：native 构建、格式、Release 构建、Core/Desktop 测试、diff check；
- **集成门禁**：plural ABI、1～3 生命周期、接触、墙、资源释放和 WPF 接缝测量；
- **实时行为**：正式 Wallpaper/Window 中的实际进程、数量、拖动、墙、堆叠和切换；
- **人工验收**：用户是否确认形变、重叠、承重、滑落、墙和干扰程度；
- **交付状态**：是否提交、合并、推送、部署；默认全部为否。

局部自动测试、离屏渲染或 Agent 观察不能代替用户人工验收。

## 9. 阶段完成条件

只有同时满足以下条件，P2D-007C 才能请求用户确认完成：

1. C0 的失败证据真实记录，后续实现让对应测试通过；
2. 单体全部基线回归通过，没有改变满意外形和输入；
3. 双体轻接触、强挤压、释放和不粘连通过；
4. 三体承重、偏心滑落、支撑移除和 36,000 帧稳定通过；
5. 左右墙、地面区分、动态宿主边界和墙上不可起跳通过；
6. 任意团子拖动、数量、三色、眼睛、帽子和完整骨架通过；
7. 正式 Wallpaper/Window 实时验证完成；
8. 用户完成人工观感验收；
9. 验证记录、开发计划和运行说明与实际结果同步；
10. 没有把提交、合并、推送、部署或 P2D-008 混入本任务。

计划实施完成仍不自动授权提交、合并、推送、部署或结束整个 Phase 1。
