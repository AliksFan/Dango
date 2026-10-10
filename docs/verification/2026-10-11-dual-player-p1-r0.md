# 双人P1 R0：固定中心下的自身约束诊断

日期：2026-10-11。合同冻结：`e036d42c1bd6cc45a94a15cbd84d49e2c1921117`；原算法来源：`6ae33f49336ef4141177e22c65585e2415106e5d`。范围：[R0合同](../planning/2026-10-11-dual-player-p1-r0-contract.md)。

**结论：诊断可信，首败定位到下层底部node12的径向最小长度／raw角残差；与地面夹紧的约束方向冲突，96轮末尾残差停滞。没有修复算法、证明全形状无解或解除P1门禁。** 下一步[R1位置投影候选](../planning/2026-10-11-dual-player-p1-r1-proposal.md)待批准。

用户另已批准[world丢失／释放后安全出生](../adr/proposals/2026-10-11-world-loss-safe-spawn.md)：进程重启和正常休眠实际释放world后明确提示并安全出生；普通重连／接管／world仍在内存的暂停保持原状态。这是产品改约，本轮未实施恢复接线。

## 1. 实际对照与反例

独立工具：[Dango.PrescribedConstraintDiagnostics](../../eng/tools/Dango.PrescribedConstraintDiagnostics/README.md)。Windows x64 Release，原Box2D3.1.1的233份实际文件hash与前轮manifest一致。新增钩子只在实验exe编译启用；关闭宏时原投影代码不变，未重建DLL。输入从前轮实际ABI记录逐float转换，Add使用原分离anchor，从ordinal1重放，不用oracle位置初始化节点。

| 核查 | 实际结果 |
|---|---|
| stack，无钩子／有钩子 | 各41步合法；Tick42第5子步`−91`，与冻结DLL每步共同输出、contact count及trace逐位一致 |
| push，无钩子／有钩子 | 各完整600步通过，与冻结DLL逐位一致 |
| 零速度深重叠，无钩子／有钩子 | 均确实进入16个native子步后`−422`，并非输入预检拒绝 |
| 实验程序间原17点比较 | 三组output文件hash均相等；包含每步公开17点位置／速度、状态及core trace，故障live state也比较 |
| 故意写观察state | 实际exit1；32次观察前后mutation被捕获，42步冻结对照均失败；未把这个变体算正常通过 |
| 锁存与资源 | lastsafe和contact count保持；后续步仍故障。7个实际子进程均退出，各自实验native与冻结DLL的bytes0→0，同owner线程 |

有钩子stack发生2588次自身约束调用，其中1次失败；该失败入口＋96真实sweep＋失败出口共98份局部state全部保存。全量观察文件为5093504 bytes；push全量观察约62.9 MB，只存在本地ignored实验文件，不进入网络或恢复数据。

六个正常诊断子进程exit0只表示“观察可信且预期失败保持”；堆叠本身仍失败。第七个变体按预期exit1。没有UI、正式服务、监听或数据库操作。

## 2. 原始首败与行残差

Tick42、实际子步5、entity1（下层）、phase0（直接自身约束）。失败core约`(−.000304948917,−.233009502)`，inverse mass0。原参数：径向rest`.68`、min ratio`.65`、node radius`.045`、ground`−.72`，实际float地面中心限约`−.675`。

| node12行 | 入口／末态 | 原门槛 |
|---|---|---|
| 最坏raw角残差 | 入口`.001747653` → 96轮后`.0000661246013` | `1e−5` |
| 径向最小长度归一化残差 | 96轮后`.0000137178331` | `1e−5` |
| 周长、display角、floor残差 | 96轮后均0 | 原门槛保持 |

其余节点在末态没有超过门槛的行。第90–96轮最大残差均为`.0000661246013`，但完整位置向量逐位不同：7轮有7个不同位置向量。**这是残差平台，不是整个state完全不动或已证明固定点。**

node12实际位于地面中心限，Y约`−.675`；原float行记录的径向长度不足`9.32812691e−6 p`。只读double几何分析得到不足约`9.32315e−6 p`，其单独径向下界修正Y方向约`−9.32315e−6 p`，会往地面以下移动；raw角的middle-node Y梯度约`−7.044844`，该下界修正同样倾向把底节点往下推。原sweep末尾的地面夹紧会阻止这部分位移。float行值与double分析分开，不用后者替换原残差门。事实说明当前顺序投影对这个受地面限制的局部形状存在竞争，尚不证明它是唯一作用或全局无解。

作为只读几何检查，保留该core并使节点停在地面时，径向最小圆与地面有两个交点，横向距离约`.002896700366 p`。这个结果只证明“径向下界＋地面”两条约束有几何候选；周长、两种角、整个轮廓安全与成对接触仍需独立求解验证。分析没有额外步进、增加96轮或修改live world。

## 3. 证据与交付

全部新证据在`artifacts/p1-r0/`：

- `summary.json`、`process-evidence.json`：实际PID／退出码、原输出对照、owner及资源。
- `build-manifest.json`、`input-manifest.json`：实际编译设置、233份依赖及原始输入来源／fixture hash。
- `stack-1.failure.jsonl`：98阶段逐行残差与真实失败调用身份；`stack-1.observations.bin`保存每次调用局部17点、质量及参数。
- `decoded-failure.json`：只读解码及有范围限制的几何推断。独立解码脚本后补，原编译／运行manifest保持原时点，不把新脚本说成先前构建输入。
- 三组`*.output.bin`：有／无观察器完整状态逐位比较；`mutation-2.stdout.log`保留真实拒绝结果。
- `final-source-and-resources.json`：最终source／binary／静态／Git与资源核查。

受保护日常DLL、前轮A／authority DLL和B EXE均保持前轮hash。编译的native及C工具源码未再改变；新增只读分析脚本与文档不需要重建。原单人／家具完整回归见前轮报告，本轮未重复执行，也没有修改其求解路径或参数。

最终静态核对11份任务文件、183项相对文件链接，无缺失、尾随空白或本机配置／敏感凭据匹配；两份PowerShell脚本语法检查及`git diff --check`通过。隔离Release构建0warning／error。任务进程／监听均0，既有家具候选及共享.NET辅助进程保留；ignored实验数据没有删除。

Linux、实际UI、持续活动观察、人工及双机未验证。安全出生恢复实现未开始，R1修复仍待批准，P1不准入、P2停止。本轮只允许隔离分支本地提交，不合main／推送／安装／部署／替换日常DLL；保留全部证据与既有用户程序。
