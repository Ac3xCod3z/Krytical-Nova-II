@echo off
rem Cutover step 9 (12 Nov 2026), by hand ONCE: the IL2CPP entity server could not start ugs.exe, so its own server-list
rem upload failed. This runs exactly the command the server logged, in the owner's profile (start through explorer.exe).
set OUT=C:\Users\dlaco\Krytical Nova II DOTS\Builds\Release-Cutover\server-list-upload.log
"C:\Users\dlaco\AppData\Local\KryticalNova\tools\ugs.exe" ccd entries sync "C:\Users\dlaco\Krytical Nova II DOTS\Builds\Server\Windows\KryticalNovaServer\ServerList" -b krytical-nova-servers -x -r -n "server list" -e production -p f2ed0dbf-063b-4143-9e21-0f4bb3b3aa81 > "%OUT%" 2>&1
echo exit %errorlevel% >> "%OUT%"
