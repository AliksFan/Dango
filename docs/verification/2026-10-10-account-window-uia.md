# 账号真实窗口 UI Automation 验证

日期：2026-10-10。范围依据[登录同步、昵称和门廊修订](../planning/2026-10-10-account-sync-nickname-and-porch-design.md)。使用已整合的实际 `ProductHomeView`、`AccountPanelView` 和 `DesktopAccountController`；本记录仅说明真实 WPF 窗口的渲染、独立 UIA 操作和受控业务边界，不是正式 HTTPS／数据库端到端验收。

## 方法和隔离

新增 `AccountWindowUiaTests` 为每项测试创建一个真实可见、归属明确的 Window；独立线程通过该窗口 HWND 执行 `AutomationElement.FromHandle`，仅查找该窗口的后代。UIA client 使用 InvokePattern 打开和提交，ValuePattern 填写邮箱／昵称，SelectionItemPattern 选择配置来源，TogglePattern 设置记住登录；不使用 RaiseEvent、直接修改 RadioButton 或在 UI 线程调用 provider 代替这些操作。

密码为本 fixture 的虚构字符串，由 fixture 预填 PasswordBox；该控件没有可读 ValuePattern。HTTP 使用限定 `.invalid` origin 的受控 handler，账号／团子身份虚构，资料和配置写入版本受检查。秘密、原始配置及本机配置仓储均为内存 fixture，没有读取系统凭据、发送真实请求、启动服务或连接数据库。

真实 Desktop dispatcher timer 保持默认两秒，远端变更检查没有手动调用 `PumpConfigurationAsync`。本机成功保存通知由 fixture 提交给真实 controller；该测试不能代替实际捏脸文件落盘和宿主 SavedToDisk 接线的另外验证。

## 已执行结果

`account-window-uia-first.trx`：4／4 通过，零失败／跳过。

| 实际流程 | 证据 |
|---|---|
| 首次昵称为空 | 独立 UIA 读取“保存昵称”；在线房间 client 与配置 Ready 尚不可用；空白昵称不发写入；UIA 输入合法昵称并保存后进入真实来源选择 |
| 登录配置不同 | 选来源前本机零应用；UIA 选择账号配置并明确确认后本机与账号相同；团子 ID 保持，已有账号配置未重复上传 |
| 首页昵称及房间入口移除 | UIA 关闭账号面板后，实际首页账号按钮 Name 为昵称；再次打开账号页没有“我的房间”按钮 |
| 关闭浮层持续同步 | UIA 关闭后成功保存通知上传一次；实际两秒 dispatcher timer 读取之后的远端配置并应用，本机不回传，账号面板仍关闭 |
| 记住登录，配置相同 | 关闭首个窗口并创建第二个真实窗口；背景恢复显示同一昵称，不打开账号浮层，零配置写入／本机应用 |
| 记住登录，离线配置不同 | 背景恢复主动打开来源选择；UIA 选本机并确认后只写一次，保留账号团子身份；未自动重放旧保存 |

截图由实际显示的 WPF 内容生成，已检查来源选择与首页结果：本工作树 `artifacts/test-runs/723894f333cb45c0a1b543ac3f88de7b/account-window-uia/` 下的 `nickname-before-configuration.png`、`homepage-after-nickname-choice.png`、`homepage-after-closed-panel-timer-sync.png`、`remembered-equal-no-overlay.png` 及 `remembered-different-after-explicit-choice.png`。

## 验收边界

这些检查证明实际 UIA 查找和操作、真实 controller 的资料／配置逻辑以及窗口关闭后的 timer 生命周期。没有证明正式 TLS、SMTP、系统 WinCred、数据库前向迁移或真实服务持久化；没有改动停止后的 R2 数据。没有原生键盘／鼠标注入或正式多人画面测试。

手感、界面效果和使用流畅度仍由用户验收。没有合并 main、远端推送、部署、更新日常安装或替换受保护 native DLL。
