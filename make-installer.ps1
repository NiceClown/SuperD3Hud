param(
    [switch]$SkipBuild,
    [switch]$NoLaunch
)

# Собирает переносимый установщик SuperD3Hud.
#
# В один файл SuperD3Hud-Setup.exe зашивается архив со всем, что нужно
# лаунчеру: сам exe, библиотеки плагинов TurboHUD, компилятор Roslyn,
# примеры плагинов и настройки по умолчанию. Установщик распаковывает всё
# рядом с собой, поэтому папку можно перенести на любой компьютер.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$binDir = Join-Path $root 'bin'
$installerDir = Join-Path $root 'installer'
$stageDir = Join-Path $installerDir 'stage'
$payload = Join-Path $installerDir 'payload.zip'
$setupExe = Join-Path $root 'SuperD3Hud-Setup.exe'

if (-not $SkipBuild) {
    Write-Host 'Building the launcher...'
    Get-Process SuperD3Hud -ErrorAction SilentlyContinue | Stop-Process -Force
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'The launcher build failed.' }
}

Write-Host 'Collecting the files...'
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

function Add-File([string]$relative, [string]$source) {
    if (-not (Test-Path $source)) {
        Write-Host ('  skipped (not found): ' + $relative)
        return
    }
    $target = Join-Path $stageDir $relative
    $dir = Split-Path -Parent $target
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Copy-Item $source $target -Force
}

# The program itself and the libraries it needs.
Add-File 'SuperD3Hud.exe' (Join-Path $binDir 'SuperD3Hud.exe')
Add-File 'Turbo.Plugins.dll' (Join-Path $binDir 'Turbo.Plugins.dll')
Add-File 'Turbo.Plugins.Default.dll' (Join-Path $binDir 'Turbo.Plugins.Default.dll')
foreach ($dll in @('SharpDX.dll','SharpDX.Direct2D1.dll','SharpDX.Direct3D11.dll',
                   'SharpDX.DXGI.dll','SharpDX.Mathematics.dll','SharpDX.DirectInput.dll')) {
    Add-File $dll (Join-Path $binDir $dll)
}
Add-File 'README.md' (Join-Path $root 'README.md')
Add-File 'LICENSE' (Join-Path $root 'LICENSE')

# The compiler that builds the plugins of the user on their own computer.
$roslynSource = Join-Path $binDir 'roslyn'
if (-not (Test-Path $roslynSource)) {
    $roslynSource = 'D:\ARL\th\TurboHUD_LightningMOD_For_D3\TurboHUD\roslyn'
}
if (-not (Test-Path $roslynSource)) { throw 'The Roslyn compiler is missing; the plugins would not build.' }
$roslynTarget = Join-Path $stageDir 'roslyn'
New-Item -ItemType Directory -Force -Path $roslynTarget | Out-Null
Copy-Item (Join-Path $roslynSource '*') $roslynTarget -Recurse -Force

# The examples of the plugins and the folders for the own plugins.
$examplesSource = Join-Path $root 'examples\turbohud'
if (Test-Path $examplesSource) {
    $examplesTarget = Join-Path $stageDir 'examples\turbohud'
    New-Item -ItemType Directory -Force -Path $examplesTarget | Out-Null
    Copy-Item (Join-Path $examplesSource '*.cs') $examplesTarget -Force
}
foreach ($name in @('plugins-turbohud','plugins-local')) {
    $dir = Join-Path $stageDir $name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $readme = Join-Path $binDir ($name + '\readme.txt')
    if (Test-Path $readme) { Copy-Item $readme (Join-Path $dir 'readme.txt') -Force }
}

# The settings. They are written from scratch, so no path of this computer
# ends up in the package: the game and the launcher find each other on their
# own on the computer where the package is unpacked.
$configDir = Join-Path $stageDir 'config'
New-Item -ItemType Directory -Force -Path $configDir | Out-Null

$settings = @'
[general]
HudRoot=
HudExePath=
D3Path=
LaunchMode=engine
CustomLauncher=
MinimizeToTray=1
CheckForUpdates=0
UpdateRepo=
ShortcutCreated=0
AdvancedMode=0

[map]
Enabled=1
ShowMinimap=1
ShowPanel=0
Scale=1
OffsetX=0
OffsetY=0
Opacity=45
PanelX=-1
PanelY=-1
PanelSize=340
PanelLocked=0
ShowLargeMap=1
LargeScale=1.0
LargeOffsetX=0
LargeOffsetY=0
LargeAuto=1
LargeFit=0
LargeFollow=1
Configured=1

[overlay]
Enabled=1
ShowBuffs=1
ShowSkills=1
ShowHealth=0
ShowInventory=1
ShowMonsterMarkers=1
ShowLootMarkers=1
ShowDamageMeter=1
Opacity=220
Scale=1
BuffCorner=1
BuffOffsetX=16
BuffOffsetY=120
Cooldown0=0
Cooldown1=0
Cooldown2=0
Cooldown3=0
Cooldown4=0
Cooldown5=0
CooldownDescriptor=0
CooldownStartDescriptor=0
LargeMapKey=9
WaypointKey=77
'@
[System.IO.File]::WriteAllText((Join-Path $configDir 'settings.ini'), $settings,
                                (New-Object System.Text.UTF8Encoding($false)))

# The list of the plugins of TurboHUD with their state: the same set that the
# launcher uses here, so the fork behaves the same on any computer.
Add-File 'config\turbohud_state.txt' (Join-Path $binDir 'config\turbohud_state.txt')
Add-File 'config\plugin_descriptions.json' (Join-Path $binDir 'config\plugin_descriptions.json')
Add-File 'config\macros.json' (Join-Path $binDir 'config\macros.json')
Add-File 'config\plugins_local_state.txt' (Join-Path $binDir 'config\plugins_local_state.txt')

Write-Host 'Packing the archive...'
if (Test-Path $payload) { Remove-Item $payload -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stageDir, $payload, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$sizeMb = [math]::Round((Get-Item $payload).Length / 1MB, 1)
Write-Host ('Archive: ' + $sizeMb + ' MB')

Write-Host 'Compiling the setup program...'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found.' }
$fw = Split-Path -Parent $csc

if (Test-Path $setupExe) { Remove-Item $setupExe -Force }
$args = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    ('/out:' + $setupExe),
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.IO.Compression.dll',
    '/r:System.IO.Compression.FileSystem.dll',
    '/codepage:65001',
    ('/resource:' + $payload + ',SuperD3Hud.Setup.payload.zip'),
    (Join-Path $installerDir 'Setup.cs')
)
$output = & $csc @args 2>&1
$code = $LASTEXITCODE
foreach ($line in $output) { Write-Host $line }
if ($code -ne 0) { throw 'csc failed with exit code ' + $code }

Remove-Item $payload -Force -ErrorAction SilentlyContinue
Remove-Item $stageDir -Recurse -Force -ErrorAction SilentlyContinue

$setupSize = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
Write-Host ('Installer: ' + $setupExe + ' (' + $setupSize + ' MB)')
Write-Host 'INSTALLER: OK'
