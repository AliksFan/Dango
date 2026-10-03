# Dango 本地桌面安装说明

状态：固定启动、更新与回退流程已获人工验收，自动与真实包验证详见[流程验证记录](verification/2026-09-20-local-launch-update.md)。2026-09-22 已完成已验收性能候选的日常安装更新、启动/模式切换/退出与设置保留检查，见[本地交付记录](verification/2026-09-22-performance-local-delivery.md)。内部候选不是发布版本。

## 日常安装

先用已验收的物理模块生成候选：

```powershell
.\eng\publish-desktop-entry.ps1 -RunName <新的输出名称> -PhysicsModulePath <已验收模块DLL路径>
```

再把候选的 `package` 目录交给安装器：

```powershell
.\eng\install-desktop-entry.ps1 -PackagePath <package目录>
```

默认安装到当前用户的 `%LOCALAPPDATA%\Programs\Dango`，并创建桌面 `Dango.lnk`。安装完成不会自动启动程序；双击桌面快捷方式启动当前版本。

若只想预置候选而不切换当前版本，使用：

```powershell
.\eng\install-desktop-entry.ps1 -PackagePath <package目录> -StageOnly
```

自动验证或人工演练可传入隔离的 `-InstallRoot` 与 `-ShortcutPath`，不要把测试结果写入正式安装根。

## 状态、切换与回退

从安装根运行：

```powershell
.\tools\Manage-Dango.ps1 -Action Status
.\tools\Manage-Dango.ps1 -Action Start
.\tools\Manage-Dango.ps1 -Action Activate -VersionId <版本标识>
.\tools\Manage-Dango.ps1 -Action Rollback
```

`Status` 只显示状态；`Start` 才启动当前版本。`Activate` 切换到已完整校验的指定版本。`Rollback` 切回 `previous`，并在成功后交换当前和上一版本；也可使用安装根中的 `Rollback.lnk`。

更新、切换或回退前，请先从托盘菜单或应用正常退出正在运行的 Dango。脚本不会自动结束旧进程；若检测到另一版本仍在运行，会拒绝把它当作新版本已启动。切换与回退均不会自动启动程序。

## 设置与保留内容

用户设置仍在 `%LOCALAPPDATA%\Dango\settings.json`。安装、更新和回退不会复制、回拷或重写它；只有通过对应版本的受控源契约检查的版本才可激活。2026-10-03 收口候选主文件保持严格 Schema 4，窗口档位独立写 `settings.window-preset.json`（Schema 1），避免旧版本拒绝新增字段。旧版保存主偏好后，新版仍能恢复窗口档位。

sidecar 损坏或版本不支持时只降级档位并保护原文件；其余有效偏好可保存，但界面仍提示档位未保存。显式“备份并恢复保存”先保留原字节。两文件各自原子写入，先 sidecar、后主文件；后者失败时不声称全部保存成功。

安装器为每个完整候选创建不可变版本目录，保留旧版本；失败复制或校验产物保留在安装根的 `.staging-<guid>`，不进入 `versions/`。它会校验发布清单、文件清单、许可、两 DLL、哈希、设置契约，以及五份受控设置模型、保存和入口源文件的 LF 标准化合同哈希。该源合同校验只用于本地可信发布链：不会读取、上传或改写真实用户设置，也不验证任意第三方二进制的字段。任何校验失败都不切换当前版本。

若桌面快捷方式或目标位置已有非安装器创建的用户文件，安装器会拒绝覆盖。请先确认该文件来源和处置方式，再重新执行安装。

## 显式迁移管理工具

旧工具与新源码不同仍会阻断普通更新，不自动覆盖。2026-10-03 收口候选新增一次性显式迁移入口，**日常安装迁移仍需独立批准**：

```powershell
.\eng\migrate-local-install-tools.ps1 -InstallRoot <已经确认的安装根> -Action Migrate
```

迁移核验旧两工具的固定原始指纹、安装所有权、全部已安装包、停止状态与操作锁；验证 staging 工具后写恢复 journal，再切换目录。保留旧工具备份，不修改版本状态、快捷方式或用户设置。应用回退使用兼容的新管理器，不回退管理器。

若迁移进程中断，用同一套受审查源码显式恢复；未完成迁移时启动及更新被拒绝：

```powershell
.\eng\migrate-local-install-tools.ps1 -InstallRoot <同一安装根> -Action Recover
```

备份、工具或安装状态被改动时拒绝自动恢复，保留现场，不删除内容。

## 隔离实包验收

新版管理器支持 `Start -AcceptanceSettingsRoot <虚构目录>`。目录须有 `.dango-local-install-acceptance.json` 标记：`{"application":"Dango","acceptanceFormat":1,"fictional":true}`；必须在日常设置、程序包及其祖先之外，不能使用目录、marker 或设置文件的重解析点。仍启动真实 Product 与普通单实例，不是静态视觉预览。

普通启动、验收启动、不同验收根不能冒充彼此；运行中更新和回退仍拒绝。旧 A 不支持该参数，不隔离冷启动旧 A；`Dango.SettingsRollbackProbe` 加载真实旧程序集的设置读写代码，在虚构目录验证 A/B 往返。真实 A 冷启动留待另行批准日常更新后的人工检查。

`eng/test-real-local-install.ps1` 只在本 checkout 的新 artifacts 目录验证真实包安装、工具迁移及 A→B→A，不启动 EXE。`eng/test-local-desktop-window.ps1` 通过 Windows PowerShell 5.1 验证 B 的专用验收快捷方式、真实窗口、三档当前屏幕物理尺寸、重复启动、运行中拒绝、关闭隐藏/管理器恢复和正常退出。后者失败不杀进程，保存本次 PID 供正常退出；不证明键盘手感、实际托盘菜单点击或第二显示缩放，人工验收独立记录。

## 当前限制

此流程没有联网更新、ZIP、开机自启、自动修复或管理工具自更新。原生发布预检只接受显式的已批准 SHA、x64、ABI 1 和必需导出；失败不创建发布输出，不下载、重编译或替换旧 DLL。旧物理 DLL 缺少导出是已知失败；长期稳定性仍未验证。同一实包在用户实际切换的同屏 150% 和 100% 条件下各完成 7/7 自动检查，用户分别确认两轮候选整体人工验收通过，验收进程已正常退出，Windows 已实测恢复原 150% 缩放；跨屏、热插拔和第二分辨率未验证。流程历史验收不等于后续候选自动通过；自动差异和未验证项仍保留，详见[本轮记录](verification/2026-10-03-windows-local-closeout.md)。日常安装更新尚未执行或获授权。未进入正式房间时桌面宿主保持隐藏，空门廊不是可用房间；本批不接联网样本。
