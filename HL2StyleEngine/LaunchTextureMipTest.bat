@echo off
setlocal
cd /d "%~dp0"
call "%~dp0Tools\UseDotnet.bat"
if errorlevel 1 exit /b 1
"%HS2_DOTNET%" build Tools\LevelAuthoring\LevelAuthoring.csproj -c Release
if errorlevel 1 goto failed
"%HS2_DOTNET%" Tools\LevelAuthoring\bin\Release\net10.0\LevelAuthoring.dll launch-texture-mip-lab
if errorlevel 1 goto failed
exit /b 0
:failed
set "HS2_EXIT_CODE=%ERRORLEVEL%"
pause
exit /b %HS2_EXIT_CODE%
