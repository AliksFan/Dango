# Dango Phase 1 方向控制实施细节

> 状态：方向控制实现与自动门禁通过，待真实 Windows / 人工验收
>
> 日期：2026-09-03
>
> 适用范围：`feat/p1-004-box2d-soft-body` 上的 P2D-006 方向控制增量
>
> 已批准设计：[方向控制与普通窗口模式设计](phase-1-direct-control-and-window-mode-design.md)
>
> 既有实现基线：[Box2D 视觉软体实施计划](phase-1-box2d-soft-body-implementation-plan.md)、[P2D-006 验证记录](verification/phase-1-p2d-006-single-dango-motion.md)

## 1. 目的与授权边界

本计划把已批准的方向控制拆成可先失败、可逐步实现、可在真实 Windows 桌面验收的增量。实施完成后，用户可以在控制中心开启键盘控制，并在方向键与 WASD 两套固定方案中二选一；当前 P2D-006 的自动快速动作时间线被移除，团子由用户输入触发低速左右走动、单跳/长按复跳和轻微挤压。

本文只覆盖方向控制和当前 WorkerW/P2D-006 接入。普通窗口模式、默认宿主切换、Overlay 删除、窗口位置恢复与宿主迁移均不在本增量内。方向控制完成并取得 WorkerW 人工验收后，普通窗口模式才能作为顺序后继开始；本文不授权窗口模式编码。

用户于 2026-09-03 明确授权开始方向控制实施。该授权不包含普通窗口模式、提交、合并、推送、部署或开始 P2D-007。

## 2. 已确认基线

- P2D-006 当前以 `SingleDangoMotionController` 持有 Core 权威动作状态和位置，`SingleDangoMotionRuntime` 只管理表现世界生命周期。
- WorkerW 以 60 Hz `motionTimer` 调用 Core、位置映射和物理表现；表现帧不能产生落地、抓岛或其他权威结果。
- 当前 `WorkerWWindow` 使用 `AdvanceAuthoredAction` 与 `ApplyAuthoredMotion`，按固定时间自动串联 `Idle → Wander → ApproachIsland → ClimbUp → OnIsland → ClimbDown → Falling → Landing → Idle`，并用线性插值移动。这正是本增量要移除的快速自动时间线。
- P2D-006 的格式、Release 构建、Core 24/24、Desktop 100/100、native 生命周期和 WorkerW 宿主循环已有通过记录；动作观感与交互人工验收仍未完成。
- P1-003 已验证 WorkerW 原生分层、团子图标上下层、桌面图标优先输入和鼠标拖动。键盘实现必须复用当前 WorkerW 目标句柄与生命周期，不读取窗口标题、桌面内容或图标文本。
- 当前控制中心设置 Schema 为版本 1，保存窗口尺寸与桌面对象层级；现有“允许与团子互动”关闭时，键盘控制必须随之失效。

若编码开始前上述类、Schema、P2D-006 状态或测试数量已变化，先更新本计划中的文件边界和验收基线，不能把旧计划强套到新代码。

## 3. 固定产品规则

### 3.1 设置与键位

| 项目 | 固定行为 |
|---|---|
| 键盘控制默认值 | 关闭 |
| 键位默认值 | 方向键 |
| 可选方案 | 方向键或 WASD，只能二选一 |
| 方向键方案 | `↑` 跳、`↓` 挤压、`←` 左、`→` 右 |
| WASD 方案 | `W` 跳、`S` 挤压、`A` 左、`D` 右 |
| 方案外按键 | 不观察、不拦截、不记录，保持原行为 |
| 全局能力 | 不注册系统全局快捷键；不在其他应用获得焦点时控制 |

键盘控制只有在“允许与团子互动”和“键盘控制”都开启时才可能生效。切换方案、关闭任一开关、隐藏团子、宿主失效或退出时，先清空按住状态，再应用新状态。

