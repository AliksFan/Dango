# Box2D native 依赖

## 正式桌面构建：独立 belt 模块

普通 `dotnet build src/Dango.Desktop/Dango.Desktop.csproj` 和 `dotnet publish` 自动通过
`eng/build-belt-native.ps1` 构建、检查并携带 `dango_belt_product.dll`、`dango_belt_room.dll`
及 `belt-native-manifest.json`。需要 Windows x64、CMake 和 Visual Studio C++ 工具链；脚本优先使用 PATH
中的 CMake，也可定位 Visual Studio 自带的 CMake。`DANGO_BELT_ONLY=ON` 不下载或构建 Box2D。
原生中间文件放在已忽略的 `obj/belt-native/<Configuration>`，避免较深发布目录触发 MSVC 路径长度限制。

清单记录当前原生源文件哈希、两个 DLL 的 SHA-256、ABI、模型、调参版本及能力；正式发布还重新验证实际导出与身份。
需要显式覆盖已构建测试模块时，可使用 `DangoBeltModulePath`、`DangoBeltRoomModulePath` MSBuild 属性；未覆盖的模块仍自动构建，
清单将标记 `explicit-override`。正式 `eng/publish-desktop-entry.ps1 -RunName <name>` 只接受默认源码构建产物。
发布脚本默认无需旧模块路径；其可选 `PhysicsModulePath` 参数只接受下述历史已批准模块。
新 belt-default 包合同要求两个 belt DLL 及清单，旧包合同继续要求原 `dango_physics_module.dll`，保持历史包检查和回滚兼容。

> 2026-09-17 整理：本地检查点仅收纳已验收模块和基础原生验证 target；下文联合求解、休眠/接触诊断属于保留的历史实验，相关源码和额外 CMake target 仍在原工作树未提交。不要把历史诊断命令当作干净 checkout 的必需构建步骤。正式旧 DLL 原样保存；本轮只在新目录构建模块，未运行会替换正式 DLL 的脚本。

当前统一使用 `dango_physics_module` target：正式 `dango_box2d.dll` 与模块共同编译 `dango_box2d_bridge.c` 的 self rig/约束/施力原语，模块提供容量 1/2/3（默认 3）的世界、稳定实体 ID、真实添加/移除、固定步、重置和所有权释放。单/双体与三体已经分别人工验收。独立三体预览 target 已归并，旧二进制仅留作对照。构建与 .NET API 见[可复用物理模块说明](../../../docs/phase-1-physics-module.md)，使用新的 `physics-module-unified-build` 目录。它不替换正式 DLL。

新增独立 `dango_collision_review` DLL target：两个原 self rig 共用 world，原施力/初始化/读帧函数由单体与实验适配共用，数学语句保持原样。新增跨体节点—边修正，未覆盖正式 `dango_box2d.dll`。接口、运行与已知可见接触缝隙见 [JointReview](../../tools/Dango.JointReview/README.md)。测试禁用接触导出仅供负例调用，不能用于正式宿主。

联合候选新增测试专用入口：`velocity-tests`（41 项）、`velocity-tests-broken`（预期失败）、`preview-smoke`（1/2/3 体连续检查，目前双/三体 `-205`）、`preview-host`（stdio 快照协议）。WPF 使用方法见 [JointReview](../../tools/Dango.JointReview/README.md)。这些入口不修改正式 ABI/DLL，不能替代完整阶段验收。

本目录固定 Dango P1-004 使用的官方 Box2D `v3.1.1`：

- tag：`v3.1.1`
- commit：`8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`
- 许可证：MIT，原文保存在 `LICENSE.box2d.txt`
- 目标：`win-x64`
- Dango bridge ABI：`13`
- 完整来源、归档、许可证和二进制 SHA-256：`dependency.json`

## 交付选择

仓库提交固定的 `dango_box2d.dll`、小型 C17 bridge、构建脚本、许可证和哈希清单，不提交 Box2D 的 233 个上游源文件。上述 belt 原生构建和 self-contained 运行不联网（首次 .NET 包还原可能联网）；只有显式重新构建 Box2D 依赖且本机没有缓存归档时，下述脚本才从官方 GitHub commit URL 下载源码。

旧 DLL 随仓库保存；旧统一模块诊断仍要求按模块说明另行构建或提供 `dango_physics_module.dll`，并显式传给消费项目。仓库保留从固定 commit 重建的审查路径；若后续需要修改 Box2D 本体或供应链策略，再单独评估 vendored source，不在应用启动时下载 DLL。

## 从固定来源重建

要求 Windows x64、Visual Studio 2022 C++ Build Tools 和其 CMake 组件。从仓库根运行：

```powershell
pwsh -NoProfile -File .\eng\native\box2d\build-box2d.ps1
```

脚本会：

