# 联合求解隔离预览

## 当前模块化入口

统一入口 `--physics DLL [2|3]` 由 `Dango.Physics` 可复用模块驱动，默认三体；窗口只保留输入映射、UI 和显示。旧 `--collision`/`--three-collision` 是数量快捷方式，使用同一个模块 DLL。旧 `CollisionSession` 仍供历史 DLL 的比例/支撑/负例验证使用。

```powershell
dotnet build eng/tools/Dango.JointReview/Dango.JointReview.csproj -c Release -o artifacts/p2d-physics-module/unified-review-bin
$moduleDll = (Resolve-Path artifacts/native-spike/physics-module-unified-build/Release/dango_physics_module.dll).Path
& ./artifacts/p2d-physics-module/unified-review-bin/Dango.JointReview.exe --physics $moduleDll 3
```

native 只构建新隔离目录的 `dango_physics_module` target；不运行会复制正式 DLL 的脚本。模块 API、最小示例和当前限制见[物理模块说明](../../../docs/phase-1-physics-module.md)。

以下旧候选章节保留历史命令与测量，不是当前构建指引；不要用当前源码覆盖历史验收 DLL 或托管对照目录。当前入口以上文为准。

## 历史候选：全方向3%相对重叠

重叠上限=两只当前样条沿接触法线投影全宽的较小值×3%，在物理坐标计算，无像素、DPI或显示器分辨率输入。顶部、侧面和斜向统一；不是面积比例。保留头顶展开、竖直起跳与静止抗推，不吸附。正式DLL不替换。

```powershell
dotnet build eng/tools/Dango.JointReview/Dango.JointReview.csproj -c Release -o artifacts/collision-ratio-bin
$ratioDll = (Resolve-Path artifacts/native-spike/collision-ratio-build/Release/dango_collision_review.dll).Path
& ./artifacts/collision-ratio-bin/Dango.JointReview.exe --collision $ratioDll
& ./artifacts/collision-ratio-bin/Dango.JointReview.exe --verify-cushion $ratioDll artifacts/ratio-new | Out-String
& ./artifacts/collision-ratio-bin/Dango.JointReview.exe --verify-cushion-broken $ratioDll artifacts/ratio-negative-new | Out-String
& ./artifacts/collision-ratio-bin/Dango.JointReview.exe --verify-support $ratioDll artifacts/ratio-support-new | Out-String
& ./artifacts/collision-ratio-bin/Dango.JointReview.exe --verify-collision $ratioDll artifacts/ratio-regression-new | Out-String
```

native只构建隔离目录的 `dango_collision_review` target，不运行复制正式DLL的脚本。验收目录须为新目录；标志64禁用全方向相对重叠，128禁用头顶展开。显示接触采样提高至128点，与验证轮廓一致。当前按无量纲比例验收，以下旧像素/顶部零重叠门禁不再适用；测试中的像素数仅为位移、形变等诊断值。

## 当前候选：有限侧重叠与头顶支撑展开

本节覆盖下方历史入口。侧向接触法线平滑启用约3px穿入容差，竖向承重保持原限制；没有吸引/锁合。真实头顶支撑时复用原地面下半身展开力，不改变真实grounded、空气操控、摩擦或跳跃参数。失去支撑、跳跃、拖动时不施加该新增力，原弹簧恢复。正式DLL不替换。

```powershell
dotnet build eng/tools/Dango.JointReview/Dango.JointReview.csproj -c Release -o artifacts/collision-cushion-bin
$cushionDll = (Resolve-Path artifacts/native-spike/collision-cushion-build/Release/dango_collision_review.dll).Path
& ./artifacts/collision-cushion-bin/Dango.JointReview.exe --collision $cushionDll
& ./artifacts/collision-cushion-bin/Dango.JointReview.exe --verify-cushion $cushionDll artifacts/cushion-new | Out-String
& ./artifacts/collision-cushion-bin/Dango.JointReview.exe --verify-cushion-broken $cushionDll artifacts/cushion-negative-new | Out-String
& ./artifacts/collision-cushion-bin/Dango.JointReview.exe --verify-support $cushionDll artifacts/cushion-support-new | Out-String
& ./artifacts/collision-cushion-bin/Dango.JointReview.exe --verify-collision $cushionDll artifacts/cushion-regression-new | Out-String
```

