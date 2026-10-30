# 配饰统一编辑器本地 main 集成

日期：2026-10-29。用户先确认当前版本“通过，目前没有想改的地方”，在获知合并前收尾范围后明确要求“开始进行收尾合并吧”。本轮授权为核对交付、提交、集成检查及合入本地main；不包含推送、标签、上传发布、安装或删除旧数据。

## 基线与范围

- 源分支 `codex/accessory-editor-rig`，收尾前HEAD `c91e5f4`；原工作区的64个已修改文件和140个新增文件为本轮累积现场，保留并逐项核对，没有重置或清理。
- 集成基线为本地main `5ce45af`，共祖 `d929a8e`；提交历史双方分叉，9个同路径文件需核对。main工作区干净，使用独立 `codex/accessory-editor-closeout` 工作区准备集成，不在主工作区解决冲突。
- 交付范围及人工验收见[验收记录](2026-10-29-accessory-editor-acceptance.md)。阶段旧描述在顶部加当前状态，历史证据保留。不同DPI、高对比及长时内存不写成已经通过。

## 收尾核对

- pending文本的个人绝对路径与常见凭据模式扫描无命中。新增原生头文件、Web输入源／散列清单、原创样例工程和规划参考图片纳入交付核对，运行数据、WebView缓存和本机产物不纳入。
- 代码审查未形成确定阻断项。关于重复PhysicalTick的初步假设经注释、主链和既有睡眠测试核对撤回：native身体可休眠，独立host呈现时钟仍允许配饰自然回落。没有为该假设修改生产行为；临时反例已精确恢复，原Presenter测试8/8通过。
- 界面规范沿用桌面v0.1（2026-10-10）第2–5节及已确认页面差异；产品装配和旧入口退役按开发工作流第13节核对。

## 集成结果

本地main已从 `5ce45af64589e4b9a55e6654df9929007eff7693` 快进至代码交付提交 `ae4d4209f4d0b9cc6b233b90b332d17a83f6e9cf`。本记录和验收状态随后作为文档提交补入main。未推送远端、安装、打标签或上传发布；本地包为内部候选产物。

| 提交 | 内容 |
|---|---|
| `13c09c3` | 固定源分支累积改动，统一编辑器、双向骨架、图层操作和模拟模式 |
| `69492fc` | 与main普通合并，保留三栏位、输入屏障与暂停合同 |
| `7e9f29d` | Web资源换行归一化，保证干净检出可重建；浏览器拖动测试只结束自身夹具进程 |
| `337e4d2` | 三栏位接入完整配饰工程、旧格式备份升级及宿主交接测试适配 |
| `ae4d420` | 发布候选声明roster-v2存档合同，保留旧合同并阻止跨代自动降级 |

实际产生4个文本冲突：开发计划保留双方历史；`ProductHomeView.BeltBody.cs` 同时保留三栏位资格和编辑窗口／动态预览暂停条件；`ProductHomeView.xaml.cs` 先处理输入屏障再取消挂点选择；`DesktopPetAccessoryIntegrationTests.cs` 保留main现行边界帮助方法与唯一正式room装配。没有强制覆盖任一历史。

## 存档兼容与验证

用户单独批准“保留原件备份，支持可回滚升级，继续使用已有栏位”。实现和回滚顺序见[ADR 0010](../adr/0010-dango-roster-editable-accessory-upgrade.md)：严格有效schema1首次读取时，先逐字节核验不可覆盖的 `dangos/backups/roster-v1-<原字节SHA256小写>.json`，再原子提交schema2；保留栏位身份、外观、穿戴快照、原PNG和初始化记录。当前新版重新读取已恢复schema1时仍会升级，回滚须离线恢复备份后使用旧版启动。本轮没有读取或迁移日常存档。

