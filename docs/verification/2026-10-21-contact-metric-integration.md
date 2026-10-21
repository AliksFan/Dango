# 共用接触弹性度量正式接入与后台验证

后续状态：用户在本轮交付后明确回复“验收通过”，该确认适用于 `497ad4b` 的平台角部／推球停机修复。正常玩法人工复测不再列为该修复的待办；完整实时性能、整体Windows／多人验收仍未覆盖。随后提出的配饰与首页／门廊需求独立记录于[新一轮验证](2026-10-21-home-porch-accessory-repair.md)。用户未授权合main、推送或部署。以下保留交付时的工程证据和当时边界。

日期：2026-10-21。分支 `codex/belt-page-completion`，起点 `81f83a5`，工作区起始干净。用户明确批准[具体数值方案](../planning/2026-10-21-room-physics-stability-repair.md)接入生产room链并继续后台验证；没有Agent前台操作、合main、推送、安装或部署授权。

## 行为与实现

原质量度量位置修正会在平台角部反复放大局部弯曲能，下一身体积分在原预算内失败。正式room模块现以现有积分目标的局部弹性度量修正接触位置，身体包含核心响应，团子／球使用同一块度量共同分配位移。原三修复DLL的phase60路径仍精确在输入tick612返回 `-705`；新实际DLL的phase60／90各1000tick通过，phase60包含真实球上表面接触，停止方向输入后的后400tick继续成功。

生产修改仅在 `eng/native/box2d/dango_belt_room.c` 的共用方向与静态修正、`dango_belt_room_collection_impl.h` 的团子／球修正。两份native合同记录度量公式及额外成本。材料、自由积分器、四子步、40次积分／20次下降搜索、2e-7残差、32轮位置修正、每坐标.01、原连续形状／材料／碰撞证书、速度／KE门禁和整世界回滚均保留；没有新增产品模式或旧质量方向回退。静态受保护共同平移候选仍需通过原最终证书。

测试共用现有原生重放：新增 `--projection-properties` 和 `--combined-actual [60|90] [--dll <module>]`，覆盖核心实际参与、接触进展、平移等变、双方质量加权平移守恒、速度不写与非有限拒绝。原相位路径仍保留。托管测试仅将诊断输出改到本轮证据目录，保护旧冻结记录，没有改变接受条件。

## 实际验证

证据根为 `artifacts/belt-contact-metric-integration/`；`evidence-summary.json` 记录实际结果、源码／模块／日志哈希和文本LF哈希，`finalize.py` 核对来源绑定及各记录，不把含失败的原始TRX改成绿色。

| 范围 | 结果 | 证据 |
|---|---|---|
| native正式编译 | `/W4 /WX`通过，身体自由积分源码未变 | `source-native-build.log`、`native-contract-build.log` |
| 新实际DLL与源编译共享重放 | phase60／90各1000tick通过；包含全部1000成功帧的累计数值摘要相同 | `native-regressions/*actual-v2.log`、`summary-v2.json` |
| 旧三修复DLL负控 | phase60输入tick612，`-705`，预期退出1 | `old-dll-phase60-negative-v2.log` |
| 核心耦合与双侧修正性质 | 核心方向／实际位移非零并匹配；接触进展约.001000002，质量加权平移残差约4e-18 | `properties-v3.log` |
| 既有集合合同及原top／corner路径 | 默认ABI、事务、几何、传冲量、回滚／故障锁存通过；原两条各1000tick通过 | `contracts-v3.log`、`source-original-top.log`、`source-original-corner.log` |
| 原九配置站台／撤支撑 | 各480tick站台及撤支撑通过 | `native-contracts/platform-nine.log` |
| 无平台对冻结单体 | 九配置各480tick，全部受比较通道逐帧逐字节一致 | `native-contracts/frozen-product-nine.log` |
| 墙面共享机制 | 双侧持续推压、反向离墙、空中反应、平台／墙共同接触、拒绝及完整回滚通过 | `native-contracts/walls.log` |
| 真实Scene／Session产品链 | 10通过、0失败、0跳过；包含原五项、推球、球唤醒、30件容量、重进和保存／中断恢复 | `managed-product-integration-with-audit-warning.trx` |
| 托管room绑定 | 31项首轮通过；两个旧模块夹具更正后2项通过，共33项 | `managed-room-binding.trx`、`managed-legacy-binding-corrected.trx` |
| 镜头／休眠、恢复安全和文件存储 | 65项首轮通过；两个缺少元数据夹具的用例补齐后2项通过，共67项 | `managed-room-storage-sleep.trx`、`managed-desktop-metadata-completed.trx` |
| 默认本地源码发布和来源核验 | 未使用警告例外，发布成功，manifest origin=`source-build`且绑定有效 | `local-publish.log`、`review-app/belt-native-manifest.json` |

