# 第一份本地单人预览ZIP准备记录

日期：2026-10-22。分支`codex/local-mode-closeout`；范围见[首发计划](../planning/2026-10-22-first-local-preview-release.md)。用户“下一步”后完成本地包准备，本记录不表示已对外发布或完整发布门禁通过。

## 改动与来源

- Desktop仅新增版本`0.1.0-preview.1`、产品名和AssemblyTitle；不改变用户已通过的界面、焦点、物理与存档实现。
- 新封装入口`eng/package-desktop-preview.ps1`复用正式包完整性合同。它要求干净已提交来源、当前HEAD和EXE版本匹配，拒绝覆盖已有输出；只复制现有清单文件，保留`app/package-manifest.json`及原包字节。外围加入启动脚本、中文说明、发行说明和相同runtime pack许可。
- 选择ZIP以复用已有正式构建，避免首轮新增安装器或自动更新；用户数据与解压目录分离。普通用户说明明确完整解压、退出方式、整体数据备份、手动更新及旧存档兼容局限。
- 静态审查指出Windows PowerShell 5.1可能产生反斜杠ZIP条目，已按归一化路径修正；复审无新增阻断。实际封装只在PowerShell7.6.5执行，不称5.1运行通过。

## 实际构建与封装

执行源码：`d81ebf2c03f61149857ee50d022ebc4d5ba6f825`，工作树干净。

```powershell
.\eng\publish-desktop-entry.ps1 -RunName preview-0.1.0-preview.1-20261022-v1
.\eng\package-desktop-preview.ps1 -RunName preview-0.1.0-preview.1-20261022-v1 -PackagePath .\artifacts\desktop-entry\preview-0.1.0-preview.1-20261022-v1\package -Version 0.1.0-preview.1
```

两条命令退出码均为0。正常发布日志未检出告警或错误，未使用告警例外或外部native覆盖。正式EXE的ProductName为`Dango`、FileVersion为`0.1.0.0`，ProductVersion为`0.1.0-preview.1+d81ebf2c03f61149857ee50d022ebc4d5ba6f825`。

| 项目 | 实际结果 |
|---|---|
| 正式候选 | `artifacts/desktop-entry/preview-0.1.0-preview.1-20261022-v1/package`，`sourceDirty=false`。 |
| 原包与拷贝包 | 481项文件清单、来源／native及设置合同完整性通过；两份包清单SHA256相同：`E4A68E80CC2AD623414125AD7FB7F46A386C3CFC4B304A9ED4780D5809CDC278`。 |
| ZIP | `artifacts/preview-releases/preview-0.1.0-preview.1-20261022-v1/Dango-v0.1.0-preview.1-win-x64.zip`，77,255,574字节，约73.7MiB。 |
| ZIP完整性 | 489个文件逐项尺寸／SHA256核对通过，没有重复或缺失文件；这不是应用运行测试。 |
| ZIP SHA256 | `B696CF8B285B0B42CFA931A6682DE55C54D7B185C4F89B49E6F1B7EDE5864346`，同目录`SHA256SUMS.txt`。 |
| 附带许可 | 原包Box2D许可保留；外层加入.NETCore10.0.11的LICENSE.TXT／THIRD-PARTY-NOTICES.TXT、WindowsDesktop10.0.11的LICENSE，记录各自来源包身份与文件哈希。 |
| 内容核对 | 清单未含settings／accessories／appearance／catalog、验收marker或manual-session等用户／虚构档案文件。当前包未含SignalR.Common DLL；不代表仓库NU1903已解决或完整安全审计通过。 |
| 签名与公开状态 | EXE为`NotSigned`；本地来源清单保持`local-draft`和`publicReleaseApproved=false`。 |

日志：`artifacts/local-mode-closeout/preview-0.1.0-preview.1-publish.log`、`preview-0.1.0-preview.1-package.log`。机器摘要：ZIP同目录`artifact-summary.json`；ZIP内`release-manifest.json`绑定源码、包清单、说明和许可。发行说明原件为[release-notes.md](../releases/0.1.0-preview.1/release-notes.md)，用户说明为[guide.txt](../releases/0.1.0-preview.1/guide.txt)。

## 尚未完成的发布门禁

本轮未新增或运行测试，未启动应用、占屏或注入输入。首次安装环境运行、正常EXE退出／重启／存档、ZIP方式更新与保留数据等本次实包体验未验证，不能用压缩包哈希检查替代。历史通过证据只沿用其实际适用范围。

600秒持续观察、推球／平台和桌宠真实鼠标／穿透缺项、Schema1 UNKNOWN及联机测试链NU1903仍保留。Windows 10／多屏／DPI／全性能矩阵继续沿用既定延期；本地预览范围的对外放行及上述风险处置尚未据本轮打包自动批准。完整工作流第8.5节发布门禁保持。

## Git与环境

本轮只创建本地提交与artifacts产物，未合main、创建标签、推送、上传Release、安装或部署。main仍为`ca9a3ab`，原用户AGENTS未提交修改保持；其SHA256仍为`82FFA6893B84E7F439B58B64F1E28D5C3FA6D3617B58DD088DDC91F76872BFB7`。保护DLL仍为`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。

未创建新的产品窗口或服务端资源；用户先前保留的旧查看窗口PID26224及焦点候选PID27328仍在，不结束或替换。原档案、候选和失败证据未删除。
