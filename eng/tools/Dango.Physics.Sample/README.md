# 无 WPF 物理模块样例

从仓库根构建统一 native 模块后运行：

```powershell
dotnet run --project eng/tools/Dango.Physics.Sample/Dango.Physics.Sample.csproj -c Release -- artifacts/native-spike/physics-module-unified-build/Release/dango_physics_module.dll 3
```

最后一个参数可取 1/2/3，省略时为 3。样例在一个 world 中添加指定数量的团子、推进、移除最后一只、重新添加得到新 ID、显式重置，再推进并释放；每帧检查锁存错误，失败退出 1，参数错误退出 2。它不加载 WPF、不替换正式 DLL、不创建持久数据。

构建、坐标和 API 详见[模块说明](../../../docs/phase-1-physics-module.md)。
