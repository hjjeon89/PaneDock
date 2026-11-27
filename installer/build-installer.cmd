@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1"
set "result=%errorlevel%"
if not "%result%"=="0" echo PaneDock installer build failed.
if not "%~1"=="--no-pause" pause
exit /b %result%
