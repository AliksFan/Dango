# 双人 P1-W：独立 WPF 语义呈现

日期：2026-10-11。依据用户批准的[Windows先行计划](../planning/2026-10-11-dual-player-windows-first-plan.md)，W0顺序修订先提交于`a9e6845`，再实施W1–W4。**九组正常WPF轨迹、四个指定错误变体及真实窗口短验证通过；人工和600秒持续观察尚未完成，P1-W未标记准入。** Linux仍未验证，Docker排障保持停止，不阻断后续满足Windows／后端条件的工作。

## 1. 范围与实际来源

新增独立`eng/tools/Dango.SemanticPresentationReview`，未修改生产窗口、Core、Physics、solver、ABI或参数，不接网络／账号／数据库。两视角分别拥有sampler、native replica、adapter、实际DeformableDangoVisual，仅相机水平位置不同；不是复制同一局部帧。原R1三份trace只按20Hz提取中立语义，实际局部呈现各自60Hz固定步。

模块硬锁原R1 SHA `59EC861EE748BA93E661D058EEBD01D3E8A04ABF9D24F0376BACB4D98015B6DC`。push／stack／withdraw的60Hz原证据SHA分别为`7D4178BAE57C38090924D012E479FD701DB558695BC4B038ED5A922F777FEFD8`、`3EF3C24E6FB81A96E32FC0620A1EDA727494094122550D501CBB62B896A224DD`、`02F6E209E095185B86CD36DA1F597368D08DE9BE67F0BF51E5E661B064BBE8F4`。载入时检查原成功报告、完整600步及来源对应，执行前后复核全部输入hash。

实际候选工具DLL SHA `72ED4FB699BCBB6EEE13A4BA49502164448F6713105408B3443603A451983B27`。证据根`artifacts/p1-windows-review/`，保护A／authority／daily／B replay／R1五项产物全部匹配既有冻结hash，见`protected-current.json`。

## 2. 自动与真实 WPF 几何

Release构建零warning／error，PS脚本解析、diff检查通过。真实WPF BodyPath经ApplyFrame后，从其实际quadratic路径和到scene的ancestor transform独立采样，不用第二次geometry factory或已缓存的物理几何冒充绘制。保持160×120、原点(80,66)、gait=null、原`.98`限制及原形态参数。

三场景各配30/60、60/144、144/30两实例显示调度，**9／9完整通过，每实例600相邻固定步，共10800步**。不同显示调度次数是离屏调度证据，不是应用FPS。最大实际中心误差`9.9920e-16 p`、实际曲线映射误差`1.3323e-15 p`、SAT侵入比`.0300103081≤.031`、支撑正gap为0。共同采样中心、身份和支撑一致。

堆叠／撤支撑每实例均经历246个真实支撑步；撤支撑后每实例420步仅保留幸存实体，native local ID未改变，旧visual、pair、start/end support清除并自然下落。三种布局960×620、1200×760、960×1100各留图，共27张；已核对横向推挤与纵向堆叠图。96DPI离屏图片不代表真实OS DPI、前台或辅助设置验收。

四种错误实现实际exit1，指定oracle失败，未用异常或未启动冒充红灯：

| 错误变体 | 指定失败 |
|---|---|
| Top减60，绘制原点错6DIP | actual-authority-center |
| 两视角共用同一replica | replica-ownership |
| 第120步显示fallback | actual-fallback |
| 撤下层后残留旧visual | visual-membership |

`suite-r2/manifest.json`及7个真实进程回执：正常3进程约28.9／33.7／12.9秒，反例约1.0–5.5秒，全部在未改变的60秒期限内退出，native allocation均`0→0`，全部输入hash一致。外部verifier复核9个schedule pair、步骤、阈值、实际PID／exit、指定oracle及布局产物数量。

首轮`normal-r1/`把9组集中到一进程，在7组完成后触发60秒期限，真实退出`-1`，未计整批通过或推定native正常释放。根因是批量几何／native工作超过单进程期限；保留21张图、7组进度和`normal-r1-process.json`。随后按场景分进程，未扩大期限、减少步骤或放宽阈值；最终新`r2`包含地面细线及记录完善，所有正常／反例重跑。

## 3. 实际可见窗口短验证

`--window-check`运行与人工候选相同的管线，依真实WPF Rendering回调依次执行三个场景，各两实例600步，共3600步。实际PID39904，exit0，约34.7秒进程生命周期、33.087秒可见且有焦点的活动、1034次实际回调、0次最小化挂起。两实例实际几何和共同时间检查通过；正常关闭、输入hash一致、native allocation `0→0`。证据为`window-r2/`、`window-r2-process.json`及`window-summary.json`。

窗口为隔离语义重放，UI负载时最多补4步；本次丢弃约3.014秒额外回放时钟积累，保持所有600步，未跳过轨迹。该行为不作为服务器实时调度／59FPS证明。回调不冒充compositor Present，33秒不冒充600秒持续观察。

## 4. 界面、人工与交付

遵循桌面规范v0.1 §§2–6：Home刷子直接读取既有首页XAML资源、暖白／棕色细强调、少字、160×120原团子、三个场景按钮及暂停／重放图标加短文字、中文提示及辅助名称，错误文字保留。技术窗复用WPF基础按钮布局；共同生产控件模板尚未迁移，不在本轮批量改UI。无编辑草稿、保存或滚动内容，因此这些合同不适用。高对比启动使用系统刷子，但真实OS DPI／高对比切换／减少动画矩阵未验证。

人工候选将以`--review`打开同一build，真实启动身份另保留`manual-window.json`，有意留到用户关闭。待人工观察：推挤接触、堆叠是否贴合、撤下层后自然下落、两个视角一致、暂停／继续及重放。600秒持续观察、正式账号／Hub／持久接线、真实多客户端／双机和Linux仍未验证；前两者人工项完成前P1-W不宣称准入。

本轮本地提交，未合回main、推送、安装、部署、启动Docker或替换生产DLL。7个正常／反例子进程及短验证进程已退出，首轮超时进程已由自有60秒边界结束；无任务网络服务／监听／数据库资源。旧数据、证据和工作树保留，人工候选单独列为唯一有意保留进程。
