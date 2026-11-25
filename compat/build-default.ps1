param(
    [switch]$Clean
)

# Builds Turbo.Plugins.Default.dll: the plugin base class and default decorators
# that TurboHUD plugins inherit from and use.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'baseplugin'
$outDir = Join-Path $root '..\bin'
$out = Join-Path $outDir 'Turbo.Plugins.Default.dll'
$api = Join-Path $outDir 'Turbo.Plugins.dll'

$roslynCsc = Join-Path $outDir 'roslyn\csc.exe'
if (-not (Test-Path $roslynCsc)) {
    $roslynCsc = 'D:\ARL\th\TurboHUD_LightningMOD_For_D3\TurboHUD\roslyn\csc.exe'
}

$defaultSourceDir = 'D:\ARL\th\TurboHUD_LightningMOD_For_D3\TurboHUD\plugins\Default'
$useFullDefault = (Test-Path $roslynCsc) -and (Test-Path $defaultSourceDir)

if ($Clean -and (Test-Path $out)) { Remove-Item $out -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not (Test-Path $api)) { throw 'Turbo.Plugins.dll is missing. Build the API first.' }

if ($useFullDefault) {
    Write-Host "Building full Turbo.Plugins.Default.dll from $defaultSourceDir using Roslyn..."
    $sources = (Get-ChildItem -Path $defaultSourceDir -Recurse -Filter '*.cs').FullName
    $rsp = Join-Path $outDir 'build_default.rsp'
    $lines = @(
        '/nologo',
        '/target:library',
        '/optimize+',
        ('/out:' + $out),
        '/r:System.dll',
        '/r:System.Core.dll',
        '/r:System.Drawing.dll',
        '/r:System.Windows.Forms.dll',
        ('/r:' + $api),
        ('/r:' + (Join-Path $outDir 'SharpDX.dll')),
        ('/r:' + (Join-Path $outDir 'SharpDX.Direct2D1.dll')),
        ('/r:' + (Join-Path $outDir 'SharpDX.Direct3D11.dll')),
        ('/r:' + (Join-Path $outDir 'SharpDX.DXGI.dll')),
        ('/r:' + (Join-Path $outDir 'SharpDX.Mathematics.dll')),
        ('/r:' + (Join-Path $outDir 'SharpDX.DirectInput.dll'))
    )
    $lines += $sources
    Set-Content $rsp $lines -Encoding UTF8
    $output = & $roslynCsc ("@" + $rsp) 2>&1
    $code = $LASTEXITCODE
    Remove-Item $rsp -Force -ErrorAction SilentlyContinue
    if ($code -ne 0) {
        Write-Host "Roslyn failed, falling back to minimal BasePlugin..."
        $useFullDefault = $false
    }
}

if (-not $useFullDefault) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found.' }

    $sources = (Get-ChildItem $srcDir -Filter *.cs).FullName
    $cscArgs = @('/nologo','/target:library','/optimize+',('/out:' + $out))
    $cscArgs += ('/r:' + $api)
    $cscArgs += '/r:System.dll'
    $cscArgs += '/r:System.Core.dll'

    Write-Host ('Compiling ' + $sources.Count + ' files -> ' + $out)
    $output = & $csc $cscArgs $sources 2>&1
    $code = $LASTEXITCODE
    foreach ($line in $output) { Write-Host $line }
    if ($code -ne 0) { throw ('csc failed with exit code ' + $code) }
}

Write-Host ('Plugin base built: ' + $out)
