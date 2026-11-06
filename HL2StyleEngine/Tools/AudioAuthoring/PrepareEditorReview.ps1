$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
$source = Join-Path $root 'HS2Editor/bin/Release/net10.0'
$review = Join-Path $root ('Tools/LevelAuthoring/bin/Release/net10.0/qa/audio-editor-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$app = Join-Path $review 'App'
$content = Join-Path $review 'Content'
New-Item -ItemType Directory -Path $app, "$content/Levels" -Force | Out-Null
Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $app
foreach ($folder in @('Shaders', 'runtimes')) { Copy-Item -LiteralPath "$source/$folder" -Destination $app -Recurse }
Copy-Item -LiteralPath "$root/HL2StyleEngine.sln" -Destination $review
New-Item -ItemType Junction -Path "$content/Models" -Target "$root/Game/Content/Models" | Out-Null
New-Item -ItemType Junction -Path "$app/Content" -Target $content | Out-Null
foreach ($folder in @('Audio', 'Prefabs', 'UI', 'Materials', 'Scripts', 'Animations')) {
    if (Test-Path -LiteralPath "$root/Game/Content/$folder") { Copy-Item -LiteralPath "$root/Game/Content/$folder" -Destination $content -Recurse }
}
Copy-Item -LiteralPath "$root/Game/Content/Levels/audioTest.json" -Destination "$content/Levels"
$project = Get-Content -LiteralPath "$root/HS2Project.json" -Raw | ConvertFrom-Json
$project.ProjectName = 'Audio review (isolated)'
$project.ContentRoot = 'Content'
$project.StartupLevel = 'Content/Levels/audioTest.json'
$project.RecentLevels = @($project.StartupLevel)
$project | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath "$review/HS2Project.json"
Write-Output $review
