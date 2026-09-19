# Phase 1 可复用团子物理模块说明

## 1. 目的与当前边界

已验收三体能力现在统一到 `dango_physics_module.dll`：默认容量为 3，可显式配置 1、2 或 3。原三体人工确认见[预览验收记录](phase-1-physics-three-preview.md)，统一前后轨迹验证见[统一记录](phase-1-physics-module-unification.md)。原三体预览 DLL 仅保留为历史对照，不再作为新构建入口。

本模块把已通过人工验收的单体、双体与三体物理封装成项目内可复用组件。算法仍在 native 层唯一维护；场景只提交动作意图并读取只读状态，不能改节点坐标、绕过跨体接触或为每只团子各自推进一个共享世界。

当前支持 Windows x64、Box2D 3.1.1、0～3 只存活团子（创建容量 1～3）和默认物理参数版本 1。第四只明确拒绝。自然滑落表现已获接受，不保证长期不滑落；所有 DPI/硬件组合、正式桌面宿主接入、四体和多人同步未在本轮验证。

正式旧 `dango_box2d.dll` 保留不变。后续桌面接入候选已消费本模块，仅将 `dango_physics_module.dll` 复制到隔离应用输出，不覆盖正式旧 runtime；本轮宿主接入已获用户独立人工认可，验收范围与剩余门禁见[桌面接入记录](verification/phase-1-physics-desktop-integration.md)。

## 2. 分层与文件职责

| 层 | 主要文件 | 职责 |
|---|---|---|
| native 公共原语 | `eng/native/box2d/dango_box2d_bridge.c`、`dango_self_physics_internal.h` | 16 节点 rig、单体施力、约束、帧读取和真实 rig 销毁；正式 DLL 与模块共用 |
| native 模块 ABI | `dango_physics_module.h`、`dango_collision_review.c` | 世界、稳定 ID、添加/移除、固定步、重置、错误锁存、最后安全帧和释放 |
| native 构建 | `eng/native/box2d/CMakeLists.txt` | 独立 `dango_physics_module` target；静态链接固定 Box2D，MSVC `/W4 /WX` 和 `/Brepro` |
| .NET 核心 | `src/Dango.Physics/` | 版本化默认参数、纯数据 API、native ABI 校验、线程和所有权约定 |
| WPF 显示适配 | `DangoPhysicsPresentationAdapter` | 把 `DangoBodyState` 转为原 `PhysicsPresentationFrame`，不求解物理 |
| 第一消费者 | `eng/tools/Dango.JointReview` | UI、键鼠意图与既有视觉组件；`--physics DLL [2\|3]` 加载统一模块 |

依赖方向为 `Dango.Physics → .NET BCL + native DLL`，`Dango.Desktop → Dango.Physics`，评审工具再引用二者。物理程序集不引用 WPF。

## 3. 构建与运行

从仓库根使用 Visual Studio 2022 C++ Build Tools 自带 CMake，把固定 Box2D 源码目录传给新隔离构建目录：

```powershell
$cmake = '<Visual Studio CMake executable>'
$box2d = '<verified Box2D 3.1.1 source directory>'
& $cmake -S .\eng\native\box2d -B .\artifacts\native-spike\physics-module-unified-build -A x64 "-DBOX2D_SOURCE_DIR=$box2d"
& $cmake --build .\artifacts\native-spike\physics-module-unified-build --config Release --target dango_physics_module
dotnet build .\src\Dango.Physics\Dango.Physics.csproj -c Release
```

这条路径不运行 `build-box2d.ps1`，因此不会复制或替换正式 DLL。Box2D 来源、许可证与固定 commit 见 [native 依赖说明](../eng/native/box2d/README.md)。

已有 `physics-module-build` 和 `physics-three-preview-build` 是验收对照目录，不在其中重建。CMake 当前只有一个可复用模块 target，`dango_physics_module`。消费项目用 `ProjectReference` 引用 `src/Dango.Physics/Dango.Physics.csproj`，将统一 native DLL 随应用放在独立目录，再将其解析后的路径传给 `Create`；不会自动搜索或替换正式桌面 runtime。该项目不发布 NuGet，也不自动下载 native 依赖。

运行可编译的无 WPF 最小示例：

```powershell
dotnet run --project .\eng\tools\Dango.Physics.Sample\Dango.Physics.Sample.csproj -c Release -- .\artifacts\native-spike\physics-module-unified-build\Release\dango_physics_module.dll 3
```

运行第一消费者：

```powershell
dotnet build .\eng\tools\Dango.JointReview\Dango.JointReview.csproj -c Release -o .\artifacts\p2d-physics-module\unified-review-bin
& .\artifacts\p2d-physics-module\unified-review-bin\Dango.JointReview.exe --physics .\artifacts\native-spike\physics-module-unified-build\Release\dango_physics_module.dll 3
```

