@echo off
setlocal
cd /d "%~dp0"
call "%~dp0Tools\UseDotnet.bat"
if errorlevel 1 exit /b 1
"%HS2_DOTNET%" run --project HS2Editor\HS2Editor.csproj
