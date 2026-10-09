# 圆形自然态与材料小样

2026-10-09～10。范围：用户同意继续验证“所有团子的自然形状为圆形，落地形态由物性产生”。本轮交付独立 Windows 单团子对照小样，生产捏脸、设置、物理默认值和 DLL 均不切换。完整材料模型、多人配置、持久化和生产接入尚未获本轮验证。

## 模型和操作

- 左侧：当前 `DangoPhysicsDefaults.Current` 原参数，以及正式身体绘制的 72:58 坐标比例。首版虽然使用原物理，却抵消了正式绘制比例，不能作为原外观对照；本轮已修正。
- 右侧：关闭 `TopInwardForce` 和 `GroundDeformationStrength`；保留现有径向／外围弹簧、面积保持、防交叉与地面接触。两侧分别拥有独立单体物理世界，不表示共享世界或多人同步。
- 所有参数都用于真实 native 模拟。拖动抓取节点，松开后移除拖动力；同时跳跃、持续下压、正常下落和高处自由落体均使用真实物理输入。
- 高处自由落体使用 `Add(0, 8)`；地面不移动、不关闭重力，镜头只改变显示原点。
- 大小 80%～120%：实际静止长度、节点半径及排斥距离随尺寸缩放，面积下限和质量随尺寸平方缩放。恒定二维密度是小样假设，不制定生产重量或跳跃规则。
- 柔软度 0～100：径向／外围频率分别映射为原值的 0.55／0.90（硬）、0.35／0.75（默认）、0.25／0.55（软），区间线性插值。默认频率为 1.82／15.075，面积保持仍为 4.8。既有长度与角度硬限制保留；默认落地底部径向压缩会触及 0.65 下限，不声称完全由无限制弹簧决定形态。
- 晃动抑制 0～100：两组弹簧阻尼比 0.5～2.0，默认 90 对应原值 1.85。它改变恢复过程，不宣称数值越高越快回圆。
- 参数修改是草稿；“应用参数并下落”显式重建两个对照世界。暂停／失焦清空输入，错误停止并显示诊断，只能显式重新下落。

显式传入 `--baseline-library` 时，左侧实际加载指定基线 DLL，右侧实际加载首个候选 DLL；仅传一个 DLL 时，两侧都加载该 DLL，只比较原默认参数和材料参数。程序不按文件名识别算法，也不自动查找旧 DLL。当前已认可的小样使用旧隔离 DLL；下述新材料试验没有切换该人工验收基线。

身体复用生产 `DangoStreamGeometryFactory` 的轮廓校验和样条。左侧每物理单位对应 X=105、Y=105×58/72 像素，保留正式身体比例；右侧两轴均为 105 像素，使自由飞行时的可见身体接近圆形。地面、镜头、骨架和拖动逆映射均使用本侧比例。轮廓先按配置体型转换到默认尺寸坐标，再补偿画布尺寸，保证真实大小不被缩小；超出表现坐标范围时保留旧等比例处理。真实 native 甩动回归另行检查径向硬上限，不能靠表现坐标归一化掩盖超限。

眼睛和芽复用正式基础资产、保持装饰等比例，用当前绘制轮廓边界定位；身体颜色取参考图的 #F5D5A5。装饰仅用于外形对照，未接入完整生产附件运动或捏脸挂点链，不证明生产捏脸兼容。

## 预先固定的验收

1. 小／中／大 × 软／中／硬共九组，检查所有帧成功、16 节点、有限坐标、面积和速度安全；正常下落 10 秒内接地，末 2 秒速度小于 0.03，核心与宽高漂移小于 0.003，轮廓穿地容差 0.002。
2. 高处自由飞行连续后 1 秒径向差不超过 4%、骨架及实际样条显示宽高比偏差不超过 4%、几何圆度至少 0.975。圆心通过最小二乘拟合；节点分布不均不能被误当作非圆。九组正常下落每帧均检查实际绘制合法。
3. 九组都用实际拖动力先产生至少 10% 径向差；松开后 1.5 秒内恢复近圆并维持至少 0.3 秒，全程无接触。另从默认参考落地形态实际提起、松开，使用同样回圆门禁。禁止把重新生成的圆形当作恢复。
4. 尺寸实际直径比例达到 0.8／1.2，容差 0.01；软硬端点的落地宽高比差至少 0.03或高度差至少自然直径的 3%；阻尼端点实际恢复轨迹径向差最大差至少 0.01。
5. 圆度纯指标测试接受不均匀采样的圆，并拒绝横向放大 20% 的椭圆。这两份是明确标注的合成度量夹具，不冒充物理模拟。
6. 默认参考图身体宽高比 331/221，容差 0.03；最宽位置在从顶向下 60%～70%；高度 10%、25%、50%、65%、80%、90%、97% 的归一化宽度为 0.5438、0.7915、0.9728、1、0.9366、0.7915、0.5619，平均误差不超过 0.04、最大误差不超过 0.08；底部最后 2.3% 高度的覆盖宽度为总宽的 43%～57%。默认落地末 120 帧逐帧匹配。正例接受正式原外观；负例拒绝抵消比例后的原物理轮廓，以及只有宽高比相近的对称椭圆。此组阈值在参数扫描前固定。
7. 默认候选连续 36,000 帧（10 分钟模拟时间），每帧检查健康及穿地；10 秒停稳后每秒检查接地、速度、参考外形，并对整段核心和宽高跨度要求小于 0.003；末 600 帧逐帧采样。首版只检查末段漂移，本轮补充整段采样以发现慢形变。加速运行不等于 10 分钟真实桌面运行。

