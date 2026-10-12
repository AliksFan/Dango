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

协调器及S2终态记录已本地提交`5815b29`，恰包含本轮四个root文件，raw暂存blob与live逐字节一致；原四项dirty及cache排除。该提交不代表运动整批或人工验收通过。

## 第四失败冻结诊断与唯一固定输入参考（已独立复核，待新合同批准）

实际managed边界包`fourth-failure/managed-input/manifest.json` SHA`9b7c40cb3beeda338cbeb5137e6980b538c6dc5f30f057b2a296c2a42cac9148`。独立零world审查`matrix/fourth-failure-review/managed-freeze/review-final.json` SHA`dc8e13630dc6b3c3fefce7bbb7e3b85c64efcdd14294cb0978d59076233fa631`通过：实际程序集41字段／164字节配置、全部4446行actual Step lambda输入、初始高度5d／3及Add的conv.r4、FixedStep166666 ticks／dt、最终double目标位值均精确相同。34份原包及保全副本全部SHA不变。

四个实际DLL从同一冻结边界独立运行，都在公开4446帧锁存−96，健康4445帧及停帧保留行完全相同；完整公开流SHA`398edbf775b91010fb8ac56994983d363c5297f7ae03b805f4b3ce8ab5e345f4`，每pair的186690健康float字段0位差，末健康行与实际S2报告按位相同。内部停止必须区别：旧认可版在71124小步拒绝已经离地的超锥记录；receipt-v10、endpoint-v5、singleton-v5在该步正确退休该记录，在71125小步拒绝新触地记录。公开相同不代表失败帧内部world／小步状态相同。

当前71125小步node3的y与float(ground+radius)都为`-.6660000085830688`，gap严格0；原tolerance为`8.1599996412731746e-06`。真实raw N／T为`.08098693192005157`／`-.060812924057245255`，mixed friction为`.7200000286102295`。实际摩擦末态N为`.08446238934993744`，恢复法向差量`-.0034754574298858643`、切向差量0；原摩擦末态通过，恢复后锥超量为`.0025023307577534126`，超过原余量。实时node／live／generation／point／方向／来源、负恢复差量、原摩擦末态、float前后状态、stored／rest检查全部成立，唯一失败guard为detached。原离地入口在此场景正确拒绝，不能把触地记录退休。

source-equivalent observer公开流与实际singleton DLL全字节相同；失败diagnostic仅history.source运行指针前8字节不同，其余200704字节相同。233个原Box2D文件和全部观测插桩逆还原已独立核验。此前71124的合法退休事件与当前分类拒绝分开；singleton当前没有original selection事件，last-event全零，block／rank／selection／retry及下游检查均未到。不能使用旧事件放行当前记录。

唯一私有固定输入参考零world、只执行一次：保留pinned node3真实raw N／T和touching两条active行，诊断副本仅改变该冻结输入的入口guard及该例外slack为0。原入口仍BUILD／RECEIPT失败，参考原build／完整solve、double／float行与账本、原double-scale cap全部通过；solves1、active10、原limit32，额外modeAttempts0。独立重建160条几何／梯度，实际active IDs为6、41、43、45、61、63、84、86、131、147；全17节点速度／冲量、能量、动量及角动量账本均通过。最终node3总N／T为`.2969305336411595`／`-.19582786455627618`，锥excess为`-.01796212816060938`。capScale1；没有主动缩放限速覆盖，也没有新候选运行或完整矩阵通过。

封闭清单`fourth-failure/closure-manifest.json` SHA`2b47eccec7efdfba44da81564432554f66a316a9a541888ac21c20edb263363d`，guard-result SHA`ffde22fb968a651c8bf0a6477f0aa00ca84304cda06a3faf7eb5497f116f640f`，reference JSON SHA`ce0684f30dc99a39a2f33a6883130adbe7e51b17d8365078bd6e201ce72dbaac`，original JSON SHA`e107b0b7602215f1dfb2eaa9fde580ea72aac29498a384d4e7fce8407574b573`，pre-run manifest SHA`7a647efdd6807f01d0a6e375c3f299ae235f6a90b7b0a22bfb1111c95711c5e7`。350项闭包文件及closure自身已独立保全，692个受保护native／旧证据文件未变；HEAD5815b29为根正常提交，不冒充原封闭时点HEAD。

独立证据终报`matrix/fourth-failure-review/native-reference/review-final.json` SHA`16cee207a9b92f6c166ae8a3e40d7f650a0e68d49451c5b0cc87d8105a58222f`，合同审查`contract-review.json` SHA`9338a53b5152a47f7e1b8f47332ed1697405deac383430f564d0e62c95dae3fe`，呈审终报`presentation-review-final.json` SHA`8f488c1950e3e22567f639f13535f4c95cc606db7bb908ccbbb887fef4a70f7c`。当前证书的sequence／step／view71125、shape／body／point／generation及恢复N／T与实际diagnostic逐字段、二进制一致，补充SHA`d54160eb445c9a3963aee84283bcf4e13a5b9a5c5e2043c759268619a632b653`。审查零新world、零native执行，无源码／index／门槛／S3变更。

根计划新增“第四失败的触地记录导入方案（待用户批准）”，审核原raw SHA`11dcaaa80544ba71b2832979b429c15c8ae41a0c03851aa961a14853ae235726`，已保全为contract-draft-preserved.md。独立审查仅确认证据与最小合同足以呈审：gap严格0、整次最多一条单point、fresh完整来源、raw留current及active接触行、该例外slack0、原全部终态门槛和共享补救预算不扩大。新私有目录／运行时入口尚未批准或实施；S2仍失败，candidate及S3均未准入，真实窗口／人工验收／main合并／推送／安装／正式DLL替换未开展。

第四失败及呈审证据另存工作区外`singleton-fourth-223254`，834文件源／目标SHA全部一致，backup manifest SHA`696a5c55ef6cd2540a34b629c1c0ae39b9a7b3b2b73f00bc2e5f6cc86863a0c9`；同时复核前序`singleton-motion-215710`的754份封存文件未变。新副本明确包含实际DLL／observer冻结世界旁路，唯一固定参考则为零world／1次执行；记录physicalMatrixPassed=false、candidateImplemented=false、S3未开始及仍需用户批准，不把审查或保全成功描述为物理验收。

## 第四失败触地入口实施准备（已批准，进行中）

