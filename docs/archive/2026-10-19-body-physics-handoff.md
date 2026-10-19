# 身体物理与捏脸来源归档交接

日期：2026-10-19。新任务为 `codex/face-body-belt-integration`，当前计划入口为[连续外圈身体接入](../planning/2026-10-19-belt-body-product-integration.md)。归档表示源分支停止承担后续产品实施，转为历史来源和对照；不表示旧 F5 全部验收通过，也不表示新物理已进入 main。

## 1. 固定来源

| 来源 | 归档 ref | 冻结提交 | 状态 |
|---|---|---|---|
| 连续外圈实验 | `refs/archive/2026-10-19/continuous-belt-lab` | `75621d31e8de2c7f85699bcded503d2088a45c0d` | C5 最终实验人工通过，承接新物理来源 |
| 旧 F5 身体候选 | `refs/archive/2026-10-19/face-body-material` | `6963bde55b194657068153ca0c7fd34c676f8e10` | 实现到 F5，待人工及部分回归；作为旧候选对照 |
| 新产品基线 | `main` 的本次固定起点 | `788456bf52a9d3d699c31a4c00cc0ea39b0c69fd` | 新整合支线基于此提交，main 未推进 |

归档 ref 固定在交接说明提交之前；两个原分支另追加最小收口文档和入口标记。原分支名及工作树保留，不强制检出、重置、删除或移动。以后若修订实验，应新建任务分支，不覆盖这些冻结引用。

新树本轮没有合并两个源分支的生产源码，也没有使用 `ours` 合并制造已集成的历史。继承关系由固定 Git 对象、完整源码历史 bundle、二进制及逐文件提取计划表达；真正移植从新计划 F1 开始。

## 2. 保全介质与范围

本地保全目录相对于 Git common dir：`archives/body-physics-20261019-2d8a190a/`。用 `git rev-parse --path-format=absolute --git-common-dir` 在本机定位，不把个人绝对路径写入仓库。

| 文件 | 用途／SHA256 |
|---|---|
| `source-history.bundle` | 包含两个冻结 ref 的完整 Git 历史，已执行 `git bundle verify`；SHA256 `7F0FAB007BE3BBDA5809937C2F8CE19D79363F74856AF7D2D71150A3560AD4EB` |
| `native/dango_physics_lab_belt.dll` | 冻结新内核 C455；SHA256 `C455EAD0272CA847AE05B2FDCBCC4D09662A1D16886CF6CD3EDC3DAA151DDDCA` |
| `native/face-body-material/dango_physics_module.dll` | 旧 F5 已验候选对照；SHA256 `5A555470A26943A8929A3C38D926859987738D4AEF6873058291C71ABD12C51B` |
| `grounded` | 实验树既有的空未跟踪文件，额外副本；原文件保持未跟踪、未修改 |
| `receipt.json`、`handoff-complete.json` | 原始来源／状态／哈希与收口后的提交和核对结果；收尾收据只有验证完成后才生成 |
| `closeout-history.bundle` | 追加两个源分支收口提交及新分支计划交付后的可恢复历史，收尾时另行校验 |

bundle 保存已跟踪的 Git 历史，不包含所有忽略的 `artifacts`、运行目录和缓存。本次只额外复制明确的冻结模块及既有 `grounded`；所有原忽略产物和失败日志仍保留在原工作树。**这不是完整工作树备份，不足以授权或支持直接删除旧工作树。** 本地保全也不是远端备份，本轮不推送。

源 native 是独立实验模块，不能与应用目录的受保护旧 `dango_box2d.dll` 混同。受保护生产 DLL 不复制覆盖、不修改，其本轮起点 SHA256 为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。

## 3. 继承清单

以下路径是冻结 ref 中的 Git 路径，可通过第 5 节的只读命令恢复；它们不是当前新树已移植的源码。

