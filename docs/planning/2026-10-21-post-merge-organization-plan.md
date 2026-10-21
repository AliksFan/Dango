# 合并后目录、历史入口与工作规则整理方案

日期：2026-10-21。状态：**供审阅，未批准实施**。依据[当前源码盘点](2026-10-21-post-merge-organization-inventory.md)，起点为本地 `main@5ae2292`；文档保存在独立 `codex/post-merge-organization-plan` 分支。

## 1. 目标与本轮结果

整理目标是让产品开发与验收使用正式默认链，减少同一页面的新旧接线；让测试夹具、独立实验、生产回退与共享契约各有明确归属。保持已验收身体、接触、W/S、配饰、房间保存与交接行为。

本轮只完成消费者盘点、设计、规则草案和实施顺序。未修改产品、测试、构建或AGENTS；未运行程序、占屏、启动服务、合main、推送、安装或清理。文档阶段允许的本地提交不等于实施授权。

不纳入：整体目录改名、物理算法／材料／预算调整、两个native模块合并、统一所有owner、数据库／多人／性能扩展、历史证据迁移或删除、完整历史矩阵重跑、Schema1未知故障及旧小尺寸／依赖问题的顺手修复。本方案不设计新页面或布局；后续若改变可见页面，先按桌面规范v0.1对应章节另行设计和验收。

## 2. 推荐取舍

| 决策 | 推荐与理由 | 替代方案及后果 |
| --- | --- | --- |
| 产品验收入口 | 普通产品host＋显式独立数据作用域；脚本沿真实页面入场。先替换消费者，再退役unified／porch／local-room／desktop-pet／face／accessory共6个旧产品review CLI | 只统一脚本名称仍会选择旧物理；一次删除全部host分支会破坏大量旧夹具。两者均不采用 |
| 纯UI／旧preview | 首页UI、设置UI、soft-body、paired-contact共4个独立窗口入口迁入现有JointReview；产品页面控件继续复用 | 新建全能验收框架增加维护面；直接删除会失去有记录的历史诊断。不采用 |
| 旧宿主诊断 | physics-host／eye-hat两入口本阶段保留，明确标为旧模块宿主诊断；probe／smoke保持原用途 | 全部迁出需拆更大的host／验证面板，本阶段无必要。保留项不是永久兼容承诺，也不计为默认产品通过 |
| 测试类型 | 迁出仍被测试使用的FakeWorld／PlaceholderSceneFactory；三个无实际生产消费者的旧包装／映射／辅助色策略按清单退役，先确认产品意图与断言职责 | 把所有候选机械搬到tests会保留只为自身测试而存在的重复实现；按文件名直接删又会误删真实fallback |
| 生产共享内容 | AuthoredPoseSampler先独立为生产文件；共享帧、fallback、焦点与交接函数保留。旧专属逻辑仅在消费者消失后逐段迁移 | 不以“旧文件”标签整体删除LiveScene／Box2D文件 |
| native和线程 | 保留编辑worker、房间owner及product／room两个ABI；本阶段不移动native目录 | 合并会改变线程、句柄注册、热配置、集合事务、发布源哈希等合同，风险超过本轮收益 |

建议实施边界是“6个产品review退役＋4个独立预览迁移＋测试职责清理”，不要求把App的全部诊断归零。每批独立交付，失败不拖入无关项目。

## 3. 启动与数据隔离设计