跨宿主的批准规则保持不变：WorkerW 只在 Explorer 桌面实际获得焦点时启用；后继普通窗口只在场景窗口自身获得焦点时启用。当前增量只实施前者，不能用控制中心焦点、任意 Explorer 窗口或其他应用焦点代替。

### 3.2 WorkerW 焦点门禁

WorkerW 下必须同时满足以下条件才进入输入有效期：

1. 当前活动宿主为 WorkerW，团子可见且宿主处于 `Attached`；
2. 两个交互开关均开启；
3. Explorer 的桌面表面实际拥有前台/键盘焦点；
4. 当前事件是所选方案的四个白名单键，且没有 `Ctrl`、`Alt`、`Win` 修饰键；
5. 事件不是注入键盘事件。

“Explorer 桌面焦点”必须由已发现的 Progman、WorkerW、`SHELLDLL_DefView`、桌面 `SysListView32` 句柄及其父子关系判定。只检查 `explorer.exe` 进程不充分，因为任务栏、开始菜单和文件管理器窗口也属于 Explorer。判定不得读取标题、文本、文件或图标内容。

输入桥不是全局快捷键。焦点监测只订阅前台/焦点变化并核对句柄，不读取窗口文本；它在门禁成立时安装受限的 `WH_KEYBOARD_LL` 回调，在焦点离开、宿主隐藏/丢失或交互关闭时立即卸载。回调第一步重新核对焦点和白名单：任何不确定、非白名单、带系统修饰键或注入事件均直接调用下一个钩子。现有 Shell 生命周期轮询只承担句柄失效兜底，不能用一秒轮询代替及时的焦点离开处理。

白名单控制键生效时，按下和释放事件都被独占，避免 Explorer 同时移动桌面图标选择。自动重复的多次按下只维持一个 held 状态，不排队动作；对应释放清除 held 状态。

### 3.3 动作语义

#### 上键：单跳与长按复跳

- 新的按下沿只有在 Core 确认团子位于可起跳表面且未拖动时才能发起一次跳跃。
- 起跳后增加明确的 Core 空中阶段；上升、下降和落地结果由 Core 时间/位置规则产生，Box2D 只消费 cue。
- 空中新的按下沿和键盘自动重复均忽略，不排队，不允许二段跳。
- 持续按住时，必须先由 Core 确认落地，再从落地确认时刻等待 `450 ms`，门禁仍成立且按键仍按住才可复跳。
- 释放立即取消后续复跳资格，不截断已经发生的跳跃；再次按下按新的按下沿处理。
- 跳跃开始时取消挤压并把水平控制速度归零；空中不响应左右输入，避免引入未批准的空中控制。

第一轮内部调参以约 `48 DIPs` 的跳高和约 `640 ms` 的完整腾空时间为候选。它们不是用户设置，也不是人工验收前的最终观感常量；调整必须保留单跳、落地权威和复跳冷却测试。

#### 下键：轻微挤压

- 只在 Core 确认落地且未拖动时生效；空中按下无效。
- 按住时在约 `120 ms` 内把归一化挤压量从 0 提升到 1，对应高度最多减少约 8%、宽度最多增加约 5%。
- 释放、起跳、拖动或焦点丢失后在约 `160 ms` 内回到 0；不得附加跳跃或明显反弹。
- 挤压量作为表现 cue 进入 Physics Presentation；Core `VisualBounds`、地面接触点和权威位置保持不变。
- Box2D/fallback 都必须保持底边不动。表现异常时回退到有界手工姿势，不能用改变 Core 位置补偿。

#### 左右键：低速走动

