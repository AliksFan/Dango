# 本地邮箱服务端操作

B1 候选实现，人工邮箱投递和客户端界面尚未验收。正式入口与 Phase 2A 管道原型明确分开；正式服务只监听 HTTPS loopback，不映射匿名 `/room` Hub。当前密钥保护适配为 Windows DPAPI，其他平台须先配置等效保护。

## 配置与启动

在进程环境中配置 `DANGO_DATABASE`（显式命名 `dango_*`）、`DANGO_CONNECTION_STRING`（同名库、`127.0.0.1`、独立角色）、`DANGO_KEYS_DIRECTORY`（仓库外）、`DANGO_ENDPOINT`（HTTPS loopback）、`DANGO_RECOVERY_EPOCH`（非空 GUID）、`DANGO_TLS_CERTIFICATE`（外部 PFX 绝对路径）与可选 `DANGO_TLS_PASSWORD`。禁止把秘密放参数、源码、聊天、普通存档或输出。

恢复代际必须存放在仓库外独立维护配置中，普通重启沿用。旧备份还原必须先完成单独批准的维护隔离与全部账号恢复锁定，不能只改此配置就宣称完成恢复。

从仓库根启动：

```powershell
dotnet run --project src/Dango.Server -- --formal
```

启动检查迁移已经应用，失败只输出稳定错误码。普通启动不会建库／迁移／种子；已批准的精确数据目标才能运行 `--formal-migrate`。不得把 runtime 连接配置直接用于未经授权的既有数据库。`GET /health/ready` 检查迁移状态。Windows Schannel 不支持服务端 EphemeralKeySet；PFX 加载使用 DefaultKeySet、不使用 PersistKeySet，临时 native key container 随本次证书 Dispose 释放。

## 发件配置

使用专用发件邮箱，配置 `DANGO_SMTP_HOST`、`DANGO_SMTP_PORT`、`DANGO_SMTP_SENDER`、`DANGO_SMTP_USERNAME`、`DANGO_SMTP_PASSWORD`、`DANGO_SMTP_TLS`。TLS 必须显式为 `StartTls` 或 `SslOnConnect`；保留证书校验。MailKitLite 4.18.1 足够支持本期 SMTP／MIME，net10.0、MIT；它省略本期未用的 OpenPGP／S/MIME／DKIM。

未配置 SMTP 时任务保留并显示 `smtp_not_configured`；“queued”“smtp_accepted”“verified”是不同事实。邮件可能因 SMTP 接受后进程中断而重复，挑战仍只能消费一次。真实收件箱送达必须另外验收；受控接收器测试不代表真实邮箱送达。

## 邀请维护与客户端合同

邀请只能由维护进程签发。通过受保护进程环境传入 `DANGO_INVITATION_EMAIL` 和客户端安全生成的 256 位 base64url `DANGO_INVITATION_PROOF`（无 padding）；运行 `--formal-invite`，仅输出邀请 ID。邀请有效七天、绑定邮箱、不授予房间访问权。撤销使用 `DANGO_INVITATION_ID` 与 `--formal-invite-revoke`；维护运行权限由操作者的本地进程和外部配置保护，不开放管理 HTTP 接口。

客户端先 GET `/api/v1/auth/request-protection`，保存同一 CookieContainer 的 Cookie 与 `requestToken`，所有写操作携带 `X-CSRF-TOKEN`。登录／注销后重新领取；没有 Origin 的原生请求也不豁免。浏览器 Origin 仅允许配置的 HTTPS 来源。

开始注册提交 `email`、`invitationId`、`invitationProof`、`requestKey`，继续秘密置于 `X-Registration-Proof`，后续状态、重发、完成使用相同头。继续秘密不同于邮件证明，不得放 URL、邮件或普通存档。邮件 GET 只显示确认页，显式 POST 才消费邮件证明。

