param(
    [Parameter(Mandatory = $true)][string]$Out
)
# Compiles all sources into a throw-away exe without touching bin\SuperD3Hud.exe.
# Used for compile checks while the real exe may be running or other edits are in progress.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$bin = Join-Path $root 'bin'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fw = Split-Path -Parent $csc
$a = @('/nologo', '/target:exe', '/platform:anycpu', '/optimize+', ('/out:' + $Out),
       ('/win32manifest:' + (Join-Path $root 'src\app.manifest')))
foreach ($r in 'System.dll','System.Core.dll','System.Windows.Forms.dll','System.Drawing.dll',
               'System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') {
    $a += ('/r:' + (Join-Path $fw $r))
}
foreach ($n in 'Turbo.Plugins.dll','SharpDX.dll','SharpDX.Direct2D1.dll','SharpDX.Direct3D11.dll',
               'SharpDX.DXGI.dll','SharpDX.Mathematics.dll','SharpDX.DirectInput.dll') {
    $a += ('/r:' + (Join-Path $bin $n))
}
$src = Get-ChildItem (Join-Path $root 'src\*.cs') -Recurse | ForEach-Object { $_.FullName }
& $csc $a $src
exit $LASTEXITCODE
