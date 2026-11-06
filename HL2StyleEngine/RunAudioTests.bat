@echo off
setlocal
cd /d "%~dp0"
call "%~dp0Tools\UseDotnet.bat"
if errorlevel 1 exit /b 1
"%HS2_DOTNET%" build Tools\LevelAuthoring\LevelAuthoring.csproj -c Release
if errorlevel 1 exit /b 1
"%HS2_DOTNET%" Tools\LevelAuthoring\bin\Release\net10.0\LevelAuthoring.dll check-audio --device
exit /b %ERRORLEVEL%
