# 参与 Dango 开发

感谢关注 Dango。项目目前以 Windows 本地单人预览为范围；较大的功能或行为变更请先讨论设计，再开始实现。

## 开发与验证

1. 阅读[开发环境](docs/development-environment.md)，准备 Windows x64、.NET SDK 和原生构建工具。
2. 从仓库根目录构建：`dotnet build .\src\Dango.Desktop\Dango.Desktop.csproj -c Release`。
3. 按改动范围选择验证。[测试说明](tests/README.md)列出已确认的无窗口定向组；其他测试可能打开产品窗口或使用原生运行时。构建或无窗口测试通过不代表真实桌面与人工体验已验收。
4. 提交变更时说明目的、实际验证结果与未验证项。设计及工作流程可参考[开发工作流](docs/development-workflow-v0.1.md)。

提出问题时，请使用虚构示例并移除真实桌面内容、账号、密码、令牌、个人文件路径和其他敏感信息。不要上传本地存档或日志，除非已确认其中不含敏感信息且确有必要。安全问题按[安全政策](SECURITY.md)处理。

提交贡献即表示你同意将自己有权授权的部分以项目 MIT 许可证贡献。第三方内容保留其原有许可；请确认有权使用并保留来源和许可信息，参阅[第三方声明](THIRD-PARTY-NOTICES.md)及[素材说明](docs/open-source/assets.md)。