native仅构建隔离目录的 `dango_collision_review` target，不运行复制正式DLL的脚本。输出目录须为新目录。测试标志64禁用侧重叠、128禁用头顶展开；负例应前三项失败，分离恢复仍应通过。当前碰撞回归改测方向相关深度，并保留竖向面积门禁；旧 `--verify-natural` / `--verify-patch` 的无重叠贴合门禁属于历史，不适用于已批准的新侧容差。操作A/D移动、W竖直跳跃，点击或下拉选中另一只；“上下落体”便于观察承重压扁。

## 当前候选：竖直起跳与自然受压

本节优先于以下历史入口；短平面候选已被用户否决。删除强制平面整形，保留原单向碰撞、原弹簧/面积力。起跳始终向上，支撑体在接触附近承受向下反作用。保留低跳、静止抗推、颜色、帽子和骨架。测试标志16已不再改变最终求解，32仍用于关闭局部支撑反作用。

```powershell
dotnet build eng/tools/Dango.JointReview/Dango.JointReview.csproj -c Release -o artifacts/collision-natural-bin
$naturalDll = (Resolve-Path artifacts/native-spike/collision-natural-build/Release/dango_collision_review.dll).Path
$rejectedDll = (Resolve-Path artifacts/native-spike/collision-patch-build/Release/dango_collision_review.dll).Path
& ./artifacts/collision-natural-bin/Dango.JointReview.exe --collision $naturalDll
& ./artifacts/collision-natural-bin/Dango.JointReview.exe --verify-natural $naturalDll artifacts/natural-new $rejectedDll | Out-String
& ./artifacts/collision-natural-bin/Dango.JointReview.exe --verify-natural-broken $naturalDll artifacts/natural-negative-new $rejectedDll | Out-String
& ./artifacts/collision-natural-bin/Dango.JointReview.exe --verify-support $naturalDll artifacts/natural-support-new | Out-String
& ./artifacts/collision-natural-bin/Dango.JointReview.exe --verify-collision $naturalDll artifacts/natural-regression-new | Out-String
```

native只构建隔离的 `dango_collision_review` target；不可覆盖正式DLL。自然验收额外传入已保留的被否决DLL作对照，输出目录须新建。旧 `--verify-patch` 及20px平面门禁仅保留历史，不作为当前验收。

## 当前候选：局部受压与短段贴合

本节优先于以下历史入口。复用原视觉与物理骨架，只增加真实节点的接触平面约束和接触位置相关反作用，不做波浪凹陷、不吸附、不降低原弹簧/面积参数。

```powershell
dotnet build eng/tools/Dango.JointReview/Dango.JointReview.csproj -c Release -o artifacts/collision-patch-bin
$patchDll = (Resolve-Path artifacts/native-spike/collision-patch-build/Release/dango_collision_review.dll).Path
& ./artifacts/collision-patch-bin/Dango.JointReview.exe --collision $patchDll
& ./artifacts/collision-patch-bin/Dango.JointReview.exe --verify-patch $patchDll artifacts/patch-new
& ./artifacts/collision-patch-bin/Dango.JointReview.exe --verify-patch-broken $patchDll artifacts/patch-negative-new
& ./artifacts/collision-patch-bin/Dango.JointReview.exe --verify-support $patchDll artifacts/patch-support-new
& ./artifacts/collision-patch-bin/Dango.JointReview.exe --verify-collision $patchDll artifacts/patch-collision-new
```

native仍只构建独立 `dango_collision_review` target，勿运行复制正式DLL的脚本。输出证据目录必须为新目录。`--verify-patch` 检查20px以上共同短平面、分离恢复及左右局部压缩；负例关闭平面与局部分布，应四项失败。原低跳/抗推验收仍由 `--verify-support` 检查，碰撞和原DLL关闭增量对照由 `--verify-collision` 检查。布局3/4仅为左右偏置支撑测试夹具，正常窗口仍只有并排和上下落体选项。

## 当前候选：低跳、静止抗推、借力起跳

本节覆盖下文旧软化版本。构建托管输出 `artifacts/collision-support-bin`；独立native构建目录 `artifacts/native-spike/collision-support-build`，只构建 `dango_collision_review`，不覆盖正式DLL。

```powershell
dotnet build eng/tools/Dango.JointReview/Dango.JointReview.csproj -c Release -o artifacts/collision-support-bin
$supportDll = (Resolve-Path artifacts/native-spike/collision-support-build/Release/dango_collision_review.dll).Path
& ./artifacts/collision-support-bin/Dango.JointReview.exe --collision $supportDll
& ./artifacts/collision-support-bin/Dango.JointReview.exe --verify-support $supportDll artifacts/support-new
& ./artifacts/collision-support-bin/Dango.JointReview.exe --verify-support-broken $supportDll artifacts/support-negative-new
```

