# B1a2 正常旅程通过，最终 scoped 查询失败

日期：2026-10-10。用户批准后仅执行一次固定 B1a2 计划。唯一正式客户端旅程 **1 Passed、0 Failed、0 NotExecuted**，但运行器最终退出 1，失败码 `accountsync_run_scoped_evidence_failed`；整体运行仍为 Failed。本轮额度已消耗，不能补 SQL、重复旅程或复用原清单。

2026-10-11补充：修复后另获精确批准的独立只读closure已执行一次并通过，补齐此轮现存账号的最终数据库证据，见[补验记录](2026-10-11-account-sync-b1a2-closure-passed.md)。本文件及原Failed回执保持原事实，不改写为Passed。

## 已验证事实

执行候选 HEAD `237db114cad57270868c6018cf4171a49b3f8773`；运行前工作区干净，根与独立审查的 222 个冻结输入全部匹配。批准清单 SHA256 `AECA0FCBC2BAF55EB6E8B2D9888AF32B93B3624131999EEF42B055ED220F4102`，计划 SHA256 `B50B74F640F5850BAF3269E949F5A698138374F903D430D0CC03FA45D3351E25`。执行期间没有改源码或重建候选。

真实 FormalHost、HTTPS、PostgreSQL 与三个先后完整 AppHost 完成邀请注册、首次昵称、每次登录比较、两种来源选择、记住登录恢复、在线捏脸同步及草稿保留。原生场景健康帧持续推进。昵称最终为“新同步团子”，profile_version=3；eyeSize=128，configuration_version=6；PetId 全程保持，四个会话均注销。唯一邮件保持 Pending、attempts=0，没有发送邮件。

19 条业务尝试与 10 条资源记录逐项匹配。配置写入5、Login4、Profile2、Logout4，另有邀请注册四步各1；70 次 HTTP，旅程约49.74秒。三份登录选择 archive 与三份连续同步 archive 在正式旅程中验证原始本机字节、配置、来源、身份和版本；后续独立审计仅复核非秘密投影，没有重新打开原始 archive。

before／after 完整快照分别为845838／846852字节，大小预检与完整 EOF 匹配。22 表所有原有行的完整摘要多重集保全；九类注册图各+1、sessions+4，其余12表零增量，包括四张配饰表。七项迁移一致，本轮零迁移。

三个 HWND、配置 Pump、UIA worker 均收尾；Host inFlight=0，临时 CNG key 已消失。十个工具的身份、退出、Dispose、双流和任务收尾分别确认；PostgreSQL 与 CMD 桥退出，pidfile 不存在，固定端口无监听。数据、证据、密钥和恢复代际保留。

固定虚构身份：

| 项目 | 值 |
| --- | --- |
| SourceOwnerId | `4fd0debb-576b-4410-a8da-f8f434a2e390` |
| UserId | `c0179320-d30f-421f-a43a-7fd83f72999b` |
| PetId | `aac9ef69-694d-49bf-940d-3fc0d5992cbd` |
| DefaultRoomId | `c317c4f6-07a6-4cf2-8bde-8ca5e9fc4d8f` |
| RecoveryEpoch | `67559361-c6b5-487d-b4da-948bb049000a` |

## 失败与证据边界

最终 scoped 查询包含 `nickname='新同步团子'`。原 PostgreSQL 日志准确报 `invalid byte sequence for encoding "UTF8": 0xd0 0xc2`。Windows psql 的窄 argv 经 ACP/CP936 编码，“新”恰为 `D0 C2`；其 UTF8 为 `E6 96 B0`，与声明 `PGCLIENTENCODING=UTF8` 不一致。失败发生在最终独立查询，不能据客户端结果推定该查询通过。

独立只读审计读取13份许可的非秘密证据，前后源 hash 不变，没有连接数据库、读取秘密／state 或重跑。投影 `artifacts/account-sync-b1a2-result/3bd13001100949f1ad0365916bf4bab6/independent-audit.json`，SHA256 `C4DA22FB5D18178DB11650A1A4591C542BD83AD2F948338D8808FE6D1A7ECFA5`。原 `resource-receipt.json` 正确记录 TestsPassed=false，FinalScopedGraphValidated=false。

原计划、清单、候选、TRX、数据和失败回执均保留。后续独立只读 closure 即使通过，也不覆盖或改写本次 Failed 回执。

## 后续修复

实际 SQL getter 将中文条件改为 ASCII-only PostgreSQL `U&'\65B0\540C\6B65\56E2\5B50'`，保留 UTF8 声明；运输断言拒绝非ASCII、NUL、空白和超过16384字符的输入。修复后的同一查询在 CP936 与 UTF8 下字节完全相同，Unicode 转义准确还原原昵称。

根纯离线边界检查 **416/416**，包括原字节错误复现、昵称精确解码及编码、大小、NUL反例。回执 `artifacts/account-sync-b1a2-offline/ad910a0a67d24be482e44129a01f8013/offline-results.json`。尚未为修复启动 PostgreSQL或进行新 SQL 查询。

2026-10-11，新的单次只读closure修复及离线检查完成：只核对上述现存账号及其团子、默认房、四会话与邮件，零业务写入、零迁移，不重复UI旅程。实际SQL模型47/47、根PowerShell1132/1132、故障副本红灯和独立审查证据见[新候选记录](2026-10-11-account-sync-b1a2-closure-candidate.md)，真实执行仍等待其新的精确批准。B1b异常／多设备、门廊真实技术验证和用户主观验收仍未通过；未合并main、push、部署、安装或替换生产DLL。
