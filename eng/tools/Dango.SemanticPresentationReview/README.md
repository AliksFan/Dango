# Windows 隔离双人语义呈现

按 `docs/planning/2026-10-11-dual-player-windows-first-plan.md`，复用R1冻结模块及三份完整authority语义轨迹。每个视角拥有独立sampler／native replica／adapter／真实DeformableDangoVisual，仅相机不同；未联网，不接正式服务或数据库。

命令参数：`--verify[-push|-stack|-withdraw]|--window-check|--review <R1_MODULE> <R1_SEMANTIC_DIRECTORY> <ProductHomeView.xaml> <NEW_EVIDENCE_DIRECTORY> [MUTATION]`。输出目录必须新建或为空。模块与三份trace逐bytes硬锁，运行后复核。正式自动执行按三个场景独立进程，每进程60秒，不扩大全部600步或几何门。首轮九组合单进程在7组完成后触发60秒截止，原进度和退出证据保留，未计整批通过。

`--verify`执行三个场景、30/60/144Hz三组调度及三种真实WPF离屏布局。从BodyPath已应用的真实quadratic曲线／ancestor transform独立测量中心与128点SAT，不复制物理层geometry结果。显示调度次数不代表真实FPS。`wrong-origin`、`shared-replica`、`fallback`、`stale-visual`分别要求`actual-authority-center`、`replica-ownership`、`actual-fallback`、`visual-membership`失败并exit1。

`--window-check`实际打开WPF窗口，以Rendering回调完成三组600步后关闭；120秒工具期限，无持续观察冒充。`--review`打开相同交互候选并保留到人工关闭。窗口隐藏／最小化不推进；UI负载时最多补4步并允许回放变慢，不作为服务端调度或59FPS证据。600秒有效观察、OS DPI／辅助设置、人工和双机分别待验收。

Home刷子从现有首页XAML资源读取，不另建颜色表；技术窗无编辑保存操作、滚动内容或玩家输入，按规范v0.1 §§2–6保留必要状态文字。无生产代码、solver／参数／ABI改动，Linux排障保持停止。
