# 圆形材料：固定接触末态候选结果与复核

2026-10-10。对应[已批准方案](../planning/2026-10-10-round-material-contact-solver-candidate.md)第 2／3／5 项。**独立夹具通过，真实私有候选失败，停止于设计复核。** 不标记为新物理或人工验收通过。

## 固定实现及夹具

`dango_material_contact_solver.h` 在合法固定几何上建立 128 条形状边界、16 条地面法向、16 条切向行，17 个动态体；几何门槛沿用原值。速度门槛为相对长度闭合／互补 `1e-5 /s` 与角度闭合／互补 `1e-5 rad/s`，不混合单位。固定最多 384 次交替逐行扫描。

Box2D 的法向／切向累计冲量已经影响输入速度，候选只施加新增差量；摩擦预算为实际混合系数乘以已有法向加新增法向。`totalNormalImpulse` 只作遥测。实际混合运算为 float 乘法及 `sqrtf` 后转 double，静置系数 `3.3941125869750977`。入口只接受声明的 8 FLT 舍入范围内实际 receipt 锥超额，该测得的不可变数值余量随本小步保留，不能加入通用额外摩擦预算。

double 和 float 发布分别检查原几何、所有速度行、互补、摩擦锥、速度耗能、质量加权动量／角动量及逐体冲量重构。float 速度／互补／锥失败只能在原 384 上限内继续；能量／账本失败立即停止。现有配置不会产生相等的上下限，本候选不支持这种退化配置，不能声称兼容全部生产配置。

接触耗散投影可能增加某个点的速度，原统一正比例限速在之后单独执行。其验证重算缩放后速度行，检查准确的原 float 缩放公式；限速能量／动量变化独立记账。`cap_report.final_rows` 的速度指标对应缩放后状态，接触账本指标对应未限速接触输出，二者不得混读。

独立固定夹具最终通过 **1,252 项检查、28 个正例、30 个拒绝负例**：梯度、混合边界、真实 receipt、摩擦撤回、失去支撑、不同质量、纯内力账本、float 发布、统一限速、预算耗尽／输出保持、非法输入与退化、账本报告可用性。日志为 `contact-solver-fixtures-final-v4.log`；前一版 1,208 项及后续报告保留。梯度最大误差 `2.30594712769e-9`。

## 真实私有连接与失败

`dango_material_contact_adapter.h` 的顺序为：真实世界仅推进一次 → 容量完整读取接触 → 原 POSITION 修正至多 384 次 → 在副本执行原旋转／平移耗散各一次 → 新速度裁定至多 384 次 → 原统一限速与独立验证 → 合法候选批量写回。该私有路径跳过旧 solve／damp／limit 整块，不重复执行。容量限定一个团子、16 外围节点；默认无宏模块仍走原代码。

默认原计划 36,000 帧；正向及镜像原计划分别 6,000 轨迹帧加同一世界 36,000 零输入帧。三者均在尝试第 29 帧时失败，只完成 28 个合法帧。轨迹在第 600 帧后才启用拖动，因此当前失败发生在初始自然落地，不能称已执行甩动后长时门槛。

| 失败项 | 实际结果 |
|---|---:|
| 错误／阶段 | 锁存 `-96`／阶段 4 速度裁定 |
| 总小步 | 460（第 29 帧第 12 小步） |
| 原位置迭代／几何残差 | 21／`9.131152182817459e-6`，通过 |
| 速度扫描／活动行访问 | 384／3,072 |
| 活动行 | 8：节点 11／12 的径向下限、原始角下限、地面法向、切向 |
| 最大角度闭合 | `2.6105164769006888e-5 rad/s`，失败 |
| 最大角度互补开离 | `1.5575768560355368e-5 rad/s`，失败 |
| 最大相对长度闭合 | `5.57973391419865e-6 /s` |
| 接触状态 | 摩擦／锥合法，未到限速／写回阶段 |

源内诊断与实际 DLL 都复现此故障，最后合法 42 个 float 逐位一致。实际 post-drag 报告区分 `AttemptedWorldFrame=29`、会计数失败调用的 `SessionReportedFrames=29`、`CompletedDragFrames=28`、`CompletedIdleFrames=0`。无采样产生的零跨度不是通过。

初版阶段 4 的未生成输出与未计算账本是零结构；v2 改为 `uncapped/output/speedCapReport/finalSpeedScale=null`、`ledgerEvaluated=false` 及账本数值 `null`；v3／v4 进一步验证非法输入及几何失败复用报告不会保留旧可用性标志。v2／v3／v4 输入、几何、阻尼、全部行、double 速度和失败残差与初版完全一致；实际 DLL 报告除文件哈希及实际耗时外全部相同。该差异只说明报告可用性，未改变物理候选。

## 独立复核：可行性及收敛

