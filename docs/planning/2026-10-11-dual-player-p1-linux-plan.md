# 双人 P1：本机 Docker 临时 Linux 验证

日期：2026-10-11。用户要求“继续吧，需要我帮忙吗”，并选择“本机 Docker 临时容器”。本轮继续已批准 P0–P1 的 Linux 平台门禁，不启动 P2，不接正式服务、账号、数据库、家具或 UI 入口。Windows 隔离证据来自 `0e98cee`；本地 main `a4acdeb` 已吸收。

## 1. 环境和资源边界

已核实本机 Docker CLI 29.7.2，context为desktop-linux；Docker Desktop已安装但引擎未运行，WSL仅Stopped的docker-desktop。按用户选定环境，使用已有Docker Desktop和一个带任务标签、结束即删除的临时linux/amd64容器。不得安装新WSL发行版、创建数据库／对象存储、公开端口、使用host网络或privileged、修改Docker用户配置或处理其他容器。

官方.NET SDK镜像选10.0.300（符合仓库global.json），拉取后固定实际image digest／ID，记录OS、CPU架构、SDK、CMake和GCC版本。容器内临时安装C17编译工具、CMake、Python3、binutils和unzip；无新生产依赖。镜像缓存保留，临时容器可写层不保留。最多2 CPU、3GiB内存、128进程；验证阶段关闭网络。仅挂载本轮筛选源码／固定Box2D输入（只读）及新ignored证据目录（读写），不挂载整盘、用户目录、认证目录或Docker socket。

## 2. 来源合同与文件所有权

Box2D保持v3.1.1、commit `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`；复用已缓存官方zip，先验证`dependency.json`中的archive和许可证SHA，再解压至容器工作区。源码快照只含验证工具、Physics及必需root构建文件；不复制bin／obj／旧artifacts／真实用户数据。

两个独立实现owner从本计划提交开始；根负责资源启动、精确host快照、组合执行与报告：

- profile owner：`eng/tools/Dango.SafeSpawnAdmission/Program.cs`及新增`NativeBinaryProfile.cs`。Windows默认仍硬锁原R1 DLL SHA；Linux只在实际linux-x64进程中消费显式`--linux-build-record <json>`，核对平台、同源SourceHash、固定Box2D来源／许可证、Release、ABI1及本轮module／probe SHA。不得增加任意hash覆盖参数，不修改solver、ABI、参数或生产adapter。
- runner owner：该工具`linux/`子目录下的shell入口与Python外部verifier。核对来源和构建记录、ELF x86-64、实际导出／ABI；执行原suite＋全部17反例＋六组跨进程。新记录为版本1，固定字段`Version,Platform,SourceHash,ModuleHash,NativeProbeHash,Box2DCommit,Box2DArchiveHash,Box2DLicenseHash,BuildConfiguration,AbiVersion,Compiler,Exports`。额外实际Box2D树清单／环境／SDK／构建参数另留证据，不含认证信息。

`SourceHash`沿用工具当前四个来源目录的相对路径、ordinal顺序和bytes；包含新增Linux脚本，排除bin／obj，不能只声明旧Windows来源。Linux产物不是Windows DLL逐字节副本，不拿跨OS轨迹逐位一致充当此门。先集成profile再runner，跨owner接口变更先通知根。

## 3. 执行与失败门

1. 在新ignored目录冻结host输入清单、Git提交、根构建配置及受保护五项产物hash；Linux实际构建`dango_physics_module.so`、独立`dango_safe_spawn_probe`和纯net10 console，不能依赖WPF／桌面。
2. 独立读取ELF／导出和实际ABI，写本轮Release构建记录；错误module／source／平台记录必须拒绝，零world创建，不能把Windows常量改成通配。
3. 执行43项正常suite及17个指定oracle红／真实exit1反例。保留单／双体600步、健康world九项续接、真实fault／last-safe／分配及线程／取消证据；不改变原容差或参数。
4. lost／released各120和1200步前缀，fault／unknown各120步，同一producer退出后另PID消费；只传领域元数据和明确fixture处置。成功恢复仍≤65536bytes／5秒、全组Tick0与首健康帧、当前ready及新epoch／原因；fault／unknown仍零Create。四次新出生在同Linux构建内按原完整bit编码比较，不能把Windows／Linux之间不逐位相同视为失败或成功证明。
5. Python外部验证核对实际PID／exit、模式／mutation、必须check清单和类型、指定oracle红且无exception冒充反例、metadata／receipt来源关联、准确恢复原因、source／module／probe执行前后一致及创建释放／allocation。每个子进程仍60秒，上限失败不得自动扩大。任一正常门失败，保留证据并停止扩展。

输入网络获取／临时包安装与关闭网络的真实验证分开记录。不得以编译、镜像存在、运行时版本或Windows测试代替Linux实际load／step。若网络、引擎或资源不可用，给出具体缺项并保持Linux未验证，不安装替代发行版或换参数。

## 4. 收口和后续

报告区分静态、真实Linuxheadless、Windows受影响profile回归、真实呈现／人工与Git／资源。仅对修改影响的Windows哈希/profile拒绝路径和必要能力重验，不无理由重跑旧完整native矩阵。全部实际child退出、native分配归零；只删除本轮临时容器，不删除cached镜像、旧证据或工作树，报告Docker引擎是否仍运行。

本轮不打开前台窗口。P1真实语义呈现可在下一增量用原R1语义记录驱动两个独立呈现管线和现有WPF团子控件，按桌面规范v0.1 §§2–6；隔离重放窗不证明网络、多客户端或账号授权。Linux通过也不能单独准入P1或启动P2，正式接线和同机／双机仍按原阶段复核。
