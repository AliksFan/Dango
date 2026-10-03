[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$PhysicsModulePath,
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
if (-not $RepositoryRoot) { $RepositoryRoot = Join-Path $PSScriptRoot '..' }
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$moduleFullPath = [IO.Path]::GetFullPath($PhysicsModulePath)
$acceptedModuleHash = '00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C'
$protectedInputs = @{
    'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll' = '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A'
    'eng/native/box2d/LICENSE.box2d.txt' = '68A3E676D7E94093B102D5CBA0D4E04AF812040D6F230C3DB67A6664574E43D2'
    'eng/native/box2d/dependency.json' = 'F878EE6173D5037446F75FFED09F286AEEAFB1B50BEAE794D1615A59CC27E698'
}

function Assert-PlainFile([string]$Path) {
    $cursor = $Path
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points are not supported for trusted publish inputs: $cursor"
            }
        }
        $cursor = Split-Path -Parent $cursor
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required trusted input is missing: $Path. Supply the reviewed physics module explicitly; see eng/native/box2d/README.md. No output was created."
    }
}

function Assert-InputHash([string]$Path, [string]$Expected) {
    Assert-PlainFile $Path
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -cne $Expected) {
        throw "Trusted input hash mismatch: $Path. Expected $Expected; actual $actual. Keep the accepted artifact or review a new baseline; no output was created."
    }
    return $actual
}

# Only the exact accepted binary is ever loaded. Do not execute an untrusted DLL to inspect it.
$actualModuleHash = Assert-InputHash $moduleFullPath $acceptedModuleHash
foreach ($relative in @($protectedInputs.Keys | Sort-Object)) {
    $null = Assert-InputHash (Join-Path $RepositoryRoot $relative) $protectedInputs[$relative]
}
if ([IntPtr]::Size -ne 8) { throw 'Publish preflight requires a 64-bit Windows PowerShell host.' }
if (-not ('Dango.PublishInputs.NativeModule' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
namespace Dango.PublishInputs {
    public static class NativeModule {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true, SetLastError=true)]
        static extern IntPtr GetProcAddress(IntPtr library, string name);
        [DllImport("kernel32.dll", SetLastError=true)]
        static extern bool FreeLibrary(IntPtr library);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetAbi();
        public static int Verify(string path, string[] exports) {
            using (var reader = new BinaryReader(File.OpenRead(path))) {
                if (reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Native input is not a PE file.");
                reader.BaseStream.Position = 0x3c;
                int offset = reader.ReadInt32();
                if (offset < 0x40 || offset > reader.BaseStream.Length - 24) throw new InvalidDataException("Invalid PE offset.");
                reader.BaseStream.Position = offset;
                if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664)
                    throw new InvalidDataException("Native input must be Windows x64.");
            }
            var library = LoadLibraryExW(path, IntPtr.Zero, 0x1100);
            if (library == IntPtr.Zero) throw new IOException("Accepted module cannot load: Win32 " + Marshal.GetLastWin32Error());
            try {
                foreach (string name in exports) {
                    if (GetProcAddress(library, name) == IntPtr.Zero) throw new InvalidDataException("Missing required module export: " + name);
                }
                var query = (GetAbi)Marshal.GetDelegateForFunctionPointer(GetProcAddress(library, exports[0]), typeof(GetAbi));
                int abi = query();
                if (abi != 1) throw new InvalidDataException("Physics module ABI mismatch: expected 1; actual " + abi);
                return abi;
            } finally { FreeLibrary(library); }
        }
    }
}
'@
}
$exports = @('dango_physics_get_abi_version', 'dango_physics_world_create', 'dango_physics_world_add',
    'dango_physics_world_remove', 'dango_physics_world_step', 'dango_physics_world_reset', 'dango_physics_world_destroy')
$abi = [Dango.PublishInputs.NativeModule]::Verify($moduleFullPath, $exports)
[pscustomobject]@{
    PhysicsModulePath = $moduleFullPath
    PhysicsModuleSha256 = $actualModuleHash
    NativeAbi = $abi
    RequiredExports = $exports
    ProtectedInputsVerified = $true
}
