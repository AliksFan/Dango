# Phase 2A：本地最小多人同步实施记录

日期：2026-09-30。范围依据：[详细设计](../planning/2026-09-24-minimal-multiplayer-sync-design.md)、[M0–M7 计划](../planning/2026-09-30-minimal-multiplayer-sync-plan.md)。

## 交付与边界

已实现 Protocol、ASP.NET Core/SignalR 服务端、客户端 Replica/恢复、独立 native/WPF 表现和本地会话入口。一个内存房间、两个虚构身份，只有左/右/停/非接触招呼；服务端拥有身份、连续事件序号、幂等及业务裁定。没有数据库、迁移、账号、共享碰撞、手机、云部署或容量结论。

同一台电脑实际启动 Server+A+B 三个进程：双 native 和 A native/B authored fallback 两种配置均跑通移动、真正 Stop 事件、同一招呼、动作到期、断线/恢复、最小化停止及恢复不追发旧输入。两端在相同服务端时刻比较完整规范状态，窗口仅显示确认状态。私有匿名管道传递票据/令牌；日志与导出采用白名单，不含凭证、真实桌面内容或全局按键。

**这不是 Phase 2A 已验收**：M7 双机、不同 DPI/视口、真实网络中断及用户观感仍待安排。自动脚本操作真实 WPF 窗口和窗口自身的离屏捕获，不代表用户已经看过或认可效果；渲染次数不是 FPS。

## 实现结构

| 位置 | 作用 |
| --- | --- |
| `src/Dango.Protocol` | v0.1 DTO/校验、有限数字 JSON、移动与动作时间采样 |
| `src/Dango.Server` | 有界单读者房间、身份代际、幂等/ordinal、内存快照、真实 Hub |
| `src/Dango.Client.Sync` | 连续事件 Replica、连接/同步双 fence、时钟、命令账本、时间水位修复 |
| `src/Dango.Desktop/Multiplayer` | 每 pet 一个 capacity1 native 世界、局部轮廓/眼帽、服务端时间表现 |
| `eng/tools/Dango.MultiplayerSample` | 独立 A/B Window、焦点输入、私有管道启动器和证据输出 |
| `eng/run-multiplayer-review.ps1` | 核验 DLL 指纹、最终构建定位、项目内启动入口 |

不接入日常 App/Wallpaper/单机随机生活；不修改旧 `dango_box2d.dll`。各实现线在独立 Worktree 开发，按 Protocol → Server/Client → Desktop 顺序本地集成到 `codex/p2a-minimal-multiplayer-sync`。`main` 和阶段分支未合入本实现，未推送、部署、更新安装或清理 Worktree。

## 自动证据

统一证据根：`artifacts/multiplayer-sync/implementation/results/`（ignored，保留本地）。

| 类别 | 已运行结果 | 证据目录 |
| --- | --- | --- |
| Protocol | 6/6；JSON `1e999` 溢出先失败后由有限数字转换器修复 | `m1-finite-green` |
| Server | 18/18；含真实 Kestrel 子进程、强制 WebSockets、双连接与重启新 epoch | `m3-server` |
| Client.Sync | 30/30；27 单元/可控传输 + 3 真进程网络矩阵 | `m4-final-overflow` |
| 多人 Desktop/隔离 | 14/14；12 表现/输入 + 2 会话目录隔离 | `m5-desktop` |
| Core 回归 | 37/37 | `m0-core` |
| Native M0 探针 | 1/1；600 步，两世界无跨团子接触、有限且有界的 core-local 轮廓 | `m0-native-local` |
| Physics 非长稳回归 | 20/33 通过，13 项因旧比较 DLL 缺失无法验证；0 跳过 | `m0-physics` |
| Desktop 完整回归 | 563/564 通过，1 项旧 DLL 缺 `dango_box2d_self_bodies_create` 导出失败；0 跳过 | `desktop-full` |

上述目标测试均实际发现并执行，不用编译成功、0 用例或 skipped 替代。缺少的旧比较 DLL 是 `collision-ratio-build`、`physics-module-build`、`physics-three-preview-build` 产物；没有用新统一 DLL 冒充这些旧基线。完整历史长稳、第二 DPI、容量等没有补作通过结论。

至少六类错误实现均有有效可编译红例，恢复实现后重跑为绿：

