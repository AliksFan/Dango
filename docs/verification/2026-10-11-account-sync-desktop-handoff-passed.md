# 完整桌面宿主捏脸同步与换号门禁通过

日期：2026-10-11。按[无数据库计划](../planning/2026-10-11-account-sync-desktop-handoff-offline-plan.md)完成三项真实 AppHost／HWND／独立 UIA 旅程及相关账号回归。正式 HTTP 的 Handler 和 secret store 是受控夹具，本记录证明完整客户端接线与生命周期隔离，不表示本轮 HTTPS／PostgreSQL 端到端验证或用户主观验收通过。

## 实际结果

最终源码为 `tests/Dango.Desktop.Tests/AppShell/HostAccountUiaHandoffTests.cs`，SHA256 `A8E19B5CB45E0CB441DFA9697E0386C040692D7E8B5E62AA5DC5363FE7493B1D`。使用生产 `DangoApplicationHost.CreateForAccountIntegration` 和 `Start()`，真正 native 场景、健康物理帧与 `CompositionTarget.Rendering`，独立线程 UIA 及真实配置文件／journal。捏脸通过实际 Apply 触发 SavedToDisk，远端读取由生产两秒 DispatcherTimer 驱动，没有手动 Pump／Notify 代替这些接线。

| 固定旅程 | 实际结果 | HTTP／本机成功保存 | 耗时 |
| --- | --- | --- | --- |
| 首次设置昵称，关闭账号浮层后成功保存111；timer采用远端118；配置版本1→2→3、上传仅一次、远端应用不回传 | Passed | 13／1 | 11.1975732秒 |
| A实际timer GET保留回包，UIA注销及离线保存118，再登录B；等待A原pump结束后比较B，明确采用云端103，成功保存125只上传给B | Passed | 19／2 | 9.6924782秒 |
| A成功保存111的PATCH已在受控服务端提交，再保留ACK；注销／换号后ACK不能成为B基线或回执；B选择云端并保存125，A旧意图不复活 | Passed | 21／3 | 8.2356341秒 |

两项异常旅程都记录真实请求到达、取消、释放、returned／settled，旧 pump 的 Task 保持在途直到回包。B身份和昵称可以先加载，但 ConfigurationReady 仍为 false；其实际配置比较 GET 必须在旧 pump 完成之后发送。逐份检查整份配置、原始文件字节、UserId／PetId／SessionId、expectedVersion、档案与回执，以及 A 后续零 HTTP 请求。来源未选时两项单选均未选、确认按钮禁用；实际 UIA 选择云端后才启用并确认。

A的 Logout 排在被保留的 HTTP 后，关闭账号面板取消尚未发送的注销。因此两项旅程的 A Logout 实际接收数为0；测试检查实际清除记住登录材料并重新登录，不将本机退出写成服务端会话撤销。晚到 PATCH 保留已提交的A配置及原档，但不能伪造已确认成功回执。

受影响的 HostAccountBinding、AccountSyncHandoffBoundary、AccountWindowUia、AccountIntegrationBoundary 四组回归为35个精确方法、41个展开结果，全部 Passed。固定 FQN 等值 OR 过滤后，TRX 逐条核对方法集合、DataRow multiplicity 与执行ID去重，零失败／跳过。

## 失败及修正

第一目录 `f4d9510564c6411ea9570245e17bd166` 在构建中被 MSTEST0017 拦截，新测试断言把名为 expected 的变量放在 actual 参数位置；没有运行桌面旅程。修正只调整等值断言参数顺序，没有抑制检查器或改动CAS条件。

第二目录 `7c76723345374476971fc7c08a32f39e` 构建通过，三项旅程为1 Passed／2 Failed。两项异常都停在“来源未选但确认按钮应可用”的错误测试预期。保存证据证明旧 pump 已结束，B实际比较 GET 的 PreviousPumpSettled 为 true；生产 AccountPanelView 在 choice 为空时禁用确认按钮。修正测试等待该真实状态，再新增独立 UIA 未选／已选和按钮禁用／启用断言。没有修改产品逻辑或删掉隔离、字节、版本、零重放断言。

