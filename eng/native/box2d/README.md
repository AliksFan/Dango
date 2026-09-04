# Box2D native 依赖

本目录固定 Dango P1-004 使用的官方 Box2D `v3.1.1`：

- tag：`v3.1.1`
- commit：`8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`
- 许可证：MIT，原文保存在 `LICENSE.box2d.txt`
- 目标：`win-x64`
- Dango bridge ABI：`5`
- 完整来源、归档、许可证和二进制 SHA-256：`dependency.json`

## 交付选择

仓库提交固定的 `dango_box2d.dll`、小型 C17 bridge、构建脚本、许可证和哈希清单，不提交 Box2D 的 233 个上游源文件。普通 `dotnet build` 和 self-contained 运行不联网；只有显式重新构建 native 依赖且本机没有缓存归档时，脚本才从官方 GitHub commit URL 下载源码。

这个选择让干净 checkout 可以直接构建和发布，同时保留从固定 commit 重建二进制的审查路径。若后续需要修改 Box2D 本体或供应链策略，再单独评估 vendored source，不在应用启动时下载 DLL。

## 从固定来源重建

要求 Windows x64、Visual Studio 2022 C++ Build Tools 和其 CMake 组件。从仓库根运行：

```powershell
pwsh -NoProfile -File .\eng\native\box2d\build-box2d.ps1
```

脚本会：

1. 下载固定 commit 的源码归档到已忽略的 `artifacts/native-spike/`；
2. 校验归档和 MIT 许可证 SHA-256；
3. 用 C17、MSVC 静态运行库和 `/Brepro` 构建官方 Box2D 与 Dango bridge；
4. 输出 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll`；
5. 更新不含时间戳的 `dependency.json` 二进制哈希。

也可传入已位于固定 commit 的官方 Git checkout：

```powershell
pwsh -NoProfile -File .\eng\native\box2d\build-box2d.ps1 `
  -SourceRoot D:\path\to\box2d-v3.1.1
```

自定义源码目录必须是 Git checkout 且 HEAD 精确匹配固定 commit。构建脚本不会安装工具、修改系统环境或在运行时联网。

## Bridge 能力

bridge 暴露版本、ABI、`create → body/joint → step → destroy` 生命周期探针，以及 P2D-004 的 opaque soft-body 会话：

- 12/16 个动态外圈节点；
- 运动学核心、相邻弹簧和径向弹簧；
- ABI v5 接收拖动状态和局部点击坐标，以临时 mouse joint 形成局部抓取支点；
- 拖动期间施加有界悬垂力并降低径向弹簧刚度，释放时销毁支点并恢复原弹性；
- 只与外圈节点碰撞的静态接地代理；
- 2/4 个 Box2D 子步；
- 有限数、正向面积、半径、质心和速度/能量上限；
- 销毁时检查 Box2D 分配回到创建前基线。

.NET 不接收 Box2D ID、body、joint 或 world 类型，只持有 bridge opaque handle 并采样局部轮廓。WPF 动态渲染仍未实现。

## 生命周期探针

self-contained 发布后可运行无窗口探针：

```powershell
.\Dango.Desktop.exe --box2d-native-probe
```

退出码 `0` 表示加载、ABI/版本与生命周期探针通过；`20` 表示受控加载失败，`21` 表示 native 生命周期探针失败。