1. 同命令生成第二次事件：`delegated-server/mutation-duplicate-red`。
2. 忽略当前连接代际：`delegated-server/mutation-generation-red`。
3. 按到达顺序先应用事件 12：`delegated-client/red-replica`。
4. 同连接迟到快照反退：同上。
5. 丢最后事件而无时间水位检查：同上。
6. 使用 native CoreX/CoreY 放置网络团子：`delegated-presentation/red`。

另有固定步长、数字溢出、30 秒过期回执、诊断白名单，以及事件缓冲溢出后旧快照不能恢复 Live 等先行红/绿证据。缓冲溢出的有效红例见 `delegated-client/red-overflow-fence`，新快照水位不低于已丢失的事件后才能恢复；红例没有作为故障开关留在生产路径。

M0 曾错误地以“平移两个 native 世界后逐顶点相等”判断 core-local 契约，600 步后差异达约 0.014；原失败记录保留。native 源码明确输出 node-core，实测同起点两世界零差、平移后误差随浮点求解累积。因此改为直接验证局部包络和无接触，并在 M5 对**同一 body 只变 CoreX/CoreY**，严格断言网络落点完全不变；不是增大原来的误差容忍值。

## 时间、有限资源与测量

本地参数版本 `p2a-local-1`：协议 0.1；x 范围 0.08–0.92、地面 0.12、初始 x 0.30/0.70；移动 0.12 单位/秒、段最长 800 ms、续约间隔 250 ms；招呼 1200 ms。业务限频 10/s、burst 20；报文 4 KiB；每身份待处理 64、房间队列 128、outbound 256、Replica buffer 64；结果 120 s/2048、逻辑重试最多 30 s；RPC 2 s、水位 2 s、无响应 watchdog 6 s。

native 固定 1/60 步长，每次最多 4 步；大于 100 ms 的帧间隔丢弃积压；隐藏不步进，恢复先同步。30/60/144 Hz 采样的 native 步数与最终局部轮廓等价；不会高速补播旧动作。共享状态只按服务端单调时间采样。

网络矩阵通过真实 WebSockets，在客户端测试传输外层注入 RPC 往返和广播单程延迟，RTT 0/100/300 ms，加 0–10 ms 抖动；不是物理 WAN 测量。丢最后一条事件、丢已提交回执、离线输入与恢复均收敛；原始实测 RTT/修复/重连时间在 TRX 的白名单诊断中。两 WPF 首轮同机恢复测得约 68 ms、35 ms（包含重新加入与状态对照）；只是两次本机观察，不是 P95 或公网承诺。

这些数值冻结为首版本地可复现参数，并未证明容量、网络观感、长稳或生产安全。

## 实际入口与退出

```powershell
pwsh -NoProfile -File ./eng/run-multiplayer-review.ps1 -RunName review-unique-name -PhysicsModulePath <核验过的统一模块DLL> -Build
```

必须使用新会话名，拒绝已有目录和路径穿越。A/B 使用独立窗口、实例互斥键和目录；不读写日常设置。服务器仅动态回环 HTTP/WS；这不是局域网部署配置。双机 WSS/证书/防火墙另行准备，不关闭证书验证。

自动真实窗口验证附加 `-Verify`；验证另一端降级再附加 `-FallbackB`。关闭 A/B 后服务通过私有 STOP 正常退出；超时只报告本会话 PID/端口，不杀其他 Dango、不删除证据。

最终构建同机证据：`artifacts/multiplayer-sync/m6-window-native-01/` 和 `m6-window-final-01/`。manifest 含实际 Server DLL/客户端/Protocol/native 指纹；状态对照、窗口自身 PNG、恢复用时和 teardown 各自保存。双 native 与一端 fallback 均通过，teardown 均确认三进程退出、code 0；服务器分别在动态回环端口监听，仅会话期间存在。首轮 `m6-native-01`、`m6-fallback-01` 和截图修正前 `m6-final-fallback-01` 证据也保留用于追溯，不以窗口 RenderTargetBitmap 留图误差冒充运行显示问题。

SDK 实测 10.0.303；SignalR.Client 固定 10.0.11，官方文档/包兼容与 MIT 许可证已核查。统一 native SHA-256：`00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`。旧 DLL 保持原样。
