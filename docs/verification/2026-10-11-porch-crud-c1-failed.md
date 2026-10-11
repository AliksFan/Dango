# 门廊 CRUD C1 一次真实执行失败记录

日期：2026-10-11。状态：用户精确批准后仅执行一次；失败，不补跑、不补查数据库。冻结提交 `8ca2400e8364f04af758f8f03d6b897e83e762b3`，批准清单 `artifacts/porch-crud-c1-approved.json`，SHA256 `260297216F567E920C51D9054031D3697205A5433D4FEE89F0502CD62FD17537`。批准范围见[原计划](../planning/2026-10-11-porch-crud-c1-local-plan.md)。

## 实际结果

正式入口 1 Failed，测试耗时 56.78 秒；外层返回 `porch_c1_run_tests_failed_no_retry`。实际完整 Desktop、正式 HTTPS、PostgreSQL、独立 HWND／PID UIA、B772 native 已启动；两个虚构账号完成注册、登录和昵称请求，但未形成完整终态数据库验收。

12 次业务登记：Invite／Start／Confirm／Complete／Login／Profile 各 2；40 次 HTTP 登记与实际 Send，终态均 RanToCompletion。Create／Update／Dissolve／Restore／Logout 均无尝试。第一次行走在 `Walk` 的按键后位置等待处超时，未到建房牌，未发起共同房写入。Stage 焦点前置检查已通过，不能将此次失败称为焦点获取失败。

执行前快照 `before.json` 为 851243 字节，完整 EOF、在 1 MiB 上限内。外层实际七工具：version、status、start、before-size、before-full、vstest、stop。测试失败后没有运行 after-size／after-full／scoped，符合失败只收尾合同；因此旧 22 表摘要保全、最终注册图／房间归属／审计／outbox 和会话撤销均未验证。两次正常注销未执行，不宣称两会话已撤销。

## 收尾证据

证据保存在本机 LocalApplicationData 下 `Dango/BackendDevelopment/run20261011porchcrudc1`：原 TRX、执行前快照、attempts／resources ledgers、外层 `resource-receipt.json`、唯一 `porch-*` 子目录的 `desktop-evidence.json` 全部保留。

| 资源或合同 | 保存的证据 |
|---|---|
| PostgreSQL、启动桥与工具进程 | CleanupConfirmed=true；postmaster.pid 不存在，63237 无监听 |
| HTTPS | Stopped／Disposed／LifecycleTasksSettled／ListenerAbsent 均 true；InFlightRequests=0 |
| 临时 CNG 私钥 | TLSDisposed.NativeCleanupConfirmed=true，NativeProbeFailed=false |
| Desktop HWND、runtime、计时器、UIA、HTTP、同步协调器及内存秘密 | 对应停止／排空／释放字段均 true |
| 房间原操作排空检查 | RoomOperationsDrained=false，RoomOperationsDrain 抛 NullReferenceException |
| 数据、服务密钥与 epoch | DataRetained／KeysAndEpochRetained=true；不删除、不还原 |

房间排空失败来源已经通过源码核实：`DangoApplicationHost.Dispose` 将 `roomController` 置 null，测试工具在宿主释放后通过 `Rooms` getter 再反射读取该字段；此空引用不能作为原任务排空证据。需在释放前捕获原控制器／操作对象，释放后观察同一对象。

外层 `FixtureNativeCleanupConfirmed=false` 是测试失败后尚未完成聚合资源合同验证的状态；与原 ledger 中 TLSDisposed.NativeCleanupConfirmed=true 分开报告。实际资源退出不等于全部收尾合同通过，C1 不得标为通过。

## 后续边界

只在隔离 artifact 中用内存 HTTP handler 的实际完整 Desktop/native 宿主复现行走问题，并修复测试工具生命周期；不读取原秘密、不启动 PostgreSQL、不增加真实业务查询或写入。原失败证据与批准清单不改写，C1 授权已消耗。任何新的正式旅程均需新的候选、一次性证据目录、冻结清单及精确批准。

主观验收、C2 并发／丢响应／中断、完整 B5／多人／运维与生产部署仍未完成。未合 main、推送、部署、更新日常安装或替换生产 DLL。