当前 `--verify-interaction` / `--verify-interaction-broken` 是上述新验收的别名；旧软化验证源码保留历史，不作为当前门禁。输出目录必须为新目录。

W地面跳跃降低；踩住另一只时按W可较低起跳，并施加向下反作用。A/D靠近时起跳可落到对方头顶；先顶住再起跳会失去助跑速度，不承诺自动落到头顶。侧面接触/腾空不能借力，仍有冷却。静止抗推使用有限地面摩擦、整体旋转/水平耗散及接触位置修正的静摩擦投影，主动移动/拖动时解除，不固定坐标。候选小步960Hz，关闭增量作原DLL对照时240Hz。新增局部软化已撤回，恢复原径向/周向频率和面积力；杏色/浅粉、帽子全局置顶、骨架保留。需要人工确认手感，非稳定堆叠或正式宿主交付。

## 最新交互候选：柔软挤压、自然分离

本节覆盖下文旧参数。构建目录改用 `artifacts/collision-soft-bin`，仍加载独立 `dango_collision_review.dll`。新增整团起跳9单位速度（实测越过并落到另一只头顶）、杏色/浅粉、独立帽子高于所有身体；停手旋转衰减由6提高至12/秒。接触附近降低朝向对方的弹簧频率和面积恢复力，分开后平滑恢复；无吸引力或黏附，不保证稳定站立。原形状、骨架、原版单体入口和正式DLL保持。

```powershell
dotnet build eng/tools/Dango.JointReview/Dango.JointReview.csproj -c Release -o artifacts/collision-soft-bin
$collisionDll = (Resolve-Path artifacts/native-spike/collision-aligned-build/Release/dango_collision_review.dll).Path
& ./artifacts/collision-soft-bin/Dango.JointReview.exe --collision $collisionDll
# 验证时使用新的输出目录
& ./artifacts/collision-soft-bin/Dango.JointReview.exe --verify-interaction $collisionDll artifacts/interaction-new
& ./artifacts/collision-soft-bin/Dango.JointReview.exe --verify-interaction-broken $collisionDll artifacts/interaction-negative-new
```

正例检查样条高度、真实方向输入跳落、空中连跳拒绝、配色/Z序、双向受压核心接近和分离恢复；负例禁用软化应退出1。水平包围宽度受倾斜影响，不作为软化判据，核心接近只是贴合代理指标，最终手感由人工验收。原版无接触等价测试显式关闭新跳跃、软化、停手阻尼。

## 原版双体碰撞实验（用户已授权尝试）

先构建独立 `dango_collision_review` target（不运行复制正式 DLL 的脚本），再构建评审工具：

```powershell
cmake --build artifacts/native-spike/collision-aligned-build --config Release --target dango_collision_review
dotnet build eng/tools/Dango.JointReview/Dango.JointReview.csproj -c Release -o artifacts/collision-aligned-bin
$collisionDll = (Resolve-Path artifacts/native-spike/collision-aligned-build/Release/dango_collision_review.dll).Path
& ./artifacts/collision-aligned-bin/Dango.JointReview.exe --collision $collisionDll
```

点击团子或下拉框选中，然后拖动或用 A/D 移动、W 跳跃、S 下压。选择“并排碰撞”或“上下落体（探索）”后按场景重置。使用原版 160×120 组件、颜色、帽子及帧转换，不改变单体参数。原地面摩擦保留，跨体摩擦暂为 0；不保证稳定堆叠。关闭窗口释放自己的共享 world 和独立 DLL，无端口、子进程或持久化状态。

两个 rig 共用一次 world 小步，沿用原内部力与单体约束。接触边界使用原显示映射和二次 B 样条的 64 点凸多边形采样，以显示控制点梯度分配位置/速度修正，不拉伸身体。有限值、凸性、接触分离与能量/动量检查失败或96轮后不收敛，均以 `-402` 锁存并保留最后安全帧；不启用旧联合速度求解器。

新增用户批准的停手旋转阻尼：只有接地且无主动输入时，以6/秒的指数衰减消除整体旋转分量，保留质心平移与形变分量；操作和腾空时不介入。骨架开关、身体和附件不改。关闭该阻尼的验证入口才要求全时段原 DLL 无接触等价；启用后的停手轨迹属于有意变化。

