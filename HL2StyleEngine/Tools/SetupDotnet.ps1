$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $root ".dotnet"
$archive = Join-Path $env:TEMP ("hs2-dotnet-" + [guid]::NewGuid().ToString("N") + ".zip")
try {
    Invoke-WebRequest "https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.zip" -OutFile $archive
    $expected = "24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6ee437ab874bb7c79430"
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $expected) {
        throw "The .NET SDK checksum did not match. No files were installed."
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
    & (Join-Path $destination "dotnet.exe") --version
} finally {
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
}