用户已认可默认落地外形，并在甩动修复后回复“通过，下一步”，甩动修复人工验收通过。本轮不声称全滑块组合、极端鼠标操作、长期真实桌面或多体接触已通过，长期漂移门禁仍失败，整个物理阶段尚未验收。

本轮实际结果：Release 构建零警告、零错误；新自动门禁 **34/35，长期漂移失败**，因此候选待修改，不标记已验收。九组实际拖动后回圆约 0.32～0.87 秒；参考落地形态实际提起、松开后约 0.60 秒回圆。默认第 10 秒实际样条宽高比约 1.489（正式原版约 1.498），截面平均／最大误差约 0.0143／0.0382，最宽位置 61%，底部覆盖 55.39%。同尺寸软硬端点的实际落地形态差通过。

默认连续 36,000 帧健康、接地停稳、外形匹配通过，但 10 秒后整段宽高最大跨度 0.027926，超过 0.003；宽高比缓慢变至约 1.523。该量是节点加外沿半径后的轮廓控制点高度跨度，按 105 像素／单位换算约 2.93 像素，实际 WPF 样条高度／宽度跨度分别为 1.488／1.665 像素，应区分这两个量。有限扫描径向／外围频率、局部角度平衡及阻尼均未消除慢形变；扫描不替代验收，也不自动切换小样配置。外形校准保留当前参考形状较好的默认候选，没有放宽阈值或增加形状补偿力；之后的甩动修复只增加有界约束求解预算，见下节。

外形校准时只有弹簧、面积径向压力和触限位置／速度投影耦合的线索，尚未统计各项净力或约束修正，也没有对原版应用相同整段门槛。用户随后批准甩动修复并要求下一步，本轮补齐有边界的静置诊断，结果见下节；不能继续无界寻参或把未定位的机制当作根因。

已打开真实窗口，通过 UI Automation 验证三滑块、未应用草稿提示、应用后实际形态变化、恢复默认和高处下落，并查看真实物理帧离屏快照。自动操作不作为用户手感验收；本小样尚不满足生产接入门禁。

## 甩动冻结修复

用户报告“通过，但是甩动的时候卡住了”。旧窗口真实诊断为右侧候选 `-91`，约束求解失败锁存；窗口仍响应操作，但两个对照面板均停止。稳定复现使用先停稳 600 帧、再反复横向及上下甩动的 `ground-swing` 输入，默认候选在第 764 帧冻结。旧 96 次投影后残差为 1.1973083e-5，略超原有 1e-5 门槛；继续同一投影到第 101 次即可满足门槛。

本修复将最大投影预算从 96 增至 384，保留原精度、提前退出、形状硬限制和真实故障锁存，不自动重建世界。正常已收敛帧结果保持一致；困难帧最坏求解预算增至四倍。原生测试加入真实失败输入夹具，并检查收敛及动能未增加。两侧显示和拖动共用连续镜头转换，拖动期间固定镜头，防止身体运动反馈到鼠标目标；松手、失焦、暂停和捕获丢失均解除固定。拖出面板会被裁剪，松手后镜头恢复跟随可能重新定位；用户已通过此版甩动复测。

扩展尺寸回归还发现 120% 合法身体被生产绘制校验的默认尺寸绝对阈值误判为 `FootLikeSpike`。现在按配置体型校验、同比补偿绘制大小，生产校验器保持不变。三个尺寸的合成正例都按真实比例绘制，真正 canonical 尖脚负例都被拒绝；它们明确属于绘制夹具。真实轨迹每帧独立检查径向硬上限加节点外沿，容差为 2e-5（求解容差及浮点误差）；最大 canonical 半径约 0.874607，合法上限为 0.8746。

实际验证：

