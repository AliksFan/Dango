# Phase 1 团子图像生成记录

> 状态：AP-003 确定性候选待人工确认
>
> 日期：2026-09-02

## 1. 生成边界

- 工具：Codex 内置 `imagegen`。
- 用途：占位视觉方向草稿，不是运行时资产或正式美术。
- 输入参考：无；不使用第三方角色或用户桌面内容。
- 第一轮上限：3 个方向，每个方向 1 张代表草稿。
- 选定方向后的修正上限：2 轮单点修正。
- 输出状态：`draft`；用户选择前不得生成完整动作包。

## 2. 共同提示约束

```text
Use case: stylized-concept
Asset type: Phase 1 desktop-pet visual direction draft
Primary request: design one original round Dango desktop companion character
Composition/framing: one complete character, centered, front three-quarter standard pose, generous transparent margin, no crop
Constraints: genuinely transparent background; small separate-looking soft oval shadow; body, face, blush, shadow, and one removable accessory must be visually separable; readable when reduced to a 160 by 120 logical canvas; no text; no watermark; no scenery
Avoid: existing IP traits, complex costume, photorealistic fur, busy texture, baked checkerboard, extra characters, props, dramatic effects
```

## 3. 方向提示词

### A：暖糯手绘

```text
Subject: a slightly flattened warm cream mochi-like creature, tiny calm dark-brown eyes and mouth, restrained pink blush, one small removable leaf accessory
Style/medium: soft hand-drawn 2D cel illustration with a fine warm dark-brown outline
Lighting/mood: gentle diffuse light, quiet and comforting
Color palette: warm cream, muted pink, leaf green, soft brown
Materials/textures: smooth rice-cake softness, nearly textureless
```

### B：清爽图形

```text
Subject: a simple pale-mint round creature, tiny calm face, subtle blush, one removable miniature rounded cap
Style/medium: clean flat graphic 2D illustration, rounded medium-weight dark outline, large simple color shapes
Lighting/mood: clear, calm, friendly, low visual noise
Color palette: pale mint, warm white, muted coral, charcoal
Materials/textures: flat and clean with only one restrained soft shading plane
```

### C：柔和水粉

```text
Subject: a soft cushion-like pale-lavender round creature, tiny gentle face, muted blush, one removable four-petal flower accessory
Style/medium: soft 2D gouache illustration with an extremely light pencil-like edge
Lighting/mood: tender, handmade, quiet, softly dimensional
Color palette: pale lavender, warm white, dusty rose, muted yellow
Materials/textures: restrained gouache grain only inside the body, clean transparent outer edge, no paper background
```

## 4. 尝试记录

### 2026-09-02：第一轮内置生成尝试

- 执行方式：按 A/B/C 三份独立提示词并行调用内置 `imagegen`。
- 工具结果：连接失败，调用未返回任何可用图像或保存路径。
- 资产状态：未生成；`drafts/`、`export/` 和 provenance 均未增加图片条目。
- 验证状态：没有图片，因此 Alpha、尺寸、小尺寸可读性和人工视觉选择均未执行。
- 使用量状态：未独立核实，不能根据“无输出”推断是否计入产品使用量。

按 skill 与实施计划停止，不自动切换到需要 `OPENAI_API_KEY` 的 CLI，也不连续盲目重试。下一次动作必须是用户选择重试内置生成，或明确授权 CLI/API fallback；CLI 路线还需要用户在本机设置 API Key，不能在聊天中提供密钥。

生成成功后再记录输出文件、实际保存路径、Alpha/尺寸检查和用户选择。未入选草稿不进入候选资产包。

## 5. 2026-09-02 用户补充要求与重试提示词

用户补充：首版计划支持简单捏脸，至少把身体颜色、眼睛形状和帽子作为三个独立可配置类别；视觉必须简约、纯色、无光影。

因此第 3 节的首轮 A/B/C 提示词作废，不再重试手绘、水粉、明暗或材质方向。重试使用以下共同约束：

