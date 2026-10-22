# B3独立预览迁移与B4规则草案

续行：用户随后批准B4五条规则并纳入结构优化，当前结果见[B4记录](2026-10-22-post-merge-cleanup-b4.md)。下文保留B3交付时的草案与人工状态快照。

日期：2026-10-22。B3状态：**实现与受影响自动验证完成，前台／人工未验**。B4状态：**精简草案待用户检查，未写入有效规则**。用户已授权B3／B4，随后明确要求规则先写出并精简、稍后确认；又补充身体物理及其它接口应稳定、简单、集中切换。当前保存在`codex/post-merge-cleanup-b3-b4`，未合main、推送、安装或部署。[B1／B2记录](2026-10-21-post-merge-cleanup-b1-b2.md)的人工待验状态保留。

## 实施范围

- `4a13476`：四个独立模式由[JointReview](../../eng/tools/Dango.JointReview/README.md)承接，产品App在设置／实例／host前拒绝`--home-ui-review`、`--settings-ui-review`、`--soft-body-preview`、`--paired-contact-preview`。
- 首页／设置复用生产`ProductHomeView`，不创建产品host、设置存储、托盘或网络连接；两个物理窗口连同XAML迁入`eng/tools/Dango.JointReview/Previews`，只在工具编译，视觉控件显式跨程序集引用Desktop。
- 保留旧物理算法与窗口规格。两个物理模式在构造前检查固定旧DLL身份及ABI，初始化／推进失败明确报错；无窗口检查验证真实帧和关闭释放。新增输出拒绝已有文件／目录及链接祖先，报告用CreateNew保留证据。
- 追加设置源码身份`dango-desktop-settings-v4-belt-default-v3`，保留v1／v2。存档格式与路径实例算法不变；两个旧宿主诊断、probe／smoke仍按原用途保留。没有移动共享fallback、焦点、帧合同或更改native算法、材料、预算、存档、协议／服务端。
- `598d072`保存第一版精简草案；用户后续对接口规范的补充继续记录在[整理方案第10～11节](../planning/2026-10-21-post-merge-organization-plan.md#10-b4共同制定规则2026-10-22精简草案)。AGENTS与工作流尚无改动。

## 实际验证

证据根：`artifacts/post-merge-cleanup-b3-b4/`，以下均为相对此目录的路径。

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| Desktop／JointReview构建 | 通过，0警告／错误 | `jointreview-build-final.log` |
| Desktop.Tests及入口／控件 | 构建通过；产品入口拒绝、默认链／作用域身份、模块override与迁移后控件合同8/8，零跳过 | `desktop-tests-build-02.log`、`results/b3-entry-and-controls.trx` |
| 四个工具窗口 | 未显示构造、Measure／Arrange及关闭4/4；两个旧物理预览真实帧通过，timer、native session／body引用释放 | `isolated-previews-final/isolated-previews.json` |
| 失败路径 | 缺DLL／错误哈希／错误参数7/7按预期exit1；已有输出目录／文件2/2拒绝，原报告哈希不变 | `jointreview-negative-results.json`、`isolated-output-preservation.json` |
| 安装作用域与源码身份 | v2原作用域用例及v3当前源码／固定实例向量／旧v2身份保护2/2；真实B1／B2旧v2包仍被当前管理模块校验通过 | `scoped-install-01/summary.json`、`historical-v2-package.json` |
| 正式内部候选包 | 干净`598d072a49fb6c06bd96fa1d0e89eea7d3d22a9d`生成v3包，10/10发布预检；481项包清单校验、同包无窗口smoke exit0并退出 | `publish-01.log`、`preflight-01/summary.json`、`smoke.json` |
| 生产归属 | 构建与发布的Desktop均无两个迁移窗口；工具各编译一份；正式包不含JointReview | `assembly-purity-build.json`、`assembly-purity-package.json` |

候选包为`artifacts/desktop-entry/b3-cleanup-20261022-v1/package/`，`packageType=internal-candidate`、`sourceDirty=false`。后续仅文档改变时不重建相同产品包。旧B1／B2包与全部历史产物保留。

首轮无restore构建因缺assets失败，工具恢复构建另有一次入口误插的编译错误，均核实后修正；初始日志保留。Desktop.Tests仍仅在命令行使用`-p:NoWarn=NU1903`，未改变依赖或项目规则；正式publish没有该例外，不能将其报告为依赖安全验收。

## 边界与保护

桌面规范v0.1第2～6节继续适用：复用页面保持既有主题、布局、草稿与反馈合同；两个历史实验窗口保留原诊断外观，没有借迁移重新设计。无窗口检查不证明真实前台呈现、输入、DPI或人工观感。本轮未执行前台／全局输入，也未重跑未变化的B1／B2矩阵或完整旧物理矩阵。

独立只读审查未发现阻断B3交付的问题；7份当前导航／交付文档的386处相对文件链接存在，Git差异空白检查通过。机读摘要位于证据根`closeout-summary.json`。main仍为`5ae2292`，仅原AGENTS未暂存修改，index为空；main AGENTS SHA256仍`7F2DD53E64CB4E3B32445CBD798D5ABD01AB4B283EB096F5A854674B6802A029`，保护DLL仍`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。没有暂存或提交原用户修改，没有更改其他分支、删除worktree／数据／包／失败记录。

本轮自有Desktop／JointReview进程均已退出，无新增服务、端口或数据库；候选包与虚构fixture数据作为证据保留。B4的新规则与通用接口方向仍待用户确认，尚无全模块重构授权或实现。Schema1 UNKNOWN、旧小尺寸失败、NU1903及完整Windows／DPI／多屏／59FPS／多人边界继续保留。
