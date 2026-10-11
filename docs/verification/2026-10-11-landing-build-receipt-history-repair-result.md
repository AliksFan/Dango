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

新报告明确`freshRebaseline`并保存原丢失报告SHA，路径`receipt-history/matrix/fresh-old-baseline.json`，SHA`212A4805E8F63E3E8FD1EC1A907226500062AE03E108E1ABCF13158C0468321B`。实际工具SHA`C2FA0A1BA0BA376ECC5A7438029D08ABC261CAF30A72E47D6758A85A1674286E`；四个实际加载依赖的集合SHA`5872C3054AD540523214D6EC95FC7485016615FF5C2C71C9950EDD0BCF4F7573`。独立脚本v2及独立审查复算45组配置／全部效果／恢复，核对完整窗口计数与汇总／严格−96及实际工具和依赖均通过；不是完整逐帧窗口重算，三个指纹已冻结。

新的协调器构建通过（0警告、0错误）；旧native准入未改，v10候选SHA已据有限门禁与独立审查准入。6个严格错误码案例（2正向、4负向）和最新old-baseline／former／landing的裸flag、错误位置、错误DLL、篡改来源、保全已有输出等负例均已实测通过；基线审计另拒绝坏恢复／−960／配置漂移。证据为`matrix/negative-*`，不引用清理前草稿负例冒充最新通过。

新DLL的原失败组合80／50／胖0 changing已完整新跑6660帧，实际退出0；全部健康／绘制／attempt／session计数完整，释放600帧后再方向控制60帧移动`1.6681843996048`，原门槛为≥.04。独立脚本及独立审查均通过，former报告SHA`878B22EE3805587B587DE7912B7163FBF11F59924C366B1C236C342249FCB9D0`、工具SHA`14AD82F63061A8B53D89EAD201E938F5EC14BE38EE947C4291AF341935A7FBFF`、依赖集合SHA`7E2D80A01BB9E4F39B58484F0201B1D83A7EB90309C79B1063E1422AC7168692`已冻结。此报告只有600／6000／6600／6660四个样本，不把它当作前5864帧逐位证明；逐位结论仍限于前述native独立对照。

新建app-landing从头执行45组S1，共162000健康／绘制帧，实际退出0，原短时、均值、检查点、全部效果及恢复角点均通过。独立脚本核配置／检查点／末帧／均值／短时漂移与新旧基线全部相同；独立审查另核每组除耗时外全部trial字段相同，不把报告样本等同于逐帧轨迹比较。默认胖端实际重绘宽／高变化均为0像素。S1报告SHA`78E99A9BC94081A0C5AB3141A1955EBBFA56A9F3674283B46BCAEFD4651ED9E5`、实际工具SHA`7139746A3023C057C08C58D02CD9B29EFDB9618303D15B48B3AE4C4D3F74D4D1`、依赖集合SHA`7E2D80A01BB9E4F39B58484F0201B1D83A7EB90309C79B1063E1422AC7168692`经独立审查后已冻结。

新建app-motion，构建0警告／0错误，裸flag／错误位置／错误DLL／两项篡改来源／已有输出／严格错误6例均实测通过。从头执行171项S2，不复用former或其它旧trial；实际退出1，在第108项立即停止。45项flight、45项recovery和17项drag通过；第108项size80／soft50／build100的ground-swing在4089帧以HealthOrStop／原生−96停止，健康／绘制及最后合法帧为4088，attempt／session为4089。该trial没有完成，S2不通过，剩余63项未执行。原80／50／胖0 changing在本矩阵内再次跑满6660帧通过。

失败报告`receipt-history/matrix/motion.json` SHA`252FC720633F1A73500979387521A4A17886020BC90591521FC01276F45B9C06`、实际工具SHA`90250276F7BF786DB73284C4D1E46604D16825B9F4D0A747EB886B388CAEBA91`、依赖集合SHA`7E2D80A01BB9E4F39B58484F0201B1D83A7EB90309C79B1063E1422AC7168692`已记录；脚本v2核来源、通过项原门槛及首失败停止语义通过，不等于S2物理通过。正在对该唯一新失败进行原／新DLL与固定输入旁路诊断，不改材料参数、门槛或正式源码。S3和L4尚未实现或执行。

## 第二失败的原生旁路诊断

上述S2失败报告已独立审查。真实旧／新DLL均完成4088健康帧，在4089锁存−96；前4088×42＝171696个float字段逐位0差异，包含失败时重复末合法行的两份686952字节轨迹SHA均为`158fa70b0e6b1c4c947c018a1d73ccc04d828697e98b34ed8f6a9ca4b2cb42c1`。实际最后合法42字段与矩阵报告一致；完整配置164字节SHA为`3dbc84f998b93f0c1b76de55bb5902ad42a1277f2cefa0881cb40f6df8965cfa`。

