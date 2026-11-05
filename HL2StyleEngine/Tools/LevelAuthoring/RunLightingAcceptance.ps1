param(
    [ValidateSet('Lighting', 'ValidationDebug', 'Baseline', 'LongControls')]
    [string]$Phase = 'Lighting'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location -LiteralPath $root
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
$dll = Join-Path $root 'Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll'
$qa = Join-Path $root 'Tools/LevelAuthoring/bin/Release/net10.0/qa'
$folder = Join-Path $qa ('lighting-acceptance-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + $Phase)
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$results = [System.Collections.Generic.List[object]]::new()

function Invoke-Check([string]$name, [string]$exe, [string[]]$arguments) {
    $log = Join-Path $folder ($name + '.log')
    $start = [DateTime]::UtcNow
    Write-Host "[Acceptance] Starting $name"
    & $exe @arguments 2>&1 | Tee-Object -FilePath $log | Out-Host
    $code = $LASTEXITCODE
    $results.Add([pscustomobject]@{
        Name = $name; Executable = $exe; Arguments = $arguments; StartedUtc = $start.ToString('O')
        Seconds = ([DateTime]::UtcNow - $start).TotalSeconds; ExitCode = $code; Log = $log
    })
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $folder 'commands.json')
    Write-Host "[Acceptance] $name exit=$code"
    return $code
}

function Get-ProtectedHashes {
    $files = @(Get-ChildItem -LiteralPath (Join-Path $root 'Game/Content/Levels') -File -Filter '*.json')
    $files += @(Get-ChildItem -LiteralPath (Join-Path $root 'Engine.Render/Shaders') -File)
    $files += @(Get-ChildItem -LiteralPath (Join-Path $root 'Engine.Physics') -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
    $files += Get-Item -LiteralPath (Join-Path $root 'Engine.Core/Time/FixedTimestep.cs')
    @($files | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ Path = $_.FullName; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}

$before = Get-ProtectedHashes
$before | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $folder 'protected-before.json')
$build = Invoke-Check 'build-release' $dotnet @('build', 'Tools/LevelAuthoring/LevelAuthoring.csproj', '-c', 'Release', '--no-restore')
if ($build -ne 0) { throw "Build failed. Reports: $folder" }

if ($Phase -eq 'Lighting') {
    $checks = @(
        'check-imgui-layout',
        'validate-pbr-materials', 'validate-pbr-lighting', 'validate-emission',
        'validate-indirect-bake', 'validate-reflections', 'validate-room-reflections',
        'validate-room-transitions', 'validate-reflection-streaming',
        'validate-lighting-stability', 'validate-shadow-test', 'validate-shadow-contact',
        'validate-multi-shadows', 'validate-atmosphere', 'check-atmosphere-pixels', 'check-lighting-switch-pixels', 'check-shadow-pixels',
        'validate-flashlight-mount', 'check-floor-lighting', 'check-lighting-allocations',
        'validate-lighting-dependencies', 'check-lighting-dependency-images --baked',
        'validate-pbr-legacy', 'capture-shadow-test',
        'validate-shadow-package Game/bin/Release/net10.0',
        'validate-shadow-package HS2Editor/bin/Release/net10.0',
        'check-texture-destination', 'check-texture-transitions', 'check-texture-editor',
        'validate-physics-replay atmosphereTest Tools/LevelAuthoring/bin/Release/net10.0/qa/physics-replay-20261028-111003-380',
        'validate-physics-replay sixRoomShadowTest Tools/LevelAuthoring/bin/Release/net10.0/qa/physics-replay-20261028-111902-477'
    )
    foreach ($check in $checks) {
        $name = $check.Replace(' ', '_').Replace('/', '_')
        # These fixed tool arguments have no embedded spaces or quoting requirements.
        $null = Invoke-Check $name $dotnet (@($dll) + $check.Split(' '))
    }
} elseif ($Phase -eq 'ValidationDebug') {
    foreach ($check in @('validate-multi-shadows', 'validate-pbr-shadows', 'validate-rendering', 'validate-atmosphere', 'check-atmosphere-pixels')) {
        $null = Invoke-Check $check $dotnet @($dll, $check)
    }
} elseif ($Phase -eq 'Baseline') {
    foreach ($bat in @('RunGameplayStabilityTests.bat', 'RunFramePacingTests.bat')) {
        # EOF lets the existing BAT's final pause return; its gates/exit status are unchanged.
        $null = Invoke-Check $bat $env:ComSpec @('/d', '/c', ('"{0}" <nul' -f (Join-Path $root $bat)))
    }
} else {
    foreach ($check in @(
        'validate-atmosphere',
        'profile-frame-pacing --no-debug',
        'profile-frame-pacing --long --reference --no-debug',
        'profile-frame-pacing --long --no-debug',
        'soak-gameplay --native-ui --puzzles --long --memory-trace'
    )) {
        $null = Invoke-Check ($check.Replace(' ', '_')) $dotnet (@($dll) + $check.Split(' '))
    }
}

$after = Get-ProtectedHashes
$after | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $folder 'protected-after.json')
$changed = @(Compare-Object $before $after -Property Path, Sha256)
$failed = @($results | Where-Object ExitCode -ne 0)
[pscustomobject]@{
    Phase = $Phase; Commands = $results.Count; FailedCommands = $failed.Count
    ProtectedInputsUnchanged = $changed.Count -eq 0; ChangedInputs = $changed
    ManualAcceptance = 'Not performed by this runner. Automated results do not clear Gate L or Gate B alone.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $folder 'summary.json')
Write-Host "[Acceptance] Reports: $folder"
if ($failed.Count -ne 0 -or $changed.Count -ne 0) { exit 1 }
