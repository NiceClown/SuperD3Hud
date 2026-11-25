param(
    [switch]$Clean
)

# Builds Turbo.Plugins.dll from the public interface sources of the plugin API.
#
# The interface files are plain C# declarations: the contract that plugins are
# written against. They are compiled here into our own assembly, so plugins can
# be loaded without the vendor's protected engine. No engine binary is used,
# referenced or redistributed.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ifaceDir = Join-Path $root 'interfaces'
$sharpDir = Join-Path $root 'sharpdx'
$outDir = Join-Path $root '..\bin'
$out = Join-Path $outDir 'Turbo.Plugins.dll'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found.' }

if ($Clean -and (Test-Path $out)) { Remove-Item $out -Force }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$refs = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll')
foreach ($name in @('SharpDX.dll','SharpDX.Direct2D1.dll','SharpDX.Direct3D11.dll',
                    'SharpDX.DXGI.dll','SharpDX.Mathematics.dll','SharpDX.DirectInput.dll')) {
    $path = Join-Path $sharpDir $name
    if (Test-Path $path) { $refs += $path }
}

$sources = Get-ChildItem $ifaceDir -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw 'No interface sources found.' }

$cscArgs = @('/nologo','/target:library','/optimize+',('/out:' + $out))
foreach ($r in $refs) { $cscArgs += ('/r:' + $r) }

Write-Host ('Compiling ' + $sources.Count + ' interface files -> ' + $out)
$output = & $csc $cscArgs $sources 2>&1
$code = $LASTEXITCODE
foreach ($line in $output) {
    if ($line -notmatch 'warning CS1692') { Write-Host $line }
}
if ($code -ne 0) { throw ('csc failed with exit code ' + $code) }

# SharpDX is an MIT-licensed open library; its binaries travel with the API.
foreach ($name in @('SharpDX.dll','SharpDX.Direct2D1.dll','SharpDX.Direct3D11.dll',
                    'SharpDX.DXGI.dll','SharpDX.Mathematics.dll','SharpDX.DirectInput.dll')) {
    $src = Join-Path $sharpDir $name
    if (Test-Path $src) { Copy-Item $src (Join-Path $outDir $name) -Force }
}

Write-Host ('Plugin API built: ' + $out)
