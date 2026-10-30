# 依赖与第三方组件范围

本清单区分运行/发行、构建、测试和研究依赖。证据来自项目文件、编辑器 `package-lock.json`、组件自带 manifest/NOTICE，以及本地 Desktop restore/build 输出。传递依赖范围只代表当前解析配置；修改项目版本、target framework、RID 或发布模式后应重新生成并复核。仓库级 MIT 不替代第三方许可证。

## Windows Desktop 实际还原与发行依赖

本地 Desktop 构建使用 `src/Dango.Desktop/obj/project.assets.json`（target `net10.0-windows`）及 `src/Dango.Desktop/bin/Release/net10.0-windows/Dango.Desktop.deps.json`。当前结果显示桌面依赖：

| 包/组件 | 当前作用与范围 | 许可证据/处理 |
| --- | --- | --- |
| `Microsoft.Web.WebView2` 1.0.4258.31 | 桌面项目直接 NuGet 包；包内运行时程序集为 `Microsoft.Web.WebView2.Core`、`.WinForms`、`.Wpf`，均 1.0.4258.31；当前 deps 文件没有列出这些程序集的额外 NuGet 依赖 | NuGet 原包许可与 notice：`src/Dango.Desktop/Accessories/Editing/licenses/LICENSE.txt`、`NOTICE.txt`；包 SHA-256 与 WebView2 Evergreen 预安装/不自动捆绑说明：同目录 `dependency.json`。发行时保留这些材料。当前解析结果以 `project.assets.json` 和实际 deps 文件为准 |
| .NET / WPF / WinForms framework | 项目目标 `net10.0-windows`；依赖 Windows Desktop framework。当前普通构建输出不等于自包含部署目录 | .NET/Windows framework runtime 许可由所选 Microsoft 部署方式及其发行文件提供。若采用 self-contained，保留随 SDK/runtime 生成的 license/third-party notice；本清单不声称已盘点某个 self-contained 发布目录 |
| Box2D 3.1.1 + Dango C bridge | 跟踪的 Windows x64 native 文件 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll`；固定上游源码 commit `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3` | MIT 原文 `eng/native/box2d/LICENSE.box2d.txt`；来源和二进制哈希在 `eng/native/box2d/dependency.json`。`Dango.Desktop.csproj` 复制依赖许可与来源 JSON。独立 belt DLL 是由仓库源码构建，不是此 Box2D 二进制 |
| miniPaint audited subset / jQuery Slim 3.7.1 | `src/Dango.Desktop/Accessories/Editing/Web/` 中的 HTML、CSS、JS 是运行时编辑器资源 | 两者 MIT；原文 `Web/licenses/MIT-minipaint.txt`、`MIT-jquery.txt`。`Web/manifest.json` 固定源 commit 和逐文件 SHA-256；`Web/NOTICE.txt` 记录适配/排除。发行保留全套材料 |

此范围由一次成功的 Desktop restore/build 的 `project.assets.json` 和输出 `.deps.json` 支持，不是对所有配置的永久依赖锁定。Box2D DLL 和 Web editor 静态资源由项目显式复制，未必会作为 NuGet 包条目出现在 `.deps.json`。

## 其他 .NET 包范围

直接包引用以项目文件为准：`src/Dango.Client.Sync` 使用 `Microsoft.AspNetCore.SignalR.Client` 10.0.11；测试项目使用 `Microsoft.NET.Test.Sdk` 18.9.0、`MSTest.TestAdapter` 4.3.3、`MSTest.TestFramework` 4.3.3；`Dango.Server.Tests` 另引用 SignalR Client 10.0.11。它们分别属于同步客户端或测试范围，不应误列为 Desktop 发行包依赖。每个已还原项目在对应 `obj/project.assets.json` 可查看解析版本；本轮 CI 提供的 Desktop 输出不覆盖这些项目全部传递依赖的许可清单。

## 编辑器 Node.js 依赖

唯一跟踪的 Node lock 文件为 `eng/accessory-editor/package-lock.json`（lockfile v3）：

| 包 | 锁定版本 | 作用 | 许可字段/发行边界 |
| --- | --- | --- | --- |
| `jquery` | 3.7.1 | 运行时构建输入；bundle 随编辑器 Web 资源分发 | MIT；原文 `src/Dango.Desktop/Accessories/Editing/Web/licenses/MIT-jquery.txt` |
| `esbuild` / `@esbuild/*` | 0.25.11 | 构建工具及平台可选二进制 | MIT；仅构建期，不是产品运行依赖 |
| `playwright-core` | 1.56.1 | 测试/浏览器自动化 API | Apache-2.0；仅测试期。lockfile/NOTICE 说明未下载浏览器二进制 |

锁文件的 `resolved` registry 地址是仓库内依赖配置。npm 包、浏览器二进制和缓存不因 lockfile 存在而随产品发行；发行集合以 Web manifest 和发布脚本实际复制内容为准。

## 构建、测试与研究工具

- `global.json` 固定 .NET SDK feature band。Windows 桌面构建需要 .NET 10 SDK/Windows Desktop targeting pack；重建 Box2D 需要 `eng/native/box2d/build-box2d.ps1` 说明的 Windows x64、Visual Studio C++ Build Tools 与 CMake；Belt native 构建也需要 CMake 和 VS C++ 工具链。它们是开发工具前置，不是仓库 vendored runtime。
- MSTest 和 Microsoft.NET.Test.Sdk 是测试依赖；开发 `eng/` 下的 review/probe 工具通过项目引用或本地产物引用产品程序集，不因而成为用户发行组件。
- `docs/research/` 的 SDK、框架和工具链接是研究引用，不是应用依赖，也不产生被引用产品或材料的再分发许可。
- 忽略目录中的 `obj`/`bin` 是构建输出，不计入已跟踪第三方源/二进制清单。当前 Desktop deps 文件提供本次配置的软件包证据；若维护者更改目标框架、RID 或 self-contained 配置，应复核产物实际依赖并同步许可材料。