1. 正式默认产品链保持不变。把当前分散的运行选项、互斥、设置路径和实例身份解析为同一个小型启动配置；不设计registry、插件或新的验收执行器。
2. 通用 `--data-root` 继续只改变数据位置与路径作用域身份。自动写入验收继续使用现有 `--local-install-acceptance-settings` 的owned会话合同，保留虚构标记、日常／包目录排除、重解析路径及设置文件检查；旧face／accessory owner检查的覆盖先由替代入口承接，再退役专用参数。
3. **实例隔离是迁移前置**：当前通用data-root按目录计算实例名，local-install-acceptance未如此处理。建议沿用路径身份算法，使owned验收设置也按解析后的路径隔离；验证不会唤醒、复用或写入日常实例。这属于本方案拟实施变更，尚未修复或实测。
4. 产品验收脚本默认先构建／发布和准备owned独立目录，显示包源、两个模块身份、数据目录和操作说明；可见启动必须显式选择，遵守既有屏幕授权规则。`NoLaunch`只做准备，脚本里的旧“已请求可见窗口”注释不能当新一轮授权。
5. 已退役／未知参数在任何设置加载、实例协调器或窗口创建前拒绝，绝不静默落入日常数据。旧生成包／CMD保留原程序及历史合同，不就地改写。历史产物不要求由新程序兼容其旧review CLI。
6. 不兼容选项组合统一拒绝；维持smoke无host、probe只检查旧模块的原边界。失败仍给出真实错误，不以fallback假帧冒充新身体已成功初始化。

## 4. 模块与文件归属

| 位置 | 应承担职责 | 拟变更 |
| --- | --- | --- |
| `src/Dango.Desktop/AppShell` | 正式页面、设置／保存、房间交接、宿主生命周期 | 先收敛启动选择，保留唯一生产组合；旧review构造留到直接消费者分类后处理 |
| `src/Dango.Desktop/Physics/Belt` | 单体worker、快照、暂停屏障、休眠与原生session | 不改所有权、推进、物理及休眠合同 |
| `src/Dango.Desktop/Physics/Presentation` | 产品adapter、共享帧、真实回退 | 单独拆出 `AuthoredPoseSampler.cs`；不将共享模型连同假世界迁出 |
| `tests/Dango.Desktop.Tests/Physics/Presentation/Support` | 测试假世界 | 放迁入的DeterministicFakePhysicsWorld，保持契约测试能检测错误后端／错误fallback |
| `tests/Dango.Core.Tests/Scene/Support` | 资产／场景确定性夹具 | 放迁入的PlaceholderSceneFactory，不移动真实placeholder资源 |
| `eng/tools/Dango.JointReview` | 显式旧物理、纯UI及独立preview窗口 | 使用已存在的Desktop引用和friend权限承接4个入口；不把整个Desktop源码链接编译第二份 |
| `eng/tools/Dango.LocalRoomProbe`／当前review脚本 | 有界验证、默认产品场景驱动、历史旧模块实验 | 逐个标出物理版本和职责；产品断言改默认host，旧版本回归仍明确隔离 |
| `eng/local-install` | 包／设置兼容性、旧版本校验与回退 | App／host修改时增加新合同；保留全部旧合同，不覆写旧身份使旧包“通过” |
| `eng/native/box2d`／`src/Dango.Physics` | 已验收两个native模块、ABI与绑定 | 本阶段不迁移、不合并、不改变导出及门禁 |
| `docs`／既有artifacts | 当前索引、决策、原始验收和失败 | 新增整理结果与导航；保留历史日期、失败、UNKNOWN及原始产物 |

共享输入清除、焦点、保存或呈现函数只在有重复实现证据时做最小提取，不先创造统一runtime。`ProductHomeView.LiveScene.cs` 混有Belt焦点处理，不能整文件移出生产。

## 5. 最小分批实施顺序（均待批准）

### B1：先隔离测试夹具和共享回退

- 文件：盘点第4节的FakeWorld／Sampler、Placeholder工厂及其两个消费测试；三个退役候选分别小提交处理。辅助色策略须确认旧辅助槽位意图后才纳入删除。
- 先把sampler原样独立，FakeWorld只迁到tests；迁移工厂保持确定性结果。退役三个未消费类型时，核实其测试断言是否在真实替代链已有覆盖，不为了“全绿”删除仍需的业务断言。
- 验证：受影响Core／Desktop构建；AssetPackageParser、PhysicsPresentationContract及真实fallback／网络presentation的现有定向测试。包装生命周期、主色保存／取消及现有抓点／坐标断言按实际差异选择；不重跑原生物理。
- 负控：在隔离副本让fallback原因／native错误诊断缺失，应由现有契约断言失败；若覆盖缺失，只补该边界用例。迁移后的测试仍引用生产帧／sampler，不能复制一份生产算法使二者共同出错。
- 完成标准：生产中无FakeWorld／工厂消费；Sampler生产调用全部保持；删除清单无未处理消费者，职责清单与差异一致。
- 回退：B1局部提交可审查地revert；不触碰存档、旧包或main现有修改。

