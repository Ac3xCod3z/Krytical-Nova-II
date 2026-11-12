@echo off
rem Cutover publish, RESUME from the server step (12 Nov 2026): only after the client step is proven passed in Builds\cutover-client.log.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-cutover.ps1" -From Server
echo.
echo Exit code %errorlevel%. Press a key to close.
pause > nul
