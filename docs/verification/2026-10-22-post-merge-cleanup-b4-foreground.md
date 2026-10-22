# B4首轮前台回归：未通过

当前状态（2026-10-22）：用户在独立数据的正式候选包人工体验后明确“验收通过合入main”，本次候选人工通过，并获本地合并授权。家具完整用例及真实鼠标尺寸菜单／快捷键定向检查已通过；竖屏两项继续按用户指示暂缓，历史失败与未覆盖的环境专项保留。下文按时间保留首轮结果及后续修正。

日期：2026-10-22。源快照`3ee855383cf17ef304eaf5f7a57ce1194a004782`，分支`codex/post-merge-cleanup-b3-b4`；[首批交付](2026-10-22-post-merge-cleanup-b4.md)的271项定向自动结果保留为其原覆盖范围。

用户明确批准约6分钟前台激活及全局键鼠操作，范围为B4编辑保存、输入释放和房间往返，使用各用例创建的虚构独立数据。实际可见测试时间为北京时间11:31:12至11:36:51，339.1秒，未超过360秒预算；之后仅执行无窗口基线布局对照与静态核对。占屏结束后自有Desktop／testhost均已退出，原测试finally释放A/D/W/S/Space及鼠标左键，未触碰日常数据、系统显示设置或真实高对比开关。

桌面规范v0.1第2／4／6节适用于主题和状态核对；本轮只验证实际显示器的窗口预设，未改变OS DPI，不宣称完成多屏／多DPI或人工观感验收。

## 最终结果与保留失败

按相同用例取最后一次结果，共**16项：12通过、3失败、1未执行**。初次失败、基线对照与修正后结果分文件保留，不用重跑覆盖历史。

| 范围 | 结果与含义 |
| --- | --- |
| 正式默认host、编辑保存／取消／隐藏／重启 | 通过；真实连续身体、显式保存与回读、隐藏暂停及同一owner相关断言通过 |
| 全局键鼠与焦点 | 首页／标题／捏脸按钮三位置通过；文本、菜单、失焦／重激活、设置暂停相关断言通过；修正陈旧断言后，真实D/W/S同按水平力、实际身体拖动／释放完整用例通过 |
| 桌宠 | 双表面单实体／单owner、隐藏恢复以及回门廊后释放两表面3项通过 |
| 当前显示器窗口预设 | Landscape1080p／Landscape1440p两项真实物理像素测量通过；没有切换系统DPI |
| 竖屏编辑布局 | Face／Wardrobe两项失败，打开编辑器后预览Top没有按用例预期上移；生产源码等于整理前753c695的基线也同样失败，不能算本轮新增回归，也不能记作已修复 |
| 普通房家具往返 | 在`RequestNearbyFurniture()`即返回false，尚未进入家具编辑／保存／隐藏／往返断言；这些后续行为**未验证**。静态发现夹具`ResumePorch()`仍停在560，而当前目录普通房门从780开始，560为桌宠门；需先定位并校正真实入场前置，再重新验证，不能把原用例的失败改成通过 |
| 尺寸菜单真实鼠标点击 | 未执行：MSTest未取得前台焦点，安全检查在注入点击前终止；不能以其它焦点测试替代此项 |

## W/S陈旧断言的定位与修正

初次`FocusedOsKeysAndMouseReachNewBodyAndReleaseOnFocusLoss`在W/S双按处超时。独立基线生产源码经`git diff 753c695 HEAD -- src/Dango.Desktop`确认无差异，Release配置使用同哈希product／room DLL，再跑同用例仍在同处失败。

[已批准规则](../planning/2026-10-21-balanced-ws-control-proposal.md)及[既有交付](2026-10-21-balanced-ws-control.md)要求双按仅抵消竖向控制，保留A/D等独立输入；原断言期待`Stretch=0, Downward=0, CancelForces=true`属于已退役的全取消语义。本轮仅修正测试：真实OS输入D＋W＋S时，要求原始W／S通道都为1、`CancelForces=false`、水平输入为1，且真实native `Stats[18]>0`；继续执行原失焦清理与鼠标拖放断言。修正后1/1通过。没有修改产品或native实现，没有删除原业务断言。

## 证据与交付

证据根`artifacts/post-merge-cleanup-b4/foreground-20261022-01/`：

