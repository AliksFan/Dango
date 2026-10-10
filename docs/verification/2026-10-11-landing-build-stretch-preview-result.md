# 落地体型两端增强：伸展预览结果

日期：2026-10-11。用户要求两端“明显一档，仍保持团子感”，随后明确“通过，继续”，批准[首个有限增量](../planning/2026-10-11-landing-build-stronger-endpoint-preview.md#已批准的伸展范围增量)。本轮仅完成新预设的默认胖端下落和原失败组合的一条拖甩轨迹，均通过；同尺度外形图已生成，观感待用户确认。完整新矩阵、长时与交互滑块未验证，不宣称本批整体验收或生产可用。

## 1. 范围与实现

新增隔离版本 `landing-build-v3-stretch-preview`，保留 A／B／C 旧映射。体型 0～50、50～100 分别线性插值；体型 50 直接返回旧材料配置。

| 体型 | 径向频率系数 | 外围频率系数 | 抗面积压缩系数 | 径向最大比例 |
|---|---:|---:|---:|---:|
| 胖 0 | 0.50 | 0.70 | 1.00 | 1.30 |
| 默认 50 | 1.00 | 1.00 | 1.00 | 1.22 |
| 瘦 100 | 1.60 | 1.15 | 1.15 | 1.22 |

除这四个字段外，配置精确保留认可基值；初始圆形、自然骨长／面积／角度、阻尼、质量、重力、接触和其余保护均未改变。没有新增塑形力或接地状态切换，没有修改 native 或正式 DLL。

必要源码变更为 `LandingBuildMapping.cs` 的明确候选枚举与映射、`LandingBuildStretchPreviewVerification.cs` 的有限协调器、`Program.cs` 的专用 CLI 入口，以及将现有 `LandingBuildMatrixVerification.TrialComplete` 改为 internal 以复用原完整性检查。

入口参数：

```text
library --preview-landing-build-stretch landingReport cPreviewReport oldMotionStopReport output
```

协调器先校验实际 DLL、来源及配置，再执行默认胖端下落，成功后才执行原触发拖甩。任一硬失败立即停止；返回 0 表示有限通过，1 表示门禁失败，2 表示预检拒绝或执行不完整。输出使用 `CreateNew` 拒绝覆盖。

## 2. 实际验证

| 项目 | 结果与边界 |
|---|---|
| Release 构建 | 零警告、零错误；输出在独立 `landing-build-stretch-preview/app/` |
| 旧 A／B／C 配置及合成判据 | 8/8，退出 0；不冒充物理模拟 |
| 新配置检查 | 2/2，覆盖三档大小、三档柔软度、五档体型的中点精确相等和仅四字段变化，核对端点、插值及连续性 |
| 新默认胖端下落 | 大小 100／柔软度 50／体型 0；3,600/3,600 健康及绘制帧，120 个原短时窗样本、600 个原均值窗样本，600／1,800／3,600 检查点齐全 |
| 下落原短时门禁 | 漂移 `0.00000785742304287851`，保留原 `<0.003` 门槛及接地、速度、穿地、径向上限与实际绘制检查 |
| 原触发组合在新预设下 | 大小 80／柔软度 0／体型 0，ground-swing；6,660/6,660 健康及绘制帧，Passed=true、ExecutionCompleted=true |
| 拖甩释放后响应 | `Moved=1.53541541099548`，原门槛仍为 `.04`；峰值速度 `11.9998807907104` |
| 本轮报告 | 退出 0，completed=true，outcome=`stretch-preview-passed-limited-validation-only`，frozenForProduction=false |
| 错误正式 DLL 预检 | 退出 2，未创建目标报告 |
| 已有报告覆盖预检 | 退出 2，已有 `preview.json` SHA256 不变 |

实际固定步长 `166666 ticks`；下落采样继续使用第 481～600 帧的原短时窗和第 3,001～3,600 帧的原均值窗，未延迟或放宽原门槛。`shapePreviewPassed` 仅表示默认胖端下落、绘制及停稳门禁通过，不表示用户接受增强幅度。所有模拟均为固定轨迹加速执行，不能替代真实交互窗口或人工手感验收。

## 3. 复用来源与失败保留

仅当配置精确相等时复用默认 B50 和新瘦 C100 的完整下落证据；报告保留它们各自的旧 `MappingVersion`，不把旧帧标成新模拟。来源均使用同一认可 native SHA256。C 原报告记录的工具程序集 SHA256 与现有 `landing-build/app-l3/Dango.MaterialSample.dll` 一致；C 原报告没有独立步长字段，不伪补该字段。B 来源和本轮报告的实际步长均为 `166666 ticks`。

| 来源文件（相对 `artifacts/round-material/`） | SHA256 |
|---|---|
| `landing-build/l3-landing-matrix.json` | `A1AB1FA3A92D5F9DFBDE02581D70FBBED7F0FD77C9A19DCBB95CE7E205B12BC2` |
| `landing-build-stronger-preview/shape-trials.json` | `1D99644AE5800B3C0E1B7F0CF64DBB5180C4B490261C5E893B733972B7E629AB` |
| `landing-build/l3-motion-matrix.json` | `6A88DEAF2E0A44FD8A1933F897182352628CC4AE58402CD481A01883361E6378` |

旧 B 在第 4,913 帧 native -96 的失败 trial 原样保存为 `formerFailedCase`，原报告未覆盖。新四项预设在同轨迹通过不等于修复旧 B，也不能将结果归因于径向上限这一单项；原根因未由本轮定位。此前 C 和 v2 外形增强不足的结果同样保留，不改写为通过视觉目标。

## 4. 实际外形

以下为相同大小 100／柔软度 50、相同第 3,001～3,600 帧窗口的原生轮廓均值，不是绘制图片尺寸。

| 体型 | 宽 | 高 | 宽高比 |
|---|---:|---:|---:|
| 新胖 0 | 1.7975638584 | 1.0405396438 | 1.7275303918 |
| 默认 B50（复用） | 1.6943141221 | 1.1257621119 | 1.5050374356 |
| 新瘦 C100（复用） | 1.5649225464 | 1.2330538977 | 1.2691436939 |

新胖相对旧 B 胖约宽 +4.1%、高 −4.1%、宽高比 +8.6%。对比图 `artifacts/round-material/landing-build-stretch-preview/fat-default-slim.png` 使用各自实际第 3,600 帧，统一等比 scale 190、地面线及原 `SampleGeometry.Create` 和装饰绘制；未做身体非等比拉伸。已视觉核对标题、芽、身体及脚线，图中明确完整稳定性待验证。图像中的实际样条宽／高分别为胖 338.2971／197.6784、默认 316.5815／213.8946、瘦 293.0274／234.3037 像素；是否达到用户要求的幅度仍待人工确认。

## 5. 交付状态与未验证项

产物、原始 JSON、stdout／stderr 及预检日志仅保存在忽略目录 `artifacts/round-material/landing-build-stretch-preview/`，不提交本机路径或二进制。`preview.json` SHA256 为 `129946AA14149BD2D6BD997D3A800B22844E822B8A2F4BFE70AC0FDAB9912283`。

认可候选 DLL SHA256 仍为 `055BE2B5E6CD3816EB4D59F616577B653C00512B4750299A013D2CEBF676E2C5`；正式 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll` 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。本轮进程已退出，没有启动交互窗口、服务、端口或持久数据操作；既有独立 motion／long Worktree 保留。

本轮完成必要源码和文档的本地交付，不合并 main、不推送、不安装、不部署、不替换正式 DLL。新预设的其它大小／柔软度／中间档下落、飞行、恢复、其它拖甩、长时稳定和 L4 真实滑块窗口均未验证；生产捏脸、保存迁移、房间及多人接入均未实施。当前在有限预览交付处等待用户确认新两端幅度，不自动恢复原完整矩阵或把候选冻结为生产映射。