- Release 构建零警告、零错误；`git diff --check` 通过。
- `--verify-drag` **35/35**：3 个尺寸绘制正反例、2 个左右镜头坐标检查、30 组 native 轨迹。三种轨迹各含原版默认及大小 80／100／120 × 柔软度 0／50／100，晃动抑制使用默认 90。每组先停稳 600 帧，再甩动／间歇释放 5400 帧，再释放 600 帧、重新施加方向控制 60 帧，全程健康、绘制合法、无冻结且再次响应。不是硬件鼠标手感验收。
- 原生约束夹具、退化拒绝及三组 36,000 帧基线检查通过；弹簧平衡四组通过。模块回归 **11/11**，包含原版默认 900 帧逐帧兼容和容量 1／2／3。
- 原外形门禁重新运行仍为 **34/35**：参考外形、近圆、恢复等通过；长期漂移仍为 0.027926，未解决、未放宽。
- 新真实窗口 UI Automation 验证滑块／草稿／应用／恢复默认／高处下落通过；旧冻结小样正常关闭；用户随后确认新版甩动通过。此人工认可不替代长期漂移门禁。

失败证据保留在 `drag-before.json` 和 `drag-after-diagnostic.json`；当前结果为 `drag-after-final.json`、`verification-drag-final.json`、`native-drag-constraints.log`、`test-results/drag-module-tests.trx`、`ui-verification-drag-fixed.json` 和 `comparison-drag-fixed.png`（均位于忽略的 `artifacts/round-material/`）。当前隔离 DLL 为 `native-drag-final/Release/dango_physics_module.dll`，SHA256 为 `A4600B47CD198B1F1A735EE9FCF0C0DD81DD544C715305BC52A2176D3BE7F553`。正式 DLL SHA256 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。只在小样分支本地提交，不合并、推送或更新日常安装。

## 长期静置诊断

本轮仅增加诊断工具和记录，不改变已人工通过的小样力学。`--diagnose-idle` 使用实际 DLL 分别跑原版和候选各 36,000 帧，逐帧检查健康、绘制和穿地，保留同一漂移采样集合，以及第 600／1800／3600／7200／18000／36000 帧完整轮廓。参考失配、停稳或跨度失败仍记录，并继续跑完整段；原生或健康失败仍停止对应世界。退出 0 表示诊断完整执行，不代表稳定门禁通过。

实际步长为 `166666 ticks`，managed 为 0.0166666 秒，native float 的精确 double 值为 0.016666600480675697；因此 36,000 帧约为 599.9976 秒。两组均完整运行，每帧实际绘制合法，零穿地、零参考失配、零停稳失配；每组保留 1180 个既有门槛样本，跨度均失败：

| 对照 | 核心 X 跨度 | 核心 Y 跨度 | 外沿控制点宽度跨度 | 外沿控制点高度跨度 | 观察 |
|---|---:|---:|---:|---:|---|
| 原版默认 | 0.270553 | 0.000070 | 0.000009 | 0.000066 | 外形稳定，约 28.41 像素横滑 |
| 材料默认 | 0.020076 | 0.022334 | 0.020564 | 0.027926 | 10～30 秒继续压扁，随后形状趋稳，仍有慢横移 |

候选实际 WPF 样条宽高比在 10／30／600 秒分别约为 1.48917／1.52249／1.52253。原版第一次超过跨度门槛在第 961 帧，候选在第 781 帧；因此不能把“速度较小”当作形状已经收敛，也不能把所有问题归于柔软度。

独立 `dango_material_idle_diagnostics` exe 编译现有 native 源码并包装实际模块的 create／add／step 调用链，逐小步记录七类显式力的合力、质量质心／core 力矩、功率和绝对施力量，统计 Box2D 积分、硬约束、地面阻尼及限速前后的状态修正，每秒聚合，六个检查点保存完整节点快照。它没有在原 DLL 中安装探针。两组 × 六帧 × 42 项，共 504 项按 float 位逐项一致；四个跨度分量也与 managed 报告完全相同，证明已核对的基线轨迹未被探针改变。每外层帧实际有 16 个小步；求解 sweep 计数含修正和校验，通常为迭代数的两倍。

确证的机制：面积项的单位径向压力不保证合力为零；局部角度平衡虽合力为零，却可能有净力矩；径向长度范围每小步重设会清空 Box2D 累计冲量。积分与硬约束长期存在相反的垂直修正，地面阻尼只改速度，不撤销已经发生的位置变化。这些是实际测量或源码事实，尚未证明其中单项就是慢形变的充分根因。

只在独立 exe 中做三个单变量对照，各 36,000 帧，保留精度及原门槛，均无原生错误锁存：

| 诊断改动 | 最大跨度 | 六个检查点参考外形 | 结论 |
|---|---:|---|---|
| 面积压力补 core 等量反力 | 0.027834 | 全匹配 | 合力已降至浮点噪声，慢松弛仍存在 |
| 关闭局部角度平衡力 | 0.037475 | 全失配 | 稳定性更差且破坏已认可外形 |
| 参数未变时跳过径向范围重设 | 0.028810 | 全匹配 | 改变松弛过程，仍不过原门槛 |

