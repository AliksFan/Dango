# B5C Windows 人工验收环境 R2 精确重试计划

日期：2026-10-10。状态：**用户已确认批准；R2 真实环境及客户端已就绪，保持运行等待人工操作，收尾尚未发生**。

## 前次尝试和修复

原[人工计划](2026-10-10-backend-b5c-windows-manual-plan.md)获用户批准后已尝试启动。用户在 Windows 系统警告确认了本次公钥信任；独立 PostgreSQL 成功启动，但维护检查错误地使用默认 `__EFMigrationsHistory`，正式表为 `dango.__ef_migrations_history`，因而在邀请码签发前停止。已有数据库不需要迁移；此次没有初始化、迁移、业务种子写入、账号、挑战、邮件或候选客户端启动。

已停止本次 PostgreSQL 及宿主，清理本次 Root 信任及指定两槽，释放临时服务私钥容器；实查记录 PID／监听／pidfile 消失、公钥不再受信任，助手正常退出。原数据库、原 credential／key ring／恢复代际及 `run20261010b5cmanual` 全部保留。

修复将维护检查接入正式共用 `DangoDatabaseOptions.Create`，包括 PostgreSQL 18、迁移历史 schema、timeout 和敏感日志关闭。新增离线 metadata SQL 反例在旧实现下确实失败；修后完整工具门禁 125 项。助手另记录非秘密启动阶段／稳定错误码，以便发生中途失败时定位，不输出原始异常、连接串或凭证。

## 本次请求的精确授权

只能在以下 R2 范围执行一次准备与人工验收：

- 再次启动原当前用户 LocalApplicationData 下 `Dango/BackendDevelopment/run20261008b0/pgdata` 的既有独立 PostgreSQL 18，仅监听 `127.0.0.1`；数据库 `dango_backend_b0`、角色 `dango_b0_owner`、记录端口、credential、key ring、recovery epoch 均沿用。不连接系统 PostgreSQL 服务。
- 原人工目录不覆写、不删除。新建且仅使用受当前用户 ACL 保护的 `Dango/BackendDevelopment/run20261010b5cmanualr2`。已有目录、reparse、原实例或同模式候选运行时先拒绝。
- R2 生成唯一一份新临时 TLS 证书：SAN 仅 `127.0.0.1`、仅 server-auth、非 CA、最多3天。PFX、DPAPI 密码和非秘密回执只写 R2 受保护目录；当前用户 Root 仅导入这份公钥。若 Windows 弹出系统安全确认，由用户核对本次名称／指纹后操作。不得重新信任或删除前次证书及其他证书。
- R2 选择一个两槽均为空的临时 loopback HTTPS origin，然后全程固定。只允许该 origin 精确 SHA256 对应的 WinCred `/session` 与 `/registration` 两槽正常读写／删除；不得遍历、解析或覆盖其他凭据。
- 签发一份新随机 `.invalid` 邮箱邀请。使用正常客户端最多创建1个邮箱账号及一份预注册／邮箱挑战／未投递邮件／唯一 Pet／默认桌宠房和房主席位。密码由用户设置。邀请及确认 proof 仅在内存或本机助手临时显示，不写明文、命令行或日志。
- R2 最多2个共同房、3次登录尝试／最多3个会话、4次明确 Enter／最多4历史租约、2次公共码重置、2次配置写入。沿用原宿主写前 guard 的摘要、归属、串行并发和失败不退款规则；审计／事件按正式生命周期追加保留。到额度或失败停止，不自动补数据、提高配额、重启清空预算。
- 继续直接使用正式宿主，明确关闭 Email、Expiry、Live、Outbox workers。维护只确认本轮已捕获 pendingID，核验挑战后走正常 TLS／Origin／fresh CSRF HTTP；没有真实 SMTP 邮件和全库任务。收尾只按本轮 ID 读 actual counts／邮件 Attempts 为零／无 dispatch 的证据。
- 客户端候选、隔离设置目录和操作步骤沿用原计划。每次重开由同一 launcher 传入同一 R2 origin，最多4次启动；不使用日常安装和生产 DLL。
- 结束先确认所有本轮候选真实退出，再有界 Stop／Dispose 本次宿主和临时 native server key，仅停止本次 PostgreSQL、清 R2 专用两槽及这份公钥信任。保留数据、文件与回执。不能证明归属或停止时报告，不盲目 kill。

R2 不授权迁移、初始化、重置、新建库、永久业务删除、备份还原、原目录变更、main 合并、推送、部署、日常安装或生产 DLL 替换。仍需用户人工验收；多人移动／动作与画面未接入，不能关闭完整 B5。

再次启动或更换 R2 资源目标仍需新的精确决定，不把这次批准当无限重试授权。
