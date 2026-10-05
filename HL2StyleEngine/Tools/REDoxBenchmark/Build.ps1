param([ValidateSet('net8.0','net10.0')][string]$Framework = 'net10.0')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$work = Join-Path $PSScriptRoot '.work'
$projects = Join-Path $work "projects-$Framework"
$names = @('Engine.Core','Engine.Platform','Engine.Render','Engine.Physics','Engine.Input','Engine.Runtime','Engine.Editor','Engine.UI','Game','REDoxBenchmark')
foreach ($name in $names) {
    $source = if ($name -eq 'REDoxBenchmark') { $PSScriptRoot } else { Join-Path $root $name }
    $destination = Join-Path $projects $name
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    $doc = [System.Xml.Linq.XDocument]::Load((Join-Path $source "$name.csproj"))
    foreach ($tf in $doc.Descendants('TargetFramework')) { $tf.Value = $Framework }
    $props = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('PropertyGroup'))
    $props.Add([System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('EnableDefaultCompileItems'), 'false'))
    $doc.Root.Add($props)
    $group = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('ItemGroup'))
    $compile = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('Compile'))
    $compile.SetAttributeValue('Include', "$source/**/*.cs")
    $exclude = "$source/bin/**;$source/obj/**;$source/.work/**"
    if ($name -eq 'Engine.Editor') { $exclude += ";$source/Editor/LevelIO.cs" }
    if ($name -eq 'REDoxBenchmark') { $exclude += ";$source/Codec.cs;$source/ExperimentalLevelIO.cs" }
    $compile.SetAttributeValue('Exclude', $exclude)
    $group.Add($compile)
    if ($name -eq 'Engine.Editor') {
        foreach ($file in @('ExperimentalLevelIO.cs','Codec.cs')) {
            $item = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('Compile'))
            $item.SetAttributeValue('Include', (Join-Path $PSScriptRoot $file))
            $group.Add($item)
        }
    }
    foreach ($reference in $doc.Descendants('ProjectReference')) {
        $referencedName = [IO.Path]::GetFileNameWithoutExtension($reference.Attribute('Include').Value)
        $reference.SetAttributeValue('Include', (Join-Path $projects "$referencedName/$referencedName.csproj"))
    }
    foreach ($item in @($doc.Descendants('None'))) {
        $include = $item.Attribute('Include')
        if ($null -eq $include) { continue }
        $relative = $include.Value.Replace('\','/')
        $item.SetAttributeValue('Include', "$source/$relative")
        $prefix = if ($relative.Contains('**')) { $relative.Substring(0, $relative.IndexOf('**')) } else { '' }
        $item.SetElementValue('Link', $prefix + '%(RecursiveDir)%(Filename)%(Extension)')
    }
    if ($name -eq 'Engine.Editor' -and $Framework -eq 'net10.0') {
        $reference = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('ProjectReference'))
        $reference.SetAttributeValue('Include', (Join-Path $work 'REDox/src/REDox.Serialization.SystemTextJson/REDox.Serialization.SystemTextJson.csproj'))
        $group.Add($reference)
    }
    $doc.Root.Add($group)
    $doc.Save((Join-Path $destination "$name.csproj"))
}
$env:DOTNET_CLI_HOME = Join-Path $work 'cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget/packages'
$dotnet = if ($Framework -eq 'net10.0') { Join-Path $work 'dotnet/dotnet.exe' } else { 'dotnet' }
& $dotnet build (Join-Path $projects 'REDoxBenchmark/REDoxBenchmark.csproj') -c Release --artifacts-path (Join-Path $work "build-$Framework") -p:NuGetAudit=false -p:UseSharedCompilation=false -m:1 -v:minimal
if ($LASTEXITCODE -ne 0) { throw "Benchmark build failed: $LASTEXITCODE" }