## 4. 最小 API 示例

示例源码位于 `eng/tools/Dango.Physics.Sample/Program.cs`，核心调用如下：

```csharp
using var world = DangoPhysicsWorld.Create(nativePath);
var first = world.Add(0, 0);
var second = world.Add(2.8, 0);
var third = world.Add(0, 1.65);
var frame = world.Step(TimeSpan.FromSeconds(1d / 60),
[
    new(first, Horizontal: 1),
    new(second),
    new(third),
]);
if (!frame.Succeeded)
{
    // Keep the returned last safe frame, then explicitly reset or exit.
    world.Reset();
}
```

`DangoPhysicsWorld` 拥有一个 native world；`Dispose` 释放该 world 与本实例加载的 DLL，重复 `Dispose` 无操作。操作与首次释放只能由创建线程调用，跨线程调用明确失败。释放后调用 Add/Remove/Step/Reset 抛出 `ObjectDisposedException`；容量等元数据不代表 world 仍存活。必须用 `using` 或 `finally` 显式释放，无终结器兜底。

`Add(x,y)` 在同一 world 中真实创建一个 rig 并返回单调递增的稳定 `DangoEntityId`。ID 只在所属 world 中有意义，不是全局标识；不要跨 world 混用。`Remove(id)` 真实销毁指定 rig，不重建其他实体；旧 ID 随即过期，再添加得到新 ID。输入列表必须恰好覆盖当前实体且不能重复。容量溢出为 `CapacityExceeded`，无效/过期 ID 为 `InvalidEntity`。

`Reset()` 保留当前实体 ID 和初始锚点，重建这些实体的物理状态并清除故障锁存。`DangoPhysicsFrame` 在 native 故障时返回最后安全帧和非零 `ErrorCode`；调用方应停止提交普通输入，只允许显式重置或退出。

| API | 契约 |
|---|---|
| `Create(path, capacity = 3, configuration = null)` | 容量 1/2/3；初始为空，不自动添加团子；null 使用参数版本 1 |
| `Capacity / Count` | 最大数量 / 当前存活数量，移除不降低容量 |
| `Add(x,y)` | 锚点 X/Y，初始核心 Y 还包含默认 DropHeight；不直接设置稳定姿态 |
| `Remove(id)` | 可移除任意一只，包括底部支撑；不重置幸存者 |
| `Step(dt, inputs)` | 世界每次推进一次；输入次序可变，但每个有效 ID 恰好一次；空世界传空列表 |
| `DangoPhysicsInput` | ID、Horizontal、Downward、JumpPressed、DragActive、DragTargetX/Y |
| `DangoPhysicsFrame` | Bodies、累计接触小步数、锁存错误码；用实体 ID 映射状态，不依赖数组位置 |
| `DangoBodyState` | 核心位置、局部轮廓、质心、有向面积、最大节点/核心速度、接地信息、拖动节点；不包含每个节点的完整速度向量 |
| `Reset()` | 保留存活实体 ID 和出生锚点；不恢复已移除实体；接触计数清零 |
| `Dispose()` | 托管重复释放幂等；原生 handle 销毁后不得再次使用或再次 destroy |

无 WPF 样例接受数量 1/2/3，实际演示移除最后一只、再添加得到新 ID、重置和逐帧错误检查。评审窗口接受 2/3，默认 3；移除按钮直接调用模块 Remove，而“按场景重置”重新创建场景，因此会恢复场景中的全部团子。旧 `--collision`/`--three-collision` 命令只保留数量快捷方式，不再需要不同 DLL。

## 5. 状态、坐标与固定步

- 世界坐标：X 向右、Y 向上；长度是无 DPI 的物理单位。
- 每只团子：动态核心加 16 个按物理坐标逆时针排列的轮廓节点。状态包含 32 个核心局部坐标、核心世界坐标、质心、有向面积、最大速度、接地/接触和拖动节点。
- 输入：水平 `[-1,1]`、下压 `[0,1]`、单帧起跳、拖动开关和世界坐标拖动目标。
- `Step` 接受 `(0,100ms]`，当前实验窗口固定约 1/60 秒；native 在候选正常模式按 960 Hz 划分小步，不能由场景重复推进共享 world。
- 物理到显示：现有 160×120 组件使用核心局部轮廓；最大绝对坐标超过 0.98 时统一缩放，横向 72/80、纵向 58/60，之后走原二次 B 样条。
- DPI 只影响 WPF 逻辑像素到设备像素的最终显示，不参与物理、接触或 3% 计算。

跨体容差取两只当前 128 点显示样条沿接触法线投影全宽的较小值，再乘 3%。每轮重新测量；它不是面积、半径或固定像素比例。没有吸引、黏附或强制贴平，超限阻挡而分离自由。

