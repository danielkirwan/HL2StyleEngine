param(
    [ValidateSet('Package', 'SourceLighting', 'Source')]
    [string]$Mode = 'Package',
    [switch]$ColdCandidate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = [IO.Path]::GetFullPath((Join-Path $root 'Game/bin/Release/net10.0'))
$pointerPath = Join-Path $app 'loading-probe.json'
if (-not (Test-Path -LiteralPath $pointerPath)) { throw 'Run PrepareLoadingProbe.bat once before this probe.' }
if ((Get-Item -LiteralPath $pointerPath).Length -gt 65536) { throw 'Invalid probe metadata size.' }
$pointer = Get-Content -LiteralPath $pointerPath -Raw | ConvertFrom-Json
if ($pointer.Version -ne 1) { throw 'Unsupported probe metadata version.' }

function Resolve-ProbePath([string]$relative) {
    if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or
        $relative.Contains(':') -or ($relative -split '[/\\]' | Where-Object { $_ -eq '..' -or $_ -eq '.' })) {
        throw 'Probe paths must stay in the prepared app output.'
    }
    $path = [IO.Path]::GetFullPath((Join-Path $app $relative))
    $packages = (Join-Path $app 'LoadingTestPackages') + [IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($packages, [StringComparison]::OrdinalIgnoreCase)) { throw 'Probe path is not a prepared package.' }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Prepared file missing: $path" }
    return $path
}

$manifest = Resolve-ProbePath $pointer.Manifest
$level = Resolve-ProbePath $pointer.Level
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
$reports = Join-Path (Split-Path -Parent $manifest) 'probes'
$null = New-Item -ItemType Directory -Path $reports -Force
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
$report = Join-Path $reports ($stamp + '-' + $Mode + '.json')
$env:HS2_LOAD_CACHE_LABEL = if ($ColdCandidate) {
    'User-declared post-restart candidate; OS file cache not independently verified; no build/cook/copy by probe'
} else { 'Prepared probe; OS file cache uncontrolled; no build/cook/copy by probe' }
# A frame-profile environment from another test must not end this readiness probe early.
Remove-Item Env:HS2_PROFILE_FRAMES -ErrorAction SilentlyContinue
$arguments = @((Join-Path $app 'Game.dll'), '--level', $level, '--asset-package', $manifest, '--load-report', $report, '--load-probe')
if ($Mode -eq 'Source') { $arguments += '--source-assets' }
if ($Mode -eq 'SourceLighting') { $arguments += '--source-lighting' }
Write-Host "Loading probe: $Mode; report: $report"
Push-Location $app
try {
    & $dotnet @arguments
    $result = $LASTEXITCODE
} finally { Pop-Location }
exit $result
