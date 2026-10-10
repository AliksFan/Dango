# B1a2 单次只读核对候选

日期：2026-10-11。编码修复、固定只读运行器与离线检查完成，等待[精确closure计划](../planning/2026-10-10-account-sync-b1a2-closure-plan.md)的新批准。此次仅核对现存虚构账号；原B1a2旅程Passed、整个运行Failed的事实及回执保留，见[原记录](2026-10-10-account-sync-b1a2-scoped-query-failure.md)。

## 最终实现

scoped SQL使用ASCII-only Unicode literal，精确还原“新同步团子”；空白、非ASCII、NUL及超过16384字符的输入拒绝。实际getter在CP936和UTF8下字节一致，原“新”的CP936 `D0 C2` 错误已离线复现。

新的 `Dango.AccountSyncClosurePostgres.psm1` 与运行器绑定原User、Pet、默认房、SourceOwner、RecoveryEpoch；单个只读query返回六项目标／历史与八项业务计数。stage、目录、端口、角色、工具参数、一次查询与最多五工具都固定。CreateNew预留先于原state／DPAPI读取；只读原ACL和凭据，不触达keys或正式Host。

共享进程 helper 的 `PreserveRawOutput` 默认为false，原模式保留Trim行为。true只接受closure固定scoped／CompleteEof／精确target、argv与env；两流分别131072字符／UTF8字节上限，完整EOF和显式关闭后返回原文。运行器先将stdout与stderr CreateNew保存，再检查退出码、严格JSON及事实。重复字段、类型、大小写、额外字段和边界均拒绝。ClosurePassed同时要求PG、CMD、所有工具／输出、pidfile及端口完整收尾。

## 最终离线证据

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 新closure严格JSON、输入冻结、argv、预算、raw双流、EOF、CreateNew、SIDACL | 238/238 | `artifacts/account-sync-b1a2-closure-offline/ebf273f743c44fce8d5feb0701d9b669/offline-results.json` |
| Sync运输／图保护／archive／旧批准隔离 | 416/416 | `artifacts/account-sync-b1a2-offline/9ecc8020e7f94fb8b81d7bef2f8a23fa/offline-results.json` |
| 原真实旅程runner函数离线回归 | 40/40 | `artifacts/account-sync-b1a2-runner-offline-20261010/result-d4c97c5725374ddd96b2612cc69a3ada.json` |
| 原共享Profile helper | 237/237 | `artifacts/account-profile-offline/49c9cd18370748aea8854a929fc713d1/offline-results.json` |
| 原22表／配饰保护与D1 diagnostic | 105/105、96/96 | `artifacts/protected-graph-offline/c9db1002a4d34266b1ca7e02aca97abc/offline-results.json`；`artifacts/diagnostic-offline/cc8c25ee98ac47f18e72cadf5d17a2ef/offline-results.json` |
| 实际生成SQL与正式EF模型／ModelSnapshot／历史元数据 | 47 Passed、0 Failed、0 Skipped | `artifacts/scoped-sql-metadata-build/2ead5c8f4e4f42bd80e88cad110339f6/results/scoped-sql-metadata.trx` |
| 新绝对artifact目录Release构建 | 0 warning、0 error | `artifacts/scoped-sql-metadata-build/e139443ae02e41578774b19af338d944/build.log` |

六组PowerShell合计1132/1132。根238项回执SHA256 `56A34BF29441343D3F2B534C2E3A23E7F38504CA4945997F8E1F7139BED247DC`；416项SHA256 `F4EC1463BEBFB48F3548951DAA4B65C02A978AFA133927212290F9F59A940A0C`；47项TRX SHA256 `C173C4208BA4DE6B816D4C28B5523867BCE83C9FB27637628112AFF3630B1D9A`。测试源码SHA256 `B61CF678C16A68FF8D1F2EA49B1A653BE0739260C6CF90C773E76F1D1FBAD3CD`；构建日志SHA256 `E11A59C25D32654498A48E4F9BD1362DDF11A5A57B3A1DCF7EEBB62897421534`。

SQL输入artifact由根调用实际getter生成，旧SQL取自原run实际psql参数，而不是复制期待字符串。文件 `artifacts/account-sync-sql-review/3bf8f61e4598408b917b48d2fae4fea8/sql-review.json`，SHA256 `C4E7D3B270A457BEC2C8B79976AD4B3E1331C02AA22CECBF7313E7EEE0A2599D`。当前两个getter输出与其FixedScopedSql／ClosureSql逐字符一致。artifact记录的是生成时source hash；closure后加raw helper使module hash变化，不能称旧source hash仍匹配。最终审批另冻结当前源码和SQL摘要。

