param(
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [Parameter(Mandatory = $true)][string]$OsirisDllPath,
    [Parameter(Mandatory = $true)][string]$StoryPath,
    [Parameter(Mandatory = $true)][string]$LogPath,
    [int]$MinimumFunctions = 1100,
    [int]$MinimumNodes = 10000
)
$ErrorActionPreference = 'Stop'
$gameRoot = [IO.Path]::GetFullPath($GameRoot)
$dllPath = [IO.Path]::GetFullPath($OsirisDllPath)
if (!(Test-Path -LiteralPath $dllPath -PathType Leaf)) { throw "OsirisDLL.dll not found: $dllPath" }
Set-Location $gameRoot
$source = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class OsiLoadProbe {
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] public static extern IntPtr LoadLibrary(string p);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] public static extern IntPtr GetProcAddress(IntPtr h, string n);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate UInt32 GetHandle(byte a, ushort b, ushort c);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public delegate bool Invoke(UInt32 h, IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void Error(IntPtr p);
    [StructLayout(LayoutKind.Sequential)] public struct Init { public IntPtr get; public IntPtr call; public IntPtr query; public IntPtr error; }
    [DllImport("OsirisDLL.dll", EntryPoint="?RegisterDIVFunctions@COsiris@@QAEXPAUTOsirisInitFunction@@@Z", CallingConvention=CallingConvention.ThisCall)] public static extern void Register(IntPtr self, IntPtr init);
    [DllImport("OsirisDLL.dll", EntryPoint="?OpenLogFile@COsiris@@QAE_NPBD0@Z", CallingConvention=CallingConvention.ThisCall, CharSet=CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool OpenLog(IntPtr self, string path, string mode);
    [DllImport("OsirisDLL.dll", EntryPoint="?Load@COsiris@@QAE_NPBD@Z", CallingConvention=CallingConvention.ThisCall, CharSet=CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Load(IntPtr self, string path);
    [DllImport("OsirisDLL.dll", EntryPoint="?GetStoryVersionString@COsiris@@QAE_NPAD@Z", CallingConvention=CallingConvention.ThisCall, CharSet=CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool GetVersion(IntPtr self, StringBuilder value);
    [DllImport("OsirisDLL.dll", EntryPoint="?GenerateFunctionList@COsiris@@IAEXXZ", CallingConvention=CallingConvention.ThisCall)] public static extern void GenerateFunctionList(IntPtr self);
    [DllImport("OsirisDLL.dll", EntryPoint="?GenerateNodeList@COsiris@@IAEXXZ", CallingConvention=CallingConvention.ThisCall)] public static extern void GenerateNodeList(IntPtr self);
    [DllImport("OsirisDLL.dll", EntryPoint="?CloseLogFile@COsiris@@QAEXXZ", CallingConvention=CallingConvention.ThisCall)] public static extern void CloseLog(IntPtr self);
    public static GetHandle getDelegate = Handle;
    public static Invoke callDelegate = NoInvoke;
    public static Invoke queryDelegate = NoInvoke;
    public static Error errorDelegate = OnError;
    public static UInt32 Handle(byte a, ushort b, ushort c) { return (UInt32)(a & 15) | (16u * ((UInt32)b | (((UInt32)c & 1023u) << 16))); }
    public static bool NoInvoke(UInt32 h, IntPtr p) { return false; }
    public static void OnError(IntPtr p) { Console.Error.WriteLine(Marshal.PtrToStringAnsi(p)); }
}
'@
if ([Environment]::Is64BitProcess) { throw 'Run this probe with 32-bit Windows PowerShell.' }
$story = [IO.Path]::GetFullPath($StoryPath)
$log = [IO.Path]::GetFullPath($LogPath)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $log) | Out-Null
Add-Type -TypeDefinition $source
$module = [OsiLoadProbe]::LoadLibrary($dllPath)
if ($module -eq [IntPtr]::Zero) { throw 'Could not load OsirisDLL.dll.' }
$osiris = [OsiLoadProbe]::GetProcAddress($module, '?Osiris@@3VCOsiris@@A')
if ($osiris -eq [IntPtr]::Zero) { throw 'Could not locate exported global Osiris object.' }
$init = New-Object OsiLoadProbe+Init
$init.get = [Runtime.InteropServices.Marshal]::GetFunctionPointerForDelegate([OsiLoadProbe]::getDelegate)
$init.call = [Runtime.InteropServices.Marshal]::GetFunctionPointerForDelegate([OsiLoadProbe]::callDelegate)
$init.query = [Runtime.InteropServices.Marshal]::GetFunctionPointerForDelegate([OsiLoadProbe]::queryDelegate)
$init.error = [Runtime.InteropServices.Marshal]::GetFunctionPointerForDelegate([OsiLoadProbe]::errorDelegate)
$memory = [Runtime.InteropServices.Marshal]::AllocHGlobal([Runtime.InteropServices.Marshal]::SizeOf([type][OsiLoadProbe+Init]))
try {
    [Runtime.InteropServices.Marshal]::StructureToPtr($init, $memory, $false)
    [OsiLoadProbe]::Register($osiris, $memory)
    if (![OsiLoadProbe]::OpenLog($osiris, $log, 'w')) { throw 'Could not open Osiris load log.' }
    $loaded = [OsiLoadProbe]::Load($osiris, $story)
    $version = New-Object Text.StringBuilder 128
    $hasVersion = [OsiLoadProbe]::GetVersion($osiris, $version)
    if ($loaded) {
        [OsiLoadProbe]::GenerateFunctionList($osiris)
        [OsiLoadProbe]::GenerateNodeList($osiris)
    }
    [OsiLoadProbe]::CloseLog($osiris)
    if (!$loaded) { throw "OsirisDLL rejected the story file. See $log" }
    if (!$hasVersion) { throw 'Story loaded but GetStoryVersionString failed.' }
    $functionCount = (Select-String -LiteralPath $log -Pattern '^(Osi_proc|Osi_query|Int_proc|Int_query|DIV_query|event|call)\s').Count
    $nodeCount = (Select-String -LiteralPath $log -Pattern '^\s*\d+:').Count
    if ($functionCount -lt $MinimumFunctions -or $nodeCount -lt $MinimumNodes) {
        throw "Story is structurally incomplete: $functionCount functions, $nodeCount nodes. Minimum expected: $MinimumFunctions functions, $MinimumNodes nodes."
    }
    Write-Host 'OSIRIS LOAD SUCCEEDED'
    Write-Host "Story: $story"
    Write-Host "Story version: $version"
    Write-Host "Functions: $functionCount; Rete nodes: $nodeCount"
    Write-Host "Load log: $log"
}
finally {
    [Runtime.InteropServices.Marshal]::FreeHGlobal($memory)
}
