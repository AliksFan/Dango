# 昵称 D1 只读目标诊断结果

日期：2026-10-10。状态：用户精确批准的 D1 已执行一次并完成，退出码0；一次只读元数据查询、零业务写入，资源收尾通过。一次性额度已消耗，原数据与证据保留。

## 执行与事实

执行前实查 `codex/backend-b5-windows`、HEAD `2817090220bc7e8187c7fc116372f0f0bae5100a`、干净工作树。清单 `artifacts/account-profile-d1-approved.json` SHA256 `472EE72B5EDA17AC49B4B90F5522475E9596B175AF67894E4DEEC304F52F33E8`，根及独立复核36份输入全部匹配、零缺失。D1目录与原pidfile不存在，生产DLL摘要仍为既有基线。随后仅执行批准的 `eng/invoke-account-profile-target-diagnostic.ps1 -ExecuteApprovedPlan` 一次；未重试或补查数据库。

OwnerId `4c9a2a4e-06ce-41f1-ab7e-d01ea34b8f55`，原证据目录为 LocalAppData 下 `Dango/BackendDevelopment/run20261010profiled1`。`target-facts.json` 先于比较保存，当前实测：

| 核验项 | 实测结果 |
| --- | --- |
| 数据库／角色 | dango_backend_b0／dango_b0_owner，均匹配 |
| 版本 | 180006，匹配PostgreSQL18 |
| 数据目录 | 原run20261008b0下pgdata，路径归一化后匹配 |
| 监听地址 | 127.0.0.1，匹配 |
| 迁移历史 | 原精确五项后增加20261009124814_AccessoryAccountBackup，不匹配旧五项候选 |
| 昵称状态 | AccountProfile迁移不存在，nickname／profile_version两列尚未同时存在 |

六个比较只有 `MigrationsMatch=false`，其余五个为true；`ProfileMigrationPresent=false`、`profileColumnsPresent=false`。诊断成功与目标全部匹配是独立结果，前者不授权迁移。D1证明当前候选会因额外配饰历史拒绝；A4未保存当时六字段，不能把D1当前事实补写成A4原始证据。

## 来源与后续边界

本地ref `codex/accessory-account-c2`、提交 `ac8642934f39b5cd738c0b7eeaf7ae208128a8e1` 含该迁移；当前root与main源树未整合该产品代码。原Git blob SHA256为 `D80A458FE5F538A254D06953516B9900F7A4BDE99CD35B1ED6718D6A3B579F45`。其独立计划及验证文档记录2026-10-09曾获精确批准、在同一B0库前向应用并27项真实检查通过；这些历史证据说明来源，不扩大本轮配饰接入或数据操作授权。

迁移增加四表：accessory_blobs、accessory_asset_references、accessory_current_outfits、accessory_commit_receipts，以及它们的约束／索引。旧昵称候选的18表完整图遗漏这四表，不能只修改历史名单然后继续。后续A5候选将采用精确六项历史，并将四表作为不透明完整旧图保护对象；保留既有有意异常测试行，不做语义修复或重新规范化。

当前昵称迁移源仅修改users两列和两个check。正式单迁移入口要求仅该已知迁移pending并指定目标；EF对当前assembly外的已应用迁移不生成Up／Down，pending model比较只涉及当前snapshot／runtime model，不根据数据库额外表自动调整结构。源码审查与静态模型检查不能替代后续真实迁移及22表保全证据。当前未执行昵称迁移、七项业务验证、联合同步／门廊验证或主观验收。

## 收尾与证据

完整启动链 `pg_ctl31880 → CMD8100 → PG34464` 通过归属证明。五工具为postgres版本8124、pg_ctl状态16564、启动31880、唯一psql38396、停止6480；退出码依次0、3、0、0、0。psql stdout483字符、stderr0、完整EOF、读取失败None；所有工具进程退出、Process Dispose、双同步流关闭与独立输出资源清理均确认。只停止本次已证明归属的实例。

根及独立审查追加只读检查PG／CMD／五工具PID均不存在、pidfile不存在、记录端口无监听。原数据、凭据、keys／恢复代际及全部证据保留，没有测试证书、HTTPS、SMTP、WinCred、系统信任或其他服务操作。

原receipt SHA256 `13516412F169D9865E56CFF99F28247F6B46BDF0FE2924FFB9D932835ECC2EBB`，facts `8CD20ECA9BCBDCB04BCC97C230E640C74784C218DCC7DDC17060E2643573E288`，matches `A5614B220B01158821E4F44B44EA708CAAE8A51045BC546D7F42C66FD8D9F77E`，三份原件未改。根非秘密投影 `artifacts/account-profile-d1-result/8209f6613a144f848ecd2f69c96e92d9/root-summary.json` SHA256 `8BFD2F1693F1944625201A91055CDD60F1A9195046EFA989D8D556F3759A26E5`；独立只读复核通过。同目录source-snapshots以CreateNew保留执行时模块、runner、命令和输入记录。D1额度已消耗，后续真实验证需新固定清单、证据目录与精确批准。
