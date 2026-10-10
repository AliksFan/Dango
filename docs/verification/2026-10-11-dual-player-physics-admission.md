# 正式普通房双人P0–P1隔离验证

日期：2026-10-11。分支：`codex/ordinary-room-physics-admission`。设计来源：`af55f9ce6dbed24e5621f28307cfa2cd57eb4efa`；P0冻结提交：`ae7f01ccb963f86a54098a8873a2fe642b0ffb23`。

**结果：P0合同已冻结；P1部分能力有实际证据，但不准入。完整恢复及实际绘制接触门禁失败，Linux未验证，依赖这些能力的P2停止。** 本文不是整个多人阶段验收或正式双人房可玩交付。

范围依据[实施计划](../planning/2026-10-10-formal-ordinary-room-dual-player-plan.md)和[冻结合同](../planning/2026-10-11-dual-player-p0-contract.md)。用户已授权本批隔离实施、虚构夹具、构建、验证及本地提交；P2–P6、正式服务／数据库、main整合、推送、安装和部署仍未授权。

## 1. 实现范围

- native独立可选authority ABI v1：280-byte共同帧，中心／速度、地面及团子支撑、接触和实际起跳结果。旧ABI1、42floats、原三体上限与未启用路径保持。无家具权威最多两体。
- managed可选绑定及strict owner-thread；整组presentation目标校验和角色互斥。完整checkpoint capability明确unsupported，失败导入保留活跃世界。
- 普通房仿射坐标映射和独立无UI headless工具。64／8纯DTO队列，同一专用线程完成native生命周期、步进和读取；工具只串行提交，未证明生产背压或多房间调度。
- 独立20Hz共同帧sampler、60Hz局部轮廓候选、真实quadratic B-spline128点绘制SAT及失败门禁。候选未通过，不接WPF或正式普通房入口。

主要文件在`eng/native/box2d/dango_authority_*`、`DangoPhysicsWorld.Authority.cs`、`src/Dango.Physics/Admission/`、`src/Dango.Physics/Presentation/`、`eng/tools/Dango.PhysicsAdmission/`及四份对应测试。家具代码仅加角色保护，未解除单团子guard。未升级依赖、修改Box2D缓存或原团子参数，未接新圆形模型、家具／皮球／手套、Hub、账号、数据库或用户存档。

## 2. 最终候选与门禁

最终源码／测试文件hash与资源记录保存在`artifacts/ordinary-room-physics-admission/final-source-and-resources.json`。候选在最终验证前冻结，之后只整理文档，没有改变源码或二进制。

| 项目 | 当前实际证据／结论 |
|---|---|
| Windows隔离native构建 | MSVC `/W4 /WX`通过；.NET10、CMake3.31.6，Box2D3.1.1 pinned官方233文件与只读源码缓存一致 |
| 独立baseline | 从只读设计源码重建；SHA256 `BA6E92D57CAFBD5CC806F709890AA1E8ED784F97CF42A09F4168EE08065E97A0` |
| 最终candidate | `artifacts/authority-native/build/Release/dango_physics_module.dll`；SHA256 `C7D3140079BEFD29E3E8512B5CF69376D4B342AD583A7C458EB89420A72FE07A` |
| authority与原轨迹 | 最终managed14/14、0跳过；5400步、单／分离双／堆叠双的42floats显式逐位对照；native正常3721checks／0失败，三反例219／600／1失败、预期exit1 |
| 坐标与旧单／双／三体 | 最终root22/22、0跳过；坐标6项、owner／生命周期等6项、旧容量与单／双／三体900步逐状态对照10项；与上一行的world测试6项重叠，不重复累计 |
| 静态平台／球／集合旧回归 | 最终平台边界／lastsafe零失败，球1037cases／244104checks零失败，集合1507checks零失败 |
| 独立headless | 最终exit0、Success=true，约13.730秒；三场景各1200步、64次额外world生命周期、取消退出及native bytes 0→0；缺capability的独立baseline反例预期／实际exit1 |
| 呈现合同／反例 | 最终7/7通过、0跳过，包含相位错配、错误center、超限穿透与支撑缝隙反例 |
| 真实绘制呈现 | 最终0/9通过、0跳过；堆叠／推挤有超限穿透，正常门禁exit1；失败保留 |
| 完整恢复 | 5/5冷恢复场景下一Tick发散；capability未准入 |
| Desktop隔离Release构建 | 通过，0warning／error；只是可选绑定源码兼容，不是正式房间接线或运行验证 |
| Linux | 未验证；仅docker CLI与Stopped docker-desktop WSL，没有运行daemon；未启动／安装环境 |

最终日志：`artifacts/authority-native/boundaries-final-{build,managed,static,ball,collection}.log`、`boundaries-final-probe-{0,1,2,3}.jsonl`；root的`artifacts/ordinary-room-physics-admission/{root-final-tests.log,headless-final-build.log,headless-final-smoke.json,headless-final-missing-capability.json,desktop-release-build.log}`。TRX在各自`test-results/`，最终呈现为`semantic-final.trx`。受影响验证通过后未扩大或重复全量历史测试，旧固定artifact依赖缺失的suite不在本轮通过范围。

## 3. 保留的失败与修复

### 原轨迹与角色／故障边界

authority只读输出不得改变物理。故意丢支撑、速度置零、移除支撑起跳反作用的三个native变体必须以exit1失败；正常变体exit0只表示实验harness与可用语义合同通过，**不表示完整恢复通过**。

