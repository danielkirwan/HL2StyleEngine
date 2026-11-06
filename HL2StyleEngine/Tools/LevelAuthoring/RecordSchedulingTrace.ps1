param([Parameter(Mandatory)][string]$Folder, [Parameter(Mandatory)][string]$Instance)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$started = $false
try {
    $profile = (Join-Path $PSScriptRoot 'SchedulingTrace.wprp') + '!HS2Scheduling'
    & wpr -start $profile -filemode -recordtempto $Folder -instancename $Instance 2>&1 |
        Out-File -LiteralPath (Join-Path $Folder 'wpr-start.log')
    if ($LASTEXITCODE -ne 0) { throw "WPR start failed: $LASTEXITCODE" }
    $started = $true
    New-Item -ItemType File -Path (Join-Path $Folder 'ready') | Out-Null
    $deadline = [DateTime]::UtcNow.AddMinutes(9)
    while (-not (Test-Path -LiteralPath (Join-Path $Folder 'stop')) -and [DateTime]::UtcNow -lt $deadline) {
        $bytes = (Get-ChildItem -LiteralPath $Folder -Filter '*.etl' -File -Recurse | Measure-Object Length -Sum).Sum
        if ($bytes -gt 2GB) {
            'Trace stopped at 2 GiB guard; inspect frame coverage before drawing conclusions.' | Set-Content -LiteralPath (Join-Path $Folder 'bounded-stop.txt')
            break
        }
        Start-Sleep -Seconds 2
    }
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $Folder 'error.txt')
} finally {
    if ($started) {
        & wpr -stop (Join-Path $Folder 'scheduling.etl') -skipPdbGen -instancename $Instance 2>&1 |
            Out-File -LiteralPath (Join-Path $Folder 'wpr-stop.log')
        if ($LASTEXITCODE -ne 0) { "WPR stop failed: $LASTEXITCODE; instance=$Instance" | Set-Content -LiteralPath (Join-Path $Folder 'error.txt') }
    }
    New-Item -ItemType File -Path (Join-Path $Folder 'finished') -Force | Out-Null
}