2026-10-12用户明确“批准”本轮有限触地记录合同。新的`touching-receipt/`候选和`LandingBuildTouchingReceiptVerification.cs`协调器分别实施；所有旧native源码、DLL、失败现场和封闭清单保留。新协调器严格导入四份实际旧失败报告，绑定第四失败的原有序163项prefix、前162项通过、4446／4445计数、swing／DragActive／Moved null及准确double目标；原物性、门槛、首失败停止和前序工具／依赖指纹保持。

实际`app-preparation`构建0警告／0错误，未创建物理world。新审计v6的41个案例通过，包含实际四former失败尾部和5个有序prefix反例；`audit-negative-result-v6-final.json`记录worldSteps0。审计脚本SHA`deab236194576408aa288935bbf4841212e2c5223cf805426f4154528f2d9491`。这证明审计能识别伪造统计、错误计数和错误首失败顺序，不证明新候选或四former物理通过。

Candidate及former／S1的全部7个新准入常量仍PENDING。native有限验证和独立复核尚待封闭；没有新完整6660、S1或S2结果，S3没有实施／运行，真实窗口和人工验收尚未开展。原四项dirty、cache和正式DLL继续保留；未合并main、推送或更新安装。

managed准备独立审查随后通过，`touching-receipt/matrix/preparation-review/review-final.json` SHA`3928456b59457006938ef22818cb44c6f61ea368f125e465ec146226efa2be19`。实际app四个Import函数及四个ExpectedStop正例通过；历史第二／第三／第四报告的原108／144／163项有序prefix、前序实际报告／工具／依赖／DLL均核验。第四终态29个有意义反例、28项真实CLI及严格旧错误6子例通过，54份源／app／报告逐项保全，旧协调器与index未改。审查零world；PENDING拒绝不计作未来真实native准入后的来源／CreateNew输出碰撞验证，该后续验证仍必需。

## 新touching native v2有限门禁（封闭，待独立准入）

候选DLL SHA`4388f63610c01fa5413eda20829ea9e6f33ac93ec36a6793dce4dcb0e4eaf757`；`touching-receipt/native-final-manifest-v2.json` SHA`0b12bf4518ad0f8b350a743f6c618d178b6ad0203844f3ead75c9c139c90325a`。新13467断言／81负例及原23914断言／270负例全部通过，ordinary20完整位比较保持。四个实际有限前缀、ABI、reset／新world及四组实测1048576字节reserve线程通过。第四实际DLL前4445帧186690个float字段与旧singleton无位差，4446帧正常完成；该有限结果不等于完整6660或矩阵通过。

当前固定小步完整block65904字节、uncapped／final各372字节及cap336字节与唯一reference相同，17节点double／float／cap账本及160行独立复算通过；原raw保留current、normal／tangent两行active及例外slack0。进程指针、测试双接触fixture导致的world代次0／1和原报告计时按准确offset单独登记，各自source与runtime ID关系独立核验；不泛化忽略证书字段。capScale1限制保持，主动缩放重新超锥拒绝已验证，正向capScale<1未验证。

generated-v2的251文件完整map、11替换逆还原、Box2D233原文件、4项来源反例和1048受保护文件均核验。v1仅新增测试float字面量触发/WX失败，runtime DLL已构建；v2修测试并补双例外证书fixture，物理实现未改。endpoint首跑driver将262144缓冲传入只读138720 loader而失败，正确原artifact复验124断言／43负例通过。两类失败日志和前版产物保留，不作为物理失败或删去冒充一次全过。全部新源LF，局部`* -text`保持封闭字节。

已交现有独立审查，Candidate和前序六常量仍PENDING；四former完整复验未开始，S1／S2／S3和人工验收未开展，无提交／合并／推送／安装或正式DLL替换。

本轮finite native另存工作区外`2026-10-12/touching-native-v2-101308`，753文件源／目标SHA全部一致，backup manifest SHA`67d70d0d7bd2c35d4b2cc81990cb262f8f9a021c68d8ccc782ef447812e28e0c`。副本明确包含有限native世界轨迹，独立审查仍待封闭，完整6660和managed矩阵未开始；不将保全成功当作物理验收。

随后独立终审封闭，`touching-receipt/matrix/native-review-v2/review-final.json` SHA`5bbafd0df946a01aa82750f38c8f43f88ee67514279d7ca8ea46035a8b9ee249`，nativeGateApproved和mayEnterFourFormerGate为true。复核自己的四EXE／351负例／ordinary20、四组实测1MiB actual DLL前缀与生命周期、全部来源逆还原、第三／第四完整账本及逐位比较通过；另一次同DLL、串行创建两个world后，两条1MiB线程各并发执行同一4446冻控，公开流逐位一致、各events1且reset清零。审查比对脚本首轮漏复制的原null-report timing导致失败，按精确timing offset补列后所有其它字段一致，失败日志保留；实现未改。正向capScale<1仍未验证。

根协调器仅固定已审DLL指纹，前序六常量继续PENDING；新建`app-former`用于四个历史失败的全新完整复验，不复用finite trial。S1／S2／S3仍未准入，尚无人工验收或main合并／推送／安装／正式DLL替换。

新app-former构建0警告／0错误；真实准入后9项CLI／四来源篡改／CreateNew输出保全及严格旧错误6子例通过。native源码仅本地提交`9eb55b907f942ca820c44de0b90a69ed42be94aa`，恰11个封闭源，暂存与提交blob均逐字节匹配，1048保护不变。Git helper首轮错误读取审查根层flag而非`admission.nativeGateApproved`，在碰index前停止；脚本和失败记录保全，修正读取位置后完成上述核验和提交，native实现及门槛未改。

## 四处touching former完整复测（通过，待独立准入S1）

四条原有序轨迹各完成6660健康／绘制帧，退出0、耗时81.159915秒；全部原释放恢复及重新操控门槛通过。报告`touching-receipt/matrix/former-failures.json` SHA`642B7FC2D951DE37FEE8A34C9B9BB0B5861B91EDF15E0D6D9D4A3B643B68D218`；实际工具SHA`54061CA8C0824AE24D7D832E620FFCAC9E53F7A06A2AB081A16301F10A38D3E6`、依赖集合SHA`5EC433EC6876B239D90D3A69B1472D112663FC6F5A88DA389F18C0935D7058C8`。v6审计绑定真实日志／退出／报告／工具／依赖，并独立复算保存float32形状、计数、配置、恢复和控制位移；auditPassed与physicalPassed均true。四个再次操控位移为`1.6681843996047974`、`1.4335687160491943`、`.9116765856742859`和`.767914891242981`，均超过原≥.04门槛。

完整former已交现有独立零world审查。三个former指纹继续PENDING，S1未开始；S2／S3／真实窗口及人工验收尚未开展。本轮完整trial不继承finite prefix的通过，既有物性和全部门槛不变。

