@echo off
setlocal
set "KCAD_PACKAGE=%~dp0"
powershell.exe -NoProfile -Command "$ErrorActionPreference='Stop'; if (!(Test-Path -LiteralPath (Join-Path $env:KCAD_PACKAGE 'RuiCAD.bundle\Contents\RuiCAD.UI.dll'))) { throw 'Extract the entire RuiCAD-CAD2020 zip first.' }; Get-ChildItem -LiteralPath (Join-Path $env:KCAD_PACKAGE 'RuiCAD.bundle') -Recurse -File | Unblock-File; Unblock-File -LiteralPath (Join-Path $env:KCAD_PACKAGE 'bundle-install.ps1')"
if errorlevel 1 goto failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0bundle-install.ps1"
if errorlevel 1 goto failed
pause
exit /b 0

:failed
echo KCAD installation failed. See the message above.
pause
exit /b 1