- 地面按左/右后按固定步长逐步加速；松开后逐步减速。
- 初始最大速度为 `55 DIPs/s`，初始加速度候选 `110 DIPs/s²`。人工验收可以在不改变产品语义的前提下调参。
- 根据首次人工反馈，固定减速度替换为表面运动配置：默认地面摩擦系数 `1.0`、基础减速度 `320 DIPs/s²`，从最大速度约 `172 ms` 停稳；允许安全系数范围 `0.05–3.0`。
- Core 只接收当前可站立表面的 `surfaceId + frictionCoefficient`。未来地板、地毯或其他家具由权威接触解析选择配置，不在方向输入、WorkerW 或 Box2D adapter 中按家具类型分支。
- 加速与刹停产生 `[-1,1]` 的接地摩擦 cue：启动时上半身轻微滞后，刹停时上半身沿原运动方向轻微前倾并在约 `140 ms` 内回正；底边和权威位置不动。
- 左右同时按住时目标速度为 0，按减速规则停止，不采用“最后按下者获胜”。
- 到达 Core 工作区边界时夹紧权威位置，并清零继续朝边界的速度，不能抖动或越界。
- 有非零地面速度时使用 `Wander` 动作语义；速度衰减为 0 后回到 `Idle`。
- 单一水平方向输入更新 `FacingDirection`；停稳后保留最后面向，左右同按不改变面向。`FacingDirection` 进入原生 Box2D 步进：两只眼睛各自以独立物体和三条阻尼距离关节连接左右及顶部节点，帽子独立物体以两条带局部锚点的阻尼距离关节连接顶部相邻节点。转向由关节逐步完成，不在表现变换器中瞬移。
- 走到岛边不会自动触发接近或爬岛。`ApproachIsland`、`ClimbUp`、`OnIsland` 和 `ClimbDown` 仍保留为可用状态，但本增量不再自动串联它们。

### 3.4 优先级与清理

处理优先级为：隐藏/退出或宿主失效、拖动、跳跃、挤压、左右走动、`Idle`。

- 鼠标拖动开始时，输入适配层清空四键 held 状态，Core 清零水平速度与复跳资格；拖动结束继续使用现有 `Falling → ConfirmLanding → Landing` 权威路径。
- 点击反馈不得中断跳跃、下落或落地；稳定 `Idle` 时才进入 `Reacting`。
- 焦点丢失只停止后续输入与加速；地面水平速度按减速规则收敛，挤压按恢复时序收敛。它不能产生额外起跳。
- 隐藏、WorkerW 失效、切换宿主或退出时必须同步停止输入桥、清空输入状态、停止调度并释放表现世界。

## 4. 状态、时序与数据流

### 4.1 输入状态

Desktop 输入适配层只维护以下易失状态：

```text
InputGate = Disabled | DesktopFocused
SelectedScheme = ArrowKeys | Wasd
Held = { Left, Right, Jump, Squash }
```

它把 held 快照归一化为 `DirectionalControlIntent`，不向 Core 传原始虚拟键码。输入回调不直接移动 WPF 元素、不调用 Box2D、不发出 `landed`，也不写日志。

### 4.2 Core 控制状态

Core 在 `SingleDangoMotionController` 内持有：

```text
HorizontalVelocityDipsPerSecond
FacingDirection = Center | Left | Right
JumpHorizontalVelocityDipsPerSecond
AirPhase = Grounded | Rising | Falling | Landing
LandingConfirmedAtMilliseconds
JumpWasReleasedSinceLastTakeoff
SquashAmount (0..1, presentation cue only)
```

为避免把“正在上升”误写为 `Falling`，实施时在 `DangoActionState` 增加 `Jumping`，并补齐状态转换、clip/fallback 映射和穷举测试：`Idle/Wander/Landing → Jumping → Falling → Landing → Idle`。`Jumping` 可以先复用已批准的无脚空中手工姿势，但不能缺少明确 fallback。

每个 60 Hz tick 的顺序固定为：

1. 取得单调时钟 `now` 与有界 `dt`；
2. WorkerW 焦点门禁产生一个语义 intent 快照；
3. Core 根据 intent、当前动作和工作区推进权威状态、位置、速度与表现 cue；
4. 把 Core 快照和 cue 转成 `PhysicsPresentationInput`；
5. 表现世界步进并输出局部轮廓；
6. WPF 容器应用 Core 权威位置，视觉组件应用局部形变；
7. 任何表现错误只能 fallback，不能回写 Core。

