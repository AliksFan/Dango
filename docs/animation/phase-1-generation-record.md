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
