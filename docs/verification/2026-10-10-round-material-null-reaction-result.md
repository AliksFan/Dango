# 圆形材料：一维冗余反力数学阶段结果

2026-10-10。用户回复“继续”，批准[一维冗余反力方案](../planning/2026-10-10-round-material-contact-null-reaction-candidate.md)。**固定第 147 帧数学验收失败，停止于第 1／2 项；未连接新私有 DLL，未运行新世界或人工验收。** 原有限块的真实第 147 帧失败和原材料镜像长时失败仍保留。

## 实现与固定输入

新增独立 `dango_material_contact_null_reaction.h`，保留原完整 pivot、16 行容量及一次因式消元，满秩路径使用原运算顺序。一维分支取得 raw pair，执行 RELEASED／ZERO 精确身份，回到物理单位并规范方向，重算有效 pair 的原矩阵残差、逐体零冲量证书，之后才允许单边／累计摩擦锥区间及一次最近零选择。失败路径不发布解。

`dango_material_contact_block.h` 的接入仅由新私有数学宏启用；CMake 只新增独立测试目标，没有带该宏的新 DLL／世界目标。诊断可用性与通过标记分开，下一阶段同时要求前阶段通过和相同 mode index；新模式清除后续诊断。`null_reaction.used` 只表示选值已用于内部求解，不独立证明整块发布成功，必须结合块状态和实际输出检查。

第 147 帧来自原 `contact-block-cap-native-default.json`，SHA256 `9386a609f4fc764dd367ccb82a16811bd5b275caccee220b026b2493b87c523b`。新固定 header 的 93 个 float、4 条完整 receipt 和 double dt 与该报告逐位一致；没有重放或修改世界缓存。

## 实际 C 首失败

| 检查 | 实际结果 |
|---|---:|
| 活动行／数值秩／自由原列 ID | 9／8／55 |
| 完整 pivot 搜索／消元 | 285／8，一次消元 |
| 特解逐行残差 | 全部通过 |
| 有效零方向逐行残差 | 第 139 行失败 |
| 残差绝对值 | `1.1154989369220591e-16` |
| 批准的该行门槛 | `5.0552782626453784e-27` |
| helper／块状态 | `INCONSISTENT`／`NULL_REACTION`，不表示整个物理问题无解 |
| 逐体／区间／α 选择 | 未执行，不能读取未运行零值为通过 |
| 输出 | sentinel 保持，没有生成合法反力／速度输出 |

完整 pivot 的自由方向与独立 Python 固定重放逐位相同。第 139 行对主要非零方向坐标 55／155／156 的系数均为零，只有其他坐标上消元留下的极小舍入分量参与，导致批准的 `Σ|Mij qj|` 尺度接近零。原判据由此要求这些结构零几乎精确成立。

独立后续证书诊断另显示体 10／13／16 的逐体门槛也会拒绝同一方向。体 10 只有角度行 86 的极小分量参与，残差等于该项尺度，门槛要求 `1≤128*9*DBL_EPSILON`，无法成立。**这不是实际候选路径中的第二个故障，也没有修改方向／清零系数。**

捕获帧仍存在上一数学复核证明的合法物理解；本轮失败说明所批准的相对数值证书不能接受普通 double 消元得到的近结构零方向。不能据此否定圆形自然态或合法落地运动，也不能直接绕过证书发布特解。

## 下一设计边界

独立只读计算比较了整行算子范数和逐体算子范数尺度，固定帧可覆盖这些舍入误差；没有修改候选或物理门槛，也不表示候选验收通过。具体[数值证书尺度候选](../planning/2026-10-10-round-material-null-certificate-scale-candidate.md)**待批准，未实施**。它改变原已批准证书，因此按原计划的固定失败停止点等待新的设计决定。

## 本地证据与交付

证据在忽略的 `artifacts/round-material/`，创建新文件，保留首失败：

