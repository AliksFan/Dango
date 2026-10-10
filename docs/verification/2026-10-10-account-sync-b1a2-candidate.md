# B1a2 导航修复候选与离线验证

日期：2026-10-10。状态：导航及测试资源边界已修复，Release 与离线回归通过；新的真实 PostgreSQL／HTTPS／完整 AppHost 旅程尚未执行，等待[精确 B1a2 计划](../planning/2026-10-10-account-sync-b1a2-local-plan.md)批准。B1a 原额度已消耗，不能重跑或补查，失败证据见[原记录](2026-10-10-account-sync-b1a-navigation-failure.md)。

## 修复与证据边界

新增无 WPF／数据库依赖的 `AccountSyncAppearanceNavigator`，正式旅程和独立窗口测试通过同一源码使用实际 UIA 动作与页面状态。换装已打开时先点击收起按钮，等待首页入口可用，再分步 Face、Eyes、Range。已打开捏脸时复用编辑器；Apply、Cancel、Close 分别等待真实保存／草稿／页面结果；相同值不点禁用的保存按钮，非法参数及账号／设置／门廊／团子离开等阻挡状态在输入前拒绝。

实际 UIA 同时检查 owned HWND／进程、启用和非屏幕外状态。每个 AppHost 最多 128 份非秘密页面状态证据，不记录输入或秘密，不操作任务外窗口。正式产品代码、AutomationId 和账号／同步业务没有为导航修复改变。

独立窗口先实际点击 Wardrobe，再打开和关闭账号；换装保留、Launchers 折叠、旧 FaceButton 查找实际失败。新 helper 随后通过真实 UIA 关闭换装、打开捏脸并保存 111，真实文件重读一致；动画开关两种模式均完成 Apply／Cancel／Close，草稿及取消不改原字节。这个反例证明旧 helper 的导航前提错误，**仍不能解释 B1a 最初进入换装的来源**。

唯一 UIA worker 被跟踪；超时后保留任务、拒绝重入，关闭窗口后有界排空并观察异常。新增真实窗口故障例令 worker 超过短等待，在 HWND 已销毁后才放行，晚到 Invoke／Range 均被拒绝，Dispose 必须等 worker 完成；实际证据的 HwndClosed、UiaWorkerSettled 为 true，异常已观察，晚到 UIA trace 为 0。构造初始化失败也还原 SynchronizationContext。

正式旅程、构造异常及每项 Dispose 失败均首先关闭业务预算，再独立尝试其余收尾。未捕获 HWND 的路径明确不能证明窗口关闭；不以 IsWindow(0)=false 报告成功。DesktopDisposed 严格新增 UiaWorkerSettled=true 门禁，三个宿主分别校验；缺失、false、错大小写及字符串假 true 均拒绝。运行器独立记录 UIA worker 收尾汇总，部分失败不会当作完整通过。

## 根实际验证

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 最终 Release 预算／请求／archive／新批准隔离 | 100 Passed、0 Failed、0 Skipped，零编译警告／错误 | `artifacts/account-sync-b1a2-offline-root/account-sync-b1a2-offline-final.trx` |
| 独立 Windows HWND 导航、账号 UIA 及完整宿主接线联合 | 27 Passed、0 Failed、0 Skipped（新导航16、既有账号4、宿主接线7） | `artifacts/account-sync-b1a2-offline-root/desktop/account-sync-b1a2-desktop-final.trx` |
| 固定 stage／旧批准拒绝／冻结／图保护／资源 schema | 406/406，真实资源 0 | `artifacts/account-sync-b1a2-offline/e5520edd10bc40da9ce5a864ae231da8/offline-results.json` |
| 实际提取正式 runner 函数离线调用 | 40/40，真实资源 0 | `artifacts/account-sync-b1a2-runner-offline-20261010/result-b6293554ed514903a78b8cd5c702d1e6.json` |
| 共享 Profile helper／22表及配饰保护／diagnostic 回归 | 237/237、105/105、96/96 | 本轮各自 `artifacts/account-profile-offline/`、`protected-graph-offline/`、`diagnostic-offline/` 回执 |
| 最终 candidate 给旧 B1a／Profile grant=1，移除新 grant 和其余 DB 环境后调用唯一真实旅程 | 1 NotExecuted、0 Passed、0 Failed，资源初始化前拒绝 | `artifacts/account-sync-b1a2-offline-root/approval-closed/account-sync-b1a2-old-grants-closed-final.trx` |

