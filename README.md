# Dango

Dango 是一款 Windows 桌面团子项目。目前提供本地单人预览：创建和选择最多三个团子栏位，一次最多让一只团子出场；可以捏脸、编辑配饰，并在本地房间中布置木平台和皮球。

![真实产品场景离屏渲染示例：团子近景](docs/open-source/media/room-near.png)

上图由真实产品场景离屏生成，使用虚构团子与家具；完整房间图、来源及复现方法见[演示素材](docs/open-source/demo.md)。

## 当前能力

- 三个团子栏位，一次最多一只团子出场。
- 捏脸、换装及配饰图像／图层和骨架编辑。
- 本地单人房间、木平台和皮球，以及桌宠模式和本地保存。

项目仍在开发中。联机与云同步尚未交付；Windows 单人预览版见 [v0.1.0-preview.2](https://github.com/AliksFan/Dango/releases/tag/v0.1.0-preview.2)。有关历史阶段及验证边界，请参阅[原 README 存档](docs/archive/2026-10-30-readme-before-opensource.md)和[验证记录](docs/verification/README.md)。

## 使用前提

需要 Windows x64。配饰图像编辑需要系统预装 WebView2 Evergreen Runtime；Dango 不自动安装它。预览版的存档兼容性和其他限制见[发行说明](docs/releases/0.1.0-preview.2/release-notes.md)。

## 后续方向

以下方向不承诺日期，范围可能调整：

- 完善本地单人体验与兼容性验证。
- 继续丰富团子动作与互动。
- 探索多人房间与联机互动。
- 探索账号与云同步。

## 从源码构建

需要 Windows x64、仓库锁定的 .NET SDK、CMake 和 Visual Studio 2022 C++ 工具。安装和检查方法见[开发环境](docs/development-environment.md)。从仓库根目录运行：

```powershell
dotnet build .\src\Dango.Desktop\Dango.Desktop.csproj -c Release
```

构建后可运行 `src/Dango.Desktop/bin/Release/net10.0-windows/Dango.Desktop.exe`。首次还原 .NET 包可能需要联网。运行和验证涉及真实桌面时，请先阅读[贡献指南](CONTRIBUTING.md)。

## 参与和许可

欢迎阅读[贡献指南](CONTRIBUTING.md)，了解环境、验证和提交问题的方式。安全问题请参阅[安全政策](SECURITY.md)。

项目代码采用 [MIT 许可证](LICENSE)；第三方组件和素材的许可及使用边界见[第三方声明](THIRD-PARTY-NOTICES.md)与[素材说明](docs/open-source/assets.md)。开源准备状态见[准备清单](docs/open-source/preparation.md)。
