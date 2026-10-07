# DIY 配饰首版本地实施验证

日期：2026-10-07。分支：`codex/accessory-diy`。实现起点：`8ff78e03d6695d90cd1d9d5e9c5eaad9883fc491`。用户已批准[设计](../planning/2026-10-07-accessory-diy-design.md)并明确“通过，开始实施”；本报告交付可人工验收的 Windows 本地候选，未标记阶段已验收。

## 范围与实现

- 静态透明 PNG 导入，内部哈希资产保存；单文件 ≤16 MiB，最长边 ≤2048，像素总量 ≤4,194,304。源文件可以移动，清单不记录源路径。
- 独立配饰库和副本，最多同时佩戴八件，团子前／后图层、层内排序；同序号按 Ordinal 实例 ID 保持实际绘制顺序。没有官方分类。
- 非破坏性图片尺寸、旋转、查看缩放及图片支点；身体附着位置可用数值或当前真实身体轮廓点选。
- 图片内部弯曲／拉伸柔软度，与连接弹性分开；旋转阻尼、锁定朝向、回正、最大摆角及重力影响。
- 草稿、编辑器确认、取消和显式应用分开；独立 Schema 1 清单、图片先写／清单最后提交、坏图隔离和坏清单保护。旧三种素材进入统一库；缺失清单才使用旧帽兼容，合法空列表与损坏清单不会恢复旧帽。
- 首页、门廊、普通房 Window／Wallpaper、标准 Window、WorkerW／overlay 消费同一 runtime 配饰帧。配饰不创建物理实体，不碰撞、不施加反作用、不抢身体时钟，不改变命中或既有自主移动计算。

主要文件为 `src/Dango.Desktop/Accessories/`，`ProductHomeView.Accessories.cs`、`StackedDangoSceneRuntime.Accessories.cs`，以及已有 Home／Porch／Room／Window／WorkerW 的窄接缝；相应失败反例在 `tests/Dango.Desktop.Tests/Accessories/`。未新增依赖、修改 native API 或替换正式 DLL。

配饰挂接到另一件配饰、区块色相、局部材质绘制、多区域身体贴合和云端／多人资产同步均未实现，继续按已批准边界后置。

## 与捏脸界面对齐

按用户“ui方面和捏脸模块对齐”的后续要求，换装保留左参数／右预览的方向，复用捏脸暖白主题、字体、滑杆、同行数值、32 DIP 线性图标及状态文字。图像／位置、柔性／连接、旋转／重力三组可收起；预览说明与收起按钮固定在团子上方，当前配饰复位／取消／应用固定在预览下方，滚动参数不会带走保存操作。独立图片编辑窗口使用同一主题和固定确认栏。

新增布局反例会拒绝 footer 被放回滚动区、图标被放大、团子被改变尺寸；还覆盖双模块取消隔离、当前条目复位、真实按钮点击和应用重载。受控截图仅绘制测试自有 WPF 控件，首页布局截图未绑定 runtime，不能作为真实 native 场景或人工验收通过证据。最初 footer SizeChanged 打断换装入场动画的问题已修复，原动画断言保留；150% DPI 的布局取整按实际像素检查，截图父容器偏移只修正捕获方式。

最终自审新增了真实控件焦点反例：有效清单中仅修改名称／数字后直接应用、非法混合输入不能部分保存、高对比弹窗底色与文字成对复制。首次四项全部失败，保留在 `artifacts/accessory-ui-editor-harness/TestResults/accessory-editor-input-theme-before.trx`。图片弹窗两项首次失败另保留于 `accessory-editor-modal-input-before.trx`：聚焦输入合法 185 后确认仍返回旧 100，非法 500 仍关闭窗口并返回旧值。换选条目／复制／打开图片编辑的合法与非法输入共六项首次失败，保留于 `accessory-editor-cross-item-before.trx`。修复与最终验证结果分别记录，不能用方法级调用代替真实焦点／按钮路径。