三个变体均拒绝作为修复通过，不切换当前候选。`--measure-idle-probe` 从真实 42-float 帧重建 WPF 样条，区分外形失配与跨度失败；六帧测量不能代替全程门禁。原生报告的 displayWidth／Height 是外沿控制点包络，也不能当作 WPF 样条边界。

实际结果文件为 `idle-before.json`、`idle-native-baseline.json`、`idle-native-area-reaction.json`、`idle-native-no-local-balance.json`、`idle-native-cached-range.json` 和 `idle-render-*-v2.json`，均在忽略目录 `artifacts/round-material/`。Release 构建及差异检查通过；managed、native 诊断和绘制测量均验证了拒绝覆写既有报告，原报告 hash 不变。所有诊断进程已结束，没有端口或持久数据操作，当前及正式 DLL hash 不变。此次没有修复长期漂移，也没有关闭物理阶段。

用户批准下一步后，先实施[候选方案](../../../docs/planning/2026-10-09-round-material-stability-candidate.md)第 1 项独立算法夹具，见下节。新力尚未接入物理候选；继续保留现有扁形、近圆、松手恢复、甩动及长期稳定门槛，不能以冻结身体或增加落地塑形力补偿失败。

## 独立材料算法夹具

`dango_material_forces.h/.c` 仅由新增 `dango_material_force_tests` 可执行目标编译；没有接入 bridge、正式 DLL 或已通过的小样 DLL。它计算 double 精度的候选材料能量与负梯度，当前只提供数学证据。

- 面积压缩：`E_A = 0.5 × s / (A0 × g0) × max(A0-A,0)^2`，`g0 = r × sin(2π/n)`；`A` 是 CCW 多边形面积，`r` 为固定自然半径。梯度使用 `.5 × (y_next-y_prev, x_prev-x_next)`，不给 core 追加补偿力。`s` 的单位为力，能量单位为力乘长度；自然正多边形的梯度幅值用 `g0` 归一化，在压缩正多边形半径 `R` 下节点力为旧压力的 `R/r` 倍，并不精确复刻原算法。
- 弯曲：`E_B = 0.5 × k × Σ(t_next-t_i)^2`，`t_i = atan2(cross(e,f),dot(e,f))` 是有符号转角，使用原始相邻转角差。`k` 为能量系数，尚未映射到小样的原局部角度力强度。力包含角度对三节点位置的全部梯度。
- 每个材料项的整组力按同一比例限幅。限幅后验证合力／力矩闭合，不声称仍是原能量梯度，也不要求含阻尼系统总机械能恒定。
- 输入必须为简单 CCW 轮廓，允许温和凹角；节点数 3～64。短边小于等于包围盒对角线的 `1e-10`，面积小于等于该尺度平方的 `1e-12`、反向转角距 `±π` 小于等于 `1e-10` 弧度，以及自交／自触均明确拒绝。反向角 cut 只由弯曲项检查。非法、非有限或数值表示范围失败返回错误码、能量为 NaN、有效非别名输出力清零；调用方不得将其当作零力成功。全部输入／输出存储（含标量）须互不重叠。限幅接口允许 1～64 个力，失败时保留原组并返回 NaN 比例。

判据在实现前写入候选计划：合力／力矩相对门槛 `1e-11` 加 double 舍入底限；梯度每分量误差不超过 `1e-8 + 3e-7 × max(|解析值|, |数值差分|)`。Release `/W4 /WX` 构建零警告、零错误；正常套件退出 0。实际覆盖：

- 12 种非均匀／不对称／温和凹形轮廓（大小 80／100／120%，原位置及平移旋转版本），分别检查面积和弯曲，共 24 组主要夹具。独立公式核对能量，再检查负梯度差分及合力／力矩。最大记录归一化合力为 `7.64279199e-17`，力矩为 `4.21918762e-17`；最大梯度绝对差为 `1.7510331e-8`，逐分量通过上述合成门槛。
- 平移／旋转及自然尺度协变；3／8／16／64 节点正多边形；未压缩与面积激活边界；零刚度；非法参数、空指针、直接别名、非有限、短边、零面积、反向绕序、自交、自触、反向角 cut；极值面积系数及真实面积下溢均拒绝，失败输出满足约定。
- 整组限幅的零／启用／未触限模式，共同尺度、节点力上限及闭合全部通过；一般 1／2／3／16／64 个力组和错误后原组不变均通过。能量梯度只验证未限幅力。
- 四个负例单独实测退出 1，正常套件也确认拒绝。旧面积／角度负例直接编译并调用未改动 bridge 的实际静态函数，截获施力而不步进世界；旧面积合力为 `2.1038613`（相对总施力 `0.0641478739`），旧角度合力为 0、节点均值处力矩为 `0.0157821182`（相对施力乘轮廓尺度 `0.00510727556`），拒绝量远大于 float 舍入噪声。逐点独立限幅和反向梯度也被拒绝。