随后独立零world终审封闭通过，`touching-receipt/matrix/former-review/review-final.json` SHA`7B36B6E5C19BCA770E60AB3F9D83E262861ADA14AF78EC5A0128D26D1E4788D1`，fourFormerGatePassed与mayEnterS1均true。实际app离线复算16样本的全部ShapeMetrics／WPF Bounds／IsRound／Configure、四个控制位移与计数完全一致；前3整个trial除耗时外与旧singleton former一致。24计数反例、41审计案例、9准入后CLI及6严格错误子例通过；92份当前源／应用／报告保全，11个native提交blob及1048旧保护文件不变。仅4个保存样本不能重新计算全部轨迹PeakSpeed／MaximumCanonicalRadius；终报明确该边界，不把样本复算写成全帧复算。

审查自身两次路径／缺少原源快照问题保留失败证据，使用独占新路径及原0a56提交的未改源后复验通过，无实现、物性或门槛变更。外部HEAD`60bcd90`的父提交为本轮`9eb55b9`，仅更新AGENTS.md，已保留。S1准入按已批准顺序执行；S2／S3仍未准入。

former封闭证据另存工作区外`touching-former-104344`，2229文件源／目标SHA全部一致；backup manifest SHA`2078f446d5461ae262650b420051a3a4c2886cffa105bd62e99606a8c8743491`。副本保留former实际app对应的未固定三常量源码，备份后仅固定已审former报告／工具／依赖指纹并构建新的app-landing。该备份物理通过仅指四former，不表示S1／S2／S3或人工验收通过。

## 新touching fresh45组S1（通过，待独立准入S2）

新app-landing构建0警告／0错误，七项真实来源／CLI／CreateNew保全及严格旧错误6子例通过。fresh45下落各完成3600健康／绘制帧，共162000帧；原120／100／胖0恢复角点通过。全部胖瘦及柔软度效果判据通过，默认胖端实际WPF绘制宽／高delta0／0。实际退出0、耗时450.7207615秒。

报告`touching-receipt/matrix/landing.json` SHA`260E3350F4386019D7A2A502E5B448433C3D4375CAE0B448201ECF1F809C1DA2`；实际工具SHA`AB9CF51957907B61B28AC2673DB8E0E9FEB16B65B30EAE66A4620962F303653F`、依赖集合SHA`BD1B03183480914BC8589EA4B55ACA3305679EB1E08E780C79B2FF9BF9B132D5`。v6审计绑定实际日志／退出／工具／依赖与已审former／old-baseline，配置／保存形状／计数及效果核验通过。现有审查代理正在独立零world复核；三个Landing指纹继续PENDING，S2／S3未准入。

随后独立零world终审封闭，`touching-receipt/matrix/landing-review/review-final.json` SHA`FC2279987D2D2993A4FAA35A97BA0A75E492DE8A1FD71543718431EBE0DACAA3`，S1Passed与mayEnterS2为true，S2Passed及S3EntryApproved为false。135下落检查点＋109恢复样本共244轮廓的实际app Shape／WPF和独立Python复算通过；45整个trial及recovery除耗时外与old-baseline及singleton S1相同。3档效果通过，真实程序集拒绝4种有意义效果反例，全部计数、88／1.4666667秒释放恢复时间及默认胖端190scale delta0／0核验通过。600均值／120短窗的逐帧数据未持久化，审查明确仅保存形状复算、完整聚合与两份已封闭报告一致，不声称全部中间帧离线重算。

96份当前源／应用／报告保全，1048旧保护及11native提交blob不变。Python首次将ReleaseSpread要求跨语言exact equality而出现1ULP差异，失败记录保留；改用已有2e-12离线跨语言容差后复算通过，真实app相同字段仍exact equality，原物理门槛未改。S1封存后再固定三个Landing指纹并新建app-motion，不复用旧物理trial。

root首个S1备份命令误将review SHA参数分成两段，精确指纹预检在创建目标／复制前拒绝；调用失败记录保留。修正完整实测参数后重试，不修改审查结论、物理门槛或源码指纹。

随后S1证据另存工作区外`touching-landing-110517`，2413文件源／目标SHA全部一致；backup manifest SHA`01101745b1cb0dc1893da68606ecc9d7679bdf8d39ee4b5d6b56424feb563c7c`。副本保留app-landing实际对应的Landing三常量PENDING源码；完成备份后才固定已审S1报告／工具／依赖指纹并新构建app-motion。封存范围为S1及其前序，不表示S2／S3或人工验收通过。

## 新touching fresh171项S2（通过，待独立准入S3）

新app-motion构建0警告／0错误，七项真实CLI／来源／CreateNew输出保全及严格旧错误6子例通过；实际工具SHA`11798494610FE1241B6C5F644DDBD027E15F5F3F9A6192B0FA085BC1150C3777`，候选native指纹4388…保持。fresh171项S2已从头启动，原45飞行＋45恢复＋81拖甩有序执行，不复用旧trial。三个阶段的实际工具各自冻结，不要求前后重新构建的程序集指纹相同。完整运动物理通过及独立终审前，S3未实施／运行，真实窗口和人工验收未开展。

随后171项全部通过，原有序45飞行＋45恢复＋81拖甩均fresh，reusedTrials0；实际退出0、耗时1598.7705993652344秒。报告`touching-receipt/matrix/motion.json` SHA`7819655A0EB60267798C1F80176C72A637447992ACEA0A1EC384A79E51922843`，日志SHA`E133237B1B75E35EF7CF3C4FD71104C4611AD851DA44CE212D79C99342F7AAFB`；实际工具117984…及依赖集合BD1B…核验通过。v6完整有序矩阵／保存形状／配置／计数与实际退出／报告／日志／来源绑定审计通过，auditPassed及physicalPassed均true。此前第163项120／100／胖0 changing完成6660帧，最后8项亦从头通过；未把旧未执行项补写为复用。现有代理正在独立零world终审，S3仍未实施／运行。

实际171项共552465健康／绘制帧及7929保存样本，恢复时间最大1.4666666666666666秒；这些计数来自最终实际报告，不把保存样本数描述为完整每帧轮廓记录。

随后独立零world终审封闭通过，`touching-receipt/matrix/motion-review/review-final.json` SHA`6B816C09E2B33342D892BD994017C1EBB5BA24CC98EDDD77659B5AC8EA7AD35B`，S2Passed及mayEnterS3Implementation为true。171项actual MotionComplete／Configure及7929保存ShapeMetrics／WPF／IsRound完全相等；独立Python全有序矩阵、计数、配置、来源、实际exit／log、全部保存轮廓／健康、flight与recovery完整保存圆窗、81个float再次操控位移通过。前162整个trial除耗时与singleton旧S2相同；第163整个trial除耗时与本轮former[3]相同，后8项明确为fresh首验。最小再次操控位移`.6186034679412842`，原≥.04门槛保持。

