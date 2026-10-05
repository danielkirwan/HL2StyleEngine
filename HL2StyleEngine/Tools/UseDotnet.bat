@echo off
set "HS2_DOTNET=dotnet"
if exist "%~dp0..\.dotnet\dotnet.exe" (
  set "HS2_DOTNET=%~dp0..\.dotnet\dotnet.exe"
  set "DOTNET_ROOT=%~dp0..\.dotnet"
  exit /b 0
)
dotnet --version >nul 2>&1
if not errorlevel 1 exit /b 0
echo This project requires a .NET 10 SDK. Install it or run Tools\SetupDotnet.ps1.
pause
exit /b 1