首轮测试误把只属于弯曲的反向角 cut 拒绝条件用于面积项，因此失败；修正为该简单 CCW 轮廓面积成功／弯曲失败，并新增真正的非相邻自触负例，没有改动门槛。自审还补充了非空输出时的独立非法节点数检查，避免空指针错误掩盖缺失的节点数校验。首轮失败记录为 `material-forces-step1-first-run.log`；最终正常结果为 `material-forces-step1-final-v2.log`；四个真实失败模式为 `material-forces-step1-negative-{area,bending,point-cap,sign}.log`，均在忽略的 `artifacts/round-material/`，保存时拒绝覆写。独立只读审查通过。

此项完成算法夹具验证，当时未运行新材料的实际世界轨迹、WPF 绘制、甩动或人工验收。旧长期稳定门禁仍失败，两份受保护 DLL hash 保持上文值；已认可小样外形和操作没有切换。没有端口、持久数据操作或残留测试进程。用户随后授权持续推进至需要人工协助／验收，第 2 项先单独替换面积力，再运行弯曲及明确标注的组合，见下节；夹具通过不证明漂移已修复。

## 隔离材料实际试验

新材料只通过私有编译定义 `DANGO_MATERIAL_EXPERIMENT_MODE` 启用：`dango_material_area`、`dango_material_bending`、`dango_material_both` 分别替换面积项、局部角度平衡项和两项组合。默认 `dango_physics_module` 编译仍走旧算法，没有新增 ABI 或生产配置字段，也不按柔软度偷偷切换。面积系数沿用 4.8；弯曲系数固定为 `k = S × r × tan(π/n) / 2`，默认约 0.11497135，只匹配规则自然环的旧中心径向贡献，不称完整算子等价。新材料计算先使用 double、整组共同限幅，再验证全部 float 输出后施力。

实际面积私有 DLL 与已认可隔离 DLL 使用双源入口运行 `--diagnose-idle`。报告中候选为 `dango_material_area.dll`（SHA256 `3A9B7CE8DF1C15BB7960FDAAF5A44AF1B092F26FEDD55DCBFDE1794D1FCDFC49`），原参数对照实际加载上文 `A4600B…F553` 基线。两组各完整执行 36,000 帧，逐帧绘制、健康及穿地通过；原版跨度仍为 0.270553，材料跨度为 0.0287595，原采样集合出现 52 个参考失配样本，首次在第 601 帧，跨度首次失败在第 3181 帧。因此面积替换没有通过。此报告为 `idle-actual-area-gradient.json`；独立源码诊断的默认算法回归另核对了 504 个 float 检查点值和 8 个跨度值，全部按位一致，见 `gradient-baseline-equivalence.json`。

五个固定原生试验各执行 36,000 帧，无错误或锁存。跨度沿用原采样集合及 0.003 门槛；WPF 列仅测量 10／30／60／120／300／600 秒六帧真实快照，不替代全帧门禁：

| 固定试验 | 最大跨度 | 六帧 WPF 参考外形 | 结论 |
|---|---:|---|---|
| 面积梯度 | 0.0287595 | 前三帧失配，后三帧匹配 | 拒绝 |
| 弯曲梯度 | 0.0277615 | 全匹配 | 跨度失败，拒绝 |
| 两梯度组合 | 0.0286142 | 前三帧失配，后三帧匹配 | 拒绝 |
| 两梯度＋跳过未变径向范围重设 | 0.0282055 | 前三帧失配，后三帧匹配 | 拒绝 |
| 两梯度＋自然圆环半节点初相位 | 0.000765890 | 全匹配 | 默认静置通过，镜像甩动后长时漂移失败 |

前三项实际 float 施力的内部合力／力矩降至浮点噪声：两梯度组合的最大一秒平均合力分别约 `4.72e-9`／`5.70e-9`，力矩约 `1.30e-9`／`2.42e-9`。这是每秒聚合的平均量，不是瞬时最大值，也没有证明长期稳定。面积和两梯度组合的早期核心 Y 约 -0.233，后期约 -0.256414；节点坐标对应由一个底节点支撑转到两个相邻底节点支撑、径向下限与底边上限共同触限的几何分支。触地记录是投影后的几何接近统计，不等于实际接触冲量或完整支撑力证据。缓存范围试验仍发生转换，因此冲量清零不是已证实的充分根因。