收尾审查复现旧`dango_collision_step`绕过presentation中立输入限制：两体×四类操作使live状态及输出变化，304checks失败。红灯保存在`artifacts/authority-native/legacy-mode-red.jsonl`；最小guard修复保留旧接口和正常未启用行为。

审查还发现语义输出`authority_error=-422`锁存后，旧步进／成员／边界与平移操作仍可能改变live世界。另有旧诊断导出可在健康新模式下关闭真实接触或阻尼。先复现130checks失败、exit1，保留`semantic-fault-role-red.jsonl`。最终修复传播有效错误、停止故障世界修改、保留最后合法共同帧，并限制所有相关新旧入口；debug setter保留旧void签名且在新模式无操作，oracle返回mode conflict，enable拒绝已降级世界。显式Reset作为重建恢复动作单列，不能让下一Tick静默续跑。故障注入只在独立测试可执行文件，未新增生产测试setter。全部修复在最终正常3721checks及旧轨迹／家具回归中复验。

### 完整恢复失败

Box2D公开API不提供joint impulse、contact cache、sleepTime和solver graph的完整导出／恢复接口。本轮没有引用private headers、raw memcpy内部世界、关闭warm-start或补丁依赖。

独立coldpose实验恢复17点公开位置和速度，初始误差全部0；未来120步超过冻结位置`1e-5 p`、速度`1e-4 p/s`预算，全部第1恢复Tick失败：

| 场景 | 最大位置误差p | 最大速度误差p/s |
|---|---:|---:|
| 地面 | .00122400373 | .00969033875 |
| 动态相撞 | .00343167782 | .0791598037 |
| 堆叠 | .00535873696 | .0929717124 |
| 空中 | .00594799221 | .144071206 |
| 移除下层 | .000442385674 | .0470485836 |

原始记录：`artifacts/authority-native/probe-normal.jsonl`，最终正常probe同时保留该失败实验。`CompleteCheckpointSupported=false`；unsupported导入／导出不改变原世界。不能用Add＋重新落下、初始pose相同或实验进程exit0宣称恢复成功。

### 低频呈现失败

第一轮`target(T) → Step(dt) → 以T中心绘制`错开一物理步：push三项通过，stack／withdraw六项在Tick31出现`.0131398932593 p`支撑缝隙，超过`.008 p`。原始九份JSON保存在`artifacts/ordinary-room-physics-admission/semantic/`。

同一P1内修正为`target(T−dt) → neutral Step(dt) → T轮廓与T权威绘制中心`，并增加相位反例。原参数、SAT及误差预算未放宽。修正后九项全红，30／60／144Hz的对应结果一致：

| 场景 | 首次失败Tick | 实际绘制穿透比 | 原上限 |
|---|---:|---:|---:|
| stack／withdraw | 31 | .03425825853 | .031 |
| push | 176 | .03786973166 | .031 |

绘制中心误差为0、相位对齐、native无fault；所执行前缀的普通中心重建最大误差约`.0002092 p`。这是**提前失败前缀**的数值，不能据此宣布600步完整插值门禁通过。局部动态core和局部轮廓相对权威中心的接触残差造成超限，不能改用本地center或缝隙修补来通过。

时间修正证据保存在`semantic-timing-corrected/`；最终hash证据另存`semantic-final/`。每份包括authority trace、step起点目标、局部core／contour、128绘制点及完整失败oracle。withdraw虽有完整authority source trace，但局部呈现在Tick31停止，**实际显示撤支撑尚未执行／验证**。

## 4. 未验证与交付边界

正式服务、输入限频／deadline／ready、数据库事务、checkpoint持久化／fence、账号接管、多客户端、网络延迟／断线、实际Windows可视窗、600秒活动观察、DPI、性能和人工／双机验收均未在本轮完成。后续协议数值只是P0合同，不能把工具的有界集合视作正式实现。

headless的Airborne仅是空中出生smoke；第300步Jump不能证明空中起跳拒绝。取消probe分别有限等待owner／caller，不能写成总关停≤3秒。单进程计算和Desktop构建不证明在线同步。

资源收尾已核对：本批headless、testhost与native probe进程均已退出，native allocation 0→0；没有本批应用／房间服务的残留进程或新网络监听。测试只写ignored artifacts／TRX及虚构输入，无账号／DB／对象存储、种子数据、正式日志或用户存档。未操作现有人工候选或运行服务；没有删除持久数据。共享.NET构建辅助进程不当作房间服务，也未为收尾停止它们。

本批交付为独立分支本地提交；不标记P1或多人阶段已验收，不合并main、推送、安装、部署或替换受保护DLL。受保护`src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll`最终SHA256仍为`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。旧设计树仍为`af55f9c`且工作区干净；已验家具候选及其他账号／后端分支不纳入本次修改。Markdown相对文件链接278项无缺失，`git diff --check`通过；新文件无尾随空白。

## 5. 下一方案

第一轮交付时，可审阅的[下一轮候选](../planning/2026-10-11-dual-player-p1-followup-proposal.md)建议并行验证展示专用规定中心／局部节点，以及从规范初始世界的同构建跨进程重放；当时两者未实现、未批准。用户随后回复“继续”，批准两条隔离验证线，见[本轮冻结合同](../planning/2026-10-11-dual-player-p1-followup-contract.md)。本报告保留第一轮结果，不以批准下一轮方案改变原失败状态。若恢复的正确性／有限预算不成立，再明确选择服务器重启后安全重生等产品改约；安全重生和私有solver格式仍未批准。

保持本轮原红灯，不进入P2。Linux环境另案列明目标后执行；不会自动启动Docker／WSL或用Windows结果替代。