修复让输入缓冲参与应用按钮状态，并在提交或依赖操作前整批校验；非法输入保留在原件上，合法输入先提交到原件，再切换／复制／进入图片编辑。未触碰字段保存原 double 精度；图片弹窗确认也先整批校验。用户可以拖动同一参数滑杆纠正其非法文字；其他字段的非法缓冲仍阻止提交。主题增加共享底色，弹窗底色与文字来自同一 Home 主题。选项事件在校验刷新前捕获用户刚选择的值，避免佩戴／图层／锁定／摆角操作被旧值覆盖；对应首次十二行中四项失败，保留于 `accessory-editor-choice-slider-before.trx`。

独立完整编辑器类最终 **37/37 通过，0 失败／跳过**，证据 `artifacts/accessory-ui-editor-harness/TestResults/accessory-editor-complete-input-boundaries-after.trx`。Home 布局／交互独立检查 **50/50**（新增 19、原交互 10、原布局 21），证据 `artifacts/accessory-ui-home-harness/results/accessory-home-ui-final.trx`；这些重复用例不与集中组相加。

最终应用进程检查曾出现第二进程入口查找超时（`accessories-ui-verified-functional.trx`，180 通过／1 失败）及一次保存轮询超时（`accessories-ui-process-window-selection.trx`，11 通过／1 失败），均保留。原测试使用 `Process.MainWindowHandle`，未限定实际产品窗；现按自有 PID 加产品窗名检索，并记录窗口及真实输入状态。另实测持有 `FileShare.Read` 句柄会阻止 Windows 的 `File.Move(... overwrite:true)`，关闭读句柄后同一替换成功，证据 `artifacts/accessory-verification/read-share-probe-d1a5a383384a49a1891ce80111df725b/evidence.json`。测试在 UIA 异步调用后立刻反复读取目标可能干扰写入，现先等待数值回读、UI 保存完成与应用按钮禁用，再独立读取磁盘一次并断言 0.73；没有移除磁盘断言、重试应用或改动产品保存逻辑。历史超时未捕获足够状态，不能把上述竞态写成每次超时已证实的唯一根因。

## 静态与自动门禁

最终 Release Desktop 构建：零警告、零错误。受影响 C# 的 `dotnet format whitespace --verify-no-changes` 通过；新旧文件的 `git diff --check` 在提交前核对。

| 验证 | 实际结果 | 本地证据（`artifacts/accessory-verification/`） |
|---|---|---|
| UI 修订前的配饰专项、真实进程、受影响 Home／Settings／Camera／DesktopPhysics | 134 通过，0 失败，0 跳过 | `accessories-final.trx` |
| UI 对齐后的同组集中检查 | 156 通过、1 性能失败，0 跳过；共 157 项 | `accessories-ui-final.trx` |
| 同源独立重跑原可见窗口性能用例 | 1 通过，0 失败，0 跳过；原阈值保留 | `accessories-ui-performance-isolated.trx` |
| 输入修复、真实进程诊断后的最终受影响功能组 | 181 通过，0 失败，0 跳过；与性能用例分开 | `accessories-ui-verified-functional-final.trx` |
| 同一最终构建的原可见窗口性能用例 | 1 通过，0 失败，0 跳过 | `accessories-ui-verified-performance.trx` |
| Core 完整回归 | 37 通过，0 失败，0 跳过 | `accessories-core.trx` |
| Physics 完整回归 | 52 通过，0 失败，0 跳过；运行 19 分 25 秒 | `accessories-physics.trx` |
| 使用真正旧模块的 GaitSampleIsolation | 7 通过，0 失败，0 跳过 | `accessories-pinned-gait.trx` |

另外，A0 独立最终 harness 18/18 通过，证据为 `artifacts/accessory-a0-harness/TestResults/accessory-a0-final-owned.trx`；其用例已包含在最终配饰专项中，不重复计数。

