# 步态项目小样

从当前任务项目启动真实 Product 单团子，观察自主散步及 WASD 横移的贴地形变。使用独立虚构设置与互斥身份，不更新日常安装。

先按[实施计划](../../../docs/planning/2026-09-22-gait-sample-plan.md)验证统一物理模块并构建 `Dango.slnx`。向构建实际产出的 `Dango.GaitSample.exe` 传入一个绝对路径参数：当前仓库 `artifacts/gait-sample/` 下尚不存在的新会话目录。程序拒绝已有目录、越界路径、重解析目录、错误 DLL 或第二个候选实例。

窗口默认启用交互及 WASD，两个 Review 模式均关闭。点击场景获得焦点后，用 A/D 横移、W 跳跃、S 下压；也可等待原有自主散步。控制中心可更换三种现有帽子、隐藏/显示、切换模式和退出。效果强度先受最大新增位移 2 DIP 限制，实际自然感由人工判断。

会话目录保留独立设置及 `started.json`、`exit.json`；出错记录 `failure.json`。`started.json` 只证明真实宿主 Ready、可见模式及程序集身份，不代表步态或人工验收通过。退出记录检查 runtime、场景、附件、托盘、窗口和宿主调度资源；Windows 宿主没有 shell timer，其字段为 null。

Wallpaper 的全局键盘控制可能与日常实例抢键。存在冲突时先观察 Window；由用户关闭冲突实例的桌面键盘控制或自行退出后，再验收 Wallpaper 键盘。程序不处理日常进程。
