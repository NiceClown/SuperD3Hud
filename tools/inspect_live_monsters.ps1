$game = Get-Process -Name "Diablo III64" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($game -eq $null) { Write-Host "Game not found"; exit 1 }

$asm = [System.Reflection.Assembly]::LoadFrom("bin/SuperD3Hud.exe")
$readerType = $asm.GetType("SuperD3Hud.Game.GameReader")
$memType = $asm.GetType("SuperD3Hud.GameMemoryReader")
$actorModType = $asm.GetType("SuperD3Hud.Game.ActorModule")

$mem = [System.Activator]::CreateInstance($memType, @($game.Id))
$baseAddr = $game.MainModule.BaseAddress
$reader = [System.Activator]::CreateInstance($readerType, @($mem, $baseAddr))

$playerRecord = $null
$playerRef = [System.Type]::GetType("SuperD3Hud.Game.ActorRecord, SuperD3Hud")
$args = @([ref]$null)
$method = $readerType.GetMethod("TryReadLocalPlayer")
$hasPlayer = $method.Invoke($reader, $args)
$player = $args[0]

Write-Host "Player: WorldId=$($player.WorldId), Pos=($($player.X), $($player.Y))"

$actorModule = [System.Activator]::CreateInstance($actorModType)
$snapshot = $actorModule.Poll($reader, $player.WorldId, $true)

Write-Host "Monsters total: $($snapshot.Monsters.Count)"
Write-Host "SpecialArea: $($snapshot.SpecialArea), RiftPercentage: $($snapshot.RiftPercentage)"

foreach ($m in $snapshot.Monsters) {
    if ($m.IsElite -or $m.IsBoss) {
        $aff = [string]::Join(", ", $m.AffixNames)
        Write-Host "ELITE: Sno=$($m.Sno), Rarity=$($m.Rarity), IsBoss=$($m.IsBoss), Label='$($m.Label)', Affixes=[$aff]"
    }
}
$mem.Dispose()
