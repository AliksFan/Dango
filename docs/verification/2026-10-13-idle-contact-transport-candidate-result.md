# 静置横漂候选：零world可行性拒绝

> 日期：2026-10-13
>
> 状态：当前实现路径未获物理准入；原失败及24角点暂停保留

用户批准了`../planning/2026-10-13-idle-contact-transport-candidate-design.md`中的独立候选及最多1world单组复测。实际在零world阶段发现设计错误及实现路径超出固定阻尼约束，因此未构建或运行物理修正候选，未使用这次world预算。

## 1. 已核实事实

- 实际B4各配置的vcxproj及原CMake定义都包含`DANGO_MATERIAL_NO_INTERNAL_HORIZONTAL_DAMPING_CANDIDATE`。有效分支为`vx -= clamp(COMVX, ±35h)`；exp分支未启用。主Agent原设计误读了未启用分支，错误设计与首次按exp假设构建的纯准入checker保留，没有启用新的物理阻尼。
- 拟采用的有界求根把固定clamp结果c0改为未知量u，使最终COM回到原阻尼目标。在alpha=1基线中，要求`Jg.x/M = u-c0`，相对原规则增加的均布冲量为`M(c0-u)=-Jg.x`。这是抵消地面合力的额外反冲，不能称作原clamp不变。该实现路径已拒绝，不自动实施或弱化门禁。
- 新增两个仅含标准C算术的准入checker DLL，不链接Box2D、不提供创建/推进world API。v1保留错误exp假设；v2只用于核对真实alpha=1基线。它们不是物理候选DLL。

## 2. 实际检查结果

`artifacts/idlefix-feasibility-v1/zero-world-receipt.json`与`fixture-ledger.jsonl`记录对原SHA固定的111559880-byte trace仅读取复算。512条封存记录中的509条不符合原设计exp假设；alpha=1复算全部符合真实阻尼逐位输出，其中509条的记录接触增量冲量改变COM超过原float账本精度界限，3条未超界。原active IDs全部139/140/155/156、模式0/0/2/2，其余15点速度未变；基于地面节点11/12的独立质量加权计算与native checker的最大差异约1.906e-21。

最后记录（frame36000/substep15）：固定clamp约-1.20529956e-4，原阻尼后COM约2.49367e-11，接触后约-1.18907439e-5 world/s；新增地面水平冲量约-2.73963335e-6，COM变化约-1.18907688e-5，算术精度界限约5.65094e-11。若固定这份记录的接触冲量，所需u估算约-1.32420725e-4；此值仅是补偿估算，实际Jg会随u/模式变化，不是已经求出的根。

27项实际native算术合同检查通过，覆盖无变化、非finite/坏质量/时长/token/phase、修改阻尼、未平衡增量反力、固定clamp改变及预算越界。通过只表示这些纯准入拒绝条件能失败；`physicalCandidateAdmissionPassed=false`。

检查首版错误断言每条记录都呈冲突，在sequence9527停止，没有写结果；`check.py`和`first-check-failure.txt`保留。`check-v2.py`改为枚举真实509/3分布并逐项独立复算。一次PowerShell修改脚本的here-string语法错误发生于执行前，未写文件；后续改用单独脚本。两次检查都没有物理重跑。

## 3. 边界与交付

本轮0world、0物理Step。实际积分入口采集、联合物理求解、位置修正及10分钟复测均未实施；不将旧36000帧诊断算作本轮执行。上述反例拒绝当前有界求根在“原clamp固定”约束下的实现路径，不证明所有可能的联合耗散模型均不可行。

修订草案`../planning/2026-10-13-idle-contact-transport-candidate-revision.md`明确保留当前alpha=1，并将u成为可调预算内未知量、额外被动性验证视作新的物理行为变更。草案未批准、未实现。原index15失败、原物理门槛、24角点暂停及正式DLL保持；未验证真实桌面、人工验收或收敛，未合main、push、安装、部署或替换正式DLL。

## 4. 终审与证据封存

`artifacts/idlefix-feasibility-v1/terminal-review.json`的SHA256为`283047A89BB0E7E1929A82906B28F889B70F5DADB8791A7238024D568DD7883F`。终审仅核验已存零world记录、源码、公式及hash，没有重新构建、运行checker或进行物理复测，结论仍为当前路径拒绝准入。

保护复核确认5,712份既有证据和受保护文件未变，11份native源码与前序版本原始blob匹配；原dirty、删除和cache保留。`protection-final.json`的SHA256为`7FCD4BF617EC1290AE0F920761FACF5636509825A1E4B7AD4C98CDCD06133A7B`。原候选DLL与正式DLL保持原hash。

本轮外封存标识为`idle-candidate-feasibility-result-v1`，逐文件SHA核验结果以封存目录的`manifest.json`为准；封存收录两版纯算术checker源码、构建、全部本轮证据与终审、原trace及输入、三篇本轮文档。终审在旧build/protection seal之后生成，由新外封存单独收录，不覆盖旧seal或前序封存。

收尾时另有`eng/tools/Dango.MaterialSample/Program.cs`修改和`CornerWindow.cs`新增，不属于本轮；保留且不纳入本轮提交。本轮提交范围仅为候选原设计、未批准修订草案与本结果文档。
