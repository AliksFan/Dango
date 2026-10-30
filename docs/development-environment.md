# Dango 开发环境

本指南覆盖 Windows 本地桌面项目的源码构建和定向验证。预览包尚未对外发布。

## 环境要求

- Windows x64，PowerShell 7 和 Git。
- .NET SDK `10.0.300` feature band 的稳定补丁：仓库的 [global.json](../global.json) 使用 `latestPatch`，不选择预览版或其他 feature band。
- CMake 和 Visual Studio 2022 C++ 构建工具，包括 x64 C++ 工具链。CMake 可以使用 PATH 中的版本或 Visual Studio 自带版本。

从仓库根目录检查 SDK 和工具：

```powershell
dotnet --version
dotnet --list-sdks
git --version
cmake --version
```

若 CMake 仅由 Visual Studio 提供而未加入 PATH，最后一条命令可能找不到它；构建脚本也会尝试定位 Visual Studio 自带的 CMake。SDK 未匹配时请安装对应 feature band，不要通过修改锁定文件绕过。

## 构建

```powershell
dotnet build .\src\Dango.Desktop\Dango.Desktop.csproj -c Release
```

普通桌面构建导入 `eng/Dango.BeltNative.targets`，默认调用 `eng/build-belt-native.ps1` 构建产品和房间所需的 native 模块，因此需要上述 C++ 工具链。首次 .NET 包还原可能联网；belt 原生构建不下载 Box2D。旧 Box2D DLL 的显式重建是另一条维护路径，来源、许可和命令见 [Box2D native 依赖](../eng/native/box2d/README.md)。

构建后可运行 `src/Dango.Desktop/bin/Release/net10.0-windows/Dango.Desktop.exe`。有关发布脚本和本地数据位置的历史说明见[原 README 存档](archive/2026-10-30-readme-before-opensource.md)。

## 验证

[测试说明](../tests/README.md)列出了构建两个测试项目后的无窗口定向过滤组。测试项目并非全部无窗口；其他测试可能涉及产品窗口或原生运行时。真实桌面行为和人工观感需要单独验证，不能由构建或定向测试替代。
