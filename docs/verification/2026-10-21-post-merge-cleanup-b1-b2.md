# B1／B2：测试职责与默认产品入口整理

日期：2026-10-21。状态：**实现与受影响自动验证完成，人工观感待验**。基于本地 `main@5ae2292`，在 `codex/post-merge-cleanup-b1-b2` 独立实施。用户明确批准B1／B2，并要求完全忽略其他分支兼容；[原方案](../planning/2026-10-21-post-merge-organization-plan.md)及[源快照盘点](../planning/2026-10-21-post-merge-organization-inventory.md)保留。B3／B4未实施，未合main、推送、安装或部署。

## 1. 实施结果

| 提交 | 范围与结果 |
| --- | --- |
| `2abbacf` | B1：`AuthoredPoseSampler`原样独立留在生产；`DeterministicFakePhysicsWorld`和`PlaceholderSceneFactory`迁到测试Support。退役无生产消费者的`SingleDangoMotionRuntime`、`DragGrabPointMapper`、`DangoBodyColorPolicy/Palette`及仅测试这些实现的用例；真实颜色合同保留为`OpaqueSrgbColorTests`，补充非零native错误码透传断言 |
| `01dfdbb` | B2：统一启动配置、设置路径和实例身份；退役6个旧产品review CLI和face／accessory专用设置参数；产品host、脚本与产品断言改走默认Belt链。owned验收与通用`--data-root`采用同一路径作用域身份；安装管理器同步识别、激活及停机门禁。迁移LocalRoomProbe、JointReview家具验证与直接host测试，旧物理断言放显式测试夹具 |
| `672bd2a` | 前台回归发现测试仍查找旧的`A · 换装`辅助名称；核对当前页面与失败日志后，仅将两处UIA定位改为`Q · 换装`，实际EXE重启用例复测通过；没有因此修改产品代码 |

已退役参数：`--unified-live-home-review`、`--porch-scene-review`、`--local-room-review`、`--desktop-pet-room-review`、`--face-review`、`--accessory-review`。旧／未知／冲突参数在设置读取、实例协调器或窗口创建前拒绝。普通产品不再接受旧模块override；显式测试模块覆盖须给出绝对`roomModulePath`。

四个独立预览入口与两个旧宿主诊断继续保留，属于后续B3边界。旧物理测试使用`LegacyProductPreviewFixture`，标记`LegacyModuleIntegration`，不作为默认产品通过证据。没有调整native算法、材料、预算、存档schema、多人协议或服务端。

## 2. 当前操作入口与发布身份

- 普通启动使用默认产品链；产品验收脚本为[run-product-review.ps1](../../eng/run-product-review.ps1)，`face`、`local-room`、`desktop-pet-room`三个薄wrapper复用它。默认构建并准备独立虚构数据；只有显式`-Show`才打开窗口，按既有屏幕规则授权。由真实首页／门廊进入场景。
- 新内部候选包：`artifacts/desktop-entry/b1-b2-cleanup-20261021-v1/package/`。`package-manifest.json`绑定干净源码`01dfdbbde7328e1e53edbbe14d3550b789be09c1`，`sourceDirty=false`，`packageType=internal-candidate`。
- 新设置合同为`dango-desktop-settings-v4-belt-default-v2`，旧合同原样保留。App和host源码按LF归一化绑定；固定跨语言目录身份向量已由C#与PowerShell验证。
- `belt-native-manifest.json`绑定源码生成的两个模块：product SHA256 `D60212F4DDAB01B6104A440F38C19D966184B58B899192F7C8090E79A932ECF7`；room SHA256 `2CEDB643D0250FFBF152DFBAA76C9240A65CAD775546ED59CEBC92997C5C5EE8`。正式默认发布不要求旧模块。

证据主目录为`artifacts/post-merge-cleanup-b1-b2/`；以下路径均相对此目录。候选包之后只有测试定位与文档提交，产品源码未变化，无需为此重建相同包。

## 3. 实际验证与失败记录