1048保护项及11个native提交raw blob不变，原dirty4／cache／正式DLL保全。reviewer首个close helper误用singleton native指纹代替singleton报告指纹，精确断言拒绝；已改为原FourthFailureSha2564401…，失败helper／日志及定位记录保留，不改物理／源码／门槛。正向capScale<1仍未验证，7929保存样本不能离线复算未保存全部拖甩中间帧的全程PeakSpeed／MaximumCanonicalRadius，终报如实限定。S3可开始实施，尚未运行或通过，人工验收仍未开展。

S2封闭证据另存工作区外`touching-motion-114315`，2631文件源／目标SHA全部一致，backup manifest SHA`6284892e80a3623e1b0db96df35feb59dc77acf181d0d7d409b91ed4e47d8fbb`。副本保留app-motion实际对应的源码和完整stage证据，不包含尚未实施的S3；先保全再进入后级，不回写旧清单。

root managed协调器／Program及本轮两份记录仅本地提交`e6e5c31ae84334231522c333f01f34cfa44075da`，恰4文件，live／index／committed raw blob逐字节一致。原dirty4及cache排除，正式DLL不变；未合并main、推送或更新安装。本地提交时S3尚未实施／运行。

## S3长时实施与默认旧差异对照（首新失败已停止）

S2终审及封存完成后开始后级准备。原`drift-repair-v18-wide-post-drag.json`不在恢复后的当前工作区，不能引用缺失报告为新候选准入。使用已封闭app-motion与原认可055B…DLL，原`PostDragIdleDiagnostics`固定100／50／90参数及原6000正向／镜像拖甩＋同世界36000静置，从头重建两条旧DLL对照；执行前保存实际源／app／依赖／DLL／旧说明和输入manifest，执行日志、实际退出及工具指纹分别保留。该对照不算新候选S3 trial、不声称恢复了原报告字节。旧default配置及每项控制／dt／采样轨迹和实际新旧保存结果须独立核实；不能自动给例外或将原失败改判通过。

旧对照实际退出0、耗时265.9708秒，两条各42000健康／绘制帧。正向原漂移`.010632098`失败，镜像`.000845393`通过；报告`matrix/long-old-default/old-default.json` SHA`360C1373DC9A7435D6A4F480EB590BC3885EDAF5D30D92ADC5D2942327E7FB13`，日志SHA`6D3C3173716BA4668B064CA93459838BF45873F13B7A02E150BF8733F7D9D0AA`，输入manifest SHA`A1570F152F2FD08AA2B4D46128E94A606D358E483EA21268DCF99EE51AABDCDD`。实际app-motion工具117984…、依赖BD1B…、原DLL055B…绑定；原输入快照不回写。

新S3模块仅向原long helper增加可选只读保存轨迹observer，保持原settling守卫先于observer、原世界／控制／力／采样／门槛；默认Forward在原漂移阈值出现新不匹配即停止，其它组合observer为空。唯一旧差异须绑定1180保存样本的double位、6检查点完整native float流、拖甩末帧／最后帧、首次漂移时间及peak／spans／计数；原失败不改判通过，未保存中间帧不声称全部离线逐帧复算。

首构建缺少Tuning using导致2个CS0246、0警告，失败源码／诊断保留；修正后v2／v3／v4均新目录构建0警告／0错误。当前app-long-preparation-v4工具SHA`57398F6362AD2B039FF4A0588F7D8837C1D91984CAF8A09503EB1BDCAFDDF166`，纯合同`matrix/long-contract-v4/result.json`通过7个observer及31个counter反例，明确worldCreations0／worldSteps0／physicalTrialsRun0／candidateAdmitted=false。默认指纹仍PENDING，独立准备审查进行中，候选135项尚未运行。

独审用实际v4程序集证明两个验证绑定缺口：首次漂移的candidate NativeBits损坏及WorldManagedSeconds＋1ULP仍可被保留。旧对照只保存首次时间／phase／detail及该帧gate metrics，没有完整首次native frame／bits；不虚构旧帧，也不重跑旧轨迹。root仅补齐已存时间／phase／frame／prefix span／detail位对照、PeakSpeed／Spans double位比较，以及新首次帧与其NativeBits内部一致性。其余完整保存轮廓对照、物理helper／参数／门槛未改。全新app-long-preparation-v5构建0警告／0错误，工具SHA`7DC692D31A14E3BC9A7D7DB1E89729FB30EEBBFAAF410E671B98E25A48811B06`，原38及新12证据反例通过，world0。新正例为明确标注的合成内部一致性fixture，不声称补回缺失旧首漂移全帧。独审待封闭，Known指纹仍PENDING。

独审终封闭前精确拒绝了v5依赖指纹：实际为`B31537C90AF5B55CCBC2F0FA4132ECCF91490BDB2C52150EFBFE70708A3497E6`，不能当成封闭BD1B集合。当前HEAD的SDK构建嵌入e6e…版本字符串，而封闭app-motion为60bcd…；新v6仅显式重现SourceRevisionId，构建0警告／0错误，但依赖仍为`2027CDD437C89D34118A20A97581E231A8B46D9E3A65C9FDA0A671FD5A947D94`，再次精确拒绝，不声称仅版本字段差异或等价。两版实际产物／错误证据均保留，候选world0。

v7采用明确的运行包组装：v5实际tool／PDB及其运行文件不改，四managed依赖DLL与相应PDB逐文件使用封闭app-motion原字节，源／目标指纹在`long-preparation-v7-assembly.json`保存。实际tool仍7DC6…，runtime依赖严格BD1B，50纯合同反例通过；工具不是重新编译，未验证物理。独审正在核验tool AssemblyRef与原依赖AssemblyDef身份、实际v7 Import／合同／WPF及源码PDB绑定。后续全新app-long构建同样保留所有重建依赖字节，再明确采用原封闭运行依赖，实际新tool／runtime仍需零world复核准入。