1. 下载固定 commit 的源码归档到已忽略的 `artifacts/native-spike/`；
2. 校验归档和 MIT 许可证 SHA-256；
3. 用 C17、MSVC 静态运行库和 `/Brepro` 构建官方 Box2D 与 Dango bridge；
4. 运行 `dango_self_constraints_tests` 的几何、守恒及顶部圆润度三个档位各 36,000 帧原生运行测试，通过后才输出 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll`；
5. 更新不含时间戳的 `dependency.json` 二进制哈希。

也可传入已位于固定 commit 的官方 Git checkout：

```powershell
pwsh -NoProfile -File .\eng\native\box2d\build-box2d.ps1 `
  -SourceRoot D:\path\to\box2d-v3.1.1
```

自定义源码目录必须是 Git checkout 且 HEAD 精确匹配固定 commit。构建脚本不会安装工具、修改系统环境或在运行时联网。

## Bridge 能力

bridge 暴露版本、ABI、`create → body/joint → step → destroy` 生命周期探针，以及 P2D-004 的 opaque soft-body 会话：

- 12/16 个动态外圈节点；
- 运动学核心、相邻弹簧和径向弹簧；
- 单体路径继续接收拖动状态和局部点击坐标，以临时 mouse joint 形成局部抓取支点；
- 拖动期间施加有界悬垂力并降低径向弹簧刚度，释放时销毁支点并恢复原弹性；
- 只与外圈节点碰撞的静态接地代理；
- 2/4 个 Box2D 子步；
- 有限数、正向面积、半径、质心和速度/能量上限；
- 销毁时检查 Box2D 分配回到创建前基线。

ABI v9 保留隔离的 P2D-007 paired session，并将 P2D-007A stacked session 扩展为交互式表现物理：三个软体环处于另一个共享 Box2D world 和同一静态地面，Core 权威锚点只用于出生/重置；运行中动态表现核心与轮廓节点受重力影响，bridge 返回三份局部轮廓、相邻层真实 contact、表现核心位置与收束速度。stacked 专用的团子间平滑防穿透层和轮廓节点使用 `0.02` 低摩擦，不沿用地面摩擦下限，以允许偏心滑落和及时分离；帽子没有 native 碰撞对象。第一只团子可接收有界二维控制力；表现位移只用于渲染，不回写 Core。重置、fallback 或销毁 stacked session 会一次释放整组共享 world；paired session 的双体横向接触与释放行为不变。

.NET 不接收 Box2D ID、body、joint 或 world 类型，只持有 bridge opaque handle 并采样局部轮廓。单体、双体和三体 WPF 预览均只消费托管表现帧。

## 生命周期探针

联合求解隔离测试的 J2.5 接触管理位于 `dango_joint_contact_registry.h`，只读诊断位于 `dango_joint_contact_diagnostics.h`；均由测试源包含，不链接到正式 DLL。现有隔离构建中的 `dango_joint_contact_solver_tests.exe contact-identity` 验证规范键、去重、容量和真实地面读取，`contact-diagnostics NEW_LOG_PATH` 只读观察旧 J3，日志路径必须尚不存在。完整复跑与当前边界见[碰撞验证记录](../../../docs/verification/phase-1-p2d-007c-inter-body-collision.md)；该测试通过不代表 J3R 位置收敛通过。

J3R 隔离位置候选位于 `dango_joint_position_replay.h`，`position-contract` 运行契约正反例，`position-r NEW_OUTPUT_PREFIX` 只读加载两份原始 fixture，独占保存新日志与末态。原 `position` 保留旧 J3 行为。J3R 两组在 96 次扫描后仍未收敛，当前处于设计评审停止点，不能通过运行后续命令跳过门禁。

ABI 11 在历史单体/paired/stacked 导出之外保留独立的 self session，供当前正式单团子重设计使用。self 配置包含 `top_softness`；当前默认值为 0。self 路径在每个不超过 1/240 秒的小步后联合求解角度、长度和地面，原始及外扩轮廓同时受约束；不收敛返回 `-91`，由托管端锁存，不自动重生。完整设计与验证见[角度约束修订方案](../../../docs/phase-1-dango-angle-constraint-design.md)。本次没有修改 paired/stacked 求解器。

ABI 12 曾增加顶部圆润度，现已退役。当前 ABI 13 将 self 配置的 `top_softness` 和 `crown_roundness` 合并为 `top_inward_force`（0～1，候选默认 0.5），沿上半部径向内收，权重为向上分量的平方，核心承受反作用；恢复统一径向限制和弹性，接地水平形变仅保留下半部外扩。其余会话接口不变，见[顶部内收力试验](../../../docs/phase-1-dango-top-inward-force.md)。

self-contained 发布后可运行无窗口探针：

```powershell
.\Dango.Desktop.exe --box2d-native-probe
```

退出码 `0` 表示加载、ABI/版本与生命周期探针通过；`20` 表示受控加载失败，`21` 表示 native 生命周期探针失败。
