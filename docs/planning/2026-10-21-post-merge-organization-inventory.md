# 合并后目录与历史入口：只读盘点

日期：2026-10-21。状态：**已完成静态盘点；建议待批准，未实施**。配套[设计与分批计划](2026-10-21-post-merge-organization-plan.md)。

后续实施说明：本盘点保留为 `main@5ae2292` 的来源快照。B1已退役／迁移的文件改以代码路径记载，可用 `git show 5ae2292:<路径>` 查回原文；不为维持历史链接而留下生产空壳。

## 1. 快照、方法与限制

- 源快照：本地 `main@5ae22926d45d2e35f71f7791e509ec1db9ff7beb`，已实时核对 HEAD、分支、index、status 和 worktree 列表。main 唯一既有修改是未暂存 `AGENTS.md`，无暂存项；原交付 `codex/belt-page-completion@c55245f` 工作区干净，未清理其 ignored 包、负控或冻结证据。
- main 的 `AGENTS.md` SHA256 为 `7F2DD53E64CB4E3B32445CBD798D5ABD01AB4B283EB096F5A854674B6802A029`；受保护 `dango_box2d.dll` 为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。不复制用户的 AGENTS 修改到本分支，也不暂存或提交它。
- 最小基线：[工作流](../development-workflow-v0.1.md)第3～8、10～12节、[阶段索引](../development-plan-v0.1.md)顶部、[收口计划](2026-10-20-production-closeout.md)的问题边界、[最终核验](../verification/2026-10-21-final-closeout.md)。历史审计只作检索线索，以下消费者重新从当前源码核实。
- 方法：`rg` 定义／调用检索，配合 `git grep -n -I` 覆盖 tracked 的 C#、XAML、项目、脚本、JSON和Markdown；核对项目默认编译与显式引用。排除构建输出，不遍历全部物理历史，不依据文件名或是否进入 solution 判死代码。
- “仅测试调用”指当前仓库可检索引用；不证明外部程序集或动态拼接反射绝对不可达。本轮未构建、运行测试、启动产品或服务，也未重新验证历史物理及人工验收。
- 本轮文档在从上述本地 main 创建的独立 worktree、`codex/post-merge-organization-plan` 分支保存；生产源码、规则及存档未改。

## 2. 正式启动、构建与发布（事实）

| 入口／边界 | 当前接线与证据 | 整理建议 |
| --- | --- | --- |
| 普通启动 | [App](../../src/Dango.Desktop/App.xaml.cs)第20～28、94～117行创建产品宿主；[宿主](../../src/Dango.Desktop/AppShell/DangoApplicationHost.cs)第42～65行，默认 `usesContinuousBody=true`，选择 `CreateBeltRoom` | 作为产品验收唯一行为基线，保持 |
| `--data-root <绝对目录>` | App第149～158行只解析设置位置，第237～245行按设置路径派生实例身份；普通产品物理不变。它是通用产品选项，并不要求虚构会话owner标记 | 保持；自动验收另保留有owner校验的数据入口，不能把通用路径选项当作已有数据保护 |
| `--local-install-acceptance-settings <目录>` | App第120～126、178～225行要求owned标记、目录及文件约束；[安装管理模块](../../eng/local-install/Dango.LocalInstall.psm1)第498行使用 | 保留合同；当前实例名解析不按该目录派生身份，不能直接假设与日常实例并行隔离。迁移或替换review脚本前，需补实例隔离门禁 |
| `--smoke-test` | App第75～79行在host／托盘／实例协调器创建前退出；现有[最终核验](../verification/2026-10-21-final-closeout.md)仅证明基础托管启动 | 保持，不能用其通过证明native、页面或输入 |
| `--box2d-native-probe` | App第53～56、267～282行探测受保护旧Box2D；[native说明](../../eng/native/box2d/README.md)仍给出命令 | 保留为旧模块诊断，不算新身体产品验收 |
| 普通构建 | [Desktop项目](../../src/Dango.Desktop/Dango.Desktop.csproj)引用Core／Physics／Protocol并导入[targets](../../eng/Dango.BeltNative.targets)；targets第19～25行在构建／复制／发布前接源码构建及显式覆盖检查 | 保持，不再手工复制ignored模块作为正常构建前置 |
| native产物 | [构建脚本](../../eng/build-belt-native.ps1)第20～27行以 `DANGO_BELT_ONLY=ON` 构建两个target，复制并校验manifest | 保持两个模块和源码身份，不移动native目录 |
| 正式内部包 | [发布脚本](../../eng/publish-desktop-entry.ps1)第44～47、60～61、79、112～122行生成源码绑定、设置合同与包清单；输出 `artifacts/desktop-entry/<RunName>/package/` | 保持；当前是 `internal-candidate`，发布脚本不等于安装或部署 |
| 兼容性门禁 | [安装管理模块](../../eng/local-install/Dango.LocalInstall.psm1)第9～32行包含App／宿主的历史与当前源码哈希；第111～128行校验native源路径与产物身份 | 改App／host也影响发布：须新增经审查的合同，保留旧合同用于旧包校验／回退，不能只覆盖旧哈希 |