随后v7独立零world准备终审封闭通过，报告`matrix/long-preparation-review/review-final.json` SHA`763316A69733FDCC8893D16E2EB2087568C59383084F1DD17759851B025C4815`，mayFixKnownDefaultPinAndBuildFreshLong=true、mayRunCandidateLong=false。6项AssemblyRef身份兼容、4DLL/PDB原字节与BD1B、actualtool／PDB4源、171／45 Import、50反例及v4两旧反例拒绝、9实际CLI通过；22份runtime map、1048保护和11native提交blob核验。原旧对照84000帧、2360保存gate metrics／spans及firstDrift world6781／idle781核验；12个实际WPF105检查点复算通过。首漂移完整旧frame缺失与未恢复原报告边界明确保留。准备指纹仍PENDING，没有候选S3 world；后续需要准确pin派生freshbuild及真实准入后零world复核。

准备证据另存工作区外`touching-long-preparation-123911`，690份源／目标SHA全部相同，manifest SHA`94A69325E4B59C48C5F953B69DD0E93355424E768019D4552F3F6591CD9F2AB5`。完成外封存后仅固定KnownDefault pin为360C…，其它三源字节不变。新app-long构建0警告／0错误；210项构建source／project／配置前后SHA相同，实际tool SHA`6C33DBB7E44B796E45439FE4C12DDA888163E30189FEE67471789FBCC2DED982`。原重建4DLL/PDB全部保留，运行包明确采用封闭app-motion原8份DLL/PDB，runtime依赖严格BD1B。根12项实际准入后CLI／3来源篡改／错native／CreateNew输出保全、50合同及严格旧错误通过，world0。

fresh-build独立零world终审封闭通过，报告`matrix/long-preparation-review/fresh-build-review.json` SHA`834C91ADB416ACD13F8F07EAC06D3C66A164722ED884BEE9959599C5B25AB486`，mayRunCandidateLong=true、S3PhysicalPassed=false。build receipt SHA`60EC1EB3DC79D8528FC134926D2893F88D9F053A423D73D08A494BE1DBD25B24`、22份runtime map、新tool／PDB4源／AssemblyRef、准确pin派生、封闭donors与重建保全核验；独立重复12项真实CLI及实际Import／合同／旧反例／WPF通过。外690、1048保护及11native blob不变。下一步按已批准顺序从头运行135项；此终审没有创建候选world，不替代S3物理或人工验收。

root runner首尝试的输入快照因相对CLI路径与绝对repo的relative_to不相容，在空snapshot目录创建后、任何文件复制／report／world创建前退出。原runner、空目录及`long-run-attempt1-failure.json`保留；保全命令一次PowerShell here-string解析错误亦记录，未执行文件操作。仅将输入4路径resolve为绝对路径，改用全新`long-execution-inputs-v2`，其余前置／首失败停止／原门槛未改。重新启动后输入全快照及两审查／runtime全map核验完成，135项从默认Idle开始fresh执行。

实际退出1、耗时576.263264秒，最终`matrix/long.json` SHA`E793C157937160C98821A344812CD87CDD25387C434968473E25FEEB86785B5C`，日志SHA`0241C916A2B6BB20CD331A74FCF9812FDAB3E45E1A952564B177D9AB6D0F48A9`；实际tool6C33…、native4388…、runtime BD1B…及输入manifest`8F75545BC37BF9CD008350E2A5EFF15A921ECE809A49D59E59644CCEA626D5E2`绑定。仅5项执行，共163201健康／绘制帧和4731保存gate样本，后130项未执行。

| 有序项 | 配置（尺寸／柔软度／胖瘦） | Profile | 实际runner结果 | 完成帧 |
| --- | --- | --- | --- | --- |
| 1 | 100／50／50 | Idle | PASS | 36000 |
| 2 | 100／50／50 | Forward | KNOWN-DIFFERENCE，原漂移仍失败 | 42000 |
| 3 | 100／50／50 | Mirror | PASS | 42000 |
| 4 | 80／0／0 | Idle | PASS；保存后CoreY span另有1ULP往返边界 | 36000 |
| 5 | 80／0／0 | Forward | LongDrift，首新失败停止 | 7201 |

第二项原漂移`.010632097721099854`与重建旧默认保存证据精确匹配，保持Passed／thresholdsPassed=false。第五项在world7201／idle1201停止，原控制跨度为CoreX `.0031642913818359375`，原限`.003`；未发生native锁存停止。11个保存样本覆盖idle601～1201，全程grounded，CoreX从`-.5847913026809692`减到`-.5879555940628052`，最大速度从`.0011836476624011993`减到`.0003404579183552414`。该有限窗单向减速不证明最终收敛、周期性或未来总漂移。

零world的`matrix/long-failure-observation.json`明确区分小样Scale105下的物理中心与可见轮廓：gate窗CoreX绘制中心位移`-.33225059509277344`；加上CoreRelativeDisplayPixelsBounds偏移后，可见bounds中心位移为`+.39116477966308594`，左右边界位移约`+.39077950`／`+.39155006`。绘制宽／高跨度仅`.0010223388671875`／`.005092620849609375`。拖甩末帧至停止时CoreX按float32复算位移`-.006991147994995117`、绘制中心`-.7340705394744873`；不能把中心直接称作整个轮廓的位移。上述为保存样本及WPF绘制单位复算，未测设备DPI、未进行真实窗口或人工验收。

原离线audit-long首尝试严格比较第四项非控制CoreY span时拒绝：stored`2.980232238769531e-08`、recomputed`2.9802322387695312e-08`，差`3.308722450212111e-24`，失败证据保留。新独立`audit-long-v2.py`只对非控制聚合span采用既有离线`2e-12×max(1,abs(expected))`比较；控制MaximumExistingGateDrift仍exact、原`.003`物理门槛未改。`long-audit.json`一致性审计通过，matrixCompleted／allOriginalThresholdsPassed均false；原镜像保存数据的纯合成正例及17个反例通过，world0。独审进一步发现实际C# JSON反序列化后的CountsComplete也受该CoreY 1ULP影响，不能将这一边界仅称跨语言差异或说actual exact门禁已通过。原源、实际应用、JSON及首次失败保留，终审结论另记；S3物理未完成、人工验收未准入。

独立零world终态审查已封闭，`matrix/long-review/review-final.json` SHA`0F9E67C1D513B6A31C34185E2F6BA4DEC891BE2E298F0D6A82805540BCB5EBEB`。auditPassed／forensicAuditPassed仅确认完整性、5项有序前缀及首新失败一致；savedReportExactCountsCompletePassed／matrixCompleted／S3PhysicalPassed／mayContinueMatrix均false，mayPreserveFailureScene=true。实际程序集171项Import、45配置、35个保存native轮廓／WPF105 bounds、2个失败位流、默认两方向2360个样本和16个检查点／起止完整native帧核验通过；4731个gate样本和首失败的控制最大跨度精确复算。210 source／build输入、22 runtime文件、1048旧保护及11native提交raw blob不变，原dirty／cache及正式DLL保全。

