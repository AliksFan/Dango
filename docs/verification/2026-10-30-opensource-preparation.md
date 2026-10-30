# 开源准备验证

日期：2026-10-30。来源：`codex/opensource-preparation`，从本地 `main@9fbb828` 建立的独立 Worktree。原讨论工作区的 README、AGENTS 和研究修改保留；准备阶段不修改产品生产逻辑或本地 main，也不改日常安装；后续经明确授权执行的远端公开见末节。

## 本地工程门禁

| 检查 | 当前证据 |
| --- | --- |
| Windows Desktop Release | 通过，0 警告、0 错误；包含产品及房间两个 native 模块源码构建 |
| Core / Protocol | 135 / 135、6 / 6 通过，无窗口白名单 |
| 原生及发布输入合同 | 两个 native 模块来源、产物、ABI 校验通过；受保护 Box2D 输入哈希通过 |
| 现有设置发布兼容合同 | 7 / 7 通过 |
| 编辑器 | 锁文件还原、build、verify 通过；生成资源对 Git 无差异 |
| 新许可封装合同 | 7 / 7 通过：完整材料、缺失或篡改拒绝、原字节复制与来源哈希 |
| 新源码快照合同 | 7 / 7 通过：排除诊断与参考图、许可保留、导出副本引用转换、全文件哈希、dirty 与覆盖拒绝、源树不变 |
| 本地文本审查工具 | 4 / 4 通过：旧提交已移除的虚构 token 仍被发现、值不出现在报告、二进制与示例域、转义路径 |
| 完整解决方案 | 未通过：既有 SignalR 10.0.11 传递依赖 `NU1903`，未升级或压制 |
| GitHub 托管 CI | 发布提交 `2ffc082` 的 [Windows CI](https://github.com/AliksFan/Dango/actions/runs/41000304860) 全部通过 |

## 演示素材

[场景图](../open-source/demo.md)由隐藏 HWND、实际产品 scene/driver 和 native 模块生成。实际 `hidden=true`、`ready=true`、`nativeFault=0`、`nativeTick=successfulSteps=162`。两图已查看，包含团子、平台和球，无真实桌面或用户内容。没有 Show、全局输入或应用 shell；不表示前台呈现、真实输入或性能验收。

## 实际候选核对

实现提交 `43c3b01` 已在新的本地克隆中执行完整 self-contained 发布和预览封装，并从公开源码 ZIP 提取到另一个目录，以虚构验证身份建立独立 Git 元数据后构建。该验证仓库只用于本地构建，没有成为公共仓库，也没有替换作者身份。

- 源码 ZIP 包含 1446 个文件，排除 70 个已跟踪诊断文件和 4 张参考图；2 份文档的相关引用仅在副本中转换。所有文件的 size/SHA256 与清单逐项一致，ZIP CRC 通过。
- 源码快照中 Desktop Release 构建 0 警告、0 错误；Core 135/135、Protocol 6/6、Python 审查工具 4/4、许可封装 7/7、源码导出 7/7 再次通过。
- 干净克隆实际生成 `Dango-v0.1.0-preview.2-win-x64.zip`。应用清单 499 项及外层发行材料 10 项逐字节核对，完整归档 511 项 CRC 通过；两份根许可证与来源一致，原生和微软运行时许可保留。
- 实际包 PDB=0，4 个自有托管 DLL 与 3 个 native DLL 无 CodeView 或 EmbeddedPortablePdb。以实际工作树、克隆目录和用户目录对包内文件进行 UTF-8/UTF-16LE、两种斜杠形式检查，未命中这些具体路径；apphost 的上游 CodeView 路径与微软原件一致。该核对不代替完整二进制秘密清查。
- `sourceDirty=false`，`publicReleaseApproved=false`，`applicationTestsRun=false`；封装没有冒充应用或人工验收。候选仍是本地草稿。

对应证据位于本 Worktree 忽略目录 `artifacts/open-source/candidate-43c3b01/`、`artifacts/oss-verify/clone-43c3b01/` 和 `artifacts/oss-verify/snapshot-43c3b01/`。最后的说明更新不改变上述已验证代码和脚本；最终交付应重新从干净 HEAD 导出，使用其机器清单及 SHA256 识别。

## 已知失败与残留

- 新许可合同首次因 PowerShell 大小写不敏感的动态作用域遮蔽变量失败；已复现并只修测试参数命名，之后 7 项全部通过。
- 离屏示例首个虚构布局被真实出生保护区合同拒绝；改用已有合法锚点，在新目录成功。失败记录保留。
- 所有测试数据、还原缓存、生成输出及审查报告位于本 Worktree 或其忽略产物目录；没有改动日常用户存档。示例工具已退出，无其常驻窗口、进程或端口。
- 前台、长时运行、不同 DPI 和历史保存兼容性沿用已有未验证边界。本地 main 未合并、日常安装未更新，也未部署服务器；远端发布另见下节。

## 原仓库公开执行

作者明确选择“使用原仓库，按此方案公开并发布”后，沿用 [AliksFan/Dango](https://github.com/AliksFan/Dango)，没有新建替代公共仓库。

- 原远端 114 个分支的 835 个提交及 5243 个 blob 完成文本模式检查；53 条字面秘密、8 条凭据 URL、621 条连接串候选经脱敏复核，均为测试数据、变量或匹配误报，未发现其中的真实硬编码秘密。此范围不等于保证整个历史不存在任何秘密。
- 4 张参考图在这些远端历史中均无提交。旧诊断产物在 3 个已有分支中保留；本次 main 源码导入不包含诊断目录或参考图。
- 原远端 main 从 `788456b` 正常快进到发布提交 `2ffc082`；原有另外 113 个分支的 HEAD 逐项核对保持不变。没有强推、删除旧分支或重写原远端历史。
- 发布提交基于原远端 main 追加；没有把本地尚未推送的研究历史作为祖先引入。原本地 main 仍为 `9fbb828`，原讨论工作区的未提交内容保留。后续公开开发应从新的 origin/main 建立分支。
- [v0.1.0-preview.2](https://github.com/AliksFan/Dango/releases/tag/v0.1.0-preview.2) 已公开发布，状态为 prerelease、非 draft，tag 指向 `2ffc082eb7c32c910fdf9f4aa2cc017a7ec01352`。
- 原 GitHub repository ID 保持不变，visibility 为 public，license 检测为 MIT；私密漏洞报告 enabled=true。匿名 API 可读取仓库和 Release，5 个附件均返回 HTTP 200、长度正确，服务端 SHA256 与本地逐项一致。

| 附件 | SHA256 |
| --- | --- |
| Windows ZIP | `8C7B1D88C4AE0C1BA662F87796CDE0F8A0D430649381CDA43E1482756873FBF0` |
| 源码 ZIP | `591CAB45C5DC9B431652C819BBF5D21E5EEDDB8934EFABC6F2A11591146150C3` |

公开 ZIP 的构建来源为 `2ffc082`，与 tag 及其 CI 一致；随后仅更新发布记录文档。封装脚本产生的草稿字段作为构建阶段记录原样保留，实际公开凭据以 GitHub Release 和随附件提供的 BUILD-INFO.json 为准。未执行前台屏幕测试，未启动服务端、数据库或常驻测试进程。编译缓存、私有审查报告及本地候选留在忽略产物目录；用户存档未改动。