## 3. 当前review／preview入口逐项清单

App第163～166行当前列出14个无值运行选项：**10个review＋2个preview＝12个历史审查入口**，另2个为上述probe／smoke。四个带值选项不计入模式。收口旧文档的“13种”属于此前快照。

| 当前入口 | 行为与当前消费者证据 | 建议、替代位置与风险 |
| --- | --- | --- |
| `--home-ui-review` | App第30～50行，仅 `ProductHomeView`，无物理／托盘／日常设置；[首页历史验收](../verification/2026-10-01-home-ui-fidelity.md)第25行 | 迁入现有 `eng/tools/Dango.JointReview` 的纯UI窗口；只保留布局用途，不作为产品体验验收 |
| `--settings-ui-review` | 同一分支调用 `ShowSettingsPage`；[设置历史验收](../verification/2026-10-01-settings-ui.md)第42行 | 与首页纯UI入口一批迁移；保留“未连接桌面”反馈，避免静态预览冒充设置真实生效 |
| `--physics-host-review` | App第82、108～117行；[脚本](../../eng/run-physics-host-review.ps1)第23～32行；旧模块固定哈希及宿主诊断 | 本阶段保留为旧宿主诊断；README显式标明版本与用途。迁出需先剥离旧宿主消费者，不为减少计数重做宿主 |
| `--eye-hat-review` | App第83、108～117行；[脚本](../../eng/run-eye-hat-review.ps1)的 `Hosts` 分支第14、31、49行；独立附件面板 | 保留旧附件／宿主诊断。该脚本其余Static／Eyes／Hats本来已使用JointReview，不能整脚本删 |
| `--unified-live-home-review` | App第84、110行；host第54～65行选择旧产品runtime；[入口测试](../../tests/Dango.Desktop.Tests/AppShell/DesktopPetRoomEntryTests.cs)使用对应构造参数 | 退役冗余CLI；产品检查改默认链＋受保护独立数据。旧隔离构造使用点需先分类 |
| `--porch-scene-review` | App第85、111行；host第220～223行，禁止真实房间绑定；[门廊预览合同](../verification/2026-10-04-room-porch-e1.md)第9行描述虚构门 | 退役产品CLI；历史虚构门若仍有设计用途，只在工具夹具中显式展示，正式门廊保持真实目录 |
| `--local-room-review` | App第86、112行；[脚本](../../eng/run-local-room-review.ps1)第16～23行；两个ordinary-furniture脚本也用该入口。另有LocalRoomProbe、JointReview与多组测试直接构造旧隔离host | 先迁移实际产品检查和脚本，再退役CLI；不能只删App开关，见下表 |
| `--desktop-pet-room-review` | App第87、114～116行自动入桌宠；[脚本](../../eng/run-desktop-pet-room-review.ps1)第16～26行还写出CMD；[桌宠测试](../../tests/Dango.Desktop.Tests/AppShell/DesktopPetRoomProductTests.cs)直接构造 | 改默认链后沿真实门廊入桌宠；旧一键自动入场仅保留为工具驱动，不能新增生产行为开关替代 |
| `--face-review` | App第88、129～136行强制owned设置；[脚本](../../eng/run-face-review.ps1)第21～35行绑定旧E1模块 | 先保证owner检查、实例隔离和默认新身体覆盖，再迁移脚本并退役CLI／专用settings选项 |
| `--accessory-review` | App第89、139～146行强制owned设置，第112行实质选择旧local-room路径；[跨进程测试](../../tests/Dango.Desktop.Tests/Accessories/AccessoryProcessRestartTests.cs)第162行等仍引用 | 与face同批替换，继续验证真实保存／重启／桌宠交接；不将旧路径测试改名当作默认链验证 |
| `--soft-body-preview` | App第59～64行；[窗口](../../src/Dango.Desktop/Rendering/Dango/SoftBodyPreviewWindow.xaml.cs)第17行起；老单体参数实验 | 迁入现有JointReview，连同配套XAML移动；保留真实实验，不冒充新身体参数效果 |
| `--paired-contact-preview` | App第67～72行；[窗口](../../src/Dango.Desktop/Rendering/Dango/PairedContactPreviewWindow.xaml.cs)第12行起；[历史合同](../verification/phase-1-p2d-007-paired-contact.md)第38行是确定性时间线 | 迁入JointReview；旧[恢复设计](../phase-1-dango-physics-characteristics-restoration-design.md)第79行已将其排除当轮人工验收入口，物理回归仍保留 |

