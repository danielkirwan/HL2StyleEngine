param([switch]$Long)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location -LiteralPath $root
$qa = Join-Path $root 'Tools/LevelAuthoring/bin/Release/net10.0/qa'
$folder = Join-Path $qa ('scheduling-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $folder | Out-Null
$script = Join-Path $PSScriptRoot 'RecordSchedulingTrace.ps1'
$instance = 'HS2Acceptance' + [Guid]::NewGuid().ToString('N')
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
$probe = Join-Path $root 'Tools/SchedulingProbe/bin/Release/net10.0/SchedulingProbe.dll'
if (-not (Test-Path -LiteralPath $probe)) { throw 'Build the scheduling helper and LevelAuthoring before running; no builds during capture.' }
$helper = $null
$start = [DateTime]::UtcNow
$exit = $null
try {
    # Only the recorder elevates. The real engine workload retains ordinary process privileges.
    $helper = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-File', ('"{0}"' -f $script), '-Folder', ('"{0}"' -f $folder), '-Instance', $instance)
    $readyDeadline = [DateTime]::UtcNow.AddSeconds(90)
    while (-not (Test-Path -LiteralPath (Join-Path $folder 'ready'))) {
        if ((Test-Path -LiteralPath (Join-Path $folder 'finished')) -or [DateTime]::UtcNow -gt $readyDeadline) {
            throw "Recording did not start; inspect $folder"
        }
        Start-Sleep -Milliseconds 250
    }
    $arguments = @('Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll', 'profile-frame-pacing', '--reference', '--no-debug')
    if ($Long) { $arguments += '--long' }
    Write-Host '[Scheduling] Recording the normal full-detail/VSync workload; diagnostic run only.'
    & $dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $folder 'workload.log') | Out-Host
    $exit = $LASTEXITCODE
} finally {
    New-Item -ItemType File -Path (Join-Path $folder 'stop') -Force | Out-Null
    if ($helper -and -not $helper.WaitForExit(120000)) {
        Write-Warning "Recorder has not exited. Its nine-minute guard stops only $instance. Inspect its logs before another workload."
    }
    [pscustomobject]@{ Instance=$instance; StartedUtc=$start.ToString('O'); Seconds=([DateTime]::UtcNow-$start).TotalSeconds;
        WorkloadExitCode=$exit; Long=[bool]$Long; DiagnosticOnly=$true; Quality='Full detail, VSync, D3D11 debug off';
        Notes='CPU trace overhead is not an acceptance baseline. Raw ETL stays local; may include other process names.' } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'run.json')
}
if (-not (Test-Path -LiteralPath (Join-Path $folder 'finished')) -or (Test-Path -LiteralPath (Join-Path $folder 'error.txt'))) {
    throw "Recording incomplete; inspect $folder"
}
$report = Get-ChildItem -LiteralPath $qa -Directory |
    Where-Object { $_.CreationTimeUtc -ge $start -and $_.Name -match '^(texture-level-|gameplay-soak-)' } |
    Sort-Object CreationTimeUtc -Descending | Select-Object -First 1
if (-not $report) { throw 'No workload report found.' }
& $dotnet $probe (Join-Path $folder 'scheduling.etl') (Join-Path $report.FullName 'frames.json')
if ($LASTEXITCODE -ne 0) { throw "Trace analysis failed; raw evidence retained: $folder" }
Write-Host "[Scheduling] Reports: $folder"
