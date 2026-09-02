# Dango Phase 1 动画预生产实施计划

> 状态：Approved
>
> 日期：2026-09-02
>
> 批准日期：2026-09-02
>
> 前置设计：[Phase 1 动画预生产设计](phase-1-animation-preproduction-design.md)
>
> 对应阶段：P1-004 的非宿主前置工作；不改变 P1-003 → P1-004 顺序

## 1. 目标与授权边界

本计划把已批准的动画预生产设计拆成可审查、可停止的任务，在不修改桌面宿主和 WPF 渲染适配器的前提下，建立项目专用动画生产 skill、动作语言、资产检查入口和第一套可交给 P1-004 使用的占位动画候选。

本计划批准后允许在 `feat/p1-004-animation-preproduction` 分支内：

- 创建仓库内可审查的 `dango-animation-pipeline` skill 源码；
- 使用内置 `imagegen` 生成明确标记为草稿的虚构团子视觉候选和关键姿势参考；
- 创建、整理和验证项目自有的透明位图候选；
- 为现有资产 Schema 制作独立候选包和来源记录；
- 运行不会读取桌面内容或写入业务数据的本地验证；
- 为每个通过门禁的增量创建本地提交。

本计划不授权：

- 修改 `Dango.Desktop/DesktopHost`、WPF Rendering Adapter、托盘或控制中心；
- 绕过 P1-003/C3 门禁进入完整 P1-004 集成；
- 把草稿、概念图、Schema 通过或静态预览称为动画完成；
- 购买或安装 Clip Studio Paint、Runway、Higgsfield、Spine、Live2D、Rive 或其他付费工具；
- 使用第三方角色、美术包、受限字体或来源不明的参考资产；
- 合并到阶段分支或 `main`、推送、部署、发布 skill 或删除 Worktree；
- 制作正式团子美术、多人动作、贴贴、群体或情侣动作。

## 2. 分支、Worktree 与文件所有权

- 分支：`feat/p1-004-animation-preproduction`。
- 使用独立 Worktree；实际本机路径只在实施开始时只读核对，不写入仓库文件。
- 基线：`d6df4ca`；实施开始前重新核对实际 HEAD 和工作区。
- 主阶段 Worktree 保留给 P1-003；动画预生产不得修改其未提交文件。

动画分支拥有以下路径：

```text
tools/codex-skills/dango-animation-pipeline/
docs/animation/
assets/preproduction/phase-1-dango/
docs/verification/phase-1-animation-preproduction.md
docs/phase-1-animation-preproduction-*.md
```

只有 AP-005 获得批准并执行候选资产包对接时，才允许修改：

```text
assets/placeholder/
tests/Dango.Core.Tests/Assets/
```

本分支不修改 `README.md`、`docs/development-plan-v0.1.md` 或 `docs/phase-1-implementation-plan.md`，避免与正在推进的 P1-003 发生基线文档冲突。需要同步这些文件时，先完成 P1-003 集成，再基于最新阶段分支单独处理。

## 3. 计划产物结构

```text
tools/codex-skills/dango-animation-pipeline/
  SKILL.md
  agents/openai.yaml
  references/
    asset-contract.md
    motion-language.md
    review-checklist.md
  scripts/
    validate_export.py       # 仅在 AP-002 证明标准库实现充分时保留

docs/animation/
  phase-1-motion-spec.md
  phase-1-visual-baseline.md
  phase-1-generation-record.md

assets/preproduction/phase-1-dango/
  drafts/                    # 未进入运行时的候选；只保留入围草稿
  source/                    # 可编辑源文件或明确的生成记录
  export/                    # 人工检查后的透明 PNG 候选
  candidate.asset.json       # 与当前资产契约兼容但不默认加载
  provenance.json            # 来源、生成方式、人工修改和许可证

docs/verification/
  phase-1-animation-preproduction.md
```

不提交被淘汰的大批生成结果、工具缓存、临时拆帧、个人绝对路径或美术软件自动备份。无法用开放格式保存的专有源文件必须同时保留可审查导出物和来源说明。

## 4. 公共验证门禁

文档或 skill 任务至少执行：

```powershell
git diff --check
python (Join-Path $env:CODEX_HOME 'skills\.system\skill-creator\scripts\quick_validate.py') `
  .\tools\codex-skills\dango-animation-pipeline