`dt` 必须使用现有固定步长/补步上限，不按键盘事件到达频率积分。时钟由调用方注入或作为参数传入 Core，测试不得依赖真实睡眠。

### 4.3 跳跃时序

```text
Grounded + Jump down edge
  -> Jumping/Rising（记录起跳表面、起点与起跳瞬间的固定水平速度）
  -> apex
  -> Falling
  -> Core ConfirmLanding（恢复同一可起跳表面并记录落地时刻）
  -> Landing
  -> Idle
  -> 若 Jump 仍 held 且落地后已满 450 ms，则再次 Jumping
```

任何焦点、方案、宿主或交互状态变化都会清除 held，因此恢复后必须收到新的按下事件。释放后即使 `450 ms` 已满也不会复跳。

起跳瞬间只按左或右时，水平速度固定为 `-55/+55 DIPs/s`，完整 `640 ms` 跳跃约前进 `35.2 DIPs`；空中后续方向输入不改变轨迹或面向。无水平方向或左右同按时水平速度为 0。

### 4.4 原生附件连接

- 原生桥 ABI 提升到 v5，`soft_body_step` 包含有界目标面向、拖动状态与局部抓取点输入，以及左右眼位置、帽子位置/旋转输出和拖动恢复 cue；依赖清单、托管委托和固定 ABI 要求同步更新。
- 左右眼和帽子各自拥有 Box2D 动态 body 与无碰撞质量形状；每只眼睛通过三条、帽子通过两条有阻尼 distance joint 连接身体节点。
- 眼睛正面目标半间距为 `19/72` 局部单位，转向目标半间距为 `17/72`，对应当前渲染约从 `38 px` 收到 `34 px`；不缩放眼睛。
- 帽子连接采用约 `2.8 Hz / 0.55` 阻尼比，并降低 body 的线性/角阻尼、扩大转向目标位移，使左右转向约 `250–350 ms` 完成并保留轻微回摆。
- 只在目标面向改变时更新关节目标长度并唤醒附件，稳定站立时允许身体节点继续休眠。
- 鼠标拖动以点击位置创建临时 mouse joint，并在拖动期间适度降低径向弹簧刚度、向轮廓节点施加有界悬垂力；释放或取消时销毁 joint 并恢复径向弹性。
- 原生输出必须有限且处于附件安全界限内；WPF 继续做最终可视范围裁剪。释放世界必须同时释放附件和关节，既有 50 次创建/销毁零泄漏门禁继续适用。
- native 不可用、约束失稳或输出越界时继续回退手工姿势；fallback 不伪装成物理连接。

### 4.4 自动时间线移除

从 `WorkerWWindow` 删除 P2D-006 自动演示职责：

- 删除 `AdvanceAuthoredAction`、`ApplyAuthoredMotion`、`TrackActionOrigin`、`Lerp` 及只为它们存在的起点/观测字段；
- `motionTimer` 只消费方向 intent、推进 Core、应用权威位置和步进表现；
- 启动和恢复后保持 `Idle`，不在 2.5 秒后自动移动；
- 输入结束后回到 `Idle`，不补播 `ApproachIsland/ClimbUp/ClimbDown`；
- 保留点击、拖动、显式落地、隐藏、宿主恢复和退出路径，并对其与键盘状态的互斥补测试。

## 5. 模块与文件边界

下表是实施时的预期所有权。开始编码时若基线已移动，可调整文件名，但不得改变职责分层。

