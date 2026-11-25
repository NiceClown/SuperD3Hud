param(
    [switch]$Run,
    [switch]$Console
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$binDir = Join-Path $root 'bin'
$out = Join-Path $binDir 'SuperD3Hud.exe'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found.' }

$fw = Split-Path -Parent $csc
$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.Windows.Forms.dll',
    'System.Drawing.dll',
    'System.Web.Extensions.dll',
    'System.IO.Compression.dll',
    'System.IO.Compression.FileSystem.dll'
)

New-Item -ItemType Directory -Force -Path $binDir | Out-Null

# The TurboHUD compatibility host needs the public plugin API and SharpDX.
# Build the API from the open interface sources when it is missing.
$api = Join-Path $binDir 'Turbo.Plugins.dll'
if (-not (Test-Path $api)) {
    Write-Host 'Plugin API not found, building it from the public interfaces...'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'compat\build-api.ps1')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $api)) { throw 'Could not build Turbo.Plugins.dll.' }
}

$defaultApi = Join-Path $binDir 'Turbo.Plugins.Default.dll'
if (-not (Test-Path $defaultApi)) {
    Write-Host 'Plugin base class not found, building it...'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'compat\build-default.ps1')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $defaultApi)) { throw 'Could not build Turbo.Plugins.Default.dll.' }
}

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/debug:pdbonly',
    ('/out:' + $out),
    ('/win32manifest:' + (Join-Path $srcDir 'app.manifest'))
)

$icon = Join-Path $srcDir 'app.ico'
if (Test-Path $icon) { $cscArgs += ('/win32icon:' + $icon) }
foreach ($r in $refs) {
    $cscArgs += ('/r:' + (Join-Path $fw $r))
}
foreach ($name in @('Turbo.Plugins.dll','SharpDX.dll','SharpDX.Direct2D1.dll',
                    'SharpDX.Direct3D11.dll','SharpDX.DXGI.dll','SharpDX.Mathematics.dll',
                    'SharpDX.DirectInput.dll')) {
    $path = Join-Path $binDir $name
    if (Test-Path $path) { $cscArgs += ('/r:' + $path) }
}

$sources = Get-ChildItem (Join-Path $srcDir '*.cs') -Recurse | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw 'No source files found.' }

Write-Host ('Compiling ' + $sources.Count + ' files -> ' + $out)
$output = & $csc $cscArgs $sources 2>&1
$code = $LASTEXITCODE
foreach ($line in $output) { Write-Host $line }
if ($code -ne 0) { throw ('csc failed with exit code ' + $code) }

Copy-Item (Join-Path $root 'README.md') (Join-Path $binDir 'README.md') -Force -ErrorAction SilentlyContinue

# TurboHUD example plugins travel with the program, so a fresh install can
# create its plugin folder from them.
$examples = Join-Path $root 'examples\turbohud'
if (Test-Path $examples) {
    $exampleTarget = Join-Path $binDir 'examples\turbohud'
    New-Item -ItemType Directory -Force -Path $exampleTarget | Out-Null
    Copy-Item (Join-Path $examples '*.cs') $exampleTarget -Force -ErrorAction SilentlyContinue
}

$roslynTarget = Join-Path $binDir 'roslyn\csc.exe'
if (-not (Test-Path $roslynTarget)) {
    $roslynSource = 'D:\ARL\th\TurboHUD_LightningMOD_For_D3\TurboHUD\roslyn'
    if (Test-Path $roslynSource) {
        Write-Host 'Copying bundled Roslyn compiler...'
        Copy-Item $roslynSource (Join-Path $binDir 'roslyn') -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# A shortcut with the application icon is placed next to the project folder,
# so the launcher is easy to find and to start.
$shortcut = Join-Path (Split-Path -Parent $root) 'SuperD3Hud.lnk'
try {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = $out
    $link.WorkingDirectory = $binDir
    $link.IconLocation = ($out + ',0')
    $link.Description = 'SuperD3Hud — локальный лаунчер Diablo III'
    $link.Save()
    Write-Host ('Shortcut: ' + $shortcut)
} catch {
    Write-Host ('Shortcut not created: ' + $_.Exception.Message)
}

Write-Host 'Build OK.'

if ($Run) {
    Write-Host 'Running self-test...'
    & $out --selftest
    $code = $LASTEXITCODE
    $report = Join-Path $root 'config\selftest.txt'
    if (Test-Path $report) { Get-Content $report }
    exit $code
}
