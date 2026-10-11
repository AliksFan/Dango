# 双人 P1：Linux 门禁准备与首次环境启动

日期：2026-10-11。用户选择本机 Docker 临时容器，按[冻结计划](../planning/2026-10-11-dual-player-p1-linux-plan.md)实施。**验证工具及受影响 Windows 回归完成；首次 Docker 引擎启动失败，Linux 实际门禁未验证。P1 整体不准入，P2 停止。**

## 1. 本地实现与审查

计划在编码前提交为 `62cad85`。两个实现 owner 使用独立工作树；复杂实现及审查遵守 `gpt-6.1-sol`／`high` 规则。主实施分支为 `codex/ordinary-room-physics-admission`，最新本地 main `a4acdeb` 已是祖先，没有将本轮内容合回 main。

- `e12c0a4` 集成严格 native profile（来源 `b34622c`）：Windows 仍硬锁原 R1 DLL SHA，拒绝任何 Linux record；Linux 仅在实际 linux-x64 进程中校验固定来源、完整 record、ELF、导出和真实 loader。新增 `profilecheck`，错误 profile 指定 oracle 红灯且零 Create；没有任意期望 hash 覆盖入口。
- `cee16fe` 集成独立 Bash／Python runner（来源 `8f0d1f6`）：固定 43 项正常清单、17 个错误变体、12 个错误 profile、六组 producer／consumer、四组同 Linux 构建首帧 digest、原 64KiB／5秒／每子进程60秒门。实际 PID、exit、指定红 oracle、异常冒充、类型、来源及资源回归均有独立检查；这些是已实现的验收能力，尚不是 Linux 执行结果。
- `7917d53` 增加 host wrapper：固定 desktop-linux context 和 SDK image，输入与输出为同级目录，仅输出可写；创建尝试前记录状态，收尾成功查询唯一 name、校验真实 Id／name／任务 label 后才停止，查询失败不宣称删除成功。
- `122a30d` 集成来源链修正（来源 `d2bed1d`）：构建后及运行后复核实际解压 Box2D 全树、许可证和完整文件清单，最终汇总要求两份一致回执。没有修改 solver、ABI、物理参数、生产加载或正式服务。

独立审查先确认三个 host 缺口：只读输入经输出挂载别名可写、创建响应不确定时可能漏收尾、任意 inspect 错误被误报删除。核实后收紧；实际 Box2D 树的收尾复核也按审查补齐。修后只读审查无剩余明确阻断。容器创建／清理的实际行为仍未执行，不能用源码审查替代。

## 2. 实际静态与 Windows 验证

根集成 Release console 构建零警告／错误；`NativeBinaryProfileTests` **21／21**、零跳过。证据为 `artifacts/p1-linux/windows-profile-tests/` 的 TRX。

根实际执行原 R1 DLL 的 `profilecheck` exit0，错误 Windows DLL 和 Windows 携带 Linux record 各 exit1，指定 profile oracle 红灯、零 Create、无 exception 冒充。正常报告 verify exit0，伪造 `NativePlatform=linux-x64` 的报告 verify exit1。实际退出与 PID 保存在 `artifacts/p1-linux/windows-profile/results.json`，各报告与日志保留。

独立 Python helper **13／13** 通过，包括逐项缺失正常清单、错误类型／身份／exit／异常假红、来源记录篡改，以及实际 Box2D 单源文件改 bytes 必须失败。PowerShell 解析零错误，Bash `-n` 通过、入口为 LF，四个来源目录无 reparse point，`git diff --check` 通过。静态摘要为 `artifacts/p1-linux/static-before-run.json`。原 Windows 43 项 native suite 和六组恢复未无理由重跑，既有结果见[Windows 报告](2026-10-11-dual-player-p1-safe-spawn.md)。

## 3. 首次 Docker 启动的实际结果

执行来源冻结于 `122a30d`，新证据目录为 `artifacts/p1-linux/docker-r1/`，未覆盖旧实验。host 已筛选并留存输入文件 SHA 清单，固定 Box2D zip SHA 核对通过。

host 启动既有 Docker Desktop，等待180秒后 Linux 引擎仍不可用，wrapper 实际 **exit1**。只读诊断显示 `desktop-linux` 的 Docker API named pipe 不存在，WSL 只有 `docker-desktop`，状态仍为 Stopped。`host-manifest.json` 明确记录 `ContainerCreationAttempted=false`、`Pass=false` 和启动失败；`docker-version-after.log` 保留实际错误。

本次没有执行镜像 pull、包安装、容器创建、Linux 编译、load／step、profile 反例或跨进程验证。因此没有 image digest、Linux build record 或成功 run manifest；Linux 门禁保持**未验证**。没有用 Windows 结果或编译脚本存在来补绿。

已请求用户打开 Docker Desktop，反馈许可、WSL 或启动错误提示。用户处理环境后可以用新证据目录重试同一门禁；不安装替代 WSL 发行版、不修改 Docker 设置或物理参数，也不扩大超时预算。

## 4. 资源、遗留与交付

A／authority／日常／B replay／R1 五项受保护产物在启动前后均匹配原合同，证据为 `artifacts/p1-linux/protected-before.json` 和 `protected-after-attempt1.json`。本轮没有替换日常 DLL，没有打开验收窗口、监听测试端口或操作数据库／对象存储。

本次代码路径没有创建任务容器，故没有本轮容器待清理；Docker 引擎不可查询，不宣称全机容器清单为空。Docker Desktop 进程仍保留、Linux 引擎未运行，未停止其他资源。ignored 输入快照、日志、测试产物和 owner 工作树保留。

Linux 实际构建／运行、真实语义呈现、正式账号／Hub／持久生命周期、多客户端与人工双机均未验证。下一窄呈现增量已只读核对可复用原 R1 冻结轨迹和两个独立 WPF 管线，本轮未实施或启动。隔离窗口也不能替代正式多人验收。

交付限本地任务分支源码、计划和本报告；未合回 main、推送、安装、部署或标记阶段已验收。
