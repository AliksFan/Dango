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

用户设置仍在 `%LOCALAPPDATA%\Dango\settings.json`。安装、更新和回退不会复制、回拷或重写它；只有通过 Schema 4 与已使用字段兼容检查的版本才可激活。

安装器为每个完整候选创建不可变版本目录，保留旧版本；失败复制或校验产物保留在安装根的 `.staging-<guid>`，不进入 `versions/`。它会校验发布清单、文件清单、许可、两 DLL、哈希、设置契约，以及五份受控设置模型、保存和入口源文件的 LF 标准化合同哈希。该源合同校验只用于本地可信发布链：不会读取、上传或改写真实用户设置，也不验证任意第三方二进制的字段。任何校验失败都不切换当前版本。

若桌面快捷方式或目标位置已有非安装器创建的用户文件，安装器会拒绝覆盖。请先确认该文件来源和处置方式，再重新执行安装。

## 当前限制

此流程没有联网更新、ZIP、开机自启、自动修复或管理工具自更新。已安装管理工具的哈希不同会使更新停止，绝不会覆盖工具；工具更新须走后续受审查的迁移流程。旧物理 DLL 缺少导出是已知失败；第二种 DPI 和长期稳定性仍未验证。流程验收不等于每个后续候选自动通过；每次更新仍须核对包完整性、设置契约与日常入口。
