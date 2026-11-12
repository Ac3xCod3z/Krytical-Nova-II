@echo off
rem Cutover step 9 (12 Nov 2026): runs install-release-shortcuts.ps1 in the owner's own profile (start through explorer.exe) and keeps what it said.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-release-shortcuts.ps1" > "%~dp0..\..\..\..\Builds\Release-Cutover\install-release-shortcuts.log" 2>&1