共110项**定向托管检查**获得通过结果，包含更正／补齐夹具后的4项，不等于整个测试项目全部通过。首次绑定选错了contact模型早于r1的旧DLL，加载器按原合同拒绝；换成匹配r1的冻结capability15模块后两项通过，没有改加载器。

补充检查还误选了19项旧家具链用例，它们因缺少独立的旧家具原生模块，在创建测试世界前失败；未改旧链或拿新room DLL冒充旧模块。这19项保留在原TRX中，未完成；另一个依赖旧unified模块的测试因模块不可用未纳入筛选。本轮P2所需的新身体家具持久化、完整事务和恢复由真实BeltRoom Scene／Session的10项覆盖，不能据此宣布旧家具链或全项目验收通过。

测试项目默认还原实际被既有 `Microsoft.AspNetCore.SignalR.Common 10.0.11` 高严重性审计 `NU1903` 阻断。只有本轮测试命令使用 `-p:WarningsNotAsErrors=NU1903`，保留该警告；依赖版本和仓库策略未改，也未宣称解决漏洞。独立桌面产品的默认源码发布无需这个例外。

数值摘要排除结构padding及进程身份；它覆盖body snapshot、scaffold、curve、stats、room diagnostics以及两物体的球运动／接触数值，不是整个内部body/world内存比较。源码侧另外检查接触残差／KE／工作上限；DLL依赖原生产门禁，并与源码的累计数值摘要核对。v3只给性质测试补核心耦合断言并重跑性质／默认合同，未改生产代码或重复已经通过的长路径，v2记录原样保留。

独立只读审查未发现生产公式、球侧对称性、失败原子性或预算边界偏差；提出的核心耦合回归补强已落实。额外矩阵工作仍最多128次／tick；本轮并发测试耗时不能当作产品实时性能验收。局部度量仍不保证任意非线性接触都能量单调；旧高能状态直接积分仍失败的原型证据保留。

## 模块、复测包和交付边界

- 新room DLL SHA256：`CB420AAD86C75E92EDFA361E8323946E70026399DCA435E08F40A2C10401094C`。
- product DLL SHA256：`4871642B25107FAA3D794AC16FA04837D342BF4646627B0AF103D4B1679C3C4A`，与冻结单体相同。ABI布局／模型／材料调优身份未改，数值实现由新源码／模块哈希区分。
- 本地自包含复测包：`artifacts/belt-contact-metric-integration/review-app/Dango.Desktop.exe`。包内模块与通过重放的正式源码构建模块哈希相同，尚未启动；旧review-app目录及原三修复／诊断／冻结证据未替换，用户数据未写入。

P2已完成本轮授权的接入和后台工程验证，候选交用户正常玩法复测；P3前台呈现、真实键鼠、人工观感及完整实时性能**未验证**。桌面规范v0.1适用桌面功能界面，本轮无页面／主题修改。人工复测通过前仍暂不可合并，不将本次结果升级为整体Windows／多人验收。

本轮只有后台编译、测试和本地发布，无前台窗口、全局输入、新增服务或监听端口；原生检查、testhost和发布命令已退出，证据、测试虚构文件和本地复测包保留。未合main、推送、安装或部署；如需Agent前台操作，按AGENTS.md另行取得屏幕使用授权。
