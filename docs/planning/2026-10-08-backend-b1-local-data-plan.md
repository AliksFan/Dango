# B1 独立实例测试操作目标

状态：用户明确批准精确目标后已执行；最终 42 项真实集成门禁通过，实例停止，数据保留。详见[验证记录](../verification/2026-10-08-email-backend-b1.md)。只复用 B0 用户批准的独立实例，不接既有 PostgreSQL 服务。

- 目录：当前用户 LocalApplicationData 下 `Dango/BackendDevelopment/run20261008b0/pgdata`；只使用已批准的 B0 物理目标。
- 数据库／角色：`dango_backend_b0`／`dango_b0_owner`。连接端口从受保护外部 `state.json` 读取，密码只从 `credential.dpapi` 解密进子进程环境，不输出。
- 前向迁移：`20261008085521_AccountLifecycle`，增加 users 禁用／恢复锁定字段及 test_invitations、pending_registrations、email_challenges、email_jobs、auth_sessions 五表、检查／外键／索引；保留 B0 对象和 probe。
- 测试写入：仅随机虚构 `.invalid` 账号、邮箱邀请摘要、注册挑战、邮件状态、会话及其一套团子／默认房。服务事务反例回滚；已提交测试事实保留，不执行清空／重置／批量删除。
- 外部测试资料：在同一当前用户 ACL 保护的 state 根下生成 `b1tests-keys` 的 DPAPI 保护 key ring 及非秘密测试恢复代际文件；TLS 自签 PFX 只在内存，用客户端固定证书验证，不安装信任证书。Windows Schannel 要求临时 native key container；不使用 PersistKeySet，证书 Dispose 释放本次容器，PFX 缓冲清零。
- 网络：HTTPS 和受控 SMTP 接收器仅监听本机临时端口；接收虚构邮件到内存，不用真实 SMTP、不实际投递邮箱；测试结束关闭监听。
- 执行工具：`eng/invoke-backend-postgres-tests.ps1 -B1 -ExecuteApprovedPlan`（本机 RunName／PostgresBinDirectory 通过参数）。不带执行开关只展示计划。B1 测试额外要求 `DANGO_BACKEND_B1_TEST_APPROVED=1`。
- 结束：只停止本次启动的独立 PostgreSQL 实例，保留数据、受保护密钥和日志，报告进程、端口与数据。备份／还原、删除、main 合并、推送、部署和安装均不包含。

B0 测试检测尚未应用的 B1 迁移；没有 B1 精确授权时不能通过旧的执行开关顺带应用新迁移。
