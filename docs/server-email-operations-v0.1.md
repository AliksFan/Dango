# 本地邮箱服务端操作

B1 候选实现，人工邮箱投递和客户端界面尚未验收。正式入口与 Phase 2A 管道原型明确分开；正式服务只监听 HTTPS loopback，不映射匿名 `/room` Hub。当前密钥保护适配为 Windows DPAPI，其他平台须先配置等效保护。

## 配置与启动

在进程环境中配置 `DANGO_DATABASE`（显式命名 `dango_*`）、`DANGO_CONNECTION_STRING`（同名库、`127.0.0.1`、独立角色）、`DANGO_KEYS_DIRECTORY`（仓库外）、`DANGO_ENDPOINT`（HTTPS loopback）、`DANGO_RECOVERY_EPOCH`（非空 GUID）、`DANGO_TLS_CERTIFICATE`（外部 PFX 绝对路径）与可选 `DANGO_TLS_PASSWORD`。禁止把秘密放参数、源码、聊天、普通存档或输出。

恢复代际必须存放在仓库外独立维护配置中，普通重启沿用。旧备份还原必须先完成单独批准的维护隔离与全部账号恢复锁定，不能只改此配置就宣称完成恢复。

从仓库根启动：

```powershell
dotnet run --project src/Dango.Server -- --formal
```

启动检查迁移已经应用，失败只输出稳定错误码。普通启动不会建库／迁移／种子；已批准的精确数据目标才能运行 `--formal-migrate`。不得把 runtime 连接配置直接用于未经授权的既有数据库。`GET /health/ready` 检查迁移状态。

## 发件配置

使用专用发件邮箱，配置 `DANGO_SMTP_HOST`、`DANGO_SMTP_PORT`、`DANGO_SMTP_SENDER`、`DANGO_SMTP_USERNAME`、`DANGO_SMTP_PASSWORD`、`DANGO_SMTP_TLS`。TLS 必须显式为 `StartTls` 或 `SslOnConnect`；保留证书校验。MailKitLite 4.18.1 足够支持本期 SMTP／MIME，net10.0、MIT；它省略本期未用的 OpenPGP／S/MIME／DKIM。

未配置 SMTP 时任务保留并显示 `smtp_not_configured`；“queued”“smtp_accepted”“verified”是不同事实。邮件可能因 SMTP 接受后进程中断而重复，挑战仍只能消费一次。真实收件箱送达必须另外验收；受控接收器测试不代表真实邮箱送达。

## 邀请维护与客户端合同

邀请只能由维护进程签发。通过受保护进程环境传入 `DANGO_INVITATION_EMAIL` 和客户端安全生成的 256 位 base64url `DANGO_INVITATION_PROOF`（无 padding）；运行 `--formal-invite`，仅输出邀请 ID。邀请有效七天、绑定邮箱、不授予房间访问权。撤销使用 `DANGO_INVITATION_ID` 与 `--formal-invite-revoke`；维护运行权限由操作者的本地进程和外部配置保护，不开放管理 HTTP 接口。

客户端先 GET `/api/v1/auth/request-protection`，保存同一 CookieContainer 的 Cookie 与 `requestToken`，所有写操作携带 `X-CSRF-TOKEN`。登录／注销后重新领取；没有 Origin 的原生请求也不豁免。浏览器 Origin 仅允许配置的 HTTPS 来源。

开始注册提交 `email`、`invitationId`、`invitationProof`、`requestKey`，继续秘密置于 `X-Registration-Proof`，后续状态、重发、完成使用相同头。继续秘密不同于邮件证明，不得放 URL、邮件或普通存档。邮件 GET 只显示确认页，显式 POST 才消费邮件证明。

完成提交 `completionKey`、`password`、七字段 Schema 3 `appearance`。密码范围 12–128，注册完成返回固定 `userId/petId/roomId` 并不登录；同完成键 24 小时内重试不修改原密码／配置。默认 Cookie 8 小时、记住登录 7 天，绝对过期；每次请求查数据库会话与安全戳。最多 20 个有效会话，列表有界。禁用和恢复锁定不能用普通找回绕过。

## 运行边界

目前完成服务端账号层，在线准入、正式 Hub、房间管理、客户端离线／云端配置选择分别由 B2–B5 交付。不要仅凭登录或注册返回值宣称已经进入在线房间。

本机自动测试通过 opt-in 精确目标执行，数据和密钥保留。测试后只停止本次进程／监听；清理、备份还原、真实 SMTP、main 合并、推送、部署和日常安装均需对应授权。

依赖官方来源：[MailKitLite nuspec](https://api.nuget.org/v3-flatcontainer/mailkitlite/4.18.1/mailkitlite.nuspec)、[MimeKitLite nuspec](https://api.nuget.org/v3-flatcontainer/mimekitlite/4.18.1/mimekitlite.nuspec)、[SMTP 源码](https://github.com/jstedfast/MailKit/blob/4.18.1/MailKit/Net/Smtp/AsyncSmtpClient.cs)、[MIT 说明](https://github.com/jstedfast/MailKit/blob/4.18.1/README.md)。
