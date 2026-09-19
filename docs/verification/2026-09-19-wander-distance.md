# 自主移动距离调整

2026-09-19 用户对上一候选反馈“距离太短了，至少也要走10分之一个屏幕”。该反馈授权修改本批距离要求，不视为原候选人工验收通过。

## 调整与验收含义

- 目标从 24–48 DIP 改为当前显示器可用宽度的 12%，正常、无打断完成后净位移至少 10%。预留 2% 余量给真实减速回弹；不更改 0.35 水平输入强度或 200 ms 渐入。
- 旧 2 秒驱动上限改为 `2 + 目标DIP / 20` 秒的保护预算；达到目标即松开输入，继续自然物理减速。不清零速度、不瞬移、不修改 native 参数。
- Window 从所在显示器取工作区宽度；Wallpaper 依据团子中心的屏幕位置选显示器。两者均按当前宿主 DPI 换算 DIP，不使用窗口宽度或多屏总宽度。有效宿主未提供屏宽时不调度；屏宽变化取消并完整重排，旧宿主不能更新当前屏宽。
- 30–60 秒等待、单实体、左右 16 DIP、上下完整可见、32 DIP 滑行预留不变。空间不足选择另一方向或跳过，不缩短为不足屏宽 10% 的动作。手动输入、离地、边缘、隐藏和故障仍优先取消，不为走满距离阻止接管。

## 自动证据

证据根：`artifacts/short-wander/distance-20260919/`。

| 检查 | 结果 | 证据 |
|---|---|---|
| 回归反例 | 新断言在旧实现下失败：1200 DIP 屏幕仅走到 40 DIP 前已停止；修改后通过 | `red/red.trx` |
| 新版移动专项 | 32/32，覆盖实际长距离、两方向、10/20/40 ms、默认小窗口、边缘和接管；之后另补屏宽所有权用例纳入最终回归 | `targeted/targeted.trx` |
| 1280/3840 DIP 实测 | 最终 153.42–461.79 DIP，占屏宽 11.986%–12.057%；驱动 4.8–14 秒，最大释放后偏移约 0.34 DIP，重新连续稳定 0.50–0.52 秒 | `measurements.json` |
| 最终 Release solution build | 0 warnings/errors | `final-build.log` |
| 最终 Core | 37/37 | `final/core/core.trx` |
| 最终 Desktop | 411/412；相关 89 项全部通过；唯一失败仍为旧库缺 `dango_box2d_self_bodies_create` 的 `SharedSelfWorldExportsAreAvailable`，无新增失败 | `final/desktop/desktop.trx` |
| 最终 Physics 常规 | 32/32，排除稳定性长测 | `final/physics/physics.trx` |
| 最终 format | 仅原有 5 个文件问题，无新增诊断；不标记全绿 | `final/format.json` |

最终完整回归无新增失败；源代码未在最终构建后再修改。本次首次编译因新增 WindowInteropHelper 名称未限定命名空间而失败；修正为完全限定名后构建通过，原日志 `targeted.log` 保留，不将编译错误当作行为断言红灯。文档相对链接及 `git diff --check` 通过。

保护指纹见 `final/hashes.json`：旧生产库保持 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`；统一模块及候选副本均为 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`。

## 交付与未验证

经用户明确确认，核对路径后结束旧短距离候选 PID 31740，并从本证据根下 `final/build/bin/Dango.Desktop/release/Dango.Desktop.exe` 无参数启动普通 Product 候选 PID 23344。已确认旧进程结束、新进程响应正常、Dango 窗口句柄非零，且加载的统一物理模块来自本次候选目录、指纹与上文一致。证据见 `final/launch.json` 和 `final/launch-observed.json`；未手动改写设置。新版人工验收待完成。

真实多显示器切换、第二种真实 DPI、Window/Wallpaper 长距离观感及完整故障矩阵未验证；数值测试不代替桌面验收。稳定性长测未运行。未提交、合并、推送或部署，没有改动旧实验树及 DLL。
