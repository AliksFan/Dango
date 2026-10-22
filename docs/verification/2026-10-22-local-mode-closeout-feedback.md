# 本地收口差项与设置反馈联系方式

日期：2026-10-22。基线 `main@ca9a3ab`，实施分支 `codex/local-mode-closeout`，生产源码提交 `90e974353d9e2daf28c3c62f7873b720fb93be93`。本轮完成用户授权的差项整理与设置页反馈联系方式，后续文档补记不改变产品源码。

## 完成范围

- [差项清单](../planning/2026-10-22-local-mode-closeout-checklist.md)区分已完成基线、已有证据可沿用项、新内核普通房跨进程组合链与实时体验缺口；保留Schema1保存UNKNOWN、NU1903、竖屏暂缓和后续联网边界。
- 按用户指定，在正式设置页滚动内容末尾显示“反馈 QQ：2085475782”；普通启动使用同一 `ProductHomeView`，构建和发布无需额外开关。仅新增静态文字，未增加事件处理或存储字段。
- 桌面规范v0.1第2～6节：复用现有字体、`HomeMuted`语义颜色、单列布局和细滚动条；联系信息保持可读文字，固定返回、退出及保存失败动作仍在滚动区外。无新增页面差异或主题例外。README增加当前状态与清单入口，原历史记录保留。

## 本轮实际验证

证据目录：`artifacts/local-mode-closeout-20261022/`。

| 检查 | 结果与实际覆盖 |
|---|---|
| 正式Desktop Release构建 | 0警告、0错误；从源码构建两个Belt模块。日志`desktop-build.log`。 |
| 现有设置合同与布局 | 5/5通过、零失败、零跳过：`SettingsRefreshContractTests`两项，以及`SettingsScrollingTests.FailureAndNavigationActionsStayVisibleWhenContentScrolls`的320×360、480×640、940×480三个组件尺寸。仅构造／布局真实WPF控件，不创建窗口；覆盖旧入口清退、固定返回／退出／保存失败操作不随内容滚动或被裁切。日志`settings-layout.log`与`results/settings-layout.trx`。 |
| 测试依赖警告 | 仅测试命令使用`-p:WarningsNotAsErrors=NU1903`；已知SignalR依赖警告实际保留，未改变依赖、审计或项目规则。该定向通过不代表完整Desktop门禁及依赖安全已解决。 |
| 设置兼容合同 | `Get-DangoSettingsSourceContract`核对v4合同的5份来源成功；本轮XAML静态文字不改变该合同绑定文件、设置格式或实例身份。 |
| 正常发布链 | `eng/publish-desktop-entry.ps1 -RunName local-closeout-feedback-20261022-v1`成功，无警告例外、无外部native覆盖；生成win-x64自包含内部候选包，`sourceCommit=90e9743…`、`sourceDirty=false`，settings合同仍为`dango-desktop-settings-v4-belt-default-v4`。日志`publish.log`。 |
| 候选包校验／无窗口冒烟 | `Test-DangoPackage`验证481份清单文件及模块／来源绑定成功；同包`--smoke-test`退出码0并已退出。该路径在host、窗口及托盘创建前返回，只证明基础托管启动与退出。机器摘要`summary.json`。 |
| 文档与差异 | 本轮文档相对文件链接、Git空白差异核对通过；只读复核后，L0直接引用已存在的目标解散拒绝／告警保留证据，避免重复补验。 |

## 当前交付与下一项

候选包：[local-closeout-feedback-20261022-v1/package](../../artifacts/desktop-entry/local-closeout-feedback-20261022-v1/package/)。本轮未启动可见产品窗口、占用全局键鼠、运行数据库或服务；上述冒烟进程已退出，未占用测试端口。构建／测试产物与候选包保留。

本轮清单整理和QQ文字已完成工程交付；设置页真实窗口观感尚未验收，本地模式整体未标验收。下一批按清单先对齐已有证据，再补新内核普通房的独立进程恢复组合链；桌宠进一步跨进程位置恢复仍为范围待定。现有目录额度、配饰v1及未受影响合同不重新立项验收。

main仍为`ca9a3ab`，原用户AGENTS未暂存修改及保护DLL保留。改动仅在本轮独立分支创建本地提交；未合main、推送、更新日常安装或部署。

## 后续人工确认

2026-10-22，重新打开同一正式候选包供用户检查（PID20300，独立数据），用户明确“配饰通过，反馈qq也通过”。据此记录配饰清晰度与设置页反馈QQ人工通过。用户同时指出配饰页和首页最右侧整页滚动条需要调整，另见[布局修正](2026-10-22-home-wardrobe-viewport.md)；此新布局尚待确认，不扩大为整个本地模式验收或合并授权。
