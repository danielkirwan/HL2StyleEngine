@echo off
setlocal
cd /d "%~dp0"
call "%~dp0Tools\UseDotnet.bat"
if errorlevel 1 exit /b 1
"%HS2_DOTNET%" build Tools\LevelAuthoring\LevelAuthoring.csproj -c Release
if errorlevel 1 goto failed
set "HS2_CHECKS=Tools\LevelAuthoring\bin\Release\net10.0\LevelAuthoring.dll"
"%HS2_DOTNET%" "%HS2_CHECKS%" validate-physics-geometry
if errorlevel 1 goto failed
"%HS2_DOTNET%" "%HS2_CHECKS%" validate-collider-bounds
if errorlevel 1 goto failed
"%HS2_DOTNET%" "%HS2_CHECKS%" validate-gameplay-stability
if errorlevel 1 goto failed
"%HS2_DOTNET%" "%HS2_CHECKS%" soak-gameplay --native-ui
if errorlevel 1 goto failed
"%HS2_DOTNET%" "%HS2_CHECKS%" soak-gameplay
if errorlevel 1 goto failed
"%HS2_DOTNET%" "%HS2_CHECKS%" soak-gameplay --native-ui --puzzles
if errorlevel 1 goto failed
echo Reports: Tools\LevelAuthoring\bin\Release\net10.0\qa
pause
exit /b 0
:failed
set "HS2_EXIT_CODE=%ERRORLEVEL%"
echo A check failed. The QA folder retains the diagnostic report.
pause
exit /b %HS2_EXIT_CODE%
