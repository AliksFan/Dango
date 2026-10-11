# 后端工作分支吸收最新main

日期：2026-10-11。用户明确要求“吸收main再继续原目标”，随后说明最新内容为子智能体使用规则。已将当时本地main的配饰／UI基线`44a8775`与新增模型规则`a4acdeb`依次合入`codex/backend-b5-windows`；合并提交为`acddaf2`及`822f07c`。根实际检查最新main是本分支祖先，没有改动main工作区。

## 合并范围与保全

五处冲突为两份状态文档、Home事件处理、Home实时场景和Porch投影。文档保留双方历史范围；代码同时保留账号浮层输入隔离、配饰初始化／预览／Esc／按键释放，以及门廊隐藏配饰和更新目标／投影事件。AppHost账号绑定、昵称、每次登录比较及持续同步保留，配饰本地接线加入同一runtime。

合入前12份尚未完成的B1c文件以独立副本与摘要保存，合入后逐项摘要一致；后续作者继续实施，不清理这些文件。AGENTS五项设计原则与助手负责技术验收的规定保留，并实际加入main的子Agent模型规则：主Agent保持当前模型，资料扫描使用`gpt-6-luna`，复杂实现／审查使用`gpt-6.1-sol`、`high`。现有作者完成安全checkpoint后交接，后续复杂实施和独立审查已按该规则创建。

界面规范采用v0.1（2026-10-10）第2／3／4／5.2／6节，核对共同资源、布局、草稿、文本输入与焦点。保持原已批准页面合同，未实施滚动组件或批量风格改版；规范建议不扩大本轮范围。

## 隔离构建与实际验证

固定Box2D源码commit为`8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`，下载归档SHA256为`FF94DDF7D75FFA6171502FDC7A7F336594560278978305CFC56C72268BC66721`。仅在新artifact目录编译统一物理模块，没有运行会复制生产DLL的脚本。新模块SHA256为`B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E`，与main已验低跳版本一致；受保护`dango_box2d.dll`仍为`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。

首轮Release零警告／错误，14类桌面回归共160展开：158Passed、2Failed、0跳过。失败之一为运行器没有提供旧模块反例要求的`DANGO_PHYSICS_BASELINE_PATH`。核对原只读归档输入SHA256`00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`后，该原固定测试已Passed；没有删测试或改变旧模块能力预期。

独立审查指出缺少“配饰草稿＋账号浮层”组合，新增`AccountAccessoryInputBoundaryTests`。首次两数据行因测试在名称框失焦提交草稿前捕获基线而失败，保留原记录；只修正测试基线捕获时机，再加入runtime输入清零与健康物理帧断言，未改产品草稿语义。最终横→纵及纵→横两例均Passed、零跳过，Release零警告／错误；确认账号浮层期间A／D／W／S／Space与resize不变草稿或磁盘，第一Esc关闭账号，第二Esc从配饰字段完整取消。真实的是B772物理及WPF窗口，键鼠事件为WPF路由夹具，不能替代原生键盘测试。

| 保存证据目录（均位于artifacts/main-absorption） | 实际结果 |
| --- | --- |
| `9db6fd423e364e488967e0696ecc05d1` | 160展开，158Passed／2Failed；native重建及Release通过 |
| `followup-e9d317dc454941d18d09542f5f337c50` | 旧模块Passed、两份组合基线错误Failed、原生窗口被系统拒绝前台而NotExecuted，未注入输入 |
| `corrected-4df50688c6ea419da4cbf7e78828d9a0` | 两份修正后的组合Passed；TRX SHA256`41ECC2C4C875DFFB3A7CB0FF924FF8182AE16CA20A2C40B139D4A29FE810C710` |
| `native-input-c487658a98bf478baeffbc87fd5d363e` | 原生固定用例再次Failed，新增实际焦点与IME诊断明确根因 |

## 原生输入缺陷与后续

首轮原生W没有进入门廊，不能仅按环境问题解释。保留ownedHWND前台条件，补充Stage实际键盘焦点等待和匿名PreviewKeyDown诊断后，在独立原生用例中复现：`Key=ImeProcessed`、`ImeKey=W`、`StageFocused=true`、账号浮层与编辑关闭、无修饰键、`Handled=false`。现有游戏路径只识别原始Key，未识别中文输入法包装的W；没有记录用户输入文本或其他窗口内容。

已窄修游戏路径：在账号、文本、修饰键和焦点保护后解包`ImeProcessedKey`，KeyUp正确释放已持有输入；保留账号与配饰的普通Escape优先合同，文本框中的IME composition Escape不触发导航。没有切换系统输入法或修改输入绑定。

隔离候选`ime-fixed-60c3e615fc8d412e918c8125206d99b0`的Release零警告／错误，受影响四组共35展开全部Passed、零跳过：IME边界18、账号／配饰组合2、原首页交互11、账号UIA4。TRX SHA256为`C17F130F791898A861A39512FA7F4A4CA398D7B25ECFED84ED912B36289FDBCC`。新IME事件夹具覆盖真实WPF处理路径，仍与系统原生输入分开报告。

该目录`native`下单独复测原固定`OwnedNativeKeyboardOpensAndWalksThePhysicalPorch`，精确1个UnitTestResult为Passed、零跳过，TRX SHA256为`C6F72B6E33A44626A8DF71DDF10BAB39D9BADB0BDD619B29E367E7F1E92852D1`。实际SendInput诊断仍为`Key=ImeProcessed`、`ImeKey=W`、`StageFocused=true`，修复后`Handled=true`、`IsInPorch=true`；继续由实体走到创建目标并打开创建表单，未降低原行为断言或改用合成按键。B772输入门禁现已通过。旧完整桌面三例仍明确使用BFDD模块，其通过不能代替本轮B772的正式双账号服务端旅程。

独立复杂审查按`gpt-6.1-sol/high`重新核对处理顺序、可失败断言、两份TRX的唯一execution及各组数量、实际native轨迹、B772与受保护DLL摘要，并读回三个工具的exit0、双流EOF、reader settled和过程／输出资源收尾，未发现阻断。资料扫描按`gpt-6-luna`执行；主Agent保持当前模型。

上述测试未连接PostgreSQL、启用HTTPS或操作系统凭据。已完成工具的有界双流EOF、退出码与过程／输出资源收尾均保留；失败工具自然退出且cleanup确认，原输出和虚构本地数据保留。本次未推送、部署、更新日常安装或替换生产DLL。继续原B1c1候选准备，正式数据操作必须待该候选冻结后独立批准。