1ULP边界在实际.NET10.0.12中直接重现：重算CoreY bits为`3E60000000000000`；JsonSerializer.Serialize及Invariant ToString("R")均写`2.980232238769531E-08`，Deserialize及Invariant double.Parse均回`3E5FFFFFFFFFFFFF`。只修改离线副本的这一span后CountsComplete=true，原报告和实际应用不改；该对照只是定位唯一往返拒因，不重新认证第四项saved report。所有实际控制漂移值和物理门槛仍精确。新有限观察像素账本和v2审计／17反例均经独审复现，未创建新的物理world。当前没有活动矩阵runner；计划文档中的有限对照仅供批准后诊断，不将静态审查写成物理通过。

固定失败组合的双DLL有限观测方案已经独立零world呈审，`matrix/long-review/plan-review.json` SHA`F8502AA1DD5AA6D0F066B47B3B94A2CED9EE5E54F5BDB3E99FA49C5B5FBFB565`，绑定计划SHA`6DF5F1637EFBFD4F6F4E082F7C71739D1B31C503D50227DEA6C7A412BC63F500`。planReviewPassed／readyForUserApproval=true，无阻塞修正；mayImplement／mayCreateDiagnosticWorlds／mayResumeS3Matrix均false。方案只对80／0／胖端0 Forward，两版各fresh6000拖甩＋36000静置，最多84000帧；保留LongDrift失败，其它guard／异常／写盘失败停止整体，完整native／double位旁路用于精确复算。用户新批准及实施零world准入前不得创建诊断world；没有实施、观测结果或恢复矩阵。审查一次旧计划SHA断言在创建snapshot前拒绝，保留失败脚本／日志，再审新版本通过，S3原终报字节不动。

本次首失败现场另存工作区外`touching-long-result-132239`，2152文件源／目标SHA全部一致，backup manifest SHA`3C60BF6A847D032C9C75E595428C45302CEC973A7646D26EEEF030686810A662`。包含实际app／构建及执行输入／报告／审查／原失败尝试、v2审计反例和有限像素账本；封存的outcome为hard-failure-stop，matrixCompleted／S3PhysicalPassed／savedReportExactCountsCompletePassed均false。保全通过不表示长时物理通过；候选DLL和正式DLL均未替换或部署。

## 首新漂移的双版本并行观测（用户已批准，实施中）

用户随后回复“可以测试，但最好进行并行测试，现在这样效率有点低”。仅变更这两条已批准诊断轨迹的调度：原版055B与候选4388使用两个独立进程／world／输出，双方前置通过后共享barrier并行启动。任一非LongDrift或IO／前置失败时整项诊断停止，已准入的一帧在途推进保留，下一Step停止；不重启、不增加world或最大84000公开帧预算。原S3入口、首失败、全部门槛及旧终报不动。

新增独立诊断模块与完整位流记录，原long helper和MaterialSession保持原字节；root负责CLI／构建／协调器与记录，实施子任务负责新诊断源码，独审任务只写新审查目录且不创建world。当前尚未执行本次物理观测；新工具和并行／位流零world门禁独立准入后才启动。


### 双进程有限观测的实际执行

实际工具构建0警告／0错误，严格复用封闭BD1B依赖，未将重新构建依赖声称为等价。tool SHA `ADBD5DA81DEC056F4C7F6C4BA76BCA1742ED4DA8379976913FF3F3D8B8239186`；build receipt SHA `B39F52946E27D643C8EA79E97747AFB1533676DFCAA07C0597576C2990F39B3F`。15项实际CLI拒绝和15项实际helper合同反例均零world；包括ready后stop、错误barrier、peer native及相同PID拒绝。实际程序集／PDB／输入位与并行协议零world独审准入见`drift-diagnostic/review/admission-review.json`，SHA `AE5279DD20B1179B866F7DB1CA8832F772C5F20FBF3BEA0EA39507B529B16344`；绑定supervisor SHA `BA043B254F11CB53DF7F2E6ADBD3E4F40E4034D67B6C05774124B9E27BE7B443`。独审准备脚本一次备份路径错误在零world拒绝后保全，修正路径复核通过，程序未改变。

执行见`drift-diagnostic/execution-v1/execution.json`，SHA `1F27292C540F2D2373263F1FCDE36EB2D2AC349E60A370D0F5A8D2808526112A`。2026-10-12T06:23:38.993303Z双方真实PID Ready（0world）后发布barrier；old PID11216、candidate PID4524，分别1次world创建／1world。两版各6000拖甩＋36000静置，各42000 attempted／session／healthy／rendered／persisted帧，均正常exit0，墙钟123.719秒；未发布stop、未强制终止、输入指纹无变动。没有重跑、第三world或135项S3恢复。

root零world有限账本`execution-v1/finite-observation-v2.json`直接使用double权威位旁路及完整native位流。两版42000帧全部38个float位与实体／接触／拖点身份一致，无首分歧；不因此宣称一般等价或修复因果。两版都在world7201／idle1201以原`.003`门槛失败，原Failed／thresholdsPassed保持false；固定窗口1180原gate样本最大跨度CoreX `.00689011812210083`，CoreY `8.940696716308594e-08`、native宽／高`.0003655552864074707`／`.0022226572036743164`。候选已保存6000、idle600、11样本及7201完整首失败／lastHealthy边界精确匹配旧S3。

显示尺度105的原gate样本绘制宽／高跨度`.00113677978515625`／`.006221771240234375`；可见bounds中心必须加`CoreX*105`与core-relative bounds偏移，跨度`.5263025164604187`、起止净移`+.3682200312614441`。预固定尾段idle35401～36000的600帧全部grounded，可见中心跨度／净移绝对值`.02957332134246826`，绘制宽／高跨度`.0001220703125`／`.00003814697265625`，尾段CoreX仍下降`.00028502941131591797`。尾段最大速度首末`.0004438861215021461`／`.00044369263923726976`，瞬时速度存在波动；不能宣称严格单调减速、最终静止、周期性或无限收敛。所有显示数字是WPF绘制单位，设备DPI未测，未作真实窗口或人工验收。

拖甩末→42000的Core显示中心位移为`-1.125282347202301`，直接按native流float位恢复后作double减法，不是整体轮廓位移。root初版账本曾直接解析保存float十进制，得到`-1.125279750000006`；初版保留，v2改用完整位流，门槛、轨迹与报告不改。终态独立复算尚待封闭；当前仅诊断执行完成，不表示S3通过或准入后续矩阵。