```

`CODEX_HOME` 和 Python 必须通过当前环境实际解析，不得把解析后的本机路径写进项目脚本、skill 或资产记录。若校验器位置或调用方式变化，实施时读取当时的 `skill-creator` 指令并记录实际命令。

修改候选资产包或 Core 资产测试时还需执行：

```powershell
dotnet test .\tests\Dango.Core.Tests\Dango.Core.Tests.csproj -c Release
dotnet format --verify-no-changes
dotnet build -c Release
git diff --check
```

图片门禁至少检查：

- 文件可解码且具有 Alpha 通道；
- 同组导出尺寸、画布、原点和命名一致；
- 清单只引用包内相对路径且不存在遗漏文件；
- 透明边缘无明显白边、黑边和大面积脏像素；
- 配饰与关键接触挂点没有可见漂移；
- 来源和许可证记录覆盖每个候选文件。

自动门禁不能判断重量感、柔软感、治愈感或干扰程度；这些只记录为静态人工预审，最终仍待 P1-004 真实桌面人工验收。

## 5. 任务分解

### AP-000 — 重新核对基线并冻结预生产接口

**目标**

在创建 skill 或图像前核对最新仓库事实，确认动画分支与 P1-003 没有文件重叠，并把当前 Schema、动作枚举和占位包映射成预生产输入基线。

**只读检查**

- 当前分支、HEAD、工作区、Worktree 和阶段分支进度；
- `AGENTS.md`、ADR 0003、动画预生产设计和本计划；
- `AssetPackage`、`AssetPackageParser`、`DangoActionState`；
- `assets/placeholder/phase-1.asset.json` 与对应 Schema；
- `skill-creator` 和 `imagegen` 当时的完整指令。

**产物**

- `docs/animation/phase-1-motion-spec.md` 的接口基线部分；
- 文件所有权和实际版本记录；
- 计划与最新阶段分支存在差异时的阻塞或重放说明。

**失败验收与停止点**

- 阶段分支已经修改动作、资产 Schema 或目标路径时，不照旧实现；先更新设计或计划。
- 动画 Worktree 含来源不明修改或与 P1-003 路径重叠时，停止并向用户报告。
- 不在本任务生成图像、安装工具或修改运行时代码。

### AP-001 — 建立仓库内项目专用 skill

**目标**

创建可由 Git 审查的 `dango-animation-pipeline` skill 源码，把项目非显然约束集中为动作设计、草稿生成、导出整理和资产验收四种模式。

**目标文件**

- `tools/codex-skills/dango-animation-pipeline/SKILL.md`；
- `agents/openai.yaml`；
- `references/asset-contract.md`；
- `references/motion-language.md`；
- `references/review-checklist.md`。

**实现约束**

- 使用 `skill-creator` 初始化并保持自动发现策略；
- `SKILL.md` 只保留路由、授权边界、草稿/候选/已验收状态和最小工作流；
- Schema 细节、动作语言和检查表分别放入按需 references；
- skill 不复制完整项目文档，不把工具建议写成已购买或已安装事实；
- skill 源码留在仓库内；是否复制或链接到用户全局 skill 目录属于单独安装动作，不由本任务自动执行。

**先定义的失败验收**

- 对普通图片生成请求不应错误宣称必须使用 Dango 资产契约；
- 对视频参考不得输出“可直接进入 WPF”的结论；
- 没有用户授权时不得购买服务、安装软件、覆盖候选或修改运行时；
- 缺少仓库当前资产契约时必须停止资产包生成，而不是猜测字段。

**验证**

- `quick_validate.py` 通过；
- frontmatter 名称、描述和目录一致；
- 所有 references 从 `SKILL.md` 可发现；
- 不存在脚手架占位文本、无用目录或个人绝对路径。

### AP-002 — 建立导出检查与来源记录

**目标**

先定义能够失败的资产预检，再生成视觉素材。检查应捕获错误 PNG、缺少 Alpha、尺寸不一致、路径越界、未登记来源和清单遗漏。

**目标文件**

- `tools/codex-skills/dango-animation-pipeline/scripts/validate_export.py`；
- 对应的隔离测试样本和脚本测试；
- `assets/preproduction/phase-1-dango/provenance.json` 的最小 Schema 或说明。

**实现规则**

- 优先 Python 标准库；不为读取图片预防性加入 Pillow 或其他依赖。
- 标准库无法可靠验证所需属性时，先记录缺口，再选择复用仓库 .NET 检查或提出依赖评估，不写永远通过的检查。
- 脚本只读取明确资产目录并输出结构化结果，不修改图片、不读取桌面或扫描用户目录。
- 测试使用脚本自己创建的微小虚构样本，保存在隔离临时目录并在测试结束后清理。

**先定义的失败验收**

- 非 PNG、损坏 PNG、RGB 无 Alpha、不同画布尺寸、绝对/越界路径和缺少 provenance 均返回非零退出码。
- 正常候选目录返回零，并列出实际检查文件数；零文件不得通过。

**停止点**

- 若完整像素级透明边缘检查需要引入较重依赖，只保留确定可靠的结构检查，把视觉边缘检查交给人工预审；不得夸大自动验证范围。

### AP-003 — 视觉基准与动作语言候选

**目标**

使用内置 `imagegen` 生成少量虚构团子视觉候选，选出一套用于占位动画的标准视图，并明确重量、呼吸、柔软、安静和降低动画的动作语言。

**输入与输出**

- 生成前先写 `docs/animation/phase-1-visual-baseline.md` 的约束、避免项和选择标准；
- 第一轮最多生成 3 个造型方向，每个方向只保留 1 个代表草稿；
- 用户选定方向后最多进行 2 轮单点修正；超出时停止并重新确认视觉问题，不无限消耗生成额度；
- 入围草稿保存到 `assets/preproduction/phase-1-dango/drafts/`；
- 完整提示词、生成工具、日期、输入参考和选择结果写入 `phase-1-generation-record.md` 与 provenance；
- 不使用第三方角色图片作为输入参考。

**最低视觉约束**

- 软萌、圆润、治愈，轮廓在桌面小尺寸下清晰；
- 正面或轻微侧向的稳定标准姿势，透明背景；
- 身体、脸、腮红、阴影和简单配饰具有可拆分边界；
- 不含文字、水印、复杂背景、拟真毛发、细碎高频纹理或现有 IP 特征；
- 不因整体正弦缩放制造全部生命感。

**验收与停止点**

- 自动检查只验证文件和记录，不判定视觉通过。
- 用户必须选择或拒绝视觉方向；未选择前不得扩展完整动作包。
- 三个方向均不合格时记录失败原因并回到视觉约束，不直接增加更多随机生成。

### AP-004 — 三类动作样本证明

**目标**

先用三个代表样本证明管线能够分别处理低成本连续动作、短轮廓形变和空间接触动作，再决定是否扩展全部动作。

**样本**

1. `Idle`：身体/脸分层、呼吸 Transform 和眨眼附加帧。
2. `Landing`：接触、压扁、回弹和恢复，包含 `landed` 事件与 `ground-contact` 锚点。
3. `ClimbUp`：抓边、提身、翻越和站稳，包含 `island-grabbed` 事件与 `island-grab` 锚点。

**产物**

- 每个样本的动作意图、关键姿势、时序、循环、可中断点、事件和降低动画版本；
- 分层候选 PNG、逐帧挂点表和静态接触检查图；
- 与 `phase-1.asset.json` 的 clip 映射草案；
- 草稿、人工修整和候选状态记录。

**失败验收**

- 任一帧画布、原点或命名不一致时导出检查失败。
- 配饰挂点相对身体明显跳变、落地点漂移或抓边接触不连续时人工预审失败。
- `Idle` 只靠整体等比缩放、`Landing` 没有重心恢复、`ClimbUp` 没有明确接触阶段时不得通过。
- 降低动画版本不得改变主状态、事件时间语义或最终落点。

**检查点 A：是否扩展**

三个样本的结构门禁通过且用户认可静态动作设计后，才进入 AP-005。静态关键姿势认可不是 WPF 播放、真实桌面或最终人工体验通过。

### AP-005 — 扩展首批动作并建立候选资产包

**目标**

复用已验证流程补齐 P1-004 所需动作，并生成一个不会被应用默认加载的独立候选资产包。

**动作范围**

- 补齐 `Wander`、`ApproachIsland`、`OnIsland`、`ClimbDown`、`Dragged`、`Falling` 和 `Reacting`；
- `Hidden` 只验证无可见帧、无持续轨道，不生成图像；
- `Idle`、`Landing`、`ClimbUp` 使用 AP-004 已通过样本；
- 不加入睡眠、醒来、贴贴、群体、情侣或正式多人动作。

**目标文件**

- `docs/animation/phase-1-motion-spec.md` 完整动作表；
- `assets/preproduction/phase-1-dango/export/`；
- `candidate.asset.json`；
- `provenance.json`；
- 必要的 skill reference 更新。

**验证**

- AP-002 导出检查通过且文件数非零；
- 候选 JSON 使用现有 Core 解析器成功解析；
- 所有 `DangoActionState` 映射有 clip 或明确的 `Hidden` 运行时处理；
- clip、layer、slot、anchor、event 和 fallback 跨引用完整；
- 相同角色帧画布一致，所有来源记录齐全。

**停止点**

- 当前 Schema 无法表达逐帧挂点、脸部附加轨道或需要的遮挡变化时，不把数据塞进无关字段；记录真实缺口并提交 P1-004 资产契约变更建议。
- 资产量或人工修整量证明分层位图不可维持时，停止扩展并按 ADR 0003 提出重评估，不引入骨骼工具。

### AP-006 — 预生产验收与 P1-004 移交

**目标**

汇总静态门禁、候选资产、已知限制和人工预审，形成可由最新阶段分支重放的 P1-004 输入包。

**目标文件**

- `docs/verification/phase-1-animation-preproduction.md`；
- 候选资产与来源清单最终检查；
- P1-004 接入说明和未解决 Schema/TODO 列表。

**验证分层**

- **静态门禁**：skill 校验、脚本测试、Core 资产解析、格式、构建和测试。
- **资产结构**：PNG/Alpha/尺寸/引用/来源/挂点表完整性。
- **静态人工预审**：用户对造型方向和关键姿势的确认。
- **实时行为**：未验证；等待 P1-004 WPF 播放与真实桌面。
- **人工体验**：重量、柔软、治愈、干扰程度未完成；等待真实桌面验收。
- **交付状态**：单独报告本地提交、合并、推送、安装和外部资源状态。

**集成前提**

- P1-003 已完成且 C3 至少一个宿主候选可行；
- 最新 `phase/1-desktop-life-prototype` 的动作和资产契约与候选包兼容；
- 用户批准预生产结果进入 P1-004；
- 集成在基于最新阶段分支的新提交中完成，不直接合并旧基线上的宿主代码。

## 6. 提交策略

建议本地提交边界：

1. `docs: approve animation preproduction design and plan`；
2. `feat(skill): add Dango animation pipeline skill`；
3. `test(assets): add animation export preflight`；
4. `docs(art): define phase 1 visual and motion baseline`；
5. `assets: add representative animation samples`；
6. `assets: complete phase 1 animation candidate pack`；
7. `docs: record animation preproduction result`。

实际提交按真实产物调整，不为了匹配列表创建空提交。图像、来源记录、对应清单和检查结果必须在同一可审查增量内。未经用户另行授权，不合并、推送或安装全局 skill。

## 7. 检查点

| 检查点 | 进入条件 | 允许继续的证据 |
|---|---|---|
| A0：接口锁定 | AP-000 完成 | 最新 Schema/动作/分支边界已核对，无文件冲突 |
| A1：skill 可用 | AP-001 完成 | `quick_validate.py` 通过，路由与权限边界自审通过 |
| A2：资产预检可失败 | AP-002 完成 | 错误样本确实失败，正确非空样本通过 |
| A3：视觉方向 | AP-003 完成 | 用户选择一套占位视觉方向，来源记录完整 |
| A4：动作管线证明 | AP-004 完成 | 三类样本结构通过且用户认可静态动作设计 |
| A5：候选包完整 | AP-005 完成 | 全部首批动作可解析，图层/挂点/来源完整 |
| A6：允许移交 | AP-006 完成且 P1-003/C3 通过 | 用户批准进入 P1-004，实时与人工体验仍明确未验证 |

未通过检查点时，只修复当前检查点或更新设计；不得用增加生成次数、伪造占位数据或提前写 WPF 代码掩盖失败。

## 8. 费用与外部资源停止规则

- 内置 `imagegen` 是本计划唯一默认生成能力；不要求 API Key，也不自动改用 CLI/API 付费路径。
- 第一轮最多 3 个造型方向；选定后最多 2 轮单点修正。
- 需要 CLI、外部模型、付费插件、软件订阅或第三方素材时停止，列明用途、预计成本、许可证、数据上传范围和退出成本，再请求用户授权。
- 不把“用户可以接受花费”解释为对任意购买、订阅、上传或长期绑定的预授权。

## 9. 本计划批准后的动作

用户批准本计划后，从 AP-000 开始顺序实施。AP-001/AP-002 可以在文件所有权不重叠时连续完成，但 AP-003 的图像生成必须等待预检和视觉约束就绪；AP-004 必须等待用户选择视觉方向；AP-005 必须等待三类样本检查点通过；AP-006 只形成移交包，不提前进入 WPF 集成。
