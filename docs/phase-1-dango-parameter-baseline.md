# 用户满意参数基线（2026-09-07）

> 后续变更：本表 16 项不变。用户已批准删除 TopSoftness 和 CrownRoundness，改为[顶部内收力](phase-1-dango-top-inward-force.md)，候选默认 0.5；文末关于这两个旧参数的说明属于历史记录。

来源：用户提供的控制中心截图，并明确要求暂设默认。仅记录截图中可见的 16 项；其他旧机制参数保持代码原值，不推断截图以外的用户设置。

| 控件 | 参数 | 暂定默认 |
|---|---|---:|
| 团子大小 | RadialRestLength | 0.68 |
| 径向压缩下限 | RadialMinimumRatio | 0.65 |
| 径向拉伸上限 | RadialMaximumRatio | 1.22 |
| 外围压缩下限 | PerimeterMinimumRatio | 0.73 |
| 外围拉伸上限 | PerimeterMaximumRatio | 1.07 |
| 径向弹性 | RadialElasticity | 5.2 |
| 外围弹性 | PerimeterElasticity | 20.1 |
| 径向阻尼 | RadialDamping | 1.85 |
| 外围阻尼 | PerimeterDamping | 1.85 |
| 面积保持 | AreaPreservationStrength | 4.8 |
| 骨架排斥距离 | BoneRepulsionDistance | 0.09 |
| 骨架排斥力 | BoneRepulsionStrength | 0.3 |
| 排斥最大力 | BoneRepulsionMaximumForce | 3.5 |
| 核心排除比例 | CoreExclusionRatio | 0.25 |
| 90°～180°纠正力 | PerimeterAngleStrength | 10.0 |
| 局部角度均衡力 | LocalAngleBalanceStrength | 1.7 |

用户在 Window 地面错位修复后已确认桌面表现与操作手感通过；顶部仍略尖，单独留作[轻圆拱局部试验](phase-1-dango-rounded-crown.md)。本表 16 项继续保持不变。新硬约束中的“纠正力”是软缓冲强度，不决定是否允许越界。

截图不含顶部柔软度，`TopSoftness` 默认继续为 0；高度降低已通过自动对照，但用户指出其不能充分改善尖顶。新局部试验另增 `CrownRoundness`，不覆盖本表数值。源码默认值更新不直接重置正在运行的现场设置；重启采用当前默认值。
