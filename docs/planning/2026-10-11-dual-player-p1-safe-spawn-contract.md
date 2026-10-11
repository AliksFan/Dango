# 双人P1：安全出生隔离验证冻结合同

日期：2026-10-11。用户已批准安全出生规则及P0–P1隔离准入，并再次指示“吸收main再继续原目标”。本轮实施范围按[首轮计划](2026-10-11-dual-player-p1-safe-spawn-plan.md)S0–S4，合同在恢复编码前冻结。源码基线 `844e944` 已包含本地main `a4acdeb`。不启动P2、正式账号／Hub／数据库／Linux资源或UI，不合回main、不推送／安装／部署，不替换受保护DLL。

## 1. 来源与所有权

原Box2D、参数、authority ABI及P0坐标／时间合同保持，R1候选DLL使用计划§8冻结的SHA256。恢复不需要新native导出，complete checkpoint保持unsupported；原冷恢复／重放预算失败不改写。

根负责合同、组合进程验证脚本、集成与报告。复杂实现子Agent遵循main规则使用 `gpt-6.1-sol`、`high`，在独立工作树分别拥有：

- 纯语义owner：`eng/tools/Dango.SafeSpawnAdmission.Core/`、新的语义测试文件及必需测试项目引用；无native、网络、DB和UI依赖。
- native owner：`eng/tools/Dango.SafeSpawnAdmission/`与必要独立native probe；引用Core及现有Physics，不修改生产ABI／solver／参数。只验证隔离世界。

两边先确定DTO接口，再分别提交；集成顺序Core→native→根脚本。任何跨owner接口变化先通知根，不同时修改同一文件。

## 2. 有限合同

- 无家具最多两体。元数据最大65536 bytes，成员身份使用虚构Guid，PresenceGeneration、RuntimeRevision、RecordRevision为正的有界无符号整数，成功Tick允许0。配置ID／版本／参数hash必须匹配冻结模板。
- 元数据保存RoomId、版本、配置、成员生命周期、来源epoch／成功Tick和记录顺序；不保存位置、速度、节点、支撑或输入。规范JSON编码、完整性hash；严格拒未知字段／版本、重复属性／身份、空值、截断、超限与溢出。hash只用于完整性，不替代授权。
- 最新领域事实独立于record输入，含成员、有效控制／观察租约、事实revision、owner及运行处置。可恢复处置仅限已确认world丢失或正常释放；已知fault或unknown阻止自动出生，包括进程退出后重新验证。此处只证明可信fixture语义，不证明P3持久故障事实可靠性。
- 发布和真正Dispose的有序转换点均重新核对owner、事实revision与最新资格，拒绝检查后成员／租约变化。fixture claim捕获最后合法记录；CAS拒旧owner／epoch、同epoch倒序RecordRevision／Tick，失败保留最后合法记录。新epoch的Tick不与旧epoch直接比较。
- 正常重连、接管、隐藏与world留存暂停不Create／Reset／Translate，不更换epoch或身体；按相同有效输入与对照继续。350ms清意图不清惯性，30秒容错按领域事实处理，有效观察租约阻止释放。边界在到期时失效（`now >= expiresAt`），休眠还须最新事实明确满足原30秒容错；不能从无订阅推断。

## 3. 出生与帧时点

两个anchor `(-1.4,0)`／`(1.4,0)`，canonical Guid字符串序稳定分槽；ground `-.72`，左右界 `±13.2`，原DropHeight `.42`。出生包络以native float参数求径向最大长度加NodeRadius，转double向外取整一float ULP：当前约`.8746 p`。包络须完全在批准区内且组间有严格正间隙；恰好相切／越界拒绝。不是后续实际轮廓门的替代。

Create→EnableAuthority→SetHorizontalBounds→整组Add→Tick0 frame→neutral固定步→整组健康验证，全部同owner。Tick0 core速度0、无旧支撑／冷却；原初始化不继承节点速度。首步允许原重力产生速度，不强清velocity。新旧native EntityId可数值相同，旧输入／帧／ready仍按epoch／成员／binding fence拒绝。

全组成功前零发布；第二体构建／首步／发布前资格失败释放整个私有候选。健康world成员变化先在当前已取得raw帧做新成员空间预检；预检拒绝保持原world。实际native Add失败可能已改world并锁存，只保留最后安全发布帧、不承诺回滚、不Reset；Remove旧代际后不复活撤权身体。

## 4. 失败型验收与预算

计划§6矩阵逐项执行。至少覆盖健康push／stack／airborne各120步续接、两种真正world丢失、观察租约／30秒／350ms与释放竞态、record错误、成员／epoch／ready、CAS失败、整组出生、fault及资源。新出生一／两体各600步完整native轨迹，原侵入`.031`、支撑正gap`.008 p`及原身体安全门不变。

各错误变体由独立断言／外部verifier识别并实际exit1：误Reset健康world、watchdog／隐藏误释放、忽略observer、提前释放、旧epoch、仅PetId匹配、坏record当首次、首体即ready、跳过出生组间／边界门、旧writer覆盖、fault自动新建、缺恢复原因、继承旧Move／Jump。对不涉及native的纯业务反例明确标注fixture，不将字符串或角色当真实权限证据。

恢复预算从record校验开始至首个健康共同帧，包括规划／Create／整组Add／Tick0读取／首步及整组校验，实测≤5秒；进程启动、构建、后续600步另列。record≤64KiB且不随运行历史增长。正常项零跳过；错误变体预期失败不计正常实现失败。任一正常门失败保留原始日志并停止扩展，不放宽门槛／预算／参数。

使用新ignored证据目录，保留所有正常／失败日志、source／DLL／参数hash、实际PID／退出码与创建释放计数。线程／取消／异常检查native allocation回到起点。只跑本轮与受影响原能力门，不因文档main合入重复全套R1。

## 5. 准入边界

S0–S4结果是隔离Windows合同证据。Linux、真实窗口／多客户端、正式事务／持久故障与lease事实、真实提示和人工双机分别未验证，仍不自动启动P2。后续需要这些环境或人工操作时给出具体可执行验收项，再请求协助。
