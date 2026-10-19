@echo off
rem One-time setup of the save database (online phase 4). See setup-database.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-database.ps1"
