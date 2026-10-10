# 双人P1下一轮：规定中心与跨进程重放冻结合同

日期：2026-10-11。用户对已交付[候选方案](2026-10-11-dual-player-p1-followup-proposal.md)回复“继续”，批准推荐A＋B隔离实施、虚构夹具、构建／验证及本地提交。**P1仍未准入；不改变完整续接的产品要求，不启动P2或正式服务。** 安全重生、私有solver格式、Linux环境启动／安装、数据库、main整合、推送、安装、部署和受保护DLL替换未授权。

本合同在本轮代码编辑和失败验证前冻结。旧[P0合同](2026-10-11-dual-player-p0-contract.md)全部原物理／绘制门槛保留，不事后放宽；第一轮[报告](../verification/2026-10-11-dual-player-physics-admission.md)与三轮呈现失败原始证据保留。

## 1. 来源、时间与文件所有权

- 当前干净实施树：`codex/ordinary-room-physics-admission@ce68521bdd5765c67f9e3de97cd4376e5c34b1bb`。沿用同一隔离树，分文件所有权，不能同时改同一文件。
- 已冻结authority DLL SHA256：`C7D3140079BEFD29E3E8512B5CF69376D4B342AD583A7C458EB89420A72FE07A`；Box2D3.1.1、官方commit／archive／MIT hash、只读baseline与受保护DLL沿用P0，不补丁依赖或引用Box2D私有结构。
- 原16节点、配置4substeps、最小面积`.42`、最大速度`12 p/s`、地面`−.72 p`及原参数保留。实际native自适应子步数按原算法计算，不能以配置4声称每个frame只有4步。
- 原managed dt保持`TimeSpan.FromSeconds(1d/60)`。本轮net10 probe实测Runtime10.0.12、Ticks166666、TotalSeconds`.0166666`、float bits`0x3C888865`，原自适应`ceil(dt*960)`为16；诊断仍报告实际count。证据`artifacts/p1-followup/dt-probe-result.json`。不用shell runtime或近似literal替代。
- Native owner拥有新规定中心native模式／header／probe、collision模块及managed可选binding；semantic owner拥有`Presentation/`及semantic tests；replay owner拥有新`eng/tools/Dango.PhysicsReplayAdmission/`和独立native executable，使用自建CMake及从ce68521提取的只读基线，不同时改A的CMake。根拥有合同、协调、回归、证据与交付。

## 2. A的本地可选ABI与时间／身份

新增独立ABI v1，保留旧authority capability与legacy签名／帧。显式`BindPrescribedEpoch`在首次步进前绑定非零opaque epoch；新模式与authority／旧dynamic presentation互斥。普通Step即使neutral、旧ApplyTargets、diagnostic降级及家具入口不能绕过新模式。新模式不提供玩家操作或authority资格，checkpoint仍unsupported。

固定输入248 bytes：72-byte header含ABI／size、epoch、严格stepOrdinal、double start/end sampleTime、start/end真实authority.SceneRevision、body／start-pair／end-pair count及reserved；两个56-byte body槽含本地entityId、start/end floor标记／supportId、reserved、start/end中心位置和解析速度共8floats；start/end各一个32-byte pair槽。未用槽清零，不含节点、指针或句柄。可选绑定验证准确sizeof和所有slot／count／finite／identity，整组预检失败不得部分应用。

保留20Hz位置Hermite，速度改为同一Hermite位置的解析导数，禁止把线性混合速度当作曲线导数。60Hz每步提供T−1与T的共同位置／速度；native以相同Hermite在每个实际substep规定core。sampleTime为共同Tick单位，物理dt为冻结float；不得步进到T＋dt后按T中心绘制。

snapshot／sample携带真实authority.SceneRevision；成员／支撑／pair在关键帧原子变化，不在插值中制造新身份。pair normal固定A→B。0≤u<1保留start语义，u=1原子end语义。删除成员在已采样到删除后的固定步边界先移除，过滤start中的旧身份／support／pair；幸存身体不重建。首次没有历史：按当前速度回推一步起点，start floor／support／pair为空，end为当前样本。使用自然局部形状，不以服务器轮廓或姿态冷恢复伪造正确初始化。

sampler和replica均显式BindEpoch；incoming旧／foreign epoch拒绝并保持原world，不自动重建回旧epoch。native与managed各自核对epoch／ordinal／revision／身份。epoch授权真实性属于P2，不声称本地语法证明服务端资格。

## 3. A的节点解算与诊断停止门

core仅作为受规定运动载体，可使用kinematic承载路径；自约束core inverse mass=0，接触投影／限速／阻尼仅操作16节点。复用原节点面积／角约束／安全计算和display geometry／gradient，不能将零质量core送入旧`1/GetMass`计算。健康legacy与authority算式逐位不变。每求解阶段监测实际core，不能在末尾吸附归位掩盖漂移。

成对局部投影只限压缩，不能吸引或粘连；不把position correction变成额外velocity。不使用固定gap、renderer偏移、替换权威center或服务器轮廓。若规定中心下形变无可行解、原节点安全或实际绘制接触门失败，明确故障锁存并停止；保持最后合法输出，不以fallback得到准入。

