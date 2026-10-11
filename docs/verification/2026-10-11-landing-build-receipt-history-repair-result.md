# 拖甩接触历史收据修复结果

依据[有限修复计划](../planning/2026-10-11-landing-build-receipt-history-repair.md)。本地main的`a4acdeb`已由`661980e`合入当前分支，含子Agent模型选择规则；扫描使用gpt-6-luna，复杂实现／审查使用gpt-6.1-sol／high。根工作区保留此前四项文档修改／删除。

## 已完成的原失败诊断

保持v6物性，第5865帧／第93836小步／contact3／ground shape0与node15 shape17／point0、feature0：

| 阶段 | 实际N | 实际T |
| --- | ---: | ---: |
| 最后摩擦输出 | .0192780085 | −.0138801662 |
| 恢复输出、store及公开manifold | .0187439471 | −.0138801662 |

摩擦末态通过原入口锥检查；恢复阶段降低N但保留T，最终超锥量`.00038452374207409434`，原余量仅`1.3237158036361052e−8`。此前推断的求解迭代不收敛不适用于本帧：原block在构建前拒绝receipt，求解次数0。

position修正后node15的gap为`9.816884994506836e−5`，原row touching tolerance为`5.4400001582130715e−6`，已严格离地。totalNormalImpulse不是恢复差量或可用摩擦预算。

观测程序只增加旁路读取。重新生成的contact_solver.c与实际构建文件字节相同；去掉回调／辅助函数后，原非空代码逐行相等。唯一改动的Box2D源码文件是contact_solver.c。观测与未插桩探针的5865行、246330个float字段按位相同（含失败时保持的最后合法帧）。探针对实际DLL的独立比较仍限先前126个字段，不能声称完整轨迹直接比对DLL。

证据位于忽略目录`artifacts/round-material/landing-build-receipt-repair/`的`observer-final-audit-v2.json`、`stages.jsonl`、`observed-native.json`及`observed-rows.bin`。初始失败构建、过期源码manifest和修正前审计保留；final audit使用实际shape ID、实际落盘源码指纹及原float／double touching算术。

## 单小步固定输入对照

同目录`fixed-input/`的诊断保留原damped state、4项原收据、dt及全部93个输入float字段。旧入口仍以block build／row receipt拒绝。唯一对照从当前support列表排除node15收据，输入速度不变；原block与double／float以及独立逐体冲量、总动量、角动量和能量复算通过，共1425项断言。

该对照有17条active rows、3次linear solve／double full check、1次float检查；N143／T159均inactive，乘子与node15当前摩擦额度均为0。双精度能量`4.1351801100774876→3.8849869339400875`。这是诊断参考，不认证历史来源、不改变旧矩阵结果，不证明未来轨迹通过。`result-v2.json`中单独打印的tolerance使用了float乘法，正式row仍采用原double乘法；离地结论不受影响。

## 候选与验证状态

最终v10原生有限门禁与独立只读审查已通过：71个负例、20组普通合法收据路径完整逐字段位比较（行／梯度／支撑／double与float输出／活动集合与模式／报告／原限速）、23,175断言无失败。前5864帧／246288个字段与原字节基线相同；完整6000帧只有一次历史退休。锁存、最后合法帧、显式reset和实际capture begin拒绝状态均通过。

实际DLL独立6000帧／252000个float字段与源码测试输出按位相同。233个生成来源文件、去除旁路后的原数学源码、反还原后的adapter／collision、实际落盘哈希及三个原输入指纹均独立复核通过。反序证据是有真实shape／contact／world身份及原source kernels的固定阶段输入夹具，不是完整反序世界轨迹。v7／v8反序fixture构造失败、v9测试递归双重巨栈导致stack overflow均保留；修复仅调整测试夹具与薄wrapper，不扩大正式DLL栈或改求解算术。

新DLL SHA为`01E9AD9CCC19E98F60F2B3C5A3987E59F4113E2B88E05E4DED94EF9644BF37E4`。最终清单位于`artifacts/round-material/receipt-history/native-final-manifest-v10.json`，SHA为`82c94869793748f619f265cc6e29e054172a68ee731335de9959e3134f5904fc`。原生候选12文件已本地提交`2e97d22`；根协调器、文档及原dirty项未纳入该提交。

## 外部清理后的恢复与新基线

14:53左右原工作区的Git关联和部分忽略证据被外部清理。用户确认可能是其它任务清理，并要求继续。原两份size-landing／size-motion JSON及旁路观测审计未找回；前述观测数字来自清理前记录，不能再称其原文件仍可读取。保全副本完整按SHA校验；复用独立候选Worktree并切回原分支，恢复本轮修改及原四项dirty状态。

