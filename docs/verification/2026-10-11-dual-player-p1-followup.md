# 双人P1第二轮：规定中心与跨进程重放

日期：2026-10-11。分支：`codex/ordinary-room-physics-admission`。合同冻结提交：`552c43479c732d6758f419c1d900acac2593950e`；原生恢复基线：`ce68521bdd5765c67f9e3de97cd4376e5c34b1bb`。

**结果：A已改善推挤呈现，但堆叠求解仍停止；B同构建重放正确，体积与耗时预算失败。P1不准入，P2停止。** 自动回归通过不替代失败的技术准入门；Linux、实际UI、人工及双机仍未验证。

用户回复“继续”，批准[推荐A＋B](../planning/2026-10-11-dual-player-p1-followup-proposal.md)并行隔离实施、虚构夹具、构建／验证及本地提交。依据[本轮冻结合同](../planning/2026-10-11-dual-player-p1-followup-contract.md)，保留[第一轮](2026-10-11-dual-player-physics-admission.md)红灯和原物理／绘制门槛，不改变完整续接要求，不进入正式账号／服务／数据库。不合并main、推送、安装、部署或替换日常DLL；安全重生、solver私有结构／依赖补丁和Linux环境启动未授权。

## 1. 实现与来源

A新增独立规定中心ABI v1：输入248 bytes，诊断3936 bytes。显式绑定epoch、严格ordinal、共同采样时间及真实authority.SceneRevision，整组验证身份／支撑／pair后才步进。20Hz位置Hermite的速度为同一曲线解析导数；每个实际native子步规定kinematic core路径，节点约束、接触、限速和阻尼只作用于16节点。没有末尾吸附core、renderer偏移、上传权威轮廓或修改原团子参数。故障锁存，最后合法输出和失败诊断分别保留；旧Step、authority、旧presentation、debug和家具入口不能绕过新角色。

B新增独立`eng/tools/Dango.PhysicsReplayAdmission/`。从ce68521归档原生源码，在单独CMake目录编译Capture／Replay executable；两者是不同PID的真实进程。从规范空世界完整重放配置、成功拓扑／边界／Reset和实际controls，仅连续完全相同的controls压缩StepSpan。17点原始位置／速度通过自有rig ID及Box2D公开getter读取。独立oracle只比较恢复边界和未来120步，不作为恢复输入，不计入journal预算，不提供生产checkpoint导出。

实际dt保留`TimeSpan.FromSeconds(1d/60)`：.NET10.0.12实测Ticks166666、TotalSeconds`.0166666`，native float bits`0x3C888865`，实际自适应子步16；配置4不是每帧实际只执行4步。原Box2D3.1.1、16节点、最小面积`.42`、最大速度`12 p/s`及地面`−.72 p`不变。

文件范围：`eng/native/box2d/dango_prescribed_*`及角色guard、`DangoPhysicsWorld.PrescribedPresentation.cs`及可选加载、`Presentation/`和对应测试、独立replay工具。没有正式房间入口、Hub、账号、数据库、用户存档或家具多人接线。

## 2. A：真实绘制与停止结果

最终规定中心DLL SHA256：`4E2B92B5CC73A6C586A7A9F8EFE96F03187AECBAC6A5975E34BFBC9BB7E32A68`，路径`artifacts/p1-followup/native-build/Release/dango_physics_module.dll`。semantic各轮均复制冻结DLL，保留首个`3EE3E771…`与中间`135E1D5C…`证据。最终24项为18通过／6失败／0跳过：10合同、3推挤、4失败型反例、1真实侧触／分离通过，6堆叠／withdraw失败。最终证据在`semantic-prescribed-final2/`、`semantic-prescribed-final2-counterexamples/`及`test-results/semantic-prescribed-final2.trx`，均位于`artifacts/p1-followup/`。

| 场景 | 30／60／144Hz结果 | 实际执行范围 |
|---|---|---|
| 推挤 | 3／3完整通过 | 各600固定步；最大实际绘制侵入比`.0300066361`，最大真实core误差约`4.773e−6 p` |
| 堆叠 | 0／3通过 | 各41步合法，Tick42 native `−91`停止；合法前缀侵入比最大`.0300098745`、core误差最大约`6.377e−6 p` |
| 移除下层 | 0／3通过 | 同样Tick42停止；尚未执行Tick181的实际删除、下降及落地，不能声明撤支撑呈现通过 |

