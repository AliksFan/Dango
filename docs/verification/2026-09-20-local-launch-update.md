# 固定本地启动、更新与回退验证

用户已批准本批设计及实施，包括固定目录和桌面快捷方式配置。基线 `da5d4be`，实施分支 `codex/local-launch-update`。应用源码、设置格式和物理库未改；既有陪伴体验验收继续有效。

## 自动验证

- 两次真实入口缺口修复后，最终 Windows PowerShell 5.1 与 pwsh 均为 **25/25** 通过。证据分别为 `artifacts/local-install-tests/20260920T094908446-5f4f4471/summary.json` 与 `20260920T094909202-04bccbe6/summary.json`。新增真实已安装管理脚本的默认根解析、JSON 输出，以及空模块搜索路径下的 Shell 子进程状态/精确拒绝信息检查。
- Windows PowerShell 5.1 与 pwsh 的安装器测试最初各 22/22 通过。覆盖复制与完整性、设置源合同、A/B/回退、最新虚构设置字节保留、运行中拒绝切换、无匹配实例拒绝启动、缺失版本、状态文件锁冲突、桌面/回退快捷方式冲突。证据在 `artifacts/local-install-tests/20260920T092301191-defe0d57/` 与 `20260920T092301910-d473b06d/`。
- 应用设置、保存与单实例相关测试 21/21 通过，见 `artifacts/local-install-verification/settings-instance/settings-instance.trx`。本批未改 C#，不重跑无关整套物理与动作验收。
- Windows PowerShell 5.1 实际 self-contained 发布成功；最终应用包来自干净提交 `333ec9c`，`sourceDirty=false`，版本标识 `333ec9c2eeee-8b67926bf3bd`。478 个文件、许可和两份 DLL 校验通过，日志见 `publish-final.log`。
- 所有受保护库指纹与原基线一致。旧 DLL 缺少 `dango_box2d_self_bodies_create` 的历史门禁失败没有修复，也没有替换库或删除检查。

## 真实包集成

`artifacts/local-install-verification/real-package-integration-entry-fixed.json` 记录全新隔离安装根中的 A→B→A。A 是本轮较早的内部候选，`sourceDirty=true`；B 为上述干净提交包，两者没有新增陪伴行为差异。

修订后的已安装管理入口在 Windows PowerShell 5.1 下，不传安装根的 `Status` 和 `Rollback` 均实际执行成功。每次切换后的目标 EXE 执行 `--smoke-test`，三次退出码均为 0；真实用户设置文件前后 SHA-256 相同。smoke 只证明 Dispatcher 启动退出，不作为普通场景、物理加载或人工体验验收。

## 发现的问题与修复

- 模块未实现时的首个红灯证据保留在 `20260920T090651922-32e7a438/summary.json`。
- PowerShell 将 `File.Replace` 的空备份参数转换为空字符串，导致首次激活失败；改用 `[NullString]::Value`，保留旧状态与失败证据。一轮 pwsh 出现瞬时替换 I/O 拒绝，随后两种运行时均通过；失败时不删除旧状态，用户可重试。
- 回退快捷方式冲突预检提前，先预置完整版本和快捷方式，最后切换状态，防止安装报错但当前版本已经改变。
- 首次实际桌面启动暴露 Windows PowerShell 5.1 在参数默认表达式中无法取得安装脚本目录。将缺省安装根解析移到脚本体，并将命令输出明确序列化为 JSON；增加真实子进程入口回归。初始诊断日志 `launch-diagnostic.log` 保留，不把此前的脚本单元通过当成快捷方式已通过。
- Shell 快捷方式继承的 PowerShell 模块搜索路径与直接调用不同，Windows PowerShell 5.1 未自动加载 `Get-FileHash` 脚本函数。模块改为显式加载当前主机自带 Utility 与 CimCmdlets 清单，不修改系统配置或环境变量。受限模块路径的子进程回归与真实快捷方式复验分开记录。

## 固定目录与人工验收

正式运行目录采用 `%LOCALAPPDATA%\Programs\Dango`，桌面创建 `Dango.lnk`，安装根提供 `Rollback.lnk`。首次安装的 `previous=null`；下一次真实更新才产生日常目录的上一版。

安装到固定目录前后，用户设置文件字节相同。启动器缺省目录修复只处理本任务刚安装的脚本：先确认其与实施提交的 Git 对象完全一致，再保留原文件备份后替换；应用版本目录与用户设置不动。这不是管理工具自动更新功能。

真实桌面快捷方式重复打开返回退出码 0，产品 PID 不变，见 `shortcut-repeat-final.json`。用户随后明确回复“已退出，可以检查冷启动”；再次确认产品已退出后，从真实桌面快捷方式冷启动成功，辅助程序退出码 0，唯一产品进程位于固定版本目录，窗口响应正常、句柄非零，实际加载的统一物理模块路径与受保护指纹一致，见 `shortcut-cold-final.json`。

冷启动后的用户设置 SHA-256 仍与安装前相同，见 `settings-after-cold-launch.json`。冷启动验证结束时保留一个正式产品实例供用户使用，无残留启动器辅助进程。

2026-09-20，用户在“确认本批验收结果，以及是否将当前提交合入本地 main”的收尾确认后回复“授权”，据此记录本批人工验收通过并执行本地整合。此前配合退出仅用于冷启动验证，不作为当时的人工验收结论；本次不重新要求整套陪伴体验验收。多屏、第二 DPI、长稳定性及旧 DLL 失败继续保留，不宣布整个 Phase 1 验收完成。

## Git 与残留

已按用户授权合入本地 main：

- 合并前 main 为 `da5d4be`，阶段分支 `phase/1-desktop-life-prototype` 为 `e35de63`，任务分支 `codex/local-launch-update` 为 `03052f9`；三处工作区干净。
- 阶段分支先快进至 `03052f9`，main 随后快进纳入阶段分支，无冲突。包含安装实现 `333ec9c`、入口修复 `c35d1e1` 和后续双模式方向备忘 `03052f9`；本次验收与交付状态以文档提交同步收尾。
- 合并后完整文件树与任务来源一致，`eng/`、`src/`、`tests/`、`native/` 与已验证提交 `c35d1e1` 一致；应用源码和原生库相对原 main 未改。沿用上文自动与真实运行证据，本次未重跑测试、重新安装或启停日常程序。

未推送、部署或创建发布标签。原工作树、分支、旧程序输出和全部证据保留。测试安装根、版本目录、失败临时文件与本次启动脚本备份留在本地，不自动清理；未创建服务、端口或数据库。