| 模块/文件 | 计划变更 | 禁止事项 |
|---|---|---|
| `src/Dango.Core/Animation/DangoActionState.cs` | 增加 `Jumping` 及严格转换 | 不加入 Win32、WPF 或原始键码 |
| `src/Dango.Core/Animation/DirectionalControlIntent.cs`（新） | 定义四个布尔语义、零输入与最小 `GroundSurfaceMotionProfile`，允许未来表面提供标识与有界摩擦系数 | 不保存按键历史，不建立家具系统或把表面类型写死到控制器 |
| `src/Dango.Core/Animation/SingleDangoMotionController.cs` | 增加固定步长方向推进、跳跃/落地/冷却、表面摩擦、速度、边界和表现 cue；保持 Core 权威 | 不让 Box2D 或输入回调直接改位置 |
| `tests/Dango.Core.Tests/Animation/ActionStateMachineTests.cs` | 覆盖 `Jumping` 允许/拒绝转换 | 不放宽既有非法转换 |
| `tests/Dango.Core.Tests/Animation/SingleDangoMotionControllerTests.cs` | 覆盖单跳、无二段跳、落地后 450 ms 复跳、挤压、加减速和边界 | 不用真实时间或只测最终 happy path |
| `src/Dango.Desktop/DesktopHost/WorkerW/WorkerWDesktopFocusGate.cs`（新） | 订阅前台/焦点变化，只根据已知 Shell 句柄/关系判定桌面焦点，并在 Shell tick 复核失效句柄 | 不读取标题、桌面内容或进程外文本 |
| `src/Dango.Desktop/DesktopHost/WorkerW/WorkerWKeyboardInputBridge.cs`（新） | 管理受限钩子生命周期、白名单与 held 快照 | 不做全局快捷键，不记录非白名单事件 |
| `src/Dango.Desktop/DesktopHost/Win32/NativeMethods.cs` | 仅增加焦点/句柄与有界钩子所需的最小 Win32 封装 | 不增加键盘记录、文本读取或无界等待 |
| `src/Dango.Desktop/DesktopHost/WorkerW/WorkerWWindow.xaml.cs` | 接入焦点门禁和语义 intent，移除自动时间线，统一生命周期清理 | 不在宿主复制动作规则或移动视觉冒充 Core 结果 |
| `src/Dango.Desktop/Physics/Presentation/PhysicsPresentationModels.cs` 与 adapter/fake | 消费归一化挤压/跳跃 cue，保持有界 fallback | 不产生 Core landed 或改变权威位置 |
| `src/Dango.Desktop/DesktopHost/KeyboardControlSettings.cs`（新） | 定义 `IsEnabled=false`、`ArrowKeys` 默认值、验证与未知值回退 | 不支持任意自定义键位 |
| `src/Dango.Desktop/AppShell/ControlCenter/ControlCenterWindowSettingsStore.cs` | 设置 Schema 升级与原子迁移；旧 Schema 得到安全默认 | 不把损坏值猜成开启 |
| `src/Dango.Desktop/AppShell/ApplicationCommandController.cs` | 提交设置并等待 WorkerW 确认后返回结果 | 不直接安装钩子或改 Core 状态 |
| `src/Dango.Desktop/AppShell/ControlCenter/ControlCenterWindow.xaml(.cs)` | 增加键盘开关、方向键/WASD 单选和门禁说明 | 不修改宿主选项或加入普通窗口 UI |
| `tests/Dango.Desktop.Tests/DesktopHost/WorkerW/*` | 覆盖焦点矩阵、四键白名单、独占、卸载和故障透传 | 不依赖用户真实桌面内容 |
| `tests/Dango.Desktop.Tests/AppShell/*` | 覆盖默认值、Schema 迁移、命令确认和失败回滚 | 不用 Toast 代替运行组件确认 |
| `docs/verification/phase-1-p2d-006-single-dango-motion.md` | 实施后追加真实失败、修复、自动/实时/人工证据 | 不覆盖既有崩溃与未验证事实 |

本增量不得创建普通窗口宿主文件，不修改 `DesktopHostMode` 为最终枚举，不删除 Overlay，不迁移默认宿主。窗口模式实施分支后续会复用 `DirectionalControlIntent` 与 Core 控制器，并新增自身的 WPF 预览键盘适配器；它不能反向迫使本增量提前改宿主架构。

## 6. 先失败验收