原门槛保持：实际quadratic B-spline128点SAT侵入比≤`.031`，支撑正gap≤`.008 p`，绘制中心及实际core路径误差≤`1e−5 p`，原面积／速度／交叉／锐刺门禁保留。完整重建误差门只在600步全部完成后计入通过；堆叠和withdraw的前缀数值不能作为全程通过。

Tick42故障来自规定中心下的原节点自约束求解：原96轮sweep未通过冻结的残差／有效性门。失败步骤真实core误差仍在预算内；因此不能归结为旧动态core漂移，也不能据此宣称数学上无可行形状。没有增加迭代、放宽残差、调参数或fallback补绿。

另一个真实侧触／分离场景完整执行600步，发生113个侧接触，未授予支撑且后来明显分离。严重重叠反例使用首次零速度、start=end的近重合中心，确实进入native求解后`−422`停止；早期因巨大位移先被速度预检拒绝的弱反例保留，只代表预检边界。真实stack首败后仍保留Tick41合法输出，下一次Advance拒绝，不发布失败形状。

显式epoch、SceneRevision、解析导数、反向normal、侧向伪支撑、远离假pair和实际core proof反例与健康轨迹分开。局部语法／物理矛盾检查不证明服务器授权；授权属于未开始的P2。

## 3. B：恢复正确性与预算

冻结EXE SHA256：`B4707336E379A234F2C68B29B186342F253295AA2971475F5A0D22B29003A750`。BuildId：`bb1a7a3cffe41f5121c41ad3f7c8036c595ecc378f3ab038ed0f743e2bd3876f`；manifest记录实际21份native源码、233份Box2D源码、probe／CMake及编译设置，可复算标识。Windows x64、同构建与同dt，不代表跨CPU／编译器／Windows–Linux确定性。

9个正常场景全部正确：五个原冷恢复场景，加拓扑／边界／Reset／其他world干扰、Stop、两种600秒模拟历史。每个恢复边界及未来120步的17点位置／速度误差均为0，authority／支撑／Jump／cooldown语义差异为0。

| 600秒模拟历史 | journal大小 | 首次恢复耗时 | 正确性 | 预算 |
|---|---:|---:|---|---|
| 稀疏单体操作 | 26968 bytes | 12.357381秒 | 120未来步误差0 | 体积通过，5秒门失败 |
| 高频双体操作 | 528656 bytes | 86.5052757秒 | 120未来步误差0 | 65536-byte与5秒门均失败 |

计时从打开／校验journal至重建最后安全帧；future120对照另计。首轮并行负载未控制。本任务所有构建／测试停止后，使用同一EXE与既有journal／oracle仅单独Replay稀疏历史：恢复11.5746393秒，未来120步另计0.0414263秒，全部原始位置／速度与语义误差仍为0；实际exit1，原因`restore-time-budget`。子进程14620与原Capture进程35572不同，已退出、native bytes0→0、owner无违规。证据另存`artifacts/p1-followup/replay-final-timing/evidence.json`及`history-600s-replay.jsonl`，没有重建、Capture、重跑高频历史或覆盖首轮；未控制其他用户负载，也未停止用户程序。高频历史已确定超过体积预算，超限Replay只作为诊断且实际exit1，不计准入。

9个故意破坏journal都由实际Replay进程exit1：缺Jump、丢Stop、Add顺序、Tick、缺段、版本、build标识、dt、长度。缺Jump／丢Stop结构有效且重算完整性校验，由真实动态oracle识别。缺段在新增恢复边界预检处更早拒绝；首轮harness因旧预期reason停止，已保留原因并修正预期，仅重跑廉价反例。

最终主汇总覆盖9正常＋9负例，共36个子进程，全部退出、native bytes归零、owner-thread无违规；早期运行及修正前证据另存保留。runner自身exit1，`Correctness=true`、`Budget=false`、`BAdmitted=false`。独立完整checkpoint capability仍unsupported，未实现耐久journal／数据库或任意长历史有界恢复。

## 4. 回归、资源与交付

