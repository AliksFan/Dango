# P1-006 壁纸层与普通窗口模式验证记录

> 状态：已完成并通过自动门禁、真实桌面验证与用户人工验收
>
> 日期：2026-09-04
>
> 实施分支：`feat/p1-006-window-mode-host-cutover`

## 1. 实施范围

- 运行显示模式只保留 `Wallpaper` 与 `Window`；默认使用 WorkerW/Shell 承载的 Wallpaper。
- 两个宿主共享同一个 `DesktopSceneSession` 和 Core 团子实体，切换时按工作区归一化中心迁移并创建新的表现物理世界，不补播离线帧。
- Window 是可移动、可缩放、有任务栏入口、非置顶的标准 WPF 窗口；关闭按钮只隐藏场景，隐藏或最小化会停止 Tick 并释放物理世界。
- 设置升级到 Schema 2，保存确认后的显示模式、键盘与壁纸层设置和场景窗口布局；旧 `WorkerW`、`Overlay` 或无模式字段迁移到 `Wallpaper`。
- 删除旧 Overlay 运行窗口和专属当前测试；WorkerW 仍需的命中与拖动规则迁至中性 `DesktopHost/Interaction`。

## 2. 自动验证证据

| 门禁 | 实际结果 |
|---|---|
| `dotnet format .\Dango.slnx --no-restore` | 通过 |
| Release build | 通过，0 警告、0 错误 |
| Core tests | 36/36 通过 |
| Desktop tests | 125/125 通过 |
| Window WPF 生命周期 | `Show → Close/Hide → Show` 通过；重复 Show 不重复创建物理世界，退出完成释放 |
| x64 self-contained publish | 通过 |
| 发布物 `--smoke-test` | 退出码 0 |
| 发布物 `--box2d-native-probe` | 退出码 0 |
| `git diff --check` | 通过 |

## 3. 证据边界

- **静态门禁**：格式、Release 构建、全量单元测试、设置迁移、运行枚举和 Overlay 删除检查通过。
- **集成门禁**：共享 Core 场景、宿主切换回滚、普通窗口 WPF 生命周期和物理世界释放通过自动测试。
- **实时行为**：self-contained WPF 初始化、native 生命周期探针、Wallpaper/Window 真实桌面往返、关闭到隐藏、Explorer 恢复、DPI/多显示器和系统焦点检查全部通过。
- **人工验收**：用户于 2026-09-04 完成清单并确认“验收完毕，全部通过”；窗口观感、桌面干扰、焦点和两模式一致性通过。
- **交付状态**：已创建可审查的本地提交；未合并、未推送、未部署。

## 4. 人工验收结果

用户按实施计划第 12.2 节使用 Release/self-contained 产物完成 Wallpaper 与 Window 往返、关闭到隐藏、托盘恢复、Explorer 重启、DPI/多显示器、系统焦点和完整退出检查，全部通过。P1-006 的自动、实时与人工门禁均已满足。