三体共用同一个 Box2D world：每小步只积分一次，再按稳定 slot 顺序处理三对接触并复核所有最终间隙；最多 8 次全对扫描，每对保留 96 次局部上限和原 `1e-5` 误差界。只有两只存活时仍调用原双体求解一次。支撑从满足原条件的实体中按 slot 选择首个；跳跃反作用仅分配给这一个支撑体，方向和接触位置规则沿用已验收版本。地面起跳整团速度增量 7.7，软体支撑相对向上目标 6.0；多支撑不重复施加反作用。

## 6. 版本 1 默认参数与安全边界

默认来源唯一为 `DangoPhysicsDefaults.Current` / `DangoSelfPhysicsParameters.CreateDefault()`。关键冻结值：16 节点、4 Box2D 子步、径向长度 0.68、`TopInwardForce=0.5`、节点半径 0.045、重力 9.8、地面高度 -0.72、最大速度 12、起跳冷却 450ms。完整参数及验证范围由 `DangoSelfPhysicsParameters.Validate()` 定义。

参数分为：结构/周长和面积恢复、角度/排斥、地面形变、质量/重力/碰撞材料、拖动、水平/下压/跳跃控制以及速度/面积安全限值。修改参数后至少重跑：

1. `Dango.Physics.Tests` 的逐帧等价与生命周期；
2. 原 native 单体约束长测；
3. 比例、支撑、抗推、轮廓及故意破坏负例；
4. `Dango.Desktop.Tests` 的显示适配和原单体/runtime 回归；
5. 新实验窗口真实运行及用户手感验收。

修改冻结默认值属于行为变化，不能仅因构建通过就接受。

## 7. 故障与退出

native ABI 仍为 1（函数签名和 42 槽位状态布局未变），物理参数版本仍为 1；默认容量变化不是调参。请重新编译托管调用方，因为 C# 可选参数默认值会编译进调用程序集。旧双体 DLL 只适合作为显式 capacity=1/2 的历史对照；对它请求默认容量 3 会明确创建失败，不会静默降级到两体。

参数验证异常为 `ArgumentException` 系列；native 创建配置错误 `-410` 映射为 `InvalidConfiguration`，添加超容量 `-411` 为 `CapacityExceeded`，无效 ID `-412` 为 `InvalidEntity`。运行错误（如 `-402` 接触求解失败、`-91` 单体约束失败、`-87` 速度异常）通过 Frame.ErrorCode/LastErrorCode 锁存，保留最后安全帧，不能忽略错误码继续当作成功。直接使用 C ABI 时，调用方负责有效 handle、足够输出容量、所属线程和 exactly-once 销毁；托管重复释放保护不适用于裸指针。

缺失 DLL、无效 PE、缺少导出和 ABI 不匹配在创建世界时分别映射为可识别 `DangoPhysicsFailureKind`。native 参数/容量/ID/运行错误保留错误码；不吞错或生成成功帧。窗口退出必须释放自己的世界，不启动服务、不打开端口、不持久化桌面或输入内容。

旧 native 使用进程级 Box2D 字节计数，多个同时存活世界会产生误报。新模块按 world 所有权销毁，不以进程总字节数判断单个 world；自动测试覆盖两个同时存活的世界。

## 8. 已验收与未验证

2026-09-12 用户分别确认模块单/双体和三体人工验收通过，并接受自然滑落。新增长时间稳定性历史结果见[稳定性补充记录](verification/phase-1-physics-module-stability.md)；原“不滑落”失败没有改写为通过。统一产物的自动逐帧对照单列于[统一记录](phase-1-physics-module-unification.md)，不把自动验证写成新的人工操作。

封装继承的已验收基线是全方向 3% 相对重叠、承重压扁、竖直跳跃、静止抗推、16 节点外形、配色/骨架/帽子显示。模块化本身需分别记录自动逐帧等价、真实窗口运行和用户人工验收；自动通过不能替代后两者。

原实验映射：`CollisionSession` 保留为旧 DLL 验证器；窗口运行改用 `PhysicsModuleSession → DangoPhysicsWorld → DangoPhysicsPresentationAdapter → DeformableDangoVisual/DangoHatVisual`。后续正式宿主源码通过 `StackedDangoSceneRuntime → DesktopPhysicsScene` 接入同一模块，旧 `Box2DSelfPhysicsPresentationWorld` 留作历史路径/回归；旧 DLL 文件不变。用户于 2026-09-12 确认本轮宿主接入人工验收通过；旧 DLL 导出门禁失败仍保留，尚未发布，详见[接入验证记录](verification/phase-1-physics-desktop-integration.md)。
