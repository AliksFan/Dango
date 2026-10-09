# 官方静态平台：独立单人小样验证

日期：2026-10-09。分支：`codex/static-platform-prototype`，实施基线：`a34b097`。用户明确授权按 [F0–F4 实施计划](../planning/2026-10-09-static-platform-implementation-plan.md)编码、隔离构建／测试及打开验收窗口。当前状态：编码、自动门禁、真实窗口输入与运行观察通过，用户人工验收待完成。

## 交付范围

一只团子、一件固定实心轴对齐平台、独立 Windows 验收窗口。平台外观由内置官方木纹 `DrawingImage` 提供，碰撞矩形独立定义。窗口提供无平台对照、顶部落地、左右接近、底面顶撞、重置、暂停、移除平台、隐藏恢复和碰撞轮廓显示。

- native 按既有 128 点显示 B 样条测量矩形接触，以轮廓梯度分配位置与速度修正；没有更大的节点圆平台代理、额外物理世界或呈现层位移补偿。
- ABI 1 保持原签名，增加可选平台设置／清除及只读接触诊断。旧模块可继续运行无平台场景，缺能力时明确报错。
- 顶部法线、间隙和核心竖直速度共同判定支撑；速度 `.499999 / .5 / .500001` 的阈值反例通过。原地面接触计数保持原义，静态平台使用原静态地面起跳分支。
- 启用平台的世界最多一个平台、一只团子；拒绝不同定义、已有身体重叠、第二只团子及平台场景的原点平移。无平台仍支持原有 1／2／3 团子路径。失败参数不改有效状态；重置保留定义及身份。
- 故障返回完整最后安全状态，停止普通动作；显式重置或退出恢复。测试专用真实几何破坏 fixture 只编入 native 合同测试可执行文件。

不提供玩家 PNG 家具导入、家具编辑器、任意参数面板、玩家摆放或布局保存；未接入普通房／桌宠房、账号、服务端和多人。圆球、蹦床、窗帘仍需后续批准。

主要文件：`eng/native/box2d/dango_static_platform.h`、`dango_collision_review.c`、`dango_physics_module.h`、`dango_static_platform_tests.c`；`src/Dango.Physics/DangoStaticPlatformDefinition.cs`、`DangoPhysicsWorld.cs`；`eng/tools/Dango.JointReview/StaticPlatformSession.cs`、`StaticPlatformWindow.cs`；`eng/run-static-platform-review.ps1`；`tests/Dango.Physics.Tests/StaticPlatformTests.cs`、`StaticPlatformGeometry.cs`、`PhysicsTestLibraries.cs`。

## 二进制与来源

所有证据均为本机隔离输出，保留于 `artifacts/furniture/`，不进入 Git。目录名不代替版本判断。

