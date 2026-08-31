# Dango Windows 客户端技术栈专项调研

> 状态：提案，等待用户批准
>
> 日期：2026-08-31
>
> 所属阶段：Phase 0 — 工作区与技术基线

## 1. 调研目标

为 Dango 的 Windows 客户端选择 Phase 1 原型候选技术，重点回答：

- 哪种方案最适合长期驻留桌面、透明显示、选择性输入和多显示器？
- 哪种方案能够以较低复杂度实现二维团子与悬浮岛？
- 如何在 Windows 优先的同时，为未来移动端保留协议、数据和资产扩展空间？
- 哪些能力已有官方支持，哪些仍依赖 Windows Shell 实验？

本轮只做调研和推荐，不创建正式客户端脚手架，不把提案写成已批准 ADR。

## 2. 已确认约束

- 首发只支持 Windows。
- 目标覆盖 Windows 10 22H2 x64 和 Windows 11 x64。
- 团子采用二维软萌治愈表现，具体动画工艺尚未确定。
- 桌面稳定、低资源、输入穿透和多显示器优先于跨平台 UI 复用。
- 未来移动端共用账号、房间协议和服务端，不强求共用客户端 UI。
- 首版通过直接安装包分发，不依赖 Steam 或 Microsoft Store。
- 优先免费、开源或无需运行时授权费的成熟技术。

## 3. 关键事实

### 3.1 Windows 10 22H2 是兼容目标，不是供应商支持目标

微软已于 2025-10-14 结束 Windows 10 Home 和 Pro 22H2 支持。`.NET 10` 是当前 LTS，支持至 2028-11-14，但其官方 Windows 列表已不包含普通 Windows 10 22H2，只保留仍在生命周期内的 Windows 10 Enterprise/IoT LTSC 等版本。

因此 Dango 可以继续实测和尽力兼容 Windows 10 22H2 x64，但必须：

- 在真实 Windows 10 22H2 设备或虚拟机上持续回归。
- 把“项目测试兼容”与“微软官方支持”分开表述。
- 不为了获得已经不存在的 OS 官方支持而退回即将结束支持的旧 .NET。
- 当安全、驱动或桌面 Shell 差异无法合理维护时，允许重新评估兼容范围。

### 3.2 透明窗口不等于真正桌面壁纸

WPF、Godot、Tauri 和 Electron 都能创建无边框或透明窗口，部分还提供鼠标穿透。但 Dango 还要求：

- 与桌面图标、任务栏和普通应用正确分层。
- 只有团子或家具交互区域拦截输入，其余区域穿透。
- 能够表现“团子从桌面爬上悬浮岛”。
- 在 Explorer 重启、多显示器和 DPI 改变后恢复。

Windows 官方提供窗口透明、扩展样式、父子窗口、DPI 等基础 API，但常见的 `WorkerW` 动态壁纸挂载方式依赖未公开的 Shell 窗口结构。任何框架都不能仅凭“支持透明窗口”证明它能稳定完成 Dango 的桌面体验。

所以，桌面承载方式必须通过 Phase 1 原型决定，不能只做文档选型。

### 3.3 未来扩展不要求共用 UI

用户已确认移动端可以拥有独立表现层。真正需要提前稳定的是：

- 统一房间坐标和动作语义。
- 版本化、语言无关的网络协议。
- 团子外观与动画资产格式。
- 客户端本地隐私边界。
- 服务端权威状态规则。

因此 Windows 客户端可以优先采用原生能力，而不必为了未来移动端牺牲首版桌面体验。

## 4. 评价标准

候选方案按以下顺序评价：

1. Windows 桌面与 Win32 互操作能力。
2. 长期驻留、透明渲染和选择性输入的可控性。
3. 二维动画与场景实现成本。
4. 资源占用的优化空间；最终结论必须实测。
5. 打包、安装、调试和长期维护成本。
6. 许可证和运行时费用。
7. 未来移动端协议、资产或代码复用能力。

## 5. 候选方案

### 5.1 C# / .NET 10 / WPF + Win32 互操作

#### 官方能力

- WPF 是 .NET 的 Windows 专用 UI 框架，提供分辨率无关布局、二维图形、动画和硬件图形能力。
- `Window.AllowsTransparency` 支持透明、非矩形窗口，但要求无系统窗口边框。
- C# 可以通过 P/Invoke 直接使用 Win32 窗口、DPI、显示器和 Shell API。
- .NET 10 是 LTS，可进行 self-contained 发布，用户无需预装共享运行时。