终态零world独审封闭：`drift-diagnostic/review/terminal-review.json` SHA `9284F7CADC7B91DE1AC86E008FE102E668CE250D2FE00C0C37C51F4D79DDDF7B`，auditPassed=true，仅表示记录／来源／执行与复算一致。84000公开记录、2372个实际程序集Shape/WPF原样本／检查点、每帧真实.NET输入位、候选全部已存prefix及root v2账本均核验通过。42000对完整native帧逐位相同，首次分歧null；原`.003`门槛保持失败，originalThresholdsPassed／S3Passed／mayContinueMatrix／manualAcceptance=false。独审首个离线Shape工具在输出后自哈希前仍持有独占句柄，失败脚本／输出／日志保全；关闭句柄后的v2零world复算通过，没有重复物理运行。运行完成后的文档追加单列记录，运行内采用封存输入，不将事后结果记录误判运行输入变动。

`drift-diagnostic/protection-final.json`确认1048旧文件全部不变、11个native live/HEAD raw blob与既有提交指纹相同，3个原dirty存活文档及既有删除保留，正式DLL SHA仍`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。当前诊断worker均退出；无main合并／推送／安装／部署／正式DLL替换。该有限对照支持“这一个组合与旧认可版在本窗口具有完全相同公开物理输出”，不支持改判通过、一般等价或无限收敛。后续矩阵／差异准入或物性修复由结果复核另行确定。


## 剩余S3并行阶段（用户已授权，准备中）

已接受第二差异仅限80／0／0 Forward完整双版逐帧一致的有限观测，原LongDrift失败保留。剩余130项将由最多4个独立进程动态调度，无新增差异例外；当前尚未创建本阶段world。复用index0～4显式引用原报告／诊断及独审，index3历史double1ULP边界保留。详情见计划的新并行阶段；新工具、停止协议及来源零world反例／独审准入后开始。


### 四路实际执行及新失败

新worker保持原long helper／MaterialSession／FrameValidator等字节，以每Step前stop及一次world预算派生原Run；不允许新LongDrift继续。初次构建v1因缺using System.IO出现2个编译错误，源／日志保全；补using并统一新worker LF后v2构建0警告／0错误，tool SHA`F4EF267732BACAB15414C0D441BA67DE4FAB4C7FFB771529205615D69ABBEA23`、PDB SHA`066D4884E58BD696AB448DA6688719FC918B7FE55918BB7DFB5A6A7E61EB837C`、build receipt SHA`6BBF9A0EC1692BD16A68203472F6912B46BE88689747607C606E2D87FE2A93A1`。明确复用封闭BD1B runtime并保留rebuilt副本。

19项实际CLI负例4路并行、4个实际worker同control目录Ready→sharedstop测试全零world；CLI receipt SHA`F62A80E0103C4BBF3CD55E6EDA98910007BEF40102A2693BCE2989C8B0896933`、shared-ready SHA`29A237E89C9AA5961756C443F62C2746B96A16E1C78A6C57064688C8BB9731FD`。首次CLI测试错把原报告FileName当matrix内路径，失败保全；修正为receipt-history/matrix真实212A源后第二测试读到半写Ready JSON失败，保全并向自己测试控制目录发stop；第三测试按完整Ready重试及finally精确owned cleanup后通过，物理world始终0。独审复用前一阶段 actual Plan／Import45／CountsComplete／14实际反例／IEEE位旁路，再核对源码／PDB／AssemblyRef、23合成调度反例和实际运行收据闭合。容量限制导致两次审查交接，已完成证据由收口审查复用，不将模型中断写成门槛通过。实际准入`parallel-long/review/admission-review.json` SHA`0CA416D15A4E437D934C9C4E090153776A90E78CDD537104C540F152974FE58C`，绑定supervisor SHA`2D1AD2CBFF9888FC6D9C553B479C5C5FACCEB136B6208BDB181D13F1889B72BE`。

执行`parallel-long/execution-v1/execution.json` SHA`2A8B984B515DAB6D6334AAD1E6D6822EE1DD7DC6B7FF47D69F1C1E423318C751`。动态4进程实际准入index5/6/7/8，峰值4个独立PID／world，每worker仅一次constructor attempt；墙钟28.25秒，共29713 attempted/session/healthy/rendered帧。index8（80／0／25 Mirror，PID15596）在world7141／idle1141以原LongDrift停止并首次发布sharedstop，控制字段是native Width跨度`.0031126737594604492`，原上限`.003`。index5／6／7分别在6738／8665／7169帧响应peerstop，未入场下一Step；原v2报告的peer-stop native标注另有不一致，见后述取证，lastHealthy和rendered计数保留；它们不能当作完整通过或独立物理失败。无强制终止／来源变动／重试，index9～134的126项未派发，130项fresh完整通过数0。原顺序首失败位置未建立，不把并行先观测到的index8写成原序首次失败。整体矩阵和人工验收未通过。

root保存样本有限账本`execution-v1/failure-observation.json`从实际权威double旁路重算10个gate样本idle601～1141。native CoreX／CoreY／Width／Height跨度分别`.00004863739013671875`／`.002627760171890259`／`.0031126737594604492`／`.0023009777069091797`，不是前次胖端0的CoreX控制漂移。显示尺度105下绘制宽／高跨度`.14121246337890625`／`.09134674072265625`；可见X中心跨度`.005253255367279053`，净位移`+.0020086169242858887`。速度首末`.0019780194852501154`／`.0023083731066435575`，既不能宣布持续减速也不能推断最终收敛／周期性。都是WPF绘制单位，设备DPI／真实窗口未测；这个组合尚未与旧版实际对照，不因同映射自动继承已知差异。终态独审正在复核；下一步有限正反两方向对照只作未批准建议，未创建新增诊断world。


### 停止字段标注缺陷的取证与最小工具修正

独审v1曾按failedAttemptStatus标签误判三个peer-stop报告没有fallback。root复查实际字段发现index5／6／7的FirstFailure.NativeFrame及NativeBits非null：CheckStop先设StoppedByPeer，再ReadAllText(stop)，与publisher写句柄争用抛IOException，进入泛型catch沿原路径保留session.State。该Frame逐字段等于lastHealthy，38float位也相同，实际counter没有额外未授权Step；但标签声称none-peer-stop／stale=false，与保存字段矛盾。原报告／执行／初审保留不改。v1终审SHA`514471A48E2063E81744F435599DE20CAF13CE7266309BB9E2D9618FA170F1EA`；新零world取证`parallel-long/review/terminal-review-v2.json` SHA`CB4B415ECB16240E62FB02787957B0E999C06359DD1B142D015530A61FAA35FD`明确PeerNativeProvenancePassed=false、receiptConsistencyPassed=false、3条标签矛盾。不能把最初“审计通过”写成所有停止字段自洽，index8真实LongDrift与整批停止事实仍确认，矩阵物理仍失败。

本阶段范围内最小修工具：停止仅查File.Exists，不读取正在写的stop文件；泛型catch若已StoppedByPeer则清空currentNative／attemptedIdleFrame，仅非peer继续原fallback。健康／绘制／lastHealthy计数不变，原物性／guard／helper均不动。v3构建0警告／0错误，tool SHA`610EC5E978F5400667706B12D8C48C5C697E87DE4C9321EE06162E74F42BB705`、PDB SHA`5188B60A95C314183495AE2358142CD37F1AE8C3B2158DB866B3B5524BADE4E3`、build receipt SHA`B9BC6F729FB867D48F79F05C08DB7363D3C71B128EE029D215EB2ACFBF7AB06D`。19实际CLI反例及4worker共享Ready→stop零world回归通过；新独占stop文件实测在FileShare.None保持到进程退出的情况下得到WorkerStopException，未走旧File.ReadAllText，attempt／session／healthy／rendered／worlds均0，exit2。新工具只零world回归，不重跑矩阵／补写旧结果，不称v3完成了物理回归；执行v2原源/PDB/加载包保持在封存inputs中。

1048份旧保护文件、11个native live/HEAD raw blob、原dirty／删除状态及正式DLL SHA均核对不变；本阶段全部实际物理worker已退出。下一步正向与镜像旧新4路对照仍仅未批准建议，未实施或创建任何新增诊断world。

新v3停止修正零world独审 `parallel-long/review/stop-patch-review.json` SHA`9A57F9AD0C510F04B659AD21DD83CC15BDC1AFA06137B1DD61BDB7D799DA1249`，patchTestsPassed=true／mayResumeMatrix=false；source/PDB/22runtime／实际CLI及共享取消／独占stop回归收据闭合。独审任务本地提交代码两文件`6291ad44dca39b352788f6d8b7a913abaafacf41`，未纳入既有dirty文档；本轮计划与结果随后单独提交。旧v2执行报告的3条peer-native标签矛盾仍保留，不称修正回写或新物理回归通过。


## 用户接受index8后继续：启动争用修正与新静置缺陷

用户明确不要求旧版对照作为接受依据，并接受index8有限宽度漂移，继续其它组合；8原7141帧失败保留，不称完整长时通过，原未实施旧新4路建议取消。恢复仅5／6／7及9～134，共129项，最多4进程；peer-stop部分运行允许重建，已完成／真失败8不重跑。

恢复v1零world准入56项通过（review/resume-v1/admission-review.json SHA DAA4FAF9A32C027373A85D28E8708868A4DAA73E30290FF6ADF2776C1F4E44ED），实际启动文件发生瞬时共享冲突，index6创建world前IOException发布stop。index5推进7帧、index9推进65帧；6／7均零world，全批2world72帧，原报告／输入保留。没有长时物理判断、新物性失败或强制kill。

worker最小修正仅start文件读取：共享冲突Windows32／33在原60秒timer内CheckStop＋20ms等待重试，其它IO／坏JSON仍拒绝；schema／index／PID／maxWorlds不变。build-v4 0警告／错误，复用BD1B依赖，tool SHA8B13BD8AC586A9DCA8E94F22D4BF923305628F2E9269B68AE849F4B97D493A1F。19CLI反例和4actual worker共享取消并行通过，3actual锁测试和5pure helper反例均零world；helper初次缺using System.IO编译失败保留后修正。新64项零world准入 review/resume-v2/admission-review.json SHA6C3098D5821958FDE01DF62642C7B253A1A2EDB29891E7B2A311FBD96A4E0642绑定当前源码／PDB／runtime／实际测试及中断记录，不修改原物性或helper。

实际execution-resume-v2/execution.json SHA2A630047CD723F395C7A8C9F68DB5B4FF19F1F1AA15F2246662BCCC899AF11D1，四路5／6／7／9运行8.188秒，新共享stop发布者9（80／0／50 Idle）。无需拖甩，静置world／idle2341（约39秒）以native Width跨度.0030759572982788086超过.003原门槛；5／6／7为peer-stop部分运行。没有追加派发index10～134、重跑8、kill或来源变化；129项没有完整通过，矩阵未完成。

30个保存gate样本idle601～2341，原double位旁路复算：绘制宽／高跨度.247222900390625／.10531997680664062，可见X中心跨度.001220054580244323，净移-.001220054580244323；native CoreX／CoreY／Width／Height跨度.000011619567430898314／.0012469589710235596／.0030759572982788086／.0010016262531280518，控制项Width。速度首末.00018453304073773324／.00023149112530518323，全部保存样本grounded；不推断周期性、最终收敛、持续单调变化。单位WPF scale105绘制单位，设备DPI／真实窗口未测。未做旧版对照，用户尚未接受index9；暂停新增world，收口后说明影响询问接受。


恢复v2终态零world独审通过：review/resume-v2/terminal-review.json SHA9F19D029CF986ED646F0E26DB14B26FB42EF552EB5FE79CB20D0C8F656AAEB8B，独立恢复double位旁路重算30保存gate／bounds、4实际报告与8258健康绘制帧、3peer失败native为空、共享stop index9及所有258快照文件。执行中changedInputs={}；当前唯一live差异是运行结束后本计划文档追加结果，源码／runtime／旧证据保持不变。实际worker均已退出，index8不重跑；审计通过不表示index9物理通过或用户接受，矩阵仍停止。源码最小修正本地提交04a1df0；原dirty／删除保留，正式DLL SHA仍6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A，不合main／push／install/deploy或替换DLL。审查模型一次capacity中断不算通过，改由有限零world复算完成闭合，未因此重复物理运行。


用户随后明确接受index9（80／0／50 Idle）的有限静置宽度漂移，并要求在新的对话继续。原2341帧LongDrift及.003失败保留，已观测绘制宽／高变化约.2472／.1053，不称完整36000帧通过或长期稳定。下一对话可继续剩余组合：重建peer-stop中断的5／6／7，首次执行10～134，跳过已接受有限缺陷8／9，复用0～4原前缀。新缺陷说明实际影响并询问接受；不要求旧版对照，不自动降低门槛，不合main／push／install/deploy／替换正式DLL。保持四路并行上限和新失败共享stop，旧部分结果／工具失败记录保留。
