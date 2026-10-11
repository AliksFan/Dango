# 完整桌面同步与换号：无数据库技术门禁

日期：2026-10-11。依据用户已确认的[业务规则](2026-10-10-account-sync-nickname-and-porch-design.md)继续自动验证。B1b1 已执行完毕；本门禁不使用其额度、运行器或已冻结候选。

## 范围与资源

新测试直接启动生产 `DangoApplicationHost.CreateForAccountIntegration`，使用真实 ControlCenterWindow HWND、生产账号与捏脸接线、真实本机配置文件及 `PetConfigurationFileJournal`。独立线程 UIA client 通过 Invoke／Value／Selection／Range 模式操作该自有窗口；应用自己的 DispatcherTimer 和 SavedToDisk 事件驱动同步，不能手动 Pump 冒充定时器或完整接线。

仅注入 `FormalAccountClient.ForTesting` 的受控 HttpMessageHandler 与内存 secret store；虚构 A／B 身份、响应和本地文件必须明确标为测试数据。这证明完整客户端的生命周期隔离，不能声称网络／PostgreSQL端到端通过。

使用既有隔离 native 模块，加载前核对已记录的 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`；缺失或摘要不符即失败，不能静默退回假场景。启动后验证真正健康渲染帧。每个夹具只拥有自己的测试窗口、host 和隔离输出目录；结束停止自身 timer、排空 UIA／HTTP／同步任务、Dispose host，保留测试文件和证据，不删除既有目录。

不启动 PostgreSQL／HTTPS／SMTP，不执行迁移、注册或任何真实业务写入，不操作证书、WinCred、原 state／凭据／服务密钥，不注入原生键盘、不操作其他窗口或改生产 DLL。新构建输出使用独立 artifacts 路径，不覆盖 B1b1 已消耗的候选。

## 先定义能失败的验收

1. **真实宿主与定时器**：通过 UIA 登录并关闭账号浮层；通过真实捏脸 Apply 成功保存，生产 SavedToDisk 接线产生唯一上传。随后受控账号配置版本改变，仅等待真实两秒 DispatcherTimer；真实配置文件和已保存外观收敛，原档／回执完整，远端应用零回传，浮层保持关闭。
2. **旧 GET 跨越注销**：实际 timer 发出 A 的配置 GET，受控传输保留该请求并真实延迟回包。通过 UIA 注销，确认旧会话取消；开始 B 登录，然后释放不同颜色的 A 回包。检查真实任务顺序、A／B 的 User／Pet／Session 归属、本机原始字节、来源比较及首页昵称；A 不能覆盖 B 的比较或保存状态，也不能发出新 PATCH。B 登录会先 drain 旧 pump，测试必须证明此顺序，不能假定 B 已完成时 A pump 仍未结束。
3. **旧 PATCH 回包跨越注销**：A 的真实成功本机保存发出完整配置 PATCH，受控服务器只提交一次并保留成功响应；UIA 注销并开始 B 登录后释放响应。验证旧 ACK 不能成为 B 的同步基线、不能修改 B 昵称／身份、不能将旧意图改成 B 请求或复活。核对原请求的整份内容、expectedVersion及A会话，以及B最终选择和真实文件。

每例须有明确请求到达／释放／取消／settled记录和不可重复阶段，时间等待与任务收尾有界。不能靠最后颜色恰好相同、仅反射修改状态、重复既有controller测试或只调用 RaiseEvent 宣称通过。HTTP fault gate 被取消前未到达正式发送边界的操作须如实记录，不能伪称已有晚到提交。

来源页尚未选择时确认按钮必须禁用，两项来源均未选中；独立 UIA 明确选择后才允许确认。每例保存两张有界的自有 WPF 窗口图，采用 CreateNew，不采集其他窗口或整个桌面。结束在内存 secret store Dispose 前，有界检查生产账号操作 semaphore 已释放，同时分别排空原 pump、UIA 和 HTTP 任务；不可观察的 `DisposeAfterPumpAsync` 不写成已直接等待。

首次实现和测试结果不得预先写成通过。若边界检查失败，先保留证据并定位；不得放宽业务断言或把无可见窗口的组合测试改称完整桌面。验证新用例及受影响现有接线／UIA回归后，记录尚需正式 HTTPS／数据库联合验证的行为，再准备独立精确资源计划。

## 五项要求

正确性检查真实保存、timer和登录比较；一致性检查旧GET／ACK、任务排空及零重放；安全性逐项绑定虚构会话和账号，不把昵称当身份；可靠性检查真实文件原档、错误保留及自身任务收尾；扩展性保持一个串行pump、轮询退避和有限等待。受控传输不能验证真实服务授权、持久数据库事务、网络灾难或公开容量。

人工继续主要验收效果、手感与流畅度；上述技术工作由助手完成。无 main 合并、推送、部署、日常安装更新或生产 DLL 替换。