#### 优点

- 对 Windows 生命周期、窗口句柄、消息循环、DPI 和显示器控制最直接。
- 控制面板、托盘、设置和错误提示容易使用标准桌面 UI 实现。
- C# 同样适合后续 ASP.NET Core 服务端，团队认知成本集中。
- 不强迫未来移动端使用 WPF；可只共享协议、场景模型和部分领域代码。
- WPF 和 .NET 本身没有应用运行时授权费。

#### 风险

- WPF 不是游戏引擎，复杂二维骨骼、粒子和编辑工具需要额外实现或引入渲染库。
- 透明窗口的真实性能、合成路径和输入行为必须实测，不能从普通 WPF UI 推断。
- 真正挂入桌面层仍需 Win32/Shell 实验。
- WPF UI 无法直接复用于移动端。

#### 适配判断

最符合“Windows 桌面优先、未来客户端可分别实现”的方向。适合作为 Phase 1 首选原型方案，但渲染后端暂不提前锁定。

### 5.2 Godot 4

#### 官方能力

- Godot 是 MIT 许可的开源游戏引擎，具有成熟二维场景、动画、物理、资源和跨平台导出能力。
- Godot `Window` 官方支持透明背景、无焦点和鼠标穿透标志，Windows 在支持列表中。
- 官方编辑器最低操作系统包含 Windows 10，能够导出 Windows 应用并支持代码签名配置。

#### 优点

- 二维角色、状态机、碰撞、动画预览和移动端导出能力强。
- 团子、悬浮岛和多人场景的视觉原型速度快。
- MIT 许可，无引擎运行时费用。
- 未来 Android/iOS 表现层的复用可能性高。

#### 风险

- 真正桌面 Shell 集成仍需原生扩展、C# P/Invoke 或 GDExtension。
- 引擎窗口、渲染循环与 Windows 桌面宿主之间的生命周期耦合需要验证。
- 作为长期驻留工具，其空闲资源、Explorer 重启和多显示器行为必须实测。
- 如果另外再使用原生设置程序，可能形成双 UI/双生命周期复杂度。

#### 适配判断

是最强备选，也是验证二维表现的好工具；但在桌面宿主风险未证明前，不应仅凭跨平台和动画便利直接定为正式 Windows 客户端。

### 5.3 Avalonia + Win32 互操作

#### 官方能力

- Avalonia 是开源跨平台 .NET UI 框架，使用自己的渲染引擎，当前文档覆盖 Windows 10/11、macOS、Linux、iOS、Android 和 WebAssembly。
- 默认使用 Skia 渲染，C#/XAML 开发方式与 WPF 接近。

#### 优点

- 保留 .NET 与 XAML 体验，同时具有跨平台 UI 潜力。
- 渲染管线较统一，未来桌面平台扩展优于 WPF。
- 可以通过 Win32 互操作实现 Windows 专属宿主。

#### 风险

- Dango 最关键的桌面层行为仍然是 Windows 专属代码，跨平台 UI 优势有限。
- 相比 WPF，特殊窗口与 Shell 集成的项目经验和现成资料更少。
- 它是应用 UI 框架，不自动解决复杂角色动画和房间场景。

#### 适配判断

如果未来要求同一套桌面 UI 同时覆盖 Windows、macOS 和 Linux，价值会上升；在当前 Windows 优先前提下，没有明显超过 WPF。

### 5.4 Tauri 2

#### 官方能力

- Tauri 使用系统 WebView 渲染前端，Rust 负责本地能力。
- 配置支持无边框、透明、任务栏隐藏等窗口选项，并提供平台专属配置。
- 前端可以使用 Canvas、WebGL 或现有 Web 动画生态。

#### 优点

- 相比捆绑完整 Chromium 的方案，安装体积和运行架构通常更精简。
- Web UI 开发和未来管理界面迭代方便。
- Rust 可承载 Windows 原生桥接，跨平台能力较好。

#### 风险

- 桌面场景会同时涉及 WebView、Rust、Win32 和 JavaScript 状态边界。
- 选择性输入、桌面挂载和持续动画仍需原生定制。
- WebView 版本随系统变化，Windows 10/11 的渲染一致性需要测试。
- 对只包含少量二维团子和设置页的首版，技术层数可能过多。

#### 适配判断

适合以 Web UI 为核心的桌面工具，但 Dango 的核心风险在 Windows Shell 和持续二维场景，不是普通管理界面，因此不作为首选。

### 5.5 Electron

#### 官方能力