完成提交 `completionKey`、`password`、七字段 Schema 3 `appearance`。密码范围 12–128，注册完成返回固定 `userId/petId/roomId` 并不登录；同完成键 24 小时内重试不修改原密码／配置。默认 Cookie 8 小时、记住登录 7 天，绝对过期；每次请求查数据库会话与安全戳。最多 20 个有效会话，列表有界。禁用和恢复锁定不能用普通找回绕过。

## 运行边界

目前完成服务端账号层与 B2 配置接口／选择编排、本地房间存档候选；选择界面随 B5 接入。在线准入、正式 Hub、房间管理仍由 B3–B5 交付。不要仅凭登录或注册返回值宣称已经进入在线房间。

## 团子配置与本地存档（B2）

`GET /api/v1/me/pet` 返回本人 `userId/petId/configurationVersion/configuration`；`PATCH /api/v1/me/pet/configuration` 只接受 `expectedVersion` 和完整七字段 Schema 3 配置，携带同一会话的 CSRF 请求保护。版本须为正整数；服务器只更新配置和版本，不改变 Pet 身份、房间、成员、位置、睡眠或控制代际。版本冲突为 `409 pet_configuration_conflict`，非法持久配置为 `503 pet_configuration_invalid_state`，不会自动建 Pet 或默认修复。

Windows 本地存档按显式 settings 文件目录分隔：`appearance.json` 继续保留，独立 `local-rooms.json` 保存普通房与桌宠房的本地标识、固定模板、逻辑水平位置和窗口相机设置。恢复重新落地，不恢复按键、拖拽、速度或跳跃高度，不要求登录／连接服务器，也不上传位置。当前产品路径在确认健康宿主后周期保存，并在隐藏、切换与退出时保存；失败目标不能覆盖健康检查点。写失败保留本次游玩及待保存状态，自动重试并提供退出时重试／继续使用选项；未知／损坏／外部变化保护原文件，须检查原文件后重启以重新读取，本批没有新增房间存档修复界面。外观继续兼容既有 UTF-8／16／32 BOM，归档保留原始字节。

配置选择通过独立 `PetConfigurationChoiceCoordinator` 编排，B5 提供已登录、固定 HTTPS origin 与 User／Pet 身份的客户端。创建和预览不会覆盖数据；采用来源前先将本地原字节（含缺文件标记、旧版本）、规范配置、云端原配置／版本及选择意图持久归档。目录按 origin 哈希、UserId、PetId 分隔；归档不含 Cookie、密码或注册证明。归档失败阻止两边变更；本地未保存草稿、原文件变化也阻止覆盖。档案是本机保留副本，不等于服务器配置历史、设备丢失恢复或数据库灾备。

响应丢失只重试原 expectedVersion；409 后读取云端并按配置语义比较。相同只证明当前目标已满足，不证明原响应成功送达；不同保留冲突，不能自动提高版本覆盖。重新读取档案不重放请求。结果 receipt 与原档分开，receipt 写失败不能推定业务变更失败；归档中的选择意图仍可读取。

原子写入先 Flush(true) 再替换同目录目标；同一规范路径的 Dango writer 使用互斥锁协调。此边界不提供非合作外部应用、UNC／SUBST 别名或恶意目录替换的文件系统 CAS。实际断电与设备／数据库备份还原仍须独立验证。

本机自动测试通过 opt-in 精确目标执行，数据和密钥保留。测试后只停止本次进程／监听；清理、备份还原、真实 SMTP、main 合并、推送、部署和日常安装均需对应授权。

依赖官方来源：[MailKitLite nuspec](https://api.nuget.org/v3-flatcontainer/mailkitlite/4.18.1/mailkitlite.nuspec)、[MimeKitLite nuspec](https://api.nuget.org/v3-flatcontainer/mimekitlite/4.18.1/mimekitlite.nuspec)、[SMTP 源码](https://github.com/jstedfast/MailKit/blob/4.18.1/MailKit/Net/Smtp/AsyncSmtpClient.cs)、[MIT 说明](https://github.com/jstedfast/MailKit/blob/4.18.1/README.md)。
