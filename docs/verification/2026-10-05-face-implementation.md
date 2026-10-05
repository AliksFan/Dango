# Windows 团子捏脸实现验证

日期：2026-10-05。范围为用户批准的 F0–F5：身体／眼睛 RGB 与四滑块、整套预览／取消／默认／显式保存／失败重试／重启恢复。帽子、身体物理、服务端和多人协议没有进入外观模型。当前候选等待产品人工验收，不能标为整个 Windows 产品已验收。

## 基线与交付

专属分支 `codex/appearance-face-design`。实施前工作区干净；旧 RGB 模块由 `b01909f` 提供，设计 `b75ecc0`、计划 `2090d1c`。普通房干净原子交付 `88c0231` 已由唯一集成负责人以 `4eb06a7` 合入本候选，只提供共享 Host／Runtime 接线输入。未合入 main；main 保持 `e2f4037`。无推送、日常安装或部署。

合同与存储保持默认两色 `#FFE8C2` / `#2B201E` 和四项 `100 / 0 / 51 / 24.5`。默认请求直接执行旧完整 solver；高度和眼距为相对旧锚点的校准控制，不表示当前身体实际百分比。Schema 3 严格七字段；Schema 1/2 只读补默认眼睛，只有应用成功才升级。未来版本／坏文件保留并保护，显式备份成功后允许同一次草稿保存失败重试。

首页新增颜色／眼睛页签、系统 RGB 调色盘、四 WPF 滑块、应用／取消／默认和保存状态。三项草稿操作与保存反馈固定在编辑区底部，不随选项滚动。黑色、同色合法；眨眼和睡眠／醒来用于临时比较，不持久化。关闭、换装、设置、隐藏、卸载、离开首页和退出均取消未应用整套草稿并释放比较通道。调色盘旧回调有 generation 门禁。

Host 从实际 settings 所在目录读取 appearance.json，在首个有效帧前设入同一 Runtime。滑块／颜色／默认／取消仅更新不可变请求，不 Tick、不 Step、不新建实体或帧 owner。身体／眼睛画笔经统一绑定覆盖本地主要实体；首页、门廊、桌宠 Window／Wallpaper、Wallpaper 上图标层叠窗口及本地房共同消费。

## 静态与集成门禁

| 门禁 | 实际结果 | 证据 |
|---|---|---|
| F0 合同、默认／非有限／越界拒绝 | 4/4，无跳过 | artifacts/face-implementation/results/face-contracts.trx |
| F1 Store／Session，包括真实 Windows 文件锁替换失败 | 113/113，无跳过 | artifacts/face-implementation/f1/results/f1-restored.trx |
| F1 四项错误实现 | 接受版本4／遗漏高度／遗漏眼距／提前更新Saved分别产生2/12/12/4失败；字节恢复后113/113 | artifacts/face-implementation/f1/mutation.json |
| F2 新布局、渲染、旧冻结／安全／动态／像素／Gait | 32/32，无跳过；旧实现反证8/8失败 | artifacts/face-implementation/f2/ 下的 TRX |
| F2 新真实 Native | 2/2，无跳过 | artifacts/face-implementation/f2/f2-native-corrected.trx |
| 原 Native／附件 Scene 回归 | 9项全通过 | artifacts/face-implementation/f2/f2-native.trx（包含首轮新测试的一项失败，见下） |
| F5 隔离参数与路径保护 | 24/24，无跳过 | artifacts/face-implementation/results/face-entry.trx |
| F3/F4 首页／布局／Host／本地房产品门禁 | 54/54，无跳过；包含真实 WorkerW 外观往返 | artifacts/face-implementation/results/face-product-final.trx |
| 最终受影响回归（含上述54项及Runtime／旧眼睛像素／Gait） | 78/78，无跳过 | artifacts/face-implementation/results/face-final-regression.trx |
| 隔离产品 Release 构建 | 0警告、0错误 | artifacts/face-review/acceptance-20261005-v1/ |

F2 中性体最大圆眼高度35×眼距45共1,575个组合双眼可见且未闭合；另有1,225组连续输入采样，不能称为完整连续域数学证明。受压、倾斜、对向注视和闭眼最终双眼检查轮廓及至少2 DIP净空，尺寸退让不反写控制参数。900步真实 Native 轨迹与参考物理完全相等，独立帽子输出完全相等。140%眼睛实际几何与像素门禁通过。

