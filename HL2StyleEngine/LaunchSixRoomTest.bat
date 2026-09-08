@echo off
setlocal
cd /d "%~dp0"
dotnet run --project Game\Game.csproj -- --level "%~dp0Game\Content\Levels\sixRoomTest.json"
if errorlevel 1 pause