限定tokenizer按每个scalar的真实FROM/JOIN建立alias，`selected`继承users；实际10表、60不同table.column、72作用域引用，八scope在db.Model与ModelSnapshot均匹配。closure严格检查14输出、真实EF生成的quoted `MigrationId`及ORDER BY，以及User／Pet／Room／SourceOwner邮箱／两个RecoveryEpoch字面量。检查仅读取模型与SQL，不连接数据库，不声称已通过PostgreSQL语法执行。

## 能失败的反例与保留失败

同416测试对 `237db11` 的旧Sync／Profile源码快照为406/416，10项新增编码／运输检查失败。红回执 `artifacts/account-sync-b1a2-offline/25927b4f614840199f722ee68f49a765/offline-results.json`，SHA256 `D0CB5779383832A98AD66F388BAE2BEE13FB6CF62EB8FF6B1B785F2C7FE9E7A2`。首次捕获恰遇并行编辑shared半文件而仅parse失败，保留该证据；改为两模块都取已提交旧源码后取得上述真实红结果，没有修改正式实现来放行。

closure同238项对绕过JSON validator的artifact副本为164 Passed／74 Failed，对去掉runner exactargv guard的副本为237 Passed／1 Failed（命令注入反例）。红回执分别为 `d689901b18da485eac707aca00b431c1`／`aa0f7d65d07a45c9b249045879b43a35`，SHA256分别 `F30719CDE4398063D7F307EC2D73DE0D6D354896EC352DC18729EC52CA636C5E`／`D926F4BAEAC903BEBC26854AAF626064545657FCE5884E507B7E0D83E9D99326`。最初relative ModulePath未加载不当作业务反例；改绝对副本路径后取得74项真实失败，全部保留。

C# parser新增两个keyword/count边界时，27项先25 Passed／2 Failed；收紧后通过，最终扩展固定身份等47项全通过。原红TRX为 `artifacts/scoped-sql-metadata-build/881b8c056d86486bae58abe72d2a39f1/results/scoped-sql-metadata.trx`。真实原中文SQL、未知table／column／alias、错scope／JOIN、Unicode错字／截断、缺失只读事务、额外语句、wrong／missing固定身份均拒绝。

## 独立审查与交付边界

独立只读源码审查发现并已解决：审批前冻结三份实际PostgreSQL二进制与精确计划／项目输入、raw stderr保留、成功判定遗漏cleanup、两个错误测试写法、parser作用域与五项身份检查。没有为修复启动数据库、查询业务表、读取原state／DPAPI／keys，或预留正式closure目录。离线CreateNew／ACL反例只使用仓库artifacts下的虚构目录。

已CreateNew冻结 `artifacts/account-sync-b1a2-closure-approved.json`，34份源码／计划／项目输入与3份实际PostgreSQL工具，缺失0。清单SHA256 `06960DA1241C18CA437AA3244230834600FB908D59706AEA7C6F6DD43909F372`；计划SHA256 `5F1EC2B9308EDD269616E1B313E9CE8C2C2E26C518B7C14CBE10455C5DB9390E`；实际生成SQL SHA256 `76FA50C1DE2A897E6639FA8785653FFEB7306230C7EDAD89384D7ED001B76920`。根正式批准校验函数再次全部匹配，真实closure目录未预留。

独立冻结审计23/23：37输入逐文件hash、完整集合、清单8字段、严格plan、SQL、47项TRX及六组1132项回执全部匹配；manifest／plan／SQL前后稳定，真实closure目录不存在。投影 `artifacts/closure-freeze-audit/6e98543565e5407b909fa719c3745f6e/independent-freeze-audit.json`，SHA256 `EFADB3FADBCCDDE29991875DAFADC5ECE9F4387E9235F8AFA3FE45A8853C76FC`。审计未读state／credential／keys，未启动资源、SQL或端口探针，无剩余阻挡。

冻结及审查不等于真实只读query已执行。closure通过后仍需B1b异常／多设备、门廊真实技术验证及主观验收。未合并main、push、部署、安装、替换生产DLL或删除持久数据。
