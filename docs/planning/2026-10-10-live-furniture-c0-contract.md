# 实时家具 C0：首批实现合同

日期：2026-10-10。用户已明确回复“开始”，授权新版 C0–C4 的本地实现、隔离构建、验证及独立单人验收窗口。实现来源为设计提交 `cba27b9`，工作分支 `codex/furniture-layout-live`。人工验收、main 合并、推送、日常安装与部署分别记录。

## 来源与几何

本批使用设计树内已有平台兼容源，保持身体、材质和显示 adapter 的参数。新圆形模型另行准入，不复制其他树的未提交文件。Box2D 仍为 3.1.1，固定提交 `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`，遵守现有许可证与静态链接方式；构建仅进入本批 artifacts，禁止调用覆盖正式 DLL 的旧构建脚本。候选 hash 在实际构建后记录。

| 项目 | 固定值／合同 |
|---|---|
| 房间水平范围 | `[-8,8]` p |
| 地面／布局顶界 | `-.72`／`6` p |
| 团子出生 | 原 Add 语义 `(-5,0)`；默认 DropHeight `.42` 保留 |
| 出生保护矩形 | X `[-6.3,-3.7]`，Y `[-.72,2]`；平台不能穿入，贴边可接受 |
| 平台 | 宽 `3.4`，高 `.35`，摩擦 `.72`；水平固定 |
| 锚点 | 顶面中心，Y 向上；中心 Y 为 anchorY 减 `.175`；默认 `(0,1.375)` |
| 微调 | `.05` p，每秒最多 20 次，不补跑积压重复 |
| 显示 | 沿既有平台 80／60 投影和 adapter 映射，同一身体造型；屏幕像素不保存 |
| 手套目标 | 同一本人实体，房间内，目标变化最多 8 p/s；原 native 力上限保留，释放速度门禁为实际 MaximumSpeed 不超过 12 p/s |

几何源为公共 `LocalFurnitureSceneGeometry`，窗口、序列化校验及真实场景共同使用。文件 schema 1 保留原场景／预设／坐标版本，新增单调 `layoutVersion` 与 `lastOperationId`。版本 0 的 ID 为 null，正版本必须为非空 GUID。仅 0／1 件默认平台，64 KiB UTF-8 上限，未知／重复字段、不支持版本、非有限值及非法锚点全部拒绝，不钳制或部分载入。布局不含团子状态、相机或抓取计时。

## native 与提交

可选 Furniture ABI 1 增加 Probe／Prepare／ApplyPrepared／CancelPrepared／ReadFurnitureFrame。缺任一能力则不开放真实编辑。native 运行实例为独立 uint64 ID，文件平台 GUID 由同一 scene session 映射，不把文件版本当 native revision。

Token 绑定真实 world generation、序号、scene revision 和 physics step；任意 Step（包括失败 Step）、有效场景变化、取消／释放均失效。Prepare 仅更新待提交元数据并预计算必要接触／支撑，无正常可失败的资源分配留到 Apply；Apply 不调用 Clear→Set、Reset 或 Step，只更新家具与必要接触／支撑。完整 raw 身体、已发布帧、控制、跳跃冷却、原定义／实例以及 revision／step 在拒绝时保持。

贴边与穿入依据真实显示碰撞轮廓，float 几何舍入预算为 `1e-6` p，独立几何 oracle 验证该预算，不能沿用接触求解 `.008` 等宽松容差。删除不因接触拒绝，保持身体位置速度，清旧支撑并自然继续物理。双团子／球互斥硬限制保留。

存储每次请求取得家具专用 lease 并重读原始字节。临时写入、Flush(true)、完整重读在步边界外的后台工作进行；唯一物理 owner 在 Tick 边界完成最新 Prepare → 正式文件 Replace／首次 Move → 目标确认 → Apply → 真实帧核对，无中间 Step。最终替换耗时仍实际记录，不能承诺零停顿。

文件与 native 不是跨资源原子事务，故障态沿[新版计划](2026-10-10-live-furniture-editing-implementation-plan.md)。仅完整最后成功请求在当前 session 内去重；同 ID 不同内容及陈旧请求拒绝。重启后没有完整请求收据时安全拒绝旧 ID 重发，不宣称跨重启无限幂等。已保存未应用的恢复不再写文件或增版本，最新 Prepare 后实际 Apply 并读取对齐才能解禁。

## 文件所有权与验证

- native 负责人：平台 native、Physics 接线及定向 Physics 测试。
- 数据负责人：Core 模型／序列化／几何、Desktop 家具 Store／lease／writer 及定向测试。
- 窗口负责人：JointReview 新窗口与投影。
- 集成负责人：不可变操作、SceneSession、Program 入口、隔离脚本、整链验证与报告。

先执行能识别错误实现的几何、状态保持、token 失效、真实文件与故障门禁，再集成同一单人窗口。构建、软件驱动窗口观察、硬件输入、DPI 与人工手感分别报告；未执行项不推定通过。首批不接球布局、参数、正式门廊／Esc、账号、网络服务或多人。
