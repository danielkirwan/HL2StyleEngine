@echo off
setlocal
cd /d "%~dp0"
call Tools\UseDotnet.bat
if errorlevel 1 exit /b %errorlevel%
"%HS2_DOTNET%" build Tools\LevelAuthoring\LevelAuthoring.csproj -c Release
if errorlevel 1 exit /b %errorlevel%
"%HS2_DOTNET%" Tools\LevelAuthoring\bin\Release\net10.0\LevelAuthoring.dll cook-assets %*
if errorlevel 1 exit /b %errorlevel%
echo Compression complete. Launch the game or editor normally.
pause
