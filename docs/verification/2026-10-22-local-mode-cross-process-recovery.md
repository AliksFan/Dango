# 新身体普通房跨进程恢复验证

日期：2026-10-22。用户已明确开始本地收口清单中的恢复补验。本轮在 `codex/local-mode-closeout` 增补独立验证工具，生产代码不变；最终R5写入、独立进程恢复与越界回退三阶段工程验证通过，真实应用壳层／输入及人工体验保持未验证。

## 范围与方法

复用正式内部候选 `local-closeout-feedback-20261022-v1`（生产源码 `90e974353d9e2daf28c3c62f7873b720fb93be93`，`sourceDirty=false`）。探针的包引用模式直接使用包内Desktop、Core、Physics、Protocol程序集，以及两个Belt模块和native manifest；运行器核对这7份文件逐字节哈希相同，并核对当前生产源码与包来源一致。

使用现有 `Dango.LocalRoomProbe` 的专属新建目录及所有权标记。`recovery-write`、`recovery-read`、`recovery-unsafe`分别是顺序启动、退出的独立OS进程；只操作本轮虚构档案。未启动账号、服务或数据库，没有改系统网络设置。

实际使用产品 `AppearanceSettingsStore`、`AccessorySettingsStore`、`LocalRoomCatalogStore`、`RoomResumeStore`、`StackedDangoSceneRuntime`、`LocalRoomSceneDriver` 和 `LocalRoomSceneView`。隐藏HwndSource的WPF可见性条件有效，但Win32窗口始终不可见；真实Belt模块推进，实际离屏渲染后才取得健康呈现确认。手动帧时钟只用于工程验证，不作为墙钟性能测量或真实OS键盘输入。

本轮不启动普通应用壳层或托盘；不把组件组合链等同于完整独立EXE菜单、首屏和真实用户输入验收。原 `ui-check` 会显式显示产品窗口，未将其作为隐藏后台验证运行。

## 实际结果

开发R4的写入、重启读取与越界回退三阶段通过；两个真实写入检查点分别为 `1072.615033686161`、`1087.378687798977`，新进程恢复相同位置。最终源码复验结果见末节。

| 项目 | 检查与结果 |
|---|---|
| 外观与配饰 | 非默认身体／眼睛颜色、四项眼睛控制及110／50／50身体参数，从真实存储读取并应用到新Belt运行实例；导入的8×8透明虚构PNG有独立内部资产，实际房间配饰帧使用同一图片引用和8×8纹理。清单、资产及外观原字节核对一致。 |
| 两间房隔离 | 两房使用不同稳定身份、家具实例ID、平台／球锚点与检查点。P1通过真实driver推进及局部方向输入移动身体，捕获已确认地面检查点与当前镜头后调用产品store保存。P1退出后P2核对同一档案、房间、布局版本、逐项家具及恢复文件。 |
| 原生与帧owner | 每房写入有148个实际native成功步；读取与回退每房有14个实际成功步，native fault均为0。验证实际driver就绪、原生步数与唯一scheduler成功步数一致；释放driver后帧owner为空且运行已停止。 |
| 镜头 | 甲房Manual／Custom／120%，手动位置400／40完整恢复；乙房EdgeFollow／Custom／105%恢复模式和请求视距，跟随坐标按恢复后的身体重新计算，符合现有合同。 |
| 球回锚点 | P1球从150／190高度实际落到约66，作为瞬态反例；P2初始真实native球位于保存锚点附近（首步重力位移小于1逻辑单位），明确没有继承P1末态球位置。布局文件的锚点保持不变。 |
| 无效检查点 | 第三进程仅把甲房虚构检查点改为X=-500；真实BindRoom及driver拒绝该点并产生ResumeWarning，实际身体回到合法出生位置1080。乙房仍恢复原值。 |
| 文件保护与释放 | 每个rig完整释放后，再核对resume原字节和布局哈希；读取阶段最终核对外观、配饰清单及PNG哈希。保护DLL不变，自有子进程全部退出，窗口不显示，虚构数据和失败日志保留。 |

恢复合同仍是安全逻辑地面检查点和每房本机镜头。速度、抓取、睡眠阶段及球瞬态不是本轮重启存档，未要求恢复整个物理世界。

## 构建、失败和审查

- 包引用探针及原项目引用路径均已构建成功，0警告、0错误。本轮没有使用NU1903命令例外；未经过多人样本的构建不替代完整Desktop门禁。
- 初次编译发现测试工具可空引用，已按实际空值分支修正；开发R1眼睛高度／间距不在已批准取值范围内，R2测试PNG缺透明像素，均修正虚构输入，不改变产品校验。
- R3写入成功，读取在家具尚未附着时取球失败；已按真实scheduler首回调建立时钟的合同，等待实际Prepare附着家具，再读取首次球状态，保留原锚点与瞬态反例断言。
- 独立只读审查提出两项结果漏洞：原件检查必须位于rig释放之后；summary须在进程释放与保护DLL最终核验后确定passed。均已修正并复核关闭，未删除失败数据。
- 数据与日志分别在 `artifacts/local-room-catalog/runs/recovery-<RunName>/` 和 `artifacts/local-mode-closeout/recovery-<RunName>/`；R1～R4均保留。验证工具源码和包二进制哈希绑定于各轮summary。

## 与配饰清晰度的关系

用户询问的内置配饰清晰度修订仍在当前主线和候选中：`BuiltinAccessoryImages.TextureEdge=512`，沿用512×512内置纹理和按实际DPI取样的图库缩略图；本轮生产配饰代码与候选来源相同。上述8×8测试PNG只验证保存／恢复和贴图接线，不作为清晰度或人工观感证据。

## 当前边界

当前覆盖真实文件、独立进程、正式包程序集／DLL、隐藏WPF产品房间driver和新内核的工程组合链。正常应用壳层下的完整独立EXE交互、持续推球／平台、桌宠鼠标命中与穿透及配饰／QQ实际观感继续按后续屏幕授权和人工体验记录。竖屏暂缓及DPI／多屏等既定延期不重开。

本轮不改判整个本地模式或Phase 1验收；Schema1历史保存UNKNOWN及完整Desktop的NU1903风险继续保留。没有合main、推送、日常安装更新、部署或删除原工作树／存档／包。

## 最终源码复验R5

验证工具提交 `9066247`；工作树干净时执行 `eng/run-local-room-recovery-check.ps1 -RunName r5-final -PackagePath <本工作树的正式候选包目录>`。源哈希前后相同，并在结束后与当前4份验证源码重新核对一致；本轮生产源码始终与原候选包相同，无需因工具／文档提交重建产品包。

- [最终机器摘要](../../artifacts/local-mode-closeout/recovery-r5-final/summary.json)：`passed=true`、`processesReleased=true`、`protectedDllUnchanged=true`；包引用构建0警告／错误，7份包二进制绑定通过。
- 独立写入PID29460、读取PID28396、越界回退PID13108均已正常退出，退出码0；初始化进程同样退出。每个新阶段均在前一进程结束后开始。
- [实际恢复结果](../../artifacts/local-room-catalog/runs/recovery-r5-final/reader-result.json)与[越界回退结果](../../artifacts/local-room-catalog/runs/recovery-r5-final/unsafe-result.json)覆盖上表全部断言。甲乙房真实检查点仍为1072.615033686161／1087.378687798977，回退甲房为1080；原生fault均为0、隐藏条件成立。
- 暂无本轮测试窗口、键鼠占用、自有探针进程或服务端资源残留；所有虚构数据、R1～R5证据及原候选包保留。main仍为`ca9a3ab`，原AGENTS用户修改及保护DLL哈希保持不变。
