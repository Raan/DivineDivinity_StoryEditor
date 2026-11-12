param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$OsirisDllPath,
    [Parameter(Mandatory=$true)][string]$BaseStoryPath,
    [Parameter(Mandatory=$true)][string]$SourcePath,
    [Parameter(Mandatory=$true)][string]$OutputStoryPath,
    [Parameter(Mandatory=$true)][string]$LogPath,
    [Parameter(Mandatory=$true)][string]$WorkDirectory,
    [int]$MinimumFunctions = 1100,
    [int]$MinimumNodes = 10000
)
$ErrorActionPreference = 'Stop'
if ([Environment]::Is64BitProcess) { throw 'Run this script with 32-bit Windows PowerShell.' }
$gameRoot = [IO.Path]::GetFullPath($GameRoot)
$dllPath = [IO.Path]::GetFullPath($OsirisDllPath)
if (!(Test-Path -LiteralPath $dllPath -PathType Leaf)) { throw "OsirisDLL.dll not found: $dllPath" }
$base = [IO.Path]::GetFullPath($BaseStoryPath)
$sourcePath = [IO.Path]::GetFullPath($SourcePath)
$output = [IO.Path]::GetFullPath($OutputStoryPath)
$log = [IO.Path]::GetFullPath($LogPath)
$validationLog = $log + '.validation.log'
$work = [IO.Path]::GetFullPath($WorkDirectory)
foreach ($inputPath in @($base,$sourcePath)) {
    foreach ($otherPath in @($output,$log,$validationLog)) {
        if ($inputPath -ieq $otherPath) { throw "Input and output/log paths must be different: $inputPath" }
    }
}

if (!(Test-Path -LiteralPath $base -PathType Leaf)) { throw "Base story not found: $base" }
if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Source not found: $sourcePath" }
if ($base -ieq $output -or $sourcePath -ieq $output) { throw 'Input and output paths must be different.' }
if ($output -ieq $log -or $output -ieq $validationLog -or $log -ieq $validationLog) { throw 'Output and log paths must be different.' }
if ($work -ieq $base -or $work -ieq $sourcePath -or $work -ieq $output -or $work -ieq $log) { throw 'Working directory must be separate from all input/output files.' }
if (Test-Path -LiteralPath $output) { throw "Refusing to overwrite existing output: $output" }
if ((Test-Path -LiteralPath $log) -or (Test-Path -LiteralPath $validationLog)) { throw 'Refusing to overwrite existing logs; choose new log paths.' }
if (Test-Path -LiteralPath $work) {
    if (@(Get-ChildItem -LiteralPath $work -Force).Count -gt 0) { throw "Working directory is not empty; use a fresh directory: $work" }
} else {
    New-Item -ItemType Directory -Force -Path $work | Out-Null
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output),(Split-Path -Parent $log) | Out-Null
Set-Location $work
[Environment]::CurrentDirectory = $work

