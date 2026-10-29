@echo off
setlocal
powershell.exe -NoLogo -NoProfile -File "%~dp0Tools\RunLoadingProbe.ps1" %*
exit /b %ERRORLEVEL%
