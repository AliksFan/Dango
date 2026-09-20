# 本地固定启动、更新与回退设计

状态：已实现，真实启动检查通过，本批人工验收待确认；脚本与入口回归、设置/单实例测试及真实包切换通过，详见[本批验证记录](../verification/2026-09-20-local-launch-update.md)。
日期：2026-09-20

## 目标与范围

把已经由 `eng/publish-desktop-entry.ps1` 产生的内部桌面候选安装到当前 Windows 用户的固定位置，使桌面快捷方式始终通过受控脚本启动当前版本，并能在用户正常退出程序后显式切换或回退版本。保留每个成功和失败的版本目录，避免覆盖用户文件和设置。

本轮只覆盖本地安装、启动、更新、回退和设置保留。不联网检查更新，不生成 ZIP，不设置开机自启，不增加动作或依赖，也不修复旧物理 DLL 的缺少导出问题。

## 安装布局与版本标识

默认安装根为 `%LOCALAPPDATA%\Programs\Dango`。每个候选复制到不可变目录 `versions/<commit12>-<manifesthash12>`；`commit12` 来自候选构建标识，`manifesthash12` 来自其发布清单。版本目录一经写入且校验通过便不得修改或复用。

安装根包含：

- `versions/`：所有已完整复制并通过校验的候选；不自动清理旧版本。
- 安装根的 `.staging-<guid>`：复制或校验失败时保留的产物，供排查；不得进入 `versions/`。
- `tools/Manage-Dango.ps1`：只使用 Windows 自带 PowerShell 5.1 的管理入口。
- `install-state.json`：原子记录 `current` 和 `previous` 的版本标识。
- `Rollback.lnk`：调用管理脚本的回退入口。

桌面 `Dango.lnk` 隐藏控制台并调用 `Manage-Dango.ps1 -Action Start`。快捷方式不是程序副本，因此激活新版本后无需覆盖它。

## 包契约与安装校验

输入仍由：

```powershell
.\eng\publish-desktop-entry.ps1 -RunName <新的输出名称> -PhysicsModulePath <已验收模块DLL路径>
```

生成。安装器在复制到版本目录后，且在写入状态前，必须校验候选的发布清单、文件清单、许可文件、两份受管 DLL 及其哈希，以及 `settingsContract` 和 `settingsSourceHashes`。校验任何一项失败时，状态和快捷方式都不改变；失败产物保留在安装根的 `.staging-<guid>` 供诊断。

发布器在构建前后冻结五份受控设置模型、保存和入口源文件的 LF 标准化哈希，安装器只接受与该本地可信发布链匹配的 `settingsSourceHashes`，不能只比较 Schema 编号或可复用的契约字符串。这证明候选没有悄悄改变已审查的 Schema 4/既有字段处理；它不读取、上传或重写真实用户的 `%LOCALAPPDATA%\Dango\settings.json`，也不把任意第三方二进制的字段声明当作可信证据。设置留在原位置，安装、更新和回退均不复制、回拷或重写旧设置。

## 激活、启动与并发边界

首次安装没有 `previous`。普通安装先以 `-StageOnly` 完成复制和校验，预检并创建桌面 `Dango.lnk` 与安装根 `Rollback.lnk` 后才将新版本设为 `current`；任一快捷方式冲突时拒绝且不切换状态。`previous` 只在已有 `current` 被成功替换时更新。`-StageOnly` 只复制工具和版本，可创建快捷方式，但不激活版本。

更新先完成复制和校验，再要求正式实例已由用户正常退出，随后才切换 `current`/`previous`。脚本不得自动结束旧程序；已知旧进程也不作为自动终止目标。存在另一版本运行时，`Start` 不得把它误报为当前新版本已启动，必须报告冲突并拒绝启动或激活。

状态文件采用写入同目录临时文件后 rename 的方式更新，以获得常规 I/O 下的原子替换；这不是对断电、文件系统损坏或所有杀进程场景的承诺。状态缺失、损坏或指向未校验版本时，管理脚本停止并给出修复提示，不猜测版本。

## 回退与冲突处理

`Rollback` 仅允许激活 `previous`，且该版本目录仍完整并且 `settingsContract` 与当前设置兼容。回退同样要求用户先正常退出正在运行的正式实例，且不会自动启动程序。成功回退后交换 `current` 和 `previous`，因此可在下一次正常退出后再切回。

首装、已存在但非本安装器所有的快捷方式、或目标路径内有用户文件时，安装器拒绝覆盖并提示显式处理。所有完整旧版本保留在 `versions/`，失败产物保留在安装根 `.staging-<guid>`，避免把回退问题变成数据删除问题。

安装根的管理工具也是可信链的一部分。若已安装工具的哈希不同，安装器停止且绝不覆盖该工具；本批不支持管理工具自更新，须由后续受审查的迁移流程处理。

## 命令接口

实现后的安装入口：

```powershell
.\eng\install-desktop-entry.ps1 -PackagePath <package目录>
.\eng\install-desktop-entry.ps1 -PackagePath <package目录> -StageOnly
```

测试隔离时可显式提供 `-InstallRoot` 与 `-ShortcutPath`；默认值只服务当前用户的固定根和桌面快捷方式。

安装根内的管理入口为：

```powershell
.\tools\Manage-Dango.ps1 -Action Status|Start|Activate|Rollback [-VersionId <版本标识>] [-InstallRoot <安装根>]
```

`Activate` 用于显式切换已经校验的版本，`Rollback` 使用 `previous`。安装、激活和回退均不自动启动；只有 `Start` 启动当前版本。

## 验收与已知边界

实现前先写能在错误复制、错误哈希、错误 DLL、错误设置契约、运行中旧实例和错误状态文件时失败的测试。随后验证首装、更新、`-StageOnly`、回退、快捷方式启动、设置保留、冲突拒绝和状态恢复边界。

这不替代既有桌面人工验收。脚本与入口回归、设置/单实例测试及真实包切换通过，详见[本批验证记录](../verification/2026-09-20-local-launch-update.md)；旧 DLL 缺少导出是已知失败，第二种 DPI 与长期稳定性仍未验证。所有候选均为内部包，不称为全绿发布。
