# B3 独立实例测试操作目标

日期：2026-10-09。状态：用户在本次精确计划后明确回答“允许”，已按计划执行本次前向迁移、虚构测试及限定房间永久清理；105 项真实集成全部通过。独立实例与测试监听已结束，数据／代际／密钥保留。详见[验证记录](../verification/2026-10-09-email-backend-b3.md)。授权不包含最后一节排除的其他操作。

## 精确环境与变更

- 只启动当前用户 LocalApplicationData 下既有 `Dango/BackendDevelopment/run20261008b0/pgdata`，数据库／角色固定为 `dango_backend_b0`／`dango_b0_owner`。不初始化新实例、创建新库或换目标；所有目录祖先拒绝 reparse point。
- 端口只从同目录外部 `state.json` 读取，密码只从 `credential.dpapi` 解密到子进程环境；不输出、写命令行或提交。保留既有 `b1test-state.json` 恢复代际和 `b1tests-keys`，缺少即拒绝，不生成替代配置。
- 仅前向应用 `20261009031410_RoomManagement`。启动后先核对数据库、物理 data_directory 及 PostgreSQL 18；待应用迁移必须为空或恰好为上述一项，其余情况拒绝。不会回退迁移或执行 Down。
- 迁移增加 `audit_logs`／`domain_outbox` 表与唯一、检查、查询索引；`rooms` 增加 `template_id`／`template_version`，既有 kind=0 桌宠房回填 `desktop-pet-default`／1，其余固定为 `ordinary-default`／1；增加模板检查，将到期索引扩至 status 1、2。旧账号／团子／房间 ID、名称、成员、归属和版本不被迁移更换；B0 probe 保留。

## 虚构写入与永久删除范围

- 执行 B1／B2／B3 实际 PostgreSQL 回归，共 105 个已发现案例；默认排除 B0 迁移组。本批 B3 52 个案例包含管理 31、到期 16、HTTPS 3、真实恢复／清理 2。未获授权的测试只会在 fixture 创建前拒绝。
- 注册、邀请、挑战、队列、会话、团子、默认桌宠房及共同房全部使用本次随机虚构标识和 `.invalid` 邮箱；既有 B1 邮件调度回归可按既有策略推进独立测试库内虚构邮件任务状态，接收器仅本机内存，不连接真实 SMTP。
- 管理测试允许创建、版本更新、解散及恢复本次测试房间，故障反例回滚；席位／归属测试仅写本次虚构账号与团子记录。审计和 outbox 同事务写入，保留原操作确认，outbox 不实际投递。
- **永久删除仅允许到期测试函数调用中明确列出的、本次新生成共同房 ID**。列表必须非空、无空 ID、无重复且最多 50；每次按期限重新检查。删除该目标房间与其成员，解除该房残余实际归属，保留账号／团子身份、审计／outbox。不运行不限定 ID 的生产到期 worker，不删除既有 B0／B1／B2 房间。
- 提交成功的虚构数据保留；不批量清空、重置、还原、删除数据库／数据目录／备份／凭据／密钥。单个失败事务回滚不等于执行恢复操作。

## 网络、进程及证据

- PostgreSQL 只监听 IPv4 loopback；HTTPS／受控 SMTP 使用本机临时端口。正式测试宿主 email worker 与 room expiry worker 默认关闭，邮件案例显式受控调度；不启动持续运行的全库房间清理。
- TLS 证书仅内存、自签、客户端固定证书验证，不安装系统信任证书。Windows 临时 key container 随本次证书 Dispose 释放，不持久保存 PFX。
- 验证包括独立数据库连接竞争第三房额度／同版本更新、提交前逐步故障回滚、丢弃已接受响应正文后原确认重放、新宿主读库、精确 168h 恢复期限以及真实管理恢复与系统清理锁竞争。已有身份和非目标房间图必须保持预期事实。
- TRX／迁移 SQL 存放仓库忽略目录 `artifacts/backend-b3/` 和既有 `artifacts/backend-foundation/postgres-tests/`；不含连接串、秘密或原始异常明细。构建与查询翻译不能算真实 PG 行为通过。
- 工具 `eng/invoke-backend-postgres-tests.ps1 -B3` 不带执行开关只展示目标；获批后才增加 `-ExecuteApprovedPlan`。PostgresBinDirectory 从当前已安装的 PostgreSQL 18 二进制选择，不安装依赖。工具同时设置 B1 fixture 与 B3 独立许可标记；旧 -B1 只允许 AccountLifecycle，不能顺带本次迁移。
- 结束仅停止本次启动且路径／PID／创建时间已核实的独立实例及本次 HTTPS／SMTP 监听，报告残留 PID／端口。实例数据、代际、密钥和测试证据保留；用户既有 PostgreSQL 服务不连接、不修改、不停止。

## 未包含的授权

备份／真实还原、OS 进程强杀／断电、生产最小权限角色、真实发信、B4 Hub／公共码／正式准入、B5 UI／人工验收、main 合并、推送、部署、日常安装及受保护 DLL 替换均不包含。集成通过也不能把完整首期或 B3 人工验收标记为完成。