半节点试验只将自然圆环初相位由 0 改为 `π/16`，仍为等半径自然圆；使用未缓存的两梯度组合，不修改其他参数或采样起点、不写入接地轮廓。六帧 WPF 宽高比为 1.479962～1.479966，实际尺寸约 174.939 × 118.205 像素，最宽位置 62%，截面平均／最大误差约 0.01529／0.03645，底部覆盖约 55.74%，均通过原外形标准。随后实际 `dango_material_both_phase.dll`（SHA256 `80A7883D14FE3CB7BE9820BFB344F958C245FD3F0A08F8B5EBF18D96FE0C2DE1`）与旧隔离基线双源跑完整默认静置：候选 36,000 帧逐帧健康、绘制、穿地通过，原采样参考与停稳失配均为 0，跨度仍为 0.000765890；原版跨度失败继续保留。该报告为 `idle-actual-material-gradients-half-phase.json`。完整原小样门禁及甩动矩阵随后分别通过 **35/35**、**35/35**，报告为 `verification-material-gradients-half-phase.json` 与 `drag-material-gradients-half-phase.json`；这两套既有门禁没有覆盖甩动后 600 秒静置，不能由此认定任意落地取向长期稳定。

新固定否证使用默认大小 100、柔软度 50、晃动抑制 90，分别运行原 `ground-swing` 与仅反转 X 目标的镜像轨迹。每条先运行 6,000 帧真实甩动轨迹，再在同一世界、同一实体上全输入为零继续 36,000 帧；不重建、不重置、不修正姿态，保留静置第 10 秒后的原 1,180 个门槛样本。两条均完整执行，逐帧绘制及穿地失败为 0，原采样参考及停稳失配为 0。正向跨度 0.00102902 通过；镜像跨度 **0.00997956 失败**，首次超过 0.003 在静置第 721 帧（世界第 6,721 帧），核心 X 跨度约 0.009980、外沿控制点高度跨度约 0.006220。报告为 `idle-post-drag-material-gradients-half-phase.json`，`executionCompleted=true`、`thresholdsPassed=false`。因此半相位尚不能进入新人工验收，原 35＋35 项通过没有覆盖此长时问题。

由镜像失败再做一项固定单变量诊断：私有 `dango_material_both_phase_no_spin`（编译位值 15）只跳过接地且无主动输入时额外的 `passive_spin` 旋转速度阻尼，其原衰减系数为 120／秒。自然半相位、两梯度、步长、摩擦、线性阻尼、水平静置平移阻尼及其他约束保持原样，不扫描参数、不延后采样边界。实际默认静置 36,000 帧跨度 0.000552871 通过，逐帧与原采样问题均为 0；但同一固定甩动后静置中，正向跨度 **0.01382515**、镜像跨度 **0.01144081** 均失败，首次分别在静置第 781／721 帧，其他绘制、穿地、参考及停稳问题为 0。三份 `idle-{native,actual,post-drag}-material-gradients-half-phase-no-spin.json` 保留结果。这个变体被拒绝，不能用默认初态通过掩盖甩动后的失败。

随后从未禁用旋转阻尼的半相位模式 7 再做一项固定单变量：私有 `dango_material_both_phase_configured_friction`（编译位值 23）只跳过无主动输入时将节点摩擦设为 16 的覆盖，始终沿用原配置 0.72；旋转阻尼及其他算法、参数、约束和采样集合保持原样，没有叠加上一项改动。实际默认 36,000 帧跨度 0.000623237 通过；但两条甩动后的静置跨度分别为 **0.16435705／0.01027786**，均首次在静置第 1,681 帧（世界第 7,681 帧）超过原门槛，其他绘制、穿地、参考及停稳问题为 0。三份 `idle-{native,actual,post-drag}-material-gradients-half-phase-configured-friction.json` 保留结果；此变体同样被拒绝。旋转阻尼与摩擦单项对照均未建立充分根因，不通过无界寻参或放宽门槛补偿失败。

依据约束路径审查，再从半相位模式 7 做一项固定隔离：私有 `dango_material_both_phase_projected_radial`（编译位值 39）只关闭 Box2D 径向 distance-joint 的 limit，径向弹簧仍启用，步后投影仍维护全部原径向、外围、角度及地面硬范围；其他参数、阻尼和采样集合不变，也没有叠加上述两项改动。该试验隔离同一径向硬范围的两条既有执行路径，没有放宽原边界或引入新的安全规则，不预定这就是充分根因。实际默认 36,000 帧跨度 0.000711981 通过；两条甩动后的静置跨度分别为 **0.01374909／0.00745090**，首次分别在静置第 781／721 帧超过原门槛，其他绘制、穿地、参考及停稳问题为 0，仍拒绝作为修复。三份 `idle-{native,actual,post-drag}-material-gradients-half-phase-projected-radial.json` 保留完整结果。