| 最终验证 | 实际结果 |
|---|---|
| 新native规定中心 | 正常36104checks／0失败；动态core变体7失败、额外节点速度能量变体2失败，均实际exit1、allocation delta0 |
| 新managed binding | 2／2通过；独立输入预检及真实诊断的身份、ordinal、finite和未用槽腐败反例 |
| 原轨迹／owner／坐标／容量 | 30／30、0跳过；三种authority轨迹各1800步42floats逐位对照，旧单／双／三体各布局900步全状态对照，owner／生命周期与坐标检查 |
| 原authority native | 正常3721checks／0失败；三破坏变体219／600／1失败，均预期实际exit1；公开pose冷恢复仍5／5在首个续步发散，不计准入 |
| 旧平台／皮球／家具集合 | 平台边界与lastsafe零失败；球1037cases／244104checks零失败；集合1507checks零失败 |
| 独立headless | exit0、Success=true，约9.754秒；三个场景各1200步、64额外world生命周期、取消退出，native bytes0→0 |
| Desktop隔离Release | 0warning／error；构建涉及的20份Physics源码hash在最终候选后复核未变 |
| 静态与交叉审查 | native及managed源码、角色guard、实际core路径、能量反例与独立replay只读交叉审查无新增明确阻断；29份任务文件、185项相对文件链接无缺失，无尾随空白或本机配置／敏感凭据匹配，`git diff --check`通过，见`final-static-checks.json` |

新接触投影正常场景实际26次能量检查：累计规定中心做功约`.00012077007475`，一项实际`R32≈1.051e−9`、`R64≈1.002e−15`，解析能量门通过。故意额外速度的实际`ΔKE−W≈.2395878`，超过`1e−6＋R32＋R64`，明确失败；动态core反例第8子步误差`1.04904175e−5`超过`1e−5`。这不表示展示17点动量／能量守恒，原authority门独立保持。

最终日志包括`prescribed-probe-frozen-{0,1,2}.log`、`prescribed-binding-frozen-tests.log`、`root-original-behavior-final-tests.log`、`root-{authority-0,authority-1,authority-2,authority-3,static-platform,round-ball,collection}-final.log`、`root-headless-final.json`及`root-desktop-build.log`。native冻结来源在`prescribed-frozen-manifest.json`；根的完整source／binary／资源记录在`final-source-and-resources.json`。测试后只有文档／交付记录变更。构建和headless结果不证明实际房间接线或在线行为。

本轮误用旧构建目录曾覆盖第一轮ignored候选DLL；误写产物另存`artifacts/p1-followup/miswrite/`。从ce68521冻结原生源码在独立recovery目录按原VS2022 x64 Release `/Brepro`重建，得到完全相同的`C7D3140079BEFD29E3E8512B5CF69376D4B342AD583A7C458EB89420A72FE07A`，核对后恢复旧候选；根已复核。旧日志与失败证据未覆盖。后续A只用本轮`native-build/`。恢复来源／配置／hash见`recovery-evidence.json`。

受保护日常DLL SHA256仍为`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。既有家具人工候选窗口未停止。测试只写ignored artifacts及虚构数据，没有启动正式服务、监听端口、数据库／对象存储、Docker／WSL或修改用户存档。最终按本任务所有权核对，测试进程与监听均为0；保留3个共享.NET构建辅助进程和既有家具候选进程41216。ignored证据与虚构journal／oracle保留，没有删除持久数据；资源和四份DLL／EXE的实际hash见`final-source-and-resources.json`。

Linux、Windows实际可视窗、600秒真实活动观察、人工及双机验收均未验证。呈现headless门失败后没有启动UI。本轮交付为隔离分支本地提交；不构成main合并、推送、日常安装／部署或P1人工验收。设计树、家具候选及账号／后端分支均未修改。

## 5. 停止门

A仍缺堆叠与实际撤支撑；B虽正确重放，但固定体积／时间预算未成立，完整checkpoint仍unsupported；Linux继续未验证。P1不准入，不推进P2。任何改变原物理模型、恢复预算或服务重启后安全重生的方案，都须独立设计和用户批准，不能以本轮局部绿灯替代。

用户随后回复“继续”，只读核对首败调用顺序后交付[下一轮诊断与恢复取舍](../planning/2026-10-11-dual-player-p1-next-decision.md)；用户明确选择安全出生规则并先做堆叠诊断。见[R0合同](../planning/2026-10-11-dual-player-p1-r0-contract.md)及[恢复ADR修订](../adr/proposals/2026-10-11-world-loss-safe-spawn.md)。该批准改变后续world丢失／释放后的产品恢复要求，不能改变本报告原合同下的失败结果；R0不包括算法修复或正式恢复接线，P1仍未准入。
