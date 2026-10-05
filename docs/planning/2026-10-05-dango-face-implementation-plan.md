# Windows 团子捏脸实施计划

日期：2026-10-05；状态更新于 2026-10-06。**用户已授权 F0–F5 实施；F0–F4 已实现；F5 核心门禁、150% 实窗验证及当前候选人工观感通过，100%／125% 和其它显示器仍待验证**。用户已通过[捏脸交互设计](2026-10-05-dango-face-design.md)，并明确选择“保留现有 Windows 默认长相，校准滑块映射”。此前“开始下一步”承接实施计划编制。随后用户明确“按计划开始实现”，据此实施 F0–F5；授权仍不包含 main 合并、推送或日常安装。最新[入口与布局设计](2026-10-06-home-and-appearance-design.md)和[人工确认记录](../verification/2026-10-05-home-icons-and-ground-shadow.md#2026-10-06用户人工观感确认与设计归档)补充原计划，不关闭完整阶段专项。

## 1. 交付目标与基线

在现有“我的团子”首页，完整实现身体／眼睛 RGB 与大小、椭圆度、高度、眼距四滑块，形成同一实体的即时预览、整套取消、恢复默认、显式应用、失败重试和磁盘重启恢复。动态注视、眨眼、困倦和睡眠继续叠加在基础长相上。帽子作为独立换装部分，不进入本次模型或保存批次。

计划编制时的只读核对（以下是历史快照，不描述当前实现状态）：

- 专属工作树为 `codex/appearance-face-design@b75ecc0`，开始时干净；基于 `main@e2f4037`，独立颜色模块已在 `b01909f` 整理，四滑块示意在 `ca6fd57`、设计确认在 `b75ecc0`。
- 当前颜色 Store／Session 仅处理严格 Schema 1／2，生产首页尚未创建外观会话。旧颜色整理 33/33、新示意 165 项检查属于既有证据，不能当作本计划的产品验证。
- 普通房分支核对为 `codex/local-room-implementation@784e715`，并有进行中的未提交修改；Host、Runtime、首页 LiveScene／Porch 及启动入口均有重叠。只读参考对方工作树的 `docs/verification/2026-10-05-local-room-product.md`（当时尚未提交），不复制未提交文件或据此声称普通房已人工验收。
- 不使用已删除的本地 `team-integration` 作为起点；保留旧色板源工作树、本机设置和受保护 DLL。实施前重新核对实际 refs、状态与普通房负责人交付，不把此次快照当作未来固定 HEAD。

本捏脸既有结果见[颜色整理与设计记录](../verification/2026-10-05-appearance-consolidation-and-face-design.md)。普通房当前验证记录只存在另一工作树；其实现与未提交状态作为本次现场事实记录，不为它补写完成状态。

不增加依赖、数据库、服务、协议、账号或多人同步；不改身体碰撞、尺寸、形状、柔软度或 native 源码。不替换日常 DLL，不更新日常安装。范围是 Windows 本地产品自身外观。

## 2. 已确认的坐标合同

选择旧 Windows 默认作为锚点，避免把 HTML 的身体比例直接套进原生几何。现有 [FaceLayoutSolver](../../src/Dango.Desktop/Physics/Presentation/Attachments/FaceLayoutSolver.cs) 使用直径 `12/160`、双眼中心距 `38/160`，中心高度由 `1/160` 与身体安全区求出。真实轮廓宽高来自 [AttachmentShapeGeometry](../../src/Dango.Desktop/Physics/Presentation/Attachments/AttachmentShapeGeometry.cs)，不是 HTML 或固定 Canvas 矩形。

已实现纯托管、不可变 `EyeLayoutParameters`，位于 `Physics/Presentation/Attachments/`；不引用 WPF、不持有 runtime 或帽子。AppShell 和渲染层消费同一合同，字段使用控制单位：

| 字段 | 合法区间／默认 | 生产含义 |
|---|---|---|
| `SizePercent` | 70～140／100 | 相对现有眼睛直径的百分比 |
| `OvalPercent` | -100～100／0 | 负横、零圆、正竖；短轴倍率 `1 - abs(value)/200` |
| `HeightControlPercent` | 32～66／51 | 相对旧默认中心，上下移动身体高度的增量百分点 |
| `SpacingControlPercent` | 12～34／24.5 | 相对旧默认中心距离，增减身体宽度的百分点 |

界面仍为四滑块；大小左小右大、椭圆度左横中圆右竖、高度左高右低、眼距左近右远。大小／椭圆度不移动眼中心，眼距只对称改变中心距离。高度显示默认／上移／下移，眼距以默认／靠近／分开及相对调节量展示；后两项不标成当前真实身体比例。UI 步长分别 1、1、1、0.5，存储接受合法连续数值，不用步长网格替代范围校验。

在现有向上为正的材料坐标中，先构造基础请求，再叠加原动态注视与倾斜：

```text
D = (12 / 160) * SizePercent / 100
q = 1 - abs(OvalPercent) / 200
width  = D * (OvalPercent > 0 ? q : 1)
height = D * (OvalPercent < 0 ? q : 1)
yLegacy = Clamp(1 / 160, Bottom + .35 * H, Bottom + .65 * H)
yRequested = yLegacy - (HeightControlPercent - 51) / 100 * H
distanceRequested = 38 / 160 + (SpacingControlPercent - 24.5) / 100 * W
```

`W/H/Bottom` 使用同一帧真实轮廓。非默认参数的中线 X 在该请求高度的可用截面中求出，双眼在中线左右各偏移中心距离的一半；无有效截面或派生中心距离不合法时进入安全求解，不能产生非法几何。无配置路径和显式四项默认请求均保留旧完整求解分支，包括候选高度优先序、旧位置钳制及原退让顺序；仅公式默认相等不能替代这个要求。默认请求必须通过完整冻结参考与像素门禁，不能缩减为静态中性体相等。不整批重写原 solver 或删除冻结参考。

安全退让可暂时调整注视、位置或尺寸，但不得反写请求参数、滑块或存储。新增高度不能被旧固定高度区间直接钳死，导致部分滑块无效；安全求解按实际轮廓判定。默认等价门禁如揭示历史不安全样本，先复现并记录具体差异，不降低断言或修改冻结参考来强行通过。

## 3. 保存与草稿合同

已实现 `Rendering/Dango/DangoAppearanceConfiguration.cs`，聚合身体色、眼睛色与 `EyeLayoutParameters`。Session 使用整套 `SavedAppearance`／`DraftAppearance`；颜色访问器从聚合派生。帽子不在类型内。

Schema 3 为严格七字段对象，布局数字按上节控制单位保存：

```json
{
  "appearanceSchemaVersion": 3,
  "bodyColorHex": "#FFE8C2",
  "eyeColorHex": "#2B201E",
  "eyeSizePercent": 100,
  "eyeOvalityPercent": 0,
  "eyeHeightControlPercent": 51,
  "eyeSpacingControlPercent": 24.5
}
```

- RGB 继续严格大写 `#RRGGBB`；黑色、暗色及同色合法，不自动反色或钳制。
- 四数值必须是 JSON 数字、有限且在闭区间内；缺失、重复、额外字段、错误类型、指数溢出、非法色、越界及未来版本均整套安全默认、提示并保护原文件。Save 入口也校验，不能仅依赖 UI。
- Schema 1 保持精确两字段，保留原身体色并补默认眼睛色／布局；Schema 2 保持精确三字段，保留两色并补默认布局。旧版兼容分支也拒绝额外未来字段。读取不自动写盘；标记待迁移，只有显式“应用”成功才写 Schema 3。旧版本遇 Schema 3 继续保护，不能覆盖未知文件。
- `IsDirty` 与 `HasPendingSave` 区分。旧文件没有视觉差异时也允许显式应用升级；取消仅回滚草稿，不自动迁移，也不通过清掉待升级标记掩盖版本状态。
- 恢复默认覆盖颜色及四滑块的整套草稿；现有 `RestoreDefault()` 只改身体色，需修正。取消还原整套最近已保存值；成功写盘后才更新 Saved。失败保留旧文件、Saved 和完整草稿，允许重试或取消。
- 坏文件恢复沿用显式备份流程：备份失败不解锁；备份成功即解锁并置强制待保存，随后写失败不丢原文件或草稿。即使草稿与默认 Saved 相等，普通“应用”也仍可重试；失败不前移 Saved，重试不重复备份。不自动删除、重置或恢复用户文件。
- 新 `appearance.json` 放在实际 `settingsPath` 所在目录，随隔离运行目录走；不另写硬编码的日常目录。启动、隐藏、退出、主设置延迟保存和房间切换均不隐式应用外观草稿。

复用既有 [AtomicTextFileWriter](../../src/Dango.Desktop/Verification/VerificationSessionStore.cs) 的同目录临时文件与替换边界，不顺手重构通用 writer。写前抛异常不能代替真实替换失败验证；本计划不承诺断电耐久性。

## 4. 分批实施与可失败验收

| 批次 | 目标与文件所有权 | 必须先定义、随后实现的失败型验收 |
|---|---|---|
| F0 合同与基线 | 负责人先冻结新增 `EyeLayoutParameters`／`DangoAppearanceConfiguration` 的字段、默认、上述坐标映射；记录 native 输入与默认帧／像素基线 | 默认请求与旧正常长相相等；超界／非有限值拒绝；四项单独变化；原物理与帽子基线不变。校准不能靠改旧预期过关 |
| F1 存储与会话 | 存储负责人独占 `AppearanceSettingsStore.cs`／`AppearanceSettingsSession.cs` 及两类直接测试；依赖 F0，可以和 F2 并行 | Schema 1／2 字节不变与显式迁移；Schema 3 精确往返／负例；整套取消／默认／成功／失败重试；损坏保护与备份。临时接受 Schema 4、遗漏高度／眼距、失败后错误更新 Saved 必须被测试击中 |
| F2 基础眼睛与安全 | 布局负责人独占 `FaceLayoutSolver.cs`、`AttachmentMotionController.cs`、`AttachmentSceneController.cs`、`AttachmentGeometryFactory.cs` 及直接布局／动态／渲染测试；依赖 F0 | 140% 眼睛可真实渲染；椭圆中点连续、横竖方向正确；最大圆眼＋最小眼距不交叉；高度×眼距可用位置覆盖；眨眼／睡醒还原请求。每个最终可见双眼结果联合检查轮廓与至少 2 DIP 净空，不能只检查各自包含或 Safety 字符串 |
| F3 首页真实闭环 | 集成负责人独占 `ProductHomeView.xaml`／新增 `.Appearance.cs`、`.LiveScene.cs`、`DangoApplicationHost.cs` 与必要 `ControlCenterWindow.xaml.cs`；依赖 F1/F2 和普通房稳定接线输入 | 系统调色盘取消不改草稿；四滑块即时预览不写盘；整套应用／取消／默认；失败反馈真实；主设置自动保存不带走外观草稿；滑块与调色盘焦点不触发移动／跳跃／W 进门 |
| F4 全宿主与生命周期 | 同一集成负责人独占 `StackedDangoSceneRuntime.cs`、`DangoHostFrameBinding.cs`、`App.xaml.cs`、必要 Home／Porch／本地房挂接；依赖 F3 及普通房已交付的原子提交 | 首页／门廊／桌宠 Window／Wallpaper／本地房呈现同一实体、同一已保存外观；关闭／隐藏／换装／离开首页取消草稿；旧回调不得复活。编辑、取消、默认事件不额外调用 Tick、推进物理、重建实体或新增帧 owner |
| F5 隔离 Windows 验收 | 新增 `eng/run-face-review.ps1` 与隔离启动参数、产品验收测试／记录；不启动日常实例，不替换受保护 DLL | 真正进程退出再启动读取六项；真实文件替换失败和解锁重试；100%／125%／150% DPI、三档首页布局与焦点；Windows 实时行为及用户人工观感单独记录 |

F2 的特殊风险：当前 [AttachmentGeometryFactory](../../src/Dango.Desktop/Rendering/Dango/AttachmentGeometryFactory.cs) 将两轴上限固定为 `.075`，140% 产生 `.105`，直接接入会抛错。先定义合法最大眼睛框架与轮廓误差测试，再同步调整渲染合同，继续拒绝越界。已有 `EyeArtwork` 两轴缩放用于组合动态表情；基础形状不通过长期 `SetEyes` 命令占用表情通道。

正常中性体的静态范围门禁必须双眼可见。UI 明确吸附到上述步长，先使用最大圆眼包络覆盖可达的 35 个高度 × 45 个眼距组合，再覆盖椭圆端点／中点和代表性中间值；较小尺寸的包络关系需验证。Store 接受非格点连续值，网格测试不是整个连续域的数学证明；连续输入另以最终联合安全／中性体双眼可见性质门禁与可论证的区间余量验证，报告分别列网格和连续采样边界。受压、剪切、倾斜和对向注视另验真实轮廓及最终联合净空。不能靠长期闭眼／隐藏让正常组合通过，也不能把安全退让后的尺寸保存为用户选择。

现有 [默认布局参考测试](../../tests/Dango.Desktop.Tests/Physics/Presentation/Attachments/FaceLayoutReferenceEquivalenceTests.cs)、[眼睛像素测试](../../tests/Dango.Desktop.Tests/Rendering/EyeRenderingTests.cs)、[原生眼睛验收测试](../../tests/Dango.Desktop.Tests/Rendering/EyeNativeReviewTests.cs)继续作为相应门禁。真实 native 物理轨迹与未编辑参考一致，帽子配置及独立 HatMotion 输出不变；身体／眼睛／帽子共用的步态安全显示退让按既有规则验证，不为了帽子像素坐标相等绕过眼睛净空。受影响步态／眼帽安全测试随 F2 回归。

## 5. 运行接线与集成顺序

Host 根据实际路径创建一个外观 Session；在首个有效显示帧前将已保存配置设置到本地主要实体。首页只绑定这个 Session 和同一个 runtime，不能以旧 `PreviewColor` 初始化再次覆盖加载结果。颜色刷与基础眼睛请求分开承载，但都来自同一整套配置；身体／眼睛颜色在所有宿主通过同一绑定规则设置，不能只给首页换画笔。保存成功与场景显示故障分开报告；显示异常不谎报写盘失败，也不擅自回滚已成功保存的文件。

Runtime 保存不可变的请求快照，附件在既有 Update／成功帧链读取。滑块只更新请求，不主动 Tick，不换 command channel，不重启物理。动态表情结束、附件 Reset／Synchronize 与宿主往返均恢复当前基础请求。外观只作用于本地主要实体，不能无差别给所有 frame body 覆盖同一配置。

F3/F4 不与普通房同时改共享文件。集成前先核对对方 HEAD、未提交差异和用户验收状态，取得可审查的原子交付后由单一负责人选择性整合；不合并脏树、整树复制或擅自合入 main。若普通房仍在改接线，F0～F2 可继续独立完成，不能把 F3/F4 的依赖写成已满足。两任务所有者在同一新候选中完成交叉回归，不向其他用户对话擅自发送消息。

## 6. 验证入口与交付要求

编码获准后，以 Release 构建实际桌面与测试项目，聚焦新增合同、Store／Session、布局与渲染、首页输入／宿主、房间往返及本地隔离验收测试。文件／视觉低风险调整不新增镜像测试；涉及保存失败、动态安全和帧所有权的测试必须实际可失败。修复或合同变更后重跑受影响门禁，通过后不机械重复全量测试。

隔离 native 输入沿用已核对的候选 `dango_physics_module.dll` 与现有 `DangoPhysicsModulePath` 构建机制；启动前核对候选和受保护 DLL 的 hash。旧正式 `dango_box2d.dll` 缺导出、历史 Physics 夹具等门禁仍独立记录，不以候选模块通过关闭旧失败。

F5 新启动模式必须纳入互斥 review 参数检查，使用专属 mutex、fresh artifacts 目录和隔离设置目录；原有路径保护扩展覆盖 `appearance.json`，拒绝目录／reparse 点及日常目录。测试临时文件、备份和 TRX 留在自有产物目录。真实替换失败使用虚构隔离文件：加载后持有目标文件锁，应用失败时验证原 hash／Saved／草稿／临时文件；解锁重试后创建新 Session 重载。单元文件重载与真正产品进程重启分别报告。

人工验收路线：打开捏脸 → 身体／眼睛选色（含黑色与同色）→ 四滑块组合 → 眨眼和睡眠／醒来 → 取消 → 默认 → 应用 → 首页／门廊／可用房间与宿主往返 → 隐藏／托盘恢复 → 退出隔离产品再启动 → 模拟写失败后重试。默认长相与旧 Windows 并排比对；不要将当前聊天草稿作为新的产品默认。

最终报告分别列静态门禁、native／WPF 集成、实时行为、人工验收、Git 与安装／推送状态。只有用户完成真实产品人工验收才能标对应增量已验收；设计通过和 165 项浏览器检查不替代它。本地提交可按已授权增量保存；main 合并、推送、日常安装、部署、发布标签、停止既有产品进程及数据恢复／删除均保持各自授权边界。结束时报告自有进程、端口、临时设置和备份残留。

原计划编制时只进行了只读源码／分支审计与文档修改，没有构建、测试、启动产品／服务或修改外观文件。后续实施结果记录在下节和对应验证页；计划中的门禁不能仅凭实施授权推定通过。


## 7. 实施增量记录

- 编码前核对普通房交付为干净的 `codex/local-room-implementation@88c0231`，由唯一集成负责人合入本外观候选，集成提交 `4eb06a7`。不编辑对方源工作树；普通房人工验收状态保持独立。
- F0 合同 `7aae488`，F1 严格 Schema 3 和整套草稿 `ab13f1b`，F2 参数求解和安全 `d5ff971`；F5 隔离入口／脚本 `a28bc8b`、路径别名保护 `ab85989`。原旧求解冻结参考未修改。
- F3/F4 接线提交 `78c1b93`，固定反馈和三项草稿操作 `7bfbc77`；通过同一 Host 会话和 Runtime 配置接通首页、门廊、Window、Wallpaper 与本地房；动态比较只临时占用相应表情通道，退出编辑即释放，基础参数不占用通道。
- 实际门禁、未验证项、人工路线与运行产物见[捏脸实现验证](../verification/2026-10-05-face-implementation.md)。后续入口、图标与阴影为 `c276799`，键帽与身体贴影为 `bd775bc`，三侧入口／镜像／纵向保存区为 `da6b8b2`；2026-10-06 用户确认当前候选观感，完整 F5 其它 DPI 门禁仍开放。

## 8. 后续已确认的呈现合同与门禁

当前完整合同见[首页与自身外观编辑设计](2026-10-06-home-and-appearance-design.md)，补充 F3／F5 的范围如下：

- W 门在团子上方、A 换装在左、D 捏脸在右；一致键帽、矢量图标、中文提示和辅助名称。编辑态隐藏入口，关闭或激活后焦点回舞台。
- 横向捏脸参数右、预览工具左；换装参数左、团子右。动画来源方向与入口对应，关闭期间保列、快速切换最后请求生效、减少动画即时到位。
- 同一窗口下三态预览尺寸和接地 Y 一致，身体实际轮廓贴影；保留真实跳跃顶空、身体／帽子与侧入口净空、阴影实际像素、往返实体和唯一帧 owner 门禁。
- 纵向工具只占上部行，保存组与团子同宽并靠其右下；反馈按实际测量留白，参数在下部独立滚动。按钮须完整位于窗口内，不以额外强制整页高度迁就布局。

最新受影响检查 104/104、零跳过，150% 实窗横向／纵向以及本轮人工观感已通过，见[最新验证](../verification/2026-10-05-home-icons-and-ground-shadow.md)。100%／125% 和其它显示器保持未验证；本次文字归档不重跑或新增运行证据。
