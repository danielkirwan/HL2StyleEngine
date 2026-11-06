$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
$source = Join-Path $root 'Game/Content/Levels/pbrMaterialTest.json'
$destination = Join-Path $root 'Game/Content/Levels/audioTest.json'
if (Test-Path -LiteralPath $destination) { throw 'audioTest.json already exists; preserve authored edits.' }
$level = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
$level.Entities = @($level.Entities | Where-Object { $_.Name -notmatch '^(Dielectric_|Metal_|ScalarNoNormals_)' })
foreach ($entity in $level.Entities) {
    $cue = switch ($entity.Name) {
        'SwitchedScreen' { 'machine_hum' }
        'MovableComparisonCrate' { 'machine_hum' }
        'Ceiling_-2_-2' { 'basement_drone' }
    }
    if ($cue) {
        $entity | Add-Member AudioEmitter ([ordered]@{ Cue=$cue; AutoPlay=$true; Gain=1.0 })
    }
}
$level | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $destination -Encoding utf8
Write-Output $destination