上述失败目录和资源回执均保留。最终独立目录 `f274750a9fce493180e1611ca9dbf6ba` 重建并执行固定三项，运行器退出0，输出 `desktop_gate_three_full_host_cases_passed`。回归使用同一最终DLL，在新目录 `regressions-e8aeb865dac7446883a7a516667cea1a` 执行一次并退出0，输出 `desktop_35_methods_41_regressions_passed`。两处目录都位于 `artifacts/account-sync-desktop-handoff/`，不覆盖前次编译物或证据。

## 资源、视觉证据及范围

三份旅程证据均 JourneyPassed=true、Failures=[]，分别确认自有 HWND 消失、host Dispose、native runtime及所有被观察的host timers停止、frame driver释放、UIA／pump／HTTP Task排空、账号操作semaphore释放、coordinator处于Stopped。账号操作排空在内存秘密存储Dispose之前完成。三个工具（最终build、新旅程vstest、回归vstest）的有界双流EOF、输出资源／句柄和process Dispose均确认，未超时。

生产 `DisposeAfterPumpAsync` 是未暴露的 fire-and-forget Task；本轮只检查原 pump 和 Stopped coordinator，不宣称直接 await 该Task或所有生产异步任务。共享过程helper未返回stderr全文，仍检查其Task／EOF和关闭状态；stdout、TRX与资源回执保留。

每例两张640×360自有 WPF 窗口PNG，CreateNew落盘，边长／像素有硬限。已实际查看最终来源选择页与换号后的首页：本机118与账号103的区别可见，两项未选，首页显示B昵称；操作区保持在窗口内、采用有界滚动。这是视觉证据，不能替代用户对手感、效果和流畅度的主观认可；没有新增多DPI或长期稳定性结论。

隔离 native 模块SHA256为 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`，加载前逐例核对，无假场景回退。生产 `dango_box2d.dll` 本轮核对仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。

| 保存证据 | SHA256 |
| --- | --- |
| 最终desktop-handoff.trx | `1D405539D3FF18B431E93611CFEC6F4D05E8E6AA2ABE21B4621540F175309561` |
| 最终build.resource-receipt.json | `28E41FF25DD07A40FB766D0D4F4EF8D4627CE8E9D0C9A80D238A6F2707D2823F` |
| 最终new-tests.resource-receipt.json | `F058AB44C669343F789557938E4A9AE454444146FBBA60348BF5CD81EA52D02E` |
| desktop-regressions.trx | `81F932D471189D9CCD9FC73E7670D39CB56BA4B325EF55A6A42A0AD868C43731` |

另一代理只读复核保存证据，335/335断言通过、blockers为空，重新计算44个文件摘要并核对7份原档、6份回执、6张PNG及35方法／41展开结果。审计没有启动资源、探测网络／数据库或重复测试。CreateNew投影位于 `artifacts/account-sync-desktop-handoff/independent-audit/6ebcdf005c954a0cb5af2c51109f76be/independent-saved-evidence.json`，根读回核对SHA256 `CF88DFA9A7A39D5A97AA8C42E95C4A252EA399DDD27A0EA84FC2110F284458A3`。

正确性由真实输入和文件／回执验证；一致性由旧GET／ACK、排空顺序和零重放验证；安全性由A／B身份及会话隔离验证；可靠性由原档保留、受控迟到响应及自有资源收尾验证；扩展性由串行pump和有限任务／请求／输出验证。HTTP／secret受控，不据此推断正式服务端授权、数据库事务、容量、OS强杀或灾备还原通过。

本轮没有PostgreSQL、TLS监听、证书、WinCred或SMTP操作，也没有重跑B1b1。B1b1真实并发／丢响应／宿主恢复见[独立记录](2026-10-11-account-sync-b1b1-passed.md)。正式完整Desktop跨账号迟到响应、门廊新入口→正式服务→数据库联合验证及主观验收仍待独立门禁。未合并main、推送、部署、更新日常安装或替换生产DLL。