```text
Use case: stylized-concept
Asset type: modular Phase 1 desktop-pet character customization concept sheet
Primary request: design one original minimalist round Dango character system; show one assembled main character and a few clearly separated interchangeable examples for solid body colors, eye shapes, and simple hats
Composition/framing: one main character centered, option parts arranged cleanly around it without text or labels, generous transparent margin, no crop
Style/medium: flat 2D graphic design, solid color fills only
Constraints: genuinely transparent background; body color, eyes, mouth, blush, hat, and optional contact shadow must remain visually separable; no baked combinations; readable at 160 by 120 logical size; consistent front-facing attachment points; no text; no watermark
Anatomy: one continuous smooth rounded body silhouette with one uninterrupted curved bottom edge; absolutely no legs, feet, paws, arms, limb-like nubs, or foot-shaped bottom protrusions
Avoid: all lighting and shading, highlights, gradients, glow, texture, 3D volume rendering, background, scene props, clothing, existing IP traits, extra assembled characters, any limb or foot-like shape
```

三个方向只比较轮廓语言：

- A：无描边柔圆；
- B：统一圆角细描边；
- C：统一圆角粗描边贴纸感。

接触影边界等待用户确认；重试暂用“可选的单一纯色低透明度椭圆，不带渐变且不表达光源”。捏脸 UI、数据保存和多人同步不在本分支实现。

### 重试过程中的硬约束修正

用户进一步确认团子不能有脚。主体底部必须是连续平滑圆弧，不允许脚、腿、爪、手臂、肢体状小凸起或两个底部凸起形成的站立暗示。

- 重试 A：出现明显脚状主体和大量光晕/渐变，判定失败，不进入仓库。
- 重试 B：透明背景有所改善，但主体仍有明显脚状凸起，且身体和眼睛包含渐变/高光，判定失败，不进入仓库。
- 前两张失败图只保留在生成器默认位置，本项目不引用、不登记为候选资产。

### 用户选择与配置范围修正

- 用户选择 A 的无描边柔圆风格。
- 眼睛视为表情的一部分，暂不进入捏脸预设。
- 首版预生产的可配置类别收敛为身体纯色和帽子；眼睛独立分层以支持表情动画，但不生成捏脸眼型选项。
- 下一次定向修正保留 A 的主体轮廓语言，移除选项区中的眼睛列表，并强制去除渐变、光晕和背景。

### A 定向修正第 1 轮

- 输出：`assets/preproduction/phase-1-dango/drafts/direction-a-selected-v2.png`。
- 成功项：主体底部为连续曲线，没有脚；眼睛选项区已移除；只保留身体颜色和帽子方向。
- 资产预检：文件可解析，画布为 1536 × 1024；没有 Alpha 通道，返回退出码 1。
- 明确失败项：棋盘格是烘焙背景而非透明预览；主体、色块和部分帽子仍含渐变或明暗，不满足纯色硬约束。
- 状态：`draft`，不是 `candidate`；等待用户对轮廓和排布的意见，并需要第 2 轮单点修正才能尝试纯色候选。

### A 定向修正第 2 轮

- 继续保留无描边柔圆、无脚主体、身体颜色和帽子选项；眼睛只保留为中央角色表情。
- 输出仍为白色不透明背景，身体、色块和帽子仍含渐变。
- 因同时违反真实 Alpha 与纯色两项硬约束，判定失败，不保存到仓库、不登记 provenance。
- 已达到实施计划规定的两轮单点修正停止点，不继续调用生成器。

### AP-003 当前结论

- 用户已选择 A 的无描边柔圆风格、无脚连续轮廓。
- 首版捏脸预生产只保留身体纯色和帽子，眼睛归入表情动画。
- 内置生成器适合提供轮廓方向参考，但两轮修正仍不能稳定满足纯色和真实 Alpha，不能直接生产候选资产。
- 用户已批准改用确定性 SVG 分层源文件表达同一 A 方向，再以可重复方式导出透明 PNG。

### 确定性 SVG 候选

- 源文件：`assets/preproduction/phase-1-dango/source/direction-a/`。
- 导出文件：`assets/preproduction/phase-1-dango/export/direction-a/`。
- 生产方式：项目原创几何 SVG，通过仓库内 `export-svg.ps1` 调用本机 Microsoft Edge headless 导出；不再调用图像生成器。
- 分层：`body`、`eyes-default`、`hat-sprout`；`preview-default` 是仅供审查的组合图。
- 画布：全部为 160 × 120；没有地面阴影或接触影。
- 视觉硬约束：SVG 中没有渐变、滤镜、光照、纹理或透明度效果；身体底部连续且无脚、腿、爪、手臂或肢体状凸起。
- 资产状态：`candidate`，尚未成为已验收美术或运行时资产。

### 确定性检查结果

