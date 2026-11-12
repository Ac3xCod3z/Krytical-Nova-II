@echo off
rem Cutover publish (12 Nov 2026): start through explorer.exe, only on cutover-lead's "build go". Client > server > upload to the TEST channel.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-cutover.ps1"
echo.
echo Exit code %errorlevel%. Press a key to close.
pause > nul
