param([string]$ZigExe)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $root 'assets\QuickSimplifyIcon.ico')) -or
    -not (Test-Path -LiteralPath (Join-Path $root 'assets\QuickSimplifyTraditional.ico'))) {
    & (Join-Path $PSScriptRoot 'Generate-Icons.ps1')
}

if (-not $ZigExe) {
    $zigCommand = Get-Command zig.exe -ErrorAction SilentlyContinue
    if (-not $zigCommand) { throw 'Zig 0.15.2 is required. Pass -ZigExe with the path to zig.exe.' }
    $ZigExe = $zigCommand.Source
}
if (-not (Test-Path -LiteralPath $ZigExe)) { throw "zig.exe was not found: $ZigExe" }

$minhook = Join-Path $root 'third_party\minhook'
$sources = @(
    (Join-Path $root 'src\QuickSimplifyHook.c'),
    (Join-Path $minhook 'src\buffer.c'),
    (Join-Path $minhook 'src\hook.c'),
    (Join-Path $minhook 'src\trampoline.c'),
    (Join-Path $minhook 'src\hde\hde64.c')
)
& $ZigExe cc -target x86_64-windows-gnu -shared -O2 `
    -I (Join-Path $minhook 'include') -I (Join-Path $minhook 'src') `
    @sources -luser32 -limm32 -lole32 -o (Join-Path $dist 'QuickSimplifyHook.dll')
if ($LASTEXITCODE -ne 0) { throw 'Native DLL build failed' }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw '.NET Framework 4.x C# compiler was not found' }
& $csc /nologo /target:winexe `
    ('/out:' + (Join-Path $dist 'QuickSimplifyController.exe')) `
    ('/win32icon:' + (Join-Path $root 'assets\QuickSimplifyIcon.ico')) `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll `
    (Join-Path $root 'src\QuickSimplifyController.cs')
if ($LASTEXITCODE -ne 0) { throw 'Controller build failed' }

Copy-Item -LiteralPath (Join-Path $root 'assets\QuickSimplifyIcon.ico'),(Join-Path $root 'assets\QuickSimplifyTraditional.ico') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $minhook 'LICENSE.txt') -Destination (Join-Path $dist 'MinHook-LICENSE.txt') -Force
Write-Output "Portable files built in $dist"