- 用户人工预审后要求进一步极简化：删除嘴和腮红；相关 SVG、PNG、导出清单和来源记录均移除。
- `export-svg.ps1` 在本机 Microsoft Edge 完成 4 个文件的重复导出。
- 连续两次导出的 4 个文件 SHA-256 均保持一致，当前环境中得到逐字节一致结果。
- 导出预检检查 4 个 PNG，统一画布为 160 × 120，错误数为 0；每个文件同时具有真实透明像素和可见像素。
- 预检单元测试共 13 项通过，包括损坏 PNG、错误像素流、无 Alpha、RGBA 全不透明、RGBA 全透明、尺寸不一致、危险路径和来源缺失等失败样本。
- SVG 禁用效果扫描未发现 `linearGradient`、`radialGradient`、`filter`、`opacity` 或内联 `style`。
- 静态自审确认主体为无描边连续轮廓，没有脚或地面阴影；最终比例、脸部位置和叶芽帽气质仍需用户人工确认。
- 未验证：WPF 解析与播放、真实 Windows 桌面表现、动作重量和干扰程度。

## 6. AP-004 极简动作样本

- 用户于 2026-09-02 确认无嘴、无腮红的 A 方向候选，授权进入 `Idle`、`Landing`、`ClimbUp` 三类样本。
- 样本源、时序与挂点表位于 `assets/preproduction/phase-1-dango/motion-samples/`。
- 共导出 15 个运行时分层 PNG：12 个身体轮廓帧、睁眼/闭眼 2 个共享表情帧、叶芽帽 1 个共享配饰帧。
- 组合审查板位于 `motion-samples/review/phase-1-motion-samples.png`，仅用于静态视觉审查，不是运行时图层。
- `Idle` 证明底部稳定的轻微呼吸与独立眨眼；`Landing` 记录首次接触、压缩、回弹和恢复；`ClimbUp` 以连续身体轮廓的宽接触形变表达抓边和重量转移。
- 自审修正：首版 `Landing/fall` 离接地线过近且帽子顶部裁切，已在候选提交前上移轮廓并修正帽子挂点。
- 动作语义校验覆盖三类动作、严格递增时序、必需接触事件、图层白名单和文件引用；4 个测试通过，实际清单错误数为 0。
- 资产预检检查 15 个 PNG，统一画布为 160 × 120，错误数为 0；连续两次重新导出的 PNG 与审查板均逐字节一致。
- 当前状态：`candidate`；用户已于 2026-09-02 确认三类静态关键姿势方向，检查点 A4 通过，允许进入 AP-005。该确认不代表逐帧播放、WPF 或真实桌面体验通过。
- 未验证：逐帧播放、缓动、真实岛边遮挡、WPF 解析、真实桌面表现和最终体验。

## 7. AP-004 后续动画架构确认

- 用户确认运行时采用方案 2：手工关键姿势负责动作语义，轻量弹簧—阻尼软体形变负责接触挤压、衰减回弹和次级晃动。
- 身体、眼睛和帽子继续分层；眼睛随身体局部形变但保持独立表情轨道，帽子通过动态挂点跟随并允许很小的受限延迟。
- 双团子接触由单一 interaction director 驱动成对约束，避免双方独立求解产生抖动。
- 不引入完整物理引擎；视觉形变不裁定 Core 位置、碰撞、权限、状态或互动结果，性能不足时回退到手工关键姿势。
- 网络只同步动作意图、参与者、权威时间、接触方向或锚点、参数版本与种子，不同步逐帧弹簧、顶点或缩放状态。
- 本轮只完成文档、动作语言和 P1-004 Spike 门禁，未实现 WPF 求解器；详细决策见 [ADR 0004](../adr/0004-lightweight-soft-body-secondary-motion.md)。

## 8. Box2D 视觉软体方向批准

- 用户于 2026-09-03 将方案 2 从自有轻量弹簧—阻尼升级为真实二维物理内核，并选择“物理只负责视觉、Core/服务端继续权威”的边界。
- 已批准使用官方 Box2D 3.x C API 构造运动学核心锚点、动态节点环和弹簧关节；Box2D 输出只用于身体轮廓、眼睛/帽子挂点和本地诊断。
- 手工关键姿势继续负责动作语义、降低动画、资源缺失和异常 fallback；既有 A 极简视觉硬约束不变。
- ADR 0005 取代 ADR 0004 的“不引入 Box2D”选择；新的 [实施计划](../phase-1-box2d-soft-body-implementation-plan.md) 在用户批准前不授权依赖或代码接入。