- `contact-null-frame147-equivalence.json`：93 float／4 receipt／dt 位检查。
- `contact-null-affine-c-probe-first.c/.json`：实际 C 首失败；其中逐体零结构未执行，不能当作通过。
- `contact-null-affine-c-probe-first-detailed.c/.json`：完整原矩阵、raw／有效 pair、每行残差／门槛、可用性／历史索引与输出保持。
- `contact-null-affine-probe-first.py/.json`：独立固定矩阵重放，不运行世界。
- `contact-null-certificate-analysis.py/.json`：待批准尺度的只读诊断，不是新候选通过证据。
- `contact-null-block-frame147-diagnostic-step1.*`：实际数学宏的完整固定输入／矩阵／pair／阶段标记／模式／成本；未执行字段的零存储只可结合 availability 阅读。
- `contact-null-block-frame147-diagnostic-step2.*`、`contact-null-block-frame147-diagnostic-step2-verify.json`：新序列化把 17 个未执行字段写为 null，已执行部分与 step1 完全相同，未改候选。
- `contact-null-block-frame147-diagnostic-root-review.json`、`contact-null-block-root-review-checks.json`：根任务独立复跑与 step2 完全相同，并再次核对 93 float／4 receipt／dt 的输入位、阶段索引和输出保持。诊断进程退出 0 只表示准确记录预期失败；`block.passed=false`，不能当作必过数学正例通过。

## 最终验证层级

- 静态：新目录 `native-contact-null` 配置成功；三个相关 Release 测试目标 `/W4 /WX` 构建通过，日志 `contact-null-configure-first.log`、`contact-null-build-root-final.log`。只读源码审查确认，关闭新宏后原块头文件的 627 行非空源码与修改前 HEAD 一致；外部输出仍仅在全部物理末态通过后赋值。任务相关五份文档的 106 个本地链接与差异格式检查通过。
- 独立数学：最终根任务复跑 `contact-null-fixtures-root-final.log`，第 147 帧前 **32,451 项断言、42 个正例、37 个拒绝反例通过**，包含旧块 30,158／29／16。新增拒绝覆盖不相容 rhs、二维冗余、逐体非零而全局抵消、近退化、非有限／溢出、区间矛盾、选值残差和阶段错误／跨模式沿用。第 29 帧 double／float、31 个物理字段、解和 pivot 成本与旧满秩算法逐位一致。随后第 147 帧必过正例真实失败，**整体退出 1，数学验收未通过**；没有把它改成通过的拒绝负例。
- 单元正例：涵盖特解单边符号经一维分配修复、零点／有限闭端／无界端、RELEASED／ZERO 精确身份与原矩阵复核；使用合法 receipt 的非零 T0 和测得 cone slack 验证累计锥，并检查不同质量、两向滑动方程和一次最低 ID 模式切换。这些明确标为独立数学单元，不冒充世界轨迹。
- 旧路径回归：新目录宏关闭 `contact-null-retained-block-root-final.log` 再次通过 **30,158 项／29 正例／16 拒绝**。旧候选实际锁存测试 `contact-null-retained-latch-root-final.log` 通过 **1,355 项**，继续精确复现第 147 帧原 rank 故障、输入保持和显式 reset；测试世界已销毁。这证明旧故障处理保持，不是新候选物理通过。
- 过程修正：`contact-null-guards-stages.log` 保留一项测试预期失败：near-Gram 的唯一最大 pivot 是第二坐标，原断言错误固定了 raw 方向符号／分量。改为验证批准的规范规则（最低 ID 非零分量为正、最大绝对分量为 1）；候选算法和门槛没有改动。owner 最终日志与根任务复跑一致。
- 新候选集成／实时／人工：未连接新 DLL，长期、九组参数、35＋35 和 WPF 未执行；第 3 项以后阻塞于固定数学失败。不转移旧候选的通过记录。

正式 DLL `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`、认可隔离 DLL `A4600B47CD198B1F1A735EE9FCF0C0DD81DD544C715305BC52A2176D3BE7F553` 与旧块失败 DLL `8B51561E2C48D31A9288F035C8862F7615773D983CC9C03E1D9EDA07483F23F2` 的哈希再次核对不变。日常安装及生产捏脸保持原状态；仅在本任务分支记录本地数学增量，未合并、推送或部署。没有残留本轮测试进程、端口或持久数据操作。