原认可DLL精确找回，原164字节config、372字节state和985320字节旧5865帧轨迹按清理前完整SHA精确重构。状态由原93836小步before-build旁路读取，未修改正式测试；轨迹采用已保全候选前5864行加重复的最后合法行重构，整文件与原SHA完全相同。这是字节重构，不是两份旧JSON恢复，方法和来源见`receipt-history/input-recovery/`的独立manifest。

旧DLL的新基线已实际退出0：45组各3600帧全部新跑、原短时停稳和效果门槛通过，120／100／胖0恢复169帧通过；80／50／胖0 changing仍在5865帧以原生−96停止，健康／绘制5864帧、session／attempt5865帧。该预期停止是原问题重现，仍是物理失败。

新报告明确`freshRebaseline`并保存原丢失报告SHA，路径`receipt-history/matrix/fresh-old-baseline.json`，SHA`212A4805E8F63E3E8FD1EC1A907226500062AE03E108E1ABCF13158C0468321B`。实际工具SHA`C2FA0A1BA0BA376ECC5A7438029D08ABC261CAF30A72E47D6758A85A1674286E`；四个实际加载依赖的集合SHA`5872C3054AD540523214D6EC95FC7485016615FF5C2C71C9950EDD0BCF4F7573`。独立脚本v2及独立审查复算45组配置／完整窗口／全部效果／恢复／严格−96及实际工具和依赖均通过，三个指纹已冻结。

新的协调器构建通过（0警告、0错误）；旧native准入未改，v10候选SHA已据有限门禁与独立审查准入。6个严格错误码案例（2正向、4负向）和最新old-baseline／former／landing的裸flag、错误位置、错误DLL、篡改来源、保全已有输出等负例均已实测通过；基线审计另拒绝坏恢复／−960／配置漂移。证据为`matrix/negative-*`，不引用清理前草稿负例冒充最新通过。

新DLL的原失败组合80／50／胖0 changing已完整新跑6660帧，实际退出0；全部健康／绘制／attempt／session计数完整，释放600帧后再方向控制60帧移动`1.6681843996048`，原门槛为≥.04。独立脚本及独立审查均通过，former报告SHA`878B22EE3805587B587DE7912B7163FBF11F59924C366B1C236C342249FCB9D0`、工具SHA`14AD82F63061A8B53D89EAD201E938F5EC14BE38EE947C4291AF341935A7FBFF`、依赖集合SHA`7E2D80A01BB9E4F39B58484F0201B1D83A7EB90309C79B1063E1422AC7168692`已冻结。此报告只有600／6000／6600／6660四个样本，不把它当作前5864帧逐位证明；逐位结论仍限于前述native独立对照。

新建app-landing从头执行45组S1，共162000健康／绘制帧，实际退出0，原短时、均值、检查点、全部效果及恢复角点均通过。独立脚本核配置／检查点／末帧／均值／短时漂移与新旧基线全部相同；独立审查另核每组除耗时外全部trial字段相同，不把报告样本等同于逐帧轨迹比较。默认胖端实际重绘宽／高变化均为0像素。S1报告SHA`78E99A9BC94081A0C5AB3141A1955EBBFA56A9F3674283B46BCAEFD4651ED9E5`、实际工具SHA`7139746A3023C057C08C58D02CD9B29EFDB9618303D15B48B3AE4C4D3F74D4D1`、依赖集合SHA`7E2D80A01BB9E4F39B58484F0201B1D83A7EB90309C79B1063E1422AC7168692`经独立审查后已冻结。

新建app-motion，构建0警告／0错误，裸flag／错误位置／错误DLL／两项篡改来源／已有输出／严格错误6例均实测通过。从头执行171项S2，不复用former或其它旧trial；实际退出1，在第108项立即停止。45项flight、45项recovery和17项drag通过；第108项size80／soft50／build100的ground-swing在4089帧以HealthOrStop／原生−96停止，健康／绘制及最后合法帧为4088，attempt／session为4089。该trial没有完成，S2不通过，剩余63项未执行。原80／50／胖0 changing在本矩阵内再次跑满6660帧通过。

失败报告`receipt-history/matrix/motion.json` SHA`252FC720633F1A73500979387521A4A17886020BC90591521FC01276F45B9C06`、实际工具SHA`90250276F7BF786DB73284C4D1E46604D16825B9F4D0A747EB886B388CAEBA91`、依赖集合SHA`7E2D80A01BB9E4F39B58484F0201B1D83A7EB90309C79B1063E1422AC7168692`已记录；脚本v2核来源、通过项原门槛及首失败停止语义通过，不等于S2物理通过。正在对该唯一新失败进行原／新DLL与固定输入旁路诊断，不改材料参数、门槛或正式源码。S3和L4尚未实现或执行。