| 能力 | 来源 Git 路径 | 处置 |
|---|---|---|
| 新身体内核 | `eng/native/box2d/dango_belt.c`、`dango_belt.h`、相关 math／guard／profile／basis 文件及定向测试 | 提取必要数学、保护和已选驱动；不带入废弃弧段或参数搜索 |
| 配置与版本 | `src/Dango.Physics/Configuration/DangoBodyMaterial.cs`、实验 `LabBeltWindow.cs` 中已选 F 标定与开关 | 保留三参数范围，收拢为版本化产品映射 |
| 所有权／调度 | `LabBeltWorld.cs`、`LabBeltPhysicsWorker.cs`、`LabBeltStepPipeline.cs`、`LabBeltPresentationPacing.cs` | 按产品模块边界提取；保留完整 FIFO、代际、回压和释放合同 |
| 正式呈现 | `PhysicsLabAppearancePresenter.cs`、附件／眼睛／步态的最终等价优化 | 提取纯呈现和必要修正，重新核对正式视口／命中／同帧 |
| 身体存储／编辑 | F5 的 `AppearanceSettingsStore.cs`、`AppearanceSettingsSession.cs`、`ProductHomeView.Appearance.cs`、`StackedDangoSceneRuntime.Body.cs` 及对应测试 | 保留字段与草稿／恢复经验；重写新内核及实时预览接线 |
| 休眠经验 | 旧 `dango_rest_sleep.h` 和休眠设计／验证 | 仅保留不误睡、唤醒／故障和开关对照经验，不移植旧 Box2D 休眠代码 |

完整文件依赖须在每批提取前由实际 include／调用核对，不把本表当成未经编译的完整源码导入清单。

## 4. 证据索引与开放边界

| 冻结 ref | 证据路径 | 准确含义 |
|---|---|---|
| 连续外圈 | `docs/verification/2026-10-19-c5-final-acceptance.md` | 用户“操作正常、外观可接受，C5通过”；实验阶段通过，不是产品通过 |
| 连续外圈 | `docs/verification/2026-10-19-c5-background-joint-validation.md` | 八角后台联合及真实离屏像素正负控，不替代 OS 输入 |
| 连续外圈 | `docs/verification/2026-10-19-c5-backpressure-wait.md` | 73/73、八角重跑、7/7 OS 输入；指定 120/100/0 工况实窗 57 目标，连续满 500ms 明示暂停 |
| 连续外圈 | `docs/verification/2026-10-19-belt-long-idle-check.md` | 自然落稳后 302400 tick，整体横漂未见明显异常，内部松弛保留；移动／拖压后长时未覆盖 |
| 连续外圈 | `docs/verification/2026-10-16-force-walking-outline.md` | 多轮候选、实际失败与最终已选受力／步态／线程优化历史，不择优拼接成全绿 |
| 旧 F5 | `docs/verification/2026-10-15-face-body-material-f3-f5.md` | 实现到 F5 的隔离候选；定向通过与全量失败、缺对照、短走／帽子／竖屏／UIA限制分别保留 |
| 旧 F5 | `docs/planning/2026-10-13-face-body-material-implementation-plan.md` | 历史 F1～F6 计划，现被新计划覆盖；原 F7 房间目标留待新能力批次 |

本轮不重跑或扩大旧结论。新实时参数热更新、Schema／模型迁移、正式 Runtime 和节能休眠均未验证；相关前置和停止点见新计划。

## 5. 只读定位与恢复方法

```powershell
git show refs/archive/2026-10-19/continuous-belt-lab:docs/verification/2026-10-19-c5-final-acceptance.md
git show refs/archive/2026-10-19/face-body-material:docs/planning/2026-10-13-face-body-material-implementation-plan.md
git log refs/archive/2026-10-19/continuous-belt-lab -- eng/native/box2d/dango_belt.c
```

本机先定位 Git common dir，再核对 `receipt.json`、bundle 和模块 SHA256。bundle 自含完整历史，必要时可在新的隔离目录以 `git clone <bundle路径> <新目录>` 恢复，再从明确的冻结提交创建工作分支；不得覆盖正在工作的目录或恢复到 main。本轮没有执行恢复、没有删除或重建任何旧工作树。

## 6. 归档完成的含义

旧分支入口已指向各自的源收口记录，新任务仅在新支线推进；两个固定 ref、源码 bundle、两份冻结模块及原产物位置均可查。归档不清理持久数据、备份、忽略产物或运行进程，也不更改历史验收结论。

实际检查结果、旧分支收口提交与新计划提交的关系见[本轮交接验证](../verification/2026-10-19-body-physics-handoff.md)和本地收尾收据。原工作树继续保留，后续若要物理回收磁盘空间，需先完整保全需要的忽略文件并单独执行范围明确的回收。