- Electron 的 `BrowserWindow` 支持透明窗口、隐藏任务栏图标、忽略鼠标事件和原生窗口句柄。
- Electron 继承 Chromium 的多进程架构；每个 `BrowserWindow` 对应独立渲染进程。

#### 优点

- 窗口、透明、Canvas/WebGL、调试和前端生态成熟。
- 跨平台开发人员和资料丰富。
- 能够较快制作可视化原型。

#### 风险

- Chromium/Node 多进程结构对长期驻留小工具而言偏重，具体资源必须实测。
- 真正桌面层集成仍需 Win32 扩展或原生模块。
- 原生模块、Electron 和 Chromium 版本升级形成额外维护成本。
- 用户已明确把低资源和桌面稳定放在跨平台 UI 之前。

#### 适配判断

能力足够，但与项目优先级不匹配；除非后续证明 Web 内容生态成为核心，否则不推荐。

### 5.6 Unity 6

#### 官方能力

- Unity 提供成熟二维/三维编辑、动画、物理和多平台导出。
- 2026 年 Unity Personal 对年收入及融资不超过 20 万美元的主体免费，启动画面可选。

#### 优点

- 美术工作流、动画工具和跨移动端能力成熟。
- 如果产品未来转向复杂游戏化空间，扩展能力强。

#### 风险

- Windows 桌面壁纸和选择性输入不是其标准应用模型，仍需原生插件。
- 对轻量二维常驻宠物而言引擎能力明显超出首版需求。
- 资源、构建体积和后台行为必须严格实测。
- 许可证和产品政策由商业厂商控制，不如 MIT/.NET 方案稳定可控。

#### 适配判断

适合更重的游戏产品，不适合当前“轻量桌面陪伴”首版。

### 5.7 WinUI 3

WinUI 3 是现代 Windows UI 候选，但它并不会免除 Win32 桌面层互操作。Windows App SDK 的 Stable 通道可用于生产，但版本服务周期相对短，需要持续升级。Dango 的设置界面并不要求其现代控件优势，因此暂不优先于成熟的 WPF。

## 6. 比较结论

下表是原型前的工程判断，不是性能实测结果：

| 方案 | Windows 桌面可控性 | 二维场景能力 | 低资源潜力 | 未来移动端 | 综合判断 |
|---|---|---|---|---|---|
| .NET 10 + WPF + Win32 | 高 | 中 | 高，待实测 | 协议/模型共享 | 首选 |
| Godot 4 | 中 | 高 | 中，待实测 | 高 | 第一备选 |
| Avalonia + Win32 | 中高 | 中 | 中高，待实测 | 高 | 当前收益不足 |
| Tauri 2 | 中 | 中高 | 中高，待实测 | 中高 | 技术层数偏多 |
| Electron | 中 | 中高 | 低，待实测 | 高 | 不符合低资源优先 |
| Unity 6 | 中低 | 高 | 低，待实测 | 高 | 对首版过重 |
| WinUI 3 + Win32 | 高 | 中 | 高，待实测 | 低 | 设置 UI 优势不足 |

## 7. 推荐方案

### 7.1 Phase 1 首选候选

建议采用：

> C# + .NET 10 LTS + WPF + 集中封装的 Win32 互操作。

推荐理由：

1. Dango 当前最难的问题是 Windows 桌面宿主，而不是跨平台 UI。
2. 用户已接受未来移动端使用独立表现层。
3. C# 能直接控制 HWND、DPI、显示器和输入命中测试。
4. WPF 足以承载设置、托盘和首轮二维占位动画。
5. .NET 10 LTS 生命周期适合从 2026 年开始的新项目。
6. 如果后续 WPF 渲染不满足需求，可以保留宿主和领域边界，只替换渲染适配层。

### 7.2 暂不锁定渲染后端

Phase 1 先用 WPF 自带二维能力完成最小精灵动画，不立即引入 SkiaSharp、Direct2D 封装、Godot 嵌入或二维骨骼运行时。

只有出现以下证据时才评估替换：

- 透明场景无法稳定使用硬件合成。
- 空闲 CPU/GPU、内存或功耗无法达到测试目标。
- 团子形变和多人动作明显超出 WPF 动画维护能力。
- 多显示器或 DPI 下渲染适配不可控。

Godot 作为第一备选：如果 WPF/Win32 能完成桌面宿主，但 WPF 渲染不合格，先比较“保留原生宿主并替换渲染层”和“整体转为 Godot + Windows 原生桥接”，不直接创建长期混合架构。

