# 单人家具本地 main 整合

日期：2026-10-11。用户明确“先暂停吧，我们把家具先合并了”，授权暂停后续双人物理／呈现验证，并将已人工通过的单人家具源码合入本地 main。此次为源码整合，独立开放门禁继续保留，不把合并记录当作完整 M4、Windows、正式房间或多人阶段验收。

## 范围与来源

main 起点 `a4acdebd8567be3761271f5b03cd45c06d2a4e6d`；家具来源 `codex/furniture-ball-room@4db5b845eac70e21cf9a2edc01fdbafe72a63f81`。在独立家具集成工作树中普通合并，无冲突；保留最新 main 的 AGENTS 子 Agent 模型规则及桌面规范“空间充足时图标＋短文字”。产品、工具和测试源码与家具来源的 Git 内容完全一致。

纳入 Windows 本地单人普通房的官方木平台、刚性皮球、最多 30 件混合实例、仓库双击创建待确认预览、逐操作本地保存、真实集合碰撞及球重入恢复布置锚点。门廊 E 和房内 Esc 可进入编辑；编辑时 Esc 取消捕获／预览后退出。保留三段紧凑侧栏、两列实际家具图片、选择／移动／删除／本人手套、右键镜头和碰撞重叠拒绝规则。球沿用 q 半径 1.1、质量 .02、球重力 .5；本轮不调参数或替换团子模型。

仅带入家具设计、工具、实现和验证记录；共同起点中的配饰云保存设计文档及其产品／阶段／README 条目未纳入，原设计分支保留。未带入后续 ordinary-room-physics-admission 分支、双团子语义呈现工具、authority、Linux 或后端业务实现。自定义家具、参数调节、蹦床、窗帘、云布局、多人及桌宠房家具继续后置。

## 联合候选验证

隔离产品目录为 `artifacts/furniture-collection/main-integration-r1`，回归目录为 `artifacts/furniture-main-integration/regression-r1`。构建前 HEAD 为上述 main 起点，sourceDirty=true；实际已测文件由 `source-snapshot.json` 标识。文件 SHA256 为 `D6AF20EBD51D28B3725D93F1FC48542DE7A091C327DF5165B50540031119013B`，清单规范化 hash 为 `764564B5300DB3F4D058599D2FB2631281F8E22C504B344F9A5800332AAE9A1B`。构建前后身份相同，后续只修改整合记录与文档导航。

与原人工候选源码快照核对 570 个 src／eng／tests 文件：两份原始字节 hash 因新工作树 CRLF 规范化不同，文本去除 CRLF 差异后完全一致；其余身份相同。新候选重新编译，未以旧二进制替代联合源码验证。

| 项目 | 本轮实际结果 | 证据 |
|---|---|---|
| Desktop 与友元验证工具 Release 构建 | 0 警告、0 错误 | 产品目录及 candidate-manifest.json |
| Core 家具合同 | 98/98，0 跳过 | regression-r1/results/core.trx |
| Physics 家具与受影响回归 | 146/146，0 跳过 | regression-r1/results/physics.trx |
| Store 真实文件与租约 | 48/48，0 跳过 | regression-r1/results/store.trx |
| 受影响 Desktop 回归 | 267/267，0 跳过 | regression-r1/results/desktop.trx |
| 实际产品短验证 | 通过，59.0525 秒 | 产品目录 verification/verification-summary.json |
| 静态范围与相对链接 | 无越界文件；346 个相对文件链接有效；diff 空白检查通过 | regression-r1/integration-boundaries.json 与本轮命令输出 |

实际短验证运行 DangoApplicationHost／LocalRoomWindow、原团子、同一 native 世界与真实文件：30 件（24 平台、6 球）、80 次保存、50 次房间和 50 次编辑往返、两个独立 OS 进程恢复均通过。真实输入驱动推球及运行中心／转角呈现、重入布置锚点、隐藏不推进且保持所有 native ID 通过；scheduler faults=0。保存提交边界 p95 7.0342 ms，最大 12.6269 ms。正常释放 frame owner、native world 和所有本轮所属窗口。RequiredActiveSeconds=0，不能报作持续观察或硬件／人工验收。

沿用已独立准入的 native r5 SHA256 `BA6E92D57CAFBD5CC806F709890AA1E8ED784F97CF42A09F4168EE08065E97A0`；native 源码未改，原 1507 集合检查及故意关闭碰撞的失败证据仍见[集合记录](2026-10-10-ordinary-room-ball-collection.md)。本轮重跑托管物理回归，全部独立对照模块先核对原 hash；未重复 native 构建或宣称新的 native 准入。受保护生产 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll` 前后 SHA256 保持 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。

## 人工与开放门禁

沿用用户已明确通过的单平台普通房及皮球／多家具四项反馈人工重验，见[普通房家具记录](2026-10-10-ordinary-room-furniture-implementation.md)与[反馈修正记录](2026-10-10-furniture-editor-feedback-fix.md)。本轮没有新的产品行为，不再次要求占用电脑做人工观察。

以下仍未验证：最新皮球／多家具候选的 600 秒有效活动观察；真实 OS 150% DPI、高对比和减少动画；15 项既有前台输入捕获；既有 59 FPS 专项。此前单平台的 600.054 秒不能替代本轮集合观察，267 项筛选回归也不替代被排除的前台门禁。界面沿用桌面规范 v0.1 §§2–6 的共同暖白／棕色资源、紧凑图标工具、中文提示、固定反馈及常驻细滚动条；仓库按已批准业务使用实际家具图片。没有新增布局或进行全局控件迁移。

## 暂停与交付状态

用户指令暂停的双人候选保留于原分支 `be3c2d0883cf50e45ed400729d5b62fcdc1c10cd`，工作区干净。本轮核对自有双人 review PID 24556 已不存在，无需停止其他进程；Linux 排障不恢复，P1-W 与正式多人不准入。

家具整合保留于 `codex/furniture-main-integration`，本地 main 已从 `a4acdeb` 快进至本记录所在的整合提交；已纳入的产品源码与已人工通过的家具候选一致，文档记录此次整合和暂停状态。此次按用户明确合并指令完成源码交付，家具批次的独立开放门禁仍保留，不标记完整阶段已验收。没有推送、更新日常安装、部署或替换生产 DLL。

原家具人工候选 PID 41216 在原验收目录仍存活，本轮保留；本轮短验证及其两个重启子进程、回归 testhost 已退出。所有原布局、验收产物、分支和工作树保留；本轮测试数据仅写入任务独占 artifacts，无网络服务、端口、数据库或对象存储操作。