### B2：先让产品验收走默认链，再退役6个旧产品CLI

- 文件：App、DangoApplicationHost及相关partial、三个直接启动脚本、两个ordinary-furniture脚本的Desktop部分、LocalRoomProbe／JointReview直接host消费者；安装合同和入口／产品测试。
- 顺序：统一启动解析及owned数据实例身份 → 用现有默认产品／房间测试核对替代链 → 脚本改成默认产品路径与真实入场 → 将产品断言迁到默认host、旧版本断言标为历史夹具 → 删除6个CLI与face／accessory专用设置选择。直接host分支只在其全部消费者迁妥后移除；若仍需旧实现，明确保留为开发夹具，不能伪称已清零。
- 现有验证锚点：`BeltBodyProductTests` 的DataDirectory／ActualProductHost／编辑保存重启，`BeltPorchIntegrationTests`，`BeltRoomHostEntryTests` 的默认模块／唯一推进，`BeltRoomResumeSafetyTests`，`BeltAccessoryCompletionTests`。只选实际受影响方法；含激活／全局键鼠的用例须先获屏幕授权，不能按类名整体启动。
- 数据负控：旧／未知参数、冲突模式、缺owner、日常／包目录或链接路径在设置／实例／窗口前失败；两个不同owned目录不能共享mutex，相同规范路径仍应识别同实例。用虚构数据，禁止以真实日常数据做负控。
- 行为负控：重复帧不得再次推进；首页Pause未确认不得入房推进；配置／模块失败不得显示成功；保存／房间准备失败保留原件与恢复入口。复用已有失败夹具，不新建框架或逐家具增加矩阵。
- 发布：App／host源码哈希改变必须新增兼容性身份并保留旧合同；按实际合同变化选择现有 `test-desktop-publish-inputs`、`test-belt-native-package`、`test-desktop-publish-preflight` 和设置回退检查。生成一个新的内部候选包，校验两个模块及源码manifest；仅同包无窗口smoke，不安装。
- 完成标准：6个旧CLI均明确拒绝；产品脚本及产品断言使用默认连续身体；owned数据和实例隔离通过；旧版本测试准确标识；发布／回退合同通过。需真实体验的部分人工通过前仍为待验，不称阶段已验收。
- 回退：该批一个可独立回退的入口／合同提交组；旧合同和旧包原样保留。只回退代码不自动回滚用户数据；本批不改存档schema。

### B3：迁出4个独立窗口入口，收敛残留接线

- 文件：App中的home-ui／settings-ui／soft-body／paired-contact分支，两个preview的XAML与code-behind，JointReview入口／说明；ProductHome及host中经调用证明已无生产职责的片段。
- 保持同一ProductHome控件与旧preview算法；只在工具组合窗口、标题及实验选择。不把历史虚构门引进正式门廊，不重设计主题。移动XAML须同步x:Class、资源路径及引用，避免运行时资源缺失。
- 验证：Desktop和JointReview构建；只验证迁移窗口构造、资源、事件释放和旧诊断边界。能够后台有效运行的构造／渲染优先；真实前台呈现与输入单独申请。默认产品入口／数据解析定向检查一次，不重跑B2已完成且未变化的矩阵。
- 负控：4个旧产品参数应拒绝；工具缺旧模块时应明确失败而非进入默认产品；关闭工具后不得留下timer、native句柄或本批窗口。
- 完成标准：4个独立窗口不再由生产App分支创建；对应窗口源码只在工具项目编译一份；旧宿主两入口用途清楚，正式发布不依赖工具。混合partial若仍服务生产就继续保留，不按文件数量验收。
- 回退：窗口迁移独立提交可revert，历史包不动；不需要重建／复制受保护DLL。