当前互斥检查也有重复：App第90～93、228～234行和host第47～51行分别检查子集；preview／probe／smoke由分支顺序决定先后。本轮未做组合参数运行验证，不能宣称解析漏洞已修复。实施时由同一启动解析结果确定运行选项及数据作用域，针对不兼容组合在任何窗口、实例或设置加载前拒绝；不建立插件式启动框架。

### 3.1 容易遗漏的直接消费者

| 旧构造路径／脚本组 | 当前消费者（事实） | 实施约束 |
| --- | --- | --- |
| `isolatedLocalRoomReview` | [LocalRoomProbe](../../eng/tools/Dango.LocalRoomProbe/Program.cs)、JointReview的[家具验证](../../eng/tools/Dango.JointReview/OrdinaryRoomFurnitureVerification.cs)／[集合验证](../../eng/tools/Dango.JointReview/OrdinaryRoomFurnitureCollectionVerification.cs)；测试目录中的LocalRoomProduct、LocalRoomWallpaperProduct、LocalRoomWindowNativeInput、LocalRoomWindowReturnAppearance、LocalRoomAppearanceIntegration、LocalRoomWallpaperPettingManual、SettingsRoomIntegration、FurnitureEditingEscape、AccessoryIntegration、OrdinaryRoomFurnitureProduct／Handoff／GloveInput／RejectedEntry、RoomCameraWindow、DesktopPetRoomEntry | 按“产品默认链验证”与“旧实现专用回归”逐个分类；前者迁到默认host，后者保留明确旧版本夹具。CLI移除不授权删这些测试或整段host |
| 其它产品review构造 | `isolatedFaceReview`：FaceProduct、LocalRoomAppearanceIntegration、DesktopPetRoomEntry；`isolatedDesktopPetRoomReview`：DesktopPetRoomProduct、DesktopPetRoomEntry；unified／porch：DesktopPetRoomEntry | 先迁移对应产品断言；旧模式互斥断言在退役后改成“旧参数拒绝”，不直接抹掉数据安全覆盖 |
| 旧产品脚本 | `run-face-review`、`run-local-room-review`、`run-desktop-pet-room-review`，以及 `run-ordinary-furniture-room-review`／`run-ordinary-furniture-collection-review` 的Desktop部分 | 不改写已生成旧包／CMD；当前脚本转正式链，旧模块实验仍由显式工具入口保留 |
| 已独立的工具脚本 | `run-live-furniture-review`、`run-static-platform-review`、`run-round-ball-review` 已启动JointReview；`run-local-room-catalog-review` 启动LocalRoomProbe；`run-multiplayer-review` 启动MultiplayerSample／Server | 保留；按实际物理版本标注用途，不合并为产品验证，不运行服务来“确认可用” |

可复查：`rg -l -e isolatedLocalRoomReview -e isolatedFaceReview -e isolatedDesktopPetRoomReview src tests eng -g '*.cs'`。上表测试简称均对应 `tests/Dango.Desktop.Tests/` 中同名 `*Tests.cs`；不是新的测试矩阵或全部重跑要求。

## 4. 测试／样板候选及共享内容

