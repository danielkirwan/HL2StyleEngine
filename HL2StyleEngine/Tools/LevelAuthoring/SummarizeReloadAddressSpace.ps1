param([Parameter(Mandatory=$true)][string]$ReportFolder)
$ErrorActionPreference = 'Stop'
$folder = (Resolve-Path -LiteralPath $ReportFolder).Path
$summary = Get-Content (Join-Path $folder 'summary.json') -Raw | ConvertFrom-Json
$files = @(Get-ChildItem -LiteralPath $folder -Filter 'address-*.json' | Sort-Object Name)
if ($files.Count -lt 2) { throw 'Need at least two address snapshots.' }
$baseline = Get-Content $files[0].FullName -Raw | ConvertFrom-Json
function CategorySum($snapshot, [string]$pattern) {
    [double](($snapshot.Categories.PSObject.Properties | Where-Object Name -Like $pattern | Measure-Object Value -Sum).Sum)
}
$rows = @($files | ForEach-Object {
    $snapshot = Get-Content $_.FullName -Raw | ConvertFrom-Json
    if (-not $snapshot.CanWalkHeap -or $snapshot.InvalidObjects -ne 0) { throw "Incomplete heap walk: $($_.Name)" }
    $privateSum = ($snapshot.Categories.PSObject.Properties | Where-Object Name -NotIn @('Mapped-commit','Image-commit','Other-commit') | Measure-Object Value -Sum).Sum
    if ($privateSum -ne $snapshot.PrivateCommit) { throw "Private category accounting mismatch: $($_.Name)" }
    $label = $_.BaseName.Substring('address-'.Length)
    $checkpoint = $summary.AddressCheckpoints | Where-Object Label -EQ $label | Select-Object -First 1
    $categoryNames = @($snapshot.Categories.PSObject.Properties.Name) + @($baseline.Categories.PSObject.Properties.Name) | Sort-Object -Unique
    [pscustomobject]@{
        Point = $label; File = $_.FullName
        SnapshotPrivateMiB = $snapshot.PrivateBytes / 1MB
        SnapshotPrivateGrowthMiB = ($snapshot.PrivateBytes - $baseline.PrivateBytes) / 1MB
        GcActiveGrowthMiB = ((CategorySum $snapshot 'GC-active-*') - (CategorySum $baseline 'GC-active-*')) / 1MB
        ClrNativeGrowthMiB = ((CategorySum $snapshot 'CLR-native-*') - (CategorySum $baseline 'CLR-native-*')) / 1MB
        ManagedStackGrowthMiB = ((CategorySum $snapshot 'Managed-thread-stack') - (CategorySum $baseline 'Managed-thread-stack')) / 1MB
        UnattributedPrivateGrowthMiB = ((CategorySum $snapshot 'Private-unattributed') - (CategorySum $baseline 'Private-unattributed')) / 1MB
        ResidualGrowthMiB = (($snapshot.PrivateBytes - $snapshot.PrivateCommit) - ($baseline.PrivateBytes - $baseline.PrivateCommit)) / 1MB
        GcLastCollectionIndex = $checkpoint.Index
        GcLastCollectionCommittedMiB = $checkpoint.TotalCommittedBytes / 1MB
        GpuLocalMiB = $checkpoint.Gpu.LocalUsage / 1MB
        GpuNonLocalMiB = $checkpoint.Gpu.NonLocalUsage / 1MB
        GpuAvailable = $checkpoint.Gpu.Available
        CategoryChanges = @($categoryNames | ForEach-Object {
            [pscustomobject]@{Category=$_; DeltaBytes=((CategorySum $snapshot $_) - (CategorySum $baseline $_))}
        })
    }
})
$report = [pscustomobject]@{
    DiagnosticOnly = $true; Source = $folder; Cycles = $summary.CompletedCycles; Actions = $summary.Actions
    OriginalGuardGrowthMiB = ($summary.LastSettledPrivateBytes - $summary.FirstSettledPrivateBytes) / 1MB
    OriginalFailure = $summary.Failure
    Rows = $rows
    Notes = 'Paused snapshots do not force collection. Post-run-collected is after timing, frame report and diagnostic collection. Original guard samples precede helper startup; do not substitute paused samples for acceptance. Private accounting residual is not measured VRAM. Mapped/image commit is separate and can contain private copy-on-write pages. No post-hoc removal of capture pauses from acceptance.'
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $folder 'address-comparison.json')
$rows | Select-Object Point,SnapshotPrivateGrowthMiB,GcActiveGrowthMiB,ClrNativeGrowthMiB,UnattributedPrivateGrowthMiB,ResidualGrowthMiB,GpuLocalMiB | Format-Table -AutoSize
