# 本地房间目录与每房存档实施记录

日期：2026-10-15。分支 `codex/local-room-catalog-save`，按[已批准设计](../planning/2026-10-15-local-room-catalog-save-design.md)和[实施计划](../planning/2026-10-15-local-room-catalog-save-plan.md)形成本地实现候选。下列定向自动和实际宿主验证通过，**人工验收待用户确认，完整Desktop门禁仍有既有阻断**。未合main、推送、安装或部署。

## A 目录与真实文件存储

已实现严格版本化目录、稳定房间ID、统一3间额度、新建／解散／7天恢复、到期文件保留、导入准备记录与旧档复制保护，以及带房间ID的家具schema 3。复用既有writer和布局原子提交；未增加生产依赖、物理或native改动。

- 静态：Desktop Release构建0警告、0错误；`git diff --check`通过。
- 定向自动：`catalog-a-local-r3.trx` 62/62、零跳过，包括原家具存储用例。r2保留2个失败：无效数据异常未进入存储保护分支；已修正异常分类，再跑原断言通过。
- 完整Desktop门禁：首次还原因既有 `Microsoft.AspNetCore.SignalR.Common 10.0.11` 的NU1903失败，原失败保留；没有升级依赖、关闭审计或降低严重级别。新增 `Dango.LocalRooms.Tests` 只引用Desktop并链接本批及既有家具存储测试，不经过无关多人样本依赖。定向通过不替代完整门禁。
- A批当时尚未执行两个真实进程及真实Windows交接；后续结果见下文，不把测试中的重新实例化记为跨进程恢复。

## B／C 每房恢复与门廊入口

已接通三个普通房门位、空门位新建、目标房门旁进入／布置／解散、桌宠房门及恢复标牌。创建、恢复均不自动入房；解散经过两次确认。表单使用临时模态浮层，阻止背景及团子输入，取消不落盘，关闭恢复来源焦点。目录受保护时保留重新读取标牌，不覆盖损坏文件。

界面采用[桌面规范v0.1](../desktop-ui-design-v0.1.md)（2026-10-10）第2–6节：复用Home语义资源、暖白与棕色细强调；场景旁的短动作及中文辅助名称、40 DIP文字动作、失败／确认保留文字；浮层内容独立滚动、操作固定，细条视觉6 DIP／命中16 DIP／最短滑块28 DIP。页面差异沿用获批设计：导航附着房门和标牌，表单临时覆盖场景，无常驻管理侧栏。实际键鼠手感、DPI和辅助设置矩阵仍待验，不把本批局部滚动样式记为全站滚动卡顿修复。

每房绑定独立家具schema 3与恢复文件，切房保留同一团子／运行实例／唯一帧owner。后台准备有代数取消及IO串行；最终绑定和健康首帧确认重新取得目录资格，阻止已解散目标的迟到回调。五秒检查点及返回／隐藏／退出保存使用后台IO；家具编辑使用原用户镜头，临时全景不成为持久镜头。位置／视角保存失败保留原件，并在首页保留源房提示，不阻断可信家具游玩。

目录发布标记防止已发布目录缺失后被误当首次启动；目录、恢复和选择文件采用有界 `.previous` 最近正确副本。家具身份文件在后台暂存阶段保护原字节，再沿用既有物理边界提交。结果不明不盲目重放或覆盖，保护副本不可确认时拒绝写入。

## D 最终自动与真实进程证据

所有路径相对本Worktree的 `artifacts/local-room-catalog/`。

| 层次 | 实际结果与证据 |
|---|---|
| 构建／格式 | r4隔离候选Desktop及probe Release 0警告、0错误；4个受影响项目、29个C#文件的局部空白格式及 `--verify-no-changes` 通过；`git diff --check`通过 |
| 最终定向自动 | `results/catalog-final-r4.trx` **132/132、零跳过**；包含目录／旧档导入／恢复／镜头／门廊WPF和受影响家具、编辑、交接、配饰外观测试 |
| Core | `results/core-final.trx` **135/135、零跳过**；此后Core源码未改，沿用本次实际结果 |
| 真实跨进程 | `review-r4/verification.json` 的 `passed=true`；P1写入并退出后P2逐房核对布局、身份、版本、位置和镜头；两个进程竞争最后名额仅一个成功；终止本任务检查点进程后恢复最后成功点；终止导入进程后复用准备身份且旧文件摘要／内容不变 |
| 实际Windows宿主 | `runs/r4-ui/ui-check-result.json`：首页启动无自动入房、取消准备、独立进程在最终绑定前解散目标、A→B→A布局／镜头／实体／唯一owner、家具临时镜头保护及桌宠双层交接通过；使用实际 `DangoApplicationHost` 和准入native候选 |
| 完整Desktop门禁 | **未通过**：预存 `Microsoft.AspNetCore.SignalR.Common 10.0.11` 的NU1903，公告 `GHSA-334f-vvp4-7g6p`；没有升级依赖、关闭审计或降低严重级别。定向工程避开无关多人样本依赖，不能替代完整门禁 |
| 延后壁纸项 | 定向运行明确排除既有 `SavedAppearanceFollowsTheSameEntityAcrossRoomWindowWallpaperAndReturn`；普通房壁纸仍按既定范围搁置，未声称通过 |
| 人工／系统矩阵 | 门廊观感、连续真实键鼠、最小／纵向窗口、OS DPI、高对比、减少动画及辅助输入专项**未验证**；真实控件自动测试不替代人工操作 |

