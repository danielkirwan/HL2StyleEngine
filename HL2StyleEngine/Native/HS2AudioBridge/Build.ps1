param([switch]$Fetch)
$ErrorActionPreference = 'Stop'
$revision = 'e82fd32c1f62183922f08c14c814a02b58db1873'
$native = Split-Path $PSScriptRoot
if ($Fetch -and !(Test-Path "$native/vendor/soloud-$revision/include/soloud.h")) {
    $zip = Join-Path $env:TEMP "hs2-soloud-$revision.zip"
    Invoke-WebRequest "https://codeload.github.com/jarikomppa/soloud/zip/$revision" -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath "$native/vendor" -Force
}
$cmake = 'C:/Program Files/Microsoft Visual Studio/2022/Community/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
& $cmake -S $PSScriptRoot -B "$PSScriptRoot/obj" -A x64
if ($LASTEXITCODE) { throw 'Audio configuration failed' }
& $cmake --build "$PSScriptRoot/obj" --config Release
if ($LASTEXITCODE) { throw 'Audio build failed' }
Copy-Item -LiteralPath "$PSScriptRoot/obj/Release/HS2AudioBridge.dll" -Destination "$native/runtimes/win-x64/HS2AudioBridge.dll"