### B4：规则与入口导航收口

- 先按下节草案由用户批准；只对实际采纳条款做最小规则增补，保留main既有AGENTS修改。实施时重新核对用户文件及index，不整文件覆盖或替用户提交原有修改。
- README／验证导航明确默认产品、旧宿主诊断、独立实验；历史命令保留日期与原用途，当前操作入口指向新链。只更新事实发生变化的文档，不机械重写全部基线。
- 验证：相对链接、命令目标与当前参数、无本机绝对路径、`git diff --check`及差异审阅；不因文档变化重跑物理。规则与生产改动分别提交。
- 完成标准：后续agent能从一处当前导航找到正式构建／验收方式，历史证据仍可定位，未批准／未验收状态准确，授权边界不扩大。

B1不依赖B2；考虑本项目默认一次委派一个子任务，推荐顺序实施。B3依赖B2的入口与合同结果；B4按实际完成范围收口。无需服务、数据库、付费资源或依赖升级。若分工，root持有App／host／发布合同；子agent仅持有明确指定的测试夹具或工具窗口，先合夹具再集成入口，同一分支不由多个worktree同时修改。

## 6. 工作规则草案（没有改AGENTS）

建议作为获批后工作流的短补充，在AGENTS只留入口约束和链接；不新增一套与既有第1／4／5／6节重复的授权规则。

> **产品接线**：已批准功能交付前，验证普通启动与正式构建／发布能进入同一已批准产品链。临时参数不得成为唯一可用入口；独立样例通过只证明其声明范围。
>
> **临时入口**：新增review／preview须记录用途、生产／工具所有者、数据作用域、消费者及退出条件。已有通用数据选项足以验证产品时，不新增改变产品物理／页面接线的开关。旧模块实验显式标明版本，不参与默认产品通过判定。
>
> **类型归属**：仅供测试的假世界与场景工厂放测试项目；独立可视实验放eng/tools；生产fallback、共享帧和真实数据适配留src。删除前核对源码、脚本、项目和文档消费者，文件名及“仅找到测试”不能单独证明可整文件删除。
>
> **最小验证**：每个整理批次绑定实际调用差异、失败型断言和回退点。保留同一world唯一推进、交接屏障、成功帧、已保存配置与失败保护；未变化的历史全矩阵不重复运行。静态、后台、前台、人工与Git交付分别报告。
>
> **历史与未知**：包／模块／来源／测试结果按快照绑定；保留旧FAIL、UNKNOWN与未验证项。目录或导航收敛不修改其含义，不删除原证据。

## 7. 审阅与下一阶段授权边界

建议优先实施B1＋B2，并配套补必要导航和最小规则；B3可以后置，B4不要求整份AGENTS重写。明确批准选定批次及其中的类型退役、入口迁移与规则增补后，才进入实施。辅助色槽位若仍需保留产品意图，可从B1退役范围移出，不影响其余批次。

实施许可不包含main合并、推送、安装／部署、删除分支／worktree／证据／数据，也不包含任何前台屏幕使用。占屏项目须按AGENTS第4节说明目的、方式与时长后单独请求；后台验证也仅报告实际覆盖行为。

本轮交付检查仅为文档链接、源码证据位置与Git差异；既有[最终核验](../verification/2026-10-21-final-closeout.md)的481项／10项／121项等结果未重跑。Schema1 UNKNOWN、旧小尺寸失败、NU1903及完整Windows／性能／多人边界继续保留。

## 8. 本轮实际静态检查

- 两份新文档的72处相对文件链接及README新增的2处导航目标均存在；独立只读复核确认四候选、12入口计数和owner职责与源码一致。
- `git diff --cached --check`通过；差异仅两份新规划文档和README一条导航，无生产／测试／AGENTS／受保护DLL改动。主仓库HEAD、无暂存项状态、既有AGENTS及受保护DLL哈希与开始时一致。
- 本轮未构建或运行自动／后台／前台／人工测试；没有新增运行进程、监听端口、测试窗口、输入占用或日常存档写入。规划文档保留在独立分支，本地提交不合main、不推送。

