# 双人 P1 R1：地面感知位置投影验证

日期：2026-10-11。用户对 [R1 方案](../planning/2026-10-11-dual-player-p1-r1-proposal.md)回复“继续”，批准隔离实施、验证及本地提交。[精确合同](../planning/2026-10-11-dual-player-p1-r1-contract.md)先冻结于 `df5de6f`，再编辑代码。

**R1 局部夹具及九条完整 headless 呈现轨迹通过，原 Tick42 堆叠停止已修复。仅隔离候选；P1 整体仍未准入，P2 停止。** Linux、实际窗口／多客户端／双机、人工手感及已批准安全出生恢复接线未验证。旧完整恢复失败与预算红灯继续保留。

## 1. 变更范围

新增 `eng/native/box2d/dango_prescribed_position_projection.h`：径向位置行考虑距离边界圆与 floor 的允许交点，raw／display 角位置行保留原梯度／display Jacobian，用最多16 active sets求三节点质量加权线性化位移。每行保持原位移上限，完整非线性残差仍按原算法验证，预算仍96轮。

`dango_prescribed_internal.h` 只有 prescribed 自身约束入口改用此候选。速度行仍调用原实现，在对应位置修正前按原梯度和关闭方向投影；位置修正不写速度、不产生额外力。原周长／最终地面夹紧、角色与故障锁存保持。旧 `dango_self_constraints.h`、legacy／authority、配置／生产 ABI、UI、数据库和正式账号代码均未修改。

新增独立工具 `eng/tools/Dango.PrescribedPositionProjection/`，先验证真实入口，再条件接入 native。两份 semantic 测试新增专用 `semantic-r1` 证据位置，不覆盖前轮输出；修复后正常堆叠成为正例，故障锁存反例改为限速内逐步进入不可解的地面区域，继续要求真实 native 解算失败及最后合法帧保留。

## 2. 原失败入口与局部正反例

从 R0 原记录提取唯一 Tick42／substep5／entity1／phase0／Call2587 的 stage0、iteration0，372 bytes全部 float bits未变。入口 SHA256 `6C1EE9673190E0801C332F625DC3DFBA8361735AF0B344137204AD8CB6647361`；原失败末态 `0485751CAD12587BC0561BD226AAC3983293B69E62D20E0357813ADA9D7D389F`。没有用失败末态代替入口。

| 实际执行 | 结果 |
|---|---|
| 原算法 | 96轮失败，最大残差 `6.61246013e-5`；全部位置／速度／质量／参数末态与R0逐位相同，expected-red成功复现 |
| R1单一候选 | 6轮成功，原完整最大残差 `9.79937613e-6≤1e-5`；core、全部速度、质量和参数逐位不变 |
| 关掉floor | 自报success，完整oracle拒绝；残差 `.000295919541`，exit1 |
| 跳过径向行／径向门 | 自报success，完整oracle拒绝；残差 `1.47258534e-5`，exit1 |
| 跳过两种角行／角门 | 自报success，完整oracle拒绝；残差 `.00200000009`，exit1 |
| 移开core | 即使几何残差合法，core不变性拒绝，exit1 |
| 局部行完成即返回／跳过最终检查 | 完整oracle拒绝；残差 `.001747653`，exit1 |

所有变体均实际执行同一原入口，未另造单门夹具补绿。最终局部记录在 `artifacts/p1-r1/local-audit-final/`，之前两个运行批次保留。原始每轮／每节点径向、周长、raw角、display角、floor残差和原始位置／末态保存；所有数值finite，96轮上限及径向／角／原周长位置行移动限通过。最大实际行移动 `.002506738406536002 p`，小于原上限约 `.0136 p`。最终候选末态 SHA256 `9C5EAEA7CAFD73C53B608FA65B779741E3C06A4831BA14F446FF362546040432`。

本夹具实际8个径向候选、12个角行、192个active sets、0个奇异子集、17次径向／角位置修正。最终带观测I/O运行的QPC时长：原算法约7.8454 ms，候选约.9469 ms；两进程OS CPU各31.25 ms，含启动且粒度较粗。不能由此推断纯solver加速倍数、每行成本不变或正式服务器性能；active-set确实增加每个角行的计算量。该记录是测量，未为测量增加求解预算或扫描参数。

## 3. prescribed完整轨迹与反例

最终候选 DLL：`artifacts/p1-r1/native-build/Release/dango_physics_module.dll`，SHA256 `59EC861EE748BA93E661D058EEBD01D3E8A04ABF9D24F0376BACB4D98015B6DC`。最终只补局部周长位移观测后重建，DLL hash仍完全相同，九场景及回归对应实际最终binary。

