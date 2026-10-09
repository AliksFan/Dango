# 当前穿搭账号备份：首批并行增量验证

> 本页保留首批 176 项及当时未接线状态。后续 C2 正式私有账号／Windows 候选及最新未验证边界见[C2 记录](2026-10-09-accessory-c2-private-backup.md)。

日期：2026-10-09。状态：C0/C1/C3 的独立模块和 C4 本地管理员原型已实现；正式服务端接线与人工验收未完成。

## 范围与文件

基于本地配饰第一版 `f10121b`，在 `codex/accessory-account-sync` 开发。业务规则见[设计与计划](../planning/2026-10-09-accessory-account-backup.md)。

- `src/Dango.Protocol/Accessories`：Schema 1 当前穿搭、全部材料／层序参数、图片映射、修订号与幂等请求；严格完整 JSON 合同。
- `src/Dango.AccessoryAssets`：PNG 结构、CRC、展开预算、透明性验证，规范化版本 1 和真实文件内容库；同图并发去重，源哈希独立验证。
- `src/Dango.Client.Sync/Accessories`：账号／团子／登录代次及固定服务来源绑定，串行备份、最新快照合并、明确失败与未知结果核对、完整恢复接口。
- `src/Dango.Desktop/Accessories/AccountSync` 和最小 Store/Session seam：真实已保存事件触发队列，仅读取当前穿搭及必要实盘 PNG；完整暂存验证后守卫提交，保留本地库和草稿。
- `src/Dango.AccessoryModeration` 与 `eng/tools/Dango.AccessoryReview`：实际图片显式申请、持久待审记录、预览、通过／拒绝／撤销、并发修订保护；只按图片资格和独立展示授权投影，穿搭调整不重审。
- `eng/Verify-AccessoryAccountIncrement.ps1`：本批可重复执行的 Windows 构建与门禁；给既有真实网络测试指定本次构建的匿名服务器，并在退出时恢复进程环境变量。

没有接入正式账号 HTTP、EF／PostgreSQL、B5 页面、房间投影和通知，没有实现组合举报服务或生产垃圾回收。管理员工具是可信本地操作者原型，不是互联网管理员鉴权接口。

## 自动验证

统一执行：`pwsh -File eng/Verify-AccessoryAccountIncrement.ps1`。

| 门禁 | 实际结果 |
|---|---|
| 完整 solution Release 构建 | 0 warning / 0 error |
| Protocol | 16/16 |
| AccessoryAssets | 32/32 |
| Client.Sync | 55/55，其中新配饰 25 项；包含原有匿名同步的真实 WebSocket 回归 |
| AccessoryModeration | 22/22 |
| Windows 本机适配与相关 Storage/Deletion/WPF/模块串联 | 51/51 |
| 总计 | 176/176，无跳过；不代表全部 Desktop／物理／性能测试已运行 |

统一日志与 TRX 留在 ignored `artifacts/accessory-account-sync/`；本批改动的 `dotnet format whitespace --verify-no-changes` 和 `git diff --check` 通过。格式修正后再次运行完整本批门禁，不把此前结果冒充最终源树验证。

实盘串联覆盖：WPF 真实应用并通知 → 只取已佩戴组合 → 真文件 blob 上传、规范化 ID 映射与去重 → 真审核记录申请／批准 → 穿搭变化复用资格 → 撤销后公共投影排除 → 第二个本机仓库仍可私有恢复 → 新 Session 重载所有参数与图片，并保留未佩戴库。运输收据使用测试夹具，两个仓库在同一 Windows 机器，不是正式账号或双机验收。

管理员 CLI 用两个真实 12 × 12 canonical PNG 验证：未申请预览失败、显式待审列表、验证图片路径、通过、旧 revision 冲突、拒绝、撤销、重复申请保留撤销状态、新 CLI 进程重读，以及私有 PNG 保留。根工作树证据在 ignored `artifacts/accessory-moderation/cli-root-smoke.log`。`--open` OS 图片查看器入口未执行；自动化决策理由属于虚构夹具，未进行人工内容审核。

## 原始失败与审查修复

- C0 校验未实现时，5/8 专项红测；C1 25 项初始红测及“源哈希当 canonical ID”突变 3/3 红测；C3 16/17 初始红测；Windows commit seam 4 fail / 1 pass；C4 14 项初始红测和“批准绕过资源授权”突变失败。原始记录保留在相应工作树 ignored artifacts。
- 修复恢复暂存 partial 写入未登记导致清理漏掉文件；写入失败不替换当前清单，清理失败不遮蔽原始故障。
- 修复旧账号核对迟到返回成功、旧已知上传失败冻结新队列、云端修订号低于已验证版本；均加了能失败的反例。未知提交和修订冲突仍须明确核对／选择，未降级为盲重试或覆盖。
- C4 泛型可变投影改为不可变 Protocol 配方；公开名称使用通用标签，原私有名称保留；管理员预览要求已有显式申请。
- 初次全 Client.Sync 测试因缺少 `DANGO_SYNC_SERVER_PATH` 有 3 项失败；指定本次构建后真实网络回归通过，原 TRX 保留。
- 首次默认并行 solution 构建出现 MSB4166 子节点提前退出；未取得其临时诊断文件，不推断原因。验证脚本改用串行、不复用构建节点后，完整构建通过，不跳过任何项目。

两位独立实现代理交叉只读审查了 C3 与 C4；上述实质候选已修复。审查只覆盖本批模块，不表示项目全阶段通过。

## 未验证与下一步

1. C2：对接冻结且验证过的后端提交，由唯一 EF 模型负责人实现账号独立引用、上传租约、事务性穿搭保存、expected revision／幂等收据和鉴权下载。去重或审批不能授予私有资源访问。
2. B5：接登录会话、应用后的状态提示和本机／账号选择入口。当前产品窗口尚未启用真实自动云备份；不得用已有本机行为冒充账号保存。
3. C4/C5：正式管理员角色、申请权限、房间服务端投影、撤销通知／缓存资格、组合举报复核和引用回收。当前撤销只保证后续模块投影重新读取资格，未广播给真实玩家，也不保证抹除已合法下载字节。
4. 真实双 Windows 设备登录、网络／数据库持久恢复、多玩家展示、人工内容审核、OS 图片查看器以及最大八图的内存／响应表现，均未验证。

本机恢复的 expected-load 检查保护本应用提交及观察到的磁盘变化，不承诺对任意外部编辑器提供跨进程 CAS。文件库假设目录由可信操作者控制；进程崩溃可能留下 own temp 或未引用图片，生产清理未实施。新 codec 版本需要显式 identity/version 迁移；Linux 未实测。

## 交付与资源

本批仅在 feature 分支创建本地提交，未合入 main、推送、部署、更新日常安装或替换受保护 DLL。DLL SHA-256 保持 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。

测试使用临时虚构图片／身份和匿名内存服务器。临时 storage 已清理，本次服务器 fixture 无残留进程；未操作现有数据库、云资源或正式图片库。保留本批 managed 工作树、分支及 ignored 验证产物供后续整合。
