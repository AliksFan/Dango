# 资产来源与再分发状态

本清单按仓库当前跟踪的资源目录及其来源 manifest 汇总，用于 GitHub 开源准备。它记录来源证据和作者确认范围，不把仓库整体 MIT 许可证扩张到第三方组件。作者于 2026-10-30 确认：“是，相关素材和参考输入均有权使用及再分发”，覆盖 Direction A 轮廓、动作图和 AI 方向草稿参考输入。本确认作为作者提供的权利信息记录；并非独立法律审查。

## 资产目录

| 路径 | 内容与来源证据 | 当前处理 |
| --- | --- | --- |
| `assets/placeholder/` | 虚构占位文本、Phase 1 JSON 样例和 schema；共 6 个跟踪文件。`manifest.json`、`README.md` 记载为仓库内创建；manifest 对列出的文件标 MIT | 项目原创占位内容，按仓库 MIT 分发；不含正式美术 |
| `assets/preproduction/phase-1-dango/source/direction-a/` | Direction A body、eyes、sprout hat SVG、预览 SVG 和导出脚本。`provenance.json` 逐项记为确定性项目创建，并注明基于用户选定的 Direction A 轮廓 | provenance 支持项目原创制作陈述；作者确认相关素材和参考输入可使用、再分发。按本轮仓库 MIT 选择整理 |
| `assets/preproduction/phase-1-dango/export/direction-a/` | 上述 SVG 的 PNG 导出及预览合成；`export/provenance.json` 将导出逐项对应到源 SVG，记为 Edge 确定性导出 | 派生自原创 SVG；按 MIT 分发 |
| `assets/preproduction/phase-1-dango/motion-samples/source/` | 逐帧姿态 SVG、共享眼睛和帽子图层；`motion-samples/provenance.json` 逐项记为基于已批准 Direction A 的项目原创确定性矢量姿态 | 作者确认范围涵盖动作图及参考输入；按 MIT 分发 |
| `assets/preproduction/phase-1-dango/motion-samples/export/` | 动画姿态 PNG、候选清单和导出来源记录；manifest 指向源 SVG，声明项目原创 | 原创 SVG 的派生导出，按 MIT 分发；导出不扩大源资产权利范围 |
| `assets/preproduction/phase-1-dango/motion-samples/review/` | HTML、PNG review contact sheet 和辅助脚本；provenance 记为由项目拥有的分层导出生成，标注 review-only | 项目原创评审材料可按 MIT 分发；若文件后来加入第三方截图，应另行检查，该情况不在本次作者确认范围内 |
| `docs/planning/assets/` | 4 张参考 UI 图片：`compact-slider-reference.png`、`image-tools-reference.png`、`stacking-buttons-reference.png`（位于 `2026-10-26-accessory-editor/`）、`canvas-layout-reference.png`（位于 sibling `2026-10-27-accessory-editor/`）。`docs/planning/2026-10-26-accessory-editor-ui-contract.md` 第 12 节说明这些图作为布局/意象参考；其中最后一张明确记为 2026-10-27 用户提供，前三张未见逐文件来源 manifest 或授权说明 | 前三张来源及再分发许可在仓库材料中未查明；最后一张有用户提供的文档记录，但未见独立许可/授权记录。它们是设计参考，不是产品运行时资源；作者本次素材确认未提及这组图片。原始工作树保留原图，公开快照导出脚本固定排除 |
| `eng/accessory-editor/examples/warm-companions/generated/` | 19 张跟踪 PNG 和同目录 SVG 源，组成 3 个可编辑配饰样例及集合/佩戴/动作预览。`README.md` 称三件为原创暖色卡通配饰；`Program.cs` 标注 Original vector artwork，直接定义图形路径，并从同一组路径输出 SVG/PNG。README 说明穿戴预览的身体是原创椭圆示意，不是产品截图 | 生成器和 README 支持原创来源；属于开发示例/测试内容，不是产品默认资产。按本轮仓库 MIT 分发；其 `.png` 是源 SVG 的离屏 raster 导出。媒体共 33 个（14 SVG、19 PNG）；该 generated 目录在基线中共 40 个文件，另有 7 JSON。可从 README 中的命令重建。 |
| `assets/preproduction/phase-1-dango/drafts/direction-a-selected-v2.png` | 根 `provenance.json` 记为 Codex 内置 imagegen 输出，方向来自用户选定的无描边圆形轮廓；prompt 见 `docs/animation/phase-1-generation-record.md` | 作者确认 AI 生成参考输入可使用及再分发。作为本仓库资产按 MIT 发布；该确认不代表 AI 输出具有独占版权或保证可取得排他权利 |
| `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll` | Windows x64 原生 Box2D bridge DLL；`eng/native/box2d/dependency.json` 固定官方 v3.1.1 commit、源码归档与 DLL 哈希、MIT 路径 | 按 Box2D MIT 条款分发 DLL，并保留 `eng/native/box2d/LICENSE.box2d.txt` 及依赖清单；桌面项目已有复制规则 |
| `src/Dango.Desktop/Accessories/Editing/Web/` | 编辑器 HTML/CSS/JS 子集；`manifest.json` 固定 miniPaint 源 commit 与文件哈希，NOTICE 说明适配与排除范围 | 第三方许可独立适用；分发时保留 NOTICE、`licenses/MIT-minipaint.txt`、`licenses/MIT-jquery.txt` 和 manifest |