实际错误锁存集成验证已扩展覆盖默认、面积、弯曲、两梯度、半相位、跳过旋转阻尼、配置摩擦及投影径向编译模式，通过 166,602 项检查，包含原参数／材料参数下的 16 组健康轨迹与 28 组故障夹具。每组健康轨迹检查全部径向弹簧仍启用，仅投影径向模式关闭 Box2D 径向 limit，其他模式的径向 limit 及全部外围 limit 仍启用。故障通过主动构造短边重合或自交轮廓触发，面积／弯曲分别返回 `-94`／`-95`；模块在积分前锁存，保留最后合法 42-float 快照，骨架身份、位置、速度及接触计数不变。之后三次主动控制仍不能清除锁存或隐式重建；显式 reset 后清除错误，原实体继续健康运行 60 帧。最新结果为 `material-integration-projected-radial.log`，此前集成日志保留。该测试检验故障处理契约，不表示正常鼠标操作会触发这些人工非法几何。

最终 Release 构建零警告／零错误，独立数学 24 组主要夹具及 4 个可失败负例通过（`material-forces-step2-final.log`），默认无宏模块限定兼容回归 **11/11**（`test-results/gradient-default-scoped-module-final-tests.trx`）；首次较宽类筛选为 4 项通过、6 项历史三体库对照因夹具 DLL 缺失失败（`test-results/gradient-default-module-tests.trx`），原失败报告保留，这 6 项未验证，不声称全套回归通过。

本节证据位于忽略的 `artifacts/round-material/`：`idle-native-{area-gradient,bending-gradient,material-gradients,material-gradients-cached,material-gradients-half-phase}.json` 与同名 `idle-render-*.json`，另有上述真实 DLL 全帧报告、甩动后静置报告、基线等价报告及集成日志。缓存及半相位 WPF 报告以新文件创建，没有覆盖旧证据。**35/35＋35/35 只属于半相位模式 7 的既有门禁，其镜像甩动后长时稳定已经被否证；三个后续固定隔离也均失败。** 本增量到此停止扩展单变量变体，下一阶段已形成[接触与约束裁定候选](../../../docs/planning/2026-10-10-round-material-contact-solver-candidate.md)，属于待批准设计，不能写成已有充分根因或物理验收通过。没有选定新材料候选，没有打开新候选人工验收窗口；用户已认可的旧小样与两份受保护 DLL 保留原状态。没有切换生产、合并、推送或更新日常安装。

## 运行

按 `docs/phase-1-physics-module.md` 在新隔离目录构建当前 native 模块。本小样不调用会替换正式 DLL 的 `build-box2d.ps1`，没有新依赖。

```powershell
pwsh -NoProfile -File eng/run-material-sample.ps1 -NativeModulePath <isolated-module.dll>
pwsh -NoProfile -File eng/run-material-sample.ps1 -NativeModulePath <candidate.dll> -BaselineNativeModulePath <baseline.dll>
dotnet artifacts/round-material/app/Dango.MaterialSample.dll <isolated-module.dll> --verify artifacts/round-material/verification-drag-final.json
dotnet artifacts/round-material/app/Dango.MaterialSample.dll <isolated-module.dll> --verify-drag artifacts/round-material/drag-after-final.json
dotnet artifacts/round-material/app/Dango.MaterialSample.dll <isolated-module.dll> --snapshot artifacts/round-material/comparison-drag-fixed.png
dotnet artifacts/round-material/idle-diagnostic-app/Dango.MaterialSample.dll <isolated-module.dll> --diagnose-idle <new-idle-report.json>
dotnet artifacts/round-material/idle-diagnostic-app/Dango.MaterialSample.dll <idle-native-report.json> --measure-idle-probe <new-render-report.json>
```

省略 `-NativeModulePath` 时启动脚本使用已构建的 `native-drag-final` 隔离 DLL。可选 `-BaselineNativeModulePath` 显式指定左侧 DLL，两个路径均解析为实际文件后传给窗口；省略时仍为同 DLL 参数对照。`--verify` 和 `--verify-drag` 写完整结果，任一必过项失败则退出 1；`--probe-drag` 仅复现默认候选 `ground-swing`，不代替完整矩阵。`--snapshot` 渲染真实落地帧，并且绘制失败不能生成成功快照。

新的双源入口使用当前构建的样例程序，参数顺序固定：

```powershell
Dango.MaterialSample.exe <candidate.dll> --baseline-library <baseline.dll>
Dango.MaterialSample.exe <candidate.dll> --verify <new-report.json> --baseline-library <baseline.dll>
Dango.MaterialSample.exe <candidate.dll> --verify-drag <new-report.json> --baseline-library <baseline.dll>
Dango.MaterialSample.exe <candidate.dll> --diagnose-idle <new-report.json> --baseline-library <baseline.dll>
```