F2 首轮新 Native 帽子完全相等测试失败，是参考控制器遗漏与编辑控制器相同的 blink 输入，导致原有积分分片不同；给双方相同动态输入后通过，未修改生产算法或降低断言。F3首轮修复了未绑定 Session 的实时首页默认画笔接线；睡眠断言使用既有眼睑弹簧的≤.03闭合阈值，保留真实过渡。

独立审查修复并覆盖：页签身份不能依赖会被选中样式修改的 Tag；调色盘返回不能复活已取消草稿；不可用 fallback 帧缺 EntityIds 时统一绑定不能越界。

旧普通房 Wallpaper 专项2项在本轮因实际房间窗口未成为系统前台而失败，保留 face-product.trx 原始证据，未修改原测试或将其记为通过。新增自身外观 Native 门禁已在实际 WorkerW 中证明双 RGB 和同一实体往返，但不替代上述前台排除／全屏分类断言。

## 进程、路径与 DLL 边界

F5入口为 `--face-review --face-review-settings <owned-root>`，专属mutex；目录必须有虚构归属标记，拒绝日常／包目录、尾点／尾空格别名、祖先reparse及settings／preset／appearance为目录或reparse。`eng/run-face-review.ps1`要求fresh RunName，构建复制候选模块，不触碰日常DLL。

候选SHA256为 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`；受保护旧库保持 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。旧正式库缺少导出和历史Physics夹具缺失边界未被本次候选验证关闭；未机械重跑不受影响的整个套件。

真正产品进程验收通过：A进程13024用实际按钮和系统调色盘设置两色 `#F2B6A8` / `#112233`、四值 `126 / -42 / 44 / 19.5`，预览阶段无appearance文件，应用后严格七字段Schema 3。A正常退出exit0；B进程30080重新启动，真实界面读回全部六项，随后正常退出exit0。应用文件SHA256在A退出、B启动及B退出后保持 `26D7C821DA110F437BBD0EED1D3CC60D62DC3C2F2AB8BB8E02612A9C102279AA`。证据为 `artifacts/face-implementation/process-restart-v3.json`。

两次窗口 `GetDpiForWindow=144`，真实150%缩放；色盘读取由实际控件和自建PID验证，跨进程WM_GETTEXT读回，不通过直接写外观文件替代应用。只对自己的窗口PrintWindow，截图 `artifacts/face-implementation/process-restart-owned-home-13024.png`。首轮脚本将owned modal只按顶层窗口寻找、第二轮使用GetWindowText读取跨进程Edit导致失败，v1/v2证据保留；修正验收脚本后v3通过，产品未为此改变实现。

所有自动进程均正常退出；未kill或停止既有实例。最终以最新源码重新构建并启动全新默认隔离候选 `artifacts/face-review/acceptance-20261005-v1/`，验收窗口PID196，保留给用户，已打开捏脸眼睛页。窗口截图及DPI在 `artifacts/face-implementation/acceptance-window.png` / `acceptance-window.json`。该PID仅是交付当时的现场状态，不是稳定进程标识。隔离设置保留；无新服务或端口、无自动备份清理。

## 人工验收与限制

运行隔离产品后：捏脸→两种RGB（含黑色／同色）→四滑块→眨眼／睡眠／醒来→取消→默认→应用→门廊／本地房Window／Wallpaper往返→隐藏恢复→正常退出重启。默认长相与旧Windows默认并排比较，实际观感仍须用户确认。

现有三档首页布局及短窗口滚动门禁通过；150%真实应用／重启及窗口截图已验证；100%／125%真实显示器缩放、不同显示器、长时间运行和用户人工观感尚未验证。未为测试修改用户系统DPI。候选测试和备份留在自有artifacts，日常设置和旧色板源工作树保留。

实现均为本地提交：合同7aae488、存储ab13f1b、布局d5ff971、入口a28bc8b/ab85989、产品接线78c1b93、固定反馈7bfbc77。人工通过前不将候选合入main或标为已验收。