| 验证 | 实际结果与证据 |
|---|---|
| 存储、升级、事务与三栏位 | 136通过、0失败、0跳过；工具控制台会话 `62622`，最终输出 `c254d5`，无落盘TRX。覆盖备份失败、坏图、未知字段、幂等、跨profile引用及回滚字节一致性 |
| main宿主与WebView集成 | `artifacts/accessory-closeout/tests/accessory-integrated-hosts.trx` 为修复前18项中13通过、5失败；通过项包括Presenter 8项、后台暂停4项及真实离屏WebView 1项。5个失败均为旧Handoff夹具与main三栏位不匹配 |
| Handoff修复后复测 | 真实虚构profile、双组双PNG工程、外层Apply和正式room交接5/5通过；控制台会话 `25098`，最终输出 `221996`，无落盘TRX。保留上述历史失败TRX，不将其写为18/18通过 |
| 独立Desktop二进制轨迹 | reference与candidate各1/1通过，不同Desktop DLL与进程、相同真实native DLL；930帧静止、左右移动、跳跃、压缩及恢复的全部身体和配饰digest一致。基线 `artifacts/accessory-closeout/equivalence-baseline.json`，候选 `artifacts/accessory-closeout/tests/equivalence-integrated.trx`；reference TRX位于源工作区相同相对目录 |
| Web功能 | 15个测试文件共126个功能用例：125通过、1可选真实内置PNG用例因未提供环境变量跳过。初轮layer-drag所有功能通过但Windows浏览器关闭超过10秒；改为结束该夹具自己的browser server后，layer-drag 13/13及独立transport测试通过，结束正常 |
| Web输入与资源 | build/verify通过：6项SHA、精确资源清单、依赖允许列表、许可证及本地CSP；index.html复制前归一换行，避免Git换行转换使清单失配 |
| 发布兼容合同 | Windows PowerShell 5.1 fixture 6/6通过。17份实际持久化源码逐文件篡改和缺迁移源均被拒绝；6个旧合同哈希冻结；目录实例身份保持；跨代降级拒绝且不写安装状态。root复测证据 `artifacts/settings-contract-tests/b66375a452cb4aba95f64bbcf11230d1/summary.json` |

136项和5项复测使用 `dotnet test tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj --no-restore -p:NuGetAudit=false --verbosity minimal`，前者filter为DangoRosterUpgrade／Store／Host、AccessoryProjectStorage／Storage／SkeletonProjectContract、DangoSlotHome、AccessoryWearTransaction／UnifiedEditorSession／DraftContract各测试类，后者为 `FullyQualifiedName~AccessoryDesktopHandoffTests`。仅在命令级处理既有NuGet审计门禁，未升级依赖或修改项目策略。

## Release候选包

正式 `eng/publish-desktop-entry.ps1 -RunName accessory-editor-main-ae4d420` 成功，命令级使用既有NU1903例外。产物为 `artifacts/desktop-entry/accessory-editor-main-ae4d420/package`：

- `sourceCommit=ae4d4209f4d0b9cc6b233b90b332d17a83f6e9cf`，`sourceDirty=false`，win-x64自包含，settings合同 `dango-desktop-settings-v4-roster-v2`。
- `Test-DangoPackage` 完整核对499个文件及精确清单，manifest SHA256为 `5E40D03FAB7100AD29A13B67F115008A46EDEEB680EE914C9157D226285F3FF5`；包内 `AccessoryEditor` 的6项资源另行核对SHA与大小。
- 两个正式原生模块均为当前源码构建，ABI／模型／tuning／能力及许可证验证通过；受保护 `dango_box2d.dll` 保持已批准SHA。没有替换旧受保护输入。
- 包内 `Dango.Desktop.exe --smoke-test` 无窗口启动退出码0；证据为 `artifacts/accessory-closeout/package-verification.json`。此项只证明启动检查，不能代替编辑交互的人工验收。

## 进程、数据与未验证项

核验旧owner记录、exe、命令行和创建时间后关闭本轮旧PID 35424。仅复制其带虚构数据标记的21份设置／工程／素材文件，并逐文件核对SHA；原目录、旧数据及工程资源保留，未复制WebView缓存或房间运行数据。

从Release候选包以显式 `--data-root` 启动新PID 46048，标题为“Dango 控制中心”、窗口句柄非零且Responding=true，没有自动进入配饰编辑器。新数据在集成工作区 `artifacts/accessory-closeout/foreground-main-20261029-132310-ad6e2f5d7f314d1a8732497f6813518d`，运行owner见 `artifacts/accessory-closeout/current-trial.json`；该窗口按用户约定保留供使用，未启动本轮HTTP服务器。其他用户进程未操作。

本版编辑交互与观感已由用户验收；本轮集成差异完成上述静态、存储、离屏集成、二进制轨迹和包检查。多DPI、高对比、长时内存及完整平台矩阵仍未全面验证；不将离屏检查或启动成功替代这些项目。旧分支和Worktree均保留，未执行清理或日常数据操作。
