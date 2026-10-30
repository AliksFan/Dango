# Dango 开源准备

2026-10-30，作者批准 GitHub、MIT、Windows 单人预览版路线，并在确认沿用原仓库的方案后，明确授权更新远端 main、公开仓库及发布预览。原仓库现已公开，首个公开版本为 v0.1.0-preview.2。

## 公开范围

- Dango 自有源码、文档及项目原创素材采用根目录 [MIT](../../LICENSE)，第三方组件保留原许可证。
- 作者已确认 Direction A、动作图和 AI 方向草稿的素材与参考输入具有使用、再分发权利；具体来源和 AI 生成物范围见[素材清单](assets.md)。
- 首发定位为 Windows x64 本地单人预览。联机、云同步和完整产品首版仍按各自实际状态说明。
- 沿用原仓库 AliksFan/Dango，保留其既有远端分支和历史。以经过筛选的源码树追加正常提交，不把本地未推送的研究历史引入公开 main；源码下载 ZIP 本身不含 Git 历史。

## 公开结果

- 原仓库：[AliksFan/Dango](https://github.com/AliksFan/Dango)，MIT；原有远端分支和历史保留。
- 预览版：[v0.1.0-preview.2](https://github.com/AliksFan/Dango/releases/tag/v0.1.0-preview.2)，含 Windows ZIP、源码 ZIP、SHA256、源码逐文件清单和构建信息。
- 发布提交：`2ffc082eb7c32c910fdf9f4aa2cc017a7ec01352`；[远端 CI](https://github.com/AliksFan/Dango/actions/runs/41000304860) 已通过。
- GitHub Private vulnerability reporting 已启用；仓库、Release 和全部 5 个附件均已通过匿名访问检查。

## 已落实的仓库入口

| 范围 | 入口 |
| --- | --- |
| 项目及使用介绍 | [README](../../README.md)、[开发环境](../development-environment.md)、[演示素材](demo.md) |
| 授权与来源 | [LICENSE](../../LICENSE)、[第三方声明](../../THIRD-PARTY-NOTICES.md)、[依赖清单](dependencies.md)、[素材清单](assets.md) |
| 社区入口 | [贡献指南](../../CONTRIBUTING.md)、[安全政策](../../SECURITY.md)、Issue 与 PR 模板 |
| Windows 自动验证 | [CI](../../.github/workflows/windows-ci.yml)：桌面构建、原生模块合同、Core/Protocol 白名单、编辑器可复现构建、许可与快照合同、预览 ZIP |
| 发行封装 | [package-desktop-preview.ps1](../../eng/package-desktop-preview.ps1)：保留应用文件哈希，外层 ZIP 加入 Dango MIT 与第三方声明 |
| 公开源码导出 | [export-public-source.ps1](../../eng/export-public-source.ps1)：已提交的干净来源、唯一输出、逐文件清单、SHA256、不含历史 |

## 内容及历史检查

原始基线 `9fbb828` 的全部可达对象已进行本地文本模式检查：369 个提交、3492 个 blob；二进制、图像和归档内容需要另外按来源清单核对。检查器[源码](../../eng/audit-public-source.py)只记录规则、相对路径、行号和对象 ID，不输出匹配值，也不授予安全或版权放行。原始报告留在忽略的本地产物目录，不随源码发布。

文本匹配中的凭据 URL 是连接测试中的拒绝输入样例；提交包含作者联系元数据，产品及历史说明包含既有反馈联系方式。工作流中的本机 Worktree 示例已改为相对目录；旧提交保持原样。检查未发现所配置的私钥、GitHub/OpenAI token、AWS key 签名，仍不能据此保证不存在其他形式的秘密。

公开候选作以下处理：

1. 排除所有 `artifacts/`，包括原始基线已经跟踪的 70 个诊断文件，其中 42 个日志；不删除内部原件。
2. 排除 `docs/planning/assets/` 下 4 张没有独立再分发许可记录的外部 UI 参考图。仅在导出副本中把相关图片或链接改为说明，保留文字设计记录及原文档校验值。
3. 仅导出提交中的文件，不带 `.git`、未跟踪文件、忽略的本地配置或原始提交的作者邮箱。该规则只描述下载 ZIP，原仓库公开后其既有 Git 作者署名仍然可见；第三方版权及署名保留。
4. 发行候选不带用户存档。产品现有反馈联系方式仍沿用原产品设置，按原产品公开反馈用途保留；私密漏洞通道按安全政策配置。

原仓库全部 114 个远端分支、835 个提交另行进行了文本模式审查和候选复核；4 张参考图未进入这些远端历史。公开沿用原仓库，不要求重写或强推已有历史。导出清单列出实际排除项、文档转换、每个文件的字节数与 SHA256，可核对源码快照和内部来源。

## 本地复核

从仓库根目录运行：

```powershell
python -m unittest discover -s eng -p test_public_source_audit.py
./eng/test-preview-distribution.ps1
./eng/test-public-source-export.ps1
dotnet build src/Dango.Desktop/Dango.Desktop.csproj -c Release
dotnet test tests/Dango.Core.Tests/Dango.Core.Tests.csproj -c Release
dotnet test tests/Dango.Protocol.Tests/Dango.Protocol.Tests.csproj -c Release
```

仅在来源已经提交且工作区干净时生成新候选，输出名称每次唯一：

```powershell
./eng/publish-desktop-entry.ps1 -RunName <new-preview-run>
./eng/package-desktop-preview.ps1 -RunName <new-preview-run> -PackagePath artifacts/desktop-entry/<new-preview-run>/package -Version 0.1.0-preview.2
./eng/export-public-source.ps1 -RunName <new-source-run>
```

源码输出位于 `artifacts/open-source/<new-source-run>/`，包括 ZIP、SHA256 和机器清单。预览 ZIP 按既有封装脚本输出。构建和封装不启动应用或安装运行时，不把 `publicReleaseApproved` 或 `applicationTestsRun` 改成已通过。

## 验证边界与后续维护

- 干净克隆的自包含发布、预览 ZIP、源码快照独立构建及逐文件校验已完成，见[本轮验证](../verification/2026-10-30-opensource-preparation.md)。
- 完整解决方案仍受既有 SignalR 10.0.11 的 `NU1903` 告警影响；Windows 单人发行不引用该同步客户端。未升级依赖或压制告警，不能把白名单通过称为全仓通过。
- 桌面前台、真实键鼠、DPI、长时运行等继续保留[预览版限制](../releases/0.1.0-preview.2/release-notes.md)。离屏示例不代替人工验收。
- 发布提交的 GitHub Actions 已通过；新增代码仍应按改动范围重新运行 CI。
- 原仓库已按明确授权切换为公开并发布预览；远端 main 通过正常追加提交更新，无强推、删除旧分支或改写已有远端历史。本地原 main、研究工作区和日常安装保持原状；后续公开开发应从新的 origin/main 建立分支，避免补推未审阅的本地研究历史。
