# DIY 配饰第一版本地 main 整合

日期：2026-10-09。第一版人工验收通过，已按授权完成本地 main 整合；原自动差异与未验证边界保留。

## 授权与整合范围

用户明确：“那配饰模块的第一版设计就到这里吧，人工验收通过，合并到 main”。Windows 本地配饰第一版设计收口、人工验收通过；本轮允许本地提交、解决整合冲突和合入本地 main。分享、配饰间挂接、云端、手机端和多人仍为后续范围。

整合前 main 为 `1c47930251c82652df72506554305dd783493816`，配饰来源为 `codex/accessory-diy@b029c3a366b8abe6e2c1677741a020b777626783`；双方从 `8ff78e03d6695d90cd1d9d5e9c5eaad9883fc491` 分叉。源分支先归档第四／五轮已验收工作，工作区干净；在 main 基线上建立隔离整合 Worktree，普通合并保留两边历史及现有桌宠房间成果。

两处文本冲突分别为 `App.xaml.cs` 和开发计划。App 同时保留配饰与桌宠房间的 review 选择、独立实例名、启动构造参数及桌宠入房；入口测试增加两种模式互斥检查。开发计划同时保留两项人工确认和历史记录。

main 新增的独立 `DesktopPetDangoWindow` 没有将 runtime 传给配饰绑定，绘制范围及 native region 又仅收集 Path，造成配饰不显示或被窗口裁剪。本轮只补同一 runtime 的渲染接线及配饰网格显示范围。完整显示 footprint 独立用于 HWND，Scene 的 painted bounds 仍只含原实际 Path，避免大配饰改变身体拖动钳制；身体轮廓命中、点击／拖动、碰撞和唯一物理 owner 合同保持不变。专项红绿证据随最终结果列明。

## 验证

证据保留于 ignored 的 `artifacts/accessory-v1-main-verification`，桌宠专项另有独立输出目录。

- 初始完整整合树 Release 构建通过，0 警告、0 错误；日志 `initial-release-build.log`。
- 首次共同回归 `integration-initial.trx`：596 通过、18 失败、15 未验证，共 629 项。其中 14 失败为新 Worktree 缺少两份 ignored native 夹具或 `DANGO_PHYSICS_BASELINE_PATH`；补齐原 SHA-256 输入后，低跳／故障恢复／旧模块能力定向组 `native-fixture-rerun.trx` 16/16 通过、零跳过，不排除检查或更改断言。
- 其余 4 失败是普通房壁纸轮滚、轻抚、回切及后台全屏扫描的真实 Shell／房间前台前置条件未成立；15 未验证是普通房右键原生前台／捕获前置条件未成立。原 TRX 和诊断保留，不隐藏外部窗口、不替换为模拟结果，不宣称此环境专项通过。
- 桌宠专项公平反例 `desktop-pet-accessory-before-r4.trx`：4 项预期失败、1 项原正例通过；明确击穿缺失 runtime、动态 HWND 裁剪及动态／固定视口 native region 漏图片。初期夹具把内置逻辑尺寸设为 512、以及把 bitmap 透明容量当实际材料范围的无效失败另有记录，不将它们作为产品缺陷证据。
- 最终每件一个包围四边形的实现 `desktop-pet-accessory-after-r2.trx`：5/5、零跳过。真实离屏 owned HWND 验证前后层、opaque 图片像素可见、摘下清除、旧帽恢复、身体外 `HTTRANSPARENT`、身体命中／拖动范围和实体／物理帧／计数／owner 独立；没有逐三角形创建 native region 的额外开销。
- 修复后的完整整合树 Release 构建通过，0 警告、0 错误；日志 `final-release-build.log`。最终宿主、入口、低跳、旧模块与普通房外观／往返回归 `final-host-regression.trx` 122/122、零跳过，包含最终桌宠配饰专项；未再重复运行源码未受宿主修复影响且首次已通过的图片／材料等组。
- 全 78 个变更 C# 文件空白格式只读检查通过，`format-whitespace-final.log`；首轮新测试 6 处排版差异及修正前日志保留。最终 `git diff --check` 通过；12 份变更文档的 312 个相对链接目标全部存在，`doc-links-final.json`。

候选 native 仍使用已验证低跳模块，SHA-256 `B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E`；低跳前轨迹夹具为 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`。旧模块输入取已归档的 gait 本地交付包，SHA-256 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`，仅作只读测试输入。两份隔离夹具复制到新 Worktree 的 ignored 预期位置，无覆盖已有不同内容，证据 `native-fixture-inputs.json`。受保护 `dango_box2d.dll` 保持 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。

## 人工与未验证边界

第一版人工验收引用用户本次明确确认；最终材料、翻折与两上限的自动／真实进程证据见[第五轮记录](2026-10-09-accessory-material-v5.md#2026-10-09-第一版人工通过与本地整合授权)。原验收构建和 owned 设置／图片资产继续保留，本轮没有操作原验收窗口；最终盘点原 PID 34672 已不在运行，原工作区与整合工作区均无 Dango 应用进程，无新增服务或端口。测试子进程正常退出，ignored 数据及失败证据保留。本轮不以离屏 WPF 或 owned HWND 门禁替代真实硬件点击路由、跨进程穿透、人工观感、全部 DPI、多屏和长期稳定性。

第四轮 59 FPS 红灯、第五轮曾出现的 native 位移前置条件失败、第二进程 UIA 入口观察超时继续保留；后续通过结果不改写原失败，也不将未确定根因写成已解决。新材料主动改变配饰动态，旧 930 帧完整配饰输出相等不作为本次通过证据。普通房壁纸层继续搁置，完整 Windows／正式房间／多人阶段仍开放。

## 交付状态

已在隔离 Worktree 创建以原 main 为第一父、配饰来源提交为第二父的普通合并提交，并将本地 main 快进至该验证过的整合提交；保留双方历史，源码整合不覆盖日常安装。main 与源分支工作区干净，受保护 DLL hash 不变。

未推送、更新日常安装、部署或替换正式 DLL，没有删除分支、Worktree 或测试数据。源分支、验收构建、设置及历史失败证据保留。本次是配饰 Windows 本地第一版收口与本地源码交付，不宣布完整 Windows／正式房间／多人阶段完成。