$native = @'
using System;
using System.Runtime.InteropServices;
public static class OsiMergeRebuild {
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] public static extern IntPtr LoadLibrary(string p);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] public static extern IntPtr GetProcAddress(IntPtr h, string n);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate UInt32 GetHandle(byte a, ushort b, ushort c);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public delegate bool Invoke(UInt32 h, IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void Error(IntPtr p);
    [StructLayout(LayoutKind.Sequential)] public struct Init { public IntPtr get; public IntPtr call; public IntPtr query; public IntPtr error; }
    [DllImport("OsirisDLL.dll", EntryPoint="?RegisterDIVFunctions@COsiris@@QAEXPAUTOsirisInitFunction@@@Z", CallingConvention=CallingConvention.ThisCall)] public static extern void Register(IntPtr self, IntPtr init);
    [DllImport("OsirisDLL.dll", EntryPoint="?OpenLogFile@COsiris@@QAE_NPBD0@Z", CallingConvention=CallingConvention.ThisCall, CharSet=CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool OpenLog(IntPtr self, string path, string mode);
    [DllImport("OsirisDLL.dll", EntryPoint="?Load@COsiris@@QAE_NPBD@Z", CallingConvention=CallingConvention.ThisCall, CharSet=CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Load(IntPtr self, string path);
    [DllImport("OsirisDLL.dll", EntryPoint="?PrepareMerge@COsiris@@QAE_NXZ", CallingConvention=CallingConvention.ThisCall)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool PrepareMerge(IntPtr self);
    [DllImport("OsirisDLL.dll", EntryPoint="?Compile@COsiris@@QAE_NPBD0@Z", CallingConvention=CallingConvention.ThisCall, CharSet=CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Compile(IntPtr self, string path, string mode);
    [DllImport("OsirisDLL.dll", EntryPoint="?Merge@COsiris@@QAE_NXZ", CallingConvention=CallingConvention.ThisCall)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Merge(IntPtr self);
    [DllImport("OsirisDLL.dll", EntryPoint="?Save@COsiris@@QAE_NPBD@Z", CallingConvention=CallingConvention.ThisCall, CharSet=CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Save(IntPtr self, string path);
    [DllImport("OsirisDLL.dll", EntryPoint="?CloseLogFile@COsiris@@QAEXXZ", CallingConvention=CallingConvention.ThisCall)] public static extern void CloseLog(IntPtr self);
    public static UInt32 Handle(byte a, ushort b, ushort c) { return (UInt32)(a & 15) | (16u * ((UInt32)b | (((UInt32)c & 1023u) << 16))); }
    public static bool NoInvoke(UInt32 h, IntPtr p) { return false; }
    public static void OnError(IntPtr p) { Console.Error.WriteLine(Marshal.PtrToStringAnsi(p)); }
    public static GetHandle getDelegate = Handle;
    public static Invoke callDelegate = NoInvoke;
    public static Invoke queryDelegate = NoInvoke;
    public static Error errorDelegate = OnError;
}
'@

Add-Type -TypeDefinition $native
$dll = [OsiMergeRebuild]::LoadLibrary($dllPath)
if ($dll -eq [IntPtr]::Zero) { throw 'Could not load OsirisDLL.dll.' }
$osiris = [OsiMergeRebuild]::GetProcAddress($dll, '?Osiris@@3VCOsiris@@A')
if ($osiris -eq [IntPtr]::Zero) { throw 'Could not locate exported global COsiris object.' }
$init = New-Object OsiMergeRebuild+Init
$init.get = [Runtime.InteropServices.Marshal]::GetFunctionPointerForDelegate([OsiMergeRebuild]::getDelegate)
$init.call = [Runtime.InteropServices.Marshal]::GetFunctionPointerForDelegate([OsiMergeRebuild]::callDelegate)
$init.query = [Runtime.InteropServices.Marshal]::GetFunctionPointerForDelegate([OsiMergeRebuild]::queryDelegate)
$init.error = [Runtime.InteropServices.Marshal]::GetFunctionPointerForDelegate([OsiMergeRebuild]::errorDelegate)
$memory = [Runtime.InteropServices.Marshal]::AllocHGlobal([Runtime.InteropServices.Marshal]::SizeOf([type][OsiMergeRebuild+Init]))
try {
    [Runtime.InteropServices.Marshal]::StructureToPtr($init, $memory, $false)
    [OsiMergeRebuild]::Register($osiris, $memory)
    if (![OsiMergeRebuild]::OpenLog($osiris, $log, 'w')) { throw 'Could not open Osiris build log.' }

    Write-Host "BASE LOAD: $base"
    if (![OsiMergeRebuild]::Load($osiris, $base)) { throw 'Base story load failed.' }
    Write-Host 'BASE LOAD SUCCEEDED'

    if (![OsiMergeRebuild]::PrepareMerge($osiris)) { throw 'PrepareMerge failed.' }
    foreach ($name in @('story_merge_f.dat','story_merge_g.dat')) {
        $path = Join-Path $work $name
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "PrepareMerge did not create $name" }
        Write-Host "$name bytes: $((Get-Item -LiteralPath $path).Length)"
    }

    Write-Host "COMPILE: $sourcePath"
    if (![OsiMergeRebuild]::Compile($osiris, $sourcePath, 'r')) { throw 'Source compile failed or produced warnings/errors.' }

    if (![OsiMergeRebuild]::Merge($osiris)) { throw 'Merge failed.' }
    if (![OsiMergeRebuild]::Save($osiris, $output)) { throw 'Save failed.' }
    [OsiMergeRebuild]::CloseLog($osiris)

    Write-Host 'Running read-only structural validation...'
    & (Join-Path $PSScriptRoot 'LoadStoryCheck.ps1') -GameRoot $gameRoot -OsirisDllPath $dllPath -StoryPath $output -LogPath $validationLog -MinimumFunctions $MinimumFunctions -MinimumNodes $MinimumNodes

    Write-Host 'BUILD WITH MERGE SUCCEEDED'
    Write-Host "Output: $output"
    Write-Host "Output bytes: $((Get-Item -LiteralPath $output).Length)"
    Write-Host "Build log: $log"
    Write-Host "Validation log: $validationLog"
}
finally {
    [Runtime.InteropServices.Marshal]::FreeHGlobal($memory)
}
