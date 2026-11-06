param(
    [switch]$Long,
    [switch]$AddressSpace,
    [ValidateSet('all','native-no-reload','fallback-reload','native-reload')]
    [string]$Control = 'all'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location -LiteralPath $root
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
$dll = Join-Path $root 'Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll'
if ($AddressSpace -and -not (Test-Path (Join-Path $root 'Tools/MemoryProbe/bin/Release/net10.0/MemoryProbe.exe'))) {
    throw 'Build Tools/MemoryProbe in Release before address-space diagnostics.'
}
$qa = Join-Path $root 'Tools/LevelAuthoring/bin/Release/net10.0/qa'
$folder = Join-Path $qa ('reload-memory-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $folder | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
function ProtectedHashes {
    $files = @(Get-ChildItem Game/Content/Levels -File -Filter '*.json')
    $files += @(Get-ChildItem Engine.Render/Shaders -File)
    $files += @(Get-ChildItem Engine.Physics -Recurse -File -Filter '*.cs' | Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]')
    $files += Get-Item Engine.Core/Time/FixedTimestep.cs
    @($files | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ Path = $_.FullName; Hash = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
}
$before = ProtectedHashes
$before | ConvertTo-Json | Set-Content (Join-Path $folder 'protected-before.json')
$runs = @(
    @{Name='native-no-reload'; Options=@('--native-ui','--no-reload')},
    @{Name='fallback-reload'; Options=@()},
    @{Name='native-reload'; Options=@('--native-ui')}
)
foreach ($run in $runs) {
    if ($Control -ne 'all' -and $Control -ne $run.Name) { continue }
    $arguments = @($dll,'soak-gameplay','--puzzles','--memory-trace','--memory-stages') + $run.Options
    if ($Long) { $arguments += '--long' }
    if ($AddressSpace) { $arguments += '--address-snapshots' }
    $start = [DateTime]::UtcNow
    Write-Host "[Memory] Starting $($run.Name)"
    $log = Join-Path $folder ($run.Name + '.log')
    & $dotnet @arguments 2>&1 | Tee-Object -FilePath $log | Out-Host
    $exit = $LASTEXITCODE
    $report = Get-ChildItem $qa -Directory -Filter 'gameplay-soak-*' |
        Where-Object CreationTimeUtc -GE $start.AddSeconds(-1) | Sort-Object CreationTimeUtc -Descending | Select-Object -First 1
    $results.Add([pscustomobject]@{ Name=$run.Name; Arguments=$arguments; ExitCode=$exit; Report=$report.FullName;
        StartedUtc=$start.ToString('O'); Seconds=([DateTime]::UtcNow-$start).TotalSeconds })
    $results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $folder 'commands.json')
    if ($report -and (Test-Path (Join-Path $report.FullName 'summary.json'))) {
        $s = Get-Content (Join-Path $report.FullName 'summary.json') -Raw | ConvertFrom-Json
        Write-Host "[Memory] $($run.Name) cycles=$($s.CompletedCycles) growth=$([Math]::Round(($s.LastSettledPrivateBytes-$s.FirstSettledPrivateBytes)/1MB,2)) MiB"
    }
}
$after = ProtectedHashes
$after | ConvertTo-Json | Set-Content (Join-Path $folder 'protected-after.json')
$changed = @(Compare-Object $before $after -Property Path,Hash)
[pscustomobject]@{ DiagnosticOnly=$true; Long=[bool]$Long; AddressSpace=[bool]$AddressSpace; Control=$Control; Results=$results; ProtectedInputsUnchanged=($changed.Count -eq 0); Changed=$changed } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $folder 'summary.json')
Write-Host "[Memory] Reports: $folder"
if ($changed.Count -ne 0 -or @($results | Where-Object ExitCode -NE 0).Count -ne 0) { exit 1 }