| 层次 | 本轮结果 | 证据 |
| --- | --- | --- |
| 构建 | Desktop、Desktop.Tests、JointReview、LocalRoomProbe通过；正式publish通过 | `desktop-build-01.log`、`desktop-tests-build-final.log`、`jointreview-build-02.log`、`localroomprobe-build-01.log`、`publish-01.log` |
| Core迁移 | AssetPackageParser 9/9通过 | `results/b1-core.trx` |
| 后台定向 | 入口拒绝、owned路径与身份、生产fallback／网络呈现、worker生命周期、真实隐藏房间、保存失败保护等133/133通过，零跳过 | `results/focused-01.trx`、`focused-tests-01.log` |
| 最终启动边界 | StartupOptions 7/7通过，含固定目录身份与模块override；其中5项与133项集合重复，不相加为独立总数 | `results/startup-final.trx` |
| 安装／回退fixture | 首轮53项既有用例通过，新增作用域用例因mock误读不存在的`Function:\Start-Process`失败。核实其为cmdlet并修复mock保存／恢复作用域后，仅增量重跑新增用例，1/1通过 | `local-install-01/summary.json`、`local-install-scoped-02/summary.json` |
| 发布门禁 | 10/10通过，覆盖模块缺失、错误哈希、ABI／导出、源码与产物篡改、包完整性及room模块不可遗漏；同包无窗口smoke exit0且进程退出 | `preflight-v1/summary.json`、`smoke.json` |
| 生产程序集 | Desktop／Core元数据中无B1退役类型和测试夹具；生产Sampler仍在Desktop中 | `assembly-purity.json` |
| 前台真实链 | 用户另行授权约5分钟。首轮4通过、1失败；旧UIA选择器修正后，唯一失败项1/1通过。覆盖默认首页及延迟入房、编辑保存／取消／隐藏／重启、普通房／桌宠往返、双表面唯一owner，以及实际EXE配饰应用、第二进程恢复、源文件移动与同作用域重复实例 | `results/foreground-01.trx`、`results/foreground-restart-02.trx`、`foreground-01.log`、`foreground-restart-02.log` |

前台会话始于`09:58:52Z`；最后一项完成于`10:01:23Z`，开始后约190秒内已确认本轮进程全部退出，未超出300秒授权。窗口及输入占用已释放。首次失败的进程事件记录保留在`artifacts/accessory-verification/process/92a0bda9c74f4cb7832bd5ac518800af-restart/`，没有覆盖成通过记录。

B1负控单独保存在`artifacts/b1-negative-control-2026-10-21/`：从`2abbacf`归档到隔离副本，只把fallback的`NativeErrorCode`故意置0。生产契约测试按预期失败（expected 731／actual 0，exit1），证明错误诊断丢失会被捕获。当前生产源码未变异，归档、变异说明、日志与失败TRX均保留。

Desktop.Tests构建使用仅命令级`-p:NoWarn=NU1903`绕过既有传递依赖警告；未改项目依赖或全局规则，不能据此宣称依赖安全通过。正式publish没有该警告例外。初始编译及fixture／UIA失败日志一并保留。

## 4. 界面、保护现场与未验证边界

遵循桌面界面规范v0.1第2节视觉、第3节布局、第4节草稿与真实反馈、第5节滚动、第6节执行清单；本轮没有更换主题、布局或页面差异，沿用已验收控件。前台自动回归只证明实际覆盖行为，**未取得本轮用户人工观感验收**。

main仍为`5ae2292`，仅保留原有未暂存`AGENTS.md`，index为空。本轮分支没有AGENTS差异；main原AGENTS SHA256为`7F2DD53E64CB4E3B32445CBD798D5ABD01AB4B283EB096F5A854674B6802A029`。受保护旧DLL SHA256仍为`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。没有覆盖、暂存、提交或清理用户原修改，没有修改其他分支或删除worktree／包／失败证据。

未新启动服务、数据库或付费资源，无本轮残留Dango进程或监听端口；独立虚构验收数据与候选包按证据保留，未写日常存档、安装或部署。静态收口核对及可机读证据索引另见上述证据目录的`closeout-summary.json`。五份变更文档的313处相对文件链接均存在，Git差异空白检查通过；独立只读审查未发现阻断B1／B2交付的问题。

Schema1间歇保存UNKNOWN、旧480×270／853×480布局失败及SignalR.Common 10.0.11的NU1903继续保留。完整Windows／DPI／多屏、59FPS长测、完整旧物理矩阵及多人验收未执行；B1／B2完成不扩大此前验收结论，也不代表B3／B4或整阶段已验收。
