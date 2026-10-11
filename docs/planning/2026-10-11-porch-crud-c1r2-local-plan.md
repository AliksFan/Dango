# 门廊正式房间 CRUD C1r2 本地验证计划

日期：2026-10-11。状态：修正候选与离线验证准备完成，等待新的精确批准；真实 C1r2 尚未执行。见[准备记录](../verification/2026-10-11-porch-crud-c1r2-preparation.md)。原 C1 一次失败已收尾，见[失败记录](../verification/2026-10-11-porch-crud-c1-failed.md)，原数据、清单与证据保留。本计划不授权合 main、推送、部署、更新安装或生产 DLL。

## 修正与验收

完整宿主离线复现确认中文 IME 将 A 包装成无法解析的 ImeProcessed，方向指令未收到，自动闲逛不能算按键证据。只在 gameplay Stage 禁用 IME，账号／房间表单与编辑区明确保留 IME。行走每次要求原生按键已形成正确方向指令、全过程自有 HWND 保持前台、实际位移超过 50、新健康 native 帧及同一运行时／团子。终点使用生产场景的 NearbyDoorId 和实际互动范围；不再要求到任意指定的中心 ±35。最终 W 必须真实打开目标控件，不能改投影、改坐标或直接呼叫 controller。

测试工具在宿主释放前捕获原房间控制器及其操作对象，释放后观察同一对象。离线反例必须能在原操作尚未释放时拒绝排空通过。保留旧失败，独立离线实际完整宿主覆盖建房牌、旧门册、已确认目录房门、IME 文本上下文及生命周期。

正式旅程仍为实际 DangoApplicationHost＋B772 native＋一个真实团子：两个虚构邀请邮箱注册；B 登录并设置“门廊访客”；A 实际客户端登录并设置“门廊主人”，相同捏脸不弹选择；读门廊目录；空名称／容量 13 均零写入；创建私密共同房 6 席 v1；B 读／改 A 房均 404；A 改为“门廊茶室”8 席 v2；解散确认和取消均零写入；B 解散 404；A 明确解散 v3；B 恢复 404；实际行走到旧门册，读已解散目录并恢复 v4；确认已解散目录为空、当前目录两房；走至恢复后的真实房门再读 v4；两会话正常注销。

四次成功各须正式 HTTPS 回执、随后独立 GET 与 UI 读回。最终独立 SQL 核对完整注册图、未发送邮件、默认房／占位不变、两会话归属与撤销、共同房 v4／admission_generation=2、唯一 owner membership、零共同房占位、四条 audit／outbox 按请求号、版本、类型、同 id 和目标用户匹配，B 零回执。原 22 表摘要多重集合全部保全。

## 精确资源目标

仅已有 LocalApplicationData 下 `Dango/BackendDevelopment/run20261008b0/pgdata` 隔离 PostgreSQL 18；127.0.0.1:63237、库 `dango_backend_b0`、角色 `dango_b0_owner`；逐次核实目标、监听与原完整七迁移，零迁移，不连接其他 PostgreSQL。沿用原 DPAPI 凭据、`b1tests-keys` 和恢复代际；不主动重置或轮换，正常服务可能生成的新密钥也保留。

新证据目录为同级 `run20261011porchcrudc1r2`，CreateNew 一次性预留与私有 ACL，存在即拒绝重跑。独立 Stage `PorchCrudC1r2`、grant `DANGO_BACKEND_PORCHCRUD_C1R2_APPROVED`，旧 C1／B1c2 grant 和旧清单无效。批准检查先于预留与原状态读取。候选目录 `artifacts/porch-crud-c1-r2-candidate/bin/Dango.AccountSyncReview.Tests/release`，单测试名仍为 `Dango.AccountSyncReview.Tests.PorchCrudC1JourneyTests.NativePorchCrudPersistsAndForeignAccountCannotReadOrMutate`，全部源码／候选／脚本／native／PostgreSQL 工具哈希冻结后申请批准。

一个本机临时 HTTPS 端口、一个服务宿主、一张 1 小时证书、一个实际桌面宿主。证书精确 pinning，不导入系统信任；秘密仅测试进程内存，不操作 WinCred。关闭邮件、过期、在线租约及 outbox 后台 worker，不发送真实邮件、不创建多人租约或占位；截图只本次自有 WPF 窗口。

## 不可退还预算与固定序列

| 操作 | 次数上限 |
|---|---:|
| Invite／Start／Confirm／Complete | 各 2 |
| Login／Profile／Logout | 各 2 |
| Create | 1 |
| Update／Dissolve／Restore | 各 2（各含 B 拒绝一次） |
| 合计 | 21 |

最多两个新增 `.invalid` 账号注册图、两个会话、一个共同房；独立于原失败 C1 的已用额度，保留旧数据。HTTP≤128，包括所有读取和失败请求；实际 Send 起始间隔≥600ms，单调时钟验证；业务≤10分钟、测试进程≤300秒。预算先消耗再持久登记、失败不退款；无隐式重试，空表单／取消均零业务写入。

| 表组 | 允许新增 |
|---|---:|
| invitations／registrations／challenges／emailJobs／users／pets／petStates／sessions | 各 2 |
| rooms／memberships | 各 3 |
| audit／outbox | 各 4 |
| 其余十表（含配饰四表） | 0 |

固定十工具：version、status、start、before-size、before-full、vstest、after-size、after-full、scoped、stop。快照先测量，最大 1 MiB，不扩大；固定只读 SQL，scope 由有界规范身份重新生成全文并比对，禁止额外诊断查询。start 沿用丢弃输出合同，其余完整 EOF。失败只收尾，不补跑、不补查。

## 五项检查与收尾

- 正确性：实际控件／按键／互动范围、异常输入、版本、回执和写后读回；自动闲逛不能算方向指令成功。
- 一致性：逐版本事务／成员／审计／outbox 与旧摘要保全；C2 并发、丢响应、原请求重试及宿主中断另行覆盖。
- 安全性：B 独立会话四种越权请求拒绝，固定目标／路由／SQL／预算／私有证据；游戏 IME 与文本上下文分开验证。
- 可靠性：独立原操作排空、计时器／native／HWND／HTTPS／PostgreSQL／桥进程／临时 CNG 私钥收尾；内部不可观察 DisposeTask 仍明确不可观察。本轮无备份还原或服务中断演练。
- 扩展性：每页 4、请求／时间／输出／对象有界；公共码、成员、准入／离房及完整多人画面不在本轮。

采用桌面规范 v0.1（2026-10-10）第 3、4.2、6 节，功能直接展开在门廊；不改主题／图标／滚动规范。技术测试由助手负责；主观手感、效果与流畅度尚未验收。

无论通过或失败，结束自有输入／桌面宿主，排空可观察原任务，停止释放 HTTPS 并确认临时 CNG 私钥消失；仅按精确进程／数据目录／端口／启动区间／桥进程归属证明停止本次 PostgreSQL。资源合同未确认不得报告通过。保留全部原数据、本次新增数据、新旧服务密钥、epoch、凭据和证据；不删除、不重置、不还原。