- `input-and-editors.trx`：最初6通过／1失败；`baseline-focused-input.trx`：基线同点失败；`focused-input-approved-ws.trx`：修正后1通过。
- `desktop-room-and-layout.trx`：5通过／2失败／1未执行；`baseline-portrait-layout.trx`：无窗口基线Face／Wardrobe同样失败。
- `ordinary-room-roundtrip.trx`：家具请求前置失败，后续断言未执行。
- `summary-final.json`：最终去重16项；`screen-release-final.json`：按显式DateTimeOffset计算339.1秒；`process-audit-final.json`：无自有应用／测试进程残留。

初版`screen-release.json`的elapsedSeconds受隐式日期转换影响不正确，初版`summary.json`的分组计数不正确，两者保留，以上述`*-final.json`为准。TRX原始开始／结束时间未改动。

当前仍为**未验收、暂不合main**。下一轮先处理普通房入场夹具和竖屏失败定位，再申请新的占屏预算复测真实菜单与受影响流程。真实OS高对比切换、多DPI、多屏和用户人工观感仍未验证。仅测试／文档改变，原B4内部候选包生产源码及v4设置合同保持，无需据此重建产品包；未推送、安装或部署。

## 家具入口与快捷键续行

用户随后明确：竖屏暂不处理，修正家具测试入场，并在上方已有快捷键的按钮前标出按键。本节更新当前状态，上节保留首轮交付时的结果与计划。

- 入场修正提交`8ff3f54`：读取真实目录的活动普通房身份，按目录门位设置门廊起点，等待同一generation的真实健康呈现确认，并断言附近门身份匹配，再调用原`RequestNearbyFurniture()`。没有绕过入口资格、房间交接或删除后续断言；未修改产品房间／家具行为。
- 按钮提交`b8148d5`：首页原Q／E／F键帽保留；门廊进入／新建前加F，布置前加E，设置／门廊返回及编辑收起前加Esc；同时补辅助快捷键元数据。设置、好友、登录等无现成快捷键的按钮保持原状。遵循桌面规范v0.1第2／4／6节，复用已有22 DIP键帽及共同主题，沿用实际按键分派和业务差异，没有新增快捷键或修改竖屏布局算法。
- 后台验证：共享主题／设置合同7/7、LocalRooms相机／存储／目录／恢复94/94，零跳过；两个项目构建通过，`git diff --check`通过。独立只读审查未发现入口绕过、按键映射或资源作用域问题。
- 本次另获约2分钟占屏授权。北京时间13:18:46至13:19:14完成并释放，占用28.5秒；`ActualProductRoomPreservesWorldAndAppearanceAcrossEditSaveHideAndPorch` **1/1通过**，完整执行真实普通房布置、保存回读、相机投影、50次编辑切换、隐藏恢复、返回门廊清理、重进及删除断言。测试窗口随host释放，无自有Desktop／testhost残留，没有全局键鼠注入或服务端／数据库资源。
- 本次修复与结论限于错误入场前置。完整用例一次通过不证明所有历史偶发问题已消失；首轮家具FAIL与其“后续未验证”仍保留为当时快照，当前该路径已有上述通过证据。
- 竖屏Face／Wardrobe两项按用户指示**暂缓，未改、未复测**。真实鼠标尺寸菜单、OS高对比、多DPI／多屏及快捷键标注人工观感仍未验证，不由本轮构建或家具通过替代。

证据目录`artifacts/post-merge-cleanup-b4/furniture-and-shortcuts-20261022-01/`：`shortcut-labels-background.trx`、`furniture-entry-background.trx`、`ordinary-room-entry-fixed.trx`、`screen-session.json`及`screen-release.json`。运行时源码为`8ff3f54`加待提交的两份UI文件，文件内容随后原样提交为`b8148d5`。

内部候选包已按干净源码`b8148d522a3ef2b6584f16cf59b635fcef457801`更新：`artifacts/desktop-entry/b4-furniture-shortcuts-20261022-v1/package/`，manifest为`sourceDirty=false`、482份文件，v4设置源码合同不变。发布预检10/10，同包`--smoke-test`无host启动、exit0并退出；证据为本节目录内`publish.log`、`preflight/summary.json`及`smoke.json`。不安装、不合main、不推送或部署。原main的AGENTS未提交修改和保护DLL哈希均保持；两个工作树及虚构测试产物保留。

## 尺寸菜单与快捷键收尾检查