编码必须先加入以下会真实失败的测试，记录首次失败原因，再实现使其通过。

### 6.1 Core 定向测试

1. 默认零 intent 连续推进不会离开 `Idle`，证明自动时间线已不再驱动 Core。
2. 单个 Jump 按下沿只创建一次 `Jumping`；自动重复和空中再次按下不能产生第二次起跳。
3. 长按 Jump 在落地后的 `449 ms` 不能复跳，`450 ms` 且仍 held 才能复跳。
4. Jump 释放取消复跳资格；本次已经发生的上升/下降仍完整结束。
5. 左右分别按下按候选加速度接近 `±55 DIPs/s`，释放按减速度回零，没有瞬时匀速跃迁。
6. 左右同时按住目标速度为零；从已有速度按减速规则停止。
7. 默认地面从 `55 DIPs/s` 在 150 ms 时仍有少量速度、200 ms 内停稳；停止后的摩擦 cue 有界回正。
8. 相同输入下低摩擦表面的停止距离大于高摩擦表面；切换配置不改变四键语义、工作区边界或 Core 权威。
9. 左右输入在空中不改变 X；起跳时水平速度清零。
10. 达到左右工作区边界后位置被夹紧且朝外速度为零；持续按住不抖动。
11. Grounded 下压在 120 ms 内达到上限，释放在 160 ms 内回零；Core bounds 与 ground contact 全程不变。
12. 空中下压无效；跳跃、拖动、隐藏和退出会取消挤压/复跳状态。
13. 表现帧重复观测仍不能触发 `Landing`；只有 Core 明确落地路径可以确认。
14. `Jumping` 的允许与拒绝转换、clip/fallback 覆盖完整，未知状态不能静默回退。

### 6.2 Desktop/Win32 定向测试

1. fresh/旧版/损坏设置分别得到“键盘关闭 + 方向键”；未知方案不能启用控制。
2. WorkerW 未附着、隐藏、交互关闭、键盘开关关闭、其他应用焦点、任务栏、开始菜单和文件管理器焦点均不安装或立即卸载输入桥。
3. 只有已知 Explorer 桌面句柄/后代满足焦点门禁；仅进程名相同不足以通过。
4. 方向键方案只映射四个方向键，WASD 全部透传；WASD 方案反之。
5. 生效方案的 key-down/key-up 都独占；其他键、带系统修饰键和注入事件全部透传。
6. 自动重复不会产生多次 Jump edge；焦点丢失会原子清空 held，恢复焦点后旧按键不复活。
7. 钩子安装失败、句柄查询失败或回调异常时全部透传，设置显示不可用，鼠标与宿主继续运行。
8. 隐藏、WorkerW 重建、宿主切换、关闭和 Dispose 都卸载钩子、停止 tick 并释放 native 表现世界。
9. motion tick 不再调用任何自动动作推进或线性时间线方法；空闲 10 秒仍为 `Idle`。
10. 控制中心命令只有在运行组件确认后显示已生效；失败恢复先前设置。

测试 double 只使用虚构 HWND、虚构场景和单调时钟。不得采集真实按键或依赖当前桌面内容完成自动测试。

## 7. 实施顺序与停止点

### D0 — 基线冻结与失败证据

- 核对 `feat/p1-004-box2d-soft-body` 的 HEAD、未提交文件和 P2D-006 当前测试结果。
- 明确本任务文件所有权，确认没有另一执行者修改第 5 节中的相同文件。
- 先提交或以可恢复方式保存当前 P2D-005/P2D-006 基线由任务负责人决定；不得覆盖现有未提交成果。
- 加入第 6 节测试并记录确切失败输出。

**停止点：** 基线测试已失败、文件所有权重叠或 P2D-006 状态与本文不一致时停止实施并更新计划。

### D1 — Core 方向控制

- 先实现 `DirectionalControlIntent`、`Jumping` 状态与 Core 固定步长推进。
- 让单跳、450 ms 落地后冷却、挤压 cue、低速加减速和边界测试通过。
- 保持拖动、显式落地、隐藏/退出和表现隔离既有测试通过。

