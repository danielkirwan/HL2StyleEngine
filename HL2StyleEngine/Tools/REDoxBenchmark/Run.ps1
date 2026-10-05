param([ValidateRange(1,10)][int]$StartupRepeats = 5)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$results = Join-Path $PSScriptRoot 'results'
[IO.Directory]::CreateDirectory($results) | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$variants = @(
    @{ Framework = 'net8.0'; Codec = 'stj'; Host = 'dotnet'; Label = 'net8-stj' },
    @{ Framework = 'net10.0'; Codec = 'stj'; Host = (Join-Path $PSScriptRoot '.work/dotnet/dotnet.exe'); Label = 'net10-stj' },
    @{ Framework = 'net10.0'; Codec = 'redox-json'; Host = (Join-Path $PSScriptRoot '.work/dotnet/dotnet.exe'); Label = 'net10-redox-json' },
    @{ Framework = 'net10.0'; Codec = 'redox-dox'; Host = (Join-Path $PSScriptRoot '.work/dotnet/dotnet.exe'); Label = 'net10-redox-dox' }
)
foreach ($variant in $variants[0..1]) {
    $dll = Join-Path $PSScriptRoot ".work/build-$($variant.Framework)/bin/REDoxBenchmark/release/REDoxBenchmark.dll"
    $label = $variant.Framework.Replace('.0','')
    & $variant.Host $dll --micro $root (Join-Path $results "micro-$label.json")
    if ($LASTEXITCODE -ne 0) { throw "Microbenchmark failed: $label" }
}
# Rotate the order each round; only one game process runs at a time.
for ($round = 1; $round -le $StartupRepeats; $round++) {
    for ($index = 0; $index -lt $variants.Count; $index++) {
        $variant = $variants[($index + $round - 1) % $variants.Count]
        $dll = Join-Path $PSScriptRoot ".work/build-$($variant.Framework)/bin/REDoxBenchmark/release/REDoxBenchmark.dll"
        $output = Join-Path $results "boot-$($variant.Label)-$round.json"
        Write-Host "Startup round $round : $($variant.Label)"
        & $variant.Host $dll --boot $root $output $variant.Codec
        if ($LASTEXITCODE -ne 0) { throw "Startup failed: $($variant.Label)" }
    }
}
