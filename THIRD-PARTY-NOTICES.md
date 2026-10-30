# Dango 第三方声明

Dango 自有代码和文档使用根目录的 [MIT 许可证](LICENSE)，版权标识为 Dango contributors。第三方代码、二进制和许可声明继续受各自原许可证约束。项目原创视觉资源及 AI 草稿的来源和适用范围见[素材清单](docs/open-source/assets.md)；MIT 不授予第三方商标、角色或其他未拥有的权利。

## Windows 单人预览版

| 组件 | 版本与来源 | 许可材料 |
| --- | --- | --- |
| Box2D | 3.1.1，固定提交 `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`；通过 Dango bridge DLL 使用 | MIT 原文：[LICENSE.box2d.txt](eng/native/box2d/LICENSE.box2d.txt)；来源与二进制哈希：[dependency.json](eng/native/box2d/dependency.json) |
| miniPaint 子集 | 4.14.3，固定提交 `a79733eb803fc97084ef0ee4faa96b031e69e1c0`，包含 Dango 的适配 | MIT 原文：[MIT-minipaint.txt](src/Dango.Desktop/Accessories/Editing/Web/licenses/MIT-minipaint.txt)；改动和排除项：[NOTICE.txt](src/Dango.Desktop/Accessories/Editing/Web/NOTICE.txt) |
| jQuery Slim | 3.7.1，编辑器打包的运行时资源 | MIT 原文：[MIT-jquery.txt](src/Dango.Desktop/Accessories/Editing/Web/licenses/MIT-jquery.txt) |
| Microsoft.Web.WebView2 SDK | 1.0.4258.31，NuGet 包；包含 WPF 宿主及 Loader | 保留微软的 [LICENSE.txt](src/Dango.Desktop/Accessories/Editing/licenses/LICENSE.txt) 与 [NOTICE.txt](src/Dango.Desktop/Accessories/Editing/licenses/NOTICE.txt)，包括其中列明的其他组件；包来源和校验值：[dependency.json](src/Dango.Desktop/Accessories/Editing/licenses/dependency.json) |
| .NET 与 Windows Desktop runtime | 自包含发行实际使用的版本由发布产物 runtimeconfig 和还原清单确定 | 预览封装脚本从同版本运行时包复制 LICENSE 和 THIRD-PARTY-NOTICES；以每个 ZIP 中 `licenses/` 下的原文为准 |

WebView2 Evergreen Runtime 是单独的系统前置组件，采用微软的相应使用条款；Dango 不将其重新声明为 MIT，也不自动下载、安装或打包它。编辑器源资源清单由 [manifest.json](src/Dango.Desktop/Accessories/Editing/Web/manifest.json) 固定；上游未采用的字体、图像和不确定来源图标不属于该子集。

预览 ZIP 的根目录保留 Dango 的 LICENSE 与本声明，`app/licenses/` 保留 Box2D、WebView2 的原文，`app/AccessoryEditor/` 保留编辑器的 NOTICE 和许可证。`licenses/` 保存自包含 .NET 运行时原文；这些文件应随再分发产物一起保留。

## 构建、测试与未交付的联机实验

- 编辑器构建工具 esbuild 0.25.11 使用 MIT；测试工具 playwright-core 1.56.1 使用 Apache-2.0。版本、平台包与完整性值见 [package-lock.json](eng/accessory-editor/package-lock.json)。工具及其平台二进制不随桌面产品发行。
- .NET 测试直接引用 Microsoft.NET.Test.Sdk 18.9.0、MSTest.TestAdapter 4.3.3 和 MSTest.TestFramework 4.3.3。客户端同步实验直接引用 Microsoft.AspNetCore.SignalR.Client 10.0.11。这些包按各自上游许可证使用，传递依赖以实际还原结果为准，不能由 Dango 的 MIT 替代。
- SDK、CMake、Visual Studio C++ 编译器和 Windows 系统组件由开发者按其供应商条款安装。仓库中的构建说明不授予这些工具的再分发权。

[依赖清单](docs/open-source/dependencies.md)说明源码、构建、测试和运行时范围。新增依赖或改变随包内容时，应核对实际文件及原文，而非仅复制本表中的许可证简称。
