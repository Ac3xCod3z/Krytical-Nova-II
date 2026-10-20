@echo off
rem One-time: the practice databases of the restore drill (online phase 4, milestone 6). See make-drill-database.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make-drill-database.ps1"