固定诊断3936 bytes：56-byte header含ABI／size、epoch／ordinal、error、bodyCount、真实substepCount／sampleCount、两个native bodyId及两体各阶段最大core误差；2×97×20-byte样本包含fraction、公开API读取的actualX/Y及targetX/Y。包含起点和每个真实substep，最多96步，未用槽零。诊断仅本地实验，不上传。native每阶段误差及managed按独立曲线复核均须≤`1e-5 p`。

外部规定中心可向局部节点做功，不对展示系统声称17点动量／能量守恒。原authority的动量／能量门禁不变。新接触投影用double：`k=Σ_nodes |g|²/m`、`vn=Σ_nodes g·v＋C`、`C=Σ_cores g·v_prescribed`、`λ=max(0,−vn/k)`、`W=−λC`；节点新速度仅一次cast float，position correction不写速度。实际float节点能量门冻结为`ΔKE_float−W≤1e−6＋R32＋R64`：`R32=Σ m Σ_axes (|v*|r＋r²/2)`、`r=2^-24|v*|＋FLT_TRUE_MIN`；`R64=γ512[E_before＋E_star＋|W|＋|λ|Σ|g·v_old|＋λ²k]`、`γ512=512·2^-53/(1−512·2^-53)`。理论余项为`−λ²k/2≤0`。记录实际残差、规定功及预先解析舍入界；非有限值失败，不在失败后扩大界限。

原九项20Hz／30、60、144Hz推挤、堆叠、withdraw实际draw SAT门全部保留：侵入比≤`.031`、支撑正gap≤`.008 p`、最终绘制中心误差≤`1e-5 p`、面积／速度／交叉／锐刺安全。必须完整执行撤支撑后自然下降／落地，下一已采样固定步无旧支撑或pair，分离不粘连、侧触不授予support。无可行压缩显式失败。

反例包括动态core、错误绘制中心／相位、旧epoch／ordinal／revision、错身份、反向／非法normal、侧向伪支撑、不可接触的远离pair、注入额外速度能量、不可解形状。只能检验可观测矛盾与物理可行性，不能从无轮廓语义证明任意自洽pair是服务器事实。headless失败即停止，不启动UI；通过也只作隔离候选。

## 4. B的跨进程重放与预算

独立Capture和Replay必须两个不同PID的真实OS进程，同版本、同构建和冻结dt。executable内按自有rig IDs，用Box2D公开GetPosition／GetLinearVelocity读取原17点，不使用managed归一化contour或Box2D私有结构，不新增生产DLL／checkpoint导出。

journal从规范空世界开始，保存完整float配置、实际dt bits、源码／EXE版本标识、严格Tick／ordinal、实际成功Add／Remove与anchor／实体映射、Bounds／ClearBounds／Reset及每次实际controls。仅连续完全相同controls允许StepSpan压缩；Jump边沿必须及时清零，queued／rejected命令不记录为applied。不使用中途pose快照截断历史。配置／身份／操作／长度／版本先验证，损坏或缺段明确失败。

oracle文件独立保存恢复边界及未来120步全部17点position／velocity及authority frame／Jump／support／cooldown等公开语义。它不作为恢复输入，不计入生产journal预算，不能用oracle恢复世界或称生产checkpoint。Replay只读取journal重建，再与独立oracle比较；重放不发历史通知、不执行业务／数据库事件、不授予控制。

同构建阈值沿P0：恢复边界17点初始位置／速度每轴误差≤`1e-6`，未来120步每点position≤`1e-5 p`、velocity≤`1e-4 p/s`，支撑身份／Jump分支一致。覆盖地面、相撞、堆叠、空中、移除下层五场景，另加拓扑增删、边界、Reset、多world创建销毁干扰。所有创建／步进／读取／释放在进程同一owner线程，检查native bytes回归并确认进程退出。

另运行600秒模拟历史（不等于真实600秒活动观察）。从打开／校验journal到重建最后安全帧的技术停止预算为**5秒**，future120步对照另计；报告当前Windows host，不能作为2核4GB正式服务器SLA。journal上限保持**65536 bytes**，超限只能诊断、不得准入。不存在任意长历史的有界承诺；必要时再追加60分钟体积／耗时估计或测量，明确区分实际与推算。

结构完整的缺Jump／丢Stop变体须由真实动态oracle失败，不能只靠CRC；另测Add错序、Tick错、缺段、坏版本／长度／标识，均为实际Replay进程预期exit1。正常所有维度通过才可标为B技术可行；任何正确性、字节、时间失败保留红灯，不把正确重放等同预算通过。

## 5. 交付与后续

新证据置于`artifacts/p1-followup/`及独立presentation子目录，不覆盖第一轮。每次源码／binary变更重验受影响项，最终记录hash和本地提交。静态、native／managed、OS进程、真实UI／人工、Linux、Git与资源状态分别报告。

即使A或B通过，原完整checkpoint仍unsupported；B还缺耐久journal与无限历史预算设计，Linux仍未验证。原“每Tick／短命令账本不上数据库”规则不在本轮修改，不执行P3写入。所有所需门禁未满足前P2停止；若预算不可行，再提交具体改约供用户选择，不自行安全重生、fork solver或放宽门槛。