**停止点：** 方向控制需要 Box2D 回调才能决定落地或 Core 位置时停止；不得降低权威边界。

### D2 — 设置与受限 WorkerW 输入桥

- 升级版本化设置，确保旧设置安全迁移且默认关闭。
- 实现基于已发现桌面句柄的焦点门禁和四键白名单输入桥。
- 完成钩子安装/卸载、透传、独占、重复键与失败回退测试。

**停止点：** 若不能可靠地区分桌面与任务栏/开始菜单/文件管理器焦点，全部按键透传并停止，不得用“Explorer 进程即桌面”的宽松判定交付。

### D3 — P2D-006 WorkerW 接入

- 删除自动时间线，按第 4.2 节顺序接入 Core 和物理表现。
- 复用现有图标上下层、鼠标拖动、点击、WorkerW 恢复和资源生命周期。
- 控制中心增加方向控制设置与安全状态，但不改宿主模式区域。

**停止点：** 若键盘接入破坏桌面图标、鼠标拖动、层切换、Explorer 恢复或进程退出，停止在当前范围修复，不开始窗口模式。

### D4 — 自动门禁与发布候选

- 运行格式、Release 构建、Core/Desktop 全量测试、`win-x64` self-contained publish、smoke test、隐私负向检查和 `git diff --check`。
- 确认发布目录包含既有固定 native 依赖，退出后无 Dango 残留进程。
- 自动门禁通过只允许进入真实桌面验收，不代表观感通过。

### D5 — 真实 Windows 与人工验收

- 在真实 Explorer/WorkerW 与实际发布物上逐项执行第 8 节。
- 记录系统版本、DPI、构建/提交或工作区标识、开始/结束时间、实际结果和最小复现。
- 用户确认控制手感和桌面干扰后，才可将方向控制标记为人工通过。

**最终停止点：** 更新 P2D-006 记录并停止。普通窗口模式、提交、集成、P2D-007 和 P2D-008 均等待各自授权。

## 8. 真实 Windows 验收清单

### 8.1 设置与焦点

1. 使用干净/旧版设置启动，确认键盘控制默认关闭、默认方案为方向键，且未触发移动。
2. 开启键盘控制，分别选择方向键和 WASD，确认同一时刻只有所选方案有效。
3. 点击桌面空白处和桌面图标后分别验证控制；控制键生效时 Explorer 不同时移动图标选择。
4. 切到普通应用、文件管理器、任务栏、开始菜单和控制中心，确认立即失效，方案内外按键保持目标应用原行为。
5. 返回桌面但不重新按键，确认旧 held 不恢复；新的按下事件才开始控制。
6. 关闭“允许与团子互动”或键盘控制，确认按键全部透传，鼠标/显示状态符合对应总开关语义。

### 8.2 动作

1. 单击上键，确认只跳一次；空中重复按下和键盘自动重复不会二段跳。
2. 长按上键，确认每次必须完整落地并等待约 450 ms 才复跳；松开后不再继续。
3. 地面按住下键，确认是轻微挤压、底边不动、不穿地、不起跳；松开后平滑恢复且不明显反弹。
4. 空中按下键无效；落地后不会补播空中忽略的挤压。
5. 左右分别长按和短按，确认低速加速、松开减速；左右同时按住时平滑停下。
6. 到工作区左右边界持续按住，确认不越界、不抖动。
7. 空中按左右不改变轨迹；落地后需要新的有效输入才移动。
8. 无输入连续观察至少 30 秒，确认保持安静 `Idle`，不再自动快速平移、接近岛或爬岛。

### 8.3 WorkerW 集成与恢复