## 参考材料与研究内容

`docs/research/` 的 Markdown 文件是技术选型和链接引用，不是随项目重新分发第三方 SDK、游戏图像或字体的授权。`docs/verification/`、`docs/planning/assets/` 4 张图片是本次已核实但未清权的参考图；本次完整 tracked 媒体清点未发现其它栅格图像目录。其它历史文档中若以非栅格或内嵌方式包含截图/外部示例，仍须按具体文件检查；作者本次确认仅覆盖 Direction A 轮廓、动作图及 AI 方向草稿参考输入。原始工作树中保留这些内部参考文件；公开源码通过快照导出脚本排除 4 张参考图，并仅改写导出副本中的图片引用。tracked `artifacts/` 目录的 70 个开发诊断文件也由公开快照排除，原工作树文件保留。

以下计数固定对应 `main` 基线 commit `9fbb828` 的 `git ls-tree -r` 跟踪文件，不代表后续源码快照或工作树总数：raster 图片 72 张（`assets/` 49、`docs/planning/assets/` 4、`eng/accessory-editor/examples/warm-companions/generated/` 19）；SVG 60 个；媒体合计 132 个。`assets/` 共 119 个文件（placeholder 6、preproduction 113）；Box2D DLL 位于 `src/Dango.Desktop/runtimes/win-x64/native/`，不计入 `assets/`。`docs/planning/assets/` 4 张；generated 示例目录共 40 个跟踪文件（19 PNG、14 SVG、7 JSON），示例目录根部另有 README、项目和源文件，总计 43 个。该基线下另有 tracked `artifacts/` 70 个开发诊断文件（42 个日志）；它们保留在内部工作树，不进入公开源码快照。基线跟踪文件中未发现 `.ttf`、`.otf` 或 `.zip`。除 Box2D DLL 外没有发现其他非图像压缩二进制。

## 未覆盖事项

- `docs/planning/assets/` 的 4 张参考图片确已逐路径核对：`compact-slider-reference.png`、`image-tools-reference.png`、`stacking-buttons-reference.png` 位于 `2026-10-26-accessory-editor/`，`canvas-layout-reference.png` 位于 sibling `2026-10-27-accessory-editor/`。前三张未找到具体来源/许可，末张仅有用户提供记录且未见许可信息。快照导出脚本已固定排除这 4 张图，转换副本中的对应引用；原始工作树文件保留。
- AI 生成图按项目选择的 MIT 许可证提供，但不对其独占性、可版权性或排他权利作保证。
