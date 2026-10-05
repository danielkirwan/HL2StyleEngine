@echo off
setlocal
cd /d "%~dp0"
call "%~dp0Tools\UseDotnet.bat"
if errorlevel 1 exit /b 1
"%HS2_DOTNET%" run --project Game\Game.csproj -- --level "%~dp0Game\Content\Levels\sixRoomTest.json"
if errorlevel 1 pause
