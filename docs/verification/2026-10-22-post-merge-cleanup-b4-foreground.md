# B4首轮前台回归：未通过

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
