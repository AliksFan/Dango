# DIY 配饰第三轮反馈修订验证

日期：2026-10-08。分支 `codex/accessory-diy`，起点 `baf88f4`。本轮按[设计第 12 节](../planning/2026-10-07-accessory-diy-design.md)与[实施范围](../planning/2026-10-07-accessory-diy-plan.md)修改本地候选，尚未获得用户人工验收。本记录不改写[第二轮红灯、隔离复验与性能波动](2026-10-08-accessory-ui-refinement.md)。

## 范围与实际实现

- 内容区隐藏“配饰库”标题；团子右上侧为创建／复制／删除图标，与左侧分组顶部对齐。
- 已佩戴独立位于图库顶部，未佩戴位于下方，每行四图。双击／右键佩戴或取消，图像与位置页移除佩戴勾选。拖动两区分别排序，已佩戴同时更新同一前／后层的绘制顺序；跨区或跨层拖动拒绝，不隐式修改佩戴或前后层。排序仍由应用保存。
- 两次确认后永久删除目标条目并立即持久化；只提交最近保存清单移除目标，不提交其他草稿或 pending 图片。外层取消不会复活目标；草稿项删除不写整套清单。共享图片与已有磁盘图片保留，本轮不清理孤立图片。
- W 不跳也不导航，只用 Space 跳跃。配饰预览真实 native 启动速度设为 0.5，退出／释放 owner 恢复 1；镜头仅横向跟随，起跳、下降和落地纵向保持固定。
- 柔软度高端渐增到三倍材料响应，连接强度低端最低恢复频率由 0.75 降至 0.35 Hz；界面仍为 0–100，常用区间和连接位移边界保留。
- 创建入口捕获尺寸校验的 InvalidDataException，超限素材给出错误，窗口继续可用。

本轮主要源文件为 [Panel](../../src/Dango.Desktop/Accessories/AccessoryEditorPanel.cs)、[Session](../../src/Dango.Desktop/Accessories/AccessorySettingsSession.cs)、[MotionSolver](../../src/Dango.Desktop/Accessories/AccessoryMotionSolver.cs)、[ProductHome 输入](../../src/Dango.Desktop/AppShell/ControlCenter/ProductHomeView.LiveScene.cs)、[配饰相机与工具接线](../../src/Dango.Desktop/AppShell/ControlCenter/ProductHomeView.Accessories.cs)、[布局](../../src/Dango.Desktop/AppShell/ControlCenter/ProductHomeView.xaml)、[宿主切换](../../src/Dango.Desktop/AppShell/ControlCenter/ProductHomeView.Porch.cs)、[native 模块](../../eng/native/box2d/dango_collision_review.c)、[PhysicsWorld](../../src/Dango.Physics/DangoPhysicsWorld.cs)、[Scene](../../src/Dango.Desktop/Physics/Presentation/DesktopPhysicsScene.cs)与 [Runtime](../../src/Dango.Desktop/Rendering/Dango/StackedDangoSceneRuntime.cs)。

## 根因、反例与修正

实际崩溃日志为 `artifacts/accessory-acceptance/ui-v2-final.stderr.log`。异常路径为 ValidateDimensions → ValidatePngStructure → ImportPng → Session → Panel.Execute → Create.Click，超尺寸 2049×8 透明 PNG 在打开图片编辑器之前被拒绝。InvalidDataException 不属于 IOException，原入口 catch 漏捕获；修复范围仅为该入口。真实创建点击的旧实现 1/1 失败，修复后同一超限检查及合法素材真实 ShowDialog 确认／取消 3/3 通过。文件选择结果使用虚构路径回调，未自动操作 Windows 文件选择器本身；图像编辑模态没有 stub。

删除先用明确返回 false 的错误实现建立反例，13 项中 12 失败；实现单目标事务后 13/13 通过。覆盖保存快照与草稿分离、pending 和共享图、旧帽合法空清单、坏／未来清单、文件锁、原子写失败、外部合法改动和提交后 writer 抛错。外部变化检查是保存前快照比较，现有 Store 没有跨进程 CAS。

材料端点测试先对旧实现运行：13 项中两项预期失败；增强后新 13 项及原运动 6 项共 19/19 通过。256 DIP 未饱和示例弯曲／应变为旧映射三倍，最低连接强度位移 62.033375 DIP 对旧 13.509491 DIP。小图仍受位移上限限制，不能宣称所有素材都达到同样倍率。极端尺寸、任意支点、旋转、重力和快速锚点下形变有限、有界且网格不翻转。

复核另修正两处：上／下移与拖动使用相同的已佩戴层内候选，避免与未佩戴项交换；删除遇保护清单失败后刷新禁用状态，同时保留未确认输入。缺低跳导出时，unbounded 已成功设置而后续低跳失败的退出路径必须清理预览模式；真实 UI 的不可用→Esc→普通 Home 门禁覆盖这一点。

## 构建、native 与证据

新 native 构建使用现有 Box2D 3.1.1 固定 archive，SHA-256 `FF94DDF7D75FFA6171502FDC7A7F336594560278978305CFC56C72268BC66721`；fresh 解压至当前隔离工作区，许可证哈希与依赖声明一致。只构建 `dango_physics_module`，`/W4 /WX` 通过；保持 ABI 1，原 36 个导出均保留，仅新增一个可选的 preview-jump-speed 导出，配置结构与控制槽不变。

| 文件 | SHA-256 |
|---|---|
| 正式 `dango_box2d.dll`，未替换 | `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A` |
| 本轮隔离低跳候选 | `B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E` |
| 原第二轮模块，未修改 | `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507` |

