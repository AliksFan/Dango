# P2D-002 Box2D 获取、构建与加载 Spike 验证记录

> 状态：已完成
>
> 日期：2026-09-03
>
> 实施分支：`feat/p1-004-box2d-soft-body`

## 1. 范围与结论

本检查点固定官方 Box2D `v3.1.1`，建立最小 C17 Dango bridge、受控 .NET loader、可复现 native 构建、MIT 许可证与哈希记录，以及 `win-x64` self-contained 发布探针。

选择固定构建产物进入仓库，不 vendoring 上游 233 个源文件。普通 .NET 构建和运行不联网；只有维护者显式重建 native 依赖且本机没有缓存归档时，构建脚本才访问固定的官方 GitHub commit URL。

本轮没有实现软体环、物理参数、表现契约、WPF 动态轮廓、眼睛/帽子物理或桌面动画，也没有让 Box2D 修改 Core 状态。

## 2. 固定来源与交付物

| 项目 | 实际结果 |
|---|---|
| 官方版本 | `v3.1.1` |
| 官方 commit | `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3` |
| commit 归档 SHA-256 | `FF94DDF7D75FFA6171502FDC7A7F336594560278978305CFC56C72268BC66721` |
| 许可证 | MIT；仓库 LF 文件 SHA-256 `68A3E676D7E94093B102D5CBA0D4E04AF812040D6F230C3DB67A6664574E43D2` |
| 目标 | Windows AMD64 / `win-x64` |
| bridge ABI | `1`，5 个固定 C 导出 |
| native DLL SHA-256 | `C5CA5491614519F76B73F6EC5EFE9E42613B473ADD1E4F2043E08A91BA522A91` |
| native 依赖 | 仅 `KERNEL32.dll`；MSVC 运行库静态链接 |
| 构建器 | VS Build Tools 17.14.10、MSVC 19.44.35213、CMake 3.31.6-msvc6 |

来源、许可证、导出和路径的机器可读记录位于 `eng/native/box2d/dependency.json`。构建脚本校验固定源码归档、Git checkout commit 和许可证；使用 C17、`/MT` 与 `/Brepro`。

## 3. 先失败验收与受控降级

loader 测试在实现前先因 `Dango.Desktop.Physics.Native` 和 `Box2DNativeRequirements` 不存在而编译失败，证明验收先红。实现后以下失败均不使测试进程崩溃：

| 场景 | 实际结果 |
|---|---|
| DLL 缺失 | `MissingLibrary` |
| x86 PE 进入 x64 loader | `UnsupportedArchitecture` |
| 系统 DLL 缺少 Dango bridge 导出 | `MissingExport` |
| bridge ABI 不符 | `AbiMismatch` |
| Box2D 版本不符 | `VersionMismatch` |
| self-contained 发布目录删除 native 后运行探针 | 受控退出码 `20` |

## 4. native 生命周期与重建证据

- C bridge 每次探针创建一个 Box2D world、两个 dynamic body 和一个 distance joint，执行固定 `1/60 s`、4 substeps 的 step，然后显式销毁 joint、body 和 world。
- 单次探针结束后 `b2GetByteCount()` 相对初始值为 0。
- .NET 测试连续执行 100 次 `load → probe → dispose/unload`，全部通过且每次 outstanding bytes 为 0。
- 同一固定输入从 Git checkout 和经 SHA-256 校验的官方 commit 归档构建，DLL 均得到相同 SHA-256；再次重建保持逐字节一致。
- `dumpbin` 确认 PE machine 为 x64、五个 Dango 导出存在，依赖仅为 `KERNEL32.dll`。

## 5. 构建、发布与测试门禁

| 门禁 | 实际结果 |
|---|---|
| `dotnet format` | 通过 |
| Release build | 通过，0 警告、0 错误 |
| Core tests | 20/20 通过 |
| Desktop tests | 72/72 通过，其中 native loader/lifecycle 7/7 |
| 动画 Python tests | 19/19 通过 |
| AP-005 manifest / PNG | 10 个动作错误数 0；42 个 PNG 错误数 0 |
| `win-x64` self-contained publish | 通过 |
| 发布目录 native 探针 | 退出码 0 |
| 发布目录原有 `--smoke-test` | 退出码 0 |
| 发布物许可证与 manifest | 均存在，DLL 哈希匹配 |
| native 缺失发布副本 | 退出码 20，没有进程崩溃 |
| 退出清理 | 残留 Dango 进程 0 |

## 6. 证据分层与下一步

- **静态门禁**：格式、构建、单元测试、来源、哈希、PE/导出和许可证通过。
- **native 集成门禁**：真实官方 Box2D 的 create/step/destroy、重复加载卸载和 self-contained 独立目录探针通过。
- **实时动画行为**：未验证；探针不渲染，也没有建立软体环。
- **人工体验**：不适用；没有可供观感验收的动态效果。
- **交付状态**：仅本地实施分支；未合并、未推送、未部署、未安装新软件，忽略目录保留 native 源码/构建/发布诊断产物。

P2D-002/B2 达到完成条件。下一步是 P2D-003：先用 fake physics world 定义 Core 快照到表现 cue、轮廓、挂点、诊断和 fallback 的隔离契约，再让 native adapter 实现同一接口；本记录不授权自动进入该检查点。
