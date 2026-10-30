# 公开仓库保护规则

2026-10-30，按作者授权为 `AliksFan/Dango` 启用以下设置。Agent 的执行约束见 [AGENTS](../../AGENTS.md)，开发及发布流程见[开发工作流](../development-workflow-v0.1.md)。

| 范围 | 已启用的约束 |
| --- | --- |
| `refs/heads/main` | 禁止删除和非快进推送 |
| 合入 `main` | GitHub Actions 的 `preview` 检查必须成功，并要求分支基于最新的目标分支 |
| 检查来源 | 绑定 GitHub Actions integration ID `15368`，不是同名的任意提交状态 |
| 保护绕过 | 规则集没有配置 bypass actor；不得为让任务通过而自动关闭或绕过保护 |
| 密钥扫描 | `secret_scanning.status = enabled` |
| 推送保护 | `secret_scanning_push_protection.status = enabled` |

规则集名为 `Public main safety`，ID 为 `26186251`；版本化请求配置见 [main-ruleset.json](main-ruleset.json)。GitHub 的生效规则查询已返回 `deletion`、`non_fast_forward`、`required_status_checks`，分支查询返回 `protected=true`。仓库设置回读确认两项扫描保护均为 enabled。

普通快进更新仍可在适用 CI 通过后执行；本轮没有增加强制 PR 或多人审批要求。日常文档和局部维护可以按已授权范围从新的 `origin/main` 建立任务分支，不需要先修改保留的旧本地 `main`。

修改 CI 的 job 名称、App 来源或保护范围时，须同步核对本配置与 GitHub 实际设置，避免要求一个不再产生的检查。仓库管理设置的变更应纳入明确授权范围，并在执行后回读生效规则；不能仅凭请求成功就报告保护有效。

扫描工具只提供线索，不代替新增内容、素材权利及历史导入的审查。分级门禁、已授权动作不重复确认、发布标签及二进制保持不变等规则按工作流执行；现有贡献许可和私密漏洞报告入口分别保持在 [CONTRIBUTING](../../CONTRIBUTING.md) 与 [SECURITY](../../SECURITY.md)。

本轮只修改规则文档和仓库设置，不改生产代码、CI 覆盖范围、已有发布制品、本地旧 `main`、研究修改或用户存档。
