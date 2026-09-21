# GeometryProbe

对两个已发布包使用相同夹具、固定预热和7轮轮换顺序测量帽子Create/Remap/AvailableLift及脸部Solve。输出每轮耗时、当前线程分配、校验和与实际程序集SHA-256。只使用公开求解接口，不启动宿主、不读取日常设置、不运行原生物理、不强制GC。

```powershell
dotnet publish eng/tools/Dango.GeometryProbe/Dango.GeometryProbe.csproj -c Release -r win-x64 --self-contained true -p:DesktopPackagePath="$packagePath" -o "$probeOutput"
& "$probeOutput/Dango.GeometryProbe.exe" "$newOutputJson"
```

基线和候选各发布至独立探针输出；运行前核对产品程序集与包的SHA-256。两者均使用.NET/WPF 10.0.11，串行交替运行，记录中位数与范围。输出必须为新文件；不用秒表耗时作单元测试门禁，也不把局部速度或分配改善换算成整机CPU、存活内存、FPS或桌面验收结果。等价性由独立参考测试验证，校验和仅辅助识别运行差异。
