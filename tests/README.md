# 测试归属

- `Dango.LocalRooms.Tests` 拥有本地房间目录、恢复存储、家具布局存储、房间相机及本地房间产品交接用例；产品集成断言继续通过真实 Desktop 类型验证。
- `Dango.Desktop.Tests` 拥有其余桌面用例。测试源文件只在拥有者项目中编译，不跨测试项目链接。
- `Support/` 只存放无测试入口的共享支持代码。当前两个项目显式链接仓库定位与产品首页就绪等待工具；支持代码不引用测试项目，也不改变业务断言。
- 网络测试的 `Support/LocalServerProcess.cs` 统一私有 bootstrap/control 匿名管道、STOP/EOF 退出、失败回收与安全日志；Server 测试保留程序集定位，Sync 测试保留 `DANGO_SYNC_SERVER_PATH` 最终 exe 定位。每个夹具只回收自身启动的子进程，不输出 bootstrap、ticket 或原始 stdout/stderr。
- 调整归属时物理移动用例，并同步命名空间、支持代码引用及文档路径。可用 `dotnet msbuild <项目> -getItem:Compile` 检查两个项目的编译项交集，交集不得含测试入口。

## 无窗口定向验证

构建两个测试项目后，可以运行下列定向组；这不代表完整桌面验收。

```powershell
dotnet test tests/Dango.Desktop.Tests --no-build --no-restore --filter 'FullyQualifiedName~WorkspaceAcceptanceTests'
dotnet test tests/Dango.LocalRooms.Tests --no-build --no-restore --filter 'FullyQualifiedName~Camera|FullyQualifiedName~FurnitureLayoutStore|FullyQualifiedName~LocalRoomCatalogStoreTests|FullyQualifiedName~RoomResumeStoreTests'
```

`Native/`、`PorchCatalogUiTests` 和 `LocalRoomAppearanceIntegrationTests` 包含产品窗口或原生运行时验证，不能从项目归属推断它们是无窗口测试。执行前按根 `AGENTS.md` 核对后台有效性和屏幕授权，不将上述定向组通过记为这些用例已验证。
