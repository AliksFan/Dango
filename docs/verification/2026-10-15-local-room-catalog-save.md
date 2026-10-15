# 本地房间目录与每房存档实施记录

日期：2026-10-15。分支 `codex/local-room-catalog-save`，按[已批准设计](../planning/2026-10-15-local-room-catalog-save-design.md)和[实施计划](../planning/2026-10-15-local-room-catalog-save-plan.md)推进。整体待实施完成及人工验收，未合main、推送、安装或部署。

## A 目录与真实文件存储

已实现严格版本化目录、稳定房间ID、统一3间额度、新建／解散／7天恢复、到期文件保留、导入准备记录与旧档复制保护，以及带房间ID的家具schema 3。复用既有writer和布局原子提交；未增加生产依赖、物理或native改动。

- 静态：Desktop Release构建0警告、0错误；`git diff --check`通过。
- 定向自动：`catalog-a-local-r3.trx` 62/62、零跳过，包括原家具存储用例。r2保留2个失败：无效数据异常未进入存储保护分支；已修正异常分类，再跑原断言通过。
- 完整Desktop门禁：首次还原因既有 `Microsoft.AspNetCore.SignalR.Common 10.0.11` 的NU1903失败，原失败保留；没有升级依赖、关闭审计或降低严重级别。新增 `Dango.LocalRooms.Tests` 只引用Desktop并链接本批及既有家具存储测试，不经过无关多人样本依赖。定向通过不替代完整门禁。
- 集成／实时：尚未执行两个真实进程、实际产品门廊或真实Windows交接。本批测试中的重新实例化不记为跨进程恢复。
- 人工验收：未执行。

证据均在本Worktree的 `artifacts/local-room-catalog/`，数据为虚构隔离目录。未操作日常用户存档、账号、数据库或服务，未新增监听端口；构建／测试进程已正常结束，测试文件及证据保留。