## 9. 对多人方向分支的影响与取舍

2026-10-21补充：用户询问多人方向影响，并明确表示接受影响、必要时可以重做。以下为只读影响判断；这是对兼容性取舍的确认，不将其扩大成生产实施、合并、删除分支或数据库重跑授权。

核查了account-live-sync（0c722ac）、backend-b5-windows（8bd824d）、backend-b5c-acceptance（34e0360）、porch-account-scenes（866cbc9）、accessory-account-c2（011647e）、ordinary-room-physics-admission（b1cb2fe）、ordinary-room-multiplayer-design（9656fce）及p2a-minimal-multiplayer-sync（b6b367a）八个本地ref。方法为merge-base／changed-file交集和分支内符号核对；未模拟最终合并或构建，不根据分支名、文件数或聊天状态判定当前正在开发。存在多个共同基线时，差异规模随基线不同；这里不以文件数量衡量冲突或重做量。

| 范围 | 已核实的交集 | 影响判断／建议 |
| --- | --- | --- |
| B1候选与共享回退 | 八个ref对所核对共同基线均未改四候选及场景工厂文件；main的 `Multiplayer/NetworkPetPresentation.cs:114` 仍调用AuthoredPoseSampler | B1同文件改动冲突风险较低；保持sampler命名空间／生产职责，不将其迁到tests，仍需验证合并后消费关系 |
| 账号／配饰／门廊集成 | account-live-sync、backend-b5-windows／b5c-acceptance、porch-account-scenes、accessory-account-c2均改过 `DangoApplicationHost`／`.LocalRoom`、`ProductHomeView`／LiveScene等B2涉及的接线；B5还有 `CreateForAccountIntegration` 及PorchCrudC1DesktopRig消费者 | 预计需要处理语义冲突、调整构造与事件接线、迁移受影响验证。优先新主线职责，可重做客户端适配，不为保留旧入口而增加长期兼容层 |
| 多人协议／服务端／同步逻辑 | 上述集成分支另含 `Dango.Protocol`、`Dango.Server`、`Dango.Client.Sync` 的账号、房间访问、session或配饰同步实现 | B1／B2方案不修改这些合同。保留业务实现和历史证据，后续按模块接入并重新验证所需边界；不能把客户端重接等同于全部业务推倒重做 |
| 普通房物理准入 | ordinary-room-physics-admission改过LocalRoom／Porch／家具及旧物理绑定；没有Protocol／Server／Client.Sync差异 | 需要按当前新身体重新判断可复用的准入／呈现与接线；既有物理证据只对原模块和快照有效，不能直接外推 |
| 设计／已在main的代码来源 | ordinary-room-multiplayer-design对共同基线只增加规则及设计／计划文档；p2a-minimal-multiplayer-sync对共同基线仅有AGENTS差异 | 不能按名称把它们算成两套待迁移的多人实现。保留设计及来源，按当前主线核对其假设 |

另外，backend-b5-windows的 `docs/planning/2026-10-11-porch-crud-c1r2-local-plan.md:11` 仍绑定B772旧物理，准备记录明确真实C1r2未执行；已读取原聊天的最新结论，完整实机闭环曾决定等待新模型。即使不清理，接入当前新身体主线也需要适配、受影响验证和重新冻结；这部分不能全部归因于本次整理，更不能直接复用旧资源／屏幕授权。

推荐集成顺序：先完成获批的主线整理，保留旧分支作为实现来源；后续从整理后的稳定主线按业务模块接回多人客户端，重做必要入口及验收。独立后端／协议工作可继续；客户端入口改造宜基于同一新基线，避免在旧review链上继续堆接线。此处仅是顺序建议，没有给其它聊天发指令或暂停其工作。

本次复核未改任何相关分支。account-live-sync工作区现有两个PostgreSQL脚本／模块修改保留；main仍仅原AGENTS修改。新增内容仅本节及实施优先级说明。
