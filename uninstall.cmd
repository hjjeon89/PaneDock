@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*
set "result=%errorlevel%"
echo.
if not "%result%"=="0" echo PaneDock uninstall failed. Read the error above.
pause
exit /b %result%
