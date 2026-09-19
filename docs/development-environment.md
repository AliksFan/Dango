# Dango 开发环境

> 适用阶段：Phase 0 及后续 Windows 客户端任务
>
> 最近核验：2026-09-03

## 必要环境

- Windows x64；Phase 1 的主要开发与验证环境为 Windows 11。
- PowerShell 7；本文命令使用 PowerShell 语法。
- Git。
- .NET 10 SDK，并包含 C# WPF 模板。
- x64 的 .NET 10 Windows Desktop Runtime。

普通命令行 WPF 创建、构建和发布不要求安装额外 .NET workload、Visual Studio、C++ 工具链或 IDE。只有后续任务给出明确缺失证据并说明影响后，才单独安装必要组件。

P1-004 起，普通 .NET 构建使用仓库内固定的 `win-x64` Box2D bridge DLL，仍不要求本机 C++ 工具链。只有显式重新构建 native 依赖时才需要 Visual Studio 2022 C++ Build Tools 及其 CMake 组件；固定来源、命令和哈希见 [Box2D native 依赖](../eng/native/box2d/README.md)。应用运行时不得下载编译器或 native 文件。

## SDK 锁定策略

仓库根的 `global.json` 请求 .NET SDK `10.0.300` feature band，使用 `latestPatch`，并拒绝预览 SDK。该策略允许同一 feature band 内的已安装稳定补丁，例如 `10.0.303`，但不会静默回退到 .NET 8、跨到其他 feature band 或选择预览版本。

字段遵循 [.NET `global.json` 官方文档](https://learn.microsoft.com/dotnet/core/tools/global-json) 和其引用的 [`global.json` JSON Schema](https://json.schemastore.org/global.json)。修改锁定策略前必须同时核对当前 Schema、本机 SDK 解析结果和 CI/开发机可用版本。

从仓库根验证实际选择：

```powershell
dotnet --version
dotnet --info
```

预期 `dotnet --version` 返回已安装的稳定 `10.0.300` feature band 补丁。找不到匹配 SDK 时停止，不降低目标框架或删除锁定文件绕过门禁。

## 环境核验

```powershell
where.exe dotnet
dotnet --info
dotnet --list-sdks
dotnet --list-runtimes
dotnet new list wpf
dotnet workload list
git --version
$PSVersionTable
[System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
[System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
Get-CimInstance Win32_OperatingSystem |
  Select-Object Caption, Version, BuildNumber, OSArchitecture
Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' |
  Select-Object ProductName, DisplayVersion, CurrentBuild, UBR, EditionID
```

`where.exe dotnet` 应只解析到预期的 x64 安装。`dotnet --info` 应显示 `win-x64` 且列出仓库锁定选择。`dotnet new list wpf` 至少应包含 C# `wpf` 模板。

Windows 版本必须用 Caption、DisplayVersion 和 Build 交叉核验。注册表 `ProductName` 可能保留旧名称，不能单独作为系统版本证据。例如 Build `26100` 对应 Windows 11 24H2，即使 `ProductName` 显示 Windows 10。

## 2026-09-01 实测基线

- `dotnet`：系统级 x64 安装，唯一 PATH 解析结果；仓库不固化本机安装绝对路径。
- SDK：`10.0.303`、`8.0.424`；仓库锁定解析为 `10.0.303`。
- Runtime：`Microsoft.WindowsDesktop.App 10.0.11`，同时存在对应 .NET 与 ASP.NET Core Runtime。
- WPF 模板：`wpf`、`wpfusercontrollib`、`wpflib`、`wpfcustomcontrollib`。
- workload：未安装；最小 WPF CLI Release 构建不需要额外 workload。
- Git：`2.49.0.windows.1`。
- PowerShell：`7.6.4`，x64。
- Windows：Windows 11 Enterprise 24H2，Build `26100.9168`，x64。

这些值是一次环境证据，不是所有开发机的固定要求。执行阶段任务时仍需重新运行核验命令。

## 2026-09-03 Box2D native 重建能力

- Visual Studio Build Tools：2022 `17.14.10`，已安装 x64 C++ 工具。
- MSVC：`19.44.35213`。
- CMake：Visual Studio 内置 `3.31.6-msvc6`；不依赖 PATH 中另一个 CMake。
- P1-004 使用这些现有工具成功从官方固定 commit 重建 `win-x64` DLL，没有安装或修改系统组件。
- 这些工具只属于 native 维护路径；普通贡献者可直接使用仓库固定二进制完成 .NET 构建、测试和 self-contained 发布。

## 最小 WPF 构建探针

环境预检可在仓库外创建唯一临时目录，验证模板和 Release 构建，然后只删除该目录：

```powershell
$probeRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
  'dango-wpf-probe-' + [guid]::NewGuid().ToString('N')
)
New-Item -ItemType Directory -Path $probeRoot | Out-Null
try {
  dotnet new wpf --framework net10.0 --output $probeRoot
  dotnet build (Join-Path $probeRoot '*.csproj') -c Release
}
finally {
  if (Test-Path -LiteralPath $probeRoot) {
    Remove-Item -LiteralPath $probeRoot -Recurse -Force
  }
}
```

必须先确认 `$probeRoot` 是本任务在系统临时目录中新建的唯一目录。探针失败时保留错误证据并定位原因；不得静默安装工具、降低目标框架或把探针代码提交到仓库。

## 环境差异与停止条件

- Windows 10 22H2 x64 是 Dango 的项目实测兼容目标，不是 .NET 10 的供应商支持声明；真实兼容性在 Phase 1 单独验证。
- `dotnet` 来源不唯一、仓库解析到 .NET 8/预览版、WPF 模板缺失、进程或系统不是 x64、或目标 RID 发布失败时，停止当前检查点并报告。
- 安装 SDK、Runtime、IDE、workload 或修改系统级配置不属于自动修复动作，必须先确认缺口和影响。
