# Phase 1 团子图像生成记录

> 状态：AP-003 进行中
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
- 首版预生产的可配置类别收敛为身体纯色和帽子；眼睛、嘴和腮红继续独立分层以支持表情动画，但不生成捏脸眼型选项。
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
- 建议下一步改用确定性 SVG 分层源文件表达同一 A 方向，再以可重复方式导出透明 PNG；这是生产方式调整，执行前等待用户确认。