完整验证的原版轮廓对照使用基线；甩动矩阵三个原默认参数轨迹使用基线、27 个材料轨迹使用候选；静置诊断两个 trial 分别使用基线／候选。报告的 `librarySources` 记录两侧文件名、SHA256 和选择来源，各项保留实际角色；原 `nativeSha256`／`nativeFileName` 始终指首个候选 DLL。`ExplicitBaseline` 只表示传入了参数，允许同路径／同 hash，不证明算法不同。五参数模式仅支持上述三种报告入口，其他入口不能追加基线参数；非法组合退出 2。`--diagnose-idle` 退出 0 只表示完整执行，不代表稳定通过。

甩动后静置入口只接受单候选三参数，不接受附加基线；报告记录实际候选的单一 `librarySource`。报告路径须为新文件。退出 0 只表示两条固定轨迹完整执行，是否通过门槛单独看 `thresholdsPassed`，不能把诊断退出码作为验收结果：

```powershell
Dango.MaterialSample.exe <candidate.dll> --diagnose-post-drag-idle <new-report.json>
```

原生诊断按上述固定源码在新隔离构建目录构建 target `dango_material_idle_diagnostics`。执行 `dango_material_idle_diagnostics.exe <new-report.json> baseline` 跑原版／候选；`area-reaction`、`no-local-balance`、`cached-range` 分别只跑对应单变量候选。这些模式是明确诊断变体，不是材料预设或生产功能。诊断报告路径必须是新文件，不能用返回 0 冒充全部门槛通过。

第 2 项新增 `area-gradient`、`bending-gradient`、`material-gradients`、`material-gradients-cached`、`material-gradients-half-phase`、`material-gradients-half-phase-no-spin`、`material-gradients-half-phase-configured-friction` 和 `material-gradients-half-phase-projected-radial` 固定诊断模式。前三种私有 DLL target 如上，半相位私有 DLL 为 `dango_material_both_phase`，跳过额外旋转阻尼的固定变体为 `dango_material_both_phase_no_spin`，使用配置摩擦的固定变体为 `dango_material_both_phase_configured_friction`，单一投影径向限长的固定变体为 `dango_material_both_phase_projected_radial`；错误锁存夹具 target 为 `dango_material_integration_tests`。它们只在新的隔离构建目录中执行，不替换正式模块。

独立材料夹具使用同一固定源码，在新目录配置 CMake 后只构建 target `dango_material_force_tests`，不构建或替换 DLL。此次隔离目录为 `artifacts/round-material/native-material-forces-step1`：

```powershell
cmake --build artifacts/round-material/native-material-forces-step1 --config Release --target dango_material_force_tests
artifacts/round-material/native-material-forces-step1/Release/dango_material_force_tests.exe
artifacts/round-material/native-material-forces-step1/Release/dango_material_force_tests.exe --negative-area
artifacts/round-material/native-material-forces-step1/Release/dango_material_force_tests.exe --negative-bending
artifacts/round-material/native-material-forces-step1/Release/dango_material_force_tests.exe --negative-point-cap
artifacts/round-material/native-material-forces-step1/Release/dango_material_force_tests.exe --negative-sign
```

正常命令必过项失败则退出 1；四个负例命令在错误实现被真实门禁拒绝时退出 1，若未被拒绝反而退出 0。不可把负例的退出 1 当成新算法失败；正常套件会将负例未被拒绝视为失败。

`--calibrate` 执行有限频率／面积扫描；`--calibrate-stability`、`--calibrate-forces`、`--calibrate-damping` 分别探索频率、局部平滑和阻尼的稳定性。探索命令退出 0 仅表示报告成功生成；每行明确记录是否通过、已检查帧数，出现不匹配或漂移即停止该候选。不可把探索命令退出码称作门禁通过。

## 探索失败与后续边界

首轮错误地把旧默认顶部内收当成必然不圆的负例；实际旧默认也满足 4% 的“近圆”门禁。保留原报告，不收紧圆形阈值以拒绝旧模型，改用明确的圆／椭圆度量正反例。

曾试验面积保持强度 1.8～7.8 的“饱满程度”滑块，其端点落地面积差未达到预设 1%，因此此功能退出当前交付，保留失败记录，不降低门槛。现有面积保持仍是旧单向径向压力，不是已经改造的守恒材料模型；后续应独立评估面积梯度约束及参数效果，再决定是否开放。

首版 32/32 结果及探索失败保留在忽略目录；它们未验证用户图片外形，不作为本轮通过证据。当前状态以本次甩动修复报告及人工评审为准，先前外形报告 `verification-calibrated-final.json` 保留。频率／长时／平滑／阻尼探索报告分别为 `shape-calibration.json`、`stability-calibration.json`、`force-calibration.json`、`balance-calibration.json`、`damping-calibration.json`。正式基线、native 二进制和产品文档的已验收事实保持原样；未合并 main、未推送、未更新日常安装。