`--verify-collision DLL NEW_OUTPUT_DIRECTORY` 检查原版无接触对照、真实推挤/释放、上下落体探索并输出 JSON/PNG；`--verify-collision-broken` 显式关闭接触，应产生两个失败。负例只用于验证器，正常窗口不会调用。原 DLL 的 SHA-256 应保持基线值。

侧向首次接触间距已从9.74px降至约0.005px，上下约0.055px；两组1800帧每10帧采样的样条交叠面积为0。分段曲线仍是近似，不代表任意姿态精确接触已通过；阻尼手感与长期堆叠待人工验收。三体、持续承重、多人同步和正式宿主接入不在本轮交付内。

## 当前默认：原版单体基线

用户确认外观与单体物理必须保持。默认入口现直接复用 `StackedDangoSceneRuntime`、正式 DLL、160×120 的 `DeformableDangoVisual` 与独立帽子组件；不使用联合求解候选。支持鼠标拖动、A/D 移动、W 跳跃、S 下压、暂停、重置及原版骨架开关。此窗口用于恢复基线，不声称已接入碰撞，也不替代正式宿主及全部调参面板。

```powershell
dotnet build eng/tools/Dango.JointReview/Dango.JointReview.csproj -c Release -o artifacts/joint-baseline-bin
& ./artifacts/joint-baseline-bin/Dango.JointReview.exe --baseline
# 输出目录必须为新目录
& ./artifacts/joint-baseline-bin/Dango.JointReview.exe --verify-baseline artifacts/baseline-new-evidence
```

验证器顺序运行原 world 与真实 runtime，以相同动作比较 2400 帧完整轮廓、附件、位置、速度及接触数；要求无 fallback，并拒绝被扰动的轮廓。顺序执行避免原 native 进程级内存计数在双 world 并存销毁时误报。最后输出真实稳定帧 PNG、JSON 和加载 DLL 的 SHA-256。人工手感仍需确认。

## 历史联合候选：只用于显式诊断

按[已批准计划](../../../docs/phase-1-dango-joint-preview-plan.md)展示 native 测试进程的真实快照；不替换正式 DLL 或桌面宿主。

从仓库根目录运行（先单独构建 native 的 `dango_joint_contact_solver_tests` target，勿使用复制正式 DLL 的构建脚本）：

```powershell
dotnet build eng/tools/Dango.JointReview/Dango.JointReview.csproj -c Release
$nativeExe = (Resolve-Path artifacts/native-spike/box2d-pair-ground-build/Release/dango_joint_contact_solver_tests.exe).Path
& ./eng/tools/Dango.JointReview/bin/Release/net10.0-windows/Dango.JointReview.exe --candidate $nativeExe
```

选择数量、厚度和摆放后点击“按设置重置”。支持暂停、骨架和移开底层支撑；关闭窗口会退出自己的 native 子进程。默认单体。偏心取物理直径的 5%，不是正式渲染轮廓宽度 W 的验收场景。

双/三体目前约 frame 29 以 `-205` 停止，表示速度 96 次扫描未收敛。显示最后安全姿态，残差来自失败小步；冻结不能解读为稳定堆叠。位置预览警告限额为 `1e-3`，正式 `1e-5` 不变；拓扑、容量、非有限值、速度、能量和动量门禁不放宽。

`--verify NATIVE_EXE NEW_OUTPUT_DIRECTORY` 执行真实 1/2/3 体推进和 WPF 渲染，输出 JSON 与 PNG；退出 0 仅表示该工具链运行完成，必须检查 JSON 的物理错误字段。证据输出目录必须为新目录，避免覆盖 PNG。实时观感与支撑释放仍由后续验证确认。
# 三体人工测试入口（2026-09-12 增量，已统一）

当前先按[模块说明](../../../docs/phase-1-physics-module.md)构建统一 `dango_physics_module` target，然后运行（数量可选 2/3，默认 3）：

```powershell
dotnet build eng/tools/Dango.JointReview/Dango.JointReview.csproj -c Release -o artifacts/p2d-physics-module/unified-review-bin
& artifacts/p2d-physics-module/unified-review-bin/Dango.JointReview.exe --physics artifacts/native-spike/physics-module-unified-build/Release/dango_physics_module.dll 3
```

点击/拖动选中任意团子，A/D 移动，W 跳跃，S 下压。场景包含三体并排、三层落下、两下托一上，选择后按“按场景重置”。“移除选中团子”可测试支撑消失；重置恢复三体。异常会停止并显示最后安全帧。自然滑落是用户已接受的表现；2026-09-12 用户已独立确认“三体通过”，详见三体预览说明中的版本与验收记录。