| 候选与事实证据 | 建议／替代位置 | 影响、风险与最小验证 |
| --- | --- | --- |
| `src/Dango.Desktop/Rendering/Dango/SingleDangoMotionRuntime.cs`（源快照）第7行；仅`tests/Dango.Desktop.Tests/Rendering/SingleDangoMotionRuntimeTests.cs`（源快照）第10行使用 | 建议退役生产中的旧包装及只测该包装的专用测试；仍有价值的启动／停止／释放不变量由现有Stacked runtime测试承接。无活跃测试消费者则不为保留类型而复制到tests | 不声称两个runtime行为等价；确认生命周期覆盖没有缺口后再删，产品启动／关闭定向回归 |
| `src/Dango.Desktop/Physics/Presentation/DragGrabPointMapper.cs`（源快照）第3行；仅`tests/Dango.Desktop.Tests/Physics/Presentation/DragGrabPointMapperTests.cs`（源快照）第7行 | 建议退役未消费的视觉归一化映射与仅验证它的测试；不把它重新接入生产 | 当前实际链见DangoSceneWindow第415／438行、WorkerWWindow第580～627行、LocalRoomSceneDriver第201／210行、DesktopPhysicsScene第290～345行。host/world坐标转换并不等价于旧局部抓点语义；须保留现有拖拽／缩放安全断言 |
| `src/Dango.Desktop/Rendering/Dango/DangoBodyColorPolicy.cs`（源快照）第4／31行；仅`tests/Dango.Desktop.Tests/Rendering/DangoBodyColorPolicyTests.cs`（源快照）第6行；旧[外观整理计划](2026-10-05-appearance-consolidation.md)第14行提及辅助槽位 | 建议退役未消费的辅助色策略。主色不变，真实颜色直接来自配置；若批准保留辅助槽位产品意图，则此项暂缓，不能把无调用解释成意图已取消 | 当前主色：[AppearanceSettingsSession](../../src/Dango.Desktop/AppShell/ControlCenter/AppearanceSettingsSession.cs)第52～53行、[Belt presenter](../../src/Dango.Desktop/Rendering/Dango/BeltBodyAppearancePresenter.cs)第95／223行。验证主色编辑／取消／保存／重载与呈现；不改外观格式 |
| `src/Dango.Desktop/Physics/Presentation/DeterministicFakePhysicsWorld.cs`（源快照）第3行；构造仅见[表现契约测试](../../tests/Dango.Desktop.Tests/Physics/Presentation/PhysicsPresentationContractTests.cs)第22行起 | 将假世界迁到 `tests/Dango.Desktop.Tests/Physics/Presentation/Support/`；其使用的真实模型留生产 | 同文件第18行的AuthoredPoseSampler须先原样拆为独立生产文件；契约／fallback诊断定向回归，不删整文件及枚举 |
| `src/Dango.Desktop/Physics/Presentation/DeterministicFakePhysicsWorld.cs`（源快照）第18行起 | **保留生产职责**，只拆文件；不是假世界一起迁出 | [IPhysicsPresentationWorld](../../src/Dango.Desktop/Physics/Presentation/IPhysicsPresentationWorld.cs)第30行、[网络宠物](../../src/Dango.Desktop/Multiplayer/NetworkPetPresentation.cs)第114行、Stacked runtime第920行、Box2D与Dango适配器都调用。保留轮廓／附件／错误原因及native错误诊断 |
| `src/Dango.Core/Scene/PlaceholderSceneFactory.cs`（源快照）第7行；只见[资产测试](../../tests/Dango.Core.Tests/Assets/AssetPackageParserTests.cs)第122～123行 | 迁入该Core测试项目的Scene夹具，保留确定性构造能力 | 只移动工厂，placeholder资源及其[构建校验](../../Directory.Build.targets)仍有职责；资产包解析／确定性快照测试即可 |
| [Box2DStackedContactPresentationWorld](../../src/Dango.Desktop/Physics/Presentation/Box2DStackedContactPresentationWorld.cs)第7～19行同时定义StackedPhysicsPresentationFrame，含新room帧 | 保留共享契约；若后续迁旧实现，先拆共享记录，当前不删整文件 | 当前DesktopPhysicsScene／runtime仍消费共享帧。旧Box2D目录整体退役不属于本阶段 |

