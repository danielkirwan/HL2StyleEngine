$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$source = Join-Path $root 'HS2Editor/bin/Release/net10.0'
$review = Join-Path $root ('Tools/LevelAuthoring/bin/Release/net10.0/qa/lighting-editor-review-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$app = Join-Path $review 'App'
$content = Join-Path $review 'Content'
New-Item -ItemType Directory -Path $app, (Join-Path $content 'Levels') -Force | Out-Null
$protected = @((Join-Path $root 'HS2Project.json'), (Join-Path $source 'imgui.ini'),
    (Join-Path $root 'Game/Content/Levels/sixRoomShadowTest.json'))
@($protected | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object {
    [pscustomobject]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
}) | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $review 'source-before.json')
Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $app
foreach ($folder in @('Shaders', 'runtimes')) {
    Copy-Item -LiteralPath (Join-Path $source $folder) -Destination $app -Recurse
}
Copy-Item -LiteralPath (Join-Path $root 'HL2StyleEngine.sln') -Destination $review

# Share model inputs without duplicating the asset library; all editable documents/layouts are private copies.
New-Item -ItemType Junction -Path (Join-Path $content 'Models') -Target (Join-Path $root 'Game/Content/Models') | Out-Null
# Game resolves runtime content from its executable directory; editor uses the private project root.
New-Item -ItemType Junction -Path (Join-Path $app 'Content') -Target $content | Out-Null
foreach ($folder in @('Prefabs', 'UI', 'Materials', 'Scripts', 'Animations')) {
    $original = Join-Path $root ('Game/Content/' + $folder)
    if (Test-Path -LiteralPath $original) { Copy-Item -LiteralPath $original -Destination $content -Recurse }
}
Get-ChildItem -LiteralPath (Join-Path $root 'Game/Content/Levels') -File -Filter 'sixRoomShadowTest*' |
    Copy-Item -Destination (Join-Path $content 'Levels')
$project = Get-Content -LiteralPath (Join-Path $root 'HS2Project.json') -Raw | ConvertFrom-Json
$project.ProjectName = 'Lighting acceptance review (isolated)'
$project.ContentRoot = 'Content'
$project.StartupLevel = 'Content/Levels/sixRoomShadowTest.json'
$project.RecentLevels = @($project.StartupLevel)
$project | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $review 'HS2Project.json')
Write-Output $review