r4构建前后源码清单摘要一致：`88015D72741B7E34A51BD5DDD41EE95F972F48F67DB99ECC2F87AE1611DFE979`，完整路径及逐文件摘要见 `review-r4/source-snapshot.json`。该清单记录src／eng／tests与构建配置，构建起点为A批 `b03a061` 加本任务后续实现；文档收尾不属于该源码摘要。

DLL来源与SHA256：

- 使用 `codex/furniture-ball-room` 已准入家具集合候选：`BA6E92D57CAFBD5CC806F709890AA1E8ED784F97CF42A09F4168EE08065E97A0`；验证脚本显式传入，只读引用，不重新编译。
- 缺少集合ABI的拒绝测试使用 `codex/furniture-room-integration` 旧候选：`EFEBFC2E8F93953F8DA91244BF70363773A01E58C07C490A87B56C25BEC4815A`。
- 本任务生产保护文件 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll`：`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`，前后不变。无Physics、native实现、物性或参数改动。

保留的失败证据：A-r2异常分类、C-r2 Dispatcher线程归属和C-r3控件布局遍历失败均已定位修正；`catalog-final.trx` 初次125/126及 `catalog-ui-final-r2.trx` 暴露了投影重设未刷新附近动作，现已同步刷新投影及动作并在用例明确靠近目标；`catalog-lifecycle-red.trx` 的2项确定性旧回调入已解散房失败，修复后 `catalog-lifecycle-green.trx` 6/6、r2阶段130/130通过。随后 `catalog-ground-red.trx` 确定性暴露平台支撑被当作地面检查点，补真实地面接触条件；`catalog-final-r3.trx` 的130/131暴露首页通用刷新会清空源房保存告警，改为每房保留未解决告警，其他房成功不清除。两项定向修复及门廊UI在 `catalog-ground-notice-green.trx` 6/6通过，最终r4为132/132。未降低断言或删除失败记录；适配器快照注入验证的是检查点消费规则，不冒称实际平台跳跃操作验收。

## 限制、人工入口与交付

当前准入放置接口仅充分支持水平恢复，首版保存最近可确认的**安全地面检查点**；跳跃和平台上的瞬时位置不作为重启姿态，检查点失效回安全出生区。恢复不保存速度、抓取、睡眠阶段和球运行态，球跨进程回布置锚点。旧版只看到保留的旧固定房文件，不能读取本版多房数据或自动接收新修改。真实服务端授权、开放房下载、账号同步和跨设备目录冲突均未接入。

人工入口为 [run-local-room-catalog-review.ps1](../../eng/run-local-room-catalog-review.ps1) 的 `-Show`；本次r4已启动并检查进程存活，窗口标题为“Dango 控制中心”，从首页主动进门廊。手动进程ID见 `review-r4/verification.json` 的 `manualProcessId`，该进程保持供用户验收；其余记录的自动probe子进程均已退出。旧r2验收进程经身份核对后已结束，旧数据保留；退出窗口不自动清除测试数据，再次完整运行须给全新 `RunName`，不能复用或覆盖r4。

进程验证数据和日志保留在 `runs/r4`、`runs/r4-import`、`runs/r4-ui` 与 `review-r4`，均为本任务标记的虚构隔离目录；自动用例数据另保留于测试输出及本Worktree的 `artifacts/furniture-room/`。无日常存档、真实账号、数据库、服务或新增监听端口。失败与历史运行同样保留，不做持久清理。本批在任务分支创建本地提交，待用户人工验收；未合main、推送、更新日常安装、部署或替换生产DLL。既有整体Windows、59 FPS和后续物理整合门禁继续开放。