| 对象 | SHA-256 |
|---|---|
| 最终正常 r3 模块 | `872B585F1048557766B9F4C4528FE71C0824DAC5C9FEA2AFE047E87F3CE86D36` |
| 只读 main 对照 B772 | `B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E` |
| 受保护 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll` | `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A` |
| 最终关闭平台碰撞变体 | `3E610000755DA7CDFBFAD43D2270671F3589B736E9CF4927457824A08141C631` |
| 最终侧碰误授支撑变体 | `FF25EF96228A3A45D1083F2CC8868D3C970C8A6171A63D0EDB51FDE393B99B3B` |
| 只读历史三体对照 | `B2C4D4AD7DC983B9C2C3B0179344ABCF4226EB4A0A28F0E5F6DF53BA16A91094` |
| 只读 pre-bounds 旧模块 | `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C` |

main 对照来自 `f10121b` 的已验收模块，来源见[主线整合记录](2026-10-09-accessory-v1-main-integration.md)；独立复制到 `platform-r1/baseline/`，`baseline-input.json` 记录原提交和哈希。候选没有覆盖对照目录或正式 DLL。

固定 Box2D 3.1.1 来源提交为 `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`；源码与已保存官方归档的 233 个文件一致。归档 SHA-256 为 `FF94DDF7D75FFA6171502FDC7A7F336594560278978305CFC56C72268BC66721`，许可证 SHA-256 为 `68A3E676D7E94093B102D5CBA0D4E04AF812040D6F230C3DB67A6664574E43D2`。沿用现有 C17／.NET 10／WPF，无新增依赖。

## 静态与集成门禁

最终 native 正常构建和合同测试构建成功；UI 隔离 Release 构建 0 警告、0 错误。C# 受影响文件的空白格式只读检查通过，启动脚本 PowerShell 解析 0 错误。7 份变更 Markdown 的 238 个相对文件目标存在，`doc-links-final.json`；此检查不验证锚点。最终 `git diff --check` 通过。

| 检查 | 实际结果 |
|---|---|
| 最终 r3 平台专项 | 41/41 通过，0 失败、0 跳过；`platform-r1/tests/candidate-r3/static-platform-candidate-r3.trx` |
| 既有 world／capacity／bounds | 16/16 通过；`candidate-r3-regression/static-platform-r3-no-platform-regression.trx` |
| 真正 pre-bounds 旧模块能力保护 | 1/1 通过；`candidate-r3-prebounds-capability/static-platform-r3-prebounds-capability.trx` |
| 历史三体默认世界对照 | 六组各 900 步，完整状态／身份／接触／错误零容差相同，6/6 通过；`candidate-r3-accepted-three/static-platform-r3-accepted-three.trx` |
| 独立 B772 无平台对照 | 1／2／3 团子各 900 步，完整帧、身份、接触及错误完全一致；包括动作、拖动、增删、重置及局部边界 |
| 顶部连续支撑 | 1,800 步保持支撑；末尾最大速度 `0.008082361 < 0.03` |
| 独立绘制轮廓接触 | 最大静止间隙 `0.0005001587172350774` 物理单位，按 60 DIP／单位约 `0.03001 DIP < 2 DIP` |
| 真实侧碰 | 左右各 8 帧空中接触，均不获平台支撑或空中二次起跳 |
| 高速角点 | 左右近接触速度 `8.588777 / 8.588806`，各 183 接触帧；真实有界下压输入，速度门槛仍为至少 8 |
| 生命周期 | 100 次创建／移除／重新添加／重置／清除／释放；Box2D 分配字节初／终均 0 |
| native 支撑阈值 | 两侧、底部、间隙与 `.5` 严格边界，0 失败 |
| native 故障与恢复 | 第 4 个真实子步后破坏轮廓，触发 `-402`；身体、ID、接触步数和平台诊断冻结，修改拒绝，重置恢复，分配回收；0 失败 |

以上托管正向结果合计 64 个不同用例，0 失败、0 跳过。测试命令与输入／结果聚合保留于 `platform-r1/tests/test-commands-r3.txt`、`test-evidence-r3.json`。历史三体对照哈希与[原统一记录](../phase-1-physics-module-unification.md)一致，通过 `DANGO_PHYSICS_ACCEPTED_THREE_PATH` 从原目录只读加载，没有复制或重建旧夹具。默认 preview scale 为 1，原零容差比较保持，未更改历史断言。

独立几何 oracle 从公开身体帧重建既有显示归一化、缩放和二次 B 样条，帧中的轮廓已包含节点半径外扩，不重复外扩。2 DIP 预算固定为 `1/30` 物理单位，不随窗口缩小放宽；过程同样检查有限值、面积和速度。native 诊断没有冒充独立 oracle。

### 能失败的反例

最终两种错误候选由同一最终源码的隔离 CMake 编译变量产生。关闭接触变体在“120 步获得真实支撑”门禁 **1/1 失败**；侧碰误授支撑变体在第 39 步的实际侧面接触 **2/2 失败**，均为行为断言，均非 `MissingExport`。证据为 `platform-r1/tests/final-negative-contact/` 和 `final-negative-side-support/`。

只读旧 B772 的四项平台请求为 `MissingExport` 红，单独的旧模块能力守卫 1/1 通过；这是兼容性证据，不作为碰撞行为反例。证据保留在 `old-baseline-red/` 和 `old-capability-guard/`。

首轮 `candidate-first` 的 33/38 通过、5 个 fixture 失败保留：原侧碰检查误禁止从有限平台下方合法绕行，修正为独立几何交叠预算；原底碰 anchor 0 的首帧绘制最高点 `1.10435` 已高于底面 `0.9`，Add 拒绝正确，改用安全 anchor `-.4`；原拖放速度只达到 `5.055`，另建立真实 `Downward=1` 的高速接近，保留独立拖放测试。未降低接触、速度、面积或支撑门槛。

### 复现入口

先把 `$box2dSource` 指向已核实的固定源码；仅用 CMake 的独立输出，不使用会复制正式 DLL 的 `build-box2d.ps1`。构建目标为 `dango_physics_module` 和 `dango_static_platform_tests`，正常 `DANGO_STATIC_PLATFORM_TEST_VARIANT=0`，错误候选分别为 1 和 2。

```powershell
$candidate = (Resolve-Path artifacts/furniture/platform-r1/native-build/Release/dango_physics_module.dll).Path
$env:DANGO_PHYSICS_MODULE_PATH = $candidate
$env:DANGO_STATIC_PLATFORM_BASELINE_PATH = (Resolve-Path artifacts/furniture/platform-r1/baseline/dango_physics_module.dll).Path
dotnet test tests/Dango.Physics.Tests/Dango.Physics.Tests.csproj -c Release --filter 'FullyQualifiedName~StaticPlatformTests' --results-directory artifacts/furniture/platform-retest-new --logger 'trx;LogFileName=platform.trx'
./eng/run-static-platform-review.ps1 -RunName platform-review-new -NativeModule $candidate -ExpectedCandidateSha256 872B585F1048557766B9F4C4528FE71C0824DAC5C9FEA2AFE047E87F3CE86D36
```

所有新增证据目录必须使用新名字，不能覆盖本批产物。变量只用于测试进程，不写入系统配置。

## 真实窗口与性能

输入检查使用 `platform-ui-final-r4/bin/Dango.JointReview/release/`，最终窗口为 `platform-ui-final-r5/bin/Dango.JointReview/release/`；r5 仅补强观察结束条件及日志，输入／Step 路径未改。候选模块仍为同一 r3 哈希，两次构建均 0 警告、0 错误。OS 输入路径通过真实 HWND 的 `SendInput` 进入，没有直接调用 session 来冒充按键。输入证据为 `platform-ui-hwnd-r4/keyboard-check.json`、`lifecycle-check.json`、自身窗口 PNG 和 `close-result.json`；PID 44528 已正常退出 0。

- 当前 HWND DPI 144（150%），中文输入线程 HKL `8040804`。D 600 ms 使身体布局框 X 从 870 到 960，松键后停在 1002、速度降至 `0.0093`；A 返回后降至 `0.0059`。这些坐标仅为本任务窗口的脚本化验证数据。
- S 下压使布局框 Y `619 → 623 → 619`；Space 使 Y `619 → 494` 并清除平台支撑，持续按住不会落地后自动再次起跳。checkbox 焦点下 D 无位移，Space 只切换轮廓。
- 暂停后真实物理步 206 连续一秒不变；捕获为本 HWND，松鼠标及真实最小化后捕获均为 0；最小化时 201 步不变。D 失焦前速度 `1.4262`，恢复时即使键未松也降至 `0.0122`，X 904 不再移动，松键后降至 `0.0064`。
- 当前 150% DPI 下调整窗口尺寸，平台支撑、间隙 `0.00050` 和速度 `0.0051` 保持。对自身 1590×1200／1272×960 HWND PNG 测量，官方图像实色与碰撞覆盖层包围边界最大偏差 0 px，团子轮廓与平台顶线之间空白像素行均为 0；`visual-measurements.json` 保留方法与结果。此测量不推断抗锯齿的亚像素位置，透明圆角仍不决定碰撞。

首轮真实窗口 r2 的 Space／捕获等正常，但中文输入法下 D 没有位移，没有计为通过。最小修复仅为游戏 scene 禁用输入法处理，其他控件保留默认行为；在相同中文线程上重建复验得到上述真实动作。自动化助手有两次未获得正确目标前台的尝试，其记录保留且未计通过；最终助手在每次 OS 输入前核对本 HWND。没有用仅发送成功或静态姿态代替位移证明。

首轮 r4 观察有 20 次暂停／隐藏循环且退出 0，但期间实际最小化导致运行停止。墙钟 `600.0255682` 秒、活动仅 `414.6078782` 秒，不能作为完整持续运行证据；`platform-r1/observation-r4-incomplete.log` 独立有效时间检查为红，原 JSONL 和摘要完整保留。原物理暂停行为正确，缺口在观察器仅以墙钟判断结束。

观察器已补强为墙钟至少 600 秒、活动至少 560 秒（600 减去 20 次计划的两秒暂停／隐藏）；最小化或额外暂停自然延长观察，不补跑物理。JSONL 和摘要明确记录 `WindowState`／`Minimized`，提前关闭同样检查有效时间。

最终 r5 观察与独立有效时间检查通过，证据为 `platform-ui-final-r5/observation/observation-summary.json`、`observation.jsonl`、`platform-r1/observation-r5-validation.log`、`observation-r5-aggregate.json` 和 `observation-r5-exit.json`。本轮任务中断期间窗口长时间最小化，恢复同一进程后完成剩余活动时间；墙钟时间不是长期主动游玩证据。

| 最终运行测量 | 结果 |
|---|---:|
| 墙钟／活动时间 | `7793.2391863 / 560.0351063` 秒 |
| 真实物理步／新状态绘制 | `33,530 / 19,641` |
| 物理步频率／新状态绘制频率 | `59.87125 / 35.07102` 次／活动秒 |
| native Step P95／最大耗时 | `4.1742 / 49.0572` ms |
| native 诊断峰值接触误差 | `0.0010274648666381836 < 1/30` 物理单位 |
| 暂停／隐藏恢复 | `20 / 20` 次，暂停时内部步数不变 |
| 活动样本 CPU 均值／峰值 | `1.52746% / 5.37503%`，按逻辑处理器数量归一化 |
| Working-set 峰值／最后样本 | `268,648,448 / 228,868,096` bytes |
| 丢弃的模拟时间 | `1.0397795` 秒，另行记录，不补跑挂起时间 |
| 所有 745 个样本 | native 错误 0，物理世界 owner 始终 1 |
| 退出与清理 | PID 42596 退出 0；世界／计时器 owner 均 0，计时器停止，鼠标捕获释放 |

以上绘制频率为 `Draw` 新状态更新次数，不代表显示合成 FPS；不关闭既有 59 FPS 性能门禁。Working-set 是观测值，不单独作为泄漏证明；资源回收结合退出清理及 100 次 native 生命周期门禁。长时间最小化不算长期主动游玩或完整睡眠验收。真实物理设备手感、其他硬件及用户人工验收仍未验证。

## 人工验收与交付状态

- 用户人工手感验收：待完成；自动门禁、真实输入核验和运行观察不能替代用户判断。
- Git：本批在任务分支保存本地实施提交，具体哈希见分支日志；main 未合并，远端未推送。
- 日常安装、受保护 DLL 与部署：未改动。
- 当前窗口及测试进程：输入检查 PID 44528、首次观察 PID 11048、最终观察 PID 42596 均已退出；交付仅保留最终 r5 的用户验收窗口，不停止无关进程。
- 没有服务、端口、数据库或云资源；保留隔离构建、TRX、JSONL 和自身窗口截图，无资源删除操作。
- 尚未验证 100%／125% 等其他 DPI、多屏、睡眠、其他硬件、长期稳定性、正式房间和多人；150% 与一次窗口尺寸变更已执行，既有性能门禁继续保留。
