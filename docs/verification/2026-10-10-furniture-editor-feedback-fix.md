# 普通房家具编辑：四项反馈修正验证

日期：2026-10-10。状态：实现、自动回归与实际产品短验证完成；用户回复“通过”，四项反馈人工重验通过。600 秒持续观察及既有独立开放门禁仍待完成。依据[修正计划](../planning/2026-10-10-furniture-editor-feedback-fix.md)，分支 `codex/furniture-ball-room`，构建前 HEAD `e3f98213611a4f4bee45c9ebf38a16dfedcdac25`。本轮仅修正用户报告的四项问题。

## 行为与范围

- 皮球被地面盖住：`LocalRoomSceneView` 先绘制地面，再绘制家具；球的物理位置、碰撞地面和参数不变。
- 仓库双击失效：`OrdinaryRoomFurnitureEditorPanel` 移除合成事件内重复的 `ClickCount == 2` 判断。真实 WPF `MouseDoubleClick` 的新事件参数中 ClickCount 为 1，旧条件拦截了添加。仍只响应启用状态下的左键双击，生成一个待确认预览；单击和第三击不重复添加，上限仍为 30 件。
- 右侧栏遮挡房间右端：`RoomViewController` 依据侧栏实际宽度、右边距及 12 DIP 间隔调整水平可视区域和相机范围；可右键拖动镜头将房间右端移到侧栏左侧。缩放值、世界尺寸及物理边界不变；Whole、超宽、最小、竖向和 resize 均核对。退出先清除遮挡，再恢复进入编辑前的相机快照。
- 编辑中 Esc：`LocalRoomWindow` 取消未确认预览与手套／拖动／镜头捕获后退出编辑，不打开菜单、不保存、不重建世界。保存忙时拒绝退出，保存完成后再次按 Esc 可退出。普通游玩中的 Esc 菜单规则保留。

界面继续遵循桌面规范 v0.1 §§2–6：共同暖白／棕色资源、图标工具、中文提示、固定反馈及常驻细滚动条；保留三段侧栏和两列实际家具图片。本轮没有新增参数或主题。软件渲染与控件路由不替代真实硬件输入、OS DPI 和辅助设置验收。

## 能失败的自动验证

| 验证 | 结果 | 本地证据 |
|---|---|---|
| 旧仓库真实模板／底层鼠标路由复现 | 2 项失败；WPF 实际合成 ClickCount=1 | `artifacts/warehouse-input-results/warehouse-input-repro-r1.trx` |
| 旧绘制／相机／Esc 复现 | 7/7 失败 | `artifacts/furniture-feedback/results/feedback-repro-r1.trx` |
| 修正后仓库输入 | 13/13 通过，0 跳过 | `artifacts/warehouse-input-results/warehouse-input-fixed-r1.trx` |
| 修正后反馈及相机针对性 | 42 通过、15 跳过、0 失败 | `artifacts/furniture-feedback/results/feedback-fixed-r1.trx` |
| 最终受影响 Desktop 回归 | 267/267 通过，0 跳过 | `artifacts/furniture-feedback/results/feedback-regression-r1.trx` |
| 实际 Desktop 与友元工具 Release 构建 | 0 警告、0 错误 | `artifacts/furniture-collection/feedback-product-r1` |

新测试核对实际球底部渲染像素、相机投影与不变 Scale／世界、真实模板子树的底层 mouse down、实际 host/native/window 的测量接线与键盘焦点。保存忙状态用真实阻塞 writer 核对 Esc 不打断提交，提交完成后 fresh Esc 退出。15 项跳过均为既有前台／实际鼠标捕获环境门禁，仍未验证，不能因另一回归集无跳过而视为通过。回归筛选保留于 `feedback-regression-filter-r1.txt`。

## 实际候选与产品短验证

实际候选位于 `artifacts/furniture-collection/feedback-product-r1`。`source-snapshot.json` 保存构建前实际文件身份并核对构建后无漂移；SHA256 为 `FADB482818DB50E40365BE5C38A5F80E6DE019E0E4D88894BFB7D12356E31E28`。构建时 sourceDirty=true，HEAD 不代表所有已测试文件；manifest 另记录 exe、程序集和 native 的哈希。测试后仅补充交付文档，产品及测试源码未再修改。

本轮不改 native，沿用 r5 SHA256 `BA6E92D57CAFBD5CC806F709890AA1E8ED784F97CF42A09F4168EE08065E97A0`。受保护 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll` 前后仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。原 native 门禁见[集合验证记录](2026-10-10-ordinary-room-ball-collection.md)，本轮没有重复执行或冒充新的 native 验收。

`verification/verification-summary.json` 记录本候选实际 `DangoApplicationHost`／`LocalRoomWindow`、原团子和同一 native 世界的 30 件混合家具（24 平台、6 球）、80 次逐操作保存、50 次房间往返、50 次编辑往返及两个独立 OS 进程保存／加载，全部通过。实际方向输入推动皮球，中心／转角渲染与重入恢复确认锚点通过；隐藏不推进，恢复保持所有 native ID。耗时 29.6127 秒，保存提交边界 p95 6.3254 ms、最大 8.5869 ms，scheduler faults=0。正常释放 frame owner、native world 和所属窗口。

同目录保留最小 640×420、横向 1200×620、竖向 640×800、默认 960×600 的 96／144 DPI 离屏图。默认和最小图已实际查看：球底部完整绘制在地面前，三段侧栏及实际家具图片保留；最小高度的仓库需要滚动。离屏 DPI 图片不代表真实 OS 缩放或硬件操作通过。

## 人工现场与交付

新实际 `Dango.Desktop.exe --local-room-review` 已打开，初始窗口“Dango 控制中心”，PID 41216；`manual-window.json` 记录启动时间、路径、native hash、数据与窗口身份。用户于 2026-10-10 在四项修正交付后回复“通过”，记录球的前景绘制、仓库双击预览、编辑右端视角和 Esc 退出的人工重验通过。软件门禁仍按各自实际结果记录，不据人工通过改写测试跳过项。

替换前核对旧 review PID 35896 的路径、启动时间和参数，确认窗口隐藏及家具租约可独占，再仅结束该进程。旧数据逐文件 hash 前后相同，未删除任何数据。将用户的设置、窗口预设及已确认家具布局原字节复制到新候选的独立人工数据目录，保留新 owner；布局 971 字节，SHA256 `2AA5DD924F0F5973181ED393F02D067A3A37F80DC9BE3C77DC72049309005362`。证据为 `previous-review-stop-and-transfer.json`。30 件自动压力数据仍在单独 verification-data，没有带入人工场景。

600 秒有效活动观察继续待完成，本轮没有启动或要求占用电脑进行观察。四项反馈的人工操作据用户“通过”记录；真实 OS 150% DPI、高对比／减少动画、15 项前台捕获门禁及 59 FPS 均为独立开放项。RequiredActiveSeconds=0 的短验证不能报持续观察通过。

实现本地提交 `ce465c3907ef3e0fb1076fb3587b6baf23894667`；本次仅更新人工验收记录，产品及测试源码未变。交付仅隔离本地候选及本地提交，未合并 main、未推送、未安装、未部署、未替换受保护 DLL。所有自动 verifier 及重启子进程已退出，仅本轮人工候选保持运行；无网络服务、端口或数据库操作。四项反馈人工重验已通过，完整家具批次因独立开放门禁保留而未标记全部验收。