## 8. 为未来发展预留的边界

建议从第一天起保持以下模块边界，名称仅为概念，不是已经创建的目录：

```text
Desktop.App          Windows 生命周期、托盘、设置和更新入口
Desktop.Host.Windows HWND、桌面层、DPI、显示器和输入命中
Scene.Core           团子、家具、统一坐标和动作状态，不引用 WPF 类型
Rendering.Adapter    把 Scene.Core 投影到当前渲染技术
Protocol.Contracts   版本化快照和事件定义，不依赖 C# 私有序列化
```

扩展约束：

- 房间坐标、动作名称和持续时间不能直接绑定屏幕像素。
- 网络协议使用明确 Schema 和版本号，不直接序列化服务端内部对象。
- 团子资源采用可验证的资产包和清单，避免把 WPF 控件树当成资产格式。
- Windows 专属 Win32 代码只存在于宿主边界。
- 移动端以后可以实现自己的渲染和输入层，继续消费相同房间快照与动作事件。
- 首版不为了假想移动端抽象全部功能；只有明确跨端语义进入共享模块。

## 9. Phase 1 必须进行的技术 Spike

### 9.1 桌面宿主

比较至少两种承载模式：

1. 透明、无边框、选择性命中的桌面覆盖层。
2. 尝试挂入桌面背景层的 WorkerW/Shell 方案。

重点记录：

- 桌面图标、右键菜单和框选是否正常。
- 团子和家具区域能否交互，其余区域能否穿透。
- Explorer 重启后能否恢复。
- 任务栏、显示器热插拔、DPI 改变和全屏应用行为。
- Windows 10 22H2 与 Windows 11 的差异。

### 9.2 渲染

- 使用占位团子精灵和一个悬浮岛。
- 实现待机、移动、爬岛、拖动和落地。
- 分别记录活跃与空闲状态的 CPU、GPU、内存和帧率。
- 验证降低空闲刷新率是否不破坏生命感。
- 不用最终美术掩盖窗口、命中或渲染问题。

### 9.3 打包

- x64 self-contained 构建。
- 直接运行和安装包两种启动路径。
- 无管理员权限的正常安装和运行。
- 崩溃或强制结束后的再次启动。
- 代码签名先记录流程，不在原型阶段购买证书。

## 10. 推荐决策方式

本调研通过用户批准后，新增 ADR：

```text
docs/adr/0001-windows-client-foundation.md
```

ADR 建议只批准：

- Phase 1 使用 C#、.NET 10、WPF 和 Win32 互操作进行原型。
- Godot 作为失败后的第一备选。
- 渲染后端和最终桌面承载方式必须以 Spike 结果决定。
- Windows 10 22H2 属于项目实测兼容目标，不属于供应商支持承诺。

这不是对整个项目永久锁栈；如果 Spike 失败，必须记录证据并重新决策。

## 11. 官方资料

访问日期均为 2026-08-31：

- [.NET 和 .NET Core 支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [.NET 10 支持的操作系统](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- [Windows 10 Home 和 Pro 生命周期](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-home-and-pro)
- [WPF 概述](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/?view=netdesktop-10.0)
- [WPF Window.AllowsTransparency](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency?view=windowsdesktop-10.0)
- [Win32 扩展窗口样式](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
- [Win32 SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)
- [Windows App SDK 稳定通道](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/stable-channel)
- [Godot Window](https://docs.godotengine.org/en/stable/classes/class_window.html)
- [Godot 系统需求](https://docs.godotengine.org/en/stable/about/system_requirements.html)
- [Godot 许可证](https://godotengine.org/license/)
- [Godot Windows 导出](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_windows.html)
- [Avalonia 概述](https://docs.avaloniaui.net/docs/overview/what-is-avalonia)
- [Tauri WindowConfig](https://v2.tauri.app/reference/config/#windowconfig)
- [Electron BrowserWindow](https://www.electronjs.org/docs/latest/api/browser-window)
- [Electron 进程模型](https://www.electronjs.org/docs/latest/tutorial/process-model)
- [Unity 2026 定价与计划变更](https://unity.com/products/pricing-updates)

## 12. 等待批准的问题

1. 是否批准 C# + .NET 10 + WPF + Win32 作为 Phase 1 首选原型栈？
2. 是否接受 Windows 10 22H2 采用“项目实测兼容、供应商已停止支持”的表述？
3. 是否同意渲染后端和最终桌面挂载方式等 Spike 后再形成第二份 ADR？
