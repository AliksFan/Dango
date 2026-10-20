[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$NativeDirectory,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$WriteManifest,
    [switch]$ExternalOverride
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
$NativeDirectory = [IO.Path]::GetFullPath($NativeDirectory)
$manifestPath = Join-Path $NativeDirectory 'belt-native-manifest.json'
$sourceHashes = [ordered]@{}
$sourceRoot = Join-Path $RepositoryRoot 'eng/native/box2d'
foreach ($name in @('CMakeLists.txt', 'dango_belt.c', 'dango_belt.h', 'dango_belt_math.h', 'dango_belt_guard.h',
    'dango_belt_basis_cache.h', 'dango_belt_profile.h', 'dango_belt_room.c', 'dango_belt_room.h',
    'dango_belt_room_collection.h', 'dango_belt_room_collection_impl.h', 'dango_round_ball_types.h') | Sort-Object) {
    $file = Get-Item -LiteralPath (Join-Path $sourceRoot $name)
    $sourceHashes['eng/native/box2d/' + $file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}
if (-not ('Dango.BeltPublish.NativeIdentity' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
namespace Dango.BeltPublish {
 public static class NativeIdentity {
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
  [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true)] static extern IntPtr GetProcAddress(IntPtr library, string name);
  [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr library);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetInt();
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetString();
  static IntPtr Export(IntPtr library, string name) {
   IntPtr address = GetProcAddress(library, name);
   if (address == IntPtr.Zero) throw new InvalidDataException("Missing belt native export: " + name);
   return address;
  }
  static int IntValue(IntPtr lib, string name) { return ((GetInt)Marshal.GetDelegateForFunctionPointer(Export(lib,name),typeof(GetInt)))(); }
  static string StringValue(IntPtr lib, string name) { return Marshal.PtrToStringAnsi(((GetString)Marshal.GetDelegateForFunctionPointer(Export(lib,name),typeof(GetString)))()); }
  public static int Verify(string path, bool room) {
   using(var reader = new BinaryReader(File.OpenRead(path))) {
    if(reader.ReadUInt16()!=0x5a4d) throw new InvalidDataException("Belt native input is not PE.");
    reader.BaseStream.Position=0x3c;
    int offset=reader.ReadInt32();
    if(offset<0x40 || offset>reader.BaseStream.Length-24) throw new InvalidDataException("Invalid PE offset.");
    reader.BaseStream.Position=offset;
    if(reader.ReadUInt32()!=0x4550 || reader.ReadUInt16()!=0x8664) throw new InvalidDataException("Belt native input must be Windows x64.");
   }
   var lib=LoadLibraryExW(path,IntPtr.Zero,0x1100);
   if(lib==IntPtr.Zero) throw new IOException("Belt module cannot load: Win32 " + Marshal.GetLastWin32Error());
   try {
    string prefix=room ? "dango_belt_room_" : "dango_belt_product_";
    if(IntValue(lib,prefix+"version")!=1 || StringValue(lib,prefix+"model")!=(room ? "continuous-belt-room-v1" : "continuous-belt-v1") || StringValue(lib,prefix+"tuning")!="C455-F-v1") throw new InvalidDataException("Belt native version/model/tuning mismatch.");
    int capabilities=IntValue(lib,prefix+"capabilities");
    if(room) {
     if((capabilities & 255)!=255 || IntValue(lib,prefix+"frame_size")!=4472 || StringValue(lib,prefix+"contact_model")!="belt-solid-aabb-r1-v1" || IntValue(lib,prefix+"collection_version")!=1 || IntValue(lib,prefix+"collection_frame_size")!=16968 || StringValue(lib,prefix+"collection_model")!="belt-room-furniture-r3-v1") throw new InvalidDataException("Belt room capabilities/frame/contact/collection mismatch.");
     foreach(string name in new[]{"create","step","read_frame","set_bounds","set_solid_walls","set_platform","remove_platform","destroy","enable_collection","read_collection_frame","probe_furniture_change","prepare_furniture_change","apply_prepared_furniture_change","cancel_prepared_furniture_change"}) Export(lib,prefix+name);
    } else {
     if(capabilities!=127 || IntValue(lib,"dango_belt_version")!=1) throw new InvalidDataException("Belt product capabilities/ABI mismatch.");
     foreach(string name in new[]{"create_calibrated","step","read","destroy","set_shape_guard","set_speed_saturation","set_radial_drive","read_rear_drive","reconfigure"}) Export(lib,"dango_belt_"+name);
    }
    return capabilities;
   } finally { FreeLibrary(lib); }
  }
 }
}
'@
}
$modules = @()
foreach ($name in @('dango_belt_product.dll', 'dango_belt_room.dll')) {
    $path = Join-Path $NativeDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing belt native package file: $name" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $capabilities = [Dango.BeltPublish.NativeIdentity]::Verify($path, $name -eq 'dango_belt_room.dll')
    $model = if ($name -eq 'dango_belt_room.dll') { 'continuous-belt-room-v1' } else { 'continuous-belt-v1' }
    $modules += [ordered]@{ path=$name; sha256=$hash; abi=1; model=$model; tuning='C455-F-v1'; capabilities=$capabilities }
}
if ($WriteManifest) {
    $manifest = [ordered]@{ format=1; contract='dango-belt-native-v1'; origin=$(if ($ExternalOverride) { 'explicit-override' } else { 'source-build' }); sourceHashes=$sourceHashes; modules=$modules }
    [IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 6) + "`n"), [Text.UTF8Encoding]::new($false))
} else {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.format -ne 1 -or $manifest.contract -cne 'dango-belt-native-v1' -or $manifest.origin -notin @('source-build','explicit-override')) { throw 'Unsupported belt native manifest.' }
    if (@($manifest.sourceHashes.PSObject.Properties).Count -ne $sourceHashes.Count) { throw 'Belt native source list mismatch.' }
    foreach ($relative in $sourceHashes.Keys) { if ($manifest.sourceHashes.$relative -cne $sourceHashes[$relative]) { throw "Belt native source hash mismatch: $relative" } }
    if (@($manifest.modules).Count -ne 2) { throw 'Belt native module list mismatch.' }
    foreach ($module in $modules) {
        $record = @($manifest.modules | Where-Object { $_.path -ceq $module.path })
        if ($record.Count -ne 1 -or $record[0].sha256 -cne $module.sha256 -or $record[0].abi -ne 1 -or $record[0].model -cne $module.model -or $record[0].tuning -cne $module.tuning -or $record[0].capabilities -ne $module.capabilities) { throw "Belt native artifact binding mismatch: $($module.path)" }
    }
}
[pscustomobject]@{ ManifestPath=$manifestPath; Modules=$modules; SourceHashes=$sourceHashes }