406 项回执 SHA256 `B1D981B8F996571494EAE5623B676199E9BE616C5930AE83E2EF56C638BCCC47`；40 项 SHA256 `F9DA29C4584FA15E08771A7B897A08B91F951E76835D570951AF34BA21F20481`。最终100项 TRX SHA256 `66FB213DABE33764A9681EA5107F0E0030F854D0AD1A495FFE156DEADFF952F7`；关闭新批准的 TRX SHA256 `2D83597D99B9CE551A1B949745557096956465668A279C8D57B1A6A455A4BB27`。

代理独立导航16/16证据：`artifacts/appearance-window-uia-build/b1e33e2b64184484a73154d75aaffa8b/results/appearance-uia.trx`；该轮6个窗口及worker均收尾。故障投影为 `artifacts/appearance-window-uia/25e0f2e2de574ea09d4b6bbf53b3871b/uia-transitions.json`。这些是 standalone WPF 窗口、受控账号及未 Start 的宿主接线测试，没有启动新的 native／正式 HTTPS／数据库旅程。

最初候选构建因新增测试对两个编译期常量作恒真断言而被 MSTEST0032 拒绝，未运行测试。移除这两条无意义断言，保留真实批准 reader／initializer 拒绝测试；未抑制分析器。随后100项通过；独立审查新增收尾修复后最终候选重建并再次100项通过。一次回归命令使用不存在的脚本名，未运行检查；改为仓库实际 `test-account-profile-protected-graph-boundary.ps1` 后105项通过。

## 新旧资源与交付

B1a2 固定 stage、grant、candidate、TRX 与一次性 run 目录均独立。旧 B1a stage、grant、run／candidate／TRX 或清单即使提供匹配旧摘要也被拒绝。原 B1a 固定清单 SHA256 仍为 `6E6FCF13A1B108B9C08A4FD90208D4EBDF66E46F8AF3E73FD43270CE3AED2D50`，原计划 SHA256 仍为 `689158735FCBAF662E9D041E1BF26343142AE76B35CFE42186BE30ECC80BFB1D`，原候选 DLL SHA256 仍为 `FCE5995F9BB07489D07B1829713DB9E933F2F3406A24CA2D46C3C7EA09754919`。

新旅程仍只有19次最大业务尝试、1新账号注册图、4会话、1 HTTPS Host／临时证书、3先后完整 AppHost、最多10个工具；零迁移、邮件、系统信任或 WinCred。新的before保护全部现存22表旧行，包括原B1a失败留下的数据。新资源目录未预留，未读取原凭据或密钥内容、连接／启动数据库或生成新证书；只准备本地源码、候选与固定清单。

新固定清单 `artifacts/account-sync-b1a2-approved.json` 已 CreateNew 冻结222项输入，缺失0；SHA256 `AECA0FCBC2BAF55EB6E8B2D9888AF32B93B3624131999EEF42B055ED220F4102`。根正式批准校验函数再次逐项确认全部匹配，新精确计划 SHA256 `B50B74F640F5850BAF3269E949F5A698138374F903D430D0CC03FA45D3351E25`。批准后不改源码或重建候选。

独立只读终审无剩余阻挡项；冻结审计222文件全部存在、hash差异0，输入集合完全一致，manifest六字段与Plan33字段严格匹配，清单及计划核验前后hash不变。投影 `artifacts/account-sync-b1a2-result/38f5506ba44f4258a269cf6b398eeedc/independent-freeze-audit.json`，SHA256 `3B1D0D3A61E5A934049FEFF7A5C0A32E41189A3673A24827393B3BEA5FBB21BD`；未预留目录或读取原DB／秘密。

完整端到端、同时多设备冲突、服务重启、异常恢复、门廊业务和用户主观验收仍未通过。未合并 main、push、部署、更新日常安装、替换生产 DLL 或删除持久数据。
