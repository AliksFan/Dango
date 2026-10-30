# 奶糖小伙伴配饰样例

三件原创暖色卡通配饰，供**独立、虚构的配饰测试库**试用。它们是可继续编辑的 native schema 2 工程，不是图片占位或产品内置目录。没有修改生产代码、菜单或默认库，也没有新增 NuGet/npm 依赖。

按用户最新要求，配饰仅用少量纯色色块：每件 2 色，无描边、高光、尾条纹或装饰线。猫耳为左右外耳与内耳 4 层，尾巴为主体与尾尖 2 层，蝴蝶结为双翼、中心结与双飘带 5 层；骨架与佩戴参数保持独立可编辑。

## 生成与产物

先有与当前源码匹配的 `Dango.Desktop` Debug 构建，然后在仓库根目录执行：

```powershell
dotnet run --project eng/accessory-editor/examples/warm-companions/WarmCompanions.csproj -- eng/accessory-editor/examples/warm-companions/generated
```

生成器只引用现有桌面程序集，不重建桌面、不启动 Application、窗口、定时器或输入钩子。其它构建位置可用 `-p:DesktopBuildPath=<matching-build-directory>` 指定。输出目录由第一个参数明确指定；重跑会覆盖该目录里的同名样例产物。

- `generated/collection-preview.png`：三件配饰完整外观。
- `generated/wearing-preview.png`：按实际佩戴参数与 `64 × SizePercent / 100` 尺寸规则画出的比例和遮挡示意。身体是原创椭圆示意，不是产品截图。
- 每个配饰目录的 `source.svg`：完整原创矢量源；各成员 `.svg`/`.png`：独立、完整透明画布图层。
- `project.json`：由现有 `AccessoryEditorProject` 原生构造、序列化的可编辑工程，含成员 PNG、骨架组、物理参数、支点与分组 `renderPieces`。`final.png` 为规范化成品 PNG，`item.json` 为匹配工程身份的佩戴配置。
- 每个目录的 `motion-preview.png`：现有 native solver/skin 的四个真实输出采样，时刻为 0、1.21、2.50、5.00 秒；离屏光栅化仅用于查看，不是应用渲染器或桌面实时行为的验收。
- `validation.json`：工程/图片身份、native roundtrip、五秒模拟结果，以及实际使用的桌面程序集 SHA-256。

所有图形路径在 `Program.cs` 中定义，用同一组路径同时导出 SVG 和经 WPF 离屏栅格化的 PNG。成员图层可在现有编辑器中独立移动、绘制、删改、重新分组；SVG 是再生成源，不是新增到编辑器中的矢量图层类型。

## 佩戴与骨架

| 配饰 | 工程 | 骨架与参数 | 初始佩戴 |
|---|---|---|---|
| 奶糖猫耳 | 512×224，4 层，2 绘制片 | 左右耳各 3 节点独立组；弯曲 .12，阻尼 12，恢复 18，重力 0，弹性 0；较挺的耳根带少量耳尖惯性 | 前层，160%，身体 (.50,.065)，图片 P=(256,208) |
| 奶糖卷尾 | 256×384，2 层，1 绘制片 | 9 节点沿完整尾巴中心线；弯曲 .24，阻尼 8，恢复 11，重力 3，弹性 .015；覆盖尾根到奶油色弯尖 | 后层，155%，身体 (.87,.68)，图片 P=(48,328) |
| 莓杏蝴蝶结 | 512×384，5 层，5 绘制片 | 固定单节点中心结；双翼各 3 节点，弯曲 .10/阻尼12/恢复18/重力1；双飘带各3节点，弯曲 .22/阻尼9/恢复10/重力8；双翼/飘带根都连接中心结 | 前层，100%，身体 (.50,.70)，图片 P=(256,154) |

参数使用现有 native 单位：弯曲为 162° 的比例，阻尼为每秒衰减率，恢复为角频率，重力控制 100 对应每秒平方 4 个图片高度。所有外层佩戴配置保持较强连接与锁定旋转，由各组骨架提供局部柔性。猫耳左右没有跨组依赖；蝴蝶结所有附着节点位置与中心节点完全一致。

## 追加到隔离测试库

**本样例生成阶段没有写任何现有测试库。** 主任务在确认精确测试 root、关闭该测试应用并备份原清单后，可执行以下追加。此步骤是持久化写入，不能针对真实用户默认目录使用。

在该虚构隔离 root 创建 `dango-fictional-test-root.marker`，内容恰为：

```text
fictional-accessory-test-data-only
```

再执行：

```powershell
dotnet run --no-build --project eng/accessory-editor/examples/warm-companions/WarmCompanions.csproj -- eng/accessory-editor/examples/warm-companions/generated --append-test-library <fictional-settings-root>
```

追加直接调用现有 `AccessorySettingsStore.Load/Save/LoadProject/LoadImage`；`Save` 接收原条目追加新条目的 `AccessoryConfiguration`、按资产身份索引的 pending assets/projects 和原清单 revision。不会手工拼写存储目录或绕过原生校验。它保留原条目原值，对已存在的样例 ID 不覆盖；超过 8 个佩戴名额的新增条目先进入未佩戴区；保护/旧版清单拒绝写入。保存后重新载入并校验原条目保留和新增工程/PNG 匹配。应用若仍在运行，原会话的 revision 保护会阻止覆盖，应重开此测试应用加载新库。

## 已覆盖与未覆盖

生成时每件工程通过 `Read(bytes, expectedId)` 与 `ValidateResult`；所有层按组连续排列且对应绘制片；每件以 120 Hz 推进 600 帧，前三秒正弦平移/旋转、后两秒停止，检查非有限坐标、骨长超限、相连根位置以及重复 tick 拒绝，并生成真实变形采样图。`Refresh` 不会推进物理。

离屏视觉核对覆盖完整图形、颜色、图层遮挡和示意佩戴比例。尚未完成前台编辑器重新打开、真实团子佩戴及用户人工观感验收；静态图和五秒模拟不代替这些项目。生成器追加分支在主任务指定的隔离库上执行前，不应宣称已导入成功。

桌面视觉依据 `docs/desktop-ui-design-v0.1.md` v0.1 第 2 节暖白/棕色与内容为主；没有新增 UI 页面，所以按钮、滚动条等页面规则不适用。原创图形无外部素材或字体嵌入；预览文字使用系统字体。
