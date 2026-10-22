# 本地模式前台补验：中止，未完成

日期：2026-10-22。分支 `codex/local-mode-closeout`，执行基线 `4de4aaf8daf2bb3779c18f42d04e0896f3bed285` 加本轮验证工具。生产代码未改，仍使用 `local-closeout-feedback-20261022-v1` 正式内部候选包，生产源码 `90e974353d9e2daf28c3c62f7873b720fb93be93`、`sourceDirty=false`。

用户已授权约15分钟屏幕及输入使用。执行中申请增加约8分钟，用户选择“先结束占屏，记录未完成项”；随后已停止所有本轮窗口和输入。用户对配饰与QQ的反馈为“暂时没检查”。本轮不记录人工通过，也不标记本地模式或Phase 1验收。

## 实际执行与结果

| 项目 | 结果 | 证据与边界 |
| --- | --- | --- |
| 候选包及来源 | 通过来源核对 | `Test-DangoPackage`通过；probe内4个managed DLL、2个native DLL及native manifest与候选包逐字节哈希一致。产品源码相对包来源无差异。此项不证明实时体验通过。 |
| 正常候选EXE展示 | 已启动，人工未验 | 北京时间15:47:32启动PID8368，独立标记虚构数据；15:51:22结束。先请求关闭，进程仍存活后仅终止该自有候选进程。没有更新日常安装。 |
| 普通房R1 | 前置失败，有效0秒 | PID13924，进程15:53:15至15:53:39。目录等待的15秒错误地从整个夹具启动前计时，包含冷启动；在目录回调处理前超时。已将目录和交接各自改为独立阶段deadline，没有修改生产代码或600有效秒门禁。此原因由源码和16.56秒总耗时支持，未做单因素冷启动重放。 |
| 普通房R2 | 部分覆盖，未通过 | PID17744，进程15:57:45至16:04:44；实际Host进入普通房，2件真实家具，使用产品driver/scheduler及真实OS A/D/Space。最后保留样本为16:04:15：有效**88.8043秒／目标600秒**，总elapsed389.3316秒；native tick37→6795，采样fault=0，身体X范围254.28～1551.43，17次Space请求并观察到native接受跳跃。没有完成600秒，不以进程存活时间代替有效运行。 |
| 持续推球／平台 | 未完成 | 最后样本`ballMoved=false`，仅表示有效采样期间未观察到足够球位移，不能说球没有模拟或产品失败；没有实际推球通过证据。平台接触未单独测量。 |
| 桌宠真实OS鼠标命中／穿透 | **未运行** | 新窄探针已编译，但未启动。既有桌宠NativeInput用例使用`SendMessage`点按，窗口命中断言不能代替真实OS点击路由。 |
| 配饰清晰度／反馈QQ | **人工未验** | 现有清晰度版本仍为512×512内置纹理，逻辑尺寸保持64；候选设置页含`反馈 QQ：2085475782`。用户明确尚未检查，不从源码、构建或已打开窗口推断观感通过。 |

R2有效计数同时要求真实房间前台、WPF激活／可见／非最小化、driver ready／owner／运行，以及native tick推进；以native时间限制计入值。`excludedSeconds`包含启动和不满足任一条件的时段。当前证据没有逐项资格快照，**不能把全部排除时间归因为失焦，也不能据此认定产品缺陷**。执行中曾请求恢复自有房间焦点，仍不足以完成该门禁。

桌面界面核对沿用规范v0.1第2～6节；本轮没有改版、独立主题或新增页面例外。暖白／棕色强调、设置滚动可达性与配饰视觉的人工结论仍待确认；未执行DPI、多屏、Windows 10或性能矩阵。

## 工具、构建和证据

新增验证工具：

- `eng/run-local-room-visible-check.ps1`：独立数据、明确屏幕授权开关、包来源／二进制哈希绑定、自有子进程及失败证据。
- `eng/tools/Dango.LocalRoomProbe/BeltVisibleObservation.cs`：真实普通房壳层、OS按键、有效时间及进度记录。
- `eng/tools/Dango.LocalRoomProbe/BeltDesktopMouseVerification.cs`：待执行的真实`SetCursorPos`／`SendInput`桌宠检查，以自有虚构底层应用接到点击作为穿透证据。
- `Program.cs`只新增上述两个显式工具入口，不改产品入口。

R1、R2探针构建均0警告／0错误；这些工具构建不代表桌宠鼠标或完整Desktop测试通过。最初集成预构建曾因缺少namespace及鼠标文件尚未落盘失败，随后补齐；没有修改产品、依赖或门禁以掩盖失败。

保留目录：

- `artifacts/local-mode-closeout/visible-room-r1/`：build、来源／哈希、进程和失败summary。
- `artifacts/local-mode-closeout/visible-room-r2/`：第二轮相同来源证据与未通过summary。
- `artifacts/local-room-catalog/runs/visible-room-r1/visible-result.json`：前置失败及正常finally释放证据。
- `artifacts/local-room-catalog/runs/visible-room-r2/visible-progress.json`：**最后样本，不是最终完成结果**。
- 同一R2目录`user-stop.json`：用户停止、释放与进程结束；没有伪造`visible-result.json`。进程退出码-1来自主动终止，不记为native故障。
- `artifacts/local-room-catalog/runs/foreground-manual-20261022-01/`：真实候选EXE启动／释放记录及独立虚构数据。

## 停止、资源与交付

收到停止指令后先隐藏仅属PID17744的自有顶层窗口，使观察循环释放自己注入的键；确认A／D／Space均已松开后，精确核对exe路径并结束该进程。16:04:44完成；本轮没有注入鼠标按钮。R2未执行到正常managed finally，不能声称完成managed owner／native handle释放断言；进程退出由OS回收其窗口和native资源。R1已正常finally清理，所有wrapper报告自有子进程退出。

最终进程清点没有Dango进程残留，无本轮窗口或全局输入占用；虚构数据与失败原件保留，没有服务、端口、数据库或云资源。main已有未提交`AGENTS.md`及保护DLL哈希保持不变。

停止后的只读审查发现runner异常时直接Kill可能跳过自有输入释放。已补协作取消：向独立root写取消信号，观察／鼠标pump先退出并执行finally；超时兜底先隐藏精确归属的窗口，确认A／D／Space和鼠标左键均松开后才允许硬终止。若输入仍按住，保留隐藏进程并报告清理失败，不释放来源不明的输入。该修订只做后台构建／静态检查，取消和兜底路径的真实行为尚未复测；R1/R2仍绑定各自记录的旧工具源码哈希。

当前只交付验证工具与如实记录，未合main、推送、安装或部署。已有[跨进程恢复R5](2026-10-22-local-mode-cross-process-recovery.md)通过范围保持。Schema1历史UNKNOWN和NU1903未因此关闭。

后续先在不占屏条件下核对计时资格诊断和输入／家具夹具覆盖，避免无效长测；取得新的屏幕时段后，再做短验证确认夹具有效、600有效秒持续运行、真实桌宠鼠标检查及配饰／QQ人工确认。本轮授权已按用户要求结束。
