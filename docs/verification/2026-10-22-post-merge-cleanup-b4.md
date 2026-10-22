# B4规则与首批结构优化

日期：2026-10-22。用户已批准五条规则与结构优化顺序，实施位于`codex/post-merge-cleanup-b3-b4`，起点`753c695`。当前为实现与受影响自动验证交付，**前台／人工待验，未标阶段已验收**。完整范围及验收标准见[实施计划第12节](../planning/2026-10-21-post-merge-organization-plan.md#12-b4批准与结构优化实施计划2026-10-22)。B1～B3历史状态及证据保持原含义。

## 用户强调的检查项逐项结果

| 检查项 | 本批结果 | 后续边界 |
| --- | --- | --- |
| 物理选择与后端差异 | `ProductPhysicsComposition`集中选择editor／room默认实现；首页通过`IBodyPreviewRuntime`调用异步owner。恢复几何和地面支撑移到物理adapter，房间驱动只取能力结果 | 现有成功帧／输入格式保留，不承诺不兼容模型零修改切换；两owner、两个ABI和唯一推进保持独立 |
| 首页职责 | `HomeBodyPreviewSession`拥有绑定／关闭、键盘抑制、Space边沿及拖动状态；页面保留控件路由、坐标、呈现与确认 | 本次提取预览会话，未将整个首页迁为MVVM；导航／外观／配饰继续复用原合同 |
| 公共主题 | 调色板和普通按钮改共享编译型字典，配饰专用字典保留；去除长XAML字符串和运行时Parse，普通色值只维护一处 | 保留首页／配饰按钮及图标模板差异；真实系统高对比切换、DPI和人工观感待验 |
| 测试归属 | 12份、83个原测试方法物理迁入LocalRooms；支持代码在`tests/Support`共享，业务断言保留 | 原生／产品窗口用例没有因迁移被视为无窗口测试，见[测试说明](../../tests/README.md) |
| 网络进程夹具 | 两套wrapper共享私有bootstrap／control管道、STOP／EOF、失败清理、超时回收及安全日志检查；保留程序集／显式最终exe两种定位策略 | 正常退出与启动失败回收已覆盖；强制终止兜底分支未单独故障注入 |
| 网络传输接口 | 已检查并登记RPC字符串及SignalR异常耦合 | 按用户顺序，随下一次同步功能新增／替换处理；本批未修改网络生产合同 |
| 家具合同 | 已检查并登记OrdinaryRoom身份分支与按种类校验preset revision方向 | 随房型／家具／版本扩展实施；schema1／2／3及原UNKNOWN状态保留 |
| 过时说明与规则 | Physics README区分当前Belt产品链及旧DLL诊断链；五条规则写入工作流第13节，AGENTS只加索引 | 当前导航已更新，历史快照不回写成新状态 |

本次优化的实质是消除重复用例归属和色表／模板来源，集中产品模型选择，移走页面内输入状态与存储层的native几何解释。未以文件数、行数或构建通过代替行为验证；未调整算法、材料、预算、存档格式、数据库或共享业务授权。

## 实际验证

证据目录：`artifacts/post-merge-cleanup-b4/`。仅测试恢复／构建命令使用既有NU1903的命令级例外，未改变依赖或持久项目规则；正式Desktop发布无此例外。

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| Desktop受影响合同与控件 | 172/172，零跳过；包含worker、输入会话、共享主题、外观存储／草稿、呈现器、默认入口和房间恢复 | `results/b4-integrated-contracts.trx` |
| 隐藏HwndSource真实room | 同一最终程序集另有11/11定向通过（已包含于上行相同用例）；断言隐藏状态、真实原生推进／渲染和唯一owner | `results/b4-hidden-room.trx` |
| LocalRooms单一归属 | 构建通过，存储／相机／恢复94/94，零跳过；两个测试项目编译交集仅2个Support源，0个测试源 | `results/b4-localrooms.trx`、`ownership.json` |
| 网络私有进程生命周期 | 5/5：真实双WebSocket、bootstrap后注入失败回收、最终构建exe的RTT0／100／300三组修复与重连 | `results/b4-server-lifecycle.trx`、`results/b4-real-network.trx` |
| 源码／实例兼容合同 | v2／v3／v4作用域3/3；v4匹配当前源码，v3与v2旧身份不被覆盖；固定实例向量保持 | `scoped-install-01/summary.json` |

首轮无restore的LocalRooms验证因本工作树缺assets失败，恢复后续行；首轮输入提取编译发现Point歧义及测试专用native诊断能力需保留，均已核实修正。`BoundBeltBody`继续仅供测试／诊断观察真实实现，页面运行调用使用小合同，未把CaptureState等诊断能力扩入生产合同。

独立只读审查确认输入状态、拖动释放顺序、关闭屏障及几何公式与旧实现等价，没有新增推进owner或物理算法；恢复保留已选装配。新增网络支持代码仍只操作fixture自有child，私有bootstrap／票据／原始child输出不进入日志。

## 未验证与交付边界

本次未执行前台、置顶、全局鼠标键盘或人工验收。隐藏／无窗口测试仅覆盖其实际条件，不能替代真实输入捕获、DPI、多屏、高对比系统切换或观感。既有Schema1 UNKNOWN、旧小尺寸失败、NU1903以及完整Windows／性能／多人验收边界继续保留。

发布源码身份新增`dango-desktop-settings-v4-belt-default-v4`；旧身份原样保留，存储格式与路径实例算法不变。内部候选包和发布门禁在最终源码提交后生成，结果在续行记录；不安装、不合main、不推送或部署。