反例覆盖图片与清单写失败、提交后异常、取消不写、坏图配置保留、未来版本保护、副本独立、透明接缝、真实内部形变、弹性／旋转／重力有界恢复、缩小无残影、UV 缓存失效、失败后重试和批次单件错误隔离。同序号跨层迁移反例直接检查既有 Canvas Children 引用、ZIndex 与 mesh 支点，重复预览不推进 native 步数。

## 实时与进程证据

实际可见 WPF 窗口为 **150% ×150% DPI、tier 2、1085 ×583 DIP**。八件各使用完整 **2048px 源图、256 DIP、柔软度 100**，并持续改变位置以产生真实形变和旋转。UI 修订前集中测量的求解＋渲染管线（含准备、光栅与上传）median **12.52 ms**、p95 **14.60 ms**、max **23.96 ms**。

UI 对齐后的集中运行 p95 **16.54 ms**，超过原 16 ms 门槛，已保留失败。未改求解／光栅源码、测试输入或阈值，随后独立运行同一原用例，median **11.56 ms**、p95 **14.08 ms**、max **21.86 ms**，通过原门槛；Rendering 回调约 **60.25 FPS**，间隔 p95 **16.60 ms**，超过 100 ms 的间隔为 **0**。两次 raster p95 分别为 13.68／11.27 ms，原因尚未确定，不能把独立通过写成稳定性风险已消失。没有降低源图尺寸、DPI、网格、柔软度或阈值。此值是配饰求解和绘制专项耗时，不冒充整个产品所有场景的帧时保证。

输入修复后的最终构建以同一原性能用例实测 median **11.04 ms**、p95 **14.50 ms**、max **20.91 ms**，raster p95 **10.91 ms**、上传 p95 **1.30 ms**；仍为上述实际 150% DPI、原 2048px 源图和动态八件形变。Rendering 为 **60.25 FPS**，间隔 p95 **16.60 ms**，超过 100 ms 间隔为 **0**。这是本轮最后一次测量，先前集中超限继续作为稳定性风险保留。

光栅方案为同张预乘 Alpha 位图上的完整源图采样；UI 线程准备和上传，最多八件一次有界 CPU 并行计算，上传新旧占用范围的并集。最终实际截图为 `artifacts/accessory-ui-final-build/bin/Dango.Desktop.Tests/release/evidence/accessory-a0/actual-visible-eight-accessories.png`，早期截图也保留。自动像素检查及受控截图检查通过，不等于用户人工观感确认。

实际 native 身体对照检查完整身体、轮廓与步态快照 550 步一致，覆盖柔性、重力、弹性、多件佩戴及跳跃；真实曲线身体与附着点检查 540 步。Home → Porch → 普通房 Window → Wallpaper → Window → Porch → Home 保持同一实体与驱动。标准 Window／WorkerW 的上下图层、交互开关、合法空清单和损坏清单以离屏透明宿主 fixture 检查通过；实际 WorkerW 挂载与独占标准 Window 交接另有通过证据。above-icons overlay 在自动 fixture 中保持透明，这些检查不能证明用户实际看到所有上下层或真实实体鼠标输入均通过。

真实应用进程用例先以 Session 导入并保存虚构图片，再在第一个进程启动前移动源图；通过 UI Automation 打开换装，聚焦身体附着 X 数值框，用 `ValuePattern.SetValue` 从 0.27 改为 0.73，不先 Enter／Tab，直接以 `InvokePattern` 调用“应用”；确认 UI 保存完成后独立核对磁盘，正常退出并启动另一 PID，实际界面恢复 0.73。该用例没有自动操作文件选择器或图片编辑模态窗口；导入和编辑器的自动控件／合同检查与此进程证据分开，完整人工操作链路仍待用户试用。最终运行第一 PID **4172**、第二 PID **17156**，正常退出码均为 0；重复实例正常拒绝。日志为 `artifacts/accessory-verification/process/f39a169747fd49278c2783e47b42fa6e-restart/process-events.jsonl`。进程类共十二项，包含单实例、启动参数、标记与目录／文件边界反例。

