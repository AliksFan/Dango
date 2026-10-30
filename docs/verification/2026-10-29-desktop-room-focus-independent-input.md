# 桌宠鼠标不依赖桌面焦点：R3 输入修订验证

日期：2026-10-29。修订基线：`codex/desktop-room-single-desktop@b35ff7b`，已验收代码：`5bde536`。状态：**111 项后台／离屏回归通过，正式候选已打包，用户确认“通过，合并到main”；本轮人工验收通过，代码已快进合入本地 `main@5bde536`。未推送或更新日常安装。**

## 用户反馈与根因

用户反馈 R2 初次进入可操作，但与其它界面交互后团子失去输入，恢复房间／团子均无效。用户进一步明确：桌宠鼠标不需要 Explorer 桌面或团子窗口拥有焦点，也不需要方向键玩法。

只读检查确认正式桌宠路径未调用旧 Explorer 前台资格门禁；产品模式已关闭旧 WorkerW 壁纸输入宿主。此次故障来自另一条链：`DesktopPetDangoWindow.MessageHook → AllowsInteraction → TryReadDesktop → IVirtualDesktopManager`。每个原生消息都可能同步调用 Shell COM；跨线程同步输入消息内 Windows 拒绝该调用，宿主随后按归属故障停止输入及检查定时器，因而无法自行恢复。

新增真实宿主、真实 COM、物理驱动和 HWND 的离屏测试，用工作线程 `SendMessageTimeout(WM_NCHITTEST)` 复现：期望 `HTCLIENT=1`，实际 `HTTRANSPARENT=-1`；接收端捕获 `COMException`，HRESULT **`0x8001010D`（`RPC_E_CANTCALLOUT_ININPUTSYNCCALL`）**。红灯证据保留于 `tests/Dango.Desktop.Tests/TestResults/desktop-room-cross-thread-repro.trx`。旧同线程 `SendMessage` 测试不能覆盖该同步调用限制。

Windows 对输入同步调用中的 COM 限制见[错误码说明](https://learn.microsoft.com/en-us/windows/win32/com/com-error-codes-3)及[相关 API 说明](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiadisconnectprovider)；[`InSendMessageEx`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-insendmessageex)用于观测测试自身消息上下文。

## 修正与边界

- `DesktopPetRoomHost.AllowsInteraction` 仅读取已确认的当前桌面缓存、手动隐藏、暂停和故障状态，不查询 COM，不改变宿主生命周期，也不要求前台焦点。
- 归属仍在准备、显式恢复及既有 100 ms 健康检查中刷新。停止输入先清除缓存；切桌面仍暂停、隐藏和释放手势，回来等待新健康帧后启用输入。缓存使归属检测存在轮询间隔，真实桌面切换期间的系统 cloak 和输入捕获取消仍须人工验证。
- 归属查询失败的有界诊断保留数值 COM 错误码；不保存异常消息、堆栈、桌面标识、外部窗口或输入内容。
- 已有实体驱动保持鼠标路径，拒绝 A／D／W／S／Space 玩法键；没有新增键盘钩子或全局输入。

该修订替代 R2 在鼠标命中内即时刷新归属的实现；初始非空 GUID 注册等待及重试修复继续保留。没有额外依赖、服务或存储格式变更。

## 实际验证

| 层次 | 结果与覆盖 |
|---|---|
| 静态／构建 | `git diff --check`、Release 测试项目构建通过 |
| 跨线程红绿回归 | 修复前捕获上述 HRESULT；修复后命中返回 `HTCLIENT`，接收同步消息期间 Shell 资格调用次数为零 |
| 失去焦点通知 | 自有离屏 HWND 接收 `WM_ACTIVATEAPP`、`WM_NCACTIVATE`、`WM_ACTIVATE` 停用通知后仍可命中；`WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE`，不实际切换前台应用 |
| 暂停／恢复 | 原生消息使用已确认缓存；定时器发现非当前桌面后阻断输入，恢复等待真实新健康帧 |
| 完整定向回归 | **111/111 通过，0 失败、0 跳过**；覆盖既有透明背景、归属、单 owner、生命周期、重试、实际配饰像素、50%–200% 显示映射、存储保护及手势桥；72%／100%／200% 真实帧用例同时断言不接管玩法键 |
| 正式打包 | 普通产品入口 Release publish 到 `artifacts/desktop-room-single-desktop/candidate-r3/`，发布 DLL 与已验证 Release 构建 SHA-256 相同 |

最终结果：`tests/Dango.Desktop.Tests/TestResults/desktop-room-focus-independent-final.trx`；构建日志：`artifacts/desktop-room-single-desktop/focus-fix-build.log`。测试构建仅对既有 NU1903 局部使用 `-p:WarningsNotAsErrors=NU1903`，保留 SignalR 依赖告警，不修改依赖或仓库门禁。

后台验证使用明确不与显示器重叠的离屏区域、真实健康帧和自身原生消息；未使用全局鼠标、键盘、虚拟桌面切换或截图。进程过滤的前台观测确认测试自有窗口未激活，不记录用户其它窗口。

## 人工验收与交付

用户在 R3 候选中自行测试后，于 2026-10-29 明确回复“通过，合并到main”，本轮人工验收通过。代理未操作真实鼠标、其它应用或 Win＋Tab；该确认不扩展为完整 DPI／多屏／高对比矩阵已逐项验证。合成通知、命中消息及驱动直调仍只证明上述后台覆盖范围。

界面沿用规范 v0.1 第 2、4、4.1、6 节；本轮没有页面布局或主题变化。透明团子表面及即时保存大小继续采用已批准业务差异，真实布局／高对比验收状态不变。

交付 R3 时仅重开本轮隔离候选实例；原 R2 进程已退出，无需停止其它实例。继续使用原手动测试数据目录，保留用户 72% 大小及其它保存内容；旧候选输出保留。自动测试进程、窗口和观测已结束；供用户测试的新候选进程保留。合并阶段未操作这些进程或保存数据，未启动数据库、网络服务或端口；本次验收仅属于当前增量，不标记整个开发阶段已验收。

本轮交付时的用户复验重点：进入桌宠房后先在其它软件中正常操作，再直接左键拖动／轻抚和右键团子；随后切到其它虚拟桌面并切回，检查团子仅在原桌面出现且可继续交互。

## 本地 main 集成

- 集成前 main：`425b9df`；用户验收 R3：`5bde536`。待合入正好三项提交：`9f85974`（背景／单桌面／显示大小）、`b35ff7b`（重试与输入恢复）、`5bde536`（同步鼠标消息避开 Shell COM）。
- main 所在工作树干净，已用 `git merge --ff-only 5bde536` 快进合入，无冲突。产品代码和测试与已验收候选一致，验收后的追加改动仅同步文档状态，因此未重复运行相同代码回归。
- 原工作区、其它分支及既有修改保留；未推送、更新日常安装、部署、删除分支／工作树或清理保存数据。