## 第二失败的原生旁路诊断

上述S2失败报告已独立审查。真实旧／新DLL均完成4088健康帧，在4089锁存−96；前4088×42＝171696个float字段逐位0差异，包含失败时重复末合法行的两份686952字节轨迹SHA均为`158fa70b0e6b1c4c947c018a1d73ccc04d828697e98b34ed8f6a9ca4b2cb42c1`。实际最后合法42字段与矩阵报告一致；完整配置164字节SHA为`3dbc84f998b93f0c1b76de55bb5902ad42a1277f2cefa0881cb40f6df8965cfa`。

精确编译布局及实际导出诊断独立核验：candidate结构138720字节、old结构134936字节，原262144字节buffer其后均为零。第65410小步native phase4／−96；candidate history phase3／status1、events0，3项原收据均合法，退休0。shared block为NULL_REACTION，容量32、活动6行（39／41／132／133／148／149）、2次linear solve、double full／float检查均为0；第二模式释放row39。旧／新input及damped372字节完全相同。因此该问题属于已有shared求解器，不是本次历史入口引入的回归，不能扩大receipt退休解决。

实际null报告及严格标量算术独立复核：rank5／nullity1，factor／pair／reaction／interval各检查通过，原区间为`[0.00035281981301362044, 0.0067916135845054884]`。原选lower端点，仅normal row132／kind1的`a*alpha+b`为`+4.1359030627651384e−25`，其余10式≤0；精确有理数同样证实原表示点在该约束外。原报告将此选择失败标为INTERVAL_EMPTY，区间本身并非空。

唯一固定算术诊断邻点为`nextafter(lower,+∞)=0.0003528198130136205`，仅前进1个double表示值，仍在原bounds且全部11条原零容差式及有理数检查通过。原source明定“exactly one selection: never move the endpoint”，改变此选择须批准[新草案](../planning/2026-10-11-landing-build-receipt-history-repair.md#第二失败的有限修正草案待批准)。不把该标量例改判为修复或矩阵通过。

证据在`receipt-history/second-failure/`：actual-DLL结果SHA`b4ba4325c6143e41af24d1c4e12d7325eab466722e102cf44ce59fb3b3efe37e`；精确布局解码SHA`72120ab0be5ebf02380a009fe5172b5646f95934ba3ff9b62c0ff361d93ca40e`；null解码SHA`0ad2cca2f6ae45024af53aa3f83a2b5848dfde460660a76234104c8544bb0eb5`；标量邻点诊断SHA`08bdf84fb94f0305fefb045a116c714f17d36f0efb5951c4767833b088dca44d`。实际DLL、12项已提交native源及原shared源码未修改。

固定输入参考及独立只读审查已完成。原EXE继续复现选择失败；私有ignored header参考仅匹配此冻结的11式／bounds，并只取一次上述邻点，没有世界步进或新DLL。真实dt从actual diagnostic的solver.step_seconds读取，为`0.001041662530042231`。参考通过原selected residual、block（linear2／full1／float1）、fresh double、float及single-round cap检查；外部Python逐体冲量、总动量、角动量、能量和cap账本均通过，人为+.001速度的负控被拒绝（实际残差`.0009999999999998899`大于原allowance`1.5675889180064824e−13`）。原50个诊断文件及12项已提交native文件均未变化，源码保护清单逐项核验通过。审查员只读复跑原／参考两个EXE，stdout与保存结果一致，独立账本各导出指标与原helper精确相等。限速loss账本单独核验；原cap final report的ledgerEvaluated=false不是跳过contact账本。

此固定例的double／float速度恰完全可表示并相等，原capScale＝1、最高速度`5.60836124420166<12`；它验证当前输入的cap validator，没有覆盖实际限速降幅或一般浮点舍入差异。报告`fixed-reference/summary.json` SHA`5d4d0af2eafb2277f8ff4e762fec6f5a999ec0513b23b35a5e8270387a294ee4`；保护清单SHA`b4a21498222aa0c5494df1bb5ea372caf036c15e8df7970ee7b58bec4714be2c`。本结果仅支持呈审的固定输入方案，修复后轨迹及新完整矩阵均未运行。

## 交付边界

原认可候选DLL与正式DLL保持原哈希。未替换正式DLL、更新日常安装、合入main、推送或部署。现阶段通过原生有限门禁、原失败完整复测和新S1；新S2出现上述新失败，完整物理矩阵和人工手感仍未验收。第二失败的唯一邻点固定输入参考及独立审查通过，具体选择合同草案等待用户批准，尚未实现新候选或推进后续矩阵。根本地提交仅包含本轮协调器／计划／结果四文件，保留此前四项dirty文档，不提前标记完成。