上述三个候选包装／映射／辅助色策略的建议是“确认无真实职责后退役”，与假世界／场景工厂的“迁移活跃测试夹具”不同。所有候选均未实施删除。

## 5. 真实所有权与目录边界

| 层／所有者 | 当前职责和证据 | 本阶段处理 |
| --- | --- | --- |
| 首页／捏脸／配饰／门廊单体 | [ProductHomeView.BeltBody](../../src/Dango.Desktop/AppShell/ControlCenter/ProductHomeView.BeltBody.cs)第46～73行建立BeltBodyRuntime与presenter；[worker](../../src/Dango.Desktop/Physics/Belt/BeltBodyRuntime.cs)第8～12、26、51、98、189行承担owner线程、FIFO、暂停确认与关闭 | 保留；这些页面共享该单体，不把所有页面各造一个runtime |
| 房间／桌宠 | [LocalRoom driver](../../src/Dango.Desktop/AppShell/Rooms/LocalRoomSceneDriver.cs)第17～22行与HostFrameScheduler；[runtime](../../src/Dango.Desktop/Rendering/Dango/StackedDangoSceneRuntime.cs)第23、41、278行；[adapter](../../src/Dango.Desktop/Physics/Presentation/BeltRoomSceneAdapter.cs)第15、46～48行 | 保留同一房间runtime及唯一帧推进；房间owner不是重复的编辑worker |
| 首页与房间交接 | [BeltBody](../../src/Dango.Desktop/AppShell/ControlCenter/ProductHomeView.BeltBody.cs)第109～121行等待PauseAsync；[LocalRoom host](../../src/Dango.Desktop/AppShell/DangoApplicationHost.LocalRoom.cs)第624、649、709～715行按已应用外观及已保存配饰准入 | 保留屏障、代际、成功帧与失败恢复；不通过合并线程／owner“去重” |
| 生产与历史LiveScene混合 | [LiveScene partial](../../src/Dango.Desktop/AppShell/ControlCenter/ProductHomeView.LiveScene.cs)第87行是旧绑定，但第110～145行附近的焦点事件也接Belt处理；Porch／Settings及LocalRoom partial还同时分派新旧路径 | 只在调用分类后移动旧专属片段；不能整份LiveScene或旧partial删除 |
| product native模块 | [CMake](../../eng/native/box2d/CMakeLists.txt)第9～11行由dango_belt.c构建；[DangoBeltWorld](../../src/Dango.Physics/DangoBeltWorld.cs)第57／382行有线程所有权；[合同](../../eng/native/box2d/dango_belt_product.md)说明热配置与单体ABI | 保留独立模块 |
| room native模块 | CMake第12～14行由dango_belt_room.c构建；[room源码](../../eng/native/box2d/dango_belt_room.c)第5行复用dango_belt.c；[DangoBeltRoomWorld](../../src/Dango.Physics/DangoBeltRoomWorld.cs)第36／194行有owner约束；[room合同](../../eng/native/box2d/dango_belt_room.md)包含集合事务与整world回滚 | 保留独立ABI／模块／owner；不因共用核心而合DLL，不改材料、求解器或预算 |
| 独立工具与导航壳 | [JointReview项目](../../eng/tools/Dango.JointReview/Dango.JointReview.csproj)已引用Desktop／Physics，且[friend权限](../../src/Dango.Desktop/Properties/AssemblyInfo.cs)第6行已存在；[GeometryProbe](../../eng/tools/Dango.GeometryProbe/README.md)有实际独立发布用法；[ShapeReview](../../eng/tools/Dango.ShapeReview/README.md)保留历史指引 | 复用现有工具承接预览；GeometryProbe不因不在solution就删。ShapeReview保留历史导航，文案是否标历史在后续规则批单独处理 |

## 6. 仍未关闭的边界

Schema1间歇保存仍UNKNOWN；Face旧480×270与853×480失败、SignalR.Common 10.0.11的NU1903保留。完整Windows／DPI／多屏／59FPS／多人继续独立。此前481项包清单、10/10预检和121/121等是[既有交付证据](../verification/2026-10-21-final-closeout.md)，本轮没有重跑，也不因静态整理变为更大范围通过。
