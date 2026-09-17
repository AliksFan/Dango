# 隔离桌面外形评审

> 2026-09-17 历史资料归档：本 README 保留原评审工具的使用说明；对应项目与源码仍留在原工作树未提交。下面命令要求该历史实验源码和显式输入产物，不是当前已验收宿主的构建或运行前置。

## 动态评审模式

用户批准[动态计划](../../../docs/phase-1-dango-dynamic-review-plan.md)后，新增独立实时模式：

```powershell
$review = Start-Process -FilePath eng/tools/Dango.ShapeReview/bin/Release/net10.0-windows/Dango.ShapeReview.exe -ArgumentList --dynamic,artifacts/native-spike/box2d-build-Release/Release/dango_self_sleep_tests.exe -PassThru
```

窗口启动自己的原生测试子进程，真实推进唯一 Box2D world，不加载或替换正式候选 DLL。使用 A/D/S/W 和鼠标；失焦清除输入，故障保持锁存，只能显式重置。无左右墙，移出画面可重置；默认放大 2 倍，物理参数和横纵比例不变。关闭窗口结束所拥有的子进程，不保存配置或验收结论。

进程级自动验证：

```powershell
$check = Start-Process -FilePath eng/tools/Dango.ShapeReview/bin/Release/net10.0-windows/Dango.ShapeReview.exe -ArgumentList --verify-dynamic,artifacts/native-spike/box2d-build-Release/Release/dango_self_sleep_tests.exe,artifacts/p2d-007c/dynamic-review -WindowStyle Hidden -Wait -PassThru
$check.ExitCode
```

结果包含动作/协议测试记录及实际 WPF 离屏图。测试模式故障注入不在普通交互入口开放。真人操作与手感须另行反馈。

## 静态评审模式（已获用户接受）

这是用户批准提前进行的**静态外形评审**，不是完整功能验收。

工具不创建物理 world，不修改控制中心设置，不替换正式 DLL，不保存验收结论。
用生产 `DangoStreamGeometryFactory` 对原生模拟快照构造真正的 WPF 曲线。
原始节点按 native 规则径向外扩 0.045，再应用 presentationScale 与 160×120 DIP 显示映射；窗口中的放大始终等比。
只绘制身体轮廓，没有眼睛/装饰；不能用此工具验收拖动、碰撞或动态休眠。

## 准备与运行

从仓库根目录构建原生 `dango_self_sleep_tests`，运行 `shape-isolation` 并保存标准输出到 `artifacts/p2d-007c/shape-review-snapshots.txt`。
必须包含 production 与 ground_sleep 的 900、2085、6000、36000 帧各 16 节点；缺失、重复、非有限坐标及非法轮廓必须拒绝。

```powershell
dotnet build eng/tools/Dango.ShapeReview/Dango.ShapeReview.csproj -c Release
$review = Start-Process -FilePath eng/tools/Dango.ShapeReview/bin/Release/net10.0-windows/Dango.ShapeReview.exe -ArgumentList artifacts/p2d-007c/shape-review-snapshots.txt -PassThru
```

离线渲染检查（不显示窗口）：

```powershell
$check = Start-Process -FilePath eng/tools/Dango.ShapeReview/bin/Release/net10.0-windows/Dango.ShapeReview.exe -ArgumentList artifacts/p2d-007c/shape-review-snapshots.txt,--verify,artifacts/p2d-007c/shape-review -WindowStyle Hidden -Wait -PassThru
$check.ExitCode
```

输出包含 8 个真实 WPF 轮廓的宽高面积、24 种布局状态记录及 `shape-review-render.png`。
布局调用成功不能替代逐状态视觉验收；图是离屏 WPF 输出，不是桌面屏幕截图。

## 用户评审清单

1. 默认第 36000 帧、1×，看原尺寸下候选顶部、两侧、底部是否有不能接受的差异。
2. 切到 3× 和重叠查看，确认差异部位；重叠实线为原实现，虚线为候选。
3. 依次查看第 900、2085、6000 帧，检查是否只在某一时刻有明显变形。
4. 在 Codex 中回复“外形可接受”，或说明不可接受的时刻和部位。关闭窗口不表示批准。

当前外形数值筛查仍失败并保留；本次人工意见用于决定后续标准或修复方向，不自动豁免任何物理门禁，不意味着左右墙、多体承重、正式宿主集成已完成。