只读数学复核对捕获的八行构造质量加权 Gram 矩阵 `J W Jᵀ`。矩阵正定，最小／最大特征值 `0.7138395725`／`9799.678914`，原始条件数约 13,728（依赖行单位与缩放）；对角标准化后条件数约 336.9。固定 384 次独立重放与原生结果的速度差最大 `4.94e-17`、乘子差最大 `6.94e-18`；全部行梯度重算差为零。位置阶段不改速度，阻尼重算逐 float 一致，未发现重复阻尼或重复接触入账。

仅作为夹具诊断，直接解这个八行线性系统可得到全部六个单边乘子为正、两个切向严格在锥内部的速度解；最大行残差 `4.84e-15`，动能由 `0.06171195385` 降至 `0.04358242274`。转 float 后现有完整 validator 和原限速 validator 通过，所有 160 行、逐体冲量及动量账本均检查；最大速度约 0.84637。其余 152 行未新增超门槛闭合或触边违反。

交替两次扫描的局部误差传播谱半径约 `0.9703762544`，与第 382→384 次真实角度残差比吻合；慢模态包含左右径向／地面反力的反对称分配。**当前证据说明固定逐行算法在预算内未收敛，未说明无可行速度解；也不证明原材料小样的长时漂移只由此引起。** 诊断直接解没有接入 DLL，不能代替完整轨迹或新方案验收。

## 失败保持与显式恢复

真实自然故障的集成测试通过 315 项检查。最后合法 42-float 快照保持；失败小步的 17 体原始状态仍为世界已推进后的 adapter 输入，副本位置／阻尼／速度修正未写回。世界和 Box2D 缓存并未回滚到第 28 帧。

故障后移动、下压／跳跃、拖动三类主动输入保持原始身份、位置／速度、快照、计数及诊断。显式 reset 清错误、接触计数和私有诊断，恢复初始快照及 28 个合法帧，第 29 帧再次同类故障。它证明恢复契约，并未修复候选物理。测试世界已销毁。

## 交付层级与后续

- 静态：相关 Release `/W4 /WX` 构建通过；默认观察器无宏预处理与旧代码完全一致，详情见[捕获记录](2026-10-10-round-material-contact-capture.md)。
- 集成：独立数学／负例和真实锁存测试通过；默认无宏模块限定兼容回归 11／11（含与认可模块 900 个实际帧逐位相同及容量 1／2／3）。这不是全部物理回归。
- 实际 DLL：第 29 帧故障已复现。完整默认长时及两向甩动后长时未完成，候选失败。
- 实时／人工：九组大小／软度、飞行回圆／恢复、新候选 35＋35、WPF 新旧手感验收受阻，未验证。此前模式 7 的通过记录不转移到新候选。
- Git／部署：仅本隔离分支保存实验；未合并、推送、安装、替换正式 DLL 或部署。

依已批准计划第 5 项，保留固定预算与门槛，停止扩大迭代／参数扫描。后续[有限活动块方案](../planning/2026-10-10-round-material-contact-block-candidate.md)待用户批准；可行性诊断不意味着下一方案必然有效。

## 本地证据

证据均在忽略的 `artifacts/round-material/`，旧报告保留：

- `contact-solver-fixtures-step1.log`、`contact-solver-fixtures-step2-metadata.log`、`contact-solver-fixtures-final.log`、`contact-solver-fixtures-final-v4.log`；`contact-candidate-latch-step1.log`、`contact-candidate-latch-final.log`、`contact-candidate-latch-final-v4.log`。
- `contact-candidate-native-{default,forward,mirror}.json`、`contact-candidate-native-v{2,3,4}-{default,forward,mirror}.json`；完整 17 体及 160 行在首故障报告中。
- `contact-candidate-actual-default.json`、`contact-candidate-actual-post-drag.json`、`contact-candidate-actual-v{2,3,4}-post-drag.json`；`contact-candidate-v{2,3,4}-metadata-equivalence.json`。
- `contact-candidate-math-review-step1.json`、`contact-candidate-direct-kkt-validator-step1.log`；后者只验证捕获夹具，不含真实世界重放。
- `test-results/contact-default-scoped-module-tests.trx`、`test-results/contact-default-final-module-tests.trx`。

生产 `dango_box2d.dll` SHA256 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`；认可隔离模块仍为 `A4600B47CD198B1F1A735EE9FCF0C0DD81DD544C715305BC52A2176D3BE7F553`。最终 v4 私有测试 DLL 为 `6E5D835A1297616F54743A4BBA946EDF5F5258B4AA9B336BF2BD99587EA6BF6D`，此前各轮哈希保留于同名实际 DLL 报告；后三轮仅补充诊断元数据，实际故障与最后合法输出一致。没有残留本轮诊断进程。
