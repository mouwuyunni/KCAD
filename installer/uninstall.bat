@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0bundle-install.ps1" -Uninstall
set "KCAD_EXIT=%ERRORLEVEL%"
pause
exit /b %KCAD_EXIT%