| 场景，各30／60／144Hz、20Hz输入 | 每档实际步数 | 实际最大SAT侵入比 | 最大native core误差 | 结果 |
|---|---:|---:|---:|---|
| 推挤 | 600 | `.0300072974` | `4.77303e-6 p` | 3／3通过 |
| 堆叠 | 600 | `.0300103081` | `6.37699e-6 p` | 3／3通过，每档246个实际支撑步 |
| 撤下层 | 600 | `.0300103081` | `6.37699e-6 p` | 3／3通过，每档150个实际支撑步 |

最大支撑正gap均0；保留原 `.031`、`.008 p` 和 `1e-5 p` 门，不偏移renderer／中心、不加固定缝隙、不改团子尺寸／弹性／参数。原面积、速度、交叉、锐刺、共同采样相位和完整轨迹门保持。

withdraw三档都实际在Tick181移除下层；幸存native entity不重建，旧start／end support及pair被清除，之后自然下降并落地。开始撤除时core高度 `.9159705639 p`，最终 `−.1238268241 p`；最后actual ground contact为正。九场景9／9、零跳过，证据 `semantic-r1/`、`trajectory-summary.json`及`test-results/semantic-r1.trx`。

合同／绑定／呈现反例17／17、零跳过。故障锁存新反例前三步合法，之后每步最多`.05 p`、Hermite速度低于4.51 p/s，在Tick48、第10子步真实native `−91`失败，保留Tick47合法帧，下一步拒绝；零速度深重叠也实际进入16子步后`−422`，不能以预检错误当成功。假远离pair、旧epoch／revision／phase、侧触与分离继续通过原门。

首次锁存新夹具尝试瞬间移动较远，实际被原速度上限预检以`−410`拒绝，没进入解算，故断言红灯（16通过／1失败）。证据 `contracts-counterexamples.log`／`.trx`、`impossible-after-legal-prefix.json`保留。核实根因后仅修正测试输入，使用上述逐步下降夹具；物理代码及断言没有放宽，最终证据 `contracts-counterexamples-final.log`／`.trx`、`below-floor-after-legal-prefix.json`。这是夹具修正，不把原失败改写为通过。

native prescribed probe正常36104 checks／0失败；动态core变体7失败、额外能量变体2失败，实际exit1。接触正例26次能量检查、最大超界0；额外能量反例实际超界约`.239985141`。进程全部退出，三者AllocationDelta均0。

## 4. 原行为与资源回归

- 25项managed原行为／坐标／owner／容量检查通过，零跳过；authority三种1800步轨迹与冻结authority DLL逐位相同；legacy单／双／三体各900步完整public状态与冻结A DLL逐位相同。没有用同一候选复制件作baseline。
- 原authority native 3721 checks／0失败；三错误变体219／600／1失败，实际exit1。公开pose冷恢复仍5／5首个续步发散，完整checkpoint仍unsupported，未改写原恢复证据。
- 旧自身约束代数与三个各36000帧native场景通过；spring balance通过；平台支撑边界／lastsafe／reset／分配失败0；球1037 cases、244104 checks／0失败；家具集合1507 checks／0失败，两破坏变体均实际exit1。
- 使用原独立headless consumer载入新DLL，推挤／堆叠／空中各1200步及生命周期／并发取消通过，native bytes `0→0`，实际进程退出。此为隔离技术检查，不代表实际在线服务器或多人客户端。

native回归第一次顺序读取stdout／stderr，预期失败变体的stderr管道阻塞；核对路径和父子PID后只停止该任务capture与阻塞child，保留已完成的authority正常结果和`interrupted-log-capture.json`。改为同时异步读取两管道，余下10个实际程序全部完成；中断进程不计通过，未更改算法／测试门。

局部三批共21个正常完成进程、prescribed probes3个、native原回归11个、headless1个；实际PID、退出码、CPU与退出事实记录各manifest。中断native child另列。managed各TRX分别保存。最终任务native／testhost／headless进程和监听检查见 `final-source-and-resources.json`；共享.NET辅助进程和既有家具候选未操作，ignored实验数据保留。

Box2D全部233文件hash验证通过；A／authority／日常DLL及B EXE仍为合同冻结hash。Windows x64 Release native与local工具构建0 warning／error；PowerShell语法和`git diff --check`通过。本轮文件和相对链接检查全部通过；任务进程／监听均0。任务源码／构建及Git状态在最终manifest归档，共享辅助进程和既有家具窗口未操作。

## 5. 交付边界

本地隔离R1技术验证完成；本轮仅在阶段分支本地提交。没有main合并、推送、生产DLL替换、日常安装、部署、Linux环境启动、正式服务／数据库或UI窗口操作。

尚缺：已批准world丢失／释放后安全出生语义的实现及门禁、Linux平台、真实呈现窗口／多客户端、实际正式接线和人工双机验收；恢复历史预算失败仍是原合同结果。下一步应冻结恢复实现范围并补齐P1剩余门禁，随后重新判断P2准入，不能由本轮9条headless绿灯直接宣布多人可玩。
