# 双人P1 R0自身约束诊断冻结合同

日期：2026-10-11。用户选择“接受上述安全出生规则，先做堆叠诊断”，批准[下一步方案](2026-10-11-dual-player-p1-next-decision.md)中的R0。源码基线：`6ae33f49336ef4141177e22c65585e2415106e5d`。合同在诊断代码编辑／运行前本地提交冻结；工作树仍为`codex/ordinary-room-physics-admission`，串行使用，没有其他owner编辑本轮文件。

## 1. 范围及不变项

只增加独立native diagnostic executable、输入转换／运行脚本及编译期观察钩子。读取项目自有局部state和公开Box2D状态，不读私有结构，不补丁依赖，不增加生产诊断ABI。默认编译不启用钩子，冻结A DLL不重建或替换。

保持16节点、原float参数和dt `0x3C888865`、16实际子步、96轮自身约束、原投影顺序和`1e−5`残差、原接触／面积／速度／交叉／角／能量及core误差门。不扩大求解轮数，不移动core补救，不产生R1修复，不接UI／Hub／账号／数据库／Linux环境。不更改或删除既有证据、数据与用户进程。

## 2. 输入与观察

从最终`semantic-prescribed-final2`中转换stack-60hz的41合法输入及Tick42失败输入、push-60hz的600合法输入；记录输入文件hash，按ABI v1的完整slot／float bits生成本地二进制fixture。初始Add使用原replica的分离安全anchor，绑定相同opaque epoch后从ordinal1逐步调用原ABI，不能用oracle位置初始化节点。无未验证的pose快照或新求解路径。

观察钩子仅在实验exe编译启用，标记Tick、substep、entity及直接自身约束／接触内自身约束的阶段。自身solve入口、每轮投影后、成功或失败退出保存局部17点位置／速度、inverse mass、长度／半径／地面参数、各类残差、最坏行与finite标记。长度raw violation和原归一化值分开，角度保持原单位。观察前后state逐字节比较，不得修改原state。诊断文件写入及派生计算不作为原求解耗时测量。

## 3. 必须能失败的核查

同一fixture用两种实际exe或独立冻结DLL调用对照：无钩子和有钩子。记录构建hash、公开17点原始状态、共同输出和真实core trace。每个合法步、首败、首败后的lastsafe及错误锁存逐位一致；如不一致，诊断无效，停止解释结果。

堆叠应在Tick42第5子步以`−91`停止，41步合法；记录失败局部state及96轮行残差，不能用最后合法node snapshot冒充失败末态。推挤必须完整600步合法且与冻结证据／无钩子对照一致。严重重叠反例保持零速度近重合中心、真实进入16个native子步后`−422`；不能退化为输入预检拒绝。观察器写state的故意变体必须被观察前后不变性核查拒绝，实际进程exit1。

每个world同owner线程创建／步进／读取／释放；native bytes归零、子进程退出、无新增监听。R0没有新的物理准入绿灯；诊断成功只证明观察可信并定位首败。若失败分属多条约束，分别报告，不把最大行当数学无解证明。

## 4. 证据、结束与下一步

新增ignored证据位于`artifacts/p1-r0/`。保留旧A DLL `4E2B92B5CC73A6C586A7A9F8EFE96F03187AECBAC6A5975E34BFBC9BB7E32A68`、旧authority DLL `C7D3140079BEFD29E3E8512B5CF69376D4B342AD583A7C458EB89420A72FE07A`、日常DLL `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`和B EXE。本轮构建目录独立，Box2D来源hash与已有冻结manifest复核。

输出诊断报告、可审阅的R1候选与本地提交。R1必须另行批准，不在本轮改变算法补绿；安全出生已批准为产品规则，但恢复实现、持久化／Linux／正式服务仍后续独立合同。P1仍不准入，P2停止；不合main、不推送、不安装／部署、不替换日常DLL。
