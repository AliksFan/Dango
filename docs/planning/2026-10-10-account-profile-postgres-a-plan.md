# 昵称资料 A-only 本地 PostgreSQL 验证计划

状态：候选，尚未获本轮真实执行授权。仅编写、构建、离线门禁不代表已经执行此计划。根工作树整合及最终构建后，生成一次冻结清单；审批请求必须包含该清单的准确路径与 SHA256，不能只批准可变的文件名。

## 1. 唯一目标和范围

- 既有独立实例：当前用户 LocalAppData 下 `Dango/BackendDevelopment/run20261008b0/pgdata`；PostgreSQL 18 的固定安装 `ProgramFiles/PostgreSQL/18/bin`。
- 只监听 `127.0.0.1`，仅库 `dango_backend_b0`、角色 `dango_b0_owner`。端口读取原 `state.json`，启动前要求原目标停止、pidfile 不存在、该端口无监听；启动后核对实际进程、参数、时间、库、角色、data_directory、版本及 listen_addresses。
- 沿用该实例既有 `credential.dpapi`、`b1tests-keys`、`b1test-state.json` 恢复代际，不创建新的业务密钥、不重建或替换原材料。
- 新建一次性受保护证据目录：同级 `run20261010profilea`。目录已存在即拒绝，不退款、不续跑、不自动 retry。
- 历史必须恰为 InitialAccountRoomFoundation、AccountLifecycle、RoomManagement、RoomAccess、RoomAccessIntegrity 五项（对应原 20261008/09 迁移 ID）；唯一前向目标 `20261010055529_AccountProfile`。正式迁移入口须精确执行到此目标、拒绝后退或未来迁移。
- 只运行 `FullyQualifiedName~Dango.Server.Tests.Accounts.AccountProfilePostgresTests` 一次，7 个实际 Passed；不 build/restore、不运行旧 B1、B3/B4/B5、其他测试类或维护任务。

## 2. 固定输入和入口

入口为 `eng/invoke-account-profile-postgres-tests.ps1`，模块为 `eng/Dango.AccountProfilePostgres.psm1`。无执行开关只读取工作区输入；`-PrepareApprovalManifest` 仅在该工作树 artifacts 下 CreateNew 写一次审批清单，不读取凭据、探测端口或操作 PostgreSQL。

根整合并最终构建后准备清单：

```powershell
./eng/invoke-account-profile-postgres-tests.ps1 -PrepareApprovalManifest -ApprovalManifest ./artifacts/account-profile-a-approved.json
```

清单冻结 Server/Test 两个 Release 输出目录的 `.dll`、`.json`、`.exe`，以及 AccountProfilePostgresTests、ProfileTestOwner、ProfileTestSupport、唯一迁移源和两个运行器文件，路径排序、逐份 SHA256。根在实际审批请求中写入生成的清单 SHA256，不使用占位值。

只有本段获得明确批准后，根才在同一工作树执行：

```powershell
./eng/invoke-account-profile-postgres-tests.ps1 -ExecuteApprovedPlan -ApprovalManifest <已审查的绝对路径> -ApprovalManifestSha256 <已批准的64位大写SHA256>
```

执行前与每次迁移／测试前均核对固定清单 digest 和当前输入；批准后一起改清单与 DLL 也拒绝。子进程清除继承的 DANGO/PG 环境，再传仅本次精确配置。连接串与密码仅在内存及精确子进程环境中，不打印、写入清单或命令行。

## 3. 一次全套额度

| 操作 | 尝试上限 |
| --- | ---: |
| Invite / Start / Confirm / Complete，各操作 | 各 9 |
| Login | 10 |
| DirectProfileUpdate | 11 |
| HttpProfilePatch | 8 |
| SessionRevoke / HttpLogout，各操作 | 各 1 |
| 全部变更尝试合计 | 67 |

7 case 的尝试数依次为 concurrent_profile 7、duplicate_names 14、rollback_after_profile 6、rollback_before_commit 6、cross_session 13、expiry 6、https 15。Case 内各操作分配也固定；拒绝、回滚、HTTP 400/403、重放同样耗数，19 次资料修改中只有 5 次提交成功。

Profile 专用 helper 先验证批准、存活 owner 的 PID／创建时间／可执行路径、固定 owner-manifest 与 protected run directory，随后逐次 durable 预留、Flush(true) 再执行。短锁只保护记账，不串行化两笔正式并发事务。账本 IO 失败不调用、不退次数。

预期新增 9 个虚构 `profilea-{OwnerId:N}-{random:N}@example.invalid` 账号、Pet、默认桌宠房、房主席位、PetRoomState，以及各 9 邀请／预注册／挑战／未投递 EmailJobs，10 sessions、其中 2 revoked。本轮不修改旧业务图。EmailJobs 必须 Pending、Attempts=0、AcceptedAt／Lease 均空，无 dispatcher、SMTP receiver 或真实邮件。

## 4. 资源、证据和收尾

只启动一次上述独立 PostgreSQL；工具超时仅结束已证明归属的工具 handle，pg_ctl 不树杀 PostgreSQL。失败后只有 postmaster 的准确 executable、7 项参数、目标路径／host／port、创建时间与已证明 pg_ctl 父子关系满足时才允许停止；归属不明不盲目 kill，报告残留。

7 fixtures 各生成一份短期、不导入系统信任的测试证书；仅 https case 启动一次临时 loopback HTTPS，用精确 pin。`resources.jsonl` 的 10 字段为 Version、OwnerId、Sequence、FixtureId、CaseId、Phase、RequestedHttps、HttpsStarted、NativeKeyDisposeConfirmed、AtUtc。正常每份 preparing → ready → dispose_started → disposed，共 28 条、7 distinct fixture、恰一份曾启动 HTTPS；只有正式 fixture DisposeAsync 正常返回后 disposed 才确认 native key 清理。失败／timeout 不推定证书已清理，不重开测试。

自动核验 TRX 7 个精确用例、67 条尝试及逐 case 预算、28 条资源生命周期。正式只读 SQL 使用 `BEGIN READ ONLY` 与 psql quiet framing；history 列按已离线验证的 `"MigrationId"` 引用。迁移前后／用例后比较 18 张业务及身份表旧记录不可恢复 SHA256 摘要，用户摘要排除新增的 nickname/profile_version，另验旧用户 nickname=NULL、profile_version=1。最终核对本轮 9 个完整注册图、10 同归属／代际会话、4 个有昵称用户、5 次 version 增量、2 撤销和 9 未投递邮件；不输出密码、stamp、配置原文或原始 rowJSON。

收尾先完成 fixture Dispose，最后仅停止证明归属的本次 PostgreSQL，核对其 PID、pidfile 和记录端口退出。保留全部原数据、新虚构数据、原凭据／keys／epoch、固定清单、账本、回执、TRX；结束不删除或还原数据。资源回执分别报告工具退出、PostgreSQL 清理和 fixture native key 清理，缺证据写未确认。

这段批准不包含后续联合计划的 UIA、捏脸会话同步真实数据、门廊 CRUD、系统证书信任或 WinCred 操作，也不含 main 合并、push、部署、日常安装、生产 DLL 替换、备份还原与数据库崩溃演练。后续范围另行审查；助手承担能自动执行的技术验证，用户主要验收手感、界面和流畅度。