实际系统 **100%／125% DPI 未验证**；100% 与150%的等效像素专项不能冒充系统 DPI 切换。用户在各宿主上的长期体验和最终观感也未验证。

## 保留的失败与回归差异

第一次完整 Desktop 回归为 **1222 项：1176 通过、24 失败、22 跳过**，证据 `desktop-regression.trx`。保留原始失败，未修改门槛或删除断言。

| 初次失败 | 后续处理与当前结论 |
|---|---|
| 八项 DesktopPhysicsScene 缺少约定的 native 测试路径 | 在此前缺失的 ignored 测试目录提供实测哈希正确的候选；原测试重跑通过 |
| 四项 GaitSampleIsolation 拒绝较新模块哈希 | 独立输出使用工具指定的真正旧模块；完整类 7/7 通过，未放宽哈希 |
| 四项 RoomCameraPresentation 因兼容模式新增空配饰层 | 修复兼容／禁用模式不创建空层，原类重跑通过 |
| 一项八件配饰真实窗口性能失败 | 改为整批并行和占用区域上传；原图、DPI、网格及 16 ms 原门禁保留，最终通过 |
| 旧 `SharedSelfWorldExportsAreAvailable` 一项和旧 PortraitEditor 布局两项 | 在未修改的 HEAD 源码中复现同样三项失败，`clean-head-known-failures.trx`；保留旧 DLL 与已批准当前布局，不在本批顺手修改 |
| 四项完整普通房 Wallpaper 前台／指针／Shell 资格及 WorkerW 往返检查 | 完整运行中未通过；受实际前台获取条件影响，本批未修复或宣称通过。配饰显示与 WorkerW 交接通过不能替代这四项 |

未修改 HEAD 的比较源码保留于 `artifacts/accessory-verification/baseline-1dbca46322f447ad912e97ae01b21f98/`。完整 Desktop 初次运行之后，按改变的根因重跑受影响项目；未再机械重复全部 1222 项，22 项未执行记录也没有关闭。

## Native 与本地交付

- 正式 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll` SHA-256 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。
- 当前隔离运行候选为 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`；旧步态／Physics 对照为 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`。MSBuild 输入路径只存在本地命令与 ignored artifacts，未提交本机路径。
- 完整 Physics 的三个较早比较 DLL 只复制到此前不存在的 ignored 测试产物路径，源文件与既有目标都先核对哈希；没有覆盖已有不同文件或正式 DLL。
- `artifacts/accessory-acceptance/Launch.cmd` 指向包含本轮捏脸 UI 对齐和输入修复的 `artifacts/accessory-ui-final-build` Release 输出，使用带 `.dango-accessory-review.json` 所有权标记的独立 `review` 设置根目录；与日常单实例互不冲突。已打开实际产品换装窗口，保留 PID **22560** 供用户试用。原验收 PID 25204 在复查无草稿、无模态、名称及十个数字均无未提交输入后正常退出；同一 `review` 数据未清理，新窗口恢复原选择“小礼帽”。状态记录为 `artifacts/accessory-acceptance/ui-final-launch-evidence.json`。
- `samples/` 内两张透明 PNG 为虚构测试图案，可导入；初始配饰库沿用三个内置条目，用户也可以选择自己的图片。关闭后重新使用启动器即可检查本地恢复。
- 自动进程用例的自有子进程已退出。验收进程、虚构测试数据、编译输出、失败 TRX 和截图主动保留；未启动数据库、云资源或外部服务，未删除测试数据或 Worktree。
- 本报告随实现、测试及设计一并纳入本地任务分支提交；未合并 `main`、推送远端、更新日常安装、部署或替换正式 DLL。人工验收待用户确认，不能从本报告标记整个 Windows 阶段完成。