用户明确授权1～2分钟。干净源码`33ffb64d2b30034e59b96e77e5c81bb9de9b3b2f`，Release既有程序集，无源码改动或重新构建。授权执行区间北京时间13:28:05.994至13:30:05.721，包含准备、后台检查与资源清点，共119.7秒；实际用例时间按TRX记录，不把准备或关闭后的时间计为有效窗口／物理运行。

| 验证层次 | 用例与结果 | 实际含义 |
| --- | --- | --- |
| 真实全局鼠标 | `RealMouseSizeSelectionRestoresTheHomeStageFocus` 1/1通过，13:28:08至13:28:10 | 实际Win32鼠标按下／松开依次点击尺寸按钮与预设；菜单关闭后100／300／600ms窗口仍激活，Stage均拥有键盘焦点。既有用例选择Portrait仅检查菜单及焦点路径，未运行暂缓的Face／Wardrobe竖屏编辑布局用例，也未改变OS显示设置 |
| 无窗口导航合同 | `NavigationKeysToggleEditorsPreserveDraftBoundaryAndUseFForPorch`、`NavigationDoesNotRunInSettingsDialogsAwayStateOrWindowMenu` 2/2通过 | 直接调用导航分派，验证Q／E／F、重复键、草稿切换和设置／对话框等阻止条件；不宣称真实OS键盘注入 |
| WPF布局及控件状态 | `LauncherShortcutLettersFollowTheSelectedWardrobeForegroundInHighContrast` 1/1通过 | 960×540布局中的既有Q／E／F字母正确继承按钮及选中高对比前景；使用组件主题API，未切换OS高对比 |
| 已加载WPF窗口／事件 | `RapidReverseEscAndReducedMotionLeaveOnlyTheLastRequestedState`、`WardrobeControlKeysDoNotNavigateAndEscapeCancelsWhileOtherHomeShortcutsRemain` 2/2通过，末项13:29:46.936结束 | 实际控件点击／WPF键盘事件验证收起、Esc取消草稿、焦点恢复和转场终态；测试窗口位于屏幕外，不作为前台观感或真实OS键盘验证 |

本轮**6项通过、零失败、零跳过**，修复了首轮尺寸菜单因未取得前台而“未执行”的验证缺口。先前Inconclusive原件保留，不改写历史结果。真实鼠标用例finally恢复原鼠标坐标及原前台窗口；各窗口由finally关闭，资源清点无自有Desktop／testhost残留，无全局键鼠占用或服务端资源。

证据目录`artifacts/post-merge-cleanup-b4/menu-shortcuts-20261022-01/`：`real-mouse-size-menu.trx`、`shortcut-navigation.trx`、`shortcut-render-and-escape.trx`、`results-summary.json`及`screen-release-final.json`。初版`screen-release.json`因PowerShell将JSON时间隐式转为DateTime后再次Parse而丢失起始小数秒，elapsedSeconds偏大；初版保留，以最终文件直接解析原始DateTimeOffset字符串所得119.7266714秒为准。

本轮只增加验证记录，不修改产品或测试源码，内部候选包继续使用`b8148d5`版本。新增门廊F／E和收起／返回Esc标注的图像、裁切和人工观感尚未独立验收；用户简短体验、真实OS高对比、多DPI／多屏仍未验证。竖屏编辑两项继续暂缓，阶段不因此标记已验收，未合main、推送、安装或部署。

## 用户人工验收与本地合并授权

2026-10-22，用户要求开始人工验收后，启动并校验正式内部候选包`b4-furniture-shortcuts-20261022-v1`，生产源码`b8148d522a3ef2b6584f16cf59b635fcef457801`。使用新建的独立虚构数据目录，未更新日常安装或读取日常设置；会话及反馈凭据在`artifacts/post-merge-cleanup-b4/manual-acceptance-20261022-01/`。

交给用户的首页检查包含Q／E／Esc及按键标注、外观取消／应用／重新打开、两种横屏尺寸。用户随后明确回复**“验收通过合入main”**，据此记录本次候选人工通过并授权将当前整理分支本地合入main；不额外推断用户逐项执行了未报告的房间、系统高对比或其它环境测试。

原普通房家具完整流程的通过依据仍为前述真实宿主自动用例。竖屏两项暂缓、真实OS高对比、多DPI／多屏和历史偶发／UNKNOWN边界继续保留；本次通过不等于完整Windows／多人／性能阶段验收，也不授权推送、安装、部署或删除工作树／数据。此前“待人工”“未合main”的段落均为当时快照。