精确编译布局及实际导出诊断独立核验：candidate结构138720字节、old结构134936字节，原262144字节buffer其后均为零。第65410小步native phase4／−96；candidate history phase3／status1、events0，3项原收据均合法，退休0。shared block为NULL_REACTION，容量32、活动6行（39／41／132／133／148／149）、2次linear solve、double full／float检查均为0；第二模式释放row39。旧／新input及damped372字节完全相同。因此该问题属于已有shared求解器，不是本次历史入口引入的回归，不能扩大receipt退休解决。

实际null报告及严格标量算术独立复核：rank5／nullity1，factor／pair／reaction／interval各检查通过，原区间为`[0.00035281981301362044, 0.0067916135845054884]`。原选lower端点，仅normal row132／kind1的`a*alpha+b`为`+4.1359030627651384e−25`，其余10式≤0；精确有理数同样证实原表示点在该约束外。原报告将此选择失败标为INTERVAL_EMPTY，区间本身并非空。

唯一固定算术诊断邻点为`nextafter(lower,+∞)=0.0003528198130136205`，仅前进1个double表示值，仍在原bounds且全部11条原零容差式及有理数检查通过。原source明定“exactly one selection: never move the endpoint”，改变此选择属于独立[新方案](../planning/2026-10-11-landing-build-receipt-history-repair.md#第二失败的有限修正方案已批准实施中)。不把该标量例改判为修复或矩阵通过。

证据在`receipt-history/second-failure/`：actual-DLL结果SHA`b4ba4325c6143e41af24d1c4e12d7325eab466722e102cf44ce59fb3b3efe37e`；精确布局解码SHA`72120ab0be5ebf02380a009fe5172b5646f95934ba3ff9b62c0ff361d93ca40e`；null解码SHA`0ad2cca2f6ae45024af53aa3f83a2b5848dfde460660a76234104c8544bb0eb5`；标量邻点诊断SHA`08bdf84fb94f0305fefb045a116c714f17d36f0efb5951c4767833b088dca44d`。实际DLL、12项已提交native源及原shared源码未修改。

固定输入参考及独立只读审查已完成。原EXE继续复现选择失败；私有ignored header参考仅匹配此冻结的11式／bounds，并只取一次上述邻点，没有世界步进或新DLL。真实dt从actual diagnostic的solver.step_seconds读取，为`0.001041662530042231`。参考通过原selected residual、block（linear2／full1／float1）、fresh double、float及single-round cap检查；外部Python逐体冲量、总动量、角动量、能量和cap账本均通过，人为+.001速度的负控被拒绝（实际残差`.0009999999999998899`大于原allowance`1.5675889180064824e−13`）。原50个诊断文件及12项已提交native文件均未变化，源码保护清单逐项核验通过。审查员只读复跑原／参考两个EXE，stdout与保存结果一致，独立账本各导出指标与原helper精确相等。限速loss账本单独核验；原cap final report的ledgerEvaluated=false不是跳过contact账本。

此固定例的double／float速度恰完全可表示并相等，原capScale＝1、最高速度`5.60836124420166<12`；它验证当前输入的cap validator，没有覆盖实际限速降幅或一般浮点舍入差异。报告`fixed-reference/summary.json` SHA`5d4d0af2eafb2277f8ff4e762fec6f5a999ec0513b23b35a5e8270387a294ee4`；保护清单SHA`b4a21498222aa0c5494df1bb5ea372caf036c15e8df7970ee7b58bec4714be2c`。本结果仅支持呈审的固定输入方案，修复后轨迹及新完整矩阵均未运行。

## 交付边界

原认可候选DLL与正式DLL保持原哈希。未替换正式DLL、更新日常安装、合入main、推送或部署。现阶段通过原生有限门禁、原失败完整复测和新S1；新S2出现上述新失败，完整物理矩阵和人工手感仍未验收。第二失败的唯一邻点固定输入参考及独立审查通过。首轮根本地提交`8587cad`仅包含协调器／计划／结果四文件，保留此前四项dirty文档，不提前标记完成。

2026-10-11用户随后明确回复“批准”，新的有限端点选择合同进入实施：单独native候选及协调器，旧v10入口／报告与原shared源码保留。授权当时新候选、两失败各6660帧、fresh45组S1／171项S2尚未通过；S3不得提前实施／运行。

## 新端点候选复验（进行中）

独立私有v5候选有限门禁与只读审查通过。124项固定断言／43个新增负例、原71收据负例／20普通路径完整字段位比较／23175原断言通过。审查发现并修正了过期INTERVAL_EMPTY报告被复用以及生成器未固定认可manifest字节指纹的资格缺口；v4及更早候选的测试／审查证据保留。候选仍只允许本次原合法选择失败后，尝试一个向原非空区间内部的相邻double，整次block最多一次；物性、自然骨架、原严格门槛及shared源码均未变。

实际v5 DLL SHA为`D70D1348C76F27E4EB8A122C771DA68D41A446EF71E1F32C0BB4B51E9CA6FBC0`，有限清单`endpoint-selection/native-final-manifest-v5.json` SHA为`3cfb13707654e477badbd87441a891d9d67f1e40c8f27057748c2eb02c9049e6`。243个生成文件、233个原Box2D文件、源码精确逆还原及旧来源保护独立复核通过。实际DLL的第一处6000帧／252000字段与v10逐位一致；第二处前4088帧／171696字段逐位一致，第4089帧产生新合法输出。原完整11条失败约束、唯一nextafter及累计一次尝试独立核验。私有trace尾部从旧诊断prefix 138720字节后追加，总诊断157832字节，trace 19112字节；reset及新world清零、小buffer拒绝保全均通过。

实际第二处有限探针最后合法小步的double／float冲量、能量、动量和角动量账本独立复算通过，两个导出缓存均为float，未冒充没有导出的瞬时double缓存。该账本不是更早被修复小步本身的旁路账本；本例capScale=1，没有实际触发限速。修复小步原selected／block／double／float／ledger／cap调用链保留，并有固定输入的完整原门禁对照。10个新native文件已本地提交`d941da57`，提交blob字节与已审核清单相同。

新协调器和严格CLI构建0警告／0错误，源码及独立审计脚本均已只读复核。脚本从真实Samples的float32轮廓复算圆度、恢复连续帧及方向控制位移，绑定实际退出码／报告／日志／工具／依赖指纹；伪造汇总、零移动伪通过、错误退出码及严格释放变形边界负控均正确拒绝。已有输出保全、两个来源篡改、错误native／位置／裸flag和新类严格−96六例实测通过，不依赖PENDING较早拒绝冒充后序负例。

新DLL的80／50／胖0 changing与80／50／瘦100 ground-swing按原顺序各新跑6660帧，实际退出0；报告`endpoint-selection/matrix/former-failures.json` SHA为`8624924F900F62899A6B383C6B8BFBA91A37D390E6E64E5CED280AB68AB7CFB2`。实际工具SHA为`E21711CE8E393F74CE4BC7065D3AC1511F61EC89A45C626B365819DF26A855E6`，依赖集合SHA为`2A0BE404C8ECC8F70A0631B7A9FCA537EAEEE1ED5C0C659CFAA287B675F245F6`。独立脚本及只读审查复算通过，真实float32控制位移分别为`1.6681843996047974`、`1.4335687160491943`，均达到原≥.04门槛；三个来源指纹已固定准入S1。当时S1／S2的新物理trial尚未开始，S3／真实窗口／人工验收仍未开展。

随后新建app-landing（0警告／0错误），fresh45组S1全部通过，共162000健康／绘制帧，恢复角点亦通过；实际退出0。报告`endpoint-selection/matrix/landing.json` SHA为`5AB7B9921B9A312960F40A47985E902033915E9F9E0F31E7BC9E83618A1E699E`，工具SHA为`92E58D8159EA0CBDBEA49D4D4C344EDFE4E0E2681C1BECD9D67923D3F838DB19`，依赖集合SHA为`715F428B704769665BC09594FC5B9F74A89A83BA4E25F457A3DBDD8952B74EEB`。独立脚本及只读审查复算配置／全部效果，核对完整窗口计数与汇总；各trial除耗时外全部报告字段与旧新基线精确相等，额外135个检查点形状复算通过，不扩大为全部162000帧逐位一致或逐帧重算窗口。默认胖端实际绘制宽／高变化各0像素。7个准入／保全案例均命中预期位置，六个严格错误码案例通过。三个指纹已固定准入S2；没有实施S3或打开人工验收窗口。

新建app-motion从头执行171项S2，实际退出1，在第144项立即停止。前143项为45项flight、45项recovery、53项drag，全部完整通过；第144项size100／soft100／瘦100 ground-swing在第3463帧以HealthOrStop／原生−96停止，attempt／session3463，健康／绘制／最后合法帧3462，requested6660，ExecutionCompleted与Passed均为false。当时仍在swing阶段、拖拽活动，尚未进入释放或再次方向控制，Moved为null。后27项size120 drag未执行；不能用先前两处修复的通过推定该组合通过。

失败报告`endpoint-selection/matrix/motion.json` SHA为`DFCF1B672429CC6239AC5C58DD5A3B4FEA3DCFDBF7645B04E56F8BDF4F960DCB`，实际工具SHA为`5DE0E09BD9565C60009C2E56619FAC7F0AFB768131E2DC59E98A25949D64235A`，依赖集合SHA为`715F428B704769665BC09594FC5B9F74A89A83BA4E25F457A3DBDD8952B74EEB`；实际执行回执记退出1、耗时1084.528167秒。独立脚本及只读审查核固定有序prefix、全部通过项原门槛、真实float32形状／恢复／控制位移、实际来源／日志／退出码及唯一首失败一致。`auditPassed=true`仅表示证据一致，`physicalPassed=false`仍为物理失败。与v10原前107项相比，除耗时外全部报告字段精确相同；原两个卡点在本矩阵内重新跑满6660帧通过。S3未实施／未运行，真实窗口和人工验收未开展。

仅对这一个新失败开展旁路诊断。`third-failure/managed-input/`以实际app-motion程序集反射调用`NativeConfiguration.From`及`DangoPhysicsWorld.Step`内实际float输入映射，固定164字节配置、3463×24字节controls、4字节dt；三角函数由.NET Math计算，末目标double与失败trial按位一致，固定步长为166666 ticks／float `0.0166666`。输入导出没有物理世界步进，manifest SHA为`0D2268EE3EDE8733CFF46EAAB9143CAAB66458F38FCA8393D5ED6B55C1617815`。当时准备让实际v5／v10／旧认可DLL独立消费这三个字节输入进行复现，不改物性／原门槛／native来源，不额外扫描邻点。第三失败根因当时尚待复核；既有world最后失败trace可能是旧事件，须按当前来源核对身份后才能判因。

## 第三失败的诊断及待批准参考

三版实际DLL均完成3462健康帧，在3463帧锁存−96；全部145404健康float字段逐位相同，失败返回保留最后合法帧，最后合法42字段与实际S2报告按位一致。三份含重复最后合法行的轨迹SHA均为`71c80d604a582025b43c18312a476dc517a1d1025314c6b25f7983518297a0fa`。第55408小步phase4、block NULL_REACTION；v5／v10 history阶段3／状态1、退休事件0，全部2份原收据合法。原input／damped372字节以及current block／null事实在三版之间均相同。这只证实当前精确轨迹前缀没有v5回归，不代表其它组合通过。

v5只有3463帧发生一次original failure，neighbor attempts0；实际导出的19112字节trace与当前诊断尾部逐字相同，不是此前事件。当前mode0、dimension6／rank5／nullity1，factor／pair／reaction／interval和mode来源均有效；唯一邻点资格拒绝为两个有界端点相等，原选择为`−4.332322529620401e−6`。原row142 SIGN式残差为`+1.6155871338926322e−27`，独立C与Python原10式残差逐位相同。

精确有理数复算表明当前捕获double a/b的真实lower高于upper约`4.0213580048162753e−22`；double端点除法把两端舍入为同一点，当前10式严格可行集实际为空。lower由row59的perimeter node13→14上限绑定，upper由node14地面normal row142绑定；原选中点的row59反力为0，row142反力为微小负数。不能在原区间内再找邻点解决，也不能据此断言其它模式或原物理问题全局无解。原selected／完整double／float／cone／ledger／cap等后序检查均未到达。

诊断清单`third-failure/diagnosis-final-manifest.json` SHA为`9062327b9010fcfbf76e15869fe47fa4f3c75012c7a86ba17644106f2a0d5676`，冻结74份证据、28份原native源码及原第二失败50份证据的保全结果。`diagnosisVerified=true`不等于物理通过，`physicalTrialPassed=false`。fresh entry状态由原源码调用路径证明，未导出的入口bool不冒充实测字段。解码时的CMake字节快照另行保全，原解码结果未覆盖。

仅本小步的ignored `third-failure/fixed-mode-reference/`对照已完成：两个standalone EXE，零world、无DLL。原EXE继续同row142选择失败；参考对同一冻结10式／effective pair／mode／rank使用原certified-particular next-mode helper，在观察副本上改变两个阶段准入标志，仅复制唯一模式变化。原失败报告保留为失败；helper选择node14 normal row142的RELEASED模式，原loop重新构建全部系统、仍保留全部active物理行，不能把临时particular当解或裁剪反力。

参考2次linear solve／1次full-row check／1次float check，重建后为满秩，原fresh double／float行、冲量账本及single-round cap门槛均通过；独立逐体反力、总动量、角动量和能量账本均通过，+.001速度负控被拒绝。double能量`1.184485857734353→1.184485759277408`；实际capScale1、最高速度`3.8404760360717773<12`，没有实际限速降幅。summary SHA为`42108880c2533875503707fd3e6a064e8cae70dd9c8d5f5450f2f9e2411491cb`。

该参考仅支持[有限模式重建方案](../planning/2026-10-11-landing-build-receipt-history-repair.md#第三失败的有限模式重建方案已批准实施中)，不证明一般入口资格、共享一次补救预算或候选负例，不将观察标志改写作为候选实现。独立只读审查已复跑两个EXE，stdout与保存JSON精确一致；逐体double／float反力速度、能量、动量、角动量及cap全部数值与独立summary精确一致；诊断74份证据、固定参考21份证据、28份原native及原第二失败50份证据指纹核验通过。固定参考final manifest SHA为`3e67cb4c166683a2e257ae374ecbdb027809af8562f5e74bddc41274c155e995`。审查仅支持该固定例及呈审方案，不证明一般唯一可切换行。当时尚未批准或实施第三处新候选，没有修复后轨迹、新完整S2、S3或实时手感结果；原物性、shared源码及正式DLL未改。

用户随后明确回复“批准”，第三处有限合同进入实施。新私有候选及协调器仍须独立通过全部原生门禁／新增负例，再从头复测三个旧失败及S1／S2；参考不替代这些验证。原四项dirty文档／删除继续保留；没有main合并、推送、安装、部署或正式DLL替换授权。

## 第三处候选验证入口准备

新建独立`LandingBuildSingletonModeVerification`及严格CLI，保持原协调器与入口不变。先将新DLL及former／S1来源全部设为PENDING，不能提前进入世界验证；不含S3实现或入口。准备版本构建0警告／0错误。对真实app-pending程序集反射执行三个已封闭旧来源导入及第三失败身份检查，15项通过、零world；换行篡改命中各自SHA拒绝，错误计数、−960和后续行−96不能冒充原失败。`matrix/source-gates/result-pending.json`绑定实际工具SHA`3945469EACD01DEB909A32BEBD47D88B69403B9BD0E34DF6E6126646F7F88301`。

新的独立审计脚本v5增加原真实Step计数关系、固定请求帧数及首失败有序前缀，并保留“完整6660帧后ControlResponse门槛失败”的合法情形。它复算完成后控制位移，核对末样本和末帧／形状身份；途中健康赋值前或Observe门禁失败允许真实的计数差异，不能统一当作原生−96。35项实际旧样本／明确标为synthetic的正负控通过，零world；结果`matrix/audit-negative-result-v5-final2.json`，脚本SHA`6b5e8214808ed22a238322e5ff3cc18988a2741bc8e6922a55e43fc60620b4e8`。初轮独立复核发现的失败计数和完成后身份缺口均保留原证据并修补；最终独立审查重现35项完整JSON相同，额外第一帧／赋值前／Observe及S1检查点附近计数边界均合法，无剩余阻断。S1失败亦能审计部分前缀及检查点计数，不把报告一致当作物理通过。

根入口准备副本已在工作区外逐文件SHA核验：`singleton-preparation-200549`共48文件，manifest SHA`f90e85f7336a591fe65235cbc4c635e750d2876714db11fafd2e428d67b98d17`。该副本明确native尚未准入、worldSteps0；不包含正在实施的原生候选，不能冒充新矩阵或最终交付。

## 第三处私有候选有限门禁（已封闭，待独立审查）

新的`singleton-mode`目录共12文件，v4候选DLL SHA`65696cbd70d86138648f3e93bddde51b395694c2b1bebb5010f6a6b9c1fb87df`；`singleton-mode/native-final-manifest-v4.json` SHA`b09194768c408967ef5ae3f3ddd688a4c8ccaa31d4aa12b5b6f8138d061d2128`。新入口使用本次真实选择失败及完整来源绑定；原certified-particular helper的同一body供原／新入口使用，不改写失败阶段标志。邻点和单点模式入口共享整个block一次预算，保留全部原重建／double／float／ledger／cap／发布检查。

新固定608断言／155负例、原邻点124断言／43负例及原收据23175断言／71负例，合计23907断言／269负例全部通过，20普通路径完整字段位比较通过。第一处实际DLL前6000帧、第二处前4088旧健康帧及第4089新合法输出、第三处前3462旧健康帧及第3463新合法输出均经过有限探针；第三处145404旧健康float字段0位差，完整原block报告除14个观测计时范围与固定原失败一致。第三处只由原helper选row142 RELEASED一次，随后原全系统重建通过；额外单帧3464核对当前／历史trace分离及reset／新world／小buffer保全。此处有限prefix不能代替三个完整6660帧复验。

v3原收据测试EXE因新增完整诊断栈副本，以原1MiB测试栈出现`0xC00000FD`，失败产物保留。v4仅该新测试EXE配置8MiB，原test源、数值门槛、候选DLL和客户端栈未改。三个实际DLL有限prefix／生命周期另在显式CreateThread、实测reserve均为1048576字节的线程分别通过，保留默认线程和1MiB两组独立产物。固定及实际double／float／cap账本独立复算通过，全部nodes／activeRows与冻结参考完全相同，速度／乘子篡改负控被拒；capScale1，未覆盖主动缩放限速。

247项生成文件全map及替换逆还原、233个Box2D原文件链核验通过，原28native文件、三旧DLL、第二50／第三74／固定参考21份证据及原v5／v10清单均保全；两项源码负例在创建输出前拒绝。封闭候选已经交独立审查，managed准入仍PENDING，未启动新6660／S1／S2／S3／窗口。工作区外`singleton-native-v4-202255`副本347文件全部源／目标SHA一致，backup manifest SHA`08c047d404c0d9f824c00134d685f441cb204715e28517f1836ca65cacc9cf5e`；副本明确仍需独立审查、完整矩阵未开始。

独立审查随后保留一项阻断：`dango_singleton_end()`只按report的OK默认值记录block_passed，build成功但尚未attempt真正求解时也可能误记true。根任务核实该最小build→begin→end(false)路径，并安排仅增加真实attempted绑定及能失败反例；物理选择与门槛不改，v4不准入managed。其余独立fresh有限复跑均通过：23907断言／269负例／ordinary20；三个明确1MiB线程prefix、第三当前／历史生命周期及fixed／actual全节点账本和原参考完整一致，主动限速覆盖限制仍保留。106项新来源／证据SHA、247生成全map／五项逆还原／233原Box2D及228旧保护复核通过。独立报告`matrix/native-review-v4/review-final.json` SHA`28d50ba449ea3f6bab1b6a0fd9ec47fd5245f1b9301ebbe4333224aeaa1548b6`；该报告明确不准入，不继承为v5通过。修正版使用全新v5生成／构建／证据，v4 live源码变化须与已保全347文件副本区分，不将现有新源码伪称为旧manifest字节。

## 诊断标志修正版v5（有限门禁封闭，待独立准入）

`stage-v4-repro/result.json`以零world复现built／begin为true、attempted为false、statusOK却blockPassed为true；同一新增反例在原generated-v4下构建实际615断言／156负例，唯一失败为未solve不应block_passed，保留`stage-v4-negative/`。v5运行改动恰一行`attempted && status==OK`；相对v4只改变singleton／tests／audit／README四源文件，其余物理代码和全部门槛保持原样。审计支持显式版本及v4副本来源，不覆盖旧清单。

v5重新构建和封闭全部有限门禁：新固定615断言／156负例、原邻点124／43、原收据23175／71及ordinary20，合计23914断言／270负例全部通过。三个实际DLL有限prefix／生命周期在默认线程和实测1048576字节reserve线程均通过；fixed／actual独立double／float／cap账本通过，capScale1限制保留。与已保全v4的receipt6000及实际第一6000／第二4089／第三3463全部流共821184个float字段0位差；这证明诊断修正的有限物理输出一致，未替代新6660或完整矩阵。

DLL SHA`d6b82b9e6dbcc6ce0810330b08453dd37fac2688af0b98355abf6e275ceef0fe`，`singleton-mode/native-final-manifest-v5.json` SHA`38c649bdac8aacc141ed7e855e4aa0a61b897f7c863140ee5c3f66273a46aabd`，新生成manifest SHA`9eb2a92944cc8fd5ff9764f3f6dceb83fbf3a9591bebff0bc5176f216c5db7e9`。v4的347文件副本及全部原artifact逐项保全；Box2D233逆还原及原native／三旧DLL／旧失败证据仍核验通过。工作区外`singleton-native-v5-203845`副本438文件源／目标SHA一致，backup manifest SHA`edd5ee0491c1dd1a2353677983c9e62f9464bf6811f70bedda1a3b407e8b9b2f`。v5已交独立审查，managed仍PENDING；没有任何新完整former／S1／S2／S3或窗口结果。

随后v5独立审查封闭，无剩余阻断，准入范围仅为新三个former完整复验。`matrix/native-review-v5/review-final.json` SHA`0df2138d087f7547b3c431e259818e1b11ee38fda5fcfe101bfb6d76534665d1`；独立fresh23914断言／270负例／ordinary20、默认及实测1MiB三个prefix／生命周期、完整来源逆还原、旧证据保全、独立double／float账本全部通过。独立重现旧v4唯一反例失败，v5通过；821184个有限float字段与v4逐字节相同。active capScale<1、完整矩阵及人工验收仍未验证。

根协调器仅固定该已审核DLL指纹，former／S1前序指纹继续PENDING。新建app-former构建0警告／0错误；8项严格CLI／三个来源篡改／输出保全测试及六个错误码子例通过，真实程序集反射15例通过、零world。随后开始从头执行三处旧失败各6660帧，不继承有限prefix的通过。提交前raw index检查发现Git会把audit.py及CMakeLists.txt的已封闭CRLF字节归一化，未提交该不匹配版本；局部.gitattributes仅为这两个文件保留原字节并识别CRLF，全部12个live／暂存blob重新与封闭SHA一致。

包装只读复核通过，报告`matrix/native-review-v5/commit-packaging-review.json` SHA`3a47118a011a85c38628f0a21c3ca78a4e6cddf34f0cb13cba09272e16887e3d`。属性文件独立固定，不覆盖v5原manifest；本地提交`74b1e84`恰包含封闭12源及属性文件，13个blob逐字节核验通过，原四项dirty未纳入。

## 三处former完整复测（通过，待独立准入S1）

新候选的80／50／胖0 changing、80／50／瘦100 ground-swing、100／100／瘦100 ground-swing按原顺序各完成6660健康／绘制帧，全部释放恢复及重新操控原门槛通过；退出0、实际耗时71.456825秒。报告`singleton-mode/matrix/former-failures.json` SHA`8DDFF07824DD4B13FFFE42E244549C38AE4900A61BFFC8CA93E475EDB53C2C47`，实际工具SHA`6E13CAFC800FF41C2517B4ECD390185DB3D31F489493F101FE7EDBCF1D0D30B5`，依赖集合SHA`A647CDFD9F11C87E5C4FC53EEE0348B2D30918AEA305E29675CB51FF7C3E8922`。独立脚本v5绑定实际日志／退出／报告／依赖并复算真实float32形状、恢复及控制位移，auditPassed和physicalPassed均为true；控制位移分别`1.6681843996047974`、`1.4335687160491943`、`0.9116765856742859`，均超过原≥.04门槛。

前两条trial除ElapsedSeconds外全部保存字段与上一endpoint完整former报告精确相等，包括Samples和末健康帧；该比较只覆盖报告字段，不扩大为所有6660帧流按位对照。完整former结果已交独立只读复核，三前序常量仍PENDING，S1尚未开始；S2／S3／真实窗口／人工验收未开展。

随后former独立复核通过并准入fresh S1，报告`matrix/former-review/review-final.json` SHA`18CD54757AFB43D62BC10360D212FA447817371E4EEEA408EBF20BAA0EAFF152`。实际app-former程序集离线重算全部12个采样的ShapeMetrics及WPF绘制边界精确相同，独立Python形状／健康／径向限及float32操控位移复算通过；35个audit例、15个反射来源门禁及实际CLI／输出保全核对通过，审查零world。三个来源指纹现已固定；新建app-landing准备fresh45组S1，Landing三个常量继续PENDING，不提前准入S2。

完整former及有限native／独立审查／已绑定真实工具和来源已另存工作区外`singleton-former-210329`，607文件源／目标SHA全部一致；backup manifest SHA`d3e26809cdc719e84f153235ba2790b45391dd1a73ed2c4c665b0a41859e7734`。副本明确包含19980帧fresh managed trial，未标为零world，也未包含进行中的S1结果。

## 新singleton S1（通过，待独立准入S2）

全新app-landing构建0警告／0错误，七项来源／CLI／输出保全案例及六个严格旧错误码子例通过；实际程序集的两份前序导入离线核验通过。新45组下落均完成3600健康／绘制帧，共162000帧；原恢复角点亦通过。三档大小的全部胖瘦及柔软度效果判据通过，默认胖端实际WPF绘制宽／高变化各0像素。退出0，实际耗时540.864161秒。

报告`singleton-mode/matrix/landing.json` SHA`5327D45465148618F12504A1C284A95804F1603CB1020BE45743491745A0FD67`，实际工具SHA`499B4608C9C07C2CC1C9C39F0D744F399B6D6369CE9B5ADAF802CE3069027BAA`，依赖集合SHA`763C2142EB35FA6A863D7501E636F8599A586E5044CCC0EB4F01A84A08B22D02`。独立脚本v5绑定实际报告／日志／退出／依赖并复算原配置与完整效果通过。45条trial除ElapsedSeconds外全部保存字段与fresh旧认可基线精确相等；只覆盖报告字段和检查点，不声称162000帧逐位对照。完整结果已交最终独立只读复核，Landing三个指纹继续PENDING，S2尚未开始；S3及人工验收仍未开展。

随后S1独立复核封闭，报告`matrix/landing-review/review-final.json` SHA`54A41BDE1ED2075718FEAD6F75C59838EBECFF91CA15C585560DEB2FB8464732`。5400短门／27000均值样本计数、9组胖瘦效果行和9组软硬效果行均通过原合同；恢复169帧、release spread`.5926310515414428`、start88／1.4666666667秒、连续20圆帧。实际app-landing离线重算109恢复采样及135检查点共244条形状／绘制边界精确一致，默认胖190scale宽高delta0／0，独立audit副本复跑通过；审查零world。Landing三个指纹已固定；全新app-motion构建0警告／0错误，七项来源／CLI／保全和六个严格错误码子例通过，从头启动171项S2。S3尚未实施或运行，不提前打开人工验收窗口。

完整S1及former／原生有限证据／独立审查另存工作区外`singleton-landing-212031`，680文件源／目标SHA全部一致，backup manifest SHA`abbfc1b686979ee0135086d537e4e89ec43658d058e2e84a7b2606f2a2b4c405`。副本不包含进行中的S2报告，没有零world或整批运动通过的错误标签。

## 新singleton S2（第163项停止）

fresh S2在第163项120／100／胖0 changing遇到原生−96，立即停止，后8项未执行；前162项为45 flight、45 recovery、72 drag，均完整通过。三处旧卡点在本矩阵中重新各跑满6660帧通过。新失败requested6660、attempt／session4446，健康／绘制／最后健康／最后观测4445；ExecutionCompleted和Passed均false，仍在swing、DragActive为true、Moved为null。目标double为X`-1.4450767381087757`、Y`2.772285686387237`，没有进入释放或再次方向控制。

报告`singleton-mode/matrix/motion.json` SHA`4401D27891371B39098B2C4AD68D61B675F03794CBA751B8028B2A5E14814F5F`，实际工具SHA`CD7C6A6C1F15D69714F7C064509308934C97256A51F36158945844E2D1127E09`，实际依赖集合SHA`763C2142EB35FA6A863D7501E636F8599A586E5044CCC0EB4F01A84A08B22D02`。执行回执记录退出1、耗时1700.476166秒；独立脚本v5核验真实报告／日志／工具／依赖、全部通过项原门槛及首失败严格有序prefix，auditPassed为true、physicalPassed为false。已交最终独立复核；S2准备审查没有提前判定终态通过。

仅对该唯一新失败在`singleton-mode/fourth-failure/`开展实际managed边界冻结和原／新DLL旁路诊断。必须记录各DLL自己的实际停止位置，旧版如更早停止不强称同帧或整段一致；当前与历史trace身份也独立核验。根因尚待定位，不修改native源码、材料比例、严格门槛或共享一次补救预算。S3没有实施或运行，真实窗口与人工验收仍未开展。

S2终态独立审查已封闭，报告`matrix/motion-review/review-final.json` SHA`006804D56D0A3FAB88CB35EAF069F12BF602B391DCD41DEFE90A5462F6673089`；auditPassed为true、physicalPassed为false，明确不准入S3。严格163项prefix、前162项全部原物理门槛、失败4446／4445计数和实际首行−96均经复核。独立从样本重算60个flight圆窗、108个release恢复汇总及72个控制位移，并由实际app离线重算7894个保存采样的ShapeMetrics／WPF边界，全部一致。前143项全部保存字段除Elapsed外与旧endpoint通过项精确相等；仍不声称未保存整帧流相同。审查零world，不据此推断根因。

已审查S2失败现场另存工作区外`singleton-motion-215710`，754文件源／目标SHA全部一致；backup manifest SHA`737c88337103a72318490e6aa9d861d4bd8bba606246f3148142c60c3ad1d674`。副本如实记录hard-failure-stop、163个记录trial、auditPassed=true、physicalPassed=false及not-admitted-to-S3；不将文件保全成功标为物理通过。
