# 旧色板成果整理

日期：2026-10-05。状态：用户要求开始整理旧色板；本轮仅迁入独立颜色与本地保存模块，首页、房间宿主和物理运行时尚未接线。新捏脸界面仅设计，不把整理视为完整外观或阶段验收。

## 1. 来源、基线与边界

从重新核实的 `main@e2f4037` 建立独立 `codex/appearance-face-design` 工作树。旧 `codex/basic-appearance-palette@89bacb5` 的未提交成果作为输入，逐文件复制，不移动、提交、覆盖或清理旧树。新树与正在进行的普通房／视角设计保持独立。

只迁入以下七个文件；既有 SDK 项目默认收集文件，不改解决方案或项目引用，也不迁入旧控制中心、AppearanceSample、Host、Runtime、native 或发布脚本。

| 文件 | 本轮职责 |
|---|---|
| [OpaqueSrgbColor](../../src/Dango.Desktop/Rendering/Dango/OpaqueSrgbColor.cs) | 不透明 8-bit sRGB 与严格大写 Hex |
| [DangoBodyColorPolicy](../../src/Dango.Desktop/Rendering/Dango/DangoBodyColorPolicy.cs) | 主色及本地辅助槽位的确定性颜色 |
| [AppearanceSettingsStore](../../src/Dango.Desktop/AppShell/ControlCenter/AppearanceSettingsStore.cs) | 独立颜色 Schema 2 与严格 Schema 1 兼容 |
| [AppearanceSettingsSession](../../src/Dango.Desktop/AppShell/ControlCenter/AppearanceSettingsSession.cs) | 预览／已保存、取消、显式保存及备份恢复 |
| [颜色测试](../../tests/Dango.Desktop.Tests/Rendering/DangoBodyColorPolicyTests.cs) | 精确 RGB、非法 Hex、默认色及槽位平局 |
| [存储测试](../../tests/Dango.Desktop.Tests/AppShell/AppearanceSettingsStoreTests.cs) | 严格 JSON、旧版本不自动重写、失败保护 |
| [会话测试](../../tests/Dango.Desktop.Tests/AppShell/AppearanceSettingsSessionTests.cs) | 双颜色草稿、显式迁移、失败重试及备份 |

源文件字节、旧树状态与差异指纹在隔离验证产物中记录，完成时复核。仓库文档不记录本机绝对来源路径；本地清单不作为产品配置。

## 2. 沿用的颜色决定

沿用旧调色盘修订：身体和眼睛独立选择完整 RGB；黑色、白色、暗色、灰色、身体与眼睛同色均合法。不得恢复明度钳制、对比度拒绝或自动反色。持久文本只接受大写 `#RRGGBB`；Alpha 固定为 255，不扩展纹理、渐变、吸管或上传。

默认身体 `#FFE8C2`、眼睛 `#2B201E`；身体快捷色为 `#FFE8C2`、`#C9E2FF`、`#DDCCF8`。当前首页展示样色属于旧界面内存预览，不能据此覆盖已认可的色板契约；后续接线须统一默认值与快捷色，届时重新验证画面。

颜色保存在独立外观文件，不修改主设置 Schema 4。Schema 1 只读加载原身体 RGB，眼睛采用默认值，启动不自动升级；只有显式保存才写 Schema 2。损坏、重复字段、未知字段、缺字段、非对象根、非法色和未来版本使用安全默认并保护原文件。

普通保存通过 Session，不能绕过写保护直接调用低层 Store。恢复保护需显式选择备份后保存；备份失败不解锁，备份成功后写失败保留旧文件和可重试状态。`HasPendingSave` 包含显式迁移及恢复后的待写状态，不能只按 `IsDirty` 决定是否能保存。

## 3. 验证与后续接线

本轮在最新基线实际构建并运行三类聚焦测试；旧小样的 72/72 只作为历史，不继承为本轮结果。补充非对象 JSON 根和重复字段负例。还需记录能够抓住错误实现的证据，并复核旧源树保持原状。

本轮未创建运行中的颜色会话、打开调色盘或修改日常外观文件。类可供后续接线使用，不代表用户已能在首页保存。保存重启、Window／Wallpaper／overlay 一致、眨眼与睡眠组合、零物理步、隐藏恢复及人工观感仍未验证。

首页、`DangoApplicationHost`、`StackedDangoSceneRuntime`、帧绑定和房间宿主由同一集成负责人在普通房接口稳定后接线。该增量需自己的可失败测试与真实 Windows 验收；不整树合并旧外观分支，也不以换色触发 Tick、重建实体或争抢帧 owner。

捏脸另见[团子自身捏脸设计](2026-10-05-dango-face-design.md)。帽子另有[独立选择设计](2026-10-05-hat-selection-design.md)，不加入捏脸配置或捏脸示意。