1. 团子在图标下层和上层各完成一轮方向键/WASD、单跳、挤压和拖动，桌面图标仍可正常操作。
2. 键盘移动中开始鼠标拖动，确认键盘状态被清空；释放按现有 Core 落地路径完成。
3. 点击反馈、拖动、落地和键盘动作互不产生非法状态或崩溃。
4. 隐藏/显示团子后旧按键不恢复，显示后物理世界从 Core 快照重新创建。
5. 重启 Explorer 或触发 WorkerW 恢复，确认失效期间全部透传、恢复后要求新按键、没有补算离线帧。
6. 临时切到现有 Overlay 只验证安全停止与透传；不把 Overlay 键盘控制或人工观感列为本任务通过条件。
7. 正常退出后确认输入钩子释放、Dango 无残留进程；诊断中没有最近按键、次数或原始键码。
8. 在 100% 与当前高 DPI 环境各完成焦点、边界和基本动作检查。

### 8.4 人工观感

- 左右移动看起来是安静走动，不是图形匀速平移，不干扰桌面工作。
- 单跳有重量，长按复跳有明显落地间隔，不像高频弹簧。
- 挤压轻微、可爱，不像受伤或被压扁。
- 动作期间轮廓保持无脚、无嘴、无腮红、无阴影；眼睛和帽子稳定。

## 9. 隐私与安全检查

- 代码搜索确认没有 `RegisterHotKey`、任意键记录、按键历史、输入文本、窗口标题、桌面文件或图标文本采集。
- 低级钩子回调只保留四个语义 held 位；不保留时间线，不统计次数，不导出原始键码。
- 诊断只允许显示：键盘控制开/关、方案、焦点门禁是否满足、输入桥 `Inactive/Active/Unavailable` 和受控错误码。
- 日志、设置与验证导出不得包含最近按键、原始事件、前台窗口标题或内容。
- 回调异常、焦点未知、Explorer 句柄过期、设置损坏一律 fail open：按键透传、团子不动、输入桥停止。
- 输入桥卸载与表现世界释放必须是幂等操作；重复隐藏、恢复或退出不得留下钩子和计时器。

## 10. 验证与交付边界

实施完成报告必须分层列出实际证据：

- **静态门禁**：格式、Release 构建、Core/Desktop 测试、publish、smoke test、隐私搜索、`git diff --check` 的命令和实际结果。
- **集成门禁**：Core 权威位置/落地、表现 cue、WorkerW 输入桥、设置迁移、宿主生命周期和 native 释放结果。
- **实时行为**：真实 Windows/Explorer/WorkerW、两套键位、焦点矩阵、图标上下层、Explorer 恢复、DPI 与残留进程的实际结果。
- **人工验收**：用户对速度、起停、跳跃重量、复跳间隔、挤压和桌面干扰度的明确结论。
- **交付状态**：是否提交、是否集成到阶段分支、是否推送、是否部署；这些授权彼此独立。

自动测试、fake HWND、单次 smoke test、截图或只看到团子移动都不能替代真实桌面和人工验收。方向控制通过也不代表普通窗口模式、P2D-006/B6、P1-004、P2D-007、P2D-008 或整个 Phase 1 已完成。

## 11. 后继依赖：普通窗口模式

普通窗口模式必须在本方向控制增量完成、WorkerW 人工验收且成果进入一个安全基线后顺序开始。后继实现可以复用：

- `DirectionalControlIntent` 与 Core 固定步长控制器；
- 键盘控制设置、默认关闭和方向键/WASD 方案；
- 单跳、450 ms 落地后冷却、挤压和左右加减速自动测试；
- Physics Presentation cue 与视觉组件。

后继实现必须另行定义普通窗口自身焦点适配器、窗口生命周期、窗口位置/DPI 恢复、默认 WorkerW、Overlay 删除和宿主切换验收。它不得复用 WorkerW 低级钩子；普通窗口只使用自身的 WPF 预览键盘事件。

方向控制已按 D0–D4 完成实现和自动门禁。下一步只进入 D5：使用当前 self-contained 候选执行真实 Windows / Explorer / WorkerW 与人工观感验收；验收前不开始普通窗口模式。