低跳独立专项初次为 10/11：普通／低跳／恢复 native 峰高分别为 1.615999、0.527735、1.615988，低跳为普通的 32.6569%；默认 1／2／3 体各 1200 帧与旧模块逐字段 bit 相同。唯一初始红灯来自额外真实故障测试的夹具未能触发 -88；仅调整合法实验参数后，真实 -88 清理专项 1/1 通过，产品和原门槛未改，原红灯保留。非法倍率、多体、旧模块缺导出、零额外步进、同世界／实体／owner、Reset 与故障清理均有专项检查。

专项证据保留：

- `artifacts/accessory-ui-v3-crash-harness/results/accessory-create-oversized-before.trx` 和 `accessory-create-crash-after.trx`。
- `artifacts/accessory-ui-v3-deletion-harness/results/accessory-deletion-before.trx` 和 `accessory-deletion-after.trx`。
- `artifacts/accessory-ui-v3-material-harness/results/material-range-red.trx` 和 `material-range-green.trx`。
- `artifacts/accessory-low-jump-verification-20261008/accessory-low-jump-native-r1.trx` 和 `accessory-low-jump-fault-r2.trx`。

最终统一输出为 `artifacts/accessory-ui-v3-final-build`。Release 构建 0 警告／0 错误；受影响 C# 的 whitespace verify、Markdown 相对链接和 `git diff --check` 通过。

| 门禁 | 通过／总计 | 证据 |
|---|---|---|
| 物理全回归 | 52/52，零跳过 | `artifacts/accessory-ui-v3-verification/physics-regression.trx`；20 分 25 秒 |
| 功能首轮 | 273/278，5 失败，零跳过 | `accessory-v3-functional-r1.trx` |
| 最终功能复验 | 277/277，零跳过 | `accessory-v3-functional-final.trx`；3 分 24 秒 |
| 隔离性能 | 4/4，零跳过 | `accessory-v3-isolated-performance.trx` |

功能首轮的五个失败为旧测试适配问题：三项仍在参数面板寻找已移到右侧工具列的创建按钮；两项要求 1024×560 的位置页必须溢出，但移除库内操作和佩戴勾选后该页已自然完整放下。创建检查转到真实 ActionToolsSlot；真实滚动门禁仍保留在已验证溢出的 640×420／480×800 组件测试窗口，同时新增 1024×560 的完整适配、工具可达与应用重载检查。正式控制中心最小尺寸仍为 820×560，未把组件尺寸冒充正式窗口可达尺寸。两项旧必溢出行替换为一项自然适配门禁，因此最终总计为 277，不将首轮改写为全绿。

最终功能组包括全部配饰测试、RoomCameraPresentationTests、DesktopPhysicsSceneTests、ProductHomeInteractionTests、HomePresentationLayoutTests、UnifiedLiveHomeTests、LocalRoomProductTests 和 HomeJumpShadowTests；两种性能方法单独运行。低跳 11 项在最终同一批次全部通过；真实 Space 起跳／下降／落地、所有帧固定纵向镜头、八种尺寸／旧素材组合的头部及接地门禁、W 不跳、缺低跳导出后的不可用／Esc 清理、双击／右键与实际 routed drop、双重删除取消／保护、导入合法模态及超限错误、真实应用进程重启和单实例拒绝均有最终证据。

性能组保留完整 2048px 源图、八件、Softness=100、真实动态形变及原 16 ms 门槛。150% 等效大源图 solve+raster p95 为 9.35 ms；真实 WPF 窗口测得 150%×150% DPI、渲染 tier 2，solve+raster p95 14.95 ms、最高 19.75 ms，实际约 59.44 FPS，超过 100 ms 的间隔为零。等效采样与真实 DPI 结果分别记录，单次通过不抹掉第二轮性能波动。随后仅修正测试夹具与测试格式并增量重建，产品 DLL 哈希保持 `7D84408FC6C65D696927B697148D3D3D7DA9BC420AEBD67C0BBAAA5EBF673EE5`，性能对应同一产品二进制。

## 实际候选窗口与残留

所有测试进程结束后才打开候选，避免占用重启用例的 review mutex。当前 PID **13616**，实际运行 `artifacts/accessory-ui-v3-final-build/bin/Dango.Desktop/release/Dango.Desktop.exe`，仍使用有虚构数据所有权 marker 的原 `artifacts/accessory-acceptance/review`。启动前后 accessories.json 与 appearance.json 哈希一致，没有重新生成或覆盖既有数据；stderr 为空。

实际 UI Automation 观察到四个左分组与三个右操作均 enabled 且 onscreen，两列首项 Y 均 435 物理像素，按钮 48×48 物理像素（150% 时为 32 DIP），间距 12 物理像素（8 DIP）。真实窗口截图 `artifacts/accessory-acceptance/ui-v3-window.png` 已查看；窗口呈现独立已佩戴／未佩戴区，内容区没有“配饰库”标题。证据为 `ui-v3-launch-evidence.json`，重开入口 `artifacts/accessory-acceptance/Launch.cmd` 已指向新候选。

已保留本轮全部测试数据、截图、TRX、native archive／源码与隔离构建输出；本轮没有磁盘资源清理。结束时本任务仅保留供用户验收的 Dango PID 13616，没有本任务 testhost 残留，没有新增端口或服务；其他工作区进程未操作。

## 未验证与交付边界

用户人工验收尚未完成。真实 Windows 多档 DPI 矩阵、其他宿主重新人工观感及旧 Wallpaper 差异继续沿用旧记录的未验证／风险状态。本轮不实现分享、上传、云端、多人与配饰间挂接，不迁移 Schema，不合并 main、不推送、不更新日常安装、不部署、不替换正式 DLL。
