# Phase 1 动画动作规格

> 状态：AP-000 接口基线；动作参数待 AP-003/AP-004 视觉样本验证
>
> 日期：2026-09-02

## 1. 接口基线

- 动画预生产分支基线：`d6df4ca`，设计与计划提交：`a5792e7`。
- 最近核对的 Phase 1 阶段分支：`0e199d6`。
- 动画分支与 `0e199d6` 之间，`Dango.Core` 动画/资产模型、`assets/placeholder` 和 Core 资产测试没有差异。
- 当前运行时资产版本为 `1`，样例逻辑画布为 160 × 120、原点 `(0, 0)`、像素密度 `1`。
- Core 解析器是语义事实来源；JSON Schema 只描述结构。

本记录只冻结 AP-000 所见接口。实施中若阶段分支改变这些事实，必须停止并重新核对，不能以本文覆盖新代码。

## 2. 首批状态映射

| Core 状态 | Clip | 动作意图 | 关键语义 |
|---|---|---|---|
| `Idle` | `idle` | 安静待机、呼吸与低频眨眼 | 地面接触稳定；允许进入移动、接近、拖动或反应 |
| `Wander` | `wander` | 低干扰桌面移动 | 移动由场景决定，动画不裁定位置结果 |
| `ApproachIsland` | `approach-island` | 接近岛边并准备抓取 | 最终姿势对准岛边，不提前宣布登岛 |
| `ClimbUp` | `climb-up` | 抓边、提身、翻越和站稳 | `island-grabbed` / `island-grab` |
| `OnIsland` | `on-island` | 岛上安静待机 | 最终接触 `island-stand` |
| `ClimbDown` | `climb-down` | 探身、离边并进入下落 | 结束进入 `Falling`，不直接落地 |
| `Dragged` | `dragged` | 被抓取后的悬空和有限弹性 | 不采集或保存真实鼠标轨迹 |
| `Falling` | `falling` | 下落姿势和方向可读性 | 落地点仍由 Core 决定 |
| `Landing` | `landing` | 接触、压缩、回弹和恢复 | `landed` / `ground-contact` |
| `Reacting` | `reacting` | 点击或抚摸后的短反应 | fallback 为 `idle` |
| `Hidden` | 无 | 完全不可见并停止持续动画 | 不制作可见占位 clip |

## 3. 图层与语义点

- 用户确认的首版资产兼容要求：身体纯色和帽子是两个独立可配置类别；眼睛属于表情/动画层，不进入捏脸预设。
- 团子没有脚、腿、爪或手臂，主体底部保持连续圆弧；移动、落地和爬岛通过整体重心、轮廓形变与接触面表达。
- 必需图层职责：`body`、`eyes`、`mouth`、`blush`、`hat`；运行时需映射到当前 `body`、`face`、`accessory` slot，眼睛、嘴和腮红在源文件中继续独立。
- 所有视觉只使用纯色，不包含光照、高光、渐变、材质明暗、接触影或地面阴影。
- 当前 slot：`body`、`face`、`shadow`、`accessory`。
- 当前契约仍定义 `shadow` slot，但预生产候选不提供对应图层；P1-004 接入时不得用空白假图制造阴影资产完成的假象。
- 当前 anchor：`ground-contact`、`click`、`island-grab`、`island-stand`。
- 当前 hit shape 用途：Click、Drag、Collision。
- 首批候选不增加睡眠、醒来、贴贴、群体或情侣动作。

## 4. 待样本验证的参数

以下内容必须由 AP-003 视觉方向和 AP-004 三类动作样本确定，当前不得写成既定数值：

- 正式导出像素倍数和缩小采样方式；
- 呼吸幅度、眨眼间隔和轻晃频率；
- `Landing` 压缩比例、回弹次数和事件时刻；
- `ClimbUp` 轮廓帧数量、抓边接触区域和遮挡变化；
- 每个轮廓帧是否需要独立 accessory anchor；
- 脸部是否需要独立于身体 clip 的附加轨道；
- 当前 Schema 如何表达身体调色和帽子资源集；
- 表情动画中的眼睛与嘴是否需要从当前单一 `face` slot 拆分；
- 帽子逐帧挂点如何随轮廓形变更新；
- 降低动画模式的刷新频率和次级运动删减规则。

这些问题暴露 Schema 缺口时记录为 P1-004 TODO，不在预生产中滥用字段。
