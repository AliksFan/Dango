# 普通房家具 I0 数据与存储合同

日期：2026-10-10。范围：已批准[普通房接入计划](2026-10-10-furniture-room-integration-plan.md)中的 I0 数据与存储部分。**本文冻结两种布局合同，记录其自动门禁；不代表完整 I0、运行时接入或主程序人工验收已通过。**

旧家具独立试窗继续使用 [C0 合同](2026-10-10-live-furniture-c0-contract.md)。主程序本地普通房显式使用独立合同，不能复用旧试窗坐标或误读其文件。两者均为本地文件，不代表账号保存、服务器房间身份或多人授权。

## 场景身份与路径

| 字段 | Trial：旧独立试窗 | OrdinaryRoom：主程序本地普通房 |
|---|---|---|
| schemaVersion | 1 | 1 |
| sceneId | `local-single-window` | `local-ordinary-room` |
| coordinateSpace | `static-platform-p-v1` | `room-floor-height-v1` |
| sceneGeometryRevision | 1 | 1 |
| 数据根目录下的相对路径 | `furniture-layout.json` | `rooms/local-ordinary-room/furniture-layout.json` |
| 平台预设及版本 | `official-static-platform` / 1 | `official-static-platform` / 1 |

布局 JSON 保留严格字段：`schemaVersion`、`sceneId`、`coordinateSpace`、`sceneGeometryRevision`、`layoutVersion`、`lastOperationId`、`items`。`items` 仅允许零件或一件平台；平台只包含 `instanceId`、`presetId`、`presetRevision`、`anchor{x,y}`。未知、重复、缺失字段，错误类型、非有限坐标及不受支持的版本均拒绝。场景身份校验也适用于空布局；不能以空布局绕过合同隔离。

文件预算保持 64 KiB。相机、DPI、窗口大小、当前 native origin、团子运动、抓取状态及物理步不进入文件。尺寸和材质由冻结合同及预设版本确定，不提供可调参数。

默认 API 保持 Trial；只有显式传入 `FurnitureLayoutContract.OrdinaryRoom` 才启用普通房合同。`GetLayoutPath` 返回绝对且规范化的路径，不创建布局文件。存储对象按自身合同解析传入路径；调用方负责使用对应命名空间。放错路径的另一合同文件被标记 Protected，保留原字节，不隐式迁移或覆盖为空布局。

## 普通房几何

锚点是平台顶面中心，`X` 为固定房间内逻辑横坐标，`Y` 为距真实碰撞地面的高度，向上为正。逻辑 Surface 为 2160×360；水平可用边界是 `[24,2136]`，高度边界是 `[0,360]`。平台宽 272、高 21、摩擦 .72，默认锚点 `(1440,120)`。微调步长 4，每秒最多 20 次。

平台完整矩形为 `[X−136,X+136] × [Y−21,Y]`，完整矩形必须位于合法边界内。固定出生保护矩形是 `[900,1260] × [0,200]`。与房间或出生保护边缘相切允许；与出生保护区在两个轴上均有正长度交集时拒绝。出生保护区不跟随镜头或团子当前位置。

Trial 的旧边界、平台尺寸、默认锚点、微调步长及校验行为全部保留；`LocalFurnitureSceneGeometry` 的公开常量与入口仍可使用。

普通房逻辑锚点到当前 native origin 的转换，以及真实 `LocalGroundY/GroundY`、`NeutralBodyGroundOffset`、绘制和鼠标逆变换的对齐，由运行时接入完成。本文的序列化与存储通过不能替代该转换、真实团子碰撞或换场恢复验证，也不能把逻辑高度 360 当作永久真实地面坐标。

## 调用合同

```csharp
FurniturePlatformInstance.Validate(FurnitureLayoutContract? contract = null)
FurnitureLayout.Validate(FurnitureLayoutContract? contract = null)
FurnitureLayoutSerializer.Serialize(FurnitureLayout layout, FurnitureLayoutContract? contract = null)
FurnitureLayoutSerializer.Deserialize(ReadOnlySpan<byte> bytes, FurnitureLayoutContract? contract = null)
FurnitureEditOperation.BuildCandidate(FurnitureLayout baseline, FurnitureLayoutContract? contract = null)
new FurnitureLayoutStore(IFurnitureFileWriter? writer = null, FurnitureLayoutContract? contract = null)
FurnitureLayoutStore.GetLayoutPath(string dataRoot, FurnitureLayoutContract? contract = null)
```

`FurnitureLayoutStore.Contract` 是只读公共属性。Load、候选校验、序列化、临时文件重读和最终结果确认使用同一个合同。操作仍检查期望版本、完整目标实例和最近 operationId；成功候选只增一版，溢出拒绝，不能通过删除洗掉无效基线。

存储保留独占 lease、取得 lease 后重读正式文件、唯一临时文件、落盘 flush、临时原字节重读、提交前正式原字节比较及原子替换。提交异常后按真实目标字节区分 Committed / NotCommitted / Unknown；Unknown 禁止后续覆盖。目录或链接、坏文件、暂时不可读和不受支持的合同均保留原件。只清理当前操作自己的临时文件，不加载或删除其他残留临时文件。

## 本轮实际验证

在任务独占 `artifacts/furniture-room-i0-build` 构建，串行执行：

```powershell
dotnet test tests/Dango.Core.Tests/Dango.Core.Tests.csproj --artifacts-path artifacts/furniture-room-i0-build --filter FullyQualifiedName~Furniture --no-restore --logger 'trx;LogFileName=i0-core-furniture.trx' --results-directory artifacts/furniture-room-i0-results --verbosity minimal
dotnet test tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj --artifacts-path artifacts/furniture-room-i0-build --filter FullyQualifiedName~FurnitureLayoutStore --logger 'trx;LogFileName=i0-store-furniture.trx' --results-directory artifacts/furniture-room-i0-results --verbosity minimal
```

| 门禁 | 实际结果 |
|---|---|
| Core 家具合同与操作 | 89/89，通过；含旧 66 项和新增普通房合同 23 项 |
| Desktop 真实文件与租约 | 42/42，通过；旧 18 项同时对 Trial / OrdinaryRoom 运行，共 36 项，另加隔离 6 项 |
| 合同反例 | 空及非空布局互读、场景/坐标/几何版本错误、严格字段、有限值、完整平台越界、出生区正面积重叠、错误目标、陈旧/重复操作及溢出均被拒绝 |
| 真实文件反例 | 坏文件/未来版本/超预算、锁定读失败、临时校验失败、外部同版本字节改变、提交前后异常、目标消失/第三种内容/不可读、另一进程租约争用均按规定保护 |
| 命名空间 | 普通房保存不改变旧文件；放错路径的空及非空文件保留字节并禁止 Stage |

TRX 保留于 `artifacts/furniture-room-i0-results/`。真实文件测试仅使用 `artifacts/test-runs/` 下唯一目录及虚构标识；测试子进程已退出，lease 已释放，合成布局及可复用 `.lock` 证据保留，未清理真实用户数据。

本数据子任务未启动主程序验收窗口，未执行 native/场景世界/首帧/入口/换场、DPI、设备或性能门禁；这些仍由 I1–I4 接入及验收负责。本子任务未创建 Git 提交，未合并 main、推送、部署、更新日常安装或替换生产 DLL。
